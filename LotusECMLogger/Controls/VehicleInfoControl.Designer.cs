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
            resetButton = new Button();
            vehicleInfoView = new ListView();
            topPanel.SuspendLayout();
            SuspendLayout();
            // 
            // topPanel
            // 
            topPanel.Controls.Add(readDataButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Controls.Add(resetButton);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(617, 40);
            topPanel.TabIndex = 3;
            // 
            // readDataButton
            // 
            readDataButton.AutoSize = true;
            readDataButton.Location = new Point(9, 8);
            readDataButton.Margin = new Padding(3, 2, 3, 2);
            readDataButton.Name = "readDataButton";
            readDataButton.Size = new Size(110, 32);
            readDataButton.TabIndex = 0;
            readDataButton.Text = "Load Vehicle Data";
            readDataButton.UseVisualStyleBackColor = true;
            readDataButton.Click += readDataButton_Click;
            //
            // resetButton
            //
            resetButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            resetButton.AutoSize = true;
            resetButton.Location = new Point(476, 4);
            resetButton.Name = "resetButton";
            resetButton.Size = new Size(132, 32);
            resetButton.TabIndex = 1;
            resetButton.Text = "Perform Reset";
            resetButton.UseVisualStyleBackColor = true;
            resetButton.Click += resetButton_Click;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(122, 13);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 2;
            // 
            // vehicleInfoView
            // 
            vehicleInfoView.Dock = DockStyle.Fill;
            vehicleInfoView.FullRowSelect = true;
            vehicleInfoView.GridLines = true;
            vehicleInfoView.Location = new Point(0, 40);
            vehicleInfoView.Margin = new Padding(3, 2, 3, 2);
            vehicleInfoView.MultiSelect = false;
            vehicleInfoView.Name = "vehicleInfoView";
            vehicleInfoView.Size = new Size(617, 334);
            vehicleInfoView.TabIndex = 1;
            vehicleInfoView.UseCompatibleStateImageBehavior = false;
            vehicleInfoView.View = View.Details;
            // 
            // VehicleInfoControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(vehicleInfoView);
            Controls.Add(topPanel);
            Margin = new Padding(3, 2, 3, 2);
            Name = "VehicleInfoControl";
            Size = new Size(617, 374);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Button readDataButton;
        private Button resetButton;
        private ListView vehicleInfoView;
        private Label statusLabel;
    }
}
