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
            filePathValueTextBox = new TextBox();
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
            addRequestButton = new Button();
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
            topButtonPanel.Controls.Add(refreshConfigsButton);
            topButtonPanel.Controls.Add(newConfigButton);
            topButtonPanel.Controls.Add(saveConfigButton);
            topButtonPanel.Controls.Add(saveAsConfigButton);
            topButtonPanel.Dock = DockStyle.Fill;
            topButtonPanel.Location = new Point(2, 2);
            topButtonPanel.Margin = new Padding(2);
            topButtonPanel.Name = "topButtonPanel";
            topButtonPanel.Padding = new Padding(0, 2, 0, 2);
            topButtonPanel.Size = new Size(690, 31);
            topButtonPanel.TabIndex = 0;
            topButtonPanel.WrapContents = false;
            // 
            // configPickerLabel
            // 
            configPickerLabel.Anchor = AnchorStyles.Left;
            configPickerLabel.AutoSize = true;
            configPickerLabel.Location = new Point(2, 8);
            configPickerLabel.Margin = new Padding(2, 0, 2, 0);
            configPickerLabel.Name = "configPickerLabel";
            configPickerLabel.Size = new Size(70, 15);
            configPickerLabel.TabIndex = 0;
            configPickerLabel.Text = "Load config";
            // 
            // configPickerComboBox
            // 
            configPickerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            configPickerComboBox.FormattingEnabled = true;
            configPickerComboBox.Location = new Point(76, 4);
            configPickerComboBox.Margin = new Padding(2);
            configPickerComboBox.Name = "configPickerComboBox";
            configPickerComboBox.Size = new Size(176, 23);
            configPickerComboBox.TabIndex = 1;
            configPickerComboBox.SelectedIndexChanged += ConfigPickerComboBox_SelectedIndexChanged;
            // 
            // refreshConfigsButton
            // 
            refreshConfigsButton.Location = new Point(256, 4);
            refreshConfigsButton.Margin = new Padding(2);
            refreshConfigsButton.Name = "refreshConfigsButton";
            refreshConfigsButton.Size = new Size(63, 20);
            refreshConfigsButton.TabIndex = 2;
            refreshConfigsButton.Text = "Refresh";
            refreshConfigsButton.UseVisualStyleBackColor = true;
            refreshConfigsButton.Click += RefreshConfigsButton_Click;
            // 
            // newConfigButton
            // 
            newConfigButton.Location = new Point(323, 4);
            newConfigButton.Margin = new Padding(2);
            newConfigButton.Name = "newConfigButton";
            newConfigButton.Size = new Size(63, 20);
            newConfigButton.TabIndex = 3;
            newConfigButton.Text = "New";
            newConfigButton.UseVisualStyleBackColor = true;
            newConfigButton.Click += NewConfigButton_Click;
            // 
            // saveConfigButton
            // 
            saveConfigButton.Location = new Point(390, 4);
            saveConfigButton.Margin = new Padding(2);
            saveConfigButton.Name = "saveConfigButton";
            saveConfigButton.Size = new Size(63, 20);
            saveConfigButton.TabIndex = 4;
            saveConfigButton.Text = "Save";
            saveConfigButton.UseVisualStyleBackColor = true;
            saveConfigButton.Click += SaveConfigButton_Click;
            // 
            // saveAsConfigButton
            // 
            saveAsConfigButton.Location = new Point(457, 4);
            saveAsConfigButton.Margin = new Padding(2);
            saveAsConfigButton.Name = "saveAsConfigButton";
            saveAsConfigButton.Size = new Size(63, 20);
            saveAsConfigButton.TabIndex = 5;
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
            editorLayout.Margin = new Padding(2);
            editorLayout.Name = "editorLayout";
            editorLayout.RowCount = 3;
            editorLayout.RowStyles.Add(new RowStyle());
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
            editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            editorLayout.Size = new Size(694, 386);
            editorLayout.TabIndex = 0;
            // 
            // metadataGroupBox
            // 
            metadataGroupBox.Controls.Add(metadataLayout);
            metadataGroupBox.Dock = DockStyle.Fill;
            metadataGroupBox.Location = new Point(2, 37);
            metadataGroupBox.Margin = new Padding(2);
            metadataGroupBox.Name = "metadataGroupBox";
            metadataGroupBox.Padding = new Padding(2);
            metadataGroupBox.Size = new Size(690, 102);
            metadataGroupBox.TabIndex = 1;
            metadataGroupBox.TabStop = false;
            metadataGroupBox.Text = "Configuration Details";
            // 
            // metadataLayout
            // 
            metadataLayout.ColumnCount = 2;
            metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98F));
            metadataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            metadataLayout.Controls.Add(configNameLabel, 0, 0);
            metadataLayout.Controls.Add(configNameTextBox, 1, 0);
            metadataLayout.Controls.Add(configDescriptionLabel, 0, 1);
            metadataLayout.Controls.Add(configDescriptionTextBox, 1, 1);
            metadataLayout.Controls.Add(filePathLabel, 0, 2);
            metadataLayout.Controls.Add(filePathValueTextBox, 1, 2);
            metadataLayout.Dock = DockStyle.Fill;
            metadataLayout.Location = new Point(2, 18);
            metadataLayout.Margin = new Padding(2);
            metadataLayout.Name = "metadataLayout";
            metadataLayout.RowCount = 3;
            metadataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23F));
            metadataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37F));
            metadataLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            metadataLayout.Size = new Size(686, 82);
            metadataLayout.TabIndex = 0;
            // 
            // configNameLabel
            // 
            configNameLabel.Anchor = AnchorStyles.Left;
            configNameLabel.AutoSize = true;
            configNameLabel.Location = new Point(2, 4);
            configNameLabel.Margin = new Padding(2, 0, 2, 0);
            configNameLabel.Name = "configNameLabel";
            configNameLabel.Size = new Size(39, 15);
            configNameLabel.TabIndex = 0;
            configNameLabel.Text = "Name";
            // 
            // configNameTextBox
            // 
            configNameTextBox.Dock = DockStyle.Fill;
            configNameTextBox.Location = new Point(100, 2);
            configNameTextBox.Margin = new Padding(2);
            configNameTextBox.Name = "configNameTextBox";
            configNameTextBox.Size = new Size(584, 23);
            configNameTextBox.TabIndex = 1;
            configNameTextBox.TextChanged += EditorField_TextChanged;
            // 
            // configDescriptionLabel
            // 
            configDescriptionLabel.Anchor = AnchorStyles.Left;
            configDescriptionLabel.AutoSize = true;
            configDescriptionLabel.Location = new Point(2, 34);
            configDescriptionLabel.Margin = new Padding(2, 0, 2, 0);
            configDescriptionLabel.Name = "configDescriptionLabel";
            configDescriptionLabel.Size = new Size(67, 15);
            configDescriptionLabel.TabIndex = 2;
            configDescriptionLabel.Text = "Description";
            // 
            // configDescriptionTextBox
            // 
            configDescriptionTextBox.Dock = DockStyle.Fill;
            configDescriptionTextBox.Location = new Point(100, 25);
            configDescriptionTextBox.Margin = new Padding(2);
            configDescriptionTextBox.Multiline = true;
            configDescriptionTextBox.Name = "configDescriptionTextBox";
            configDescriptionTextBox.ScrollBars = ScrollBars.Vertical;
            configDescriptionTextBox.Size = new Size(584, 33);
            configDescriptionTextBox.TabIndex = 3;
            configDescriptionTextBox.TextChanged += EditorField_TextChanged;
            // 
            // filePathLabel
            // 
            filePathLabel.Anchor = AnchorStyles.Left;
            filePathLabel.AutoSize = true;
            filePathLabel.Location = new Point(2, 63);
            filePathLabel.Margin = new Padding(2, 0, 2, 0);
            filePathLabel.Name = "filePathLabel";
            filePathLabel.Size = new Size(52, 15);
            filePathLabel.TabIndex = 4;
            filePathLabel.Text = "File path";
            // 
            // filePathValueTextBox
            // 
            filePathValueTextBox.Dock = DockStyle.Fill;
            filePathValueTextBox.Location = new Point(100, 62);
            filePathValueTextBox.Margin = new Padding(2, 2, 2, 0);
            filePathValueTextBox.MinimumSize = new Size(4, 34);
            filePathValueTextBox.Name = "filePathValueTextBox";
            filePathValueTextBox.ReadOnly = true;
            filePathValueTextBox.ScrollBars = ScrollBars.Horizontal;
            filePathValueTextBox.Size = new Size(584, 34);
            filePathValueTextBox.TabIndex = 5;
            filePathValueTextBox.TabStop = false;
            filePathValueTextBox.Text = "Unsaved";
            // 
            // editorSplitContainer
            // 
            editorSplitContainer.Dock = DockStyle.Fill;
            editorSplitContainer.FixedPanel = FixedPanel.Panel1;
            editorSplitContainer.Location = new Point(2, 143);
            editorSplitContainer.Margin = new Padding(2);
            editorSplitContainer.Name = "editorSplitContainer";
            // 
            // editorSplitContainer.Panel1
            // 
            editorSplitContainer.Panel1.Controls.Add(ecuGroupBox);
            // 
            // editorSplitContainer.Panel2
            // 
            editorSplitContainer.Panel2.Controls.Add(requestEditorLayout);
            editorSplitContainer.Size = new Size(690, 241);
            editorSplitContainer.SplitterDistance = 178;
            editorSplitContainer.SplitterWidth = 6;
            editorSplitContainer.TabIndex = 2;
            // 
            // ecuGroupBox
            // 
            ecuGroupBox.Controls.Add(ecuLayout);
            ecuGroupBox.Dock = DockStyle.Fill;
            ecuGroupBox.Location = new Point(0, 0);
            ecuGroupBox.Margin = new Padding(2);
            ecuGroupBox.Name = "ecuGroupBox";
            ecuGroupBox.Padding = new Padding(2);
            ecuGroupBox.Size = new Size(178, 241);
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
            ecuLayout.Location = new Point(2, 18);
            ecuLayout.Margin = new Padding(2);
            ecuLayout.Name = "ecuLayout";
            ecuLayout.RowCount = 2;
            ecuLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            ecuLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            ecuLayout.Size = new Size(174, 221);
            ecuLayout.TabIndex = 0;
            // 
            // ecuListBox
            // 
            ecuListBox.Dock = DockStyle.Fill;
            ecuListBox.FormattingEnabled = true;
            ecuListBox.ItemHeight = 15;
            ecuListBox.Location = new Point(2, 2);
            ecuListBox.Margin = new Padding(2);
            ecuListBox.Name = "ecuListBox";
            ecuListBox.Size = new Size(170, 189);
            ecuListBox.TabIndex = 0;
            ecuListBox.SelectedIndexChanged += EcuListBox_SelectedIndexChanged;
            // 
            // ecuButtonPanel
            // 
            ecuButtonPanel.Controls.Add(addEcuButton);
            ecuButtonPanel.Controls.Add(removeEcuButton);
            ecuButtonPanel.Dock = DockStyle.Fill;
            ecuButtonPanel.Location = new Point(2, 195);
            ecuButtonPanel.Margin = new Padding(2);
            ecuButtonPanel.Name = "ecuButtonPanel";
            ecuButtonPanel.Size = new Size(170, 24);
            ecuButtonPanel.TabIndex = 1;
            ecuButtonPanel.WrapContents = false;
            // 
            // addEcuButton
            // 
            addEcuButton.Location = new Point(2, 2);
            addEcuButton.Margin = new Padding(2);
            addEcuButton.Name = "addEcuButton";
            addEcuButton.Size = new Size(63, 20);
            addEcuButton.TabIndex = 0;
            addEcuButton.Text = "Add ECU";
            addEcuButton.UseVisualStyleBackColor = true;
            addEcuButton.Click += AddEcuButton_Click;
            // 
            // removeEcuButton
            // 
            removeEcuButton.Location = new Point(69, 2);
            removeEcuButton.Margin = new Padding(2);
            removeEcuButton.Name = "removeEcuButton";
            removeEcuButton.Size = new Size(84, 20);
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
            requestEditorLayout.Margin = new Padding(2);
            requestEditorLayout.Name = "requestEditorLayout";
            requestEditorLayout.RowCount = 2;
            requestEditorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 114F));
            requestEditorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            requestEditorLayout.Size = new Size(506, 241);
            requestEditorLayout.TabIndex = 0;
            // 
            // ecuDetailsGroupBox
            // 
            ecuDetailsGroupBox.Controls.Add(ecuDetailsLayout);
            ecuDetailsGroupBox.Dock = DockStyle.Fill;
            ecuDetailsGroupBox.Location = new Point(2, 2);
            ecuDetailsGroupBox.Margin = new Padding(2);
            ecuDetailsGroupBox.Name = "ecuDetailsGroupBox";
            ecuDetailsGroupBox.Padding = new Padding(2);
            ecuDetailsGroupBox.Size = new Size(502, 110);
            ecuDetailsGroupBox.TabIndex = 0;
            ecuDetailsGroupBox.TabStop = false;
            ecuDetailsGroupBox.Text = "Selected ECU";
            // 
            // ecuDetailsLayout
            // 
            ecuDetailsLayout.ColumnCount = 2;
            ecuDetailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98F));
            ecuDetailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            ecuDetailsLayout.Controls.Add(ecuNameLabel, 0, 0);
            ecuDetailsLayout.Controls.Add(ecuNameTextBox, 1, 0);
            ecuDetailsLayout.Controls.Add(requestIdLabel, 0, 1);
            ecuDetailsLayout.Controls.Add(requestIdTextBox, 1, 1);
            ecuDetailsLayout.Controls.Add(responseIdLabel, 0, 2);
            ecuDetailsLayout.Controls.Add(responseIdTextBox, 1, 2);
            ecuDetailsLayout.Dock = DockStyle.Fill;
            ecuDetailsLayout.Location = new Point(2, 18);
            ecuDetailsLayout.Margin = new Padding(2);
            ecuDetailsLayout.Name = "ecuDetailsLayout";
            ecuDetailsLayout.RowCount = 3;
            ecuDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            ecuDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            ecuDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            ecuDetailsLayout.Size = new Size(498, 90);
            ecuDetailsLayout.TabIndex = 0;
            // 
            // ecuNameLabel
            // 
            ecuNameLabel.Anchor = AnchorStyles.Left;
            ecuNameLabel.AutoSize = true;
            ecuNameLabel.Location = new Point(2, 5);
            ecuNameLabel.Margin = new Padding(2, 0, 2, 0);
            ecuNameLabel.Name = "ecuNameLabel";
            ecuNameLabel.Size = new Size(62, 15);
            ecuNameLabel.TabIndex = 0;
            ecuNameLabel.Text = "ECU name";
            // 
            // ecuNameTextBox
            // 
            ecuNameTextBox.Dock = DockStyle.Fill;
            ecuNameTextBox.Location = new Point(100, 2);
            ecuNameTextBox.Margin = new Padding(2);
            ecuNameTextBox.Name = "ecuNameTextBox";
            ecuNameTextBox.Size = new Size(396, 34);
            ecuNameTextBox.TabIndex = 1;
            ecuNameTextBox.TextChanged += EditorField_TextChanged;
            // 
            // requestIdLabel
            // 
            requestIdLabel.Anchor = AnchorStyles.Left;
            requestIdLabel.AutoSize = true;
            requestIdLabel.Location = new Point(2, 31);
            requestIdLabel.Margin = new Padding(2, 0, 2, 0);
            requestIdLabel.Name = "requestIdLabel";
            requestIdLabel.Size = new Size(92, 15);
            requestIdLabel.TabIndex = 2;
            requestIdLabel.Text = "Request ID (hex)";
            // 
            // requestIdTextBox
            // 
            requestIdTextBox.Dock = DockStyle.Fill;
            requestIdTextBox.Location = new Point(100, 28);
            requestIdTextBox.Margin = new Padding(2);
            requestIdTextBox.Name = "requestIdTextBox";
            requestIdTextBox.Size = new Size(396, 34);
            requestIdTextBox.TabIndex = 3;
            requestIdTextBox.TextChanged += EditorField_TextChanged;
            // 
            // responseIdLabel
            // 
            responseIdLabel.Anchor = AnchorStyles.Left;
            responseIdLabel.AutoSize = true;
            responseIdLabel.Location = new Point(2, 52);
            responseIdLabel.Margin = new Padding(2, 0, 2, 0);
            responseIdLabel.Name = "responseIdLabel";
            responseIdLabel.Size = new Size(74, 26);
            responseIdLabel.TabIndex = 4;
            responseIdLabel.Text = "Response ID (hex)";
            // 
            // responseIdTextBox
            // 
            responseIdTextBox.Dock = DockStyle.Fill;
            responseIdTextBox.Location = new Point(100, 54);
            responseIdTextBox.Margin = new Padding(2);
            responseIdTextBox.Name = "responseIdTextBox";
            responseIdTextBox.Size = new Size(396, 34);
            responseIdTextBox.TabIndex = 5;
            responseIdTextBox.TextChanged += EditorField_TextChanged;
            // 
            // requestsGroupBox
            // 
            requestsGroupBox.Controls.Add(requestsLayout);
            requestsGroupBox.Dock = DockStyle.Fill;
            requestsGroupBox.Location = new Point(2, 103);
            requestsGroupBox.Margin = new Padding(2);
            requestsGroupBox.Name = "requestsGroupBox";
            requestsGroupBox.Padding = new Padding(2);
            requestsGroupBox.Size = new Size(502, 136);
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
            requestsLayout.Location = new Point(2, 18);
            requestsLayout.Margin = new Padding(2);
            requestsLayout.Name = "requestsLayout";
            requestsLayout.RowCount = 3;
            requestsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            requestsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            requestsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            requestsLayout.Size = new Size(498, 116);
            requestsLayout.TabIndex = 0;
            // 
            // requestButtonsPanel
            // 
            requestButtonsPanel.Controls.Add(addRequestButton);
            requestButtonsPanel.Controls.Add(removeRequestButton);
            requestButtonsPanel.Controls.Add(moveRequestUpButton);
            requestButtonsPanel.Controls.Add(moveRequestDownButton);
            requestButtonsPanel.Dock = DockStyle.Fill;
            requestButtonsPanel.Location = new Point(2, 2);
            requestButtonsPanel.Margin = new Padding(2);
            requestButtonsPanel.Name = "requestButtonsPanel";
            requestButtonsPanel.Size = new Size(494, 21);
            requestButtonsPanel.TabIndex = 0;
            requestButtonsPanel.WrapContents = false;
            // 
            // addRequestButton
            // 
            addRequestButton.AutoSize = true;
            addRequestButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            addRequestButton.Location = new Point(2, 2);
            addRequestButton.Margin = new Padding(2, 2, 6, 2);
            addRequestButton.MinimumSize = new Size(91, 18);
            addRequestButton.Name = "addRequestButton";
            addRequestButton.Padding = new Padding(7, 0, 7, 0);
            addRequestButton.Size = new Size(98, 25);
            addRequestButton.TabIndex = 0;
            addRequestButton.Text = "Add Request";
            addRequestButton.UseVisualStyleBackColor = true;
            addRequestButton.Click += AddRequestButton_Click;
            // 
            // removeRequestButton
            // 
            removeRequestButton.AutoSize = true;
            removeRequestButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            removeRequestButton.Location = new Point(108, 2);
            removeRequestButton.Margin = new Padding(2, 2, 6, 2);
            removeRequestButton.MinimumSize = new Size(97, 18);
            removeRequestButton.Name = "removeRequestButton";
            removeRequestButton.Padding = new Padding(7, 0, 7, 0);
            removeRequestButton.Size = new Size(119, 25);
            removeRequestButton.TabIndex = 1;
            removeRequestButton.Text = "Remove Request";
            removeRequestButton.UseVisualStyleBackColor = true;
            removeRequestButton.Click += RemoveRequestButton_Click;
            // 
            // moveRequestUpButton
            // 
            moveRequestUpButton.AutoSize = true;
            moveRequestUpButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            moveRequestUpButton.Location = new Point(235, 2);
            moveRequestUpButton.Margin = new Padding(2, 2, 6, 2);
            moveRequestUpButton.MinimumSize = new Size(63, 18);
            moveRequestUpButton.Name = "moveRequestUpButton";
            moveRequestUpButton.Padding = new Padding(7, 0, 7, 0);
            moveRequestUpButton.Size = new Size(79, 25);
            moveRequestUpButton.TabIndex = 2;
            moveRequestUpButton.Text = "Move Up";
            moveRequestUpButton.UseVisualStyleBackColor = true;
            moveRequestUpButton.Click += MoveRequestUpButton_Click;
            // 
            // moveRequestDownButton
            // 
            moveRequestDownButton.AutoSize = true;
            moveRequestDownButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            moveRequestDownButton.Location = new Point(322, 2);
            moveRequestDownButton.Margin = new Padding(2, 2, 6, 2);
            moveRequestDownButton.MinimumSize = new Size(77, 18);
            moveRequestDownButton.Name = "moveRequestDownButton";
            moveRequestDownButton.Padding = new Padding(7, 0, 7, 0);
            moveRequestDownButton.Size = new Size(95, 25);
            moveRequestDownButton.TabIndex = 3;
            moveRequestDownButton.Text = "Move Down";
            moveRequestDownButton.UseVisualStyleBackColor = true;
            moveRequestDownButton.Click += MoveRequestDownButton_Click;
            // 
            // requestsDataGridView
            // 
            requestsDataGridView.AllowUserToAddRows = false;
            requestsDataGridView.AllowUserToDeleteRows = false;
            requestsDataGridView.AllowUserToResizeRows = false;
            requestsDataGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            requestsDataGridView.Dock = DockStyle.Fill;
            requestsDataGridView.Location = new Point(2, 27);
            requestsDataGridView.Margin = new Padding(2);
            requestsDataGridView.MultiSelect = false;
            requestsDataGridView.Name = "requestsDataGridView";
            requestsDataGridView.RowHeadersVisible = false;
            requestsDataGridView.RowHeadersWidth = 28;
            requestsDataGridView.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
            requestsDataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            requestsDataGridView.Size = new Size(494, 65);
            requestsDataGridView.TabIndex = 1;
            requestsDataGridView.CellValueChanged += RequestsDataGridView_CellValueChanged;
            requestsDataGridView.CurrentCellDirtyStateChanged += RequestsDataGridView_CurrentCellDirtyStateChanged;
            requestsDataGridView.UserDeletingRow += RequestsDataGridView_UserDeletingRow;
            // 
            // requestHelpLabel
            // 
            requestHelpLabel.Anchor = AnchorStyles.Left;
            requestHelpLabel.AutoSize = true;
            requestHelpLabel.Location = new Point(2, 97);
            requestHelpLabel.Margin = new Padding(2, 0, 2, 0);
            requestHelpLabel.Name = "requestHelpLabel";
            requestHelpLabel.Size = new Size(399, 15);
            requestHelpLabel.TabIndex = 2;
            requestHelpLabel.Text = "Mode01 uses the PIDs column. Mode22 uses PID High and PID Low values.";
            // 
            // LoggingConfigEditorControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(editorLayout);
            Margin = new Padding(2);
            Name = "LoggingConfigEditorControl";
            Size = new Size(694, 386);
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
            requestButtonsPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)requestsDataGridView).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private FlowLayoutPanel topButtonPanel;
        private Label configPickerLabel;
        private ComboBox configPickerComboBox;
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
        private TextBox filePathValueTextBox;
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
        private Button addRequestButton;
        private Button removeRequestButton;
        private Button moveRequestUpButton;
        private Button moveRequestDownButton;
        private DataGridView requestsDataGridView;
        private Label requestHelpLabel;
    }
}
