namespace LotusECMLogger.Controls
{
    partial class EcuCodingControl
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
            codingTopPanel = new Panel();
            bitFieldLabel = new Label();
            resetCodingButton = new Button();
            saveCodingButton = new Button();
            writeCodesButton = new Button();
            readCodesButton = new Button();
            codingScrollPanel = new Panel();
            codingTopPanel.SuspendLayout();
            SuspendLayout();
            // 
            // codingTopPanel
            // 
            codingTopPanel.Controls.Add(bitFieldLabel);
            codingTopPanel.Controls.Add(resetCodingButton);
            codingTopPanel.Controls.Add(saveCodingButton);
            codingTopPanel.Controls.Add(writeCodesButton);
            codingTopPanel.Controls.Add(readCodesButton);
            codingTopPanel.Dock = DockStyle.Top;
            codingTopPanel.Location = new Point(0, 0);
            codingTopPanel.Margin = new Padding(4, 5, 4, 5);
            codingTopPanel.Name = "codingTopPanel";
            codingTopPanel.Size = new Size(1143, 67);
            codingTopPanel.TabIndex = 0;
            // 
            // bitFieldLabel
            // 
            bitFieldLabel.AutoSize = true;
            bitFieldLabel.Location = new Point(643, 20);
            bitFieldLabel.Margin = new Padding(4, 0, 4, 0);
            bitFieldLabel.Name = "bitFieldLabel";
            bitFieldLabel.Size = new Size(85, 25);
            bitFieldLabel.TabIndex = 4;
            bitFieldLabel.Text = "BitField: -";
            // 
            // resetCodingButton
            // 
            resetCodingButton.Enabled = false;
            resetCodingButton.Location = new Point(500, 8);
            resetCodingButton.Margin = new Padding(4, 5, 4, 5);
            resetCodingButton.Name = "resetCodingButton";
            resetCodingButton.Size = new Size(114, 32);
            resetCodingButton.TabIndex = 3;
            resetCodingButton.Text = "Reset";
            resetCodingButton.UseVisualStyleBackColor = true;
            resetCodingButton.Click += resetCodingButton_Click;
            // 
            // saveCodingButton
            // 
            saveCodingButton.Enabled = false;
            saveCodingButton.Location = new Point(329, 8);
            saveCodingButton.Margin = new Padding(4, 5, 4, 5);
            saveCodingButton.Name = "saveCodingButton";
            saveCodingButton.Size = new Size(157, 32);
            saveCodingButton.TabIndex = 2;
            saveCodingButton.Text = "Save Changes";
            saveCodingButton.UseVisualStyleBackColor = true;
            saveCodingButton.Click += saveCodingButton_Click;
            // 
            // writeCodesButton
            // 
            writeCodesButton.Enabled = false;
            writeCodesButton.Location = new Point(171, 8);
            writeCodesButton.Margin = new Padding(4, 5, 4, 5);
            writeCodesButton.Name = "writeCodesButton";
            writeCodesButton.Size = new Size(143, 32);
            writeCodesButton.TabIndex = 1;
            writeCodesButton.Text = "Write Codes";
            writeCodesButton.UseVisualStyleBackColor = true;
            writeCodesButton.Click += writeCodesButton_Click;
            // 
            // readCodesButton
            // 
            readCodesButton.Location = new Point(14, 8);
            readCodesButton.Margin = new Padding(4, 5, 4, 5);
            readCodesButton.Name = "readCodesButton";
            readCodesButton.Size = new Size(143, 32);
            readCodesButton.TabIndex = 0;
            readCodesButton.Text = "Read Codes";
            readCodesButton.UseVisualStyleBackColor = true;
            readCodesButton.Click += readCodesButton_Click;
            // 
            // codingScrollPanel
            // 
            codingScrollPanel.AutoScroll = true;
            codingScrollPanel.Dock = DockStyle.Fill;
            codingScrollPanel.Location = new Point(0, 67);
            codingScrollPanel.Margin = new Padding(4, 5, 4, 5);
            codingScrollPanel.Name = "codingScrollPanel";
            codingScrollPanel.Padding = new Padding(14, 17, 14, 17);
            codingScrollPanel.Size = new Size(1143, 933);
            codingScrollPanel.TabIndex = 1;
            // 
            // EcuCodingControl
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(codingScrollPanel);
            Controls.Add(codingTopPanel);
            Margin = new Padding(4, 5, 4, 5);
            Name = "EcuCodingControl";
            Size = new Size(1143, 1000);
            codingTopPanel.ResumeLayout(false);
            codingTopPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel codingTopPanel;
        private Panel codingScrollPanel;
        private Button readCodesButton;
        private Button writeCodesButton;
        private Button saveCodingButton;
        private Button resetCodingButton;
        private Label bitFieldLabel;
    }
}
