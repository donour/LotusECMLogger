namespace LotusECMLogger.Controls
{
    partial class DTCControl
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
            topPanel = new Panel();
            readCodesButton = new Button();
            clearCodesButton = new Button();
            statusLabel = new Label();
            dtcListView = new ListView();
            topPanel.SuspendLayout();
            SuspendLayout();
            // 
            // topPanel
            // 
            topPanel.Controls.Add(readCodesButton);
            topPanel.Controls.Add(clearCodesButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Margin = new Padding(4, 5, 4, 5);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(857, 100);
            topPanel.TabIndex = 4;
            // 
            // readCodesButton
            // 
            readCodesButton.Location = new Point(17, 20);
            readCodesButton.Margin = new Padding(4, 5, 4, 5);
            readCodesButton.Name = "readCodesButton";
            readCodesButton.Size = new Size(171, 32);
            readCodesButton.TabIndex = 0;
            readCodesButton.Text = "Read Codes";
            readCodesButton.UseVisualStyleBackColor = true;
            readCodesButton.Click += readCodesButton_Click;
            // 
            // clearCodesButton
            // 
            clearCodesButton.Enabled = false;
            clearCodesButton.Location = new Point(197, 20);
            clearCodesButton.Margin = new Padding(4, 5, 4, 5);
            clearCodesButton.Name = "clearCodesButton";
            clearCodesButton.Size = new Size(171, 32);
            clearCodesButton.TabIndex = 1;
            clearCodesButton.Text = "Clear Codes";
            clearCodesButton.UseVisualStyleBackColor = true;
            clearCodesButton.Click += clearCodesButton_Click;
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(377, 35);
            statusLabel.Margin = new Padding(4, 0, 4, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 25);
            statusLabel.TabIndex = 3;
            // 
            // dtcListView
            // 
            dtcListView.Dock = DockStyle.Fill;
            dtcListView.FullRowSelect = true;
            dtcListView.GridLines = true;
            dtcListView.Location = new Point(0, 100);
            dtcListView.Margin = new Padding(4, 5, 4, 5);
            dtcListView.Name = "dtcListView";
            dtcListView.Size = new Size(857, 715);
            dtcListView.TabIndex = 2;
            dtcListView.UseCompatibleStateImageBehavior = false;
            dtcListView.View = View.Details;
            // 
            // DTCControl
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(dtcListView);
            Controls.Add(topPanel);
            Margin = new Padding(4, 3, 4, 3);
            Name = "DTCControl";
            Size = new Size(857, 815);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Button readCodesButton;
        private Button clearCodesButton;
        private ListView dtcListView;
        private Label statusLabel;
    }
}
