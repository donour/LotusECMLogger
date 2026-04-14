namespace LotusECMLogger.Controls
{
    partial class AddRequestDialog
    {
        private System.ComponentModel.IContainer? components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            layout = new TableLayoutPanel();
            typeLabel = new Label();
            typeComboBox = new ComboBox();
            nameLabel = new Label();
            nameTextBox = new TextBox();
            descriptionLabel = new Label();
            descriptionTextBox = new TextBox();
            categoryLabel = new Label();
            categoryTextBox = new TextBox();
            unitLabel = new Label();
            unitTextBox = new TextBox();
            pidsLabel = new Label();
            pidsTextBox = new TextBox();
            pidHighLabel = new Label();
            pidHighTextBox = new TextBox();
            pidLowLabel = new Label();
            pidLowTextBox = new TextBox();
            buttonPanel = new FlowLayoutPanel();
            addButton = new Button();
            cancelButton = new Button();
            layout.SuspendLayout();
            buttonPanel.SuspendLayout();
            SuspendLayout();
            //
            // layout
            //
            layout.AutoScroll = true;
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(typeLabel, 0, 0);
            layout.Controls.Add(typeComboBox, 1, 0);
            layout.Controls.Add(nameLabel, 0, 1);
            layout.Controls.Add(nameTextBox, 1, 1);
            layout.Controls.Add(descriptionLabel, 0, 2);
            layout.Controls.Add(descriptionTextBox, 1, 2);
            layout.Controls.Add(categoryLabel, 0, 3);
            layout.Controls.Add(categoryTextBox, 1, 3);
            layout.Controls.Add(unitLabel, 0, 4);
            layout.Controls.Add(unitTextBox, 1, 4);
            layout.Controls.Add(pidsLabel, 0, 5);
            layout.Controls.Add(pidsTextBox, 1, 5);
            layout.Controls.Add(pidHighLabel, 0, 6);
            layout.Controls.Add(pidHighTextBox, 1, 6);
            layout.Controls.Add(pidLowLabel, 0, 7);
            layout.Controls.Add(pidLowTextBox, 1, 7);
            layout.Controls.Add(buttonPanel, 1, 8);
            layout.Dock = DockStyle.Fill;
            layout.Name = "layout";
            layout.Padding = new Padding(12);
            layout.RowCount = 9;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            layout.TabIndex = 0;
            //
            // typeLabel
            //
            typeLabel.Dock = DockStyle.Fill;
            typeLabel.Name = "typeLabel";
            typeLabel.TabIndex = 0;
            typeLabel.Text = "Type";
            typeLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // typeComboBox
            //
            typeComboBox.Dock = DockStyle.Fill;
            typeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            typeComboBox.FormattingEnabled = true;
            typeComboBox.Items.AddRange(new object[] { "Mode01", "Mode22" });
            typeComboBox.Name = "typeComboBox";
            typeComboBox.TabIndex = 1;
            typeComboBox.SelectedIndexChanged += TypeComboBox_SelectedIndexChanged;
            //
            // nameLabel
            //
            nameLabel.Dock = DockStyle.Fill;
            nameLabel.Name = "nameLabel";
            nameLabel.TabIndex = 2;
            nameLabel.Text = "Name";
            nameLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // nameTextBox
            //
            nameTextBox.Dock = DockStyle.Fill;
            nameTextBox.Name = "nameTextBox";
            nameTextBox.TabIndex = 3;
            nameTextBox.Text = "New Mode01 Request";
            //
            // descriptionLabel
            //
            descriptionLabel.Dock = DockStyle.Fill;
            descriptionLabel.Name = "descriptionLabel";
            descriptionLabel.TabIndex = 4;
            descriptionLabel.Text = "Description";
            descriptionLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // descriptionTextBox
            //
            descriptionTextBox.Dock = DockStyle.Fill;
            descriptionTextBox.Multiline = true;
            descriptionTextBox.Name = "descriptionTextBox";
            descriptionTextBox.ScrollBars = ScrollBars.Vertical;
            descriptionTextBox.TabIndex = 5;
            //
            // categoryLabel
            //
            categoryLabel.Dock = DockStyle.Fill;
            categoryLabel.Name = "categoryLabel";
            categoryLabel.TabIndex = 6;
            categoryLabel.Text = "Category";
            categoryLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // categoryTextBox
            //
            categoryTextBox.Dock = DockStyle.Fill;
            categoryTextBox.Name = "categoryTextBox";
            categoryTextBox.TabIndex = 7;
            //
            // unitLabel
            //
            unitLabel.Dock = DockStyle.Fill;
            unitLabel.Name = "unitLabel";
            unitLabel.TabIndex = 8;
            unitLabel.Text = "Unit";
            unitLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // unitTextBox
            //
            unitTextBox.Dock = DockStyle.Fill;
            unitTextBox.Name = "unitTextBox";
            unitTextBox.TabIndex = 9;
            //
            // pidsLabel
            //
            pidsLabel.Dock = DockStyle.Fill;
            pidsLabel.Name = "pidsLabel";
            pidsLabel.TabIndex = 10;
            pidsLabel.Text = "PIDs";
            pidsLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // pidsTextBox
            //
            pidsTextBox.Dock = DockStyle.Fill;
            pidsTextBox.Name = "pidsTextBox";
            pidsTextBox.TabIndex = 11;
            pidsTextBox.Text = "0x0C";
            //
            // pidHighLabel
            //
            pidHighLabel.Dock = DockStyle.Fill;
            pidHighLabel.Name = "pidHighLabel";
            pidHighLabel.TabIndex = 12;
            pidHighLabel.Text = "PID High";
            pidHighLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // pidHighTextBox
            //
            pidHighTextBox.Dock = DockStyle.Fill;
            pidHighTextBox.Name = "pidHighTextBox";
            pidHighTextBox.TabIndex = 13;
            pidHighTextBox.Text = "0x00";
            //
            // pidLowLabel
            //
            pidLowLabel.Dock = DockStyle.Fill;
            pidLowLabel.Name = "pidLowLabel";
            pidLowLabel.TabIndex = 14;
            pidLowLabel.Text = "PID Low";
            pidLowLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // pidLowTextBox
            //
            pidLowTextBox.Dock = DockStyle.Fill;
            pidLowTextBox.Name = "pidLowTextBox";
            pidLowTextBox.TabIndex = 15;
            pidLowTextBox.Text = "0x00";
            //
            // buttonPanel
            //
            buttonPanel.AutoSize = true;
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Controls.Add(addButton);
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.RightToLeft;
            buttonPanel.Margin = new Padding(0, 12, 0, 0);
            buttonPanel.Name = "buttonPanel";
            buttonPanel.Padding = new Padding(0);
            buttonPanel.TabIndex = 16;
            buttonPanel.WrapContents = false;
            //
            // cancelButton
            //
            cancelButton.AutoSize = true;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Margin = new Padding(8, 0, 0, 0);
            cancelButton.Name = "cancelButton";
            cancelButton.TabIndex = 0;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            //
            // addButton
            //
            addButton.AutoSize = true;
            addButton.Name = "addButton";
            addButton.TabIndex = 1;
            addButton.Text = "Add";
            addButton.UseVisualStyleBackColor = true;
            addButton.Click += AddButton_Click;
            //
            // AddRequestDialog
            //
            AcceptButton = addButton;
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new Size(700, 470);
            Controls.Add(layout);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            MinimumSize = new Size(700, 470);
            Name = "AddRequestDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Add Request";
            layout.ResumeLayout(false);
            layout.PerformLayout();
            buttonPanel.ResumeLayout(false);
            buttonPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel layout;
        private Label typeLabel;
        private ComboBox typeComboBox;
        private Label nameLabel;
        private TextBox nameTextBox;
        private Label descriptionLabel;
        private TextBox descriptionTextBox;
        private Label categoryLabel;
        private TextBox categoryTextBox;
        private Label unitLabel;
        private TextBox unitTextBox;
        private Label pidsLabel;
        private TextBox pidsTextBox;
        private Label pidHighLabel;
        private TextBox pidHighTextBox;
        private Label pidLowLabel;
        private TextBox pidLowTextBox;
        private FlowLayoutPanel buttonPanel;
        private Button addButton;
        private Button cancelButton;
    }
}
