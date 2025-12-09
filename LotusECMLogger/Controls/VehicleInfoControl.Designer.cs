namespace LotusECMLogger.Controls
{
    partial class VehicleInfoControl
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
            readDataButton = new Button();
            statusLabel = new Label();
            vehicleInfoView = new ListView();
            topPanel.SuspendLayout();
            SuspendLayout();
            // 
            // topPanel
            // 
            topPanel.Controls.Add(readDataButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Margin = new Padding(4, 5, 4, 5);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(881, 67);
            topPanel.TabIndex = 3;
            // 
            // readDataButton
            // 
            readDataButton.Location = new Point(13, 13);
            readDataButton.Margin = new Padding(4, 3, 4, 3);
            readDataButton.Name = "readDataButton";
            readDataButton.Size = new Size(150, 32);
            readDataButton.TabIndex = 0;
            readDataButton.Text = "Load Vehicle Data";
            readDataButton.UseVisualStyleBackColor = true;
            readDataButton.Click += readDataButton_Click;
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(174, 22);
            statusLabel.Margin = new Padding(4, 0, 4, 0);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 25);
            statusLabel.TabIndex = 2;
            // 
            // vehicleInfoView
            // 
            vehicleInfoView.Dock = DockStyle.Fill;
            vehicleInfoView.FullRowSelect = true;
            vehicleInfoView.GridLines = true;
            vehicleInfoView.Location = new Point(0, 67);
            vehicleInfoView.Margin = new Padding(4, 3, 4, 3);
            vehicleInfoView.MultiSelect = false;
            vehicleInfoView.Name = "vehicleInfoView";
            vehicleInfoView.Size = new Size(881, 556);
            vehicleInfoView.TabIndex = 1;
            vehicleInfoView.UseCompatibleStateImageBehavior = false;
            vehicleInfoView.View = View.Details;
            // 
            // VehicleInfoControl
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(vehicleInfoView);
            Controls.Add(topPanel);
            Margin = new Padding(4, 3, 4, 3);
            Name = "VehicleInfoControl";
            Size = new Size(881, 623);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Button readDataButton;
        private ListView vehicleInfoView;
        private Label statusLabel;
    }
}
