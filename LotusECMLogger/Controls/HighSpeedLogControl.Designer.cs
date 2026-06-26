namespace LotusECMLogger.Controls
{
    partial class HighSpeedLogControl
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Hook for the code-behind to release non-designer resources (the service).</summary>
        partial void DisposeManaged();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeManaged();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            layoutPanel = new TableLayoutPanel();
            configGroupBox = new GroupBox();
            configLayout = new TableLayoutPanel();
            presetLabel = new Label();
            presetPanel = new FlowLayoutPanel();
            presetComboBox = new ComboBox();
            addChannelsButton = new Button();
            csvPathLabel = new Label();
            csvPanel = new FlowLayoutPanel();
            csvPathTextBox = new TextBox();
            browseCsvButton = new Button();
            buttonPanel = new FlowLayoutPanel();
            testConnectionButton = new Button();
            startButton = new Button();
            stopButton = new Button();
            gridGroupBox = new GroupBox();
            channelsGrid = new DataGridView();
            selectColumn = new DataGridViewCheckBoxColumn();
            nameColumn = new DataGridViewTextBoxColumn();
            addressColumn = new DataGridViewTextBoxColumn();
            unitColumn = new DataGridViewTextBoxColumn();
            rateColumn = new DataGridViewComboBoxColumn();
            valueColumn = new DataGridViewTextBoxColumn();
            statusGroupBox = new GroupBox();
            statusLayout = new TableLayoutPanel();
            statusLabel = new Label();
            statusValueLabel = new Label();
            framesLabel = new Label();
            framesValueLabel = new Label();
            lastUpdateLabel = new Label();
            lastUpdateValueLabel = new Label();
            layoutPanel.SuspendLayout();
            configGroupBox.SuspendLayout();
            configLayout.SuspendLayout();
            presetPanel.SuspendLayout();
            csvPanel.SuspendLayout();
            buttonPanel.SuspendLayout();
            gridGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)channelsGrid).BeginInit();
            statusGroupBox.SuspendLayout();
            statusLayout.SuspendLayout();
            SuspendLayout();
            //
            // layoutPanel
            //
            layoutPanel.ColumnCount = 1;
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.Controls.Add(configGroupBox, 0, 0);
            layoutPanel.Controls.Add(gridGroupBox, 0, 1);
            layoutPanel.Controls.Add(statusGroupBox, 0, 2);
            layoutPanel.Dock = DockStyle.Fill;
            layoutPanel.Location = new Point(0, 0);
            layoutPanel.Margin = new Padding(4, 5, 4, 5);
            layoutPanel.Name = "layoutPanel";
            layoutPanel.RowCount = 3;
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.Size = new Size(1000, 600);
            layoutPanel.TabIndex = 0;
            //
            // configGroupBox
            //
            configGroupBox.AutoSize = true;
            configGroupBox.Controls.Add(configLayout);
            configGroupBox.Dock = DockStyle.Fill;
            configGroupBox.Margin = new Padding(4, 5, 4, 5);
            configGroupBox.Name = "configGroupBox";
            configGroupBox.Padding = new Padding(4, 5, 4, 5);
            configGroupBox.TabIndex = 0;
            configGroupBox.TabStop = false;
            configGroupBox.Text = "Configuration";
            //
            // configLayout
            //
            configLayout.AutoSize = true;
            configLayout.ColumnCount = 2;
            configLayout.ColumnStyles.Add(new ColumnStyle());
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            configLayout.Controls.Add(presetLabel, 0, 0);
            configLayout.Controls.Add(presetPanel, 1, 0);
            configLayout.Controls.Add(csvPathLabel, 0, 1);
            configLayout.Controls.Add(csvPanel, 1, 1);
            configLayout.Controls.Add(buttonPanel, 1, 2);
            configLayout.Dock = DockStyle.Fill;
            configLayout.Name = "configLayout";
            configLayout.RowCount = 3;
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.TabIndex = 0;
            //
            // presetLabel
            //
            presetLabel.Anchor = AnchorStyles.Left;
            presetLabel.AutoSize = true;
            presetLabel.Name = "presetLabel";
            presetLabel.Text = "Preset:";
            //
            // presetPanel
            //
            presetPanel.AutoSize = true;
            presetPanel.Controls.Add(presetComboBox);
            presetPanel.Controls.Add(addChannelsButton);
            presetPanel.Margin = new Padding(0);
            presetPanel.Name = "presetPanel";
            presetPanel.WrapContents = false;
            //
            // presetComboBox
            //
            presetComboBox.Anchor = AnchorStyles.Left;
            presetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetComboBox.Name = "presetComboBox";
            presetComboBox.Size = new Size(300, 33);
            presetComboBox.TabIndex = 0;
            presetComboBox.SelectedIndexChanged += PresetComboBox_SelectedIndexChanged;
            //
            // addChannelsButton
            //
            addChannelsButton.AutoSize = true;
            addChannelsButton.Name = "addChannelsButton";
            addChannelsButton.Text = "Add Channels…";
            addChannelsButton.TabIndex = 1;
            addChannelsButton.UseVisualStyleBackColor = true;
            addChannelsButton.Click += AddChannelsButton_Click;
            //
            // csvPathLabel
            //
            csvPathLabel.Anchor = AnchorStyles.Left;
            csvPathLabel.AutoSize = true;
            csvPathLabel.Name = "csvPathLabel";
            csvPathLabel.Text = "CSV File:";
            //
            // csvPanel
            //
            csvPanel.AutoSize = true;
            csvPanel.Controls.Add(csvPathTextBox);
            csvPanel.Controls.Add(browseCsvButton);
            csvPanel.Margin = new Padding(0);
            csvPanel.Name = "csvPanel";
            csvPanel.WrapContents = false;
            csvPanel.TabIndex = 1;
            //
            // csvPathTextBox
            //
            csvPathTextBox.Name = "csvPathTextBox";
            csvPathTextBox.Size = new Size(520, 31);
            csvPathTextBox.TabIndex = 0;
            //
            // browseCsvButton
            //
            browseCsvButton.AutoSize = true;
            browseCsvButton.Name = "browseCsvButton";
            browseCsvButton.Text = "Browse…";
            browseCsvButton.TabIndex = 1;
            browseCsvButton.UseVisualStyleBackColor = true;
            browseCsvButton.Click += BrowseCsvButton_Click;
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(testConnectionButton);
            buttonPanel.Controls.Add(startButton);
            buttonPanel.Controls.Add(stopButton);
            buttonPanel.Margin = new Padding(0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.WrapContents = false;
            buttonPanel.TabIndex = 2;
            //
            // testConnectionButton
            //
            testConnectionButton.AutoSize = true;
            testConnectionButton.Name = "testConnectionButton";
            testConnectionButton.Text = "Test Connection";
            testConnectionButton.TabIndex = 0;
            testConnectionButton.UseVisualStyleBackColor = true;
            testConnectionButton.Click += TestConnectionButton_Click;
            //
            // startButton
            //
            startButton.AutoSize = true;
            startButton.Name = "startButton";
            startButton.Text = "Start";
            startButton.TabIndex = 1;
            startButton.UseVisualStyleBackColor = true;
            startButton.Click += StartButton_Click;
            //
            // stopButton
            //
            stopButton.AutoSize = true;
            stopButton.Enabled = false;
            stopButton.Name = "stopButton";
            stopButton.Text = "Stop";
            stopButton.TabIndex = 2;
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;
            //
            // gridGroupBox
            //
            gridGroupBox.Controls.Add(channelsGrid);
            gridGroupBox.Dock = DockStyle.Fill;
            gridGroupBox.Name = "gridGroupBox";
            gridGroupBox.Padding = new Padding(4, 5, 4, 5);
            gridGroupBox.TabIndex = 1;
            gridGroupBox.TabStop = false;
            gridGroupBox.Text = "Channels";
            //
            // channelsGrid
            //
            channelsGrid.AllowUserToAddRows = false;
            channelsGrid.AllowUserToDeleteRows = false;
            channelsGrid.AllowUserToResizeRows = false;
            channelsGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            channelsGrid.Columns.AddRange(new DataGridViewColumn[] { selectColumn, nameColumn, addressColumn, unitColumn, rateColumn, valueColumn });
            channelsGrid.Dock = DockStyle.Fill;
            channelsGrid.Name = "channelsGrid";
            channelsGrid.RowHeadersVisible = false;
            channelsGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            channelsGrid.TabIndex = 0;
            channelsGrid.CurrentCellDirtyStateChanged += ChannelsGrid_CurrentCellDirtyStateChanged;
            channelsGrid.DataError += ChannelsGrid_DataError;
            //
            // selectColumn
            //
            selectColumn.HeaderText = "Log";
            selectColumn.Name = "selectColumn";
            selectColumn.Width = 50;
            //
            // nameColumn
            //
            nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            nameColumn.HeaderText = "Channel";
            nameColumn.Name = "nameColumn";
            nameColumn.ReadOnly = true;
            //
            // addressColumn
            //
            addressColumn.HeaderText = "Address";
            addressColumn.Name = "addressColumn";
            addressColumn.ReadOnly = true;
            addressColumn.Width = 120;
            //
            // unitColumn
            //
            unitColumn.HeaderText = "Unit";
            unitColumn.Name = "unitColumn";
            unitColumn.ReadOnly = true;
            unitColumn.Width = 80;
            //
            // rateColumn
            //
            rateColumn.HeaderText = "Rate (Hz)";
            rateColumn.Name = "rateColumn";
            rateColumn.Width = 100;
            //
            // valueColumn
            //
            valueColumn.HeaderText = "Value";
            valueColumn.Name = "valueColumn";
            valueColumn.ReadOnly = true;
            valueColumn.Width = 120;
            //
            // statusGroupBox
            //
            statusGroupBox.AutoSize = true;
            statusGroupBox.Controls.Add(statusLayout);
            statusGroupBox.Dock = DockStyle.Fill;
            statusGroupBox.Name = "statusGroupBox";
            statusGroupBox.Padding = new Padding(4, 5, 4, 5);
            statusGroupBox.TabIndex = 2;
            statusGroupBox.TabStop = false;
            statusGroupBox.Text = "Status";
            //
            // statusLayout
            //
            statusLayout.AutoSize = true;
            statusLayout.ColumnCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle());
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.Controls.Add(statusLabel, 0, 0);
            statusLayout.Controls.Add(statusValueLabel, 1, 0);
            statusLayout.Controls.Add(framesLabel, 0, 1);
            statusLayout.Controls.Add(framesValueLabel, 1, 1);
            statusLayout.Controls.Add(lastUpdateLabel, 0, 2);
            statusLayout.Controls.Add(lastUpdateValueLabel, 1, 2);
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.Name = "statusLayout";
            statusLayout.RowCount = 3;
            statusLayout.RowStyles.Add(new RowStyle());
            statusLayout.RowStyles.Add(new RowStyle());
            statusLayout.RowStyles.Add(new RowStyle());
            statusLayout.TabIndex = 0;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Name = "statusLabel";
            statusLabel.Text = "Status:";
            //
            // statusValueLabel
            //
            statusValueLabel.AutoSize = true;
            statusValueLabel.Name = "statusValueLabel";
            statusValueLabel.Text = "Idle";
            //
            // framesLabel
            //
            framesLabel.AutoSize = true;
            framesLabel.Name = "framesLabel";
            framesLabel.Text = "Frames:";
            //
            // framesValueLabel
            //
            framesValueLabel.AutoSize = true;
            framesValueLabel.Name = "framesValueLabel";
            framesValueLabel.Text = "0";
            //
            // lastUpdateLabel
            //
            lastUpdateLabel.AutoSize = true;
            lastUpdateLabel.Name = "lastUpdateLabel";
            lastUpdateLabel.Text = "Last Update:";
            //
            // lastUpdateValueLabel
            //
            lastUpdateValueLabel.AutoSize = true;
            lastUpdateValueLabel.Name = "lastUpdateValueLabel";
            lastUpdateValueLabel.Text = "—";
            //
            // HighSpeedLogControl
            //
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(layoutPanel);
            Margin = new Padding(4, 5, 4, 5);
            Name = "HighSpeedLogControl";
            Size = new Size(1000, 600);
            layoutPanel.ResumeLayout(false);
            layoutPanel.PerformLayout();
            configGroupBox.ResumeLayout(false);
            configGroupBox.PerformLayout();
            configLayout.ResumeLayout(false);
            configLayout.PerformLayout();
            presetPanel.ResumeLayout(false);
            csvPanel.ResumeLayout(false);
            csvPanel.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            gridGroupBox.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)channelsGrid).EndInit();
            statusGroupBox.ResumeLayout(false);
            statusGroupBox.PerformLayout();
            statusLayout.ResumeLayout(false);
            statusLayout.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TableLayoutPanel layoutPanel;
        private GroupBox configGroupBox;
        private TableLayoutPanel configLayout;
        private Label presetLabel;
        private FlowLayoutPanel presetPanel;
        private ComboBox presetComboBox;
        private Button addChannelsButton;
        private Label csvPathLabel;
        private FlowLayoutPanel csvPanel;
        private TextBox csvPathTextBox;
        private Button browseCsvButton;
        private FlowLayoutPanel buttonPanel;
        private Button testConnectionButton;
        private Button startButton;
        private Button stopButton;
        private GroupBox gridGroupBox;
        private DataGridView channelsGrid;
        private DataGridViewCheckBoxColumn selectColumn;
        private DataGridViewTextBoxColumn nameColumn;
        private DataGridViewTextBoxColumn addressColumn;
        private DataGridViewTextBoxColumn unitColumn;
        private DataGridViewComboBoxColumn rateColumn;
        private DataGridViewTextBoxColumn valueColumn;
        private GroupBox statusGroupBox;
        private TableLayoutPanel statusLayout;
        private Label statusLabel;
        private Label statusValueLabel;
        private Label framesLabel;
        private Label framesValueLabel;
        private Label lastUpdateLabel;
        private Label lastUpdateValueLabel;
    }
}
