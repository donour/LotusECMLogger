namespace LotusECMLogger.Controls
{
    partial class AbsControl
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
            readInfoButton = new Button();
            statusLabel = new Label();
            infoListView = new ListView();
            topPanel.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.Controls.Add(readInfoButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(600, 60);
            topPanel.TabIndex = 0;
            //
            // readInfoButton
            //
            readInfoButton.Location = new Point(12, 12);
            readInfoButton.Name = "readInfoButton";
            readInfoButton.Size = new Size(120, 32);
            readInfoButton.TabIndex = 0;
            readInfoButton.Text = "Read Info";
            readInfoButton.UseVisualStyleBackColor = true;
            readInfoButton.Click += readInfoButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(144, 21);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 1;
            //
            // infoListView
            //
            infoListView.Dock = DockStyle.Fill;
            infoListView.FullRowSelect = true;
            infoListView.GridLines = true;
            infoListView.Location = new Point(0, 60);
            infoListView.Name = "infoListView";
            infoListView.Size = new Size(600, 429);
            infoListView.TabIndex = 1;
            infoListView.UseCompatibleStateImageBehavior = false;
            infoListView.View = View.Details;
            //
            // AbsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(infoListView);
            Controls.Add(topPanel);
            Margin = new Padding(3, 2, 3, 2);
            Name = "AbsControl";
            Size = new Size(600, 489);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Button readInfoButton;
        private ListView infoListView;
        private Label statusLabel;
    }
}
