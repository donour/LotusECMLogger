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
            saveBaselineButton = new Button();
            liveStateTab = new TabPage();
            liveStateListView = new ListView();
            liveStateFieldColumn = new ColumnHeader();
            liveStateValueColumn = new ColumnHeader();
            liveStateDetailColumn = new ColumnHeader();
            liveStatePanel = new Panel();
            readLiveStateButton = new Button();
            liveStateHintLabel = new Label();
            startDiagnosticButton = new Button();
            stopDiagnosticButton = new Button();
            diagnosticIntervalLabel = new Label();
            diagnosticIntervalNumeric = new NumericUpDown();
            captureNotesLabel = new Label();
            captureNotesTextBox = new TextBox();
            openCaptureButton = new Button();
            exportCaptureButton = new Button();
            reviewBaselineButton = new Button();
            reviewSampleLabel = new Label();
            reviewSampleNumeric = new NumericUpDown();
            reviewCountLabel = new Label();
            captureStatusLabel = new Label();
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
            pumpRoutineLabel = new Label();
            durationLabel = new Label();
            durationNumeric = new NumericUpDown();
            pumpOperatorCheckBox = new CheckBox();
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
            ((System.ComponentModel.ISupportInitialize)diagnosticIntervalNumeric).BeginInit();
            ((System.ComponentModel.ISupportInitialize)reviewSampleNumeric).BeginInit();
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
            topPanel.Controls.Add(saveBaselineButton);
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
            moduleInfoButton.Size = new Size(130, 32);
            moduleInfoButton.TabIndex = 2;
            moduleInfoButton.Text = "Read Baseline";
            moduleInfoButton.UseVisualStyleBackColor = true;
            moduleInfoButton.Click += moduleInfoButton_Click;
            //
            // sniffBusButton
            //
            sniffBusButton.Location = new Point(387, 10);
            sniffBusButton.Name = "sniffBusButton";
            sniffBusButton.Size = new Size(100, 32);
            sniffBusButton.TabIndex = 3;
            sniffBusButton.Text = "Sniff Bus";
            sniffBusButton.UseVisualStyleBackColor = true;
            sniffBusButton.Click += sniffBusButton_Click;
            //
            // saveBaselineButton
            //
            saveBaselineButton.Enabled = false;
            saveBaselineButton.Location = new Point(493, 10);
            saveBaselineButton.Name = "saveBaselineButton";
            saveBaselineButton.Size = new Size(145, 32);
            saveBaselineButton.TabIndex = 4;
            saveBaselineButton.Text = "Save Baseline JSON";
            saveBaselineButton.UseVisualStyleBackColor = true;
            saveBaselineButton.Click += saveBaselineButton_Click;
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
            liveStateTab.Text = "Live Data && Captures";
            liveStateTab.UseVisualStyleBackColor = true;
            //
            // liveStateListView
            //
            liveStateListView.Columns.AddRange(new ColumnHeader[] { liveStateFieldColumn, liveStateValueColumn, liveStateDetailColumn });
            liveStateListView.Dock = DockStyle.Fill;
            liveStateListView.FullRowSelect = true;
            liveStateListView.GridLines = true;
            liveStateListView.Location = new Point(3, 177);
            liveStateListView.Name = "liveStateListView";
            liveStateListView.Size = new Size(886, 257);
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
            liveStatePanel.Controls.Add(startDiagnosticButton);
            liveStatePanel.Controls.Add(stopDiagnosticButton);
            liveStatePanel.Controls.Add(diagnosticIntervalLabel);
            liveStatePanel.Controls.Add(diagnosticIntervalNumeric);
            liveStatePanel.Controls.Add(captureNotesLabel);
            liveStatePanel.Controls.Add(captureNotesTextBox);
            liveStatePanel.Controls.Add(openCaptureButton);
            liveStatePanel.Controls.Add(exportCaptureButton);
            liveStatePanel.Controls.Add(reviewBaselineButton);
            liveStatePanel.Controls.Add(reviewSampleLabel);
            liveStatePanel.Controls.Add(reviewSampleNumeric);
            liveStatePanel.Controls.Add(reviewCountLabel);
            liveStatePanel.Controls.Add(captureStatusLabel);
            liveStatePanel.Dock = DockStyle.Top;
            liveStatePanel.Location = new Point(3, 3);
            liveStatePanel.Name = "liveStatePanel";
            liveStatePanel.Size = new Size(886, 174);
            liveStatePanel.TabIndex = 0;
            //
            // readLiveStateButton
            //
            readLiveStateButton.Location = new Point(9, 10);
            readLiveStateButton.Name = "readLiveStateButton";
            readLiveStateButton.Size = new Size(130, 32);
            readLiveStateButton.TabIndex = 0;
            readLiveStateButton.Text = "Read Live Data";
            readLiveStateButton.UseVisualStyleBackColor = true;
            readLiveStateButton.Click += readLiveStateButton_Click;
            //
            // liveStateHintLabel
            //
            liveStateHintLabel.AutoSize = false;
            liveStateHintLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            liveStateHintLabel.Location = new Point(9, 126);
            liveStateHintLabel.Name = "liveStateHintLabel";
            liveStateHintLabel.Size = new Size(865, 18);
            liveStateHintLabel.TabIndex = 1;
            liveStateHintLabel.Text = "Diagnostic Live Data (61 04). Captures preserve raw replies, timestamps, and baseline details.";
            //
            // diagnostic capture controls
            //
            startDiagnosticButton.Location = new Point(145, 10);
            startDiagnosticButton.Name = "startDiagnosticButton";
            startDiagnosticButton.Size = new Size(140, 32);
            startDiagnosticButton.TabIndex = 2;
            startDiagnosticButton.Text = "Start Capture";
            startDiagnosticButton.UseVisualStyleBackColor = true;
            startDiagnosticButton.Click += startDiagnosticButton_Click;
            stopDiagnosticButton.Enabled = false;
            stopDiagnosticButton.Location = new Point(291, 10);
            stopDiagnosticButton.Name = "stopDiagnosticButton";
            stopDiagnosticButton.Size = new Size(90, 32);
            stopDiagnosticButton.TabIndex = 3;
            stopDiagnosticButton.Text = "Stop";
            stopDiagnosticButton.UseVisualStyleBackColor = true;
            stopDiagnosticButton.Click += stopDiagnosticButton_Click;
            diagnosticIntervalLabel.AutoSize = true;
            diagnosticIntervalLabel.Location = new Point(395, 18);
            diagnosticIntervalLabel.Name = "diagnosticIntervalLabel";
            diagnosticIntervalLabel.Text = "Interval (ms):";
            diagnosticIntervalNumeric.Location = new Point(480, 15);
            diagnosticIntervalNumeric.Minimum = 100;
            diagnosticIntervalNumeric.Maximum = 5000;
            diagnosticIntervalNumeric.Increment = 100;
            diagnosticIntervalNumeric.Value = 200;
            diagnosticIntervalNumeric.Name = "diagnosticIntervalNumeric";
            diagnosticIntervalNumeric.Size = new Size(80, 23);
            diagnosticIntervalNumeric.TabIndex = 4;
            captureNotesLabel.AutoSize = true;
            captureNotesLabel.Location = new Point(9, 55);
            captureNotesLabel.Name = "captureNotesLabel";
            captureNotesLabel.Text = "Notes:";
            captureNotesTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            captureNotesTextBox.Location = new Point(62, 48);
            captureNotesTextBox.Name = "captureNotesTextBox";
            captureNotesTextBox.Size = new Size(812, 40);
            captureNotesTextBox.Multiline = true;
            captureNotesTextBox.MaxLength = 16000;
            captureNotesTextBox.ScrollBars = ScrollBars.Vertical;
            captureNotesTextBox.PlaceholderText = "Tire specifications, pressures, vehicle load, and reference measurements (saved with capture/baseline)";
            captureNotesTextBox.TabIndex = 5;
            openCaptureButton.Location = new Point(9, 94);
            openCaptureButton.Name = "openCaptureButton";
            openCaptureButton.Size = new Size(130, 28);
            openCaptureButton.TabIndex = 6;
            openCaptureButton.Text = "Open Capture...";
            openCaptureButton.UseVisualStyleBackColor = true;
            openCaptureButton.Click += openCaptureButton_Click;
            exportCaptureButton.Enabled = false;
            exportCaptureButton.Location = new Point(145, 94);
            exportCaptureButton.Name = "exportCaptureButton";
            exportCaptureButton.Size = new Size(110, 28);
            exportCaptureButton.TabIndex = 7;
            exportCaptureButton.Text = "Export CSV...";
            exportCaptureButton.UseVisualStyleBackColor = true;
            exportCaptureButton.Click += exportCaptureButton_Click;
            reviewBaselineButton.Enabled = false;
            reviewBaselineButton.Location = new Point(261, 94);
            reviewBaselineButton.Name = "reviewBaselineButton";
            reviewBaselineButton.Size = new Size(120, 28);
            reviewBaselineButton.TabIndex = 8;
            reviewBaselineButton.Text = "View Baseline";
            reviewBaselineButton.UseVisualStyleBackColor = true;
            reviewBaselineButton.Click += reviewBaselineButton_Click;
            reviewSampleLabel.AutoSize = true;
            reviewSampleLabel.Location = new Point(395, 100);
            reviewSampleLabel.Name = "reviewSampleLabel";
            reviewSampleLabel.Text = "Sample:";
            reviewSampleNumeric.Enabled = false;
            reviewSampleNumeric.Location = new Point(450, 97);
            reviewSampleNumeric.Minimum = 1;
            reviewSampleNumeric.Maximum = 1;
            reviewSampleNumeric.Value = 1;
            reviewSampleNumeric.ThousandsSeparator = true;
            reviewSampleNumeric.Name = "reviewSampleNumeric";
            reviewSampleNumeric.Size = new Size(100, 23);
            reviewSampleNumeric.TabIndex = 9;
            reviewSampleNumeric.ValueChanged += reviewSampleNumeric_ValueChanged;
            reviewCountLabel.AutoSize = true;
            reviewCountLabel.Location = new Point(558, 100);
            reviewCountLabel.Name = "reviewCountLabel";
            reviewCountLabel.Text = "No saved capture open";
            captureStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            captureStatusLabel.AutoEllipsis = true;
            captureStatusLabel.Location = new Point(9, 149);
            captureStatusLabel.Name = "captureStatusLabel";
            captureStatusLabel.Size = new Size(865, 20);
            captureStatusLabel.Text = "Read a live sample, start a capture, or open saved data for offline review.";
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
            telemetryTab.Text = "Passive Broadcasts";
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
            telemetryHintLabel.Text = "Passive broadcasts — layout and scale are provisional.";
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
            actuationTab.Text = "Pump Test";
            actuationTab.UseVisualStyleBackColor = true;
            //
            // actuationListView
            //
            actuationListView.Columns.AddRange(new ColumnHeader[] { actuationFieldColumn, actuationValueColumn, actuationDetailColumn });
            actuationListView.Dock = DockStyle.Fill;
            actuationListView.FullRowSelect = true;
            actuationListView.GridLines = true;
            actuationListView.Location = new Point(3, 193);
            actuationListView.Name = "actuationListView";
            actuationListView.Size = new Size(886, 241);
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
            actuationPanel.Controls.Add(pumpRoutineLabel);
            actuationPanel.Controls.Add(durationLabel);
            actuationPanel.Controls.Add(durationNumeric);
            actuationPanel.Controls.Add(pumpOperatorCheckBox);
            actuationPanel.Controls.Add(runRoutineButton);
            actuationPanel.Controls.Add(stopRoutineButton);
            actuationPanel.Controls.Add(actuationWarningLabel);
            actuationPanel.Controls.Add(actuationProgressLabel);
            actuationPanel.Dock = DockStyle.Top;
            actuationPanel.Location = new Point(3, 3);
            actuationPanel.Name = "actuationPanel";
            actuationPanel.Size = new Size(886, 190);
            actuationPanel.TabIndex = 0;
            //
            // pumpRoutineLabel
            //
            pumpRoutineLabel.AutoSize = true;
            pumpRoutineLabel.Location = new Point(9, 80);
            pumpRoutineLabel.Name = "pumpRoutineLabel";
            pumpRoutineLabel.Size = new Size(0, 15);
            pumpRoutineLabel.TabIndex = 1;
            pumpRoutineLabel.Text = "Pump motor test (OEM routine 06)";
            //
            // durationLabel
            //
            durationLabel.AutoSize = true;
            durationLabel.Location = new Point(318, 80);
            durationLabel.Name = "durationLabel";
            durationLabel.Size = new Size(55, 15);
            durationLabel.TabIndex = 2;
            durationLabel.Text = "Seconds:";
            //
            // durationNumeric
            //
            durationNumeric.Enabled = false;
            durationNumeric.Location = new Point(379, 77);
            durationNumeric.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            durationNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            durationNumeric.Name = "durationNumeric";
            durationNumeric.Size = new Size(60, 23);
            durationNumeric.TabIndex = 3;
            durationNumeric.Value = new decimal(new int[] { 2, 0, 0, 0 });
            //
            // pumpOperatorCheckBox
            //
            pumpOperatorCheckBox.AutoSize = true;
            pumpOperatorCheckBox.Enabled = false;
            pumpOperatorCheckBox.Location = new Point(9, 113);
            pumpOperatorCheckBox.Name = "pumpOperatorCheckBox";
            pumpOperatorCheckBox.Size = new Size(0, 19);
            pumpOperatorCheckBox.TabIndex = 6;
            pumpOperatorCheckBox.Text = "Vehicle stationary, engine off, ignition on";
            pumpOperatorCheckBox.UseVisualStyleBackColor = true;
            pumpOperatorCheckBox.CheckedChanged += pumpOperatorCheckBox_CheckedChanged;
            //
            // runRoutineButton
            //
            runRoutineButton.Enabled = false;
            runRoutineButton.Location = new Point(455, 73);
            runRoutineButton.Name = "runRoutineButton";
            runRoutineButton.Size = new Size(140, 30);
            runRoutineButton.TabIndex = 4;
            runRoutineButton.Text = "Run Pump Test";
            runRoutineButton.UseVisualStyleBackColor = true;
            runRoutineButton.Click += runRoutineButton_Click;
            //
            // stopRoutineButton
            //
            stopRoutineButton.Enabled = false;
            stopRoutineButton.Location = new Point(601, 73);
            stopRoutineButton.Name = "stopRoutineButton";
            stopRoutineButton.Size = new Size(100, 30);
            stopRoutineButton.TabIndex = 5;
            stopRoutineButton.Text = "Stop";
            stopRoutineButton.UseVisualStyleBackColor = true;
            stopRoutineButton.Click += stopRoutineButton_Click;
            //
            // actuationWarningLabel
            //
            actuationWarningLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actuationWarningLabel.Location = new Point(9, 9);
            actuationWarningLabel.Name = "actuationWarningLabel";
            actuationWarningLabel.Size = new Size(866, 62);
            actuationWarningLabel.TabIndex = 0;
            actuationWarningLabel.Text = "Short pump test; no bleeding or valve cycle. Operator conditions below are not measured by the app.\r\nRoutine completion leaves the relay command latched; the app sends explicit OFF and stop requests.\r\nIf OFF or stop is unconfirmed, turn ignition off. The 1–5 s range is a software limit, not an OEM rating.";
            //
            // actuationProgressLabel
            //
            actuationProgressLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actuationProgressLabel.Location = new Point(9, 141);
            actuationProgressLabel.Name = "actuationProgressLabel";
            actuationProgressLabel.Size = new Size(866, 44);
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
            statusLabel.AutoSize = false;
            statusLabel.AutoEllipsis = true;
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusLabel.Location = new Point(9, 5);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(880, 18);
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
            ((System.ComponentModel.ISupportInitialize)diagnosticIntervalNumeric).EndInit();
            ((System.ComponentModel.ISupportInitialize)reviewSampleNumeric).EndInit();
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
        private Button saveBaselineButton;
        private ListView infoListView;
        private ColumnHeader infoFieldColumn;
        private ColumnHeader infoValueColumn;
        private ColumnHeader infoDetailColumn;

        private TabPage liveStateTab;
        private Panel liveStatePanel;
        private Button readLiveStateButton;
        private Label liveStateHintLabel;
        private Button startDiagnosticButton;
        private Button stopDiagnosticButton;
        private Label diagnosticIntervalLabel;
        private NumericUpDown diagnosticIntervalNumeric;
        private Label captureNotesLabel;
        private TextBox captureNotesTextBox;
        private Button openCaptureButton;
        private Button exportCaptureButton;
        private Button reviewBaselineButton;
        private Label reviewSampleLabel;
        private NumericUpDown reviewSampleNumeric;
        private Label reviewCountLabel;
        private Label captureStatusLabel;
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
        private Label pumpRoutineLabel;
        private Label durationLabel;
        private NumericUpDown durationNumeric;
        private CheckBox pumpOperatorCheckBox;
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
