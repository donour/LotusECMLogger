namespace LotusECMLogger.Controls
{
    partial class SetVinDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            layout = new TableLayoutPanel();
            wmiLabel = new Label();
            wmiTextBox = new TextBox();
            vinSuffixLabel = new Label();
            vinSuffixTextBox = new TextBox();
            statusLabel = new Label();
            rulesLabel = new Label();
            warningLabel = new Label();
            buttonPanel = new FlowLayoutPanel();
            programButton = new Button();
            cancelButton = new Button();
            layout.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // layout
            //
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(wmiLabel, 0, 0);
            layout.Controls.Add(wmiTextBox, 1, 0);
            layout.Controls.Add(vinSuffixLabel, 0, 1);
            layout.Controls.Add(vinSuffixTextBox, 1, 1);
            layout.Controls.Add(statusLabel, 1, 2);
            layout.Controls.Add(rulesLabel, 0, 3);
            layout.SetColumnSpan(rulesLabel, 2);
            layout.Controls.Add(warningLabel, 0, 4);
            layout.SetColumnSpan(warningLabel, 2);
            layout.Controls.Add(buttonPanel, 1, 5);
            layout.Dock = DockStyle.Fill;
            layout.Name = "layout";
            layout.Padding = new Padding(12);
            layout.RowCount = 6;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            layout.TabIndex = 0;
            //
            // wmiLabel
            //
            wmiLabel.Dock = DockStyle.Fill;
            wmiLabel.Name = "wmiLabel";
            wmiLabel.TabIndex = 0;
            wmiLabel.Text = "Manufacturer (WMI)";
            wmiLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // wmiTextBox
            //
            wmiTextBox.BackColor = SystemColors.Control;
            wmiTextBox.Font = new Font("Consolas", 11F);
            wmiTextBox.MaxLength = 3;
            wmiTextBox.Name = "wmiTextBox";
            wmiTextBox.ReadOnly = true;
            wmiTextBox.Size = new Size(60, 30);
            wmiTextBox.TabIndex = 1;
            wmiTextBox.TabStop = false;
            //
            // vinSuffixLabel
            //
            vinSuffixLabel.Dock = DockStyle.Fill;
            vinSuffixLabel.Name = "vinSuffixLabel";
            vinSuffixLabel.TabIndex = 2;
            vinSuffixLabel.Text = "VIN remainder (14 chars)";
            vinSuffixLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // vinSuffixTextBox
            //
            vinSuffixTextBox.CharacterCasing = CharacterCasing.Upper;
            vinSuffixTextBox.Dock = DockStyle.Fill;
            vinSuffixTextBox.Font = new Font("Consolas", 11F);
            vinSuffixTextBox.MaxLength = 14;
            vinSuffixTextBox.Name = "vinSuffixTextBox";
            vinSuffixTextBox.TabIndex = 3;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Name = "statusLabel";
            statusLabel.TabIndex = 4;
            statusLabel.Text = "";
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // rulesLabel
            //
            rulesLabel.Dock = DockStyle.Fill;
            rulesLabel.ForeColor = SystemColors.GrayText;
            rulesLabel.Name = "rulesLabel";
            rulesLabel.TabIndex = 5;
            rulesLabel.Text = "";
            rulesLabel.TextAlign = ContentAlignment.TopLeft;
            //
            // warningLabel
            //
            warningLabel.Dock = DockStyle.Fill;
            warningLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            warningLabel.ForeColor = Color.FromArgb(176, 0, 0);
            warningLabel.Name = "warningLabel";
            warningLabel.TabIndex = 6;
            warningLabel.Text = "";
            warningLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(programButton);
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Margin = new Padding(0, 12, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Padding = new Padding(0);
            buttonPanel.TabIndex = 7;
            buttonPanel.WrapContents = false;
            //
            // cancelButton
            //
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Margin = new Padding(8, 0, 0, 0);
            cancelButton.Name = "cancelButton";
            cancelButton.TabIndex = 0;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // programButton
            //
            programButton.AutoSize = true;
            programButton.Name = "programButton";
            programButton.TabIndex = 1;
            programButton.Text = "Program";
            programButton.UseVisualStyleBackColor = true;
            programButton.Click += ProgramButton_Click;
            //
            // SetVinDialog
            //
            CancelButton = cancelButton;
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(640, 390);
            Controls.Add(layout);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(640, 390);
            Name = "SetVinDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Set VIN";
            layout.ResumeLayout(false);
            layout.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel layout;
        private Label wmiLabel;
        private TextBox wmiTextBox;
        private Label vinSuffixLabel;
        private TextBox vinSuffixTextBox;
        private Label statusLabel;
        private Label rulesLabel;
        private Label warningLabel;
        private FlowLayoutPanel buttonPanel;
        private Button programButton;
        private Button cancelButton;
    }
}
