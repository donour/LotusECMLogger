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
            readCodesButton = new Button();
            writeCodesButton = new Button();
            saveCodingButton = new Button();
            resetCodingButton = new Button();
            bitFieldLabel = new Label();
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
            codingTopPanel.Name = "codingTopPanel";
            codingTopPanel.Size = new Size(800, 40);
            codingTopPanel.TabIndex = 0;
            //
            // readCodesButton
            //
            readCodesButton.Location = new Point(10, 5);
            readCodesButton.Name = "readCodesButton";
            readCodesButton.Size = new Size(100, 30);
            readCodesButton.TabIndex = 0;
            readCodesButton.Text = "Read Codes";
            readCodesButton.UseVisualStyleBackColor = true;
            readCodesButton.Click += readCodesButton_Click;
            //
            // writeCodesButton
            //
            writeCodesButton.Enabled = false;
            writeCodesButton.Location = new Point(120, 5);
            writeCodesButton.Name = "writeCodesButton";
            writeCodesButton.Size = new Size(100, 30);
            writeCodesButton.TabIndex = 1;
            writeCodesButton.Text = "Write Codes";
            writeCodesButton.UseVisualStyleBackColor = true;
            writeCodesButton.Click += writeCodesButton_Click;
            //
            // saveCodingButton
            //
            saveCodingButton.Enabled = false;
            saveCodingButton.Location = new Point(230, 5);
            saveCodingButton.Name = "saveCodingButton";
            saveCodingButton.Size = new Size(110, 30);
            saveCodingButton.TabIndex = 2;
            saveCodingButton.Text = "Save Changes";
            saveCodingButton.UseVisualStyleBackColor = true;
            saveCodingButton.Click += saveCodingButton_Click;
            //
            // resetCodingButton
            //
            resetCodingButton.Enabled = false;
            resetCodingButton.Location = new Point(350, 5);
            resetCodingButton.Name = "resetCodingButton";
            resetCodingButton.Size = new Size(80, 30);
            resetCodingButton.TabIndex = 3;
            resetCodingButton.Text = "Reset";
            resetCodingButton.UseVisualStyleBackColor = true;
            resetCodingButton.Click += resetCodingButton_Click;
            //
            // bitFieldLabel
            //
            bitFieldLabel.AutoSize = true;
            bitFieldLabel.Location = new Point(450, 12);
            bitFieldLabel.Name = "bitFieldLabel";
            bitFieldLabel.Size = new Size(59, 15);
            bitFieldLabel.TabIndex = 4;
            bitFieldLabel.Text = "BitField: -";
            //
            // codingScrollPanel
            //
            codingScrollPanel.AutoScroll = true;
            codingScrollPanel.Dock = DockStyle.Fill;
            codingScrollPanel.Location = new Point(0, 40);
            codingScrollPanel.Name = "codingScrollPanel";
            codingScrollPanel.Padding = new Padding(10);
            codingScrollPanel.Size = new Size(800, 560);
            codingScrollPanel.TabIndex = 1;
            //
            // EcuCodingControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(codingScrollPanel);
            Controls.Add(codingTopPanel);
            Name = "EcuCodingControl";
            Size = new Size(800, 600);
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
