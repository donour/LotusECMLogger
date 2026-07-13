namespace LotusECMLogger.Controls
{
    partial class EngineeringAccessControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            layoutPanel = new TableLayoutPanel();
            configGroupBox = new GroupBox();
            configLayout = new TableLayoutPanel();
            profileLabel = new Label();
            profileCombo = new ComboBox();
            buttonPanel = new FlowLayoutPanel();
            runButton = new Button();
            statusPanel = new FlowLayoutPanel();
            statusLabel = new Label();
            statusValueLabel = new Label();
            writeGroupBox = new GroupBox();
            writeLayout = new TableLayoutPanel();
            enableWritesCheck = new CheckBox();
            transportLabel = new Label();
            transportCombo = new ComboBox();
            writeAddressLabel = new Label();
            writeAddressTextBox = new TextBox();
            writeBytesLabel = new Label();
            writeBytesTextBox = new TextBox();
            writeButtonPanel = new FlowLayoutPanel();
            writeButton = new Button();
            applyMagicButton = new Button();
            resultGroupBox = new GroupBox();
            resultTextBox = new TextBox();
            layoutPanel.SuspendLayout();
            configGroupBox.SuspendLayout();
            configLayout.SuspendLayout();
            buttonPanel.SuspendLayout();
            statusPanel.SuspendLayout();
            writeGroupBox.SuspendLayout();
            writeLayout.SuspendLayout();
            writeButtonPanel.SuspendLayout();
            resultGroupBox.SuspendLayout();
            SuspendLayout();
            //
            // layoutPanel
            //
            layoutPanel.ColumnCount = 1;
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.Controls.Add(configGroupBox, 0, 0);
            layoutPanel.Controls.Add(writeGroupBox, 0, 1);
            layoutPanel.Controls.Add(resultGroupBox, 0, 2);
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
            configGroupBox.Size = new Size(1107, 170);
            configGroupBox.TabIndex = 0;
            configGroupBox.TabStop = false;
            configGroupBox.Text = "Engineering Access Probe (read-only)";
            //
            // configLayout
            //
            configLayout.AutoSize = true;
            configLayout.ColumnCount = 2;
            configLayout.ColumnStyles.Add(new ColumnStyle());
            configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            configLayout.Controls.Add(profileLabel, 0, 0);
            configLayout.Controls.Add(profileCombo, 1, 0);
            configLayout.Controls.Add(buttonPanel, 1, 1);
            configLayout.Controls.Add(statusPanel, 1, 2);
            configLayout.Dock = DockStyle.Fill;
            configLayout.Location = new Point(14, 41);
            configLayout.Margin = new Padding(4, 5, 4, 5);
            configLayout.Name = "configLayout";
            configLayout.RowCount = 3;
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.RowStyles.Add(new RowStyle());
            configLayout.Size = new Size(1079, 112);
            configLayout.TabIndex = 0;
            //
            // profileLabel
            //
            profileLabel.Anchor = AnchorStyles.Left;
            profileLabel.AutoSize = true;
            profileLabel.Location = new Point(4, 8);
            profileLabel.Margin = new Padding(4, 0, 4, 0);
            profileLabel.Name = "profileLabel";
            profileLabel.Size = new Size(100, 25);
            profileLabel.TabIndex = 0;
            profileLabel.Text = "ECU Profile:";
            //
            // profileCombo
            //
            profileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            profileCombo.Location = new Point(207, 5);
            profileCombo.Margin = new Padding(4, 5, 4, 5);
            profileCombo.Name = "profileCombo";
            profileCombo.Size = new Size(400, 33);
            profileCombo.TabIndex = 1;
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(runButton);
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.Location = new Point(203, 48);
            buttonPanel.Margin = new Padding(0, 5, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Size = new Size(876, 45);
            buttonPanel.TabIndex = 2;
            buttonPanel.WrapContents = false;
            //
            // runButton
            //
            runButton.AutoSize = true;
            runButton.Location = new Point(4, 5);
            runButton.Margin = new Padding(4, 5, 4, 5);
            runButton.Name = "runButton";
            runButton.Size = new Size(184, 35);
            runButton.TabIndex = 0;
            runButton.Text = "Run Probe";
            runButton.UseVisualStyleBackColor = true;
            runButton.Click += RunButton_Click;
            //
            // statusPanel
            //
            statusPanel.AutoSize = true;
            statusPanel.Controls.Add(statusLabel);
            statusPanel.Controls.Add(statusValueLabel);
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.Location = new Point(203, 98);
            statusPanel.Margin = new Padding(0, 5, 0, 0);
            statusPanel.Name = "statusPanel";
            statusPanel.Size = new Size(876, 39);
            statusPanel.TabIndex = 3;
            statusPanel.WrapContents = false;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            statusLabel.Location = new Point(4, 0);
            statusLabel.Margin = new Padding(4, 7, 4, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(70, 25);
            statusLabel.TabIndex = 0;
            statusLabel.Text = "Status:";
            //
            // statusValueLabel
            //
            statusValueLabel.AutoSize = true;
            statusValueLabel.Location = new Point(82, 0);
            statusValueLabel.Margin = new Padding(4, 7, 4, 0);
            statusValueLabel.Name = "statusValueLabel";
            statusValueLabel.Size = new Size(41, 25);
            statusValueLabel.TabIndex = 1;
            statusValueLabel.Text = "Idle";
            //
            // writeGroupBox
            //
            writeGroupBox.AutoSize = true;
            writeGroupBox.Controls.Add(writeLayout);
            writeGroupBox.Dock = DockStyle.Fill;
            writeGroupBox.ForeColor = Color.Firebrick;
            writeGroupBox.Location = new Point(4, 185);
            writeGroupBox.Margin = new Padding(4, 5, 4, 5);
            writeGroupBox.Name = "writeGroupBox";
            writeGroupBox.Padding = new Padding(14, 17, 14, 17);
            writeGroupBox.Size = new Size(1107, 230);
            writeGroupBox.TabIndex = 1;
            writeGroupBox.TabStop = false;
            writeGroupBox.Text = "Live Memory Write (advanced — modifies the ECU)";
            //
            // writeLayout
            //
            writeLayout.AutoSize = true;
            writeLayout.ColumnCount = 2;
            writeLayout.ColumnStyles.Add(new ColumnStyle());
            writeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            writeLayout.Controls.Add(enableWritesCheck, 0, 0);
            writeLayout.Controls.Add(transportLabel, 0, 1);
            writeLayout.Controls.Add(transportCombo, 1, 1);
            writeLayout.Controls.Add(writeAddressLabel, 0, 2);
            writeLayout.Controls.Add(writeAddressTextBox, 1, 2);
            writeLayout.Controls.Add(writeBytesLabel, 0, 3);
            writeLayout.Controls.Add(writeBytesTextBox, 1, 3);
            writeLayout.Controls.Add(writeButtonPanel, 1, 4);
            writeLayout.Dock = DockStyle.Fill;
            writeLayout.Location = new Point(14, 41);
            writeLayout.Margin = new Padding(4, 5, 4, 5);
            writeLayout.Name = "writeLayout";
            writeLayout.RowCount = 5;
            writeLayout.RowStyles.Add(new RowStyle());
            writeLayout.RowStyles.Add(new RowStyle());
            writeLayout.RowStyles.Add(new RowStyle());
            writeLayout.RowStyles.Add(new RowStyle());
            writeLayout.RowStyles.Add(new RowStyle());
            writeLayout.Size = new Size(1079, 172);
            writeLayout.TabIndex = 0;
            //
            // enableWritesCheck
            //
            enableWritesCheck.AutoSize = true;
            writeLayout.SetColumnSpan(enableWritesCheck, 2);
            enableWritesCheck.ForeColor = SystemColors.ControlText;
            enableWritesCheck.Location = new Point(4, 5);
            enableWritesCheck.Margin = new Padding(4, 5, 4, 5);
            enableWritesCheck.Name = "enableWritesCheck";
            enableWritesCheck.Size = new Size(400, 29);
            enableWritesCheck.TabIndex = 0;
            enableWritesCheck.Text = "Enable writes (I understand this modifies the ECU)";
            enableWritesCheck.UseVisualStyleBackColor = true;
            //
            // transportLabel
            //
            transportLabel.Anchor = AnchorStyles.Left;
            transportLabel.AutoSize = true;
            transportLabel.ForeColor = SystemColors.ControlText;
            transportLabel.Location = new Point(4, 47);
            transportLabel.Margin = new Padding(4, 0, 4, 0);
            transportLabel.Name = "transportLabel";
            transportLabel.Size = new Size(90, 25);
            transportLabel.TabIndex = 1;
            transportLabel.Text = "Transport:";
            //
            // transportCombo
            //
            transportCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            transportCombo.Location = new Point(207, 44);
            transportCombo.Margin = new Padding(4, 5, 4, 5);
            transportCombo.Name = "transportCombo";
            transportCombo.Size = new Size(400, 33);
            transportCombo.TabIndex = 2;
            //
            // writeAddressLabel
            //
            writeAddressLabel.Anchor = AnchorStyles.Left;
            writeAddressLabel.AutoSize = true;
            writeAddressLabel.ForeColor = SystemColors.ControlText;
            writeAddressLabel.Location = new Point(4, 88);
            writeAddressLabel.Margin = new Padding(4, 0, 4, 0);
            writeAddressLabel.Name = "writeAddressLabel";
            writeAddressLabel.Size = new Size(150, 25);
            writeAddressLabel.TabIndex = 3;
            writeAddressLabel.Text = "Address (hex):";
            //
            // writeAddressTextBox
            //
            writeAddressTextBox.Location = new Point(207, 85);
            writeAddressTextBox.Margin = new Padding(4, 5, 4, 5);
            writeAddressTextBox.Name = "writeAddressTextBox";
            writeAddressTextBox.Size = new Size(284, 31);
            writeAddressTextBox.TabIndex = 4;
            writeAddressTextBox.Text = "0x40000000";
            //
            // writeBytesLabel
            //
            writeBytesLabel.Anchor = AnchorStyles.Left;
            writeBytesLabel.AutoSize = true;
            writeBytesLabel.ForeColor = SystemColors.ControlText;
            writeBytesLabel.Location = new Point(4, 129);
            writeBytesLabel.Margin = new Padding(4, 0, 4, 0);
            writeBytesLabel.Name = "writeBytesLabel";
            writeBytesLabel.Size = new Size(150, 25);
            writeBytesLabel.TabIndex = 5;
            writeBytesLabel.Text = "Bytes (hex):";
            //
            // writeBytesTextBox
            //
            writeBytesTextBox.Location = new Point(207, 126);
            writeBytesTextBox.Margin = new Padding(4, 5, 4, 5);
            writeBytesTextBox.Name = "writeBytesTextBox";
            writeBytesTextBox.PlaceholderText = "e.g. 0D B8 45 D4";
            writeBytesTextBox.Size = new Size(400, 31);
            writeBytesTextBox.TabIndex = 6;
            //
            // writeButtonPanel
            //
            writeButtonPanel.AutoSize = true;
            writeButtonPanel.Controls.Add(writeButton);
            writeButtonPanel.Controls.Add(applyMagicButton);
            writeButtonPanel.Dock = DockStyle.Fill;
            writeButtonPanel.Location = new Point(203, 167);
            writeButtonPanel.Margin = new Padding(0, 5, 0, 0);
            writeButtonPanel.Name = "writeButtonPanel";
            writeButtonPanel.Size = new Size(876, 45);
            writeButtonPanel.TabIndex = 7;
            writeButtonPanel.WrapContents = false;
            //
            // writeButton
            //
            writeButton.AutoSize = true;
            writeButton.ForeColor = SystemColors.ControlText;
            writeButton.Location = new Point(4, 5);
            writeButton.Margin = new Padding(4, 5, 4, 5);
            writeButton.Name = "writeButton";
            writeButton.Size = new Size(184, 35);
            writeButton.TabIndex = 0;
            writeButton.Text = "Write Bytes";
            writeButton.UseVisualStyleBackColor = true;
            writeButton.Click += WriteButton_Click;
            //
            // applyMagicButton
            //
            applyMagicButton.AutoSize = true;
            applyMagicButton.ForeColor = SystemColors.ControlText;
            applyMagicButton.Location = new Point(196, 5);
            applyMagicButton.Margin = new Padding(4, 5, 4, 5);
            applyMagicButton.Name = "applyMagicButton";
            applyMagicButton.Size = new Size(260, 35);
            applyMagicButton.TabIndex = 1;
            applyMagicButton.Text = "Apply Calibration Magic";
            applyMagicButton.UseVisualStyleBackColor = true;
            applyMagicButton.Click += ApplyMagicButton_Click;
            //
            // resultGroupBox
            //
            resultGroupBox.Controls.Add(resultTextBox);
            resultGroupBox.Dock = DockStyle.Fill;
            resultGroupBox.Location = new Point(4, 425);
            resultGroupBox.Margin = new Padding(4, 10, 4, 5);
            resultGroupBox.Name = "resultGroupBox";
            resultGroupBox.Padding = new Padding(14, 17, 14, 17);
            resultGroupBox.Size = new Size(1107, 536);
            resultGroupBox.TabIndex = 2;
            resultGroupBox.TabStop = false;
            resultGroupBox.Text = "Result";
            //
            // resultTextBox
            //
            resultTextBox.Dock = DockStyle.Fill;
            resultTextBox.Font = new Font("Consolas", 10F);
            resultTextBox.Location = new Point(14, 41);
            resultTextBox.Margin = new Padding(4, 5, 4, 5);
            resultTextBox.Multiline = true;
            resultTextBox.Name = "resultTextBox";
            resultTextBox.ReadOnly = true;
            resultTextBox.ScrollBars = ScrollBars.Both;
            resultTextBox.Size = new Size(1079, 478);
            resultTextBox.TabIndex = 0;
            resultTextBox.Text = "No probe run yet.";
            resultTextBox.WordWrap = false;
            //
            // EngineeringAccessControl
            //
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(layoutPanel);
            Margin = new Padding(4, 5, 4, 5);
            Name = "EngineeringAccessControl";
            Padding = new Padding(14, 17, 14, 17);
            Size = new Size(1143, 1000);
            layoutPanel.ResumeLayout(false);
            layoutPanel.PerformLayout();
            configGroupBox.ResumeLayout(false);
            configGroupBox.PerformLayout();
            configLayout.ResumeLayout(false);
            configLayout.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            statusPanel.ResumeLayout(false);
            statusPanel.PerformLayout();
            writeGroupBox.ResumeLayout(false);
            writeGroupBox.PerformLayout();
            writeLayout.ResumeLayout(false);
            writeLayout.PerformLayout();
            writeButtonPanel.ResumeLayout(false);
            writeButtonPanel.PerformLayout();
            resultGroupBox.ResumeLayout(false);
            resultGroupBox.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TableLayoutPanel layoutPanel;
        private GroupBox configGroupBox;
        private TableLayoutPanel configLayout;
        private Label profileLabel;
        private ComboBox profileCombo;
        private FlowLayoutPanel buttonPanel;
        private Button runButton;
        private FlowLayoutPanel statusPanel;
        private Label statusLabel;
        private Label statusValueLabel;
        private GroupBox writeGroupBox;
        private TableLayoutPanel writeLayout;
        private CheckBox enableWritesCheck;
        private Label transportLabel;
        private ComboBox transportCombo;
        private Label writeAddressLabel;
        private TextBox writeAddressTextBox;
        private Label writeBytesLabel;
        private TextBox writeBytesTextBox;
        private FlowLayoutPanel writeButtonPanel;
        private Button writeButton;
        private Button applyMagicButton;
        private GroupBox resultGroupBox;
        private TextBox resultTextBox;
    }
}
