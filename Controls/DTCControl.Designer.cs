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
            readCodesButton = new Button();
            clearCodesButton = new Button();
            dtcListView = new ListView();
            statusLabel = new Label();
            SuspendLayout();
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
            // dtcListView
            //
            dtcListView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dtcListView.FullRowSelect = true;
            dtcListView.GridLines = true;
            dtcListView.Location = new Point(12, 53);
            dtcListView.Name = "dtcListView";
            dtcListView.Size = new Size(576, 424);
            dtcListView.TabIndex = 2;
            dtcListView.UseCompatibleStateImageBehavior = false;
            dtcListView.View = View.Details;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(264, 16);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 3;
            //
            // DTCControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(statusLabel);
            Controls.Add(dtcListView);
            Controls.Add(clearCodesButton);
            Controls.Add(readCodesButton);
            Margin = new Padding(3, 2, 3, 2);
            Name = "DTCControl";
            Size = new Size(600, 489);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button readCodesButton;
        private Button clearCodesButton;
        private ListView dtcListView;
        private Label statusLabel;
    }
}
