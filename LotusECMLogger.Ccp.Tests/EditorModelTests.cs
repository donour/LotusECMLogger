using System.Text;
using LotusECMLogger.Services;
using LotusECMLogger.Services.A2l;
using LotusECMLogger.Services.Ccp;
using LotusECMLogger.Services.Tuning;
using LotusECMLogger.Services.Tuning.Plotting;

namespace LotusECMLogger.Ccp.Tests;

/// <summary>The tuning editor's models: the calibration tree, graph data, and step sizes.</summary>
public sealed class EditorModelTests
{
    private const string Table = "CAL_injtip_transient_throttle_response_time"; // 8 × 8, X coolant, Y rpm
    private static readonly CcpBuildProfile Gt430 = CcpBuildProfile.Known.Single(p => p.Build == "C132E0278");
    private static readonly Lazy<A2lResolvedModule> A2l = new(() => A2lLayout.Resolve(A2lParser.Parse(
        A2lGenerator.Generate(SymbolCatalogLoader.LoadByName(Gt430.Catalog)!, Gt430).Text)));

    private static CalibrationImage Image()
    {
        var bytes = new byte[Gt430.CalibrationSize];
        Encoding.ASCII.GetBytes("C132E0278 01-01-2020 00:00").CopyTo(bytes, 0);
        return CalibrationImage.FromBytes(bytes);
    }

    [Fact]
    public void GeneratedGroupsBecomeATreeThatHoldsEveryItemOnce()
    {
        var tree = CalibrationTree.Build(A2l.Value);
        Assert.Equal("Fuel and injection", tree.Children[0].Title);
        Assert.DoesNotContain(tree.Children, c => c.Title == "Ungrouped");
        var fuel = tree.Children[0];
        var injtip = fuel.Children.Single(c => c.Title == "injtip");
        Assert.Contains(injtip.Children, c => c.Item?.Name == Table && c.Title == Table[4..]);
        var all = tree.Items().Select(i => i.Name).ToList();
        Assert.Equal(A2l.Value.Items.Count, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        // One prefix only: the subsystem lists its items directly.
        Assert.All(tree.Children.Single(c => c.Title == "Idle").Children, c => Assert.False(c.IsFolder));
    }

    [Fact]
    public void WithoutGroupsTheTreeFollowsTheNameRules()
    {
        var module = A2l.Value.Module;
        var stripped = new A2lResolvedModule(new A2lModule
        {
            Name = module.Name, CompuMethods = module.CompuMethods, RecordLayouts = module.RecordLayouts,
        }, A2l.Value.Items, []);
        var tree = CalibrationTree.Build(stripped);
        Assert.Equal(CalibrationTree.Build(A2l.Value).Children.Select(c => c.Title), tree.Children.Select(c => c.Title));
        Assert.Equal(A2l.Value.Items.Count, tree.Items().Count());
    }

    [Fact]
    public void GroupsReferencingUnknownItemsOrCyclesAreTolerated()
    {
        var module = A2l.Value.Module;
        var odd = new A2lResolvedModule(new A2lModule
        {
            Name = "M",
            Groups =
            [
                new A2lGroup("A", "Loop", true, [Table, "NOT_AN_ITEM"], ["B"]),
                new A2lGroup("B", "", false, [], ["A"]),
            ],
        }, A2l.Value.Items, []);
        var tree = CalibrationTree.Build(odd);
        Assert.Equal(["Loop", "Ungrouped"], tree.Children.Select(c => c.Title));
        Assert.Equal("B", tree.Children[0].Children[0].Title);
        Assert.Equal(A2l.Value.Items.Count, tree.Items().Count());
    }

    [Fact]
    public void GraphDataReadsValuesAxesAndChanges()
    {
        var image = Image();
        var map = A2l.Value.Items[Table];
        var x = A2l.Value.Items[Table + "_X_coolant_tmp"];
        image.SetRaw(x, Enumerable.Range(0, 8).Select(i => new CellValue(i, 0, 64 + 16 * i)).ToList());
        Assert.Null(GraphData.From(image, map)!.Originals);
        image.SetRaw(map, [new CellValue(2, 3, 10)]);

        var data = GraphData.From(image, map)!;
        Assert.True(data.IsMap);
        Assert.Equal((8, 8), (data.SizeX, data.SizeY));
        Assert.Equal(50, data.Values[2, 3]);       // [x, y], 5 ms per count
        Assert.Equal(10, data.Raw[2, 3]);
        Assert.Equal(0, data.Originals![2, 3]);
        Assert.Equal("ms", data.ValueTitle);
        Assert.False(data.X.Categorical);
        Assert.Equal(0, data.X.Positions[0], 9);   // 64 × 5/8 − 40
        Assert.Equal("coolant_tmp (°C)", data.X.Title);
        Assert.True(data.Y!.Categorical);           // the rpm axis is all zero here, so it is plotted by index
        Assert.Equal(50, data.Transposed().Values[3, 2]);
        Assert.Null(GraphData.From(image, A2l.Value.Items["CAL_base"]));
    }

    [Fact]
    public void CurvesAndBlocksGraphAgainstTheirAxisOrIndex()
    {
        var image = Image();
        var curve = A2l.Value.Items.Values.First(i => i.Kind == A2lItemKind.Curve);
        var data = GraphData.From(image, curve)!;
        Assert.False(data.IsMap);
        Assert.Equal((curve.CountX, 1), (data.SizeX, data.SizeY));
        var block = A2l.Value.Items.Values.First(i => i.Kind == A2lItemKind.ValBlk && i.ElementCount > 1);
        Assert.Equal("index", GraphData.From(image, block)!.X.Title);
    }

    [Fact]
    public void FineStepIsOneRawCount()
    {
        Assert.Equal(5, TableEdits.RawStep(A2l.Value.Items[Table].Conversion), 9);
        Assert.Equal(0.625, TableEdits.RawStep(A2l.Value.Items[Table + "_X_coolant_tmp"].Conversion), 9);
        Assert.Equal(1, TableEdits.RawStep(A2lCompuMethod.Identity));
    }

    [Fact]
    public void InterpolatesRowsAndColumnsSeparately()
    {
        var values = new double[3, 3] { { 0, 5, 30 }, { 0, 5, 60 }, { 0, 5, 90 } }; // [y, x]
        var rows = TableEdits.InterpolateRows(values, new CellRange(0, 0, 2, 2));
        Assert.Equal(15, rows.Single(c => (c.X, c.Y) == (1, 0)).Value, 9);
        Assert.Equal(45, rows.Single(c => (c.X, c.Y) == (1, 2)).Value, 9);
        var columns = TableEdits.InterpolateColumns(new double[3, 1] { { 10 }, { 99 }, { 30 } }, new CellRange(0, 0, 0, 2));
        Assert.Equal(20, columns.Single(c => c.Y == 1).Value, 9);
        Assert.Throws<ArgumentException>(() => TableEdits.InterpolateColumns(values, new CellRange(0, 0, 2, 0)));
    }
}
