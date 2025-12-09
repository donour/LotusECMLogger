namespace LotusECMLogger.Controls
{
    partial class ObdResetControl
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
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
            infoLabel = new Label();
            actionsPanel = new FlowLayoutPanel();
            resetButton = new Button();
            layoutPanel.SuspendLayout();
            actionsPanel.SuspendLayout();
            SuspendLayout();
            // 
            // layoutPanel
            // 
            layoutPanel.ColumnCount = 1;
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.Controls.Add(infoLabel, 0, 0);
            layoutPanel.Controls.Add(actionsPanel, 0, 1);
            layoutPanel.Dock = DockStyle.Fill;
            layoutPanel.Location = new Point(0, 0);
            layoutPanel.Margin = new Padding(4, 5, 4, 5);
            layoutPanel.Name = "layoutPanel";
            layoutPanel.Padding = new Padding(14, 17, 14, 17);
            layoutPanel.RowCount = 2;
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.Size = new Size(857, 667);
            layoutPanel.TabIndex = 0;
            // 
            // infoLabel
            // 
            infoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            infoLabel.AutoSize = true;
            infoLabel.Location = new Point(18, 17);
            infoLabel.Margin = new Padding(4, 0, 4, 0);
            infoLabel.Name = "infoLabel";
            infoLabel.Size = new Size(821, 50);
            infoLabel.TabIndex = 0;
            infoLabel.Text = "OBD-II Learned Data Reset\r\nThis sends a Mode 0x11 request to the ECM.";
            // 
            // actionsPanel
            // 
            actionsPanel.AutoSize = true;
            actionsPanel.Controls.Add(resetButton);
            actionsPanel.Dock = DockStyle.Top;
            actionsPanel.Location = new Point(14, 80);
            actionsPanel.Margin = new Padding(0, 13, 0, 0);
            actionsPanel.Name = "actionsPanel";
            actionsPanel.Size = new Size(829, 45);
            actionsPanel.TabIndex = 1;
            actionsPanel.WrapContents = false;
            // 
            // resetButton
            // 
            resetButton.AutoSize = true;
            resetButton.Location = new Point(4, 5);
            resetButton.Margin = new Padding(4, 5, 4, 5);
            resetButton.Name = "resetButton";
            resetButton.Size = new Size(189, 35);
            resetButton.TabIndex = 0;
            resetButton.Text = "Perform Reset";
            resetButton.UseVisualStyleBackColor = true;
            resetButton.Click += resetButton_Click;
            // 
            // ObdResetControl
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(layoutPanel);
            Margin = new Padding(4, 5, 4, 5);
            Name = "ObdResetControl";
            Size = new Size(857, 667);
            Resize += ObdResetControl_Resize;
            layoutPanel.ResumeLayout(false);
            layoutPanel.PerformLayout();
            actionsPanel.ResumeLayout(false);
            actionsPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel layoutPanel;
        private Label infoLabel;
        private FlowLayoutPanel actionsPanel;
        private Button resetButton;
    }
}
