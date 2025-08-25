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
            liveDataView = new ListView();
            codingDataView = new ListView();
            menuStrip1 = new MenuStrip();
            helpToolStripMenuItem = new ToolStripMenuItem();
            aboutLotusECMLoggerToolStripMenuItem = new ToolStripMenuItem();
            obdConfigToolStripMenuItem = new ToolStripMenuItem();
            statusStrip1 = new StatusStrip();
            refreshRateLabel = new ToolStripStatusLabel();
            mainTabControl = new TabControl();
            liveDataTab = new TabPage();
            loggerControlPanel = new Panel();
            codingDataTab = new TabPage();
            codingMainPanel = new Panel();
            codingScrollPanel = new Panel();
            codingTopPanel = new Panel();
            readCodesButton = new Button();
            writeCodesButton = new Button();
            saveCodingButton = new Button();
            resetCodingButton = new Button();
            vehicleInfoTab = new TabPage();
            loadVehicleDataButton = new Button();
            vehicleInfoView = new ListView();
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            mainTabControl.SuspendLayout();
            liveDataTab.SuspendLayout();
            loggerControlPanel.SuspendLayout();
            codingDataTab.SuspendLayout();
            codingMainPanel.SuspendLayout();
            codingTopPanel.SuspendLayout();
            vehicleInfoTab.SuspendLayout();
            SuspendLayout();
            // 
            // startLogger_button
            // 
            startLogger_button.Location = new Point(14, 19);
            startLogger_button.Margin = new Padding(3, 4, 3, 4);
            startLogger_button.Name = "startLogger_button";
            startLogger_button.Size = new Size(86, 31);
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
            stopLogger_button.Size = new Size(86, 31);
            stopLogger_button.TabIndex = 4;
            stopLogger_button.Text = "Stop";
            stopLogger_button.UseVisualStyleBackColor = true;
            stopLogger_button.Click += stopLogger_button_Click;
            // 
            // currentLogfileName
            // 
            currentLogfileName.AutoSize = true;
            currentLogfileName.Location = new Point(255, 30);
            currentLogfileName.Name = "currentLogfileName";
            currentLogfileName.Size = new Size(85, 20);
            currentLogfileName.TabIndex = 5;
            currentLogfileName.Text = "No Log File";
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
            menuStrip1.Items.AddRange(new ToolStripItem[] { helpToolStripMenuItem, obdConfigToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Padding = new Padding(7, 3, 0, 3);
            menuStrip1.Size = new Size(713, 30);
            menuStrip1.TabIndex = 7;
            menuStrip1.Text = "menuStrip1";
            // 
            // helpToolStripMenuItem
            // 
            helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { aboutLotusECMLoggerToolStripMenuItem });
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(55, 24);
            helpToolStripMenuItem.Text = "Help";
            // 
            // aboutLotusECMLoggerToolStripMenuItem
            // 
            aboutLotusECMLoggerToolStripMenuItem.Name = "aboutLotusECMLoggerToolStripMenuItem";
            aboutLotusECMLoggerToolStripMenuItem.Size = new Size(249, 26);
            aboutLotusECMLoggerToolStripMenuItem.Text = "About LotusECMLogger";
            aboutLotusECMLoggerToolStripMenuItem.Click += aboutLotusECMLoggerToolStripMenuItem_Click;
            // 
            // obdConfigToolStripMenuItem
            // 
            obdConfigToolStripMenuItem.Name = "obdConfigToolStripMenuItem";
            obdConfigToolStripMenuItem.Size = new Size(102, 24);
            obdConfigToolStripMenuItem.Text = "OBD Config";
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
            mainTabControl.Controls.Add(liveDataTab);
            mainTabControl.Controls.Add(codingDataTab);
            mainTabControl.Controls.Add(vehicleInfoTab);
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
            codingDataTab.Size = new Size(705, 499);
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
            codingMainPanel.Size = new Size(705, 499);
            codingMainPanel.TabIndex = 0;
            // 
            // codingScrollPanel
            // 
            codingScrollPanel.AutoScroll = true;
            codingScrollPanel.Dock = DockStyle.Fill;
            codingScrollPanel.Location = new Point(0, 40);
            codingScrollPanel.Name = "codingScrollPanel";
            codingScrollPanel.Padding = new Padding(10);
            codingScrollPanel.Size = new Size(705, 459);
            codingScrollPanel.TabIndex = 0;
            // 
            // codingTopPanel
            // 
            codingTopPanel.Controls.Add(readCodesButton);
            codingTopPanel.Controls.Add(writeCodesButton);
            codingTopPanel.Controls.Add(saveCodingButton);
            codingTopPanel.Controls.Add(resetCodingButton);
            codingTopPanel.Dock = DockStyle.Top;
            codingTopPanel.Location = new Point(0, 0);
            codingTopPanel.Name = "codingTopPanel";
            codingTopPanel.Size = new Size(705, 40);
            codingTopPanel.TabIndex = 1;
            // 
            // readCodesButton
            // 
            readCodesButton.Location = new Point(10, 5);
            readCodesButton.Name = "readCodesButton";
            readCodesButton.Size = new Size(100, 30);
            readCodesButton.TabIndex = 0;
            readCodesButton.Text = "Read Codes";
            readCodesButton.Enabled = true; // Enabled initially since logging is not active
            readCodesButton.Click += ReadCodesButton_Click;
            // 
            // writeCodesButton
            // 
            writeCodesButton.Enabled = false;
            writeCodesButton.Location = new Point(120, 5);
            writeCodesButton.Name = "writeCodesButton";
            writeCodesButton.Size = new Size(100, 30);
            writeCodesButton.TabIndex = 1;
            writeCodesButton.Text = "Write Codes";
            writeCodesButton.Click += WriteCodesButton_Click;
            // 
            // saveCodingButton
            // 
            saveCodingButton.Enabled = false;
            saveCodingButton.Location = new Point(230, 5);
            saveCodingButton.Name = "saveCodingButton";
            saveCodingButton.Size = new Size(110, 30);
            saveCodingButton.TabIndex = 2;
            saveCodingButton.Text = "Save Changes";
            saveCodingButton.Click += SaveCodingButton_Click;
            // 
            // resetCodingButton
            // 
            resetCodingButton.Enabled = false;
            resetCodingButton.Location = new Point(350, 5);
            resetCodingButton.Name = "resetCodingButton";
            resetCodingButton.Size = new Size(80, 30);
            resetCodingButton.TabIndex = 3;
            resetCodingButton.Text = "Reset";
            resetCodingButton.Click += ResetCodingButton_Click;
            // 
            // vehicleInfoTab
            // 
            vehicleInfoTab.Controls.Add(vehicleInfoView);
            vehicleInfoTab.Controls.Add(loadVehicleDataButton);
            vehicleInfoTab.Location = new Point(4, 29);
            vehicleInfoTab.Name = "vehicleInfoTab";
            vehicleInfoTab.Size = new Size(705, 499);
            vehicleInfoTab.TabIndex = 2;
            vehicleInfoTab.Text = "Extended Vehicle Information";
            vehicleInfoTab.UseVisualStyleBackColor = true;
            // 
            // loadVehicleDataButton
            // 
            loadVehicleDataButton.Enabled = true; // Enabled initially since logging is not active
            loadVehicleDataButton.Location = new Point(10, 10);
            loadVehicleDataButton.Name = "loadVehicleDataButton";
            loadVehicleDataButton.Size = new Size(120, 30);
            loadVehicleDataButton.TabIndex = 0;
            loadVehicleDataButton.Text = "Load Vehicle Data";
            loadVehicleDataButton.UseVisualStyleBackColor = true;
            loadVehicleDataButton.Click += LoadVehicleDataButton_Click;
            // 
            // vehicleInfoView
            // 
            vehicleInfoView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            vehicleInfoView.FullRowSelect = true;
            vehicleInfoView.GridLines = true;
            vehicleInfoView.Location = new Point(10, 50);
            vehicleInfoView.MultiSelect = false;
            vehicleInfoView.Name = "vehicleInfoView";
            vehicleInfoView.Size = new Size(685, 440);
            vehicleInfoView.TabIndex = 1;
            vehicleInfoView.UseCompatibleStateImageBehavior = false;
            vehicleInfoView.View = View.Details;
            // 
            // LoggerWindow
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(713, 588);
            Controls.Add(mainTabControl);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Margin = new Padding(3, 4, 3, 4);
            MinimumSize = new Size(729, 624);
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
            codingTopPanel.ResumeLayout(false);
            vehicleInfoTab.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button startLogger_button;
        private Button stopLogger_button;
        private Label currentLogfileName;
        private ListView liveDataView;
        private ListView codingDataView;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem aboutLotusECMLoggerToolStripMenuItem;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel refreshRateLabel;
        private ToolStripMenuItem obdConfigToolStripMenuItem;
        private TabControl mainTabControl;
        private TabPage liveDataTab;
        private TabPage codingDataTab;
        private Button readCodesButton;
        private Button writeCodesButton;
        private Button saveCodingButton;
        private Button resetCodingButton;
        private Panel codingScrollPanel;
        private Panel loggerControlPanel;
        private Panel codingMainPanel;
        private Panel codingTopPanel;
        private TabPage vehicleInfoTab;
        private Button loadVehicleDataButton;
        private ListView vehicleInfoView;
    }
}
