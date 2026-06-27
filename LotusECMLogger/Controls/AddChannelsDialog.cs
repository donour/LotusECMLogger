using System.Globalization;
using LotusECMLogger.Models;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    /// <summary>
    /// Modal picker for selecting loggable channels from an ECU's symbol catalog. Users choose an ECU,
    /// search/filter the labels, multi-select, pick a rate, and Add — yielding <see cref="SelectedChannels"/>
    /// with size/scale/unit auto-derived from each symbol's type.
    /// </summary>
    public partial class AddChannelsDialog : Form
    {
        private SymbolCatalog? _catalog;
        private List<SymbolEntry> _filtered = [];

        /// <summary>Channels the user chose (valid after the dialog returns <see cref="DialogResult.OK"/>).</summary>
        public List<HighSpeedChannel> SelectedChannels { get; private set; } = [];

        public AddChannelsDialog(string? preferredEcu = null)
        {
            InitializeComponent();

            foreach (var rate in HighSpeedLogPlanner.SupportedRatesHz)
                rateComboBox.Items.Add(rate);
            int defaultIdx = Array.IndexOf(HighSpeedLogPlanner.SupportedRatesHz, 10);
            rateComboBox.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;

            var catalogs = SymbolCatalogLoader.GetAvailableCatalogs();
            foreach (var c in catalogs)
                ecuComboBox.Items.Add(c);

            if (catalogs.Count == 0)
            {
                countLabel.Text = "No symbol catalogs found in config/highSpeedLogger/database";
                addButton.Enabled = false;
                searchTextBox.Enabled = false;
                return;
            }

            var pick = preferredEcu != null && catalogs.Contains(preferredEcu) ? preferredEcu : catalogs[0];
            ecuComboBox.SelectedItem = pick; // triggers catalog load + initial filter
        }

        private void EcuComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _catalog = ecuComboBox.SelectedItem is string name ? SymbolCatalogLoader.LoadByName(name) : null;
            ApplyFilter();
        }

        private void SearchTextBox_TextChanged(object? sender, EventArgs e) => ApplyFilter();

        private void Filter_Changed(object? sender, EventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            // Clear selection first: shrinking VirtualListSize with stale selected indices can throw.
            resultsListView.SelectedIndices.Clear();

            if (_catalog == null)
            {
                _filtered = [];
                resultsListView.VirtualListSize = 0;
                countLabel.Text = "no catalog";
                return;
            }

            var filter = new SymbolFilter
            {
                Query = searchTextBox.Text,
                HideArrays = hideArraysCheckBox.Checked,
                HideCalibration = hideCalCheckBox.Checked,
            };

            _filtered = _catalog.Search(filter).ToList();
            resultsListView.VirtualListSize = _filtered.Count;
            resultsListView.Invalidate();
            countLabel.Text = $"{_filtered.Count} match(es)";
        }

        private void ResultsListView_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= _filtered.Count)
            {
                e.Item = new ListViewItem();
                return;
            }

            var entry = _filtered[e.ItemIndex];
            var t = entry.Type;
            var item = new ListViewItem(entry.Name);
            item.SubItems.Add($"0x{entry.Address:X8}");
            item.SubItems.Add(t.Size.ToString());
            item.SubItems.Add(t.Unit);
            item.SubItems.Add(FormatTransform(t));
            item.SubItems.Add(t.Category);
            e.Item = item;
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            int rate = rateComboBox.SelectedItem is int r ? r : 10;

            var picks = new List<HighSpeedChannel>();
            foreach (int idx in resultsListView.SelectedIndices)
                if (idx >= 0 && idx < _filtered.Count)
                    picks.Add(ToChannel(_filtered[idx], rate));

            if (picks.Count == 0)
            {
                MessageBox.Show("Select one or more channels (Ctrl/Shift-click), then Add.",
                    "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectedChannels = picks;
            DialogResult = DialogResult.OK;
        }

        private static HighSpeedChannel ToChannel(SymbolEntry e, int rate) => new()
        {
            Name = e.Name,
            Address = e.Address,
            Size = (byte)(e.Type.Size == 0 ? 1 : e.Type.Size),
            Signed = e.Type.Signed,
            Scale = e.Type.Scale,
            Offset = e.Type.Offset,
            Unit = e.Type.Unit,
            DefaultRate = rate,
            DefaultSelected = true,
            SourceSymbol = e.Name,
            Category = e.Type.Category,
        };

        private static string FormatTransform(ParsedChannelType t)
        {
            if (t.Scale == 1.0 && t.Offset == 0.0)
                return "";
            var parts = new List<string>(2);
            if (t.Scale != 1.0)
                parts.Add("×" + t.Scale.ToString("G6", CultureInfo.InvariantCulture));
            if (t.Offset != 0.0)
                parts.Add(t.Offset.ToString("+0.###;-0.###", CultureInfo.InvariantCulture));
            return string.Join(" ", parts);
        }
    }
}
