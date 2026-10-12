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
            cancelButton = new Button();
            progressBar = new ProgressBar();
            outputSplit = new SplitContainer();
            hexTextBox = new TextBox();
            logTextBox = new TextBox();
            connectionPanel.SuspendLayout();
            memoryPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)lengthUpDown).BeginInit();
            ((System.ComponentModel.ISupportInitialize)outputSplit).BeginInit();
            outputSplit.Panel1.SuspendLayout();
            outputSplit.Panel2.SuspendLayout();
            outputSplit.SuspendLayout();
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
            connectionPanel.Size = new Size(900, 250);
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
            infoListView.Size = new Size(876, 172);
            infoListView.TabIndex = 5;
            infoListView.UseCompatibleStateImageBehavior = false;
            infoListView.View = View.Details;
            //
            // memoryPanel
            //
            memoryPanel.Controls.Add(progressBar);
            memoryPanel.Controls.Add(cancelButton);
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
            memoryPanel.Location = new Point(0, 250);
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
            checksumButton.Size = new Size(120, 32);
            checksumButton.TabIndex = 7;
            checksumButton.Text = "Checksum";
            checksumButton.UseVisualStyleBackColor = true;
            checksumButton.Click += checksumButton_Click;
            //
            // compareButton
            //
            compareButton.Location = new Point(380, 46);
            compareButton.Name = "compareButton";
            compareButton.Size = new Size(160, 32);
            compareButton.TabIndex = 8;
            compareButton.Text = "Compare with File…";
            compareButton.UseVisualStyleBackColor = true;
            compareButton.Click += compareButton_Click;
            //
            // cancelButton
            //
            cancelButton.Location = new Point(546, 46);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(90, 32);
            cancelButton.TabIndex = 9;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            cancelButton.Click += cancelButton_Click;
            //
            // progressBar
            //
            progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progressBar.Location = new Point(650, 52);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(238, 20);
            progressBar.TabIndex = 10;
            //
            // outputSplit
            //
            outputSplit.Dock = DockStyle.Fill;
            outputSplit.Location = new Point(0, 342);
            outputSplit.Name = "outputSplit";
            outputSplit.Orientation = Orientation.Horizontal;
            //
            // outputSplit.Panel1
            //
            outputSplit.Panel1.Controls.Add(hexTextBox);
            //
            // outputSplit.Panel2
            //
            outputSplit.Panel2.Controls.Add(logTextBox);
            outputSplit.Size = new Size(900, 358);
            outputSplit.SplitterDistance = 240;
            outputSplit.TabIndex = 2;
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
            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Location = new Point(0, 0);
            logTextBox.Multiline = true;
            logTextBox.Name = "logTextBox";
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.Size = new Size(900, 114);
            logTextBox.TabIndex = 0;
            //
            // CcpControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(outputSplit);
            Controls.Add(memoryPanel);
            Controls.Add(connectionPanel);
            Name = "CcpControl";
            Size = new Size(900, 700);
            connectionPanel.ResumeLayout(false);
            connectionPanel.PerformLayout();
            memoryPanel.ResumeLayout(false);
            memoryPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)lengthUpDown).EndInit();
            outputSplit.Panel1.ResumeLayout(false);
            outputSplit.Panel1.PerformLayout();
            outputSplit.Panel2.ResumeLayout(false);
            outputSplit.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)outputSplit).EndInit();
            outputSplit.ResumeLayout(false);
            ResumeLayout(false);
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
        private Button cancelButton;
        private ProgressBar progressBar;
        private SplitContainer outputSplit;
        private TextBox hexTextBox;
        private TextBox logTextBox;
    }
}
