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
            vehicleInfoTab = new TabPage();
            vehicleInfoView = new ListView();
            loadVehicleDataButton = new Button();
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            mainTabControl.SuspendLayout();
            liveDataTab.SuspendLayout();
            loggerControlPanel.SuspendLayout();
            codingDataTab.SuspendLayout();
            codingMainPanel.SuspendLayout();
            vehicleInfoTab.SuspendLayout();
            SuspendLayout();
            // 
            // startLogger_button
            // 
            startLogger_button.Location = new Point(12, 14);
            startLogger_button.Name = "startLogger_button";
            startLogger_button.Size = new Size(75, 23);
            startLogger_button.TabIndex = 3;
            startLogger_button.Text = "Start";
            startLogger_button.UseVisualStyleBackColor = true;
            startLogger_button.Click += startLogger_button_Click;
            // 
            // stopLogger_button
            // 
            stopLogger_button.Enabled = false;
            stopLogger_button.Location = new Point(93, 14);
            stopLogger_button.Name = "stopLogger_button";
            stopLogger_button.Size = new Size(75, 23);
            stopLogger_button.TabIndex = 4;
            stopLogger_button.Text = "Stop";
            stopLogger_button.UseVisualStyleBackColor = true;
            stopLogger_button.Click += stopLogger_button_Click;
            // 
            // currentLogfileName
            // 
            currentLogfileName.AutoSize = true;
            currentLogfileName.Location = new Point(223, 22);
            currentLogfileName.Name = "currentLogfileName";
            currentLogfileName.Size = new Size(67, 15);
            currentLogfileName.TabIndex = 5;
            currentLogfileName.Text = "No Log File";
            // 
            // liveDataView
            // 
            liveDataView.Dock = DockStyle.Fill;
            liveDataView.FullRowSelect = true;
            liveDataView.GridLines = true;
            liveDataView.Location = new Point(0, 45);
            liveDataView.MultiSelect = false;
            liveDataView.Name = "liveDataView";
            liveDataView.Size = new Size(616, 322);
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
            menuStrip1.Size = new Size(624, 24);
            menuStrip1.TabIndex = 7;
            menuStrip1.Text = "menuStrip1";
            // 
            // helpToolStripMenuItem
            // 
            helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { aboutLotusECMLoggerToolStripMenuItem });
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(44, 20);
            helpToolStripMenuItem.Text = "Help";
            // 
            // aboutLotusECMLoggerToolStripMenuItem
            // 
            aboutLotusECMLoggerToolStripMenuItem.Name = "aboutLotusECMLoggerToolStripMenuItem";
            aboutLotusECMLoggerToolStripMenuItem.Size = new Size(201, 22);
            aboutLotusECMLoggerToolStripMenuItem.Text = "About LotusECMLogger";
            aboutLotusECMLoggerToolStripMenuItem.Click += aboutLotusECMLoggerToolStripMenuItem_Click;
            // 
            // obdConfigToolStripMenuItem
            // 
            obdConfigToolStripMenuItem.Name = "obdConfigToolStripMenuItem";
            obdConfigToolStripMenuItem.Size = new Size(82, 20);
            obdConfigToolStripMenuItem.Text = "OBD Config";
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { refreshRateLabel });
            statusStrip1.Location = new Point(0, 419);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Padding = new Padding(1, 0, 12, 0);
            statusStrip1.Size = new Size(624, 22);
            statusStrip1.TabIndex = 8;
            statusStrip1.Text = "statusStrip1";
            // 
            // refreshRateLabel
            // 
            refreshRateLabel.Name = "refreshRateLabel";
            refreshRateLabel.Size = new Size(47, 17);
            refreshRateLabel.Text = "no data";
            // 
            // mainTabControl
            // 
            mainTabControl.Controls.Add(liveDataTab);
            mainTabControl.Controls.Add(codingDataTab);
            mainTabControl.Controls.Add(vehicleInfoTab);
            mainTabControl.Dock = DockStyle.Fill;
            mainTabControl.Location = new Point(0, 24);
            mainTabControl.Margin = new Padding(3, 2, 3, 2);
            mainTabControl.Name = "mainTabControl";
            mainTabControl.SelectedIndex = 0;
            mainTabControl.Size = new Size(624, 395);
            mainTabControl.TabIndex = 0;
            // 
            // liveDataTab
            // 
            liveDataTab.Controls.Add(liveDataView);
            liveDataTab.Controls.Add(loggerControlPanel);
            liveDataTab.Location = new Point(4, 24);
            liveDataTab.Margin = new Padding(3, 2, 3, 2);
            liveDataTab.Name = "liveDataTab";
            liveDataTab.Size = new Size(616, 367);
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
            loggerControlPanel.Margin = new Padding(3, 2, 3, 2);
            loggerControlPanel.Name = "loggerControlPanel";
            loggerControlPanel.Size = new Size(616, 45);
            loggerControlPanel.TabIndex = 7;
            // 
            // codingDataTab
            // 
            codingDataTab.Controls.Add(codingMainPanel);
            codingDataTab.Location = new Point(4, 24);
            codingDataTab.Margin = new Padding(3, 2, 3, 2);
            codingDataTab.Name = "codingDataTab";
            codingDataTab.Size = new Size(616, 371);
            codingDataTab.TabIndex = 1;
            codingDataTab.Text = "ECU Coding";
            // 
            // codingMainPanel
            // 
            codingMainPanel.Controls.Add(codingScrollPanel);
            codingMainPanel.Controls.Add(codingTopPanel);
            codingMainPanel.Dock = DockStyle.Fill;
            codingMainPanel.Location = new Point(0, 0);
            codingMainPanel.Margin = new Padding(3, 2, 3, 2);
            codingMainPanel.Name = "codingMainPanel";
            codingMainPanel.Size = new Size(616, 371);
            codingMainPanel.TabIndex = 0;
            // 
            // codingScrollPanel
            // 
            codingScrollPanel.AutoScroll = true;
            codingScrollPanel.Dock = DockStyle.Fill;
            codingScrollPanel.Location = new Point(0, 30);
            codingScrollPanel.Margin = new Padding(3, 2, 3, 2);
            codingScrollPanel.Name = "codingScrollPanel";
            codingScrollPanel.Padding = new Padding(9, 8, 9, 8);
            codingScrollPanel.Size = new Size(616, 341);
            codingScrollPanel.TabIndex = 0;
            // 
            // codingTopPanel
            // 
            codingTopPanel.Dock = DockStyle.Top;
            codingTopPanel.Location = new Point(0, 0);
            codingTopPanel.Margin = new Padding(3, 2, 3, 2);
            codingTopPanel.Name = "codingTopPanel";
            codingTopPanel.Size = new Size(616, 30);
            codingTopPanel.TabIndex = 1;
            // 
            // vehicleInfoTab
            // 
            vehicleInfoTab.Controls.Add(vehicleInfoView);
            vehicleInfoTab.Controls.Add(loadVehicleDataButton);
            vehicleInfoTab.Location = new Point(4, 24);
            vehicleInfoTab.Margin = new Padding(3, 2, 3, 2);
            vehicleInfoTab.Name = "vehicleInfoTab";
            vehicleInfoTab.Size = new Size(616, 367);
            vehicleInfoTab.TabIndex = 2;
            vehicleInfoTab.Text = "Extended Vehicle Information";
            vehicleInfoTab.UseVisualStyleBackColor = true;
            // 
            // vehicleInfoView
            // 
            vehicleInfoView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            vehicleInfoView.FullRowSelect = true;
            vehicleInfoView.GridLines = true;
            vehicleInfoView.Location = new Point(9, 38);
            vehicleInfoView.Margin = new Padding(3, 2, 3, 2);
            vehicleInfoView.MultiSelect = false;
            vehicleInfoView.Name = "vehicleInfoView";
            vehicleInfoView.Size = new Size(600, 327);
            vehicleInfoView.TabIndex = 1;
            vehicleInfoView.UseCompatibleStateImageBehavior = false;
            vehicleInfoView.View = View.Details;
            // 
            // loadVehicleDataButton
            // 
            loadVehicleDataButton.Location = new Point(9, 8);
            loadVehicleDataButton.Margin = new Padding(3, 2, 3, 2);
            loadVehicleDataButton.Name = "loadVehicleDataButton";
            loadVehicleDataButton.Size = new Size(105, 22);
            loadVehicleDataButton.TabIndex = 0;
            loadVehicleDataButton.Text = "Load Vehicle Data";
            loadVehicleDataButton.UseVisualStyleBackColor = true;
            loadVehicleDataButton.Click += LoadVehicleDataButton_Click;
            loadVehicleDataButton.Enabled = false;
            // 
            // LoggerWindow
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(624, 441);
            Controls.Add(mainTabControl);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            MinimumSize = new Size(640, 478);
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
        private Panel codingScrollPanel;
        private Panel loggerControlPanel;
        private Panel codingMainPanel;
        private Panel codingTopPanel;
        private TabPage vehicleInfoTab;
        private Button loadVehicleDataButton;
        private ListView vehicleInfoView;
    }
}
