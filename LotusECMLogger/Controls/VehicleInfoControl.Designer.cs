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
            unlockIndicatorLabel = new Label();
            readDataButton = new Button();
            statusLabel = new Label();
            setVinButton = new Button();
            resetButton = new Button();
            vehicleInfoView = new ListView();
            topPanel.SuspendLayout();
            SuspendLayout();
            // 
            // topPanel
            // 
            topPanel.Controls.Add(unlockIndicatorLabel);
            topPanel.Controls.Add(readDataButton);
            topPanel.Controls.Add(statusLabel);
            topPanel.Controls.Add(setVinButton);
            topPanel.Controls.Add(resetButton);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Margin = new Padding(4, 5, 4, 5);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(881, 67);
            topPanel.TabIndex = 3;
            // 
            // unlockIndicatorLabel
            // 
            unlockIndicatorLabel.AutoSize = true;
            unlockIndicatorLabel.BackColor = Color.Gainsboro;
            unlockIndicatorLabel.BorderStyle = BorderStyle.FixedSingle;
            unlockIndicatorLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            unlockIndicatorLabel.ForeColor = Color.DimGray;
            unlockIndicatorLabel.Location = new Point(399, 18);
            unlockIndicatorLabel.Margin = new Padding(4, 0, 4, 0);
            unlockIndicatorLabel.Name = "unlockIndicatorLabel";
            unlockIndicatorLabel.Padding = new Padding(6, 4, 6, 4);
            unlockIndicatorLabel.Size = new Size(169, 35);
            unlockIndicatorLabel.TabIndex = 4;
            unlockIndicatorLabel.Text = "ECU: UNKNOWN";
            unlockIndicatorLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // readDataButton
            // 
            readDataButton.AutoSize = true;
            readDataButton.Location = new Point(6, 7);
            readDataButton.Margin = new Padding(4, 3, 4, 3);
            readDataButton.Name = "readDataButton";
            readDataButton.Size = new Size(168, 58);
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
            // setVinButton
            // 
            setVinButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            setVinButton.AutoSize = true;
            setVinButton.Location = new Point(575, 6);
            setVinButton.Margin = new Padding(4, 5, 4, 5);
            setVinButton.Name = "setVinButton";
            setVinButton.Size = new Size(99, 58);
            setVinButton.TabIndex = 3;
            setVinButton.Text = "Set VIN";
            setVinButton.UseVisualStyleBackColor = true;
            setVinButton.Click += setVinButton_Click;
            // 
            // resetButton
            // 
            resetButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            resetButton.AutoSize = true;
            resetButton.Location = new Point(684, 6);
            resetButton.Margin = new Padding(4, 5, 4, 5);
            resetButton.Name = "resetButton";
            resetButton.Size = new Size(189, 58);
            resetButton.TabIndex = 1;
            resetButton.Text = "Adaptations Reset";
            resetButton.UseVisualStyleBackColor = true;
            resetButton.Click += resetButton_Click;
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
        private Button resetButton;
        private Button setVinButton;
        private ListView vehicleInfoView;
        private Label statusLabel;
        private Label unlockIndicatorLabel;
    }
}
