namespace LotusECMLogger.Controls
{
    partial class LoggingConfigEditorControl
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

        #region Component Designer generated code

        private void InitializeComponent()
        {
            topButtonPanel = new FlowLayoutPanel();
            configPickerLabel = new Label();
            configPickerComboBox = new ComboBox();
            loadConfigButton = new Button();
            refreshConfigsButton = new Button();
            newConfigButton = new Button();
            saveConfigButton = new Button();
            saveAsConfigButton = new Button();
            editorLayout = new TableLayoutPanel();
            metadataGroupBox = new GroupBox();
            metadataLayout = new TableLayoutPanel();
            configNameLabel = new Label();
            configNameTextBox = new TextBox();
            configDescriptionLabel = new Label();
            configDescriptionTextBox = new TextBox();
            filePathLabel = new Label();
            filePathValueLabel = new Label();
            editorSplitContainer = new SplitContainer();
            ecuGroupBox = new GroupBox();
            ecuLayout = new TableLayoutPanel();
            ecuListBox = new ListBox();
            ecuButtonPanel = new FlowLayoutPanel();
            addEcuButton = new Button();
            removeEcuButton = new Button();
            requestEditorLayout = new TableLayoutPanel();
            ecuDetailsGroupBox = new GroupBox();
            ecuDetailsLayout = new TableLayoutPanel();
            ecuNameLabel = new Label();
            ecuNameTextBox = new TextBox();
            requestIdLabel = new Label();
            requestIdTextBox = new TextBox();
            responseIdLabel = new Label();
            responseIdTextBox = new TextBox();
            requestsGroupBox = new GroupBox();
            requestsLayout = new TableLayoutPanel();
            requestButtonsPanel = new FlowLayoutPanel();
            addMode01Button = new Button();
            addMode22Button = new Button();
            removeRequestButton = new Button();
            moveRequestUpButton = new Button();
            moveRequestDownButton = new Button();
            requestsDataGridView = new DataGridView();
            requestHelpLabel = new Label();
            topButtonPanel.SuspendLayout();
            editorLayout.SuspendLayout();
            metadataGroupBox.SuspendLayout();
            metadataLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)editorSplitContainer).BeginInit();
            editorSplitContainer.Panel1.SuspendLayout();
            editorSplitContainer.Panel2.SuspendLayout();
            editorSplitContainer.SuspendLayout();
            ecuGroupBox.SuspendLayout();
            ecuLayout.SuspendLayout();
            ecuButtonPanel.SuspendLayout();
            requestEditorLayout.SuspendLayout();
            ecuDetailsGroupBox.SuspendLayout();
            ecuDetailsLayout.SuspendLayout();
            requestsGroupBox.SuspendLayout();
            requestsLayout.SuspendLayout();
            requestButtonsPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)requestsDataGridView).BeginInit();
            SuspendLayout();
            //
            // topButtonPanel
            //
            topButtonPanel.AutoSize = true;
            topButtonPanel.Controls.Add(configPickerLabel);
            topButtonPanel.Controls.Add(configPickerComboBox);
            topButtonPanel.Controls.Add(loadConfigButton);
            topButtonPanel.Controls.Add(refreshConfigsButton);
            topButtonPanel.Controls.Add(newConfigButton);
            topButtonPanel.Controls.Add(saveConfigButton);
            topButtonPanel.Controls.Add(saveAsConfigButton);
            topButtonPanel.Dock = DockStyle.Fill;
            topButtonPanel.Location = new Point(3, 3);
            topButtonPanel.Name = "topButtonPanel";
            topButtonPanel.Padding = new Padding(0, 4, 0, 4);
            topButtonPanel.Size = new Size(986, 44);
            topButtonPanel.TabIndex = 0;
            topButtonPanel.WrapContents = false;
            //
            // configPickerLabel
            //
            configPickerLabel.Anchor = AnchorStyles.Left;
            configPickerLabel.AutoSize = true;
            configPickerLabel.Location = new Point(3, 10);
            configPickerLabel.Name = "configPickerLabel";
            configPickerLabel.Size = new Size(93, 25);
            configPickerLabel.TabIndex = 0;
            configPickerLabel.Text = "Load config";
            //
            // configPickerComboBox
            //
            configPickerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            configPickerComboBox.FormattingEnabled = true;
            configPickerComboBox.Location = new Point(102, 7);
            configPickerComboBox.Name = "configPickerComboBox";
            configPickerComboBox.Size = new Size(250, 33);
            configPickerComboBox.TabIndex = 1;
            //
            // loadConfigButton
            //
            loadConfigButton.Location = new Point(358, 7);
            loadConfigButton.Name = "loadConfigButton";
            loadConfigButton.Size = new Size(90, 34);
            loadConfigButton.TabIndex = 2;
            loadConfigButton.Text = "Load";
            loadConfigButton.UseVisualStyleBackColor = true;
            loadConfigButton.Click += LoadConfigButton_Click;
            //
            // refreshConfigsButton
            //
            refreshConfigsButton.Location = new Point(454, 7);
            refreshConfigsButton.Name = "refreshConfigsButton";
            refreshConfigsButton.Size = new Size(90, 34);
            refreshConfigsButton.TabIndex = 3;
            refreshConfigsButton.Text = "Refresh";
            refreshConfigsButton.UseVisualStyleBackColor = true;
            refreshConfigsButton.Click += RefreshConfigsButton_Click;
            //
            // newConfigButton
            //
            newConfigButton.Location = new Point(550, 7);
            newConfigButton.Name = "newConfigButton";
            newConfigButton.Size = new Size(90, 34);
            newConfigButton.TabIndex = 4;
            newConfigButton.Text = "New";
            newConfigButton.UseVisualStyleBackColor = true;
            newConfigButton.Click += NewConfigButton_Click;
            //
            // saveConfigButton
            //
            saveConfigButton.Location = new Point(646, 7);
            saveConfigButton.Name = "saveConfigButton";
            saveConfigButton.Size = new Size(90, 34);
            saveConfigButton.TabIndex = 5;
            saveConfigButton.Text = "Save";
            saveConfigButton.UseVisualStyleBackColor = true;
            saveConfigButton.Click += SaveConfigButton_Click;
            //
            // saveAsConfigButton
            //
            saveAsConfigButton.Location = new Point(742, 7);
            saveAsConfigButton.Name = "saveAsConfigButton";
            saveAsConfigButton.Size = new Size(90, 34);
            saveAsConfigButton.TabIndex = 6;
            saveAsConfigButton.Text = "Save As";
            saveAsConfigButton.UseVisualStyleBackColor = true;
            saveAsConfigButton.Click += SaveAsConfigButton_Click;
            //
            // editorLayout
            //
            editorLayout.ColumnCount = 1;
            editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            editorLayout.Controls.Add(topButtonPanel, 0, 0);
            editorLayout.Controls.Add(metadataGroupBox, 0, 1);
            editorLayout.Controls.Add(editorSplitContainer, 0, 2);
            editorLayout.Dock = DockStyle.Fill;
            editorLayout.Location = new Point(0, 0);
            editorLayout.Name = "editorLayout";
            editorLayout.RowCount = 3;
            editorLayout.RowStyles.Add(new RowStyle());
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            editorLayout.Size = new Size(992, 643);
            editorLayout.TabIndex = 0;
            //
            // metadataGroupBox
            //
            metadataGroupBox.Controls.Add(metadataLayout);
            metadataGroupBox.Dock = DockStyle.Fill;
            metadataGroupBox.Location = new Point(3, 53);
            metadataGroupBox.Name = "metadataGroupBox";
            metadataGroupBox.Size = new Size(986, 124);
            metadataGroupBox.TabIndex = 1;
            metadataGroupBox.TabStop = false;
            metadataGroupBox.Text = "Configuration Details";
            //
            // metadataLayout
            //
            metadataLayout.ColumnCount = 2;
            metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            metadataLayout.Controls.Add(configNameLabel, 0, 0);
            metadataLayout.Controls.Add(configNameTextBox, 1, 0);
            metadataLayout.Controls.Add(configDescriptionLabel, 0, 1);
            metadataLayout.Controls.Add(configDescriptionTextBox, 1, 1);
            metadataLayout.Controls.Add(filePathLabel, 0, 2);
            metadataLayout.Controls.Add(filePathValueLabel, 1, 2);
            metadataLayout.Dock = DockStyle.Fill;
            metadataLayout.Location = new Point(3, 27);
            metadataLayout.Name = "metadataLayout";
            metadataLayout.RowCount = 3;
            metadataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            metadataLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            metadataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            metadataLayout.Size = new Size(980, 94);
            metadataLayout.TabIndex = 0;
            //
            // configNameLabel
            //
            configNameLabel.Anchor = AnchorStyles.Left;
            configNameLabel.AutoSize = true;
            configNameLabel.Location = new Point(3, 5);
            configNameLabel.Name = "configNameLabel";
            configNameLabel.Size = new Size(60, 25);
            configNameLabel.TabIndex = 0;
            configNameLabel.Text = "Name";
            //
            // configNameTextBox
            //
            configNameTextBox.Dock = DockStyle.Fill;
            configNameTextBox.Location = new Point(143, 3);
            configNameTextBox.Name = "configNameTextBox";
            configNameTextBox.Size = new Size(834, 31);
            configNameTextBox.TabIndex = 1;
            configNameTextBox.TextChanged += EditorField_TextChanged;
            //
            // configDescriptionLabel
            //
            configDescriptionLabel.Anchor = AnchorStyles.Left;
            configDescriptionLabel.AutoSize = true;
            configDescriptionLabel.Location = new Point(3, 38);
            configDescriptionLabel.Name = "configDescriptionLabel";
            configDescriptionLabel.Size = new Size(102, 25);
            configDescriptionLabel.TabIndex = 2;
            configDescriptionLabel.Text = "Description";
            //
            // configDescriptionTextBox
            //
            configDescriptionTextBox.Dock = DockStyle.Fill;
            configDescriptionTextBox.Location = new Point(143, 39);
            configDescriptionTextBox.Multiline = true;
            configDescriptionTextBox.Name = "configDescriptionTextBox";
            configDescriptionTextBox.Size = new Size(834, 24);
            configDescriptionTextBox.TabIndex = 3;
            configDescriptionTextBox.TextChanged += EditorField_TextChanged;
            //
            // filePathLabel
            //
            filePathLabel.Anchor = AnchorStyles.Left;
            filePathLabel.AutoSize = true;
            filePathLabel.Location = new Point(3, 68);
            filePathLabel.Name = "filePathLabel";
            filePathLabel.Size = new Size(74, 25);
            filePathLabel.TabIndex = 4;
            filePathLabel.Text = "File path";
            //
            // filePathValueLabel
            //
            filePathValueLabel.Anchor = AnchorStyles.Left;
            filePathValueLabel.AutoEllipsis = true;
            filePathValueLabel.AutoSize = true;
            filePathValueLabel.Location = new Point(143, 68);
            filePathValueLabel.Name = "filePathValueLabel";
            filePathValueLabel.Size = new Size(74, 25);
            filePathValueLabel.TabIndex = 5;
            filePathValueLabel.Text = "Unsaved";
            //
            // editorSplitContainer
            //
            editorSplitContainer.Dock = DockStyle.Fill;
            editorSplitContainer.Location = new Point(3, 183);
            editorSplitContainer.Name = "editorSplitContainer";
            //
            // editorSplitContainer.Panel1
            //
            editorSplitContainer.Panel1.Controls.Add(ecuGroupBox);
            //
            // editorSplitContainer.Panel2
            //
            editorSplitContainer.Panel2.Controls.Add(requestEditorLayout);
            editorSplitContainer.Size = new Size(986, 457);
            editorSplitContainer.SplitterDistance = 255;
            editorSplitContainer.TabIndex = 2;
            //
            // ecuGroupBox
            //
            ecuGroupBox.Controls.Add(ecuLayout);
            ecuGroupBox.Dock = DockStyle.Fill;
            ecuGroupBox.Location = new Point(0, 0);
            ecuGroupBox.Name = "ecuGroupBox";
            ecuGroupBox.Size = new Size(255, 457);
            ecuGroupBox.TabIndex = 0;
            ecuGroupBox.TabStop = false;
            ecuGroupBox.Text = "ECUs";
            //
            // ecuLayout
            //
            ecuLayout.ColumnCount = 1;
            ecuLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            ecuLayout.Controls.Add(ecuListBox, 0, 0);
            ecuLayout.Controls.Add(ecuButtonPanel, 0, 1);
            ecuLayout.Dock = DockStyle.Fill;
            ecuLayout.Location = new Point(3, 27);
            ecuLayout.Name = "ecuLayout";
            ecuLayout.RowCount = 2;
            ecuLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            ecuLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            ecuLayout.Size = new Size(249, 427);
            ecuLayout.TabIndex = 0;
            //
            // ecuListBox
            //
            ecuListBox.Dock = DockStyle.Fill;
            ecuListBox.FormattingEnabled = true;
            ecuListBox.ItemHeight = 25;
            ecuListBox.Location = new Point(3, 3);
            ecuListBox.Name = "ecuListBox";
            ecuListBox.Size = new Size(243, 375);
            ecuListBox.TabIndex = 0;
            ecuListBox.SelectedIndexChanged += EcuListBox_SelectedIndexChanged;
            //
            // ecuButtonPanel
            //
            ecuButtonPanel.Controls.Add(addEcuButton);
            ecuButtonPanel.Controls.Add(removeEcuButton);
            ecuButtonPanel.Dock = DockStyle.Fill;
            ecuButtonPanel.Location = new Point(3, 384);
            ecuButtonPanel.Name = "ecuButtonPanel";
            ecuButtonPanel.Size = new Size(243, 40);
            ecuButtonPanel.TabIndex = 1;
            ecuButtonPanel.WrapContents = false;
            //
            // addEcuButton
            //
            addEcuButton.Location = new Point(3, 3);
            addEcuButton.Name = "addEcuButton";
            addEcuButton.Size = new Size(90, 34);
            addEcuButton.TabIndex = 0;
            addEcuButton.Text = "Add ECU";
            addEcuButton.UseVisualStyleBackColor = true;
            addEcuButton.Click += AddEcuButton_Click;
            //
            // removeEcuButton
            //
            removeEcuButton.Location = new Point(99, 3);
            removeEcuButton.Name = "removeEcuButton";
            removeEcuButton.Size = new Size(120, 34);
            removeEcuButton.TabIndex = 1;
            removeEcuButton.Text = "Remove ECU";
            removeEcuButton.UseVisualStyleBackColor = true;
            removeEcuButton.Click += RemoveEcuButton_Click;
            //
            // requestEditorLayout
            //
            requestEditorLayout.ColumnCount = 1;
            requestEditorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            requestEditorLayout.Controls.Add(ecuDetailsGroupBox, 0, 0);
            requestEditorLayout.Controls.Add(requestsGroupBox, 0, 1);
            requestEditorLayout.Dock = DockStyle.Fill;
            requestEditorLayout.Location = new Point(0, 0);
            requestEditorLayout.Name = "requestEditorLayout";
            requestEditorLayout.RowCount = 2;
            requestEditorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
            requestEditorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            requestEditorLayout.Size = new Size(727, 457);
            requestEditorLayout.TabIndex = 0;
            //
            // ecuDetailsGroupBox
            //
            ecuDetailsGroupBox.Controls.Add(ecuDetailsLayout);
            ecuDetailsGroupBox.Dock = DockStyle.Fill;
            ecuDetailsGroupBox.Location = new Point(3, 3);
            ecuDetailsGroupBox.Name = "ecuDetailsGroupBox";
            ecuDetailsGroupBox.Size = new Size(721, 104);
            ecuDetailsGroupBox.TabIndex = 0;
            ecuDetailsGroupBox.TabStop = false;
            ecuDetailsGroupBox.Text = "Selected ECU";
            //
            // ecuDetailsLayout
            //
            ecuDetailsLayout.ColumnCount = 2;
            ecuDetailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            ecuDetailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            ecuDetailsLayout.Controls.Add(ecuNameLabel, 0, 0);
            ecuDetailsLayout.Controls.Add(ecuNameTextBox, 1, 0);
            ecuDetailsLayout.Controls.Add(requestIdLabel, 0, 1);
            ecuDetailsLayout.Controls.Add(requestIdTextBox, 1, 1);
            ecuDetailsLayout.Controls.Add(responseIdLabel, 0, 2);
            ecuDetailsLayout.Controls.Add(responseIdTextBox, 1, 2);
            ecuDetailsLayout.Dock = DockStyle.Fill;
            ecuDetailsLayout.Location = new Point(3, 27);
            ecuDetailsLayout.Name = "ecuDetailsLayout";
            ecuDetailsLayout.RowCount = 3;
            ecuDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333321F));
            ecuDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333321F));
            ecuDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333321F));
            ecuDetailsLayout.Size = new Size(715, 74);
            ecuDetailsLayout.TabIndex = 0;
            //
            // ecuNameLabel
            //
            ecuNameLabel.Anchor = AnchorStyles.Left;
            ecuNameLabel.AutoSize = true;
            ecuNameLabel.Location = new Point(3, 0);
            ecuNameLabel.Name = "ecuNameLabel";
            ecuNameLabel.Size = new Size(91, 24);
            ecuNameLabel.TabIndex = 0;
            ecuNameLabel.Text = "ECU name";
            //
            // ecuNameTextBox
            //
            ecuNameTextBox.Dock = DockStyle.Fill;
            ecuNameTextBox.Location = new Point(143, 3);
            ecuNameTextBox.Name = "ecuNameTextBox";
            ecuNameTextBox.Size = new Size(569, 31);
            ecuNameTextBox.TabIndex = 1;
            ecuNameTextBox.TextChanged += EditorField_TextChanged;
            //
            // requestIdLabel
            //
            requestIdLabel.Anchor = AnchorStyles.Left;
            requestIdLabel.AutoSize = true;
            requestIdLabel.Location = new Point(3, 24);
            requestIdLabel.Name = "requestIdLabel";
            requestIdLabel.Size = new Size(128, 24);
            requestIdLabel.TabIndex = 2;
            requestIdLabel.Text = "Request ID (hex)";
            //
            // requestIdTextBox
            //
            requestIdTextBox.Dock = DockStyle.Fill;
            requestIdTextBox.Location = new Point(143, 27);
            requestIdTextBox.Name = "requestIdTextBox";
            requestIdTextBox.Size = new Size(569, 31);
            requestIdTextBox.TabIndex = 3;
            requestIdTextBox.TextChanged += EditorField_TextChanged;
            //
            // responseIdLabel
            //
            responseIdLabel.Anchor = AnchorStyles.Left;
            responseIdLabel.AutoSize = true;
            responseIdLabel.Location = new Point(3, 49);
            responseIdLabel.Name = "responseIdLabel";
            responseIdLabel.Size = new Size(137, 25);
            responseIdLabel.TabIndex = 4;
            responseIdLabel.Text = "Response ID (hex)";
            //
            // responseIdTextBox
            //
            responseIdTextBox.Dock = DockStyle.Fill;
            responseIdTextBox.Location = new Point(143, 51);
            responseIdTextBox.Name = "responseIdTextBox";
            responseIdTextBox.Size = new Size(569, 31);
            responseIdTextBox.TabIndex = 5;
            responseIdTextBox.TextChanged += EditorField_TextChanged;
            //
            // requestsGroupBox
            //
            requestsGroupBox.Controls.Add(requestsLayout);
            requestsGroupBox.Dock = DockStyle.Fill;
            requestsGroupBox.Location = new Point(3, 113);
            requestsGroupBox.Name = "requestsGroupBox";
            requestsGroupBox.Size = new Size(721, 341);
            requestsGroupBox.TabIndex = 1;
            requestsGroupBox.TabStop = false;
            requestsGroupBox.Text = "Requests";
            //
            // requestsLayout
            //
            requestsLayout.ColumnCount = 1;
            requestsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            requestsLayout.Controls.Add(requestButtonsPanel, 0, 0);
            requestsLayout.Controls.Add(requestsDataGridView, 0, 1);
            requestsLayout.Controls.Add(requestHelpLabel, 0, 2);
            requestsLayout.Dock = DockStyle.Fill;
            requestsLayout.Location = new Point(3, 27);
            requestsLayout.Name = "requestsLayout";
            requestsLayout.RowCount = 3;
            requestsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            requestsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            requestsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            requestsLayout.Size = new Size(715, 311);
            requestsLayout.TabIndex = 0;
            //
            // requestButtonsPanel
            //
            requestButtonsPanel.Controls.Add(addMode01Button);
            requestButtonsPanel.Controls.Add(addMode22Button);
            requestButtonsPanel.Controls.Add(removeRequestButton);
            requestButtonsPanel.Controls.Add(moveRequestUpButton);
            requestButtonsPanel.Controls.Add(moveRequestDownButton);
            requestButtonsPanel.Dock = DockStyle.Fill;
            requestButtonsPanel.Location = new Point(3, 3);
            requestButtonsPanel.Name = "requestButtonsPanel";
            requestButtonsPanel.Size = new Size(709, 36);
            requestButtonsPanel.TabIndex = 0;
            requestButtonsPanel.WrapContents = false;
            //
            // addMode01Button
            //
            addMode01Button.Location = new Point(3, 3);
            addMode01Button.Name = "addMode01Button";
            addMode01Button.Size = new Size(118, 30);
            addMode01Button.TabIndex = 0;
            addMode01Button.Text = "Add Mode01";
            addMode01Button.UseVisualStyleBackColor = true;
            addMode01Button.Click += AddMode01Button_Click;
            //
            // addMode22Button
            //
            addMode22Button.Location = new Point(127, 3);
            addMode22Button.Name = "addMode22Button";
            addMode22Button.Size = new Size(118, 30);
            addMode22Button.TabIndex = 1;
            addMode22Button.Text = "Add Mode22";
            addMode22Button.UseVisualStyleBackColor = true;
            addMode22Button.Click += AddMode22Button_Click;
            //
            // removeRequestButton
            //
            removeRequestButton.Location = new Point(251, 3);
            removeRequestButton.Name = "removeRequestButton";
            removeRequestButton.Size = new Size(138, 30);
            removeRequestButton.TabIndex = 2;
            removeRequestButton.Text = "Remove Request";
            removeRequestButton.UseVisualStyleBackColor = true;
            removeRequestButton.Click += RemoveRequestButton_Click;
            //
            // moveRequestUpButton
            //
            moveRequestUpButton.Location = new Point(395, 3);
            moveRequestUpButton.Name = "moveRequestUpButton";
            moveRequestUpButton.Size = new Size(89, 30);
            moveRequestUpButton.TabIndex = 3;
            moveRequestUpButton.Text = "Move Up";
            moveRequestUpButton.UseVisualStyleBackColor = true;
            moveRequestUpButton.Click += MoveRequestUpButton_Click;
            //
            // moveRequestDownButton
            //
            moveRequestDownButton.Location = new Point(490, 3);
            moveRequestDownButton.Name = "moveRequestDownButton";
            moveRequestDownButton.Size = new Size(109, 30);
            moveRequestDownButton.TabIndex = 4;
            moveRequestDownButton.Text = "Move Down";
            moveRequestDownButton.UseVisualStyleBackColor = true;
            moveRequestDownButton.Click += MoveRequestDownButton_Click;
            //
            // requestsDataGridView
            //
            requestsDataGridView.AllowUserToAddRows = false;
            requestsDataGridView.AllowUserToDeleteRows = false;
            requestsDataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            requestsDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            requestsDataGridView.Dock = DockStyle.Fill;
            requestsDataGridView.Location = new Point(3, 45);
            requestsDataGridView.MultiSelect = false;
            requestsDataGridView.Name = "requestsDataGridView";
            requestsDataGridView.RowHeadersWidth = 62;
            requestsDataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            requestsDataGridView.Size = new Size(709, 227);
            requestsDataGridView.TabIndex = 1;
            requestsDataGridView.CellValueChanged += RequestsDataGridView_CellValueChanged;
            requestsDataGridView.CurrentCellDirtyStateChanged += RequestsDataGridView_CurrentCellDirtyStateChanged;
            requestsDataGridView.UserDeletingRow += RequestsDataGridView_UserDeletingRow;
            //
            // requestHelpLabel
            //
            requestHelpLabel.Anchor = AnchorStyles.Left;
            requestHelpLabel.AutoSize = true;
            requestHelpLabel.Location = new Point(3, 281);
            requestHelpLabel.Name = "requestHelpLabel";
            requestHelpLabel.Size = new Size(558, 25);
            requestHelpLabel.TabIndex = 2;
            requestHelpLabel.Text = "Mode01 uses the PIDs column. Mode22 uses PID High and PID Low values.";
            //
            // LoggingConfigEditorControl
            //
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(editorLayout);
            Name = "LoggingConfigEditorControl";
            Size = new Size(992, 643);
            topButtonPanel.ResumeLayout(false);
            topButtonPanel.PerformLayout();
            editorLayout.ResumeLayout(false);
            editorLayout.PerformLayout();
            metadataGroupBox.ResumeLayout(false);
            metadataLayout.ResumeLayout(false);
            metadataLayout.PerformLayout();
            editorSplitContainer.Panel1.ResumeLayout(false);
            editorSplitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)editorSplitContainer).EndInit();
            editorSplitContainer.ResumeLayout(false);
            ecuGroupBox.ResumeLayout(false);
            ecuLayout.ResumeLayout(false);
            ecuButtonPanel.ResumeLayout(false);
            requestEditorLayout.ResumeLayout(false);
            ecuDetailsGroupBox.ResumeLayout(false);
            ecuDetailsLayout.ResumeLayout(false);
            ecuDetailsLayout.PerformLayout();
            requestsGroupBox.ResumeLayout(false);
            requestsLayout.ResumeLayout(false);
            requestsLayout.PerformLayout();
            requestButtonsPanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)requestsDataGridView).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private FlowLayoutPanel topButtonPanel;
        private Label configPickerLabel;
        private ComboBox configPickerComboBox;
        private Button loadConfigButton;
        private Button refreshConfigsButton;
        private Button newConfigButton;
        private Button saveConfigButton;
        private Button saveAsConfigButton;
        private TableLayoutPanel editorLayout;
        private GroupBox metadataGroupBox;
        private TableLayoutPanel metadataLayout;
        private Label configNameLabel;
        private TextBox configNameTextBox;
        private Label configDescriptionLabel;
        private TextBox configDescriptionTextBox;
        private Label filePathLabel;
        private Label filePathValueLabel;
        private SplitContainer editorSplitContainer;
        private GroupBox ecuGroupBox;
        private TableLayoutPanel ecuLayout;
        private ListBox ecuListBox;
        private FlowLayoutPanel ecuButtonPanel;
        private Button addEcuButton;
        private Button removeEcuButton;
        private TableLayoutPanel requestEditorLayout;
        private GroupBox ecuDetailsGroupBox;
        private TableLayoutPanel ecuDetailsLayout;
        private Label ecuNameLabel;
        private TextBox ecuNameTextBox;
        private Label requestIdLabel;
        private TextBox requestIdTextBox;
        private Label responseIdLabel;
        private TextBox responseIdTextBox;
        private GroupBox requestsGroupBox;
        private TableLayoutPanel requestsLayout;
        private FlowLayoutPanel requestButtonsPanel;
        private Button addMode01Button;
        private Button addMode22Button;
        private Button removeRequestButton;
        private Button moveRequestUpButton;
        private Button moveRequestDownButton;
        private DataGridView requestsDataGridView;
        private Label requestHelpLabel;
    }
}
