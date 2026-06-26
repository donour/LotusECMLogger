namespace LotusECMLogger.Controls
{
    partial class AddChannelsDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                components?.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            rootLayout = new TableLayoutPanel();
            topPanel = new FlowLayoutPanel();
            ecuLabel = new Label();
            ecuComboBox = new ComboBox();
            searchLabel = new Label();
            searchTextBox = new TextBox();
            hideArraysCheckBox = new CheckBox();
            hideCalCheckBox = new CheckBox();
            countLabel = new Label();
            resultsListView = new ListView();
            bottomPanel = new FlowLayoutPanel();
            rateLabel = new Label();
            rateComboBox = new ComboBox();
            addButton = new Button();
            cancelButton = new Button();
            rootLayout.SuspendLayout();
            topPanel.SuspendLayout();
            bottomPanel.SuspendLayout();
            SuspendLayout();
            //
            // rootLayout
            //
            rootLayout.ColumnCount = 1;
            rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            rootLayout.Controls.Add(topPanel, 0, 0);
            rootLayout.Controls.Add(resultsListView, 0, 1);
            rootLayout.Controls.Add(bottomPanel, 0, 2);
            rootLayout.Dock = DockStyle.Fill;
            rootLayout.Name = "rootLayout";
            rootLayout.RowCount = 3;
            rootLayout.RowStyles.Add(new RowStyle());
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootLayout.RowStyles.Add(new RowStyle());
            //
            // topPanel
            //
            topPanel.AutoSize = true;
            topPanel.Controls.Add(ecuLabel);
            topPanel.Controls.Add(ecuComboBox);
            topPanel.Controls.Add(searchLabel);
            topPanel.Controls.Add(searchTextBox);
            topPanel.Controls.Add(hideArraysCheckBox);
            topPanel.Controls.Add(hideCalCheckBox);
            topPanel.Controls.Add(countLabel);
            topPanel.Dock = DockStyle.Fill;
            topPanel.Name = "topPanel";
            topPanel.WrapContents = true;
            //
            // ecuLabel
            //
            ecuLabel.Anchor = AnchorStyles.Left;
            ecuLabel.AutoSize = true;
            ecuLabel.Text = "ECU:";
            //
            // ecuComboBox
            //
            ecuComboBox.Anchor = AnchorStyles.Left;
            ecuComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            ecuComboBox.Name = "ecuComboBox";
            ecuComboBox.Width = 170;
            ecuComboBox.SelectedIndexChanged += EcuComboBox_SelectedIndexChanged;
            //
            // searchLabel
            //
            searchLabel.Anchor = AnchorStyles.Left;
            searchLabel.AutoSize = true;
            searchLabel.Text = "Search:";
            //
            // searchTextBox
            //
            searchTextBox.Anchor = AnchorStyles.Left;
            searchTextBox.Name = "searchTextBox";
            searchTextBox.Width = 240;
            searchTextBox.TextChanged += SearchTextBox_TextChanged;
            //
            // hideArraysCheckBox
            //
            hideArraysCheckBox.Anchor = AnchorStyles.Left;
            hideArraysCheckBox.AutoSize = true;
            hideArraysCheckBox.Checked = true;
            hideArraysCheckBox.Name = "hideArraysCheckBox";
            hideArraysCheckBox.Text = "Hide arrays";
            hideArraysCheckBox.CheckedChanged += Filter_Changed;
            //
            // hideCalCheckBox
            //
            hideCalCheckBox.Anchor = AnchorStyles.Left;
            hideCalCheckBox.AutoSize = true;
            hideCalCheckBox.Checked = true;
            hideCalCheckBox.Name = "hideCalCheckBox";
            hideCalCheckBox.Text = "Hide CAL_/LEA_";
            hideCalCheckBox.CheckedChanged += Filter_Changed;
            //
            // countLabel
            //
            countLabel.Anchor = AnchorStyles.Left;
            countLabel.AutoSize = true;
            countLabel.Text = "";
            //
            // resultsListView
            //
            resultsListView.Dock = DockStyle.Fill;
            resultsListView.FullRowSelect = true;
            resultsListView.HideSelection = false;
            resultsListView.MultiSelect = true;
            resultsListView.Name = "resultsListView";
            resultsListView.UseCompatibleStateImageBehavior = false;
            resultsListView.View = View.Details;
            resultsListView.VirtualMode = true;
            resultsListView.RetrieveVirtualItem += ResultsListView_RetrieveVirtualItem;
            resultsListView.Columns.Add("Name", 240);
            resultsListView.Columns.Add("Address", 90);
            resultsListView.Columns.Add("Size", 50);
            resultsListView.Columns.Add("Unit", 70);
            resultsListView.Columns.Add("Transform", 120);
            resultsListView.Columns.Add("Category", 110);
            //
            // bottomPanel
            //
            bottomPanel.AutoSize = true;
            bottomPanel.Controls.Add(rateLabel);
            bottomPanel.Controls.Add(rateComboBox);
            bottomPanel.Controls.Add(addButton);
            bottomPanel.Controls.Add(cancelButton);
            bottomPanel.Dock = DockStyle.Fill;
            bottomPanel.Name = "bottomPanel";
            bottomPanel.WrapContents = false;
            //
            // rateLabel
            //
            rateLabel.Anchor = AnchorStyles.Left;
            rateLabel.AutoSize = true;
            rateLabel.Text = "Rate (Hz):";
            //
            // rateComboBox
            //
            rateComboBox.Anchor = AnchorStyles.Left;
            rateComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            rateComboBox.Name = "rateComboBox";
            rateComboBox.Width = 80;
            //
            // addButton
            //
            addButton.AutoSize = true;
            addButton.Name = "addButton";
            addButton.Text = "Add Selected";
            addButton.UseVisualStyleBackColor = true;
            addButton.Click += AddButton_Click;
            //
            // cancelButton
            //
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // AddChannelsDialog
            //
            AcceptButton = addButton;
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new Size(820, 560);
            Controls.Add(rootLayout);
            MinimizeBox = false;
            MinimumSize = new Size(700, 400);
            Name = "AddChannelsDialog";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Add Channels";
            rootLayout.ResumeLayout(false);
            rootLayout.PerformLayout();
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            bottomPanel.ResumeLayout(false);
            bottomPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel rootLayout;
        private FlowLayoutPanel topPanel;
        private Label ecuLabel;
        private ComboBox ecuComboBox;
        private Label searchLabel;
        private TextBox searchTextBox;
        private CheckBox hideArraysCheckBox;
        private CheckBox hideCalCheckBox;
        private Label countLabel;
        private ListView resultsListView;
        private FlowLayoutPanel bottomPanel;
        private Label rateLabel;
        private ComboBox rateComboBox;
        private Button addButton;
        private Button cancelButton;
    }
}
