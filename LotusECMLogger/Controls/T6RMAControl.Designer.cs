namespace LotusECMLogger.Controls
{
    partial class T6RMAControl
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        partial void DisposeManaged();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeManaged();
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            layoutPanel = new TableLayoutPanel();
            configGroupBox = new GroupBox();
            configLayout = new TableLayoutPanel();
            addressLabel = new Label();
            addressTextBox = new TextBox();
            lengthLabel = new Label();
            lengthNumeric = new NumericUpDown();
            intervalLabel = new Label();
            intervalNumeric = new NumericUpDown();
            csvPathLabel = new Label();
            csvPanel = new FlowLayoutPanel();
            csvPathTextBox = new TextBox();
            browseCsvButton = new Button();
            buttonPanel = new FlowLayoutPanel();
            startButton = new Button();
            stopButton = new Button();
            statusGroupBox = new GroupBox();
            statusLayout = new TableLayoutPanel();
            statusLabel = new Label();
            statusValueLabel = new Label();
            samplesLabel = new Label();
            samplesValueLabel = new Label();
            lastUpdateLabel = new Label();
            lastUpdateValueLabel = new Label();
            dataGroupBox = new GroupBox();
            dataTextBox = new TextBox();
            layoutPanel.SuspendLayout();
            configGroupBox.SuspendLayout();
            configLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)lengthNumeric).BeginInit();
            ((System.ComponentModel.ISupportInitialize)intervalNumeric).BeginInit();
            csvPanel.SuspendLayout();
            buttonPanel.SuspendLayout();
            statusGroupBox.SuspendLayout();
            statusLayout.SuspendLayout();
            dataGroupBox.SuspendLayout();
            SuspendLayout();
            // 
            // layoutPanel
            // 
            layoutPanel.AutoSize = true;
            layoutPanel.ColumnCount = 1;
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.Controls.Add(configGroupBox, 0, 0);
            layoutPanel.Controls.Add(statusGroupBox, 0, 1);
            layoutPanel.Controls.Add(dataGroupBox, 0, 2);
            layoutPanel.Dock = DockStyle.Fill;
            layoutPanel.Location = new Point(14, 17);
            layoutPanel.Margin = new Padding(4, 5, 4, 5);
            layoutPanel.Name = "layoutPanel";
            layoutPanel.RowCount = 3;
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layoutPanel.Size = new Size(1115, 966);
            layoutPanel.TabIndex = 0;
            // 
            // configGroupBox
            // 
            configGroupBox.AutoSize = true;
            configGroupBox.Controls.Add(configLayout);
            configGroupBox.Dock = DockStyle.Fill;
            configGroupBox.Location = new Point(4, 5);
            configGroupBox.Margin = new Padding(4, 5, 4, 5);
            configGroupBox.Name = "configGroupBox";
            configGroupBox.Padding = new Padding(14, 17, 14, 17);
            configGroupBox.Size = new Size(1107, 307);
            configGroupBox.TabIndex = 0;
            configGroupBox.TabStop = false;
            configGroupBox.Text = "Memory Read Configuration";
            // 
            // configLayout
            // 
            configLayout.AutoSize = true;
            configLayout.ColumnCount = 3;
            configLayout.ColumnStyles.Add(new ColumnStyle());
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            configLayout.ColumnStyles.Add(new ColumnStyle());
            configLayout.Controls.Add(addressLabel, 0, 0);
            configLayout.Controls.Add(addressTextBox, 1, 0);
            configLayout.Controls.Add(lengthLabel, 0, 1);
            configLayout.Controls.Add(lengthNumeric, 1, 1);
            configLayout.Controls.Add(intervalLabel, 0, 2);
            configLayout.Controls.Add(intervalNumeric, 1, 2);
            configLayout.Controls.Add(csvPathLabel, 0, 3);
            configLayout.Controls.Add(csvPanel, 1, 3);
            configLayout.Controls.Add(buttonPanel, 1, 4);
            configLayout.Dock = DockStyle.Fill;
            configLayout.Location = new Point(14, 41);
            configLayout.Margin = new Padding(4, 5, 4, 5);
            configLayout.Name = "configLayout";
            configLayout.RowCount = 5;
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.Size = new Size(1079, 249);
            configLayout.TabIndex = 0;
            // 
            // addressLabel
            // 
            addressLabel.Anchor = AnchorStyles.Left;
            addressLabel.AutoSize = true;
            addressLabel.Location = new Point(4, 8);
            addressLabel.Margin = new Padding(4, 0, 4, 0);
            addressLabel.Name = "addressLabel";
            addressLabel.Size = new Size(195, 25);
            addressLabel.TabIndex = 0;
            addressLabel.Text = "Memory Address (hex):";
            // 
            // addressTextBox
            // 
            addressTextBox.Location = new Point(207, 5);
            addressTextBox.Margin = new Padding(4, 5, 4, 5);
            addressTextBox.Name = "addressTextBox";
            addressTextBox.Size = new Size(284, 31);
            addressTextBox.TabIndex = 1;
            addressTextBox.Text = "0x40000000";
            // 
            // lengthLabel
            // 
            lengthLabel.Anchor = AnchorStyles.Left;
            lengthLabel.AutoSize = true;
            lengthLabel.Location = new Point(4, 49);
            lengthLabel.Margin = new Padding(4, 0, 4, 0);
            lengthLabel.Name = "lengthLabel";
            lengthLabel.Size = new Size(128, 25);
            lengthLabel.TabIndex = 2;
            lengthLabel.Text = "Length (bytes):";
            // 
            // lengthNumeric
            // 
            lengthNumeric.Location = new Point(207, 46);
            lengthNumeric.Margin = new Padding(4, 5, 4, 5);
            lengthNumeric.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            lengthNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            lengthNumeric.Name = "lengthNumeric";
            lengthNumeric.Size = new Size(143, 31);
            lengthNumeric.TabIndex = 3;
            lengthNumeric.Value = new decimal(new int[] { 4, 0, 0, 0 });
            // 
            // intervalLabel
            // 
            intervalLabel.Anchor = AnchorStyles.Left;
            intervalLabel.AutoSize = true;
            intervalLabel.Location = new Point(4, 90);
            intervalLabel.Margin = new Padding(4, 0, 4, 0);
            intervalLabel.Name = "intervalLabel";
            intervalLabel.Size = new Size(171, 25);
            intervalLabel.TabIndex = 4;
            intervalLabel.Text = "Polling Interval (ms):";
            // 
            // intervalNumeric
            // 
            intervalNumeric.Location = new Point(207, 87);
            intervalNumeric.Margin = new Padding(4, 5, 4, 5);
            intervalNumeric.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            intervalNumeric.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            intervalNumeric.Name = "intervalNumeric";
            intervalNumeric.Size = new Size(143, 31);
            intervalNumeric.TabIndex = 5;
            intervalNumeric.Value = new decimal(new int[] { 100, 0, 0, 0 });
            // 
            // csvPathLabel
            // 
            csvPathLabel.Anchor = AnchorStyles.Left;
            csvPathLabel.AutoSize = true;
            csvPathLabel.Location = new Point(4, 144);
            csvPathLabel.Margin = new Padding(4, 0, 4, 0);
            csvPathLabel.Name = "csvPathLabel";
            csvPathLabel.Size = new Size(141, 25);
            csvPathLabel.TabIndex = 6;
            csvPathLabel.Text = "CSV Output File:";
            // 
            // csvPanel
            // 
            csvPanel.AutoSize = true;
            csvPanel.Controls.Add(csvPathTextBox);
            csvPanel.Controls.Add(browseCsvButton);
            csvPanel.Dock = DockStyle.Fill;
            csvPanel.Location = new Point(203, 123);
            csvPanel.Margin = new Padding(0);
            csvPanel.Name = "csvPanel";
            csvPanel.Size = new Size(876, 68);
            csvPanel.TabIndex = 7;
            csvPanel.WrapContents = false;
            // 
            // csvPathTextBox
            // 
            csvPathTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            csvPathTextBox.Location = new Point(4, 18);
            csvPathTextBox.Margin = new Padding(4, 5, 4, 5);
            csvPathTextBox.Name = "csvPathTextBox";
            csvPathTextBox.Size = new Size(713, 31);
            csvPathTextBox.TabIndex = 0;
            // 
            // browseCsvButton
            // 
            browseCsvButton.AutoSize = true;
            browseCsvButton.Location = new Point(725, 5);
            browseCsvButton.Margin = new Padding(4, 5, 4, 5);
            browseCsvButton.Name = "browseCsvButton";
            browseCsvButton.Size = new Size(130, 58);
            browseCsvButton.TabIndex = 1;
            browseCsvButton.Text = "Browse...";
            browseCsvButton.UseVisualStyleBackColor = true;
            browseCsvButton.Click += BrowseCsvButton_Click;
            // 
            // buttonPanel
            // 
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(startButton);
            buttonPanel.Controls.Add(stopButton);
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.Location = new Point(203, 204);
            buttonPanel.Margin = new Padding(0, 13, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Size = new Size(876, 45);
            buttonPanel.TabIndex = 8;
            buttonPanel.WrapContents = false;
            // 
            // startButton
            // 
            startButton.AutoSize = true;
            startButton.Location = new Point(4, 5);
            startButton.Margin = new Padding(4, 5, 4, 5);
            startButton.Name = "startButton";
            startButton.Size = new Size(184, 35);
            startButton.TabIndex = 0;
            startButton.Text = "Start Logging";
            startButton.UseVisualStyleBackColor = true;
            startButton.Click += StartButton_Click;
            // 
            // stopButton
            // 
            stopButton.AutoSize = true;
            stopButton.Enabled = false;
            stopButton.Location = new Point(196, 5);
            stopButton.Margin = new Padding(4, 5, 4, 5);
            stopButton.Name = "stopButton";
            stopButton.Size = new Size(186, 35);
            stopButton.TabIndex = 1;
            stopButton.Text = "Stop Logging";
            stopButton.UseVisualStyleBackColor = true;
            stopButton.Click += StopButton_Click;
            // 
            // statusGroupBox
            // 
            statusGroupBox.AutoSize = true;
            statusGroupBox.Controls.Add(statusLayout);
            statusGroupBox.Dock = DockStyle.Fill;
            statusGroupBox.Location = new Point(4, 327);
            statusGroupBox.Margin = new Padding(4, 10, 4, 5);
            statusGroupBox.Name = "statusGroupBox";
            statusGroupBox.Padding = new Padding(14, 17, 14, 17);
            statusGroupBox.Size = new Size(1107, 133);
            statusGroupBox.TabIndex = 1;
            statusGroupBox.TabStop = false;
            statusGroupBox.Text = "Logging Status";
            // 
            // statusLayout
            // 
            statusLayout.AutoSize = true;
            statusLayout.ColumnCount = 2;
            statusLayout.ColumnStyles.Add(new ColumnStyle());
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.Controls.Add(statusLabel, 0, 0);
            statusLayout.Controls.Add(statusValueLabel, 1, 0);
            statusLayout.Controls.Add(samplesLabel, 0, 1);
            statusLayout.Controls.Add(samplesValueLabel, 1, 1);
            statusLayout.Controls.Add(lastUpdateLabel, 0, 2);
            statusLayout.Controls.Add(lastUpdateValueLabel, 1, 2);
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.Location = new Point(14, 41);
            statusLayout.Margin = new Padding(4, 5, 4, 5);
            statusLayout.Name = "statusLayout";
            statusLayout.RowCount = 3;
            statusLayout.RowStyles.Add(new RowStyle());
            statusLayout.RowStyles.Add(new RowStyle());
            statusLayout.RowStyles.Add(new RowStyle());
            statusLayout.Size = new Size(1079, 75);
            statusLayout.TabIndex = 0;
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            statusLabel.Location = new Point(4, 0);
            statusLabel.Margin = new Padding(4, 0, 4, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(70, 25);
            statusLabel.TabIndex = 0;
            statusLabel.Text = "Status:";
            // 
            // statusValueLabel
            // 
            statusValueLabel.AutoSize = true;
            statusValueLabel.Location = new Point(183, 0);
            statusValueLabel.Margin = new Padding(4, 0, 4, 0);
            statusValueLabel.Name = "statusValueLabel";
            statusValueLabel.Size = new Size(41, 25);
            statusValueLabel.TabIndex = 1;
            statusValueLabel.Text = "Idle";
            // 
            // samplesLabel
            // 
            samplesLabel.AutoSize = true;
            samplesLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            samplesLabel.Location = new Point(4, 25);
            samplesLabel.Margin = new Padding(4, 0, 4, 0);
            samplesLabel.Name = "samplesLabel";
            samplesLabel.Size = new Size(171, 25);
            samplesLabel.TabIndex = 2;
            samplesLabel.Text = "Samples Collected:";
            // 
            // samplesValueLabel
            // 
            samplesValueLabel.AutoSize = true;
            samplesValueLabel.Location = new Point(183, 25);
            samplesValueLabel.Margin = new Padding(4, 0, 4, 0);
            samplesValueLabel.Name = "samplesValueLabel";
            samplesValueLabel.Size = new Size(22, 25);
            samplesValueLabel.TabIndex = 3;
            samplesValueLabel.Text = "0";
            // 
            // lastUpdateLabel
            // 
            lastUpdateLabel.AutoSize = true;
            lastUpdateLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lastUpdateLabel.Location = new Point(4, 50);
            lastUpdateLabel.Margin = new Padding(4, 0, 4, 0);
            lastUpdateLabel.Name = "lastUpdateLabel";
            lastUpdateLabel.Size = new Size(118, 25);
            lastUpdateLabel.TabIndex = 4;
            lastUpdateLabel.Text = "Last Update:";
            // 
            // lastUpdateValueLabel
            // 
            lastUpdateValueLabel.AutoSize = true;
            lastUpdateValueLabel.Location = new Point(183, 50);
            lastUpdateValueLabel.Margin = new Padding(4, 0, 4, 0);
            lastUpdateValueLabel.Name = "lastUpdateValueLabel";
            lastUpdateValueLabel.Size = new Size(44, 25);
            lastUpdateValueLabel.TabIndex = 5;
            lastUpdateValueLabel.Text = "N/A";
            // 
            // dataGroupBox
            // 
            dataGroupBox.Controls.Add(dataTextBox);
            dataGroupBox.Dock = DockStyle.Fill;
            dataGroupBox.Location = new Point(4, 475);
            dataGroupBox.Margin = new Padding(4, 10, 4, 5);
            dataGroupBox.Name = "dataGroupBox";
            dataGroupBox.Padding = new Padding(14, 17, 14, 17);
            dataGroupBox.Size = new Size(1107, 486);
            dataGroupBox.TabIndex = 2;
            dataGroupBox.TabStop = false;
            dataGroupBox.Text = "Latest Data";
            // 
            // dataTextBox
            // 
            dataTextBox.Dock = DockStyle.Fill;
            dataTextBox.Font = new Font("Consolas", 9F);
            dataTextBox.Location = new Point(14, 41);
            dataTextBox.Margin = new Padding(4, 5, 4, 5);
            dataTextBox.Multiline = true;
            dataTextBox.Name = "dataTextBox";
            dataTextBox.ReadOnly = true;
            dataTextBox.ScrollBars = ScrollBars.Both;
            dataTextBox.Size = new Size(1079, 428);
            dataTextBox.TabIndex = 0;
            dataTextBox.Text = "No data received yet...";
            // 
            // T6RMAControl
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(layoutPanel);
            Margin = new Padding(4, 5, 4, 5);
            Name = "T6RMAControl";
            Padding = new Padding(14, 17, 14, 17);
            Size = new Size(1143, 1000);
            layoutPanel.ResumeLayout(false);
            layoutPanel.PerformLayout();
            configGroupBox.ResumeLayout(false);
            configGroupBox.PerformLayout();
            configLayout.ResumeLayout(false);
            configLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)lengthNumeric).EndInit();
            ((System.ComponentModel.ISupportInitialize)intervalNumeric).EndInit();
            csvPanel.ResumeLayout(false);
            csvPanel.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            statusGroupBox.ResumeLayout(false);
            statusGroupBox.PerformLayout();
            statusLayout.ResumeLayout(false);
            statusLayout.PerformLayout();
            dataGroupBox.ResumeLayout(false);
            dataGroupBox.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TableLayoutPanel layoutPanel;
        private GroupBox configGroupBox;
        private TableLayoutPanel configLayout;
        private Label addressLabel;
        private TextBox addressTextBox;
        private Label lengthLabel;
        private NumericUpDown lengthNumeric;
        private Label intervalLabel;
        private NumericUpDown intervalNumeric;
        private Label csvPathLabel;
        private FlowLayoutPanel csvPanel;
        private TextBox csvPathTextBox;
        private Button browseCsvButton;
        private FlowLayoutPanel buttonPanel;
        private Button startButton;
        private Button stopButton;
        private GroupBox statusGroupBox;
        private TableLayoutPanel statusLayout;
        private Label statusLabel;
        private Label statusValueLabel;
        private Label samplesLabel;
        private Label samplesValueLabel;
        private Label lastUpdateLabel;
        private Label lastUpdateValueLabel;
        private GroupBox dataGroupBox;
        private TextBox dataTextBox;
    }
}
