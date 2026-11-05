using System.Runtime.ExceptionServices;

namespace LotusECMLogger.Tests;

/// <summary>The user guide: every topic in the navigation has its own page.</summary>
public sealed class HelpDialogTests
{
    [Fact]
    public void EveryTopicShowsItsOwnPage() => OnStaThread(() =>
    {
        using var dialog = new HelpDialog();
        var tree = Descendants(dialog).OfType<TreeView>().Single();
        var content = Descendants(dialog).OfType<RichTextBox>().Single();
        // Selection events need window handles, which a dialog that is never shown does not create by itself.
        _ = dialog.Handle;
        _ = tree.Handle;
        _ = content.Handle;
        var topics = All(tree.Nodes).ToList();
        Assert.Contains(topics, n => n.Name == "ccprecording");

        string overview = "LotusECMLogger - Overview";
        foreach (var topic in topics)
        {
            tree.SelectedNode = null;
            tree.SelectedNode = topic;
            string first = content.Lines.FirstOrDefault() ?? "";
            Assert.False(string.IsNullOrWhiteSpace(first), $"{topic.Text} shows nothing.");
            // A topic with no page of its own falls back to the overview; only the Features category means to.
            if (topic.Name is not ("overview" or "features"))
                Assert.True(first != overview, $"{topic.Name} ({topic.Text}) shows the overview.");
        }

        if (Environment.GetEnvironmentVariable("CCP_UI_RENDER_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            // Children draw only once the dialog is shown: show it off-screen.
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-4000, -4000);
            dialog.ShowInTaskbar = false;
            dialog.Size = new Size(1000, 760);
            dialog.Show();
            foreach (var name in new[] { "ccp", "ccptuning", "ccprecording" })
            {
                tree.SelectedNode = topics.Single(n => n.Name == name);
                Application.DoEvents();
                using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                bitmap.Save(Path.Combine(dir, $"help_{name}.png"));
            }
        }
    });

    private static IEnumerable<TreeNode> All(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in All(node.Nodes)) yield return child;
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void OnStaThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
