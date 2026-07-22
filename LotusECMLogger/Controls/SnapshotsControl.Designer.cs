namespace LotusECMLogger.Controls
{
    partial class SnapshotsControl
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
            if (disposing && components != null)
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
            ecuVariantGroupBox = new GroupBox();
            ecuVariantPanel = new FlowLayoutPanel();
            ecuVariantLabel = new Label();
            ecuVariantComboBox = new ComboBox();
            learnedDataGroupBox = new GroupBox();
            learnedDataLayout = new TableLayoutPanel();
            learnedDataInfoLabel = new Label();
            learnedDataButtonPanel = new FlowLayoutPanel();
            downloadLearnedDataButton = new Button();
            learnedDataProgressBar = new ProgressBar();
            learnedDataStatusValueLabel = new Label();
            calibrationGroupBox = new GroupBox();
            calibrationLayout = new TableLayoutPanel();
            calibrationInfoLabel = new Label();
            calibrationButtonPanel = new FlowLayoutPanel();
            downloadCalibrationButton = new Button();
            calibrationProgressBar = new ProgressBar();
            calibrationStatusValueLabel = new Label();
            programGroupBox = new GroupBox();
            programLayout = new TableLayoutPanel();
            programInfoLabel = new Label();
            programButtonPanel = new FlowLayoutPanel();
            downloadProgramButton = new Button();
            programProgressBar = new ProgressBar();
            programStatusValueLabel = new Label();
            ecuVariantGroupBox.SuspendLayout();
            ecuVariantPanel.SuspendLayout();
            learnedDataGroupBox.SuspendLayout();
            learnedDataLayout.SuspendLayout();
            learnedDataButtonPanel.SuspendLayout();
            calibrationGroupBox.SuspendLayout();
            calibrationLayout.SuspendLayout();
            calibrationButtonPanel.SuspendLayout();
            programGroupBox.SuspendLayout();
            programLayout.SuspendLayout();
            programButtonPanel.SuspendLayout();
            SuspendLayout();
            //
            // learnedDataGroupBox
            //
            learnedDataGroupBox.Controls.Add(learnedDataLayout);
            learnedDataGroupBox.Dock = DockStyle.Top;
            learnedDataGroupBox.Location = new Point(14, 300);
            learnedDataGroupBox.Margin = new Padding(4, 5, 4, 5);
            learnedDataGroupBox.Name = "learnedDataGroupBox";
            learnedDataGroupBox.Padding = new Padding(14, 17, 14, 17);
            learnedDataGroupBox.Size = new Size(1107, 150);
            learnedDataGroupBox.TabIndex = 0;
            learnedDataGroupBox.TabStop = false;
            learnedDataGroupBox.Text = "Learned Data (Flash)";
            //
            // learnedDataLayout
            //
            learnedDataLayout.ColumnCount = 1;
            learnedDataLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            learnedDataLayout.Controls.Add(learnedDataInfoLabel, 0, 0);
            learnedDataLayout.Controls.Add(learnedDataButtonPanel, 0, 1);
            learnedDataLayout.Dock = DockStyle.Fill;
            learnedDataLayout.Location = new Point(14, 41);
            learnedDataLayout.Margin = new Padding(4, 5, 4, 5);
            learnedDataLayout.Name = "learnedDataLayout";
            learnedDataLayout.RowCount = 2;
            learnedDataLayout.RowStyles.Add(new RowStyle());
            learnedDataLayout.RowStyles.Add(new RowStyle());
            learnedDataLayout.Size = new Size(1079, 75);
            learnedDataLayout.TabIndex = 0;
            //
            // learnedDataInfoLabel
            //
            learnedDataInfoLabel.AutoSize = true;
            learnedDataInfoLabel.Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);
            learnedDataInfoLabel.ForeColor = Color.Gray;
            learnedDataInfoLabel.Location = new Point(4, 0);
            learnedDataInfoLabel.Margin = new Padding(4, 0, 4, 5);
            learnedDataInfoLabel.Name = "learnedDataInfoLabel";
            learnedDataInfoLabel.Size = new Size(600, 20);
            learnedDataInfoLabel.TabIndex = 0;
            learnedDataInfoLabel.Text = "Downloads the ECU's persisted adaptive fuel/idle/knock trims (flash) to a .bin file. Requires an unlocked ECU.";
            //
            // learnedDataButtonPanel
            //
            learnedDataButtonPanel.AutoSize = true;
            learnedDataButtonPanel.Controls.Add(downloadLearnedDataButton);
            learnedDataButtonPanel.Controls.Add(learnedDataProgressBar);
            learnedDataButtonPanel.Controls.Add(learnedDataStatusValueLabel);
            learnedDataButtonPanel.Dock = DockStyle.Fill;
            learnedDataButtonPanel.Location = new Point(4, 33);
            learnedDataButtonPanel.Margin = new Padding(4, 8, 4, 0);
            learnedDataButtonPanel.Name = "learnedDataButtonPanel";
            learnedDataButtonPanel.Size = new Size(1071, 42);
            learnedDataButtonPanel.TabIndex = 1;
            learnedDataButtonPanel.WrapContents = false;
            //
            // downloadLearnedDataButton
            //
            downloadLearnedDataButton.AutoSize = true;
            downloadLearnedDataButton.Location = new Point(4, 3);
            downloadLearnedDataButton.Margin = new Padding(4, 3, 4, 3);
            downloadLearnedDataButton.Name = "downloadLearnedDataButton";
            downloadLearnedDataButton.Size = new Size(220, 35);
            downloadLearnedDataButton.TabIndex = 0;
            downloadLearnedDataButton.Text = "Download Learned Data...";
            downloadLearnedDataButton.UseVisualStyleBackColor = true;
            downloadLearnedDataButton.Click += DownloadLearnedDataButton_Click;
            //
            // learnedDataProgressBar
            //
            learnedDataProgressBar.Location = new Point(232, 3);
            learnedDataProgressBar.Margin = new Padding(8, 3, 8, 3);
            learnedDataProgressBar.Name = "learnedDataProgressBar";
            learnedDataProgressBar.Size = new Size(300, 29);
            learnedDataProgressBar.TabIndex = 1;
            //
            // learnedDataStatusValueLabel
            //
            learnedDataStatusValueLabel.Anchor = AnchorStyles.Left;
            learnedDataStatusValueLabel.AutoSize = true;
            learnedDataStatusValueLabel.Location = new Point(548, 8);
            learnedDataStatusValueLabel.Margin = new Padding(8, 5, 4, 0);
            learnedDataStatusValueLabel.Name = "learnedDataStatusValueLabel";
            learnedDataStatusValueLabel.Size = new Size(41, 25);
            learnedDataStatusValueLabel.TabIndex = 2;
            learnedDataStatusValueLabel.Text = "Idle";
            //
            // calibrationGroupBox
            //
            calibrationGroupBox.Controls.Add(calibrationLayout);
            calibrationGroupBox.Dock = DockStyle.Top;
            calibrationGroupBox.Location = new Point(14, 150);
            calibrationGroupBox.Margin = new Padding(4, 5, 4, 5);
            calibrationGroupBox.Name = "calibrationGroupBox";
            calibrationGroupBox.Padding = new Padding(14, 17, 14, 17);
            calibrationGroupBox.Size = new Size(1107, 150);
            calibrationGroupBox.TabIndex = 1;
            calibrationGroupBox.TabStop = false;
            calibrationGroupBox.Text = "Calibration (Flash)";
            //
            // calibrationLayout
            //
            calibrationLayout.ColumnCount = 1;
            calibrationLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            calibrationLayout.Controls.Add(calibrationInfoLabel, 0, 0);
            calibrationLayout.Controls.Add(calibrationButtonPanel, 0, 1);
            calibrationLayout.Dock = DockStyle.Fill;
            calibrationLayout.Location = new Point(14, 41);
            calibrationLayout.Margin = new Padding(4, 5, 4, 5);
            calibrationLayout.Name = "calibrationLayout";
            calibrationLayout.RowCount = 2;
            calibrationLayout.RowStyles.Add(new RowStyle());
            calibrationLayout.RowStyles.Add(new RowStyle());
            calibrationLayout.Size = new Size(1079, 75);
            calibrationLayout.TabIndex = 0;
            //
            // calibrationInfoLabel
            //
            calibrationInfoLabel.AutoSize = true;
            calibrationInfoLabel.Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);
            calibrationInfoLabel.ForeColor = Color.Gray;
            calibrationInfoLabel.Location = new Point(4, 0);
            calibrationInfoLabel.Margin = new Padding(4, 0, 4, 5);
            calibrationInfoLabel.Name = "calibrationInfoLabel";
            calibrationInfoLabel.Size = new Size(600, 20);
            calibrationInfoLabel.TabIndex = 0;
            calibrationInfoLabel.Text = "Downloads the ECU's active calibration (fuel/ignition maps, limiters, etc., flash) to a .bin file. Requires an unlocked ECU.";
            //
            // calibrationButtonPanel
            //
            calibrationButtonPanel.AutoSize = true;
            calibrationButtonPanel.Controls.Add(downloadCalibrationButton);
            calibrationButtonPanel.Controls.Add(calibrationProgressBar);
            calibrationButtonPanel.Controls.Add(calibrationStatusValueLabel);
            calibrationButtonPanel.Dock = DockStyle.Fill;
            calibrationButtonPanel.Location = new Point(4, 33);
            calibrationButtonPanel.Margin = new Padding(4, 8, 4, 0);
            calibrationButtonPanel.Name = "calibrationButtonPanel";
            calibrationButtonPanel.Size = new Size(1071, 42);
            calibrationButtonPanel.TabIndex = 1;
            calibrationButtonPanel.WrapContents = false;
            //
            // downloadCalibrationButton
            //
            downloadCalibrationButton.AutoSize = true;
            downloadCalibrationButton.Location = new Point(4, 3);
            downloadCalibrationButton.Margin = new Padding(4, 3, 4, 3);
            downloadCalibrationButton.Name = "downloadCalibrationButton";
            downloadCalibrationButton.Size = new Size(220, 35);
            downloadCalibrationButton.TabIndex = 0;
            downloadCalibrationButton.Text = "Download Calibration...";
            downloadCalibrationButton.UseVisualStyleBackColor = true;
            downloadCalibrationButton.Click += DownloadCalibrationButton_Click;
            //
            // calibrationProgressBar
            //
            calibrationProgressBar.Location = new Point(232, 3);
            calibrationProgressBar.Margin = new Padding(8, 3, 8, 3);
            calibrationProgressBar.Name = "calibrationProgressBar";
            calibrationProgressBar.Size = new Size(300, 29);
            calibrationProgressBar.TabIndex = 1;
            //
            // calibrationStatusValueLabel
            //
            calibrationStatusValueLabel.Anchor = AnchorStyles.Left;
            calibrationStatusValueLabel.AutoSize = true;
            calibrationStatusValueLabel.Location = new Point(548, 8);
            calibrationStatusValueLabel.Margin = new Padding(8, 5, 4, 0);
            calibrationStatusValueLabel.Name = "calibrationStatusValueLabel";
            calibrationStatusValueLabel.Size = new Size(41, 25);
            calibrationStatusValueLabel.TabIndex = 2;
            calibrationStatusValueLabel.Text = "Idle";
            //
            // programGroupBox
            //
            programGroupBox.Controls.Add(programLayout);
            programGroupBox.Dock = DockStyle.Top;
            programGroupBox.Location = new Point(14, 17);
            programGroupBox.Margin = new Padding(4, 5, 4, 5);
            programGroupBox.Name = "programGroupBox";
            programGroupBox.Padding = new Padding(14, 17, 14, 17);
            programGroupBox.Size = new Size(1107, 150);
            programGroupBox.TabIndex = 2;
            programGroupBox.TabStop = false;
            programGroupBox.Text = "Program (Flash)";
            //
            // programLayout
            //
            programLayout.ColumnCount = 1;
            programLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            programLayout.Controls.Add(programInfoLabel, 0, 0);
            programLayout.Controls.Add(programButtonPanel, 0, 1);
            programLayout.Dock = DockStyle.Fill;
            programLayout.Location = new Point(14, 41);
            programLayout.Margin = new Padding(4, 5, 4, 5);
            programLayout.Name = "programLayout";
            programLayout.RowCount = 2;
            programLayout.RowStyles.Add(new RowStyle());
            programLayout.RowStyles.Add(new RowStyle());
            programLayout.Size = new Size(1079, 75);
            programLayout.TabIndex = 0;
            //
            // programInfoLabel
            //
            programInfoLabel.AutoSize = true;
            programInfoLabel.Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f);
            programInfoLabel.ForeColor = Color.Gray;
            programInfoLabel.Location = new Point(4, 0);
            programInfoLabel.Margin = new Padding(4, 0, 4, 5);
            programInfoLabel.Name = "programInfoLabel";
            programInfoLabel.Size = new Size(600, 20);
            programInfoLabel.TabIndex = 0;
            programInfoLabel.Text = "Downloads the ECU's compiled firmware (program, flash) to a .bin file. Largest region — may take several minutes. Requires an unlocked ECU.";
            //
            // programButtonPanel
            //
            programButtonPanel.AutoSize = true;
            programButtonPanel.Controls.Add(downloadProgramButton);
            programButtonPanel.Controls.Add(programProgressBar);
            programButtonPanel.Controls.Add(programStatusValueLabel);
            programButtonPanel.Dock = DockStyle.Fill;
            programButtonPanel.Location = new Point(4, 33);
            programButtonPanel.Margin = new Padding(4, 8, 4, 0);
            programButtonPanel.Name = "programButtonPanel";
            programButtonPanel.Size = new Size(1071, 42);
            programButtonPanel.TabIndex = 1;
            programButtonPanel.WrapContents = false;
            //
            // downloadProgramButton
            //
            downloadProgramButton.AutoSize = true;
            downloadProgramButton.Location = new Point(4, 3);
            downloadProgramButton.Margin = new Padding(4, 3, 4, 3);
            downloadProgramButton.Name = "downloadProgramButton";
            downloadProgramButton.Size = new Size(220, 35);
            downloadProgramButton.TabIndex = 0;
            downloadProgramButton.Text = "Download Program...";
            downloadProgramButton.UseVisualStyleBackColor = true;
            downloadProgramButton.Click += DownloadProgramButton_Click;
            //
            // programProgressBar
            //
            programProgressBar.Location = new Point(232, 3);
            programProgressBar.Margin = new Padding(8, 3, 8, 3);
            programProgressBar.Name = "programProgressBar";
            programProgressBar.Size = new Size(300, 29);
            programProgressBar.TabIndex = 1;
            //
            // programStatusValueLabel
            //
            programStatusValueLabel.Anchor = AnchorStyles.Left;
            programStatusValueLabel.AutoSize = true;
            programStatusValueLabel.Location = new Point(548, 8);
            programStatusValueLabel.Margin = new Padding(8, 5, 4, 0);
            programStatusValueLabel.Name = "programStatusValueLabel";
            programStatusValueLabel.Size = new Size(41, 25);
            programStatusValueLabel.TabIndex = 2;
            programStatusValueLabel.Text = "Idle";
            //
            // ecuVariantGroupBox
            //
            ecuVariantGroupBox.Controls.Add(ecuVariantPanel);
            ecuVariantGroupBox.Dock = DockStyle.Top;
            ecuVariantGroupBox.Location = new Point(14, 450);
            ecuVariantGroupBox.Margin = new Padding(4, 5, 4, 5);
            ecuVariantGroupBox.Name = "ecuVariantGroupBox";
            ecuVariantGroupBox.Padding = new Padding(14, 17, 14, 17);
            ecuVariantGroupBox.Size = new Size(1107, 120);
            ecuVariantGroupBox.TabIndex = 3;
            ecuVariantGroupBox.TabStop = false;
            ecuVariantGroupBox.Text = "ECU Version";
            //
            // ecuVariantPanel
            //
            ecuVariantPanel.Controls.Add(ecuVariantLabel);
            ecuVariantPanel.Controls.Add(ecuVariantComboBox);
            ecuVariantPanel.Dock = DockStyle.Fill;
            ecuVariantPanel.Location = new Point(14, 41);
            ecuVariantPanel.Margin = new Padding(4, 5, 4, 5);
            ecuVariantPanel.Name = "ecuVariantPanel";
            ecuVariantPanel.Size = new Size(1079, 42);
            ecuVariantPanel.TabIndex = 0;
            ecuVariantPanel.WrapContents = false;
            //
            // ecuVariantLabel
            //
            ecuVariantLabel.Anchor = AnchorStyles.Left;
            ecuVariantLabel.AutoSize = true;
            ecuVariantLabel.Location = new Point(4, 0);
            ecuVariantLabel.Margin = new Padding(4, 0, 8, 0);
            ecuVariantLabel.Name = "ecuVariantLabel";
            ecuVariantLabel.Size = new Size(120, 25);
            ecuVariantLabel.TabIndex = 0;
            ecuVariantLabel.Text = "ECU Version:";
            //
            // ecuVariantComboBox
            //
            ecuVariantComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            ecuVariantComboBox.Location = new Point(136, 0);
            ecuVariantComboBox.Margin = new Padding(4, 0, 4, 0);
            ecuVariantComboBox.Name = "ecuVariantComboBox";
            ecuVariantComboBox.Size = new Size(180, 33);
            ecuVariantComboBox.TabIndex = 1;
            //
            // SnapshotsControl
            //
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScroll = true;
            Controls.Add(programGroupBox);
            Controls.Add(calibrationGroupBox);
            Controls.Add(learnedDataGroupBox);
            Controls.Add(ecuVariantGroupBox);
            Margin = new Padding(4, 5, 4, 5);
            Name = "SnapshotsControl";
            Padding = new Padding(14, 17, 14, 17);
            Size = new Size(1143, 1000);
            learnedDataGroupBox.ResumeLayout(false);
            learnedDataGroupBox.PerformLayout();
            learnedDataLayout.ResumeLayout(false);
            learnedDataLayout.PerformLayout();
            learnedDataButtonPanel.ResumeLayout(false);
            learnedDataButtonPanel.PerformLayout();
            calibrationGroupBox.ResumeLayout(false);
            calibrationGroupBox.PerformLayout();
            calibrationLayout.ResumeLayout(false);
            calibrationLayout.PerformLayout();
            calibrationButtonPanel.ResumeLayout(false);
            calibrationButtonPanel.PerformLayout();
            programGroupBox.ResumeLayout(false);
            programGroupBox.PerformLayout();
            programLayout.ResumeLayout(false);
            programLayout.PerformLayout();
            programButtonPanel.ResumeLayout(false);
            programButtonPanel.PerformLayout();
            ecuVariantGroupBox.ResumeLayout(false);
            ecuVariantGroupBox.PerformLayout();
            ecuVariantPanel.ResumeLayout(false);
            ecuVariantPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox ecuVariantGroupBox;
        private FlowLayoutPanel ecuVariantPanel;
        private Label ecuVariantLabel;
        private ComboBox ecuVariantComboBox;
        private GroupBox learnedDataGroupBox;
        private TableLayoutPanel learnedDataLayout;
        private Label learnedDataInfoLabel;
        private FlowLayoutPanel learnedDataButtonPanel;
        private Button downloadLearnedDataButton;
        private ProgressBar learnedDataProgressBar;
        private Label learnedDataStatusValueLabel;
        private GroupBox calibrationGroupBox;
        private TableLayoutPanel calibrationLayout;
        private Label calibrationInfoLabel;
        private FlowLayoutPanel calibrationButtonPanel;
        private Button downloadCalibrationButton;
        private ProgressBar calibrationProgressBar;
        private Label calibrationStatusValueLabel;
        private GroupBox programGroupBox;
        private TableLayoutPanel programLayout;
        private Label programInfoLabel;
        private FlowLayoutPanel programButtonPanel;
        private Button downloadProgramButton;
        private ProgressBar programProgressBar;
        private Label programStatusValueLabel;
    }
}
