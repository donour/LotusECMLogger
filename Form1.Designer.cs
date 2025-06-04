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
            buttonTestRead = new Button();
            SuspendLayout();
            // 
            // deviceLabel
            // 
            deviceLabel.AutoSize = true;
            deviceLabel.DataBindings.Add(new Binding("Text", this, "J2534DeviceName", true));
            deviceLabel.Location = new Point(116, 568);
            deviceLabel.Name = "deviceLabel";
            deviceLabel.Size = new Size(78, 20);
            deviceLabel.TabIndex = 0;
            deviceLabel.Text = "No Device";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label1.Location = new Point(14, 568);
            label1.Name = "label1";
            label1.Size = new Size(106, 20);
            label1.TabIndex = 1;
            label1.Text = "J2534 Device:";
            // 
            // buttonRefreshDevice
            // 
            buttonRefreshDevice.Location = new Point(335, 557);
            buttonRefreshDevice.Margin = new Padding(3, 4, 3, 4);
            buttonRefreshDevice.Name = "buttonRefreshDevice";
            buttonRefreshDevice.Size = new Size(86, 31);
            buttonRefreshDevice.TabIndex = 2;
            buttonRefreshDevice.Text = "Refresh Device";
            buttonRefreshDevice.UseVisualStyleBackColor = true;
            buttonRefreshDevice.Click += buttonRefreshDevice_Click;
            // 
            // buttonTestRead
            // 
            buttonTestRead.Location = new Point(14, 16);
            buttonTestRead.Margin = new Padding(3, 4, 3, 4);
            buttonTestRead.Name = "buttonTestRead";
            buttonTestRead.Size = new Size(86, 31);
            buttonTestRead.TabIndex = 3;
            buttonTestRead.Text = "Connect";
            buttonTestRead.UseVisualStyleBackColor = true;
            buttonTestRead.Click += buttonTestRead_Click;
            // 
            // LoggerWindow
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(914, 600);
            Controls.Add(buttonTestRead);
            Controls.Add(buttonRefreshDevice);
            Controls.Add(label1);
            Controls.Add(deviceLabel);
            Margin = new Padding(3, 4, 3, 4);
            Name = "LoggerWindow";
            Text = "Form1";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label deviceLabel;
        private Label label1;
        private Button buttonRefreshDevice;
        private Button buttonTestRead;
    }
}
