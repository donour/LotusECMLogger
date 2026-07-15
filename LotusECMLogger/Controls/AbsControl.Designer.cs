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
            testConnectionButton = new Button();
            readInfoButton = new Button();
            moduleInfoButton = new Button();
            sniffBusButton = new Button();
            statusLabel = new Label();
            infoListView = new ListView();
            topPanel.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.Controls.Add(testConnectionButton);
            topPanel.Controls.Add(readInfoButton);
            topPanel.Controls.Add(moduleInfoButton);
            topPanel.Controls.Add(sniffBusButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(600, 60);
            topPanel.TabIndex = 0;
            //
            // testConnectionButton
            //
            testConnectionButton.Location = new Point(12, 12);
            testConnectionButton.Name = "testConnectionButton";
            testConnectionButton.Size = new Size(130, 32);
            testConnectionButton.TabIndex = 0;
            testConnectionButton.Text = "Test Connection";
            testConnectionButton.UseVisualStyleBackColor = true;
            testConnectionButton.Click += testConnectionButton_Click;
            //
            // readInfoButton
            //
            readInfoButton.Location = new Point(148, 12);
            readInfoButton.Name = "readInfoButton";
            readInfoButton.Size = new Size(100, 32);
            readInfoButton.TabIndex = 1;
            readInfoButton.Text = "Read DTCs";
            readInfoButton.UseVisualStyleBackColor = true;
            readInfoButton.Click += readInfoButton_Click;
            //
            // moduleInfoButton
            //
            moduleInfoButton.Location = new Point(254, 12);
            moduleInfoButton.Name = "moduleInfoButton";
            moduleInfoButton.Size = new Size(100, 32);
            moduleInfoButton.TabIndex = 2;
            moduleInfoButton.Text = "Read Info";
            moduleInfoButton.UseVisualStyleBackColor = true;
            moduleInfoButton.Click += moduleInfoButton_Click;
            //
            // sniffBusButton
            //
            sniffBusButton.Location = new Point(360, 12);
            sniffBusButton.Name = "sniffBusButton";
            sniffBusButton.Size = new Size(100, 32);
            sniffBusButton.TabIndex = 3;
            sniffBusButton.Text = "Sniff Bus";
            sniffBusButton.UseVisualStyleBackColor = true;
            sniffBusButton.Click += sniffBusButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(470, 21);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 4;
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
        private Button testConnectionButton;
        private Button readInfoButton;
        private Button moduleInfoButton;
        private Button sniffBusButton;
        private ListView infoListView;
        private Label statusLabel;
    }
}
