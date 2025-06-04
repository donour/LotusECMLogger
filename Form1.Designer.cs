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
            deviceLabel = new Label();
            label1 = new Label();
            buttonRefreshDevice = new Button();
            startLogger_button = new Button();
            stopLogger_button = new Button();
            currentLogfileName = new Label();
            SuspendLayout();
            // 
            // deviceLabel
            // 
            deviceLabel.AutoSize = true;
            deviceLabel.DataBindings.Add(new Binding("Text", this, "J2534DeviceName", true));
            deviceLabel.Location = new Point(102, 426);
            deviceLabel.Name = "deviceLabel";
            deviceLabel.Size = new Size(61, 15);
            deviceLabel.TabIndex = 0;
            deviceLabel.Text = "No Device";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label1.Location = new Point(12, 426);
            label1.Name = "label1";
            label1.Size = new Size(85, 15);
            label1.TabIndex = 1;
            label1.Text = "J2534 Device:";
            // 
            // buttonRefreshDevice
            // 
            buttonRefreshDevice.Enabled = false;
            buttonRefreshDevice.Location = new Point(12, 400);
            buttonRefreshDevice.Name = "buttonRefreshDevice";
            buttonRefreshDevice.Size = new Size(75, 23);
            buttonRefreshDevice.TabIndex = 2;
            buttonRefreshDevice.Text = "Refresh Device";
            buttonRefreshDevice.UseVisualStyleBackColor = true;
            buttonRefreshDevice.Click += buttonRefreshDevice_Click;
            // 
            // startLogger_button
            // 
            startLogger_button.Location = new Point(12, 12);
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
            stopLogger_button.Location = new Point(93, 12);
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
            currentLogfileName.Location = new Point(12, 47);
            currentLogfileName.Name = "currentLogfileName";
            currentLogfileName.Size = new Size(67, 15);
            currentLogfileName.TabIndex = 5;
            currentLogfileName.Text = "No Log File";
            // 
            // LoggerWindow
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(currentLogfileName);
            Controls.Add(stopLogger_button);
            Controls.Add(startLogger_button);
            Controls.Add(buttonRefreshDevice);
            Controls.Add(label1);
            Controls.Add(deviceLabel);
            Name = "LoggerWindow";
            Text = "Form1";
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
    }
}
