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
            Panel topPanel = new Panel();
            startLogger_button = new Button();
            stopLogger_button = new Button();
            currentLogfileName = new Label();
            liveDataView = new ListView();
            menuStrip1 = new MenuStrip();
            helpToolStripMenuItem = new ToolStripMenuItem();
            aboutLotusECMLoggerToolStripMenuItem = new ToolStripMenuItem();
            obdConfigToolStripMenuItem = new ToolStripMenuItem();
            statusStrip1 = new StatusStrip();
            refreshRateLabel = new ToolStripStatusLabel();
            topPanel.SuspendLayout();
            // ListView doesn't need BeginInit
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // topPanel
            // 
            topPanel.Controls.Add(startLogger_button);
            topPanel.Controls.Add(stopLogger_button);
            topPanel.Controls.Add(currentLogfileName);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 30);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(713, 60);
            topPanel.TabIndex = 9;
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
            startLogger_button.Click += buttonTestRead_Click;
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
            liveDataView.Location = new Point(0, 90);
            liveDataView.Margin = new Padding(3, 4, 3, 4);
            liveDataView.MultiSelect = false;
            liveDataView.Name = "liveDataView";
            liveDataView.Size = new Size(713, 476);
            liveDataView.TabIndex = 6;
            liveDataView.UseCompatibleStateImageBehavior = false;
            liveDataView.View = View.Details;
            liveDataView.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(liveDataView, true, null); // Enable double buffering for smoother scrolling
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
            obdConfigToolStripMenuItem.Size = new Size(110, 24);
            obdConfigToolStripMenuItem.Text = "OBD Config";
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { refreshRateLabel });
            statusStrip1.Location = new Point(0, 566);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(713, 22);
            statusStrip1.TabIndex = 8;
            statusStrip1.Text = "statusStrip1";
            // 
            // refreshRateLabel
            // 
            refreshRateLabel.Name = "refreshRateLabel";
            refreshRateLabel.Size = new Size(58, 16);
            refreshRateLabel.Text = "no data";
            // 
            // LoggerWindow
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(713, 588);
            Controls.Add(liveDataView);
            Controls.Add(topPanel);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Margin = new Padding(3, 4, 3, 4);
            MinimumSize = new Size(729, 624);
            Name = "LoggerWindow";
            Text = "LotusECMLogger";
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            // ListView doesn't need EndInit
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label deviceLabel;
        private Label label1;
        private Button buttonRefreshDevice;
        private Button startLogger_button;
        private Button stopLogger_button;
        private Label currentLogfileName;
        private ListView liveDataView;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem aboutLotusECMLoggerToolStripMenuItem;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel refreshRateLabel;
        private ToolStripMenuItem obdConfigToolStripMenuItem;
    }
}
