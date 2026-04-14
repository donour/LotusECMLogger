namespace LotusECMLogger
{
    public partial class MainWindow : Form
    {
        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Stop logger via control before disposing
                obdLoggerControl?.StopLogger();

                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            cliRunnerToolStripMenuItem = new ToolStripMenuItem();
            helpToolStripMenuItem = new ToolStripMenuItem();
            userGuideToolStripMenuItem = new ToolStripMenuItem();
            aboutLotusECMLoggerToolStripMenuItem = new ToolStripMenuItem();
            statusStrip1 = new StatusStrip();
            refreshRateLabel = new ToolStripStatusLabel();
            mainTabControl = new TabControl();
            vehicleInfoTab = new TabPage();
            vehicleInfoControl = new LotusECMLogger.Controls.VehicleInfoControl();
            liveDataTab = new TabPage();
            loggingTabControl = new TabControl();
            loggerTab = new TabPage();
            configEditorTab = new TabPage();
            loggingConfigEditorControl = new LotusECMLogger.Controls.LoggingConfigEditorControl();
            codingDataTab = new TabPage();
            dtcTab = new TabPage();
            dtcControl = new LotusECMLogger.Controls.DTCControl();
            t6RmaTab = new TabPage();
            liveTuningTab = new TabPage();
            liveTuningControl = new LotusECMLogger.Controls.LiveTuningDiskMonitorControl();
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            mainTabControl.SuspendLayout();
            vehicleInfoTab.SuspendLayout();
            liveDataTab.SuspendLayout();
            loggingTabControl.SuspendLayout();
            configEditorTab.SuspendLayout();
            dtcTab.SuspendLayout();
            liveTuningTab.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.ImageScalingSize = new Size(20, 20);
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, helpToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Padding = new Padding(9, 4, 0, 4);
            menuStrip1.Size = new Size(1000, 37);
            menuStrip1.TabIndex = 7;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { cliRunnerToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(54, 29);
            fileToolStripMenuItem.Text = "File";
            // 
            // cliRunnerToolStripMenuItem
            // 
            cliRunnerToolStripMenuItem.Name = "cliRunnerToolStripMenuItem";
            cliRunnerToolStripMenuItem.Size = new Size(292, 34);
            cliRunnerToolStripMenuItem.Text = "T6E Calibration Flasher";
            cliRunnerToolStripMenuItem.Click += CLIRunnerToolStripMenuItem_Click;
            // 
            // helpToolStripMenuItem
            // 
            helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { userGuideToolStripMenuItem, aboutLotusECMLoggerToolStripMenuItem });
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(65, 29);
            helpToolStripMenuItem.Text = "Help";
            // 
            // userGuideToolStripMenuItem
            // 
            userGuideToolStripMenuItem.Name = "userGuideToolStripMenuItem";
            userGuideToolStripMenuItem.Size = new Size(304, 34);
            userGuideToolStripMenuItem.Text = "User Guide";
            userGuideToolStripMenuItem.Click += UserGuideToolStripMenuItem_Click;
            // 
            // aboutLotusECMLoggerToolStripMenuItem
            // 
            aboutLotusECMLoggerToolStripMenuItem.Name = "aboutLotusECMLoggerToolStripMenuItem";
            aboutLotusECMLoggerToolStripMenuItem.Size = new Size(304, 34);
            aboutLotusECMLoggerToolStripMenuItem.Text = "About LotusECMLogger";
            aboutLotusECMLoggerToolStripMenuItem.Click += AboutLotusECMLoggerToolStripMenuItem_Click;
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { refreshRateLabel });
            statusStrip1.Location = new Point(0, 718);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Padding = new Padding(1, 0, 18, 0);
            statusStrip1.Size = new Size(1000, 32);
            statusStrip1.TabIndex = 8;
            statusStrip1.Text = "statusStrip1";
            // 
            // refreshRateLabel
            // 
            refreshRateLabel.Name = "refreshRateLabel";
            refreshRateLabel.Size = new Size(73, 25);
            refreshRateLabel.Text = "no data";
            // 
            // mainTabControl
            // 
            mainTabControl.Controls.Add(vehicleInfoTab);
            mainTabControl.Controls.Add(liveDataTab);
            mainTabControl.Controls.Add(codingDataTab);
            mainTabControl.Controls.Add(dtcTab);
            mainTabControl.Controls.Add(t6RmaTab);
            mainTabControl.Controls.Add(liveTuningTab);
            mainTabControl.Dock = DockStyle.Fill;
            mainTabControl.Location = new Point(0, 37);
            mainTabControl.Margin = new Padding(4);
            mainTabControl.Multiline = true;
            mainTabControl.Name = "mainTabControl";
            mainTabControl.SelectedIndex = 0;
            mainTabControl.Size = new Size(1000, 681);
            mainTabControl.TabIndex = 0;
            //
            // vehicleInfoTab
            //
            vehicleInfoTab.Controls.Add(vehicleInfoControl);
            vehicleInfoTab.Location = new Point(4, 64);
            vehicleInfoTab.Margin = new Padding(4);
            vehicleInfoTab.Name = "vehicleInfoTab";
            vehicleInfoTab.Size = new Size(992, 613);
            vehicleInfoTab.TabIndex = 2;
            vehicleInfoTab.Text = "Vehicle Information";
            vehicleInfoTab.UseVisualStyleBackColor = true;
            //
            // vehicleInfoControl
            //
            vehicleInfoControl.Dock = DockStyle.Fill;
            vehicleInfoControl.Location = new Point(0, 0);
            vehicleInfoControl.Name = "vehicleInfoControl";
            vehicleInfoControl.Size = new Size(992, 613);
            vehicleInfoControl.TabIndex = 0;
            //
            // liveDataTab
            //
            liveDataTab.Controls.Add(loggingTabControl);
            liveDataTab.Location = new Point(4, 64);
            liveDataTab.Margin = new Padding(4);
            liveDataTab.Name = "liveDataTab";
            liveDataTab.Size = new Size(992, 613);
            liveDataTab.TabIndex = 0;
            liveDataTab.Text = "Live Data";
            liveDataTab.UseVisualStyleBackColor = true;
            //
            // loggingTabControl
            //
            loggingTabControl.Controls.Add(loggerTab);
            loggingTabControl.Controls.Add(configEditorTab);
            loggingTabControl.Dock = DockStyle.Fill;
            loggingTabControl.Location = new Point(0, 0);
            loggingTabControl.Name = "loggingTabControl";
            loggingTabControl.SelectedIndex = 0;
            loggingTabControl.Size = new Size(992, 613);
            loggingTabControl.TabIndex = 0;
            //
            // loggerTab
            //
            loggerTab.Location = new Point(4, 34);
            loggerTab.Name = "loggerTab";
            loggerTab.Size = new Size(984, 575);
            loggerTab.TabIndex = 0;
            loggerTab.Text = "Logger";
            loggerTab.UseVisualStyleBackColor = true;
            //
            // configEditorTab
            //
            configEditorTab.Controls.Add(loggingConfigEditorControl);
            configEditorTab.Location = new Point(4, 34);
            configEditorTab.Name = "configEditorTab";
            configEditorTab.Size = new Size(984, 575);
            configEditorTab.TabIndex = 1;
            configEditorTab.Text = "Logging Config";
            configEditorTab.UseVisualStyleBackColor = true;
            //
            // loggingConfigEditorControl
            //
            loggingConfigEditorControl.Dock = DockStyle.Fill;
            loggingConfigEditorControl.Location = new Point(0, 0);
            loggingConfigEditorControl.Name = "loggingConfigEditorControl";
            loggingConfigEditorControl.Size = new Size(984, 575);
            loggingConfigEditorControl.TabIndex = 0;
            // 
            // codingDataTab
            // 
            codingDataTab.Location = new Point(4, 124);
            codingDataTab.Margin = new Padding(4);
            codingDataTab.Name = "codingDataTab";
            codingDataTab.Size = new Size(192, 0);
            codingDataTab.TabIndex = 1;
            codingDataTab.Text = "ECU Coding";
            codingDataTab.UseVisualStyleBackColor = true;
            // 
            // dtcTab
            // 
            dtcTab.Controls.Add(dtcControl);
            dtcTab.Location = new Point(4, 154);
            dtcTab.Margin = new Padding(4);
            dtcTab.Name = "dtcTab";
            dtcTab.Size = new Size(192, 0);
            dtcTab.TabIndex = 3;
            dtcTab.Text = "Diagnostic Trouble Codes";
            dtcTab.UseVisualStyleBackColor = true;
            // 
            // dtcControl
            // 
            dtcControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dtcControl.Location = new Point(0, 0);
            dtcControl.Margin = new Padding(5, 4, 5, 4);
            dtcControl.Name = "dtcControl";
            dtcControl.Size = new Size(190, 0);
            dtcControl.TabIndex = 0;
            //
            // t6RmaTab
            // 
            t6RmaTab.Location = new Point(4, 214);
            t6RmaTab.Margin = new Padding(4);
            t6RmaTab.Name = "t6RmaTab";
            t6RmaTab.Size = new Size(192, 0);
            t6RmaTab.TabIndex = 5;
            t6RmaTab.Text = "T6 RMA Logging";
            t6RmaTab.UseVisualStyleBackColor = true;
            // 
            // liveTuningTab
            // 
            liveTuningTab.Controls.Add(liveTuningControl);
            liveTuningTab.Location = new Point(4, 244);
            liveTuningTab.Margin = new Padding(4);
            liveTuningTab.Name = "liveTuningTab";
            liveTuningTab.Size = new Size(192, 0);
            liveTuningTab.TabIndex = 6;
            liveTuningTab.Text = "Live Tuning";
            liveTuningTab.UseVisualStyleBackColor = true;
            // 
            // liveTuningControl
            // 
            liveTuningControl.Dock = DockStyle.Fill;
            liveTuningControl.Location = new Point(0, 0);
            liveTuningControl.Margin = new Padding(4, 5, 4, 5);
            liveTuningControl.Name = "liveTuningControl";
            liveTuningControl.Size = new Size(192, 0);
            liveTuningControl.TabIndex = 0;
            // 
            // MainWindow
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 750);
            Controls.Add(mainTabControl);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Margin = new Padding(4, 5, 4, 5);
            MinimumSize = new Size(906, 764);
            Name = "MainWindow";
            Text = "LotusECMLogger";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            mainTabControl.ResumeLayout(false);
            vehicleInfoTab.ResumeLayout(false);
            liveDataTab.ResumeLayout(false);
            loggingTabControl.ResumeLayout(false);
            configEditorTab.ResumeLayout(false);
            dtcTab.ResumeLayout(false);
            liveTuningTab.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem cliRunnerToolStripMenuItem;
        private ToolStripMenuItem userGuideToolStripMenuItem;
        private ToolStripMenuItem aboutLotusECMLoggerToolStripMenuItem;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel refreshRateLabel;
        private TabControl mainTabControl;
        private TabPage liveDataTab;
        private TabControl loggingTabControl;
        private TabPage loggerTab;
        private TabPage configEditorTab;
        private LotusECMLogger.Controls.LoggingConfigEditorControl loggingConfigEditorControl;
        private TabPage codingDataTab;
        private TabPage vehicleInfoTab;
        private LotusECMLogger.Controls.VehicleInfoControl vehicleInfoControl;
        private TabPage dtcTab;
        private LotusECMLogger.Controls.DTCControl dtcControl;
        private TabPage t6RmaTab;
        private TabPage liveTuningTab;
        private LotusECMLogger.Controls.LiveTuningDiskMonitorControl liveTuningControl;
    }
}
