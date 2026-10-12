namespace LotusECMLogger.Controls
{
    partial class CcpControl
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
            connectionPanel = new Panel();
            connectButton = new Button();
            disconnectButton = new Button();
            readRegistryButton = new Button();
            stateLabel = new Label();
            noteLabel = new Label();
            infoListView = new ListView();
            memoryPanel = new Panel();
            addressLabel = new Label();
            addressTextBox = new TextBox();
            lengthLabel = new Label();
            lengthUpDown = new NumericUpDown();
            calibrationRangeButton = new Button();
            readButton = new Button();
            saveButton = new Button();
            checksumButton = new Button();
            compareButton = new Button();
            diffButton = new Button();
            cancelButton = new Button();
            progressBar = new ProgressBar();
            writePanel = new Panel();
            writeTitleLabel = new Label();
            buildLabel = new Label();
            buildComboBox = new ComboBox();
            bytesLabel = new Label();
            bytesTextBox = new TextBox();
            writeValueButton = new Button();
            writeFileButton = new Button();
            restoreButton = new Button();
            writeNoteLabel = new Label();
            subTabs = new TabControl();
            memoryPage = new TabPage();
            variablesPage = new TabPage();
            trafficPage = new TabPage();
            selfTestPage = new TabPage();
            hexTextBox = new TextBox();
            logTextBox = new TextBox();
            connectionPanel.SuspendLayout();
            memoryPanel.SuspendLayout();
            writePanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)lengthUpDown).BeginInit();
            subTabs.SuspendLayout();
            memoryPage.SuspendLayout();
            SuspendLayout();
            //
            // connectionPanel
            //
            connectionPanel.Controls.Add(infoListView);
            connectionPanel.Controls.Add(noteLabel);
            connectionPanel.Controls.Add(stateLabel);
            connectionPanel.Controls.Add(readRegistryButton);
            connectionPanel.Controls.Add(disconnectButton);
            connectionPanel.Controls.Add(connectButton);
            connectionPanel.Dock = DockStyle.Top;
            connectionPanel.Location = new Point(0, 0);
            connectionPanel.Name = "connectionPanel";
            connectionPanel.Size = new Size(900, 214);
            connectionPanel.TabIndex = 0;
            //
            // connectButton
            //
            connectButton.Location = new Point(12, 12);
            connectButton.Name = "connectButton";
            connectButton.Size = new Size(130, 32);
            connectButton.TabIndex = 0;
            connectButton.Text = "Connect";
            connectButton.UseVisualStyleBackColor = true;
            connectButton.Click += connectButton_Click;
            //
            // disconnectButton
            //
            disconnectButton.Location = new Point(148, 12);
            disconnectButton.Name = "disconnectButton";
            disconnectButton.Size = new Size(130, 32);
            disconnectButton.TabIndex = 1;
            disconnectButton.Text = "Disconnect";
            disconnectButton.UseVisualStyleBackColor = true;
            disconnectButton.Click += disconnectButton_Click;
            //
            // readRegistryButton
            //
            readRegistryButton.Location = new Point(284, 12);
            readRegistryButton.Name = "readRegistryButton";
            readRegistryButton.Size = new Size(170, 32);
            readRegistryButton.TabIndex = 2;
            readRegistryButton.Text = "Read Variable Table";
            readRegistryButton.UseVisualStyleBackColor = true;
            readRegistryButton.Click += readRegistryButton_Click;
            //
            // stateLabel
            //
            stateLabel.AutoSize = true;
            stateLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            stateLabel.Location = new Point(470, 19);
            stateLabel.Name = "stateLabel";
            stateLabel.Size = new Size(90, 15);
            stateLabel.TabIndex = 3;
            stateLabel.Text = "Not connected";
            //
            // noteLabel
            //
            noteLabel.AutoSize = true;
            noteLabel.ForeColor = SystemColors.GrayText;
            noteLabel.Location = new Point(12, 50);
            noteLabel.Name = "noteLabel";
            noteLabel.Size = new Size(300, 15);
            noteLabel.TabIndex = 4;
            noteLabel.Text = "";
            //
            // infoListView
            //
            infoListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            infoListView.FullRowSelect = true;
            infoListView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            infoListView.Location = new Point(12, 72);
            infoListView.Name = "infoListView";
            infoListView.Size = new Size(876, 136);
            infoListView.TabIndex = 5;
            infoListView.UseCompatibleStateImageBehavior = false;
            infoListView.View = View.Details;
            //
            // memoryPanel
            //
            memoryPanel.Controls.Add(progressBar);
            memoryPanel.Controls.Add(cancelButton);
            memoryPanel.Controls.Add(diffButton);
            memoryPanel.Controls.Add(compareButton);
            memoryPanel.Controls.Add(checksumButton);
            memoryPanel.Controls.Add(saveButton);
            memoryPanel.Controls.Add(readButton);
            memoryPanel.Controls.Add(calibrationRangeButton);
            memoryPanel.Controls.Add(lengthUpDown);
            memoryPanel.Controls.Add(lengthLabel);
            memoryPanel.Controls.Add(addressTextBox);
            memoryPanel.Controls.Add(addressLabel);
            memoryPanel.Dock = DockStyle.Top;
            memoryPanel.Location = new Point(0, 0);
            memoryPanel.Name = "memoryPanel";
            memoryPanel.Size = new Size(900, 92);
            memoryPanel.TabIndex = 1;
            //
            // addressLabel
            //
            addressLabel.AutoSize = true;
            addressLabel.Location = new Point(12, 13);
            addressLabel.Name = "addressLabel";
            addressLabel.Size = new Size(77, 15);
            addressLabel.TabIndex = 0;
            addressLabel.Text = "Address (hex)";
            //
            // addressTextBox
            //
            addressTextBox.Font = new Font("Consolas", 9.75F);
            addressTextBox.Location = new Point(100, 9);
            addressTextBox.Name = "addressTextBox";
            addressTextBox.Size = new Size(110, 23);
            addressTextBox.TabIndex = 1;
            addressTextBox.Text = "0x40000000";
            //
            // lengthLabel
            //
            lengthLabel.AutoSize = true;
            lengthLabel.Location = new Point(226, 13);
            lengthLabel.Name = "lengthLabel";
            lengthLabel.Size = new Size(83, 15);
            lengthLabel.TabIndex = 2;
            lengthLabel.Text = "Length (bytes)";
            //
            // lengthUpDown
            //
            lengthUpDown.Location = new Point(316, 9);
            lengthUpDown.Maximum = new decimal(new int[] { 1048576, 0, 0, 0 });
            lengthUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            lengthUpDown.Name = "lengthUpDown";
            lengthUpDown.Size = new Size(100, 23);
            lengthUpDown.TabIndex = 3;
            lengthUpDown.ThousandsSeparator = true;
            lengthUpDown.Value = new decimal(new int[] { 256, 0, 0, 0 });
            //
            // calibrationRangeButton
            //
            calibrationRangeButton.Location = new Point(430, 6);
            calibrationRangeButton.Name = "calibrationRangeButton";
            calibrationRangeButton.Size = new Size(150, 29);
            calibrationRangeButton.TabIndex = 4;
            calibrationRangeButton.Text = "Calibration Region";
            calibrationRangeButton.UseVisualStyleBackColor = true;
            calibrationRangeButton.Click += calibrationRangeButton_Click;
            //
            // readButton
            //
            readButton.Location = new Point(12, 46);
            readButton.Name = "readButton";
            readButton.Size = new Size(110, 32);
            readButton.TabIndex = 5;
            readButton.Text = "Read";
            readButton.UseVisualStyleBackColor = true;
            readButton.Click += readButton_Click;
            //
            // saveButton
            //
            saveButton.Location = new Point(128, 46);
            saveButton.Name = "saveButton";
            saveButton.Size = new Size(120, 32);
            saveButton.TabIndex = 6;
            saveButton.Text = "Save .bin…";
            saveButton.UseVisualStyleBackColor = true;
            saveButton.Click += saveButton_Click;
            //
            // checksumButton
            //
            checksumButton.Location = new Point(254, 46);
            checksumButton.Name = "checksumButton";
            checksumButton.Size = new Size(110, 32);
            checksumButton.TabIndex = 7;
            checksumButton.Text = "Checksum";
            checksumButton.UseVisualStyleBackColor = true;
            checksumButton.Click += checksumButton_Click;
            //
            // compareButton
            //
            compareButton.Location = new Point(370, 46);
            compareButton.Name = "compareButton";
            compareButton.Size = new Size(160, 32);
            compareButton.TabIndex = 8;
            compareButton.Text = "Compare with File…";
            compareButton.UseVisualStyleBackColor = true;
            compareButton.Click += compareButton_Click;
            //
            // diffButton
            //
            diffButton.Location = new Point(536, 46);
            diffButton.Name = "diffButton";
            diffButton.Size = new Size(130, 32);
            diffButton.TabIndex = 9;
            diffButton.Text = "Diff with File…";
            diffButton.UseVisualStyleBackColor = true;
            diffButton.Click += diffButton_Click;
            //
            // cancelButton
            //
            cancelButton.Location = new Point(672, 46);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(90, 32);
            cancelButton.TabIndex = 10;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            cancelButton.Click += cancelButton_Click;
            //
            // progressBar
            //
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Location = new Point(770, 52);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(110, 20);
            progressBar.TabIndex = 11;
            //
            // writePanel
            //
            writePanel.Controls.Add(writeNoteLabel);
            writePanel.Controls.Add(restoreButton);
            writePanel.Controls.Add(writeFileButton);
            writePanel.Controls.Add(writeValueButton);
            writePanel.Controls.Add(bytesTextBox);
            writePanel.Controls.Add(bytesLabel);
            writePanel.Controls.Add(buildComboBox);
            writePanel.Controls.Add(buildLabel);
            writePanel.Controls.Add(writeTitleLabel);
            writePanel.Dock = DockStyle.Top;
            writePanel.Location = new Point(0, 92);
            writePanel.Name = "writePanel";
            writePanel.Size = new Size(900, 104);
            writePanel.TabIndex = 2;
            //
            // writeTitleLabel
            //
            writeTitleLabel.AutoSize = true;
            writeTitleLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            writeTitleLabel.Location = new Point(12, 6);
            writeTitleLabel.Name = "writeTitleLabel";
            writeTitleLabel.Size = new Size(104, 15);
            writeTitleLabel.TabIndex = 0;
            writeTitleLabel.Text = "Calibration write";
            //
            // buildLabel
            //
            buildLabel.AutoSize = true;
            buildLabel.Location = new Point(12, 33);
            buildLabel.Name = "buildLabel";
            buildLabel.Size = new Size(34, 15);
            buildLabel.TabIndex = 1;
            buildLabel.Text = "Build";
            //
            // buildComboBox
            //
            buildComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            buildComboBox.Location = new Point(100, 29);
            buildComboBox.Name = "buildComboBox";
            buildComboBox.Size = new Size(260, 23);
            buildComboBox.TabIndex = 2;
            buildComboBox.SelectionChangeCommitted += buildComboBox_SelectionChangeCommitted;
            //
            // bytesLabel
            //
            bytesLabel.AutoSize = true;
            bytesLabel.Location = new Point(376, 33);
            bytesLabel.Name = "bytesLabel";
            bytesLabel.Size = new Size(66, 15);
            bytesLabel.TabIndex = 3;
            bytesLabel.Text = "Bytes (hex)";
            //
            // bytesTextBox
            //
            bytesTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bytesTextBox.Font = new Font("Consolas", 9.75F);
            bytesTextBox.Location = new Point(450, 29);
            bytesTextBox.Name = "bytesTextBox";
            bytesTextBox.PlaceholderText = "e.g. 01 2C";
            bytesTextBox.Size = new Size(438, 23);
            bytesTextBox.TabIndex = 4;
            //
            // writeValueButton
            //
            writeValueButton.Location = new Point(12, 60);
            writeValueButton.Name = "writeValueButton";
            writeValueButton.Size = new Size(190, 32);
            writeValueButton.TabIndex = 5;
            writeValueButton.Text = "Write Bytes at Address";
            writeValueButton.UseVisualStyleBackColor = true;
            writeValueButton.Click += writeValueButton_Click;
            //
            // writeFileButton
            //
            writeFileButton.Location = new Point(208, 60);
            writeFileButton.Name = "writeFileButton";
            writeFileButton.Size = new Size(190, 32);
            writeFileButton.TabIndex = 6;
            writeFileButton.Text = "Write Calibration File…";
            writeFileButton.UseVisualStyleBackColor = true;
            writeFileButton.Click += writeFileButton_Click;
            //
            // restoreButton
            //
            restoreButton.Location = new Point(404, 60);
            restoreButton.Name = "restoreButton";
            restoreButton.Size = new Size(160, 32);
            restoreButton.TabIndex = 7;
            restoreButton.Text = "Restore Backup…";
            restoreButton.UseVisualStyleBackColor = true;
            restoreButton.Click += restoreButton_Click;
            //
            // writeNoteLabel
            //
            writeNoteLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            writeNoteLabel.ForeColor = SystemColors.GrayText;
            writeNoteLabel.Location = new Point(576, 62);
            writeNoteLabel.Name = "writeNoteLabel";
            writeNoteLabel.Size = new Size(312, 32);
            writeNoteLabel.TabIndex = 8;
            writeNoteLabel.Text = "";
            //
            // subTabs
            //
            subTabs.Controls.Add(memoryPage);
            subTabs.Controls.Add(variablesPage);
            subTabs.Controls.Add(trafficPage);
            subTabs.Controls.Add(selfTestPage);
            subTabs.Dock = DockStyle.Fill;
            subTabs.Location = new Point(0, 214);
            subTabs.Name = "subTabs";
            subTabs.SelectedIndex = 0;
            subTabs.Size = new Size(900, 376);
            subTabs.TabIndex = 1;
            //
            // memoryPage
            //
            memoryPage.Controls.Add(hexTextBox);
            memoryPage.Controls.Add(writePanel);
            memoryPage.Controls.Add(memoryPanel);
            memoryPage.Location = new Point(4, 24);
            memoryPage.Name = "memoryPage";
            memoryPage.Size = new Size(892, 348);
            memoryPage.TabIndex = 0;
            memoryPage.Text = "Memory";
            memoryPage.UseVisualStyleBackColor = true;
            //
            // variablesPage
            //
            variablesPage.Location = new Point(4, 24);
            variablesPage.Name = "variablesPage";
            variablesPage.Size = new Size(892, 348);
            variablesPage.TabIndex = 1;
            variablesPage.Text = "Variables";
            variablesPage.UseVisualStyleBackColor = true;
            //
            // trafficPage
            //
            trafficPage.Location = new Point(4, 24);
            trafficPage.Name = "trafficPage";
            trafficPage.Size = new Size(892, 348);
            trafficPage.TabIndex = 2;
            trafficPage.Text = "Traffic";
            trafficPage.UseVisualStyleBackColor = true;
            //
            // selfTestPage
            //
            selfTestPage.Location = new Point(4, 24);
            selfTestPage.Name = "selfTestPage";
            selfTestPage.Size = new Size(892, 348);
            selfTestPage.TabIndex = 3;
            selfTestPage.Text = "Self-Test";
            selfTestPage.UseVisualStyleBackColor = true;
            //
            // hexTextBox
            //
            hexTextBox.Dock = DockStyle.Fill;
            hexTextBox.Font = new Font("Consolas", 9.75F);
            hexTextBox.Location = new Point(0, 0);
            hexTextBox.Multiline = true;
            hexTextBox.Name = "hexTextBox";
            hexTextBox.ReadOnly = true;
            hexTextBox.ScrollBars = ScrollBars.Both;
            hexTextBox.Size = new Size(900, 240);
            hexTextBox.TabIndex = 0;
            hexTextBox.WordWrap = false;
            //
            // logTextBox
            //
            logTextBox.Dock = DockStyle.Bottom;
            logTextBox.Location = new Point(0, 590);
            logTextBox.Multiline = true;
            logTextBox.Name = "logTextBox";
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.Size = new Size(900, 110);
            logTextBox.TabIndex = 2;
            //
            // CcpControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(subTabs);
            Controls.Add(logTextBox);
            Controls.Add(connectionPanel);
            Name = "CcpControl";
            Size = new Size(900, 700);
            connectionPanel.ResumeLayout(false);
            connectionPanel.PerformLayout();
            memoryPanel.ResumeLayout(false);
            memoryPanel.PerformLayout();
            writePanel.ResumeLayout(false);
            writePanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)lengthUpDown).EndInit();
            memoryPage.ResumeLayout(false);
            memoryPage.PerformLayout();
            subTabs.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Panel connectionPanel;
        private Button connectButton;
        private Button disconnectButton;
        private Button readRegistryButton;
        private Label stateLabel;
        private Label noteLabel;
        private ListView infoListView;
        private Panel memoryPanel;
        private Label addressLabel;
        private TextBox addressTextBox;
        private Label lengthLabel;
        private NumericUpDown lengthUpDown;
        private Button calibrationRangeButton;
        private Button readButton;
        private Button saveButton;
        private Button checksumButton;
        private Button compareButton;
        private Button diffButton;
        private Button cancelButton;
        private ProgressBar progressBar;
        private Panel writePanel;
        private Label writeTitleLabel;
        private Label buildLabel;
        private ComboBox buildComboBox;
        private Label bytesLabel;
        private TextBox bytesTextBox;
        private Button writeValueButton;
        private Button writeFileButton;
        private Button restoreButton;
        private Label writeNoteLabel;
        private TabControl subTabs;
        private TabPage memoryPage;
        private TabPage variablesPage;
        private TabPage trafficPage;
        private TabPage selfTestPage;
        private TextBox hexTextBox;
        private TextBox logTextBox;
    }
}
