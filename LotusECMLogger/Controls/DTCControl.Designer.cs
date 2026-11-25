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
            components = new System.ComponentModel.Container();
            dtcTabControl = new TabControl();
            standardTab = new TabPage();
            mode13Tab = new TabPage();
            mode13Control = new Mode13Control();
            topPanel = new Panel();
            readCodesButton = new Button();
            readFreezeFrameButton = new Button();
            clearCodesButton = new Button();
            clearTcuCodesButton = new Button();
            statusLabel = new Label();
            statusToolTip = new ToolTip(components);
            resultsSplit = new SplitContainer();
            dtcListView = new ListView();
            freezeFrameListView = new ListView();
            dtcTabControl.SuspendLayout();
            standardTab.SuspendLayout();
            mode13Tab.SuspendLayout();
            topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)resultsSplit).BeginInit();
            resultsSplit.Panel1.SuspendLayout();
            resultsSplit.Panel2.SuspendLayout();
            resultsSplit.SuspendLayout();
            SuspendLayout();
            //
            // dtcTabControl
            //
            dtcTabControl.Controls.Add(standardTab);
            dtcTabControl.Controls.Add(mode13Tab);
            dtcTabControl.Dock = DockStyle.Fill;
            dtcTabControl.Location = new Point(0, 0);
            dtcTabControl.Name = "dtcTabControl";
            dtcTabControl.SelectedIndex = 0;
            dtcTabControl.Size = new Size(600, 489);
            dtcTabControl.TabIndex = 0;
            //
            // standardTab
            //
            standardTab.Controls.Add(resultsSplit);
            standardTab.Controls.Add(topPanel);
            standardTab.Location = new Point(4, 24);
            standardTab.Name = "standardTab";
            standardTab.Padding = new Padding(3);
            standardTab.Size = new Size(592, 461);
            standardTab.TabIndex = 0;
            standardTab.Text = "Standard OBD-II";
            standardTab.UseVisualStyleBackColor = true;
            //
            // mode13Tab
            //
            mode13Tab.Controls.Add(mode13Control);
            mode13Tab.Location = new Point(4, 24);
            mode13Tab.Name = "mode13Tab";
            mode13Tab.Padding = new Padding(3);
            mode13Tab.Size = new Size(592, 461);
            mode13Tab.TabIndex = 1;
            mode13Tab.Text = "Mode 0x13 (All Codes)";
            mode13Tab.UseVisualStyleBackColor = true;
            //
            // mode13Control
            //
            mode13Control.Dock = DockStyle.Fill;
            mode13Control.Location = new Point(3, 3);
            mode13Control.Margin = new Padding(3, 2, 3, 2);
            mode13Control.Name = "mode13Control";
            mode13Control.Size = new Size(586, 455);
            mode13Control.TabIndex = 0;
            //
            // topPanel
            //
            topPanel.Controls.Add(readCodesButton);
            topPanel.Controls.Add(readFreezeFrameButton);
            topPanel.Controls.Add(clearCodesButton);
            topPanel.Controls.Add(clearTcuCodesButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(600, 84);
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
            clearCodesButton.Size = new Size(150, 32);
            clearCodesButton.TabIndex = 2;
            clearCodesButton.Text = "Clear Engine Codes";
            clearCodesButton.UseVisualStyleBackColor = true;
            clearCodesButton.Click += clearCodesButton_Click;
            //
            // clearTcuCodesButton
            //
            clearTcuCodesButton.Location = new Point(450, 12);
            clearTcuCodesButton.Name = "clearTcuCodesButton";
            clearTcuCodesButton.Size = new Size(130, 32);
            clearTcuCodesButton.TabIndex = 3;
            clearTcuCodesButton.Text = "Clear TCU Codes";
            statusToolTip.SetToolTip(clearTcuCodesButton, "Clear transmission control unit (TCU) trouble codes. Engine codes are not affected.");
            clearTcuCodesButton.UseVisualStyleBackColor = true;
            clearTcuCodesButton.Click += clearTcuCodesButton_Click;
            //
            // statusLabel
            //
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.AutoEllipsis = true;
            statusLabel.Location = new Point(12, 55);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(576, 15);
            statusLabel.TabIndex = 4;
            //
            // resultsSplit
            //
            resultsSplit.Dock = DockStyle.Fill;
            resultsSplit.Location = new Point(0, 84);
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
            resultsSplit.Size = new Size(600, 405);
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
            freezeFrameListView.Size = new Size(600, 144);
            freezeFrameListView.TabIndex = 0;
            freezeFrameListView.UseCompatibleStateImageBehavior = false;
            freezeFrameListView.View = View.Details;
            //
            // DTCControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(dtcTabControl);
            Margin = new Padding(3, 2, 3, 2);
            Name = "DTCControl";
            Size = new Size(600, 489);
            dtcTabControl.ResumeLayout(false);
            standardTab.ResumeLayout(false);
            mode13Tab.ResumeLayout(false);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            resultsSplit.Panel1.ResumeLayout(false);
            resultsSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)resultsSplit).EndInit();
            resultsSplit.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TabControl dtcTabControl;
        private TabPage standardTab;
        private TabPage mode13Tab;
        private Mode13Control mode13Control;
        private Panel topPanel;
        private Button readCodesButton;
        private Button readFreezeFrameButton;
        private Button clearCodesButton;
        private Button clearTcuCodesButton;
        private SplitContainer resultsSplit;
        private ListView dtcListView;
        private ListView freezeFrameListView;
        private Label statusLabel;
        private ToolTip statusToolTip;
    }
}
