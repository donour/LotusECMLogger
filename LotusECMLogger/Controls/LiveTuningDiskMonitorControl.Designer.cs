namespace LotusECMLogger.Controls
{
    partial class LiveTuningDiskMonitorControl
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            mainTableLayout = new TableLayoutPanel();
            presetLabel = new Label();
            presetComboBox = new ComboBox();
            baseAddressLabel = new Label();
            baseAddressTextBox = new TextBox();
            lengthLabel = new Label();
            lengthNumericUpDown = new NumericUpDown();
            outputDirectoryLabel = new Label();
            outputDirectoryTextBox = new TextBox();
            browseOutputButton = new Button();
            readFromEcuButton = new Button();
            existingFileLabel = new Label();
            existingFileTextBox = new TextBox();
            browseFileButton = new Button();
            startMonitoringButton = new Button();
            stopMonitoringButton = new Button();
            statusTextBox = new TextBox();
            mainTableLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)lengthNumericUpDown).BeginInit();
            SuspendLayout();
            //
            // mainTableLayout
            //
            mainTableLayout.ColumnCount = 5;
            mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainTableLayout.Controls.Add(presetLabel, 0, 0);
            mainTableLayout.Controls.Add(presetComboBox, 1, 0);
            mainTableLayout.Controls.Add(baseAddressLabel, 0, 1);
            mainTableLayout.Controls.Add(baseAddressTextBox, 1, 1);
            mainTableLayout.Controls.Add(lengthLabel, 2, 1);
            mainTableLayout.Controls.Add(lengthNumericUpDown, 3, 1);
            mainTableLayout.Controls.Add(outputDirectoryLabel, 0, 2);
            mainTableLayout.Controls.Add(outputDirectoryTextBox, 1, 2);
            mainTableLayout.Controls.Add(browseOutputButton, 3, 2);
            mainTableLayout.Controls.Add(readFromEcuButton, 4, 2);
            mainTableLayout.Controls.Add(existingFileLabel, 0, 3);
            mainTableLayout.Controls.Add(existingFileTextBox, 1, 3);
            mainTableLayout.Controls.Add(browseFileButton, 3, 3);
            mainTableLayout.Controls.Add(startMonitoringButton, 4, 3);
            mainTableLayout.Controls.Add(stopMonitoringButton, 0, 4);
            mainTableLayout.Controls.Add(statusTextBox, 0, 5);
            mainTableLayout.Dock = DockStyle.Fill;
            mainTableLayout.Location = new Point(0, 0);
            mainTableLayout.Name = "mainTableLayout";
            mainTableLayout.Padding = new Padding(10);
            mainTableLayout.RowCount = 6;
            mainTableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainTableLayout.Size = new Size(1143, 1000);
            mainTableLayout.TabIndex = 0;
            //
            // presetLabel
            //
            presetLabel.Anchor = AnchorStyles.Left;
            presetLabel.AutoSize = true;
            presetLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            presetLabel.Location = new Point(13, 18);
            presetLabel.Name = "presetLabel";
            presetLabel.Size = new Size(96, 15);
            presetLabel.TabIndex = 0;
            presetLabel.Text = "Memory Preset:";
            //
            // presetComboBox
            //
            mainTableLayout.SetColumnSpan(presetComboBox, 4);
            presetComboBox.Dock = DockStyle.Fill;
            presetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            presetComboBox.Location = new Point(115, 13);
            presetComboBox.Name = "presetComboBox";
            presetComboBox.Size = new Size(1015, 23);
            presetComboBox.TabIndex = 1;
            presetComboBox.SelectedIndexChanged += presetComboBox_SelectedIndexChanged;
            //
            // baseAddressLabel
            //
            baseAddressLabel.Anchor = AnchorStyles.Left;
            baseAddressLabel.AutoSize = true;
            baseAddressLabel.Location = new Point(13, 50);
            baseAddressLabel.Name = "baseAddressLabel";
            baseAddressLabel.Size = new Size(110, 15);
            baseAddressLabel.TabIndex = 2;
            baseAddressLabel.Text = "Base Address (Hex):";
            //
            // baseAddressTextBox
            //
            baseAddressTextBox.Anchor = AnchorStyles.Left;
            baseAddressTextBox.CharacterCasing = CharacterCasing.Upper;
            baseAddressTextBox.Location = new Point(129, 46);
            baseAddressTextBox.MaxLength = 8;
            baseAddressTextBox.Name = "baseAddressTextBox";
            baseAddressTextBox.Size = new Size(120, 23);
            baseAddressTextBox.TabIndex = 3;
            baseAddressTextBox.Text = "40000000";
            //
            // lengthLabel
            //
            lengthLabel.Anchor = AnchorStyles.Left;
            lengthLabel.AutoSize = true;
            lengthLabel.Location = new Point(558, 50);
            lengthLabel.Name = "lengthLabel";
            lengthLabel.Size = new Size(112, 15);
            lengthLabel.TabIndex = 4;
            lengthLabel.Text = "Length (Bytes, Hex):";
            //
            // lengthNumericUpDown
            //
            lengthNumericUpDown.Anchor = AnchorStyles.Left;
            lengthNumericUpDown.Hexadecimal = true;
            lengthNumericUpDown.Location = new Point(676, 46);
            lengthNumericUpDown.Maximum = new decimal(new int[] { 65536, 0, 0, 0 });
            lengthNumericUpDown.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            lengthNumericUpDown.Name = "lengthNumericUpDown";
            lengthNumericUpDown.Size = new Size(120, 23);
            lengthNumericUpDown.TabIndex = 5;
            lengthNumericUpDown.Value = new decimal(new int[] { 4096, 0, 0, 0 });
            //
            // outputDirectoryLabel
            //
            outputDirectoryLabel.Anchor = AnchorStyles.Left;
            outputDirectoryLabel.AutoSize = true;
            outputDirectoryLabel.Location = new Point(13, 88);
            outputDirectoryLabel.Name = "outputDirectoryLabel";
            outputDirectoryLabel.Size = new Size(99, 15);
            outputDirectoryLabel.TabIndex = 6;
            outputDirectoryLabel.Text = "Output Directory:";
            //
            // outputDirectoryTextBox
            //
            mainTableLayout.SetColumnSpan(outputDirectoryTextBox, 2);
            outputDirectoryTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            outputDirectoryTextBox.Location = new Point(129, 84);
            outputDirectoryTextBox.Name = "outputDirectoryTextBox";
            outputDirectoryTextBox.Size = new Size(541, 23);
            outputDirectoryTextBox.TabIndex = 7;
            //
            // browseOutputButton
            //
            browseOutputButton.Anchor = AnchorStyles.Left;
            browseOutputButton.Location = new Point(676, 81);
            browseOutputButton.Name = "browseOutputButton";
            browseOutputButton.Size = new Size(90, 29);
            browseOutputButton.TabIndex = 8;
            browseOutputButton.Text = "Browse...";
            browseOutputButton.UseVisualStyleBackColor = true;
            browseOutputButton.Click += BrowseOutputButton_Click;
            //
            // readFromEcuButton
            //
            readFromEcuButton.Anchor = AnchorStyles.Left;
            readFromEcuButton.Location = new Point(1003, 81);
            readFromEcuButton.Name = "readFromEcuButton";
            readFromEcuButton.Size = new Size(120, 29);
            readFromEcuButton.TabIndex = 9;
            readFromEcuButton.Text = "Read && Start";
            readFromEcuButton.UseVisualStyleBackColor = true;
            readFromEcuButton.Click += ReadFromEcuButton_Click;
            //
            // existingFileLabel
            //
            existingFileLabel.Anchor = AnchorStyles.Left;
            existingFileLabel.AutoSize = true;
            existingFileLabel.Location = new Point(13, 126);
            existingFileLabel.Name = "existingFileLabel";
            existingFileLabel.Size = new Size(89, 15);
            existingFileLabel.TabIndex = 10;
            existingFileLabel.Text = "Calibration File:";
            //
            // existingFileTextBox
            //
            mainTableLayout.SetColumnSpan(existingFileTextBox, 2);
            existingFileTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            existingFileTextBox.Location = new Point(129, 122);
            existingFileTextBox.Name = "existingFileTextBox";
            existingFileTextBox.Size = new Size(541, 23);
            existingFileTextBox.TabIndex = 11;
            //
            // browseFileButton
            //
            browseFileButton.Anchor = AnchorStyles.Left;
            browseFileButton.Location = new Point(676, 119);
            browseFileButton.Name = "browseFileButton";
            browseFileButton.Size = new Size(90, 29);
            browseFileButton.TabIndex = 12;
            browseFileButton.Text = "Browse...";
            browseFileButton.UseVisualStyleBackColor = true;
            browseFileButton.Click += BrowseFileButton_Click;
            //
            // startMonitoringButton
            //
            startMonitoringButton.Anchor = AnchorStyles.Left;
            startMonitoringButton.Location = new Point(1003, 119);
            startMonitoringButton.Name = "startMonitoringButton";
            startMonitoringButton.Size = new Size(120, 29);
            startMonitoringButton.TabIndex = 13;
            startMonitoringButton.Text = "Start Monitoring";
            startMonitoringButton.UseVisualStyleBackColor = true;
            startMonitoringButton.Click += StartMonitoringButton_Click;
            //
            // stopMonitoringButton
            //
            stopMonitoringButton.Enabled = false;
            stopMonitoringButton.Location = new Point(13, 161);
            stopMonitoringButton.Name = "stopMonitoringButton";
            stopMonitoringButton.Size = new Size(120, 29);
            stopMonitoringButton.TabIndex = 14;
            stopMonitoringButton.Text = "Stop Monitoring";
            stopMonitoringButton.UseVisualStyleBackColor = true;
            stopMonitoringButton.Click += StopMonitoringButton_Click;
            //
            // statusTextBox
            //
            mainTableLayout.SetColumnSpan(statusTextBox, 5);
            statusTextBox.Dock = DockStyle.Fill;
            statusTextBox.Font = new Font("Consolas", 9F);
            statusTextBox.Location = new Point(13, 203);
            statusTextBox.Multiline = true;
            statusTextBox.Name = "statusTextBox";
            statusTextBox.ReadOnly = true;
            statusTextBox.ScrollBars = ScrollBars.Both;
            statusTextBox.Size = new Size(1117, 784);
            statusTextBox.TabIndex = 15;
            statusTextBox.WordWrap = false;
            //
            // LiveTuningDiskMonitorControl
            //
            AutoScaleMode = AutoScaleMode.None;
            Controls.Add(mainTableLayout);
            Margin = new Padding(4, 5, 4, 5);
            Name = "LiveTuningDiskMonitorControl";
            Size = new Size(1143, 1000);
            mainTableLayout.ResumeLayout(false);
            mainTableLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)lengthNumericUpDown).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel mainTableLayout;
        private Label presetLabel;
        private ComboBox presetComboBox;
        private Label baseAddressLabel;
        private TextBox baseAddressTextBox;
        private Label lengthLabel;
        private NumericUpDown lengthNumericUpDown;
        private Label outputDirectoryLabel;
        private TextBox outputDirectoryTextBox;
        private Button browseOutputButton;
        private Button readFromEcuButton;
        private Label existingFileLabel;
        private TextBox existingFileTextBox;
        private Button browseFileButton;
        private Button startMonitoringButton;
        private Button stopMonitoringButton;
        private TextBox statusTextBox;
    }
}
