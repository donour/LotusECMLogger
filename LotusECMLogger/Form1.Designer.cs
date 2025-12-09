using SAE.J2534;
using System.ComponentModel;

namespace LotusECMLogger
{
    public partial class LoggerWindow : Form, INotifyPropertyChanged
    {
        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Stop and dispose logger first to prevent background thread exceptions
                logger?.Stop();
                logger?.Dispose();
                
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
            startLogger_button = new Button();
            stopLogger_button = new Button();
            currentLogfileName = new Label();
            obdConfigLabel = new Label();
            obdConfigComboBox = new ComboBox();
            liveDataView = new ListView();
            codingDataView = new ListView();
            menuStrip1 = new MenuStrip();
            helpToolStripMenuItem = new ToolStripMenuItem();
            cliRunnerToolStripMenuItem = new ToolStripMenuItem();
            aboutLotusECMLoggerToolStripMenuItem = new ToolStripMenuItem();
            statusStrip1 = new StatusStrip();
            refreshRateLabel = new ToolStripStatusLabel();
            mainTabControl = new TabControl();
            liveDataTab = new TabPage();
            loggerControlPanel = new Panel();
            codingDataTab = new TabPage();
            codingMainPanel = new Panel();
            codingScrollPanel = new Panel();
            codingTopPanel = new Panel();
            vehicleInfoTab = new TabPage();
            vehicleInfoControl = new LotusECMLogger.Controls.VehicleInfoControl();
            dtcTab = new TabPage();
            dtcControl = new LotusECMLogger.Controls.DTCControl();
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            mainTabControl.SuspendLayout();
            liveDataTab.SuspendLayout();
            loggerControlPanel.SuspendLayout();
            codingDataTab.SuspendLayout();
            codingMainPanel.SuspendLayout();
            dtcTab.SuspendLayout();
            SuspendLayout();
            // 
            // startLogger_button
            // 
            startLogger_button.Location = new Point(14, 19);
            startLogger_button.Margin = new Padding(3, 4, 3, 4);
            startLogger_button.Name = "startLogger_button";
            startLogger_button.Size = new Size(86, 32);
            startLogger_button.TabIndex = 3;
            startLogger_button.Text = "Start";
            startLogger_button.UseVisualStyleBackColor = true;
            startLogger_button.Click += startLogger_button_Click;
            // 
            // stopLogger_button
            // 
            stopLogger_button.Enabled = false;
            stopLogger_button.Location = new Point(106, 19);
            stopLogger_button.Margin = new Padding(3, 4, 3, 4);
            stopLogger_button.Name = "stopLogger_button";
            stopLogger_button.Size = new Size(86, 32);
            stopLogger_button.TabIndex = 4;
            stopLogger_button.Text = "Stop";
            stopLogger_button.UseVisualStyleBackColor = true;
            stopLogger_button.Click += stopLogger_button_Click;
            // 
            // currentLogfileName
            //
            currentLogfileName.AutoSize = true;
            currentLogfileName.Location = new Point(255, 29);
            currentLogfileName.Name = "currentLogfileName";
            currentLogfileName.Size = new Size(85, 32);
            currentLogfileName.TabIndex = 5;
            currentLogfileName.Text = "No Log File";
            //
            // obdConfigLabel
            //
            obdConfigLabel.AutoSize = true;
            obdConfigLabel.Location = new Point(450, 29);
            obdConfigLabel.Name = "obdConfigLabel";
            obdConfigLabel.Size = new Size(54, 20);
            obdConfigLabel.TabIndex = 8;
            obdConfigLabel.Text = "Config:";
            //
            // obdConfigComboBox
            //
            obdConfigComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            obdConfigComboBox.FormattingEnabled = true;
            obdConfigComboBox.Location = new Point(510, 25);
            obdConfigComboBox.Name = "obdConfigComboBox";
            obdConfigComboBox.Size = new Size(180, 32);
            obdConfigComboBox.TabIndex = 9;
            obdConfigComboBox.SelectedIndexChanged += ObdConfigComboBox_SelectedIndexChanged;
            //
            // liveDataView
            // 
            liveDataView.Dock = DockStyle.Fill;
            liveDataView.FullRowSelect = true;
            liveDataView.GridLines = true;
            liveDataView.Location = new Point(0, 60);
            liveDataView.Margin = new Padding(3, 4, 3, 4);
            liveDataView.MultiSelect = false;
            liveDataView.Name = "liveDataView";
            liveDataView.Size = new Size(705, 439);
            liveDataView.TabIndex = 6;
            liveDataView.UseCompatibleStateImageBehavior = false;
            liveDataView.View = View.Details;
            // TODO only enable this property on release builds
            liveDataView.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                          ?.SetValue(liveDataView, true, null); // Enable double buffering for smoother scrolling

