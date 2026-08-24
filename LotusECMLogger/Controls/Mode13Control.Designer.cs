namespace LotusECMLogger.Controls
{
    partial class Mode13Control
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
            readAllButton = new Button();
            requestFormLabel = new Label();
            requestFormComboBox = new ComboBox();
            statusLabel = new Label();
            noteLabel = new Label();
            codesListView = new ListView();
            rawPanel = new Panel();
            rawResponseLabel = new Label();
            rawResponseTextBox = new TextBox();
            topPanel.SuspendLayout();
            rawPanel.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.Controls.Add(readAllButton);
            topPanel.Controls.Add(requestFormLabel);
            topPanel.Controls.Add(requestFormComboBox);
            topPanel.Controls.Add(statusLabel);
            topPanel.Controls.Add(noteLabel);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(600, 84);
            topPanel.TabIndex = 0;
            //
            // readAllButton
            //
            readAllButton.Location = new Point(12, 12);
            readAllButton.Name = "readAllButton";
            readAllButton.Size = new Size(140, 32);
            readAllButton.TabIndex = 0;
            readAllButton.Text = "Read All Codes";
            readAllButton.UseVisualStyleBackColor = true;
            readAllButton.Click += readAllButton_Click;
            //
            // requestFormLabel
            //
            requestFormLabel.AutoSize = true;
            requestFormLabel.Location = new Point(166, 20);
            requestFormLabel.Name = "requestFormLabel";
            requestFormLabel.Size = new Size(56, 15);
            requestFormLabel.TabIndex = 1;
            requestFormLabel.Text = "Request:";
            //
            // requestFormComboBox
            //
            requestFormComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            requestFormComboBox.Location = new Point(232, 16);
            requestFormComboBox.Name = "requestFormComboBox";
            requestFormComboBox.Size = new Size(220, 23);
            requestFormComboBox.TabIndex = 2;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(466, 20);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 3;
            //
            // noteLabel
            //
            noteLabel.AutoSize = true;
            noteLabel.ForeColor = SystemColors.GrayText;
            noteLabel.Location = new Point(12, 55);
            noteLabel.Name = "noteLabel";
            noteLabel.Size = new Size(0, 15);
            noteLabel.TabIndex = 4;
            //
            // codesListView
            //
            codesListView.Dock = DockStyle.Fill;
            codesListView.FullRowSelect = true;
            codesListView.GridLines = true;
            codesListView.Location = new Point(0, 84);
            codesListView.Name = "codesListView";
            codesListView.ShowItemToolTips = true;
            codesListView.Size = new Size(600, 317);
            codesListView.TabIndex = 1;
            codesListView.UseCompatibleStateImageBehavior = false;
            codesListView.View = View.Details;
            //
            // rawPanel
            //
            rawPanel.Controls.Add(rawResponseTextBox);
            rawPanel.Controls.Add(rawResponseLabel);
            rawPanel.Dock = DockStyle.Bottom;
            rawPanel.Location = new Point(0, 401);
            rawPanel.Name = "rawPanel";
            rawPanel.Padding = new Padding(12, 4, 12, 8);
            rawPanel.Size = new Size(600, 88);
            rawPanel.TabIndex = 2;
            //
            // rawResponseLabel
            //
            rawResponseLabel.AutoSize = true;
            rawResponseLabel.Dock = DockStyle.Top;
            rawResponseLabel.Location = new Point(12, 4);
            rawResponseLabel.Name = "rawResponseLabel";
            rawResponseLabel.Size = new Size(154, 15);
            rawResponseLabel.TabIndex = 0;
            rawResponseLabel.Text = "Raw response (SID + codes):";
            //
            // rawResponseTextBox
            //
            rawResponseTextBox.Dock = DockStyle.Fill;
            rawResponseTextBox.Location = new Point(12, 19);
            rawResponseTextBox.Multiline = true;
            rawResponseTextBox.Name = "rawResponseTextBox";
            rawResponseTextBox.ReadOnly = true;
            rawResponseTextBox.ScrollBars = ScrollBars.Vertical;
            rawResponseTextBox.Size = new Size(576, 57);
            rawResponseTextBox.TabIndex = 1;
            //
            // Mode13Control
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(codesListView);
            Controls.Add(rawPanel);
            Controls.Add(topPanel);
            Margin = new Padding(3, 2, 3, 2);
            Name = "Mode13Control";
            Size = new Size(600, 489);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            rawPanel.ResumeLayout(false);
            rawPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Button readAllButton;
        private Label requestFormLabel;
        private ComboBox requestFormComboBox;
        private Label statusLabel;
        private Label noteLabel;
        private ListView codesListView;
        private Panel rawPanel;
        private Label rawResponseLabel;
        private TextBox rawResponseTextBox;
    }
}
