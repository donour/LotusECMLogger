namespace LotusECMLogger.Controls
{
    partial class OBDLoggerControl
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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            loggerControlPanel = new Panel();
            obdConfigComboBox = new ComboBox();
            obdConfigLabel = new Label();
            startLoggerButton = new Button();
            stopLoggerButton = new Button();
            currentLogfileName = new Label();
            liveDataView = new ListView();
            loggerControlPanel.SuspendLayout();
            SuspendLayout();
            //
            // loggerControlPanel
            //
            loggerControlPanel.Controls.Add(obdConfigComboBox);
            loggerControlPanel.Controls.Add(obdConfigLabel);
            loggerControlPanel.Controls.Add(startLoggerButton);
            loggerControlPanel.Controls.Add(stopLoggerButton);
            loggerControlPanel.Controls.Add(currentLogfileName);
            loggerControlPanel.Dock = DockStyle.Top;
            loggerControlPanel.Location = new Point(0, 0);
            loggerControlPanel.Margin = new Padding(4, 4, 4, 4);
            loggerControlPanel.Name = "loggerControlPanel";
            loggerControlPanel.Size = new Size(992, 75);
            loggerControlPanel.TabIndex = 0;
            //
            // startLoggerButton
            //
            startLoggerButton.Location = new Point(18, 24);
            startLoggerButton.Margin = new Padding(4, 5, 4, 5);
            startLoggerButton.Name = "startLoggerButton";
            startLoggerButton.Size = new Size(108, 40);
            startLoggerButton.TabIndex = 0;
            startLoggerButton.Text = "Start";
            startLoggerButton.UseVisualStyleBackColor = true;
            startLoggerButton.Click += StartLoggerButton_Click;
            //
            // stopLoggerButton
            //
            stopLoggerButton.Enabled = false;
            stopLoggerButton.Location = new Point(132, 24);
            stopLoggerButton.Margin = new Padding(4, 5, 4, 5);
            stopLoggerButton.Name = "stopLoggerButton";
            stopLoggerButton.Size = new Size(108, 40);
            stopLoggerButton.TabIndex = 1;
            stopLoggerButton.Text = "Stop";
            stopLoggerButton.UseVisualStyleBackColor = true;
            stopLoggerButton.Click += StopLoggerButton_Click;
            //
            // currentLogfileName
            //
            currentLogfileName.AutoSize = true;
            currentLogfileName.Location = new Point(319, 36);
            currentLogfileName.Margin = new Padding(4, 0, 4, 0);
            currentLogfileName.Name = "currentLogfileName";
            currentLogfileName.Size = new Size(102, 25);
            currentLogfileName.TabIndex = 2;
            currentLogfileName.Text = "No Log File";
            //
            // obdConfigLabel
            //
            obdConfigLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            obdConfigLabel.AutoSize = true;
            obdConfigLabel.Location = new Point(673, 36);
            obdConfigLabel.Margin = new Padding(4, 0, 4, 0);
            obdConfigLabel.Name = "obdConfigLabel";
            obdConfigLabel.Size = new Size(69, 25);
            obdConfigLabel.TabIndex = 3;
            obdConfigLabel.Text = "Config:";
            //
            // obdConfigComboBox
            //
            obdConfigComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            obdConfigComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            obdConfigComboBox.FormattingEnabled = true;
            obdConfigComboBox.Location = new Point(749, 31);
            obdConfigComboBox.Margin = new Padding(4, 4, 4, 4);
            obdConfigComboBox.Name = "obdConfigComboBox";
            obdConfigComboBox.Size = new Size(224, 33);
            obdConfigComboBox.TabIndex = 4;
            obdConfigComboBox.SelectedIndexChanged += ObdConfigComboBox_SelectedIndexChanged;
            //
            // liveDataView
            //
            liveDataView.Dock = DockStyle.Fill;
            liveDataView.FullRowSelect = true;
            liveDataView.GridLines = true;
            liveDataView.Location = new Point(0, 75);
            liveDataView.Margin = new Padding(4, 5, 4, 5);
            liveDataView.MultiSelect = false;
            liveDataView.Name = "liveDataView";
            liveDataView.Size = new Size(992, 525);
            liveDataView.TabIndex = 1;
            liveDataView.UseCompatibleStateImageBehavior = false;
            liveDataView.View = View.Details;
            //
            // OBDLoggerControl
            //
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(liveDataView);
            Controls.Add(loggerControlPanel);
            Name = "OBDLoggerControl";
            Size = new Size(992, 600);
            loggerControlPanel.ResumeLayout(false);
            loggerControlPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Panel loggerControlPanel;
        private Button startLoggerButton;
        private Button stopLoggerButton;
        private Label currentLogfileName;
        private Label obdConfigLabel;
        private ComboBox obdConfigComboBox;
        private ListView liveDataView;
    }
}
