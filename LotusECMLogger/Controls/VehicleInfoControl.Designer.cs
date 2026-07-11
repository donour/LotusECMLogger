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
            if (disposing)
            {
                components?.Dispose();
                _highSpeedLogService?.Dispose();
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
            dynoModeButton = new Button();
            setVinButton = new Button();
            resetButton = new Button();
            bottomPanel = new Panel();
            unlockIndicatorLabel = new Label();
            highSpeedIndicatorLabel = new Label();
            vehicleInfoView = new ListView();
            topPanel.SuspendLayout();
            bottomPanel.SuspendLayout();
            SuspendLayout();
            //
            // topPanel
            //
            topPanel.Controls.Add(readDataButton);
            topPanel.Controls.Add(dynoModeButton);
            topPanel.Controls.Add(setVinButton);
            topPanel.Controls.Add(resetButton);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Margin = new Padding(4, 5, 4, 5);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(881, 72);
            topPanel.TabIndex = 3;
            //
            // bottomPanel
            //
            bottomPanel.Controls.Add(unlockIndicatorLabel);
            bottomPanel.Controls.Add(highSpeedIndicatorLabel);
            bottomPanel.Dock = DockStyle.Bottom;
            bottomPanel.Margin = new Padding(4, 5, 4, 5);
            bottomPanel.Name = "bottomPanel";
            bottomPanel.Size = new Size(881, 41);
            bottomPanel.TabIndex = 4;
            //
            // unlockIndicatorLabel
            //
            unlockIndicatorLabel.AutoSize = true;
            unlockIndicatorLabel.BackColor = Color.Gainsboro;
            unlockIndicatorLabel.BorderStyle = BorderStyle.FixedSingle;
            unlockIndicatorLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            unlockIndicatorLabel.ForeColor = Color.DimGray;
            unlockIndicatorLabel.Location = new Point(6, 6);
            unlockIndicatorLabel.Margin = new Padding(4, 0, 4, 0);
            unlockIndicatorLabel.Name = "unlockIndicatorLabel";
            unlockIndicatorLabel.Padding = new Padding(6, 3, 6, 3);
            unlockIndicatorLabel.Size = new Size(169, 29);
            unlockIndicatorLabel.TabIndex = 4;
            unlockIndicatorLabel.Text = "ECU: UNKNOWN";
            unlockIndicatorLabel.TextAlign = ContentAlignment.MiddleCenter;
            //
            // highSpeedIndicatorLabel
            //
            highSpeedIndicatorLabel.AutoSize = true;
            highSpeedIndicatorLabel.BackColor = Color.Gainsboro;
            highSpeedIndicatorLabel.BorderStyle = BorderStyle.FixedSingle;
            highSpeedIndicatorLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            highSpeedIndicatorLabel.ForeColor = Color.DimGray;
            highSpeedIndicatorLabel.Location = new Point(240, 6);
            highSpeedIndicatorLabel.Margin = new Padding(4, 0, 4, 0);
            highSpeedIndicatorLabel.Name = "highSpeedIndicatorLabel";
            highSpeedIndicatorLabel.Padding = new Padding(6, 3, 6, 3);
            highSpeedIndicatorLabel.Size = new Size(169, 29);
            highSpeedIndicatorLabel.TabIndex = 5;
            highSpeedIndicatorLabel.Text = "HS LOGGER: UNKNOWN";
            highSpeedIndicatorLabel.TextAlign = ContentAlignment.MiddleCenter;
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
            // dynoModeButton
            //
            dynoModeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            dynoModeButton.AutoSize = true;
            dynoModeButton.Location = new Point(432, 6);
            dynoModeButton.Margin = new Padding(4, 5, 4, 5);
            dynoModeButton.Name = "dynoModeButton";
            dynoModeButton.Size = new Size(135, 58);
            dynoModeButton.TabIndex = 4;
            dynoModeButton.Text = "Dyno Mode";
            dynoModeButton.UseVisualStyleBackColor = true;
            dynoModeButton.Click += dynoModeButton_Click;
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
            Controls.Add(bottomPanel);
            Margin = new Padding(4, 3, 4, 3);
            Name = "VehicleInfoControl";
            Size = new Size(881, 623);
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            bottomPanel.ResumeLayout(false);
            bottomPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel topPanel;
        private Panel bottomPanel;
        private Button readDataButton;
        private Button dynoModeButton;
        private Button resetButton;
        private Button setVinButton;
        private ListView vehicleInfoView;
        private Label unlockIndicatorLabel;
        private Label highSpeedIndicatorLabel;
    }
}
