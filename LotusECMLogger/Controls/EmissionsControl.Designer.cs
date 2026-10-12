namespace LotusECMLogger.Controls
{
    partial class EmissionsControl
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
            runCheckButton = new Button();
            standardLabel = new Label();
            standardComboBox = new ComboBox();
            modelYearLabel = new Label();
            modelYearComboBox = new ComboBox();
            statusLabel = new Label();
            noteLabel = new Label();
            verdictLabel = new Label();
            resultsSplit = new SplitContainer();
            monitorsListView = new ListView();
            detailsSplit = new SplitContainer();
            criteriaListView = new ListView();
            detailsListView = new ListView();
            topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)resultsSplit).BeginInit();
            resultsSplit.Panel1.SuspendLayout();
            resultsSplit.Panel2.SuspendLayout();
            resultsSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)detailsSplit).BeginInit();
            detailsSplit.Panel1.SuspendLayout();
            detailsSplit.Panel2.SuspendLayout();
            detailsSplit.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.Controls.Add(runCheckButton);
            topPanel.Controls.Add(standardLabel);
            topPanel.Controls.Add(standardComboBox);
            topPanel.Controls.Add(modelYearLabel);
            topPanel.Controls.Add(modelYearComboBox);
            topPanel.Controls.Add(statusLabel);
            topPanel.Controls.Add(noteLabel);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(900, 84);
            topPanel.TabIndex = 0;
            //
            // runCheckButton
            //
            runCheckButton.Location = new Point(12, 12);
            runCheckButton.Name = "runCheckButton";
            runCheckButton.Size = new Size(170, 32);
            runCheckButton.TabIndex = 0;
            runCheckButton.Text = "Run Emissions Check";
            runCheckButton.UseVisualStyleBackColor = true;
            runCheckButton.Click += runCheckButton_Click;
            //
            // standardLabel
            //
            standardLabel.AutoSize = true;
            standardLabel.Location = new Point(196, 20);
            standardLabel.Name = "standardLabel";
            standardLabel.Size = new Size(56, 15);
            standardLabel.TabIndex = 1;
            standardLabel.Text = "Program:";
            //
            // standardComboBox
            //
            standardComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            standardComboBox.Location = new Point(258, 16);
            standardComboBox.Name = "standardComboBox";
            standardComboBox.Size = new Size(240, 23);
            standardComboBox.TabIndex = 2;
            standardComboBox.SelectedIndexChanged += standardComboBox_SelectedIndexChanged;
            //
            // modelYearLabel
            //
            modelYearLabel.AutoSize = true;
            modelYearLabel.Location = new Point(512, 20);
            modelYearLabel.Name = "modelYearLabel";
            modelYearLabel.Size = new Size(70, 15);
            modelYearLabel.TabIndex = 3;
            modelYearLabel.Text = "Model year:";
            //
            // modelYearComboBox
            //
            modelYearComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modelYearComboBox.Location = new Point(588, 16);
            modelYearComboBox.Name = "modelYearComboBox";
            modelYearComboBox.Size = new Size(90, 23);
            modelYearComboBox.TabIndex = 4;
            modelYearComboBox.SelectedIndexChanged += modelYearComboBox_SelectedIndexChanged;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(692, 20);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 5;
            //
            // noteLabel
            //
            noteLabel.AutoSize = true;
            noteLabel.ForeColor = SystemColors.GrayText;
            noteLabel.Location = new Point(12, 55);
            noteLabel.Name = "noteLabel";
            noteLabel.Size = new Size(0, 15);
            noteLabel.TabIndex = 6;
            //
            // verdictLabel
            //
            verdictLabel.Dock = DockStyle.Top;
            verdictLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            verdictLabel.Location = new Point(0, 84);
            verdictLabel.Name = "verdictLabel";
            verdictLabel.Padding = new Padding(12, 0, 12, 0);
            verdictLabel.Size = new Size(900, 40);
            verdictLabel.TabIndex = 1;
            verdictLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // resultsSplit
            //
            resultsSplit.Dock = DockStyle.Fill;
            resultsSplit.Location = new Point(0, 124);
            resultsSplit.Name = "resultsSplit";
            resultsSplit.Orientation = Orientation.Horizontal;
            //
            // resultsSplit.Panel1
            //
            resultsSplit.Panel1.Controls.Add(monitorsListView);
            //
            // resultsSplit.Panel2
            //
            resultsSplit.Panel2.Controls.Add(detailsSplit);
            resultsSplit.Size = new Size(900, 476);
            resultsSplit.SplitterDistance = 250;
            resultsSplit.TabIndex = 2;
            //
            // monitorsListView
            //
            monitorsListView.Dock = DockStyle.Fill;
            monitorsListView.FullRowSelect = true;
            monitorsListView.GridLines = true;
            monitorsListView.Location = new Point(0, 0);
            monitorsListView.Name = "monitorsListView";
            monitorsListView.Size = new Size(900, 250);
            monitorsListView.TabIndex = 0;
            monitorsListView.UseCompatibleStateImageBehavior = false;
            monitorsListView.View = View.Details;
            //
            // detailsSplit
            //
            detailsSplit.Dock = DockStyle.Fill;
            detailsSplit.Location = new Point(0, 0);
            detailsSplit.Name = "detailsSplit";
            //
            // detailsSplit.Panel1
            //
            detailsSplit.Panel1.Controls.Add(criteriaListView);
            //
            // detailsSplit.Panel2
            //
            detailsSplit.Panel2.Controls.Add(detailsListView);
            detailsSplit.Size = new Size(900, 222);
            detailsSplit.SplitterDistance = 450;
            detailsSplit.TabIndex = 0;
            //
            // criteriaListView
            //
            criteriaListView.Dock = DockStyle.Fill;
            criteriaListView.FullRowSelect = true;
            criteriaListView.GridLines = true;
            criteriaListView.Location = new Point(0, 0);
            criteriaListView.Name = "criteriaListView";
            criteriaListView.ShowItemToolTips = true;
            criteriaListView.Size = new Size(450, 222);
            criteriaListView.TabIndex = 0;
            criteriaListView.UseCompatibleStateImageBehavior = false;
            criteriaListView.View = View.Details;
            //
            // detailsListView
            //
            detailsListView.Dock = DockStyle.Fill;
            detailsListView.FullRowSelect = true;
            detailsListView.GridLines = true;
            detailsListView.Location = new Point(0, 0);
            detailsListView.Name = "detailsListView";
            detailsListView.ShowItemToolTips = true;
            detailsListView.Size = new Size(446, 222);
            detailsListView.TabIndex = 0;
            detailsListView.UseCompatibleStateImageBehavior = false;
            detailsListView.View = View.Details;
            //
            // EmissionsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(resultsSplit);
            Controls.Add(verdictLabel);
            Controls.Add(topPanel);
            Margin = new Padding(3, 2, 3, 2);
            Name = "EmissionsControl";
            Size = new Size(900, 600);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            resultsSplit.Panel1.ResumeLayout(false);
            resultsSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)resultsSplit).EndInit();
            resultsSplit.ResumeLayout(false);
            detailsSplit.Panel1.ResumeLayout(false);
            detailsSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)detailsSplit).EndInit();
            detailsSplit.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Button runCheckButton;
        private Label standardLabel;
        private ComboBox standardComboBox;
        private Label modelYearLabel;
        private ComboBox modelYearComboBox;
        private Label statusLabel;
        private Label noteLabel;
        private Label verdictLabel;
        private SplitContainer resultsSplit;
        private ListView monitorsListView;
        private SplitContainer detailsSplit;
        private ListView criteriaListView;
        private ListView detailsListView;
    }
}
