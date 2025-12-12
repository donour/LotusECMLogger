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
            configPanel = new Panel();
            fileConfigGroup = new GroupBox();
            browseButton = new Button();
            filePathTextBox = new TextBox();
            filePathLabel = new Label();
            memoryConfigGroup = new GroupBox();
            lengthNumericUpDown = new NumericUpDown();
            lengthLabel = new Label();
            baseAddressTextBox = new TextBox();
            baseAddressLabel = new Label();
            operationsPanel = new Panel();
            stopMonitoringButton = new Button();
            startMonitoringButton = new Button();
            readFromEcuButton = new Button();
            statusPanel = new Panel();
            statusTextBox = new TextBox();
            statusLabel = new Label();
            configPanel.SuspendLayout();
            fileConfigGroup.SuspendLayout();
            memoryConfigGroup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)lengthNumericUpDown).BeginInit();
            operationsPanel.SuspendLayout();
            statusPanel.SuspendLayout();
            SuspendLayout();
            // 
            // configPanel
            // 
            configPanel.Controls.Add(fileConfigGroup);
            configPanel.Controls.Add(memoryConfigGroup);
            configPanel.Dock = DockStyle.Top;
            configPanel.Location = new Point(0, 0);
            configPanel.Margin = new Padding(4, 5, 4, 5);
            configPanel.Name = "configPanel";
            configPanel.Padding = new Padding(14, 17, 14, 17);
            configPanel.Size = new Size(1143, 233);
            configPanel.TabIndex = 0;
            // 
            // fileConfigGroup
            // 
            fileConfigGroup.Controls.Add(browseButton);
            fileConfigGroup.Controls.Add(filePathTextBox);
            fileConfigGroup.Controls.Add(filePathLabel);
            fileConfigGroup.Location = new Point(600, 17);
            fileConfigGroup.Margin = new Padding(4, 5, 4, 5);
            fileConfigGroup.Name = "fileConfigGroup";
            fileConfigGroup.Padding = new Padding(14, 17, 14, 17);
            fileConfigGroup.Size = new Size(529, 200);
            fileConfigGroup.TabIndex = 1;
            fileConfigGroup.TabStop = false;
            fileConfigGroup.Text = "File Configuration";
            // 
            // browseButton
            // 
            browseButton.Location = new Point(400, 80);
            browseButton.Margin = new Padding(4, 5, 4, 5);
            browseButton.Name = "browseButton";
            browseButton.Size = new Size(107, 42);
            browseButton.TabIndex = 2;
            browseButton.Text = "Browse...";
            browseButton.UseVisualStyleBackColor = true;
            browseButton.Click += browseButton_Click;
            // 
            // filePathTextBox
            // 
            filePathTextBox.Location = new Point(21, 80);
            filePathTextBox.Margin = new Padding(4, 5, 4, 5);
            filePathTextBox.Name = "filePathTextBox";
            filePathTextBox.Size = new Size(370, 31);
            filePathTextBox.TabIndex = 1;
            // 
            // filePathLabel
            // 
            filePathLabel.AutoSize = true;
            filePathLabel.Location = new Point(21, 42);
            filePathLabel.Margin = new Padding(4, 0, 4, 0);
            filePathLabel.Name = "filePathLabel";
            filePathLabel.Size = new Size(263, 25);
            filePathLabel.TabIndex = 0;
            filePathLabel.Text = "Output Directory (auto-named):";
            // 
            // memoryConfigGroup
            // 
            memoryConfigGroup.Controls.Add(lengthNumericUpDown);
            memoryConfigGroup.Controls.Add(lengthLabel);
            memoryConfigGroup.Controls.Add(baseAddressTextBox);
            memoryConfigGroup.Controls.Add(baseAddressLabel);
            memoryConfigGroup.Location = new Point(14, 17);
            memoryConfigGroup.Margin = new Padding(4, 5, 4, 5);
            memoryConfigGroup.Name = "memoryConfigGroup";
            memoryConfigGroup.Padding = new Padding(14, 17, 14, 17);
            memoryConfigGroup.Size = new Size(571, 200);
            memoryConfigGroup.TabIndex = 0;
            memoryConfigGroup.TabStop = false;
            memoryConfigGroup.Text = "ECU Memory Configuration";
            // 
            // lengthNumericUpDown
            // 
            lengthNumericUpDown.Hexadecimal = true;
            lengthNumericUpDown.Location = new Point(314, 80);
            lengthNumericUpDown.Margin = new Padding(4, 5, 4, 5);
            lengthNumericUpDown.Maximum = new decimal(new int[] { 65536, 0, 0, 0 });
            lengthNumericUpDown.Minimum = new decimal(new int[] { 4, 0, 0, 0 });
            lengthNumericUpDown.Name = "lengthNumericUpDown";
            lengthNumericUpDown.Size = new Size(236, 31);
            lengthNumericUpDown.TabIndex = 3;
            lengthNumericUpDown.Value = new decimal(new int[] { 4096, 0, 0, 0 });
            // 
            // lengthLabel
            // 
            lengthLabel.AutoSize = true;
            lengthLabel.Location = new Point(314, 42);
            lengthLabel.Margin = new Padding(4, 0, 4, 0);
            lengthLabel.Name = "lengthLabel";
            lengthLabel.Size = new Size(166, 25);
            lengthLabel.TabIndex = 2;
            lengthLabel.Text = "Length (Bytes, Hex):";
            // 
            // baseAddressTextBox
            // 
            baseAddressTextBox.CharacterCasing = CharacterCasing.Upper;
            baseAddressTextBox.Location = new Point(21, 80);
            baseAddressTextBox.Margin = new Padding(4, 5, 4, 5);
            baseAddressTextBox.MaxLength = 8;
            baseAddressTextBox.Name = "baseAddressTextBox";
            baseAddressTextBox.Size = new Size(270, 31);
            baseAddressTextBox.TabIndex = 1;
            baseAddressTextBox.Text = "40000000";
            // 
            // baseAddressLabel
            // 
            baseAddressLabel.AutoSize = true;
            baseAddressLabel.Location = new Point(21, 42);
            baseAddressLabel.Margin = new Padding(4, 0, 4, 0);
            baseAddressLabel.Name = "baseAddressLabel";
            baseAddressLabel.Size = new Size(167, 25);
            baseAddressLabel.TabIndex = 0;
            baseAddressLabel.Text = "Base Address (Hex):";
            // 
            // operationsPanel
            // 
            operationsPanel.Controls.Add(stopMonitoringButton);
            operationsPanel.Controls.Add(startMonitoringButton);
            operationsPanel.Controls.Add(readFromEcuButton);
            operationsPanel.Dock = DockStyle.Top;
            operationsPanel.Location = new Point(0, 233);
            operationsPanel.Margin = new Padding(4, 5, 4, 5);
            operationsPanel.Name = "operationsPanel";
            operationsPanel.Padding = new Padding(14, 17, 14, 17);
            operationsPanel.Size = new Size(1143, 100);
            operationsPanel.TabIndex = 1;
            // 
            // stopMonitoringButton
            // 
            stopMonitoringButton.Enabled = false;
            stopMonitoringButton.Location = new Point(414, 25);
            stopMonitoringButton.Margin = new Padding(4, 5, 4, 5);
            stopMonitoringButton.Name = "stopMonitoringButton";
            stopMonitoringButton.Size = new Size(186, 58);
            stopMonitoringButton.TabIndex = 2;
            stopMonitoringButton.Text = "Stop Monitoring";
            stopMonitoringButton.UseVisualStyleBackColor = true;
            stopMonitoringButton.Click += stopMonitoringButton_Click;
            // 
            // startMonitoringButton
            // 
            startMonitoringButton.Enabled = false;
            startMonitoringButton.Location = new Point(214, 25);
            startMonitoringButton.Margin = new Padding(4, 5, 4, 5);
            startMonitoringButton.Name = "startMonitoringButton";
            startMonitoringButton.Size = new Size(186, 58);
            startMonitoringButton.TabIndex = 1;
            startMonitoringButton.Text = "Start Monitoring";
            startMonitoringButton.UseVisualStyleBackColor = true;
            startMonitoringButton.Click += startMonitoringButton_Click;
            // 
            // readFromEcuButton
            // 
            readFromEcuButton.Location = new Point(14, 25);
            readFromEcuButton.Margin = new Padding(4, 5, 4, 5);
            readFromEcuButton.Name = "readFromEcuButton";
            readFromEcuButton.Size = new Size(186, 58);
            readFromEcuButton.TabIndex = 0;
            readFromEcuButton.Text = "Read from ECU";
            readFromEcuButton.UseVisualStyleBackColor = true;
            readFromEcuButton.Click += readFromEcuButton_Click;
            // 
            // statusPanel
            // 
            statusPanel.Controls.Add(statusTextBox);
            statusPanel.Controls.Add(statusLabel);
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.Location = new Point(0, 333);
            statusPanel.Margin = new Padding(4, 5, 4, 5);
            statusPanel.Name = "statusPanel";
            statusPanel.Padding = new Padding(14, 17, 14, 17);
            statusPanel.Size = new Size(1143, 667);
            statusPanel.TabIndex = 2;
            // 
            // statusTextBox
            // 
            statusTextBox.Dock = DockStyle.Fill;
            statusTextBox.Font = new Font("Consolas", 9F);
            statusTextBox.Location = new Point(14, 42);
            statusTextBox.Margin = new Padding(4, 5, 4, 5);
            statusTextBox.Multiline = true;
            statusTextBox.Name = "statusTextBox";
            statusTextBox.ReadOnly = true;
            statusTextBox.ScrollBars = ScrollBars.Both;
            statusTextBox.Size = new Size(1115, 608);
            statusTextBox.TabIndex = 1;
            statusTextBox.WordWrap = false;
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Dock = DockStyle.Top;
            statusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            statusLabel.Location = new Point(14, 17);
            statusLabel.Margin = new Padding(4, 0, 4, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(190, 25);
            statusLabel.TabIndex = 0;
            statusLabel.Text = "Status / Activity Log:";
            // 
            // LiveTuningDiskMonitorControl
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(statusPanel);
            Controls.Add(operationsPanel);
            Controls.Add(configPanel);
            Margin = new Padding(4, 5, 4, 5);
            Name = "LiveTuningDiskMonitorControl";
            Size = new Size(1143, 1000);
            configPanel.ResumeLayout(false);
            fileConfigGroup.ResumeLayout(false);
            fileConfigGroup.PerformLayout();
            memoryConfigGroup.ResumeLayout(false);
            memoryConfigGroup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)lengthNumericUpDown).EndInit();
            operationsPanel.ResumeLayout(false);
            statusPanel.ResumeLayout(false);
            statusPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel configPanel;
        private GroupBox memoryConfigGroup;
        private Label baseAddressLabel;
        private TextBox baseAddressTextBox;
        private Label lengthLabel;
        private NumericUpDown lengthNumericUpDown;
        private GroupBox fileConfigGroup;
        private Label filePathLabel;
        private TextBox filePathTextBox;
        private Button browseButton;
        private Panel operationsPanel;
        private Button readFromEcuButton;
        private Button startMonitoringButton;
        private Button stopMonitoringButton;
        private Panel statusPanel;
        private Label statusLabel;
        private TextBox statusTextBox;
    }
}
