namespace LotusECMLogger.Controls
{
    partial class SetVinDialog
    {
        private System.ComponentModel.IContainer? components = null;

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
            vinLabel = new Label();
            vinTextBox = new TextBox();
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
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(vinLabel, 0, 0);
            layout.Controls.Add(vinTextBox, 1, 0);
            layout.Controls.Add(statusLabel, 1, 1);
            layout.Controls.Add(rulesLabel, 0, 2);
            layout.SetColumnSpan(rulesLabel, 2);
            layout.Controls.Add(warningLabel, 0, 3);
            layout.SetColumnSpan(warningLabel, 2);
            layout.Controls.Add(buttonPanel, 1, 4);
            layout.Dock = DockStyle.Fill;
            layout.Name = "layout";
            layout.Padding = new Padding(12);
            layout.RowCount = 5;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            layout.TabIndex = 0;
            //
            // vinLabel
            //
            vinLabel.Dock = DockStyle.Fill;
            vinLabel.Name = "vinLabel";
            vinLabel.TabIndex = 0;
            vinLabel.Text = "VIN";
            vinLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // vinTextBox
            //
            vinTextBox.CharacterCasing = CharacterCasing.Upper;
            vinTextBox.Dock = DockStyle.Fill;
            vinTextBox.Font = new Font("Consolas", 11F);
            vinTextBox.MaxLength = 17;
            vinTextBox.Name = "vinTextBox";
            vinTextBox.TabIndex = 1;
            //
            // statusLabel
            //
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Name = "statusLabel";
            statusLabel.TabIndex = 2;
            statusLabel.Text = "";
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // rulesLabel
            //
            rulesLabel.Dock = DockStyle.Fill;
            rulesLabel.ForeColor = SystemColors.GrayText;
            rulesLabel.Name = "rulesLabel";
            rulesLabel.TabIndex = 3;
            rulesLabel.Text = "";
            rulesLabel.TextAlign = ContentAlignment.TopLeft;
            //
            // warningLabel
            //
            warningLabel.Dock = DockStyle.Fill;
            warningLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            warningLabel.ForeColor = Color.FromArgb(176, 0, 0);
            warningLabel.Name = "warningLabel";
            warningLabel.TabIndex = 4;
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
            buttonPanel.TabIndex = 5;
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
            ClientSize = new Size(560, 350);
            Controls.Add(layout);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(560, 350);
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
        private Label vinLabel;
        private TextBox vinTextBox;
        private Label statusLabel;
        private Label rulesLabel;
        private Label warningLabel;
        private FlowLayoutPanel buttonPanel;
        private Button programButton;
        private Button cancelButton;
    }
}