            // 
            // codingDataView
            // 
            codingDataView.Location = new Point(0, 0);
            codingDataView.Name = "codingDataView";
            codingDataView.Size = new Size(121, 97);
            codingDataView.TabIndex = 0;
            codingDataView.UseCompatibleStateImageBehavior = false;
            // 
            // menuStrip1
            //
            menuStrip1.ImageScalingSize = new Size(20, 20);
            menuStrip1.Items.AddRange(new ToolStripItem[] { helpToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Padding = new Padding(7, 3, 0, 3);
            menuStrip1.Size = new Size(713, 30);
            menuStrip1.TabIndex = 7;
            menuStrip1.Text = "menuStrip1";
            //
            // helpToolStripMenuItem
            //
            helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { cliRunnerToolStripMenuItem, aboutLotusECMLoggerToolStripMenuItem });
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(55, 24);
            helpToolStripMenuItem.Text = "File";
            //
            // cliRunnerToolStripMenuItem
            //
            cliRunnerToolStripMenuItem.Name = "cliRunnerToolStripMenuItem";
            cliRunnerToolStripMenuItem.Size = new Size(249, 26);
            cliRunnerToolStripMenuItem.Text = "T6E Calibration Flasher";
            cliRunnerToolStripMenuItem.Click += CLIRunnerToolStripMenuItem_Click;
            //
            // aboutLotusECMLoggerToolStripMenuItem
            //
            aboutLotusECMLoggerToolStripMenuItem.Name = "aboutLotusECMLoggerToolStripMenuItem";
            aboutLotusECMLoggerToolStripMenuItem.Size = new Size(249, 26);
            aboutLotusECMLoggerToolStripMenuItem.Text = "About LotusECMLogger";
            aboutLotusECMLoggerToolStripMenuItem.Click += AboutLotusECMLoggerToolStripMenuItem_Click;
            //
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { refreshRateLabel });
            statusStrip1.Location = new Point(0, 562);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(713, 26);
            statusStrip1.TabIndex = 8;
            statusStrip1.Text = "statusStrip1";
            // 
            // refreshRateLabel
            // 
            refreshRateLabel.Name = "refreshRateLabel";
            refreshRateLabel.Size = new Size(60, 20);
            refreshRateLabel.Text = "no data";
            // 
            // mainTabControl
            // 
            mainTabControl.Controls.Add(vehicleInfoTab);
            mainTabControl.Controls.Add(liveDataTab);
            mainTabControl.Controls.Add(codingDataTab);
            mainTabControl.Controls.Add(dtcTab);
            obdResetTab = new TabPage();
            mainTabControl.Controls.Add(obdResetTab);
            t6RmaTab = new TabPage();
            mainTabControl.Controls.Add(t6RmaTab);
            mainTabControl.Dock = DockStyle.Fill;
            mainTabControl.Location = new Point(0, 30);
            mainTabControl.Name = "mainTabControl";
            mainTabControl.SelectedIndex = 0;
            mainTabControl.Size = new Size(713, 532);
            mainTabControl.TabIndex = 0;
            // 
            // liveDataTab
            // 
            liveDataTab.Controls.Add(liveDataView);
            liveDataTab.Controls.Add(loggerControlPanel);
            liveDataTab.Location = new Point(4, 29);
            liveDataTab.Name = "liveDataTab";
            liveDataTab.Size = new Size(705, 499);
            liveDataTab.TabIndex = 0;
            liveDataTab.Text = "Live Data";
            // 
            // loggerControlPanel
            //
            loggerControlPanel.Controls.Add(obdConfigComboBox);
            loggerControlPanel.Controls.Add(obdConfigLabel);
            loggerControlPanel.Controls.Add(startLogger_button);
            loggerControlPanel.Controls.Add(stopLogger_button);
            loggerControlPanel.Controls.Add(currentLogfileName);
            loggerControlPanel.Dock = DockStyle.Top;
            loggerControlPanel.Location = new Point(0, 0);
            loggerControlPanel.Name = "loggerControlPanel";
            loggerControlPanel.Size = new Size(705, 60);
            loggerControlPanel.TabIndex = 7;
            // 
            // codingDataTab
            // 
            codingDataTab.Controls.Add(codingMainPanel);
            codingDataTab.Location = new Point(4, 29);
            codingDataTab.Name = "codingDataTab";
            codingDataTab.Size = new Size(705, 494);
            codingDataTab.TabIndex = 1;
            codingDataTab.Text = "ECU Coding";
            // 
            // codingMainPanel
            // 
            codingMainPanel.Controls.Add(codingScrollPanel);
            codingMainPanel.Controls.Add(codingTopPanel);
            codingMainPanel.Dock = DockStyle.Fill;
            codingMainPanel.Location = new Point(0, 0);
            codingMainPanel.Name = "codingMainPanel";
            codingMainPanel.Size = new Size(705, 494);
            codingMainPanel.TabIndex = 0;
            // 
            // codingScrollPanel
            // 
            codingScrollPanel.AutoScroll = true;
            codingScrollPanel.Dock = DockStyle.Fill;
            codingScrollPanel.Location = new Point(0, 40);
            codingScrollPanel.Name = "codingScrollPanel";
            codingScrollPanel.Padding = new Padding(10, 11, 10, 11);
            codingScrollPanel.Size = new Size(705, 454);
            codingScrollPanel.TabIndex = 0;
            // 
            // codingTopPanel
            // 
            codingTopPanel.Dock = DockStyle.Top;
            codingTopPanel.Location = new Point(0, 0);
            codingTopPanel.Name = "codingTopPanel";
            codingTopPanel.Size = new Size(705, 40);
            codingTopPanel.TabIndex = 1;
            //
            // vehicleInfoTab
            //
            vehicleInfoTab.Controls.Add(vehicleInfoControl);
            vehicleInfoTab.Location = new Point(4, 29);
            vehicleInfoTab.Name = "vehicleInfoTab";
            vehicleInfoTab.Size = new Size(705, 499);
            vehicleInfoTab.TabIndex = 2;
            vehicleInfoTab.Text = "Extended Vehicle Information";
            vehicleInfoTab.UseVisualStyleBackColor = true;
            //
            // vehicleInfoControl
            //
            vehicleInfoControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            vehicleInfoControl.Location = new Point(0, 0);
            vehicleInfoControl.Name = "vehicleInfoControl";
            vehicleInfoControl.Size = new Size(705, 499);
            vehicleInfoControl.TabIndex = 0;
            //
            // dtcTab
            //
            dtcTab.Controls.Add(dtcControl);
            dtcTab.Location = new Point(4, 29);
            dtcTab.Name = "dtcTab";
            dtcTab.Size = new Size(705, 499);
            dtcTab.TabIndex = 3;
            dtcTab.Text = "Diagnostic Trouble Codes";
            dtcTab.UseVisualStyleBackColor = true;
            //
            // obdResetTab
            //
            obdResetTab.Location = new Point(4, 29);
            obdResetTab.Name = "obdResetTab";
            obdResetTab.Size = new Size(705, 499);
            obdResetTab.TabIndex = 4;
            obdResetTab.Text = "Learned Data Reset";
            obdResetTab.UseVisualStyleBackColor = true;
            //
            // t6RmaTab
            //
            t6RmaTab.Location = new Point(4, 29);
            t6RmaTab.Name = "t6RmaTab";
            t6RmaTab.Size = new Size(705, 499);
            t6RmaTab.TabIndex = 5;
            t6RmaTab.Text = "T6 RMA Logging";
            t6RmaTab.UseVisualStyleBackColor = true;
            //
            // dtcControl
            //
            dtcControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            dtcControl.Location = new Point(0, 0);
            dtcControl.Name = "dtcControl";
            dtcControl.Size = new Size(705, 499);
            dtcControl.TabIndex = 0;
            //
            // LoggerWindow
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 600);
            Controls.Add(mainTabControl);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Margin = new Padding(3, 4, 3, 4);
            MinimumSize = new Size(729, 622);
            Name = "LoggerWindow";
            Text = "LotusECMLogger";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            mainTabControl.ResumeLayout(false);
            liveDataTab.ResumeLayout(false);
            loggerControlPanel.ResumeLayout(false);
            loggerControlPanel.PerformLayout();
            codingDataTab.ResumeLayout(false);
            codingMainPanel.ResumeLayout(false);
            dtcTab.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button startLogger_button;
        private Button stopLogger_button;
        private Label currentLogfileName;
        private Label obdConfigLabel;
        private ComboBox obdConfigComboBox;
        private ListView liveDataView;
        private ListView codingDataView;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem cliRunnerToolStripMenuItem;
        private ToolStripMenuItem aboutLotusECMLoggerToolStripMenuItem;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel refreshRateLabel;
        private TabControl mainTabControl;
        private TabPage liveDataTab;
        private TabPage codingDataTab;
        private Panel codingScrollPanel;
        private Panel loggerControlPanel;
        private Panel codingMainPanel;
        private Panel codingTopPanel;
        private TabPage vehicleInfoTab;
        private LotusECMLogger.Controls.VehicleInfoControl vehicleInfoControl;
        private TabPage dtcTab;
        private LotusECMLogger.Controls.DTCControl dtcControl;
        private TabPage obdResetTab;
        private TabPage t6RmaTab;
    }
}
