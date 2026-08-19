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
            readFreezeFrameButton = new Button();
            clearCodesButton = new Button();
            statusLabel = new Label();
            resultsSplit = new SplitContainer();
            dtcListView = new ListView();
            freezeFrameListView = new ListView();
            topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)resultsSplit).BeginInit();
            resultsSplit.Panel1.SuspendLayout();
            resultsSplit.Panel2.SuspendLayout();
            resultsSplit.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.Controls.Add(readCodesButton);
            topPanel.Controls.Add(readFreezeFrameButton);
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
            readCodesButton.Size = new Size(120, 32);
            readCodesButton.TabIndex = 0;
            readCodesButton.Text = "Read Codes";
            readCodesButton.UseVisualStyleBackColor = true;
            readCodesButton.Click += readCodesButton_Click;
            //
            // readFreezeFrameButton
            //
            readFreezeFrameButton.Location = new Point(138, 12);
            readFreezeFrameButton.Name = "readFreezeFrameButton";
            readFreezeFrameButton.Size = new Size(150, 32);
            readFreezeFrameButton.TabIndex = 1;
            readFreezeFrameButton.Text = "Read Freeze Frame";
            readFreezeFrameButton.UseVisualStyleBackColor = true;
            readFreezeFrameButton.Click += readFreezeFrameButton_Click;
            //
            // clearCodesButton
            //
            clearCodesButton.Location = new Point(294, 12);
            clearCodesButton.Name = "clearCodesButton";
            clearCodesButton.Size = new Size(120, 32);
            clearCodesButton.TabIndex = 2;
            clearCodesButton.Text = "Clear Codes";
            clearCodesButton.UseVisualStyleBackColor = true;
            clearCodesButton.Click += clearCodesButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(426, 21);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 3;
            //
            // resultsSplit
            //
            resultsSplit.Dock = DockStyle.Fill;
            resultsSplit.Location = new Point(0, 60);
            resultsSplit.Name = "resultsSplit";
            resultsSplit.Orientation = Orientation.Horizontal;
            //
            // resultsSplit.Panel1
            //
            resultsSplit.Panel1.Controls.Add(dtcListView);
            //
            // resultsSplit.Panel2
            //
            resultsSplit.Panel2.Controls.Add(freezeFrameListView);
            resultsSplit.Size = new Size(600, 429);
            resultsSplit.SplitterDistance = 257;
            resultsSplit.TabIndex = 5;
            //
            // dtcListView
            //
            dtcListView.Dock = DockStyle.Fill;
            dtcListView.FullRowSelect = true;
            dtcListView.GridLines = true;
            dtcListView.Location = new Point(0, 0);
            dtcListView.Name = "dtcListView";
            dtcListView.ShowItemToolTips = true;
            dtcListView.Size = new Size(600, 257);
            dtcListView.TabIndex = 0;
            dtcListView.UseCompatibleStateImageBehavior = false;
            dtcListView.View = View.Details;
            //
            // freezeFrameListView
            //
            freezeFrameListView.Dock = DockStyle.Fill;
            freezeFrameListView.FullRowSelect = true;
            freezeFrameListView.GridLines = true;
            freezeFrameListView.Location = new Point(0, 0);
            freezeFrameListView.Name = "freezeFrameListView";
            freezeFrameListView.ShowItemToolTips = true;
            freezeFrameListView.Size = new Size(600, 168);
            freezeFrameListView.TabIndex = 0;
            freezeFrameListView.UseCompatibleStateImageBehavior = false;
            freezeFrameListView.View = View.Details;
            //
            // DTCControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(resultsSplit);
            Controls.Add(topPanel);
            Margin = new Padding(3, 2, 3, 2);
            Name = "DTCControl";
            Size = new Size(600, 489);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            resultsSplit.Panel1.ResumeLayout(false);
            resultsSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)resultsSplit).EndInit();
            resultsSplit.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Button readCodesButton;
        private Button readFreezeFrameButton;
        private Button clearCodesButton;
        private SplitContainer resultsSplit;
        private ListView dtcListView;
        private ListView freezeFrameListView;
        private Label statusLabel;
    }
}
