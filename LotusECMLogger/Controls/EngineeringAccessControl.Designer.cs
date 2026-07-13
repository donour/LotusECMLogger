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
            resultGroupBox = new GroupBox();
            resultTextBox = new TextBox();
            layoutPanel.SuspendLayout();
            configGroupBox.SuspendLayout();
            configLayout.SuspendLayout();
            buttonPanel.SuspendLayout();
            statusPanel.SuspendLayout();
            resultGroupBox.SuspendLayout();
            SuspendLayout();
            //
            // layoutPanel
            //
            layoutPanel.ColumnCount = 1;
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.Controls.Add(configGroupBox, 0, 0);
            layoutPanel.Controls.Add(resultGroupBox, 0, 1);
            layoutPanel.Dock = DockStyle.Fill;
            layoutPanel.Location = new Point(14, 17);
            layoutPanel.Margin = new Padding(4, 5, 4, 5);
            layoutPanel.Name = "layoutPanel";
            layoutPanel.RowCount = 2;
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
            configGroupBox.Size = new Size(1107, 200);
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
            configLayout.Size = new Size(1079, 142);
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
            // resultGroupBox
            //
            resultGroupBox.Controls.Add(resultTextBox);
            resultGroupBox.Dock = DockStyle.Fill;
            resultGroupBox.Location = new Point(4, 215);
            resultGroupBox.Margin = new Padding(4, 10, 4, 5);
            resultGroupBox.Name = "resultGroupBox";
            resultGroupBox.Padding = new Padding(14, 17, 14, 17);
            resultGroupBox.Size = new Size(1107, 746);
            resultGroupBox.TabIndex = 1;
            resultGroupBox.TabStop = false;
            resultGroupBox.Text = "Probe Result";
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
            resultTextBox.Size = new Size(1079, 688);
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
        private GroupBox resultGroupBox;
        private TextBox resultTextBox;
    }
}
