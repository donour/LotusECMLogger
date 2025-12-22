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
            actionsPanel = new FlowLayoutPanel();
            resetButton = new Button();
            infoLabel = new Label();
            layoutPanel.SuspendLayout();
            actionsPanel.SuspendLayout();
            SuspendLayout();
            // 
            // layoutPanel
            // 
            layoutPanel.ColumnCount = 1;
            layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutPanel.Controls.Add(actionsPanel, 0, 0);
            layoutPanel.Controls.Add(infoLabel, 0, 1);
            layoutPanel.Dock = DockStyle.Fill;
            layoutPanel.Location = new Point(0, 0);
            layoutPanel.Name = "layoutPanel";
            layoutPanel.Padding = new Padding(10);
            layoutPanel.RowCount = 2;
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.RowStyles.Add(new RowStyle());
            layoutPanel.Size = new Size(600, 400);
            layoutPanel.TabIndex = 0;
            // 
            // actionsPanel
            // 
            actionsPanel.AutoSize = true;
            actionsPanel.Controls.Add(resetButton);
            actionsPanel.Dock = DockStyle.Top;
            actionsPanel.Location = new Point(10, 18);
            actionsPanel.Margin = new Padding(0, 8, 0, 0);
            actionsPanel.Name = "actionsPanel";
            actionsPanel.Size = new Size(580, 38);
            actionsPanel.TabIndex = 1;
            actionsPanel.WrapContents = false;
            // 
            // resetButton
            // 
            resetButton.AutoSize = true;
            resetButton.Location = new Point(3, 3);
            resetButton.Name = "resetButton";
            resetButton.Size = new Size(132, 32);
            resetButton.TabIndex = 0;
            resetButton.Text = "Perform Reset";
            resetButton.UseVisualStyleBackColor = true;
            resetButton.Click += resetButton_Click;
            // 
            // infoLabel
            // 
            infoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            infoLabel.AutoSize = true;
            infoLabel.Location = new Point(13, 56);
            infoLabel.Name = "infoLabel";
            infoLabel.Size = new Size(574, 30);
            infoLabel.TabIndex = 0;
            infoLabel.Text = "OBD-II Learned Data Reset\r\nThis sends a Mode 0x11 request to the ECM.";
            // 
            // ObdResetControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(layoutPanel);
            Name = "ObdResetControl";
            Size = new Size(600, 400);
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
