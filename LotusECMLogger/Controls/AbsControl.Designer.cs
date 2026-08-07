// The Windows Forms designer resolves the type names in InitializeComponent from this file's own
// using directives — it does not see the project's implicit global usings — so they are spelled out.
using System.Drawing;
using System.Windows.Forms;

namespace LotusECMLogger.Controls
{
    partial class AbsControl
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        partial void DisposeManaged();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeManaged();
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
            absTabs = new TabControl();
            moduleTab = new TabPage();
            infoListView = new ListView();
            infoFieldColumn = new ColumnHeader();
            infoValueColumn = new ColumnHeader();
            infoDetailColumn = new ColumnHeader();
            topPanel = new Panel();
            testConnectionButton = new Button();
            readInfoButton = new Button();
            moduleInfoButton = new Button();
            sniffBusButton = new Button();
            liveStateTab = new TabPage();
            liveStateListView = new ListView();
            liveStateFieldColumn = new ColumnHeader();
            liveStateValueColumn = new ColumnHeader();
            liveStateDetailColumn = new ColumnHeader();
            liveStatePanel = new Panel();
            readLiveStateButton = new Button();
            liveStateHintLabel = new Label();
            telemetryTab = new TabPage();
            telemetryListView = new ListView();
            telemetryFieldColumn = new ColumnHeader();
            telemetryValueColumn = new ColumnHeader();
            telemetryDetailColumn = new ColumnHeader();
            telemetryPanel = new Panel();
            startTelemetryButton = new Button();
            stopTelemetryButton = new Button();
            logTelemetryCheckBox = new CheckBox();
            telemetryHintLabel = new Label();
            actuationTab = new TabPage();
            actuationListView = new ListView();
            actuationFieldColumn = new ColumnHeader();
            actuationValueColumn = new ColumnHeader();
            actuationDetailColumn = new ColumnHeader();
            actuationPanel = new Panel();
            routineComboBox = new ComboBox();
            durationLabel = new Label();
            durationNumeric = new NumericUpDown();
            checkPreconditionsButton = new Button();
            runRoutineButton = new Button();
            stopRoutineButton = new Button();
            actuationWarningLabel = new Label();
            actuationProgressLabel = new Label();
            statusPanel = new Panel();
            statusLabel = new Label();
            absTabs.SuspendLayout();
            moduleTab.SuspendLayout();
            topPanel.SuspendLayout();
            liveStateTab.SuspendLayout();
            liveStatePanel.SuspendLayout();
            telemetryTab.SuspendLayout();
            telemetryPanel.SuspendLayout();
            actuationTab.SuspendLayout();
            actuationPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)durationNumeric).BeginInit();
            statusPanel.SuspendLayout();
            SuspendLayout();
            //
            // absTabs
            //
            absTabs.Controls.Add(moduleTab);
            absTabs.Controls.Add(liveStateTab);
            absTabs.Controls.Add(telemetryTab);
            absTabs.Controls.Add(actuationTab);
            absTabs.Dock = DockStyle.Fill;
            absTabs.Location = new Point(0, 0);
            absTabs.Name = "absTabs";
            absTabs.SelectedIndex = 0;
            absTabs.Size = new Size(900, 465);
            absTabs.TabIndex = 0;
            //
            // moduleTab
            //
            moduleTab.Controls.Add(infoListView);
            moduleTab.Controls.Add(topPanel);
            moduleTab.Location = new Point(4, 24);
            moduleTab.Name = "moduleTab";
            moduleTab.Padding = new Padding(3);
            moduleTab.Size = new Size(892, 437);
            moduleTab.TabIndex = 0;
            moduleTab.Text = "Module && Faults";
            moduleTab.UseVisualStyleBackColor = true;
            //
            // infoListView
            //
            infoListView.Columns.AddRange(new ColumnHeader[] { infoFieldColumn, infoValueColumn, infoDetailColumn });
            infoListView.Dock = DockStyle.Fill;
            infoListView.FullRowSelect = true;
            infoListView.GridLines = true;
            infoListView.Location = new Point(3, 55);
            infoListView.Name = "infoListView";
            infoListView.Size = new Size(886, 379);
            infoListView.TabIndex = 1;
            infoListView.UseCompatibleStateImageBehavior = false;
            infoListView.View = View.Details;
            //
            // infoFieldColumn
            //
            infoFieldColumn.Text = "Field";
            infoFieldColumn.Width = 230;
            //
            // infoValueColumn
            //
            infoValueColumn.Text = "Value";
            infoValueColumn.Width = 260;
            //
            // infoDetailColumn
            //
            infoDetailColumn.Text = "Detail";
            infoDetailColumn.Width = 420;
            //
            // topPanel
            //
            topPanel.Controls.Add(testConnectionButton);
            topPanel.Controls.Add(readInfoButton);
            topPanel.Controls.Add(moduleInfoButton);
            topPanel.Controls.Add(sniffBusButton);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(3, 3);
            topPanel.Name = "topPanel";
            topPanel.Size = new Size(886, 52);
            topPanel.TabIndex = 0;
            //
            // testConnectionButton
            //
            testConnectionButton.Location = new Point(9, 10);
            testConnectionButton.Name = "testConnectionButton";
            testConnectionButton.Size = new Size(130, 32);
            testConnectionButton.TabIndex = 0;
            testConnectionButton.Text = "Test Connection";
            testConnectionButton.UseVisualStyleBackColor = true;
            testConnectionButton.Click += testConnectionButton_Click;
            //
            // readInfoButton
            //
            readInfoButton.Location = new Point(145, 10);
            readInfoButton.Name = "readInfoButton";
            readInfoButton.Size = new Size(100, 32);
            readInfoButton.TabIndex = 1;
            readInfoButton.Text = "Read DTCs";
            readInfoButton.UseVisualStyleBackColor = true;
            readInfoButton.Click += readInfoButton_Click;
            //
            // moduleInfoButton
            //
            moduleInfoButton.Location = new Point(251, 10);
            moduleInfoButton.Name = "moduleInfoButton";
            moduleInfoButton.Size = new Size(100, 32);
            moduleInfoButton.TabIndex = 2;
            moduleInfoButton.Text = "Read Info";
            moduleInfoButton.UseVisualStyleBackColor = true;
            moduleInfoButton.Click += moduleInfoButton_Click;
            //
            // sniffBusButton
            //
            sniffBusButton.Location = new Point(357, 10);
            sniffBusButton.Name = "sniffBusButton";
            sniffBusButton.Size = new Size(100, 32);
            sniffBusButton.TabIndex = 3;
            sniffBusButton.Text = "Sniff Bus";
            sniffBusButton.UseVisualStyleBackColor = true;
            sniffBusButton.Click += sniffBusButton_Click;
            //
            // liveStateTab
            //
            liveStateTab.Controls.Add(liveStateListView);
            liveStateTab.Controls.Add(liveStatePanel);
            liveStateTab.Location = new Point(4, 24);
            liveStateTab.Name = "liveStateTab";
            liveStateTab.Padding = new Padding(3);
            liveStateTab.Size = new Size(892, 437);
            liveStateTab.TabIndex = 1;
            liveStateTab.Text = "Live State";
            liveStateTab.UseVisualStyleBackColor = true;
            //
            // liveStateListView
            //
            liveStateListView.Columns.AddRange(new ColumnHeader[] { liveStateFieldColumn, liveStateValueColumn, liveStateDetailColumn });
            liveStateListView.Dock = DockStyle.Fill;
            liveStateListView.FullRowSelect = true;
            liveStateListView.GridLines = true;
            liveStateListView.Location = new Point(3, 55);
            liveStateListView.Name = "liveStateListView";
            liveStateListView.Size = new Size(886, 379);
            liveStateListView.TabIndex = 1;
            liveStateListView.UseCompatibleStateImageBehavior = false;
            liveStateListView.View = View.Details;
            //
            // liveStateFieldColumn
            //
            liveStateFieldColumn.Text = "Field";
            liveStateFieldColumn.Width = 230;
            //
            // liveStateValueColumn
            //
            liveStateValueColumn.Text = "Value";
            liveStateValueColumn.Width = 260;
            //
            // liveStateDetailColumn
            //
            liveStateDetailColumn.Text = "Detail";
            liveStateDetailColumn.Width = 420;
            //
            // liveStatePanel
            //
            liveStatePanel.Controls.Add(readLiveStateButton);
            liveStatePanel.Controls.Add(liveStateHintLabel);
            liveStatePanel.Dock = DockStyle.Top;
            liveStatePanel.Location = new Point(3, 3);
            liveStatePanel.Name = "liveStatePanel";
            liveStatePanel.Size = new Size(886, 52);
            liveStatePanel.TabIndex = 0;
            //
            // readLiveStateButton
            //
            readLiveStateButton.Location = new Point(9, 10);
            readLiveStateButton.Name = "readLiveStateButton";
            readLiveStateButton.Size = new Size(130, 32);
            readLiveStateButton.TabIndex = 0;
            readLiveStateButton.Text = "Read Live State";
            readLiveStateButton.UseVisualStyleBackColor = true;
            readLiveStateButton.Click += readLiveStateButton_Click;
            //
            // liveStateHintLabel
            //
            liveStateHintLabel.AutoSize = true;
            liveStateHintLabel.Location = new Point(148, 19);
            liveStateHintLabel.Name = "liveStateHintLabel";
            liveStateHintLabel.Size = new Size(0, 15);
            liveStateHintLabel.TabIndex = 1;
            liveStateHintLabel.Text = "Reads ABS RAM (mu, EDC, valves, pressures) — read-only, safe while driving.";
            //
            // telemetryTab
            //
            telemetryTab.Controls.Add(telemetryListView);
            telemetryTab.Controls.Add(telemetryPanel);
            telemetryTab.Location = new Point(4, 24);
            telemetryTab.Name = "telemetryTab";
            telemetryTab.Padding = new Padding(3);
            telemetryTab.Size = new Size(892, 437);
            telemetryTab.TabIndex = 2;
            telemetryTab.Text = "Telemetry";
            telemetryTab.UseVisualStyleBackColor = true;
            //
            // telemetryListView
            //
            telemetryListView.Columns.AddRange(new ColumnHeader[] { telemetryFieldColumn, telemetryValueColumn, telemetryDetailColumn });
            telemetryListView.Dock = DockStyle.Fill;
            telemetryListView.FullRowSelect = true;
            telemetryListView.GridLines = true;
            telemetryListView.Location = new Point(3, 55);
            telemetryListView.Name = "telemetryListView";
            telemetryListView.Size = new Size(886, 379);
            telemetryListView.TabIndex = 1;
            telemetryListView.UseCompatibleStateImageBehavior = false;
            telemetryListView.View = View.Details;
            //
            // telemetryFieldColumn
            //
            telemetryFieldColumn.Text = "Field";
            telemetryFieldColumn.Width = 230;
            //
            // telemetryValueColumn
            //
            telemetryValueColumn.Text = "Value";
            telemetryValueColumn.Width = 260;
            //
            // telemetryDetailColumn
            //
            telemetryDetailColumn.Text = "Detail";
            telemetryDetailColumn.Width = 420;
            //
            // telemetryPanel
            //
            telemetryPanel.Controls.Add(startTelemetryButton);
            telemetryPanel.Controls.Add(stopTelemetryButton);
            telemetryPanel.Controls.Add(logTelemetryCheckBox);
            telemetryPanel.Controls.Add(telemetryHintLabel);
            telemetryPanel.Dock = DockStyle.Top;
            telemetryPanel.Location = new Point(3, 3);
            telemetryPanel.Name = "telemetryPanel";
            telemetryPanel.Size = new Size(886, 52);
            telemetryPanel.TabIndex = 0;
            //
            // startTelemetryButton
            //
            startTelemetryButton.Location = new Point(9, 10);
            startTelemetryButton.Name = "startTelemetryButton";
            startTelemetryButton.Size = new Size(130, 32);
            startTelemetryButton.TabIndex = 0;
            startTelemetryButton.Text = "Start Monitor";
            startTelemetryButton.UseVisualStyleBackColor = true;
            startTelemetryButton.Click += startTelemetryButton_Click;
            //
            // stopTelemetryButton
            //
            stopTelemetryButton.Enabled = false;
            stopTelemetryButton.Location = new Point(145, 10);
            stopTelemetryButton.Name = "stopTelemetryButton";
            stopTelemetryButton.Size = new Size(100, 32);
            stopTelemetryButton.TabIndex = 1;
            stopTelemetryButton.Text = "Stop";
            stopTelemetryButton.UseVisualStyleBackColor = true;
            stopTelemetryButton.Click += stopTelemetryButton_Click;
            //
            // logTelemetryCheckBox
            //
            logTelemetryCheckBox.AutoSize = true;
            logTelemetryCheckBox.Checked = true;
            logTelemetryCheckBox.CheckState = CheckState.Checked;
            logTelemetryCheckBox.Location = new Point(255, 18);
            logTelemetryCheckBox.Name = "logTelemetryCheckBox";
            logTelemetryCheckBox.Size = new Size(85, 19);
            logTelemetryCheckBox.TabIndex = 2;
            logTelemetryCheckBox.Text = "Log to CSV";
            logTelemetryCheckBox.UseVisualStyleBackColor = true;
            //
            // telemetryHintLabel
            //
            telemetryHintLabel.AutoSize = true;
            telemetryHintLabel.Location = new Point(350, 19);
            telemetryHintLabel.Name = "telemetryHintLabel";
            telemetryHintLabel.Size = new Size(0, 15);
            telemetryHintLabel.TabIndex = 3;
            telemetryHintLabel.Text = "Passive 100 Hz broadcasts — transmits nothing, safe while driving.";
            //
            // actuationTab
            //
            actuationTab.Controls.Add(actuationListView);
            actuationTab.Controls.Add(actuationPanel);
            actuationTab.Location = new Point(4, 24);
            actuationTab.Name = "actuationTab";
            actuationTab.Padding = new Padding(3);
            actuationTab.Size = new Size(892, 437);
            actuationTab.TabIndex = 3;
            actuationTab.Text = "Pump && Valves (untested)";
            actuationTab.UseVisualStyleBackColor = true;
            //
            // actuationListView
            //
            actuationListView.Columns.AddRange(new ColumnHeader[] { actuationFieldColumn, actuationValueColumn, actuationDetailColumn });
            actuationListView.Dock = DockStyle.Fill;
            actuationListView.FullRowSelect = true;
            actuationListView.GridLines = true;
            actuationListView.Location = new Point(3, 95);
            actuationListView.Name = "actuationListView";
            actuationListView.Size = new Size(886, 339);
            actuationListView.TabIndex = 1;
            actuationListView.UseCompatibleStateImageBehavior = false;
            actuationListView.View = View.Details;
            //
            // actuationFieldColumn
            //
            actuationFieldColumn.Text = "Field";
            actuationFieldColumn.Width = 230;
            //
            // actuationValueColumn
            //
            actuationValueColumn.Text = "Value";
            actuationValueColumn.Width = 260;
            //
            // actuationDetailColumn
            //
            actuationDetailColumn.Text = "Detail";
            actuationDetailColumn.Width = 420;
            //
            // actuationPanel
            //
            actuationPanel.Controls.Add(routineComboBox);
            actuationPanel.Controls.Add(durationLabel);
            actuationPanel.Controls.Add(durationNumeric);
            actuationPanel.Controls.Add(checkPreconditionsButton);
            actuationPanel.Controls.Add(runRoutineButton);
            actuationPanel.Controls.Add(stopRoutineButton);
            actuationPanel.Controls.Add(actuationWarningLabel);
            actuationPanel.Controls.Add(actuationProgressLabel);
            actuationPanel.Dock = DockStyle.Top;
            actuationPanel.Location = new Point(3, 3);
            actuationPanel.Name = "actuationPanel";
            actuationPanel.Size = new Size(886, 92);
            actuationPanel.TabIndex = 0;
            //
            // routineComboBox
            //
            routineComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            routineComboBox.Location = new Point(9, 34);
            routineComboBox.Name = "routineComboBox";
            routineComboBox.Size = new Size(300, 23);
            routineComboBox.TabIndex = 1;
            routineComboBox.SelectedIndexChanged += routineComboBox_SelectedIndexChanged;
            //
            // durationLabel
            //
            durationLabel.AutoSize = true;
            durationLabel.Location = new Point(318, 37);
            durationLabel.Name = "durationLabel";
            durationLabel.Size = new Size(55, 15);
            durationLabel.TabIndex = 2;
            durationLabel.Text = "Seconds:";
            //
            // durationNumeric
            //
            durationNumeric.Location = new Point(379, 34);
            durationNumeric.Maximum = new decimal(new int[] { 120, 0, 0, 0 });
            durationNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            durationNumeric.Name = "durationNumeric";
            durationNumeric.Size = new Size(60, 23);
            durationNumeric.TabIndex = 3;
            durationNumeric.Value = new decimal(new int[] { 30, 0, 0, 0 });
            //
            // checkPreconditionsButton
            //
            checkPreconditionsButton.Location = new Point(455, 31);
            checkPreconditionsButton.Name = "checkPreconditionsButton";
            checkPreconditionsButton.Size = new Size(140, 30);
            checkPreconditionsButton.TabIndex = 4;
            checkPreconditionsButton.Text = "Check Preconditions";
            checkPreconditionsButton.UseVisualStyleBackColor = true;
            checkPreconditionsButton.Click += checkPreconditionsButton_Click;
            //
            // runRoutineButton
            //
            runRoutineButton.Location = new Point(601, 31);
            runRoutineButton.Name = "runRoutineButton";
            runRoutineButton.Size = new Size(100, 30);
            runRoutineButton.TabIndex = 5;
            runRoutineButton.Text = "Run";
            runRoutineButton.UseVisualStyleBackColor = true;
            runRoutineButton.Click += runRoutineButton_Click;
            //
            // stopRoutineButton
            //
            stopRoutineButton.Enabled = false;
            stopRoutineButton.Location = new Point(707, 31);
            stopRoutineButton.Name = "stopRoutineButton";
            stopRoutineButton.Size = new Size(100, 30);
            stopRoutineButton.TabIndex = 6;
            stopRoutineButton.Text = "Stop";
            stopRoutineButton.UseVisualStyleBackColor = true;
            stopRoutineButton.Click += stopRoutineButton_Click;
            //
            // actuationWarningLabel
            //
            actuationWarningLabel.AutoSize = true;
            actuationWarningLabel.Location = new Point(9, 9);
            actuationWarningLabel.Name = "actuationWarningLabel";
            actuationWarningLabel.Size = new Size(0, 15);
            actuationWarningLabel.TabIndex = 0;
            actuationWarningLabel.Text = "Drives the ABS pump and valves — stationary vehicle, ignition ON, engine OFF, brake released.";
            //
            // actuationProgressLabel
            //
            actuationProgressLabel.AutoSize = true;
            actuationProgressLabel.Location = new Point(9, 66);
            actuationProgressLabel.Name = "actuationProgressLabel";
            actuationProgressLabel.Size = new Size(0, 15);
            actuationProgressLabel.TabIndex = 7;
            //
            // statusPanel
            //
            statusPanel.Controls.Add(statusLabel);
            statusPanel.Dock = DockStyle.Bottom;
            statusPanel.Location = new Point(0, 465);
            statusPanel.Name = "statusPanel";
            statusPanel.Size = new Size(900, 24);
            statusPanel.TabIndex = 1;
            //
            // statusLabel
            //
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(9, 5);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 0;
            //
            // AbsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(absTabs);
            Controls.Add(statusPanel);
            Margin = new Padding(3, 2, 3, 2);
            Name = "AbsControl";
            Size = new Size(900, 489);
            absTabs.ResumeLayout(false);
            moduleTab.ResumeLayout(false);
            topPanel.ResumeLayout(false);
            liveStateTab.ResumeLayout(false);
            liveStatePanel.ResumeLayout(false);
            liveStatePanel.PerformLayout();
            telemetryTab.ResumeLayout(false);
            telemetryPanel.ResumeLayout(false);
            telemetryPanel.PerformLayout();
            actuationTab.ResumeLayout(false);
            actuationPanel.ResumeLayout(false);
            actuationPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)durationNumeric).EndInit();
            statusPanel.ResumeLayout(false);
            statusPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TabControl absTabs;

        private TabPage moduleTab;
        private Panel topPanel;
        private Button testConnectionButton;
        private Button readInfoButton;
        private Button moduleInfoButton;
        private Button sniffBusButton;
        private ListView infoListView;
        private ColumnHeader infoFieldColumn;
        private ColumnHeader infoValueColumn;
        private ColumnHeader infoDetailColumn;

        private TabPage liveStateTab;
        private Panel liveStatePanel;
        private Button readLiveStateButton;
        private Label liveStateHintLabel;
        private ListView liveStateListView;
        private ColumnHeader liveStateFieldColumn;
        private ColumnHeader liveStateValueColumn;
        private ColumnHeader liveStateDetailColumn;

        private TabPage telemetryTab;
        private Panel telemetryPanel;
        private Button startTelemetryButton;
        private Button stopTelemetryButton;
        private CheckBox logTelemetryCheckBox;
        private Label telemetryHintLabel;
        private ListView telemetryListView;
        private ColumnHeader telemetryFieldColumn;
        private ColumnHeader telemetryValueColumn;
        private ColumnHeader telemetryDetailColumn;

        private TabPage actuationTab;
        private Panel actuationPanel;
        private ComboBox routineComboBox;
        private Label durationLabel;
        private NumericUpDown durationNumeric;
        private Button checkPreconditionsButton;
        private Button runRoutineButton;
        private Button stopRoutineButton;
        private Label actuationWarningLabel;
        private Label actuationProgressLabel;
        private ListView actuationListView;
        private ColumnHeader actuationFieldColumn;
        private ColumnHeader actuationValueColumn;
        private ColumnHeader actuationDetailColumn;

        private Panel statusPanel;
        private Label statusLabel;
    }
}
