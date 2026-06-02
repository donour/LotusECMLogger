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
            eraseModelPanel = new Panel();
            eraseDangerLabel = new Label();
            firmwareVersionLabel = new Label();
            firmwareVersionCombo = new ComboBox();
            codingAddressLabel = new Label();
            eraseModelButton = new Button();
            codingTopPanel.SuspendLayout();
            eraseModelPanel.SuspendLayout();
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
            // bitFieldLabel
            // 
            bitFieldLabel.AutoSize = true;
            bitFieldLabel.Location = new Point(450, 12);
            bitFieldLabel.Name = "bitFieldLabel";
            bitFieldLabel.Size = new Size(57, 15);
            bitFieldLabel.TabIndex = 4;
            bitFieldLabel.Text = "BitField: -";
            // 
            // resetCodingButton
            // 
            resetCodingButton.Enabled = false;
            resetCodingButton.Location = new Point(350, 5);
            resetCodingButton.Name = "resetCodingButton";
            resetCodingButton.Size = new Size(80, 32);
            resetCodingButton.TabIndex = 3;
            resetCodingButton.Text = "Reset";
            resetCodingButton.UseVisualStyleBackColor = true;
            resetCodingButton.Click += resetCodingButton_Click;
            // 
            // saveCodingButton
            // 
            saveCodingButton.Enabled = false;
            saveCodingButton.Location = new Point(230, 5);
            saveCodingButton.Name = "saveCodingButton";
            saveCodingButton.Size = new Size(110, 32);
            saveCodingButton.TabIndex = 2;
            saveCodingButton.Text = "Save Changes";
            saveCodingButton.UseVisualStyleBackColor = true;
            saveCodingButton.Click += saveCodingButton_Click;
            // 
            // writeCodesButton
            // 
            writeCodesButton.Enabled = false;
            writeCodesButton.Location = new Point(120, 5);
            writeCodesButton.Name = "writeCodesButton";
            writeCodesButton.Size = new Size(100, 32);
            writeCodesButton.TabIndex = 1;
            writeCodesButton.Text = "Write Codes";
            writeCodesButton.UseVisualStyleBackColor = true;
            writeCodesButton.Click += writeCodesButton_Click;
            // 
            // readCodesButton
            // 
            readCodesButton.Location = new Point(10, 5);
            readCodesButton.Name = "readCodesButton";
            readCodesButton.Size = new Size(100, 32);
            readCodesButton.TabIndex = 0;
            readCodesButton.Text = "Read Codes";
            readCodesButton.UseVisualStyleBackColor = true;
            readCodesButton.Click += readCodesButton_Click;
            // 
            // codingScrollPanel
            // 
            codingScrollPanel.AutoScroll = true;
            codingScrollPanel.Dock = DockStyle.Fill;
            codingScrollPanel.Location = new Point(0, 40);
            codingScrollPanel.Name = "codingScrollPanel";
            codingScrollPanel.Padding = new Padding(10, 10, 10, 10);
            codingScrollPanel.Size = new Size(800, 476);
            codingScrollPanel.TabIndex = 1;
            //
            // eraseModelPanel
            //
            eraseModelPanel.Controls.Add(eraseDangerLabel);
            eraseModelPanel.Controls.Add(firmwareVersionLabel);
            eraseModelPanel.Controls.Add(firmwareVersionCombo);
            eraseModelPanel.Controls.Add(codingAddressLabel);
            eraseModelPanel.Controls.Add(eraseModelButton);
            eraseModelPanel.BackColor = Color.FromArgb(252, 240, 240);
            eraseModelPanel.BorderStyle = BorderStyle.FixedSingle;
            eraseModelPanel.Dock = DockStyle.Bottom;
            eraseModelPanel.Location = new Point(0, 516);
            eraseModelPanel.Name = "eraseModelPanel";
            eraseModelPanel.Size = new Size(800, 84);
            eraseModelPanel.TabIndex = 2;
            //
            // eraseDangerLabel
            //
            eraseDangerLabel.AutoSize = true;
            eraseDangerLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            eraseDangerLabel.ForeColor = Color.FromArgb(176, 0, 32);
            eraseDangerLabel.Location = new Point(10, 8);
            eraseDangerLabel.Name = "eraseDangerLabel";
            eraseDangerLabel.Size = new Size(0, 15);
            eraseDangerLabel.TabIndex = 0;
            eraseDangerLabel.Text = "⚠ DANGER — Erase Model Info (writes 0xFF to model[], persisted to EEPROM)";
            //
            // firmwareVersionLabel
            //
            firmwareVersionLabel.AutoSize = true;
            firmwareVersionLabel.Location = new Point(10, 41);
            firmwareVersionLabel.Name = "firmwareVersionLabel";
            firmwareVersionLabel.Size = new Size(0, 15);
            firmwareVersionLabel.TabIndex = 1;
            firmwareVersionLabel.Text = "Firmware:";
            //
            // firmwareVersionCombo
            //
            firmwareVersionCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            firmwareVersionCombo.Location = new Point(90, 38);
            firmwareVersionCombo.Name = "firmwareVersionCombo";
            firmwareVersionCombo.Size = new Size(240, 23);
            firmwareVersionCombo.TabIndex = 2;
            firmwareVersionCombo.SelectedIndexChanged += firmwareVersionCombo_SelectedIndexChanged;
            //
            // codingAddressLabel
            //
            codingAddressLabel.AutoSize = true;
            codingAddressLabel.Location = new Point(345, 41);
            codingAddressLabel.Name = "codingAddressLabel";
            codingAddressLabel.Size = new Size(0, 15);
            codingAddressLabel.TabIndex = 3;
            codingAddressLabel.Text = "coding_cmd: —";
            //
            // eraseModelButton
            //
            eraseModelButton.Enabled = false;
            eraseModelButton.Location = new Point(620, 34);
            eraseModelButton.Name = "eraseModelButton";
            eraseModelButton.Size = new Size(160, 32);
            eraseModelButton.TabIndex = 4;
            eraseModelButton.Text = "Erase Model Info";
            eraseModelButton.UseVisualStyleBackColor = true;
            eraseModelButton.Click += eraseModelButton_Click;
            //
            // EcuCodingControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(codingScrollPanel);
            Controls.Add(codingTopPanel);
            Controls.Add(eraseModelPanel);
            Name = "EcuCodingControl";
            Size = new Size(800, 600);
            codingTopPanel.ResumeLayout(false);
            codingTopPanel.PerformLayout();
            eraseModelPanel.ResumeLayout(false);
            eraseModelPanel.PerformLayout();
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
        private Panel eraseModelPanel;
        private Label eraseDangerLabel;
        private Label firmwareVersionLabel;
        private ComboBox firmwareVersionCombo;
        private Label codingAddressLabel;
        private Button eraseModelButton;
    }
}
