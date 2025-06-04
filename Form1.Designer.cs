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
            if (disposing && (components != null))
            {
                components.Dispose();
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
            liveDataView = new DataGridView();
            menuStrip1 = new MenuStrip();
            helpToolStripMenuItem = new ToolStripMenuItem();
            aboutLotusECMLoggerToolStripMenuItem = new ToolStripMenuItem();
            ((ISupportInitialize)liveDataView).BeginInit();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // startLogger_button
            // 
            startLogger_button.Location = new Point(12, 37);
            startLogger_button.Name = "startLogger_button";
            startLogger_button.Size = new Size(75, 23);
            startLogger_button.TabIndex = 3;
            startLogger_button.Text = "Start";
            startLogger_button.UseVisualStyleBackColor = true;
            startLogger_button.Click += buttonTestRead_Click;
            // 
            // stopLogger_button
            // 
            stopLogger_button.Enabled = false;
            stopLogger_button.Location = new Point(93, 37);
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
            currentLogfileName.Location = new Point(223, 45);
            currentLogfileName.Name = "currentLogfileName";
            currentLogfileName.Size = new Size(67, 15);
            currentLogfileName.TabIndex = 5;
            currentLogfileName.Text = "No Log File";
            // 
            // liveDataView
            // 
            liveDataView.BackgroundColor = SystemColors.Control;
            liveDataView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            liveDataView.GridColor = SystemColors.Window;
            liveDataView.Location = new Point(12, 66);
            liveDataView.MultiSelect = false;
            liveDataView.Name = "liveDataView";
            liveDataView.RowHeadersWidth = 50;
            liveDataView.RowTemplate.ReadOnly = true;
            liveDataView.ShowEditingIcon = false;
            liveDataView.Size = new Size(600, 363);
            liveDataView.TabIndex = 6;
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { helpToolStripMenuItem });
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
            // LoggerWindow
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(624, 441);
            Controls.Add(liveDataView);
            Controls.Add(currentLogfileName);
            Controls.Add(stopLogger_button);
            Controls.Add(startLogger_button);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            MinimumSize = new Size(640, 480);
            Name = "LoggerWindow";
            Text = "LotusECMLogger";
            ((ISupportInitialize)liveDataView).EndInit();
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
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
        private DataGridView liveDataView;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem aboutLotusECMLoggerToolStripMenuItem;
    }
}
