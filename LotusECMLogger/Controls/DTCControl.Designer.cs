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
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(600, 60);
            topPanel.TabIndex = 4;
            //
            // readCodesButton
            //
            readCodesButton.Location = new Point(12, 12);
            readCodesButton.Name = "readCodesButton";
            readCodesButton.Size = new Size(120, 35);
            readCodesButton.TabIndex = 0;
            readCodesButton.Text = "Read Codes";
            readCodesButton.UseVisualStyleBackColor = true;
            readCodesButton.Click += readCodesButton_Click;
            //
            // clearCodesButton
            //
            clearCodesButton.Enabled = false;
            clearCodesButton.Location = new Point(138, 12);
            clearCodesButton.Name = "clearCodesButton";
            clearCodesButton.Size = new Size(120, 35);
            clearCodesButton.TabIndex = 1;
            clearCodesButton.Text = "Clear Codes";
            clearCodesButton.UseVisualStyleBackColor = true;
            clearCodesButton.Click += clearCodesButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(264, 21);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 3;
            //
            // dtcListView
            //
            dtcListView.Dock = DockStyle.Fill;
            dtcListView.FullRowSelect = true;
            dtcListView.GridLines = true;
            dtcListView.Location = new Point(0, 60);
            dtcListView.Name = "dtcListView";
            dtcListView.Size = new Size(600, 429);
            dtcListView.TabIndex = 2;
            dtcListView.UseCompatibleStateImageBehavior = false;
            dtcListView.View = View.Details;
            //
            // DTCControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(dtcListView);
            Controls.Add(topPanel);
            Margin = new Padding(3, 2, 3, 2);
            Name = "DTCControl";
            Size = new Size(600, 489);
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
