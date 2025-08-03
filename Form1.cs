using SAE.J2534;
using System.ComponentModel;
using System.Text.Unicode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LotusECMLogger
{
    public partial class LoggerWindow : Form, INotifyPropertyChanged
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer? components = null;

        private J2534OBDLogger logger;
        private Dictionary<String, float> liveData = new Dictionary<string, float>();
        private DateTime lastUpdateTime = DateTime.Now;
        private T6eCodingDecoder originalCodingDecoder;
        private T6eCodingDecoder modifiedCodingDecoder;
        private Dictionary<string, Control> codingControls = new Dictionary<string, Control>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string selectedObdConfigName = "NO CONFIG";

        private bool _loggerEnabled = false;
        public bool loggerEnabled
        {
            get => _loggerEnabled;
            set
            {
                if (_loggerEnabled != value)
                {
                    _loggerEnabled = value;
                    OnPropertyChanged(nameof(loggerEnabled));
                    // Update button states
                    startLogger_button.Enabled = !value;
                    stopLogger_button.Enabled = value;
                }
            }
        }

        public LoggerWindow()
        {
            InitializeComponent();
            // Populate OBD config menu
            PopulateObdConfigMenu();
            // Initialize ListView columns
            InitializeListView();
            // dummy logger to avoid null reference exceptions
            logger = new J2534OBDLogger("unused", Logger_DataLogged, Logger_ExceptionOccurred, new OBDConfiguration());
            // Handle form closing to ensure logger is stopped
            this.FormClosing += LoggerWindow_FormClosing;
            // Initial button state
            loggerEnabled = false;
        }

        /// <summary>
        /// Handle form closing event to ensure logger is safely stopped
        /// </summary>
        private void LoggerWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Stop the logger before the form closes
            logger?.Stop();
            logger?.Dispose();
        }

        private void InitializeListView()
        {
            liveDataView.Columns.Add("Parameter", 200);
            liveDataView.Columns.Add("Value", 100);

            codingDataView.Columns.Add("Option", 200);
            codingDataView.Columns.Add("Value", 100);
        }

        private void PopulateObdConfigMenu()
        {
            obdConfigToolStripMenuItem.DropDownItems.Clear();
            var configs = OBDConfigurationLoader.GetAvailableConfigurations();
            if (configs.Count == 0)
            {
                var noneItem = new ToolStripMenuItem("No configs found") { Enabled = false };
                obdConfigToolStripMenuItem.DropDownItems.Add(noneItem);
                selectedObdConfigName = "NO CONFIG";
                return;
            }
            foreach (var config in configs)
            {
                var item = new ToolStripMenuItem(config);
                item.Click += ObdConfigMenuItem_Click;
                obdConfigToolStripMenuItem.DropDownItems.Add(item);
            }
            // Default to first config
            selectedObdConfigName = configs[0];
            ((ToolStripMenuItem)obdConfigToolStripMenuItem.DropDownItems[0]).Checked = true;
        }

        private void ObdConfigMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ToolStripMenuItem item in obdConfigToolStripMenuItem.DropDownItems)
                item.Checked = false;
            var clicked = (ToolStripMenuItem)sender;
            clicked.Checked = true;
            selectedObdConfigName = clicked.Text ?? "NO CONFIG";
        }

        private void buttonTestRead_Click(object sender, EventArgs e)
        {
            try
            {
                liveData.Clear();
                loggerEnabled = true;
                var outfn = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\LotusECMLog{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                // Use selected OBD config
                if (string.IsNullOrWhiteSpace(selectedObdConfigName))
                {
                    MessageBox.Show("Please select an OBD configuration before starting the logger.", "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                OBDConfiguration config = OBDConfiguration.LoadFromConfig(selectedObdConfigName);
                logger = new J2534OBDLogger(outfn, Logger_DataLogged, Logger_ExceptionOccurred, config);
                logger.CodingDataUpdated += Logger_CodingDataUpdated;
                logger.Start();
                currentLogfileName.Text = outfn;

                // Update coding view after logger is started
                UpdateCodingView();
            }
            catch (J2534Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                loggerEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load OBD configuration: {ex.Message}", "Config Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                loggerEnabled = false;
            }
        }

        private void stopLogger_button_Click(object sender, EventArgs e)
        {
            logger?.Stop();
            loggerEnabled = false;
            currentLogfileName.Text = "No Log File";
        }

        private void Logger_DataLogged(List<LiveDataReading> data)
        {
            // Check if form is disposed or being disposed
            if (IsDisposed || Disposing)
                return;
                
            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => Logger_DataLogged(data)));
                }
                catch (ObjectDisposedException)
                {
                    // Form was disposed while trying to invoke - ignore
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Form handle was destroyed - ignore
                    return;
                }
                return;
            }
            
            // Double-check after invoke to handle race conditions
            if (IsDisposed || Disposing)
                return;
            
            foreach (var r in data)
            {
                liveData[r.name] = (float)r.value_f;
            }
            
            UpdateListView();

            DateTime now = DateTime.Now;
            float refreshRate = logger.LogFileToUIRatio*1000 / (float)(now - lastUpdateTime).TotalMilliseconds;
            refreshRateLabel.Text = $"Refresh Rate: {refreshRate:F1} hz";
            lastUpdateTime = now;
        }

        private void UpdateListView()
        {
            // create collection of listView items fom LiveData dictionary
            ListViewItem[] items = [.. liveData.Select(kvp => new ListViewItem([kvp.Key, kvp.Value.ToString("F2")]))];

            liveDataView.BeginUpdate();
            liveDataView.Items.Clear();
            liveDataView.Items.AddRange(items);
            liveDataView.EndUpdate();
        }

        private void Logger_ExceptionOccurred(Exception ex)
        {
            // Check if form is disposed or being disposed
            if (IsDisposed || Disposing)
                return;
                
            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => Logger_ExceptionOccurred(ex)));
                }
                catch (ObjectDisposedException)
                {
                    // Form was disposed while trying to invoke - ignore
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Form handle was destroyed - ignore
                    return;
                }
                return;
            }

            // Double-check after invoke to handle race conditions
            if (IsDisposed || Disposing)
                return;

            // Stop the logger and reset UI state
            logger?.Stop();
            loggerEnabled = false;
            currentLogfileName.Text = "No Log File";

            // Show error message to user
            string errorMessage = ex switch
            {
                J2534Exception j2534Ex => $"J2534 Interface Error: {j2534Ex.Message}",
                TimeoutException => "Communication timeout with ECM. Please check connections.",
                UnauthorizedAccessException => "Unable to access log file. Check file permissions.",
                IOException ioEx => $"File I/O Error: {ioEx.Message}",
                _ => $"Unexpected error: {ex.Message}"
            };

            MessageBox.Show(errorMessage, "Logger Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void aboutLotusECMLoggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var ab = new AboutBox1();
            ab.ShowDialog(this);
        }

        private void UpdateCodingView()
        {
            if (logger?.CodingDecoder == null)
                return;

            originalCodingDecoder = logger.CodingDecoder;
            modifiedCodingDecoder = logger.CodingDecoder;
            
            // Clear existing controls
            codingScrollPanel.Controls.Clear();
            codingControls.Clear();
            
            var optionNames = originalCodingDecoder.GetAvailableOptions();
            int yPosition = 10;
            
            foreach (var optionName in optionNames)
            {
                // Create label
                var label = new Label();
                label.Text = optionName + ":";
                label.Size = new Size(250, 23);
                label.Location = new Point(10, yPosition);
                label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                codingScrollPanel.Controls.Add(label);
                
                // Create control based on option type
                Control control;
                if (originalCodingDecoder.IsOptionNumeric(optionName))
                {
                    // Numeric input
                    var numericUpDown = new NumericUpDown();
                    numericUpDown.Size = new Size(100, 23);
                    numericUpDown.Location = new Point(270, yPosition);
                    numericUpDown.Minimum = 0;
                    numericUpDown.Maximum = 999; // Will be set properly based on bit mask
                    numericUpDown.Value = originalCodingDecoder.GetOptionRawValue(optionName);
                    numericUpDown.ValueChanged += (s, e) => OnCodingValueChanged(optionName, (int)numericUpDown.Value);
                    numericUpDown.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    control = numericUpDown;
                }
                else
                {
                    // Dropdown for predefined options
                    var comboBox = new ComboBox();
                    comboBox.Size = new Size(200, 23);
                    comboBox.Location = new Point(270, yPosition);
                    comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
                    var possibleValues = originalCodingDecoder.GetOptionPossibleValues(optionName);
                    comboBox.Items.AddRange(possibleValues);
                    comboBox.SelectedItem = originalCodingDecoder.GetOptionValue(optionName);
                    comboBox.SelectedIndexChanged += (s, e) => OnCodingValueChanged(optionName, comboBox.SelectedItem?.ToString());
                    comboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    control = comboBox;
                }
                
                codingScrollPanel.Controls.Add(control);
                codingControls[optionName] = control;
                
                yPosition += 35;
            }
            
            // Enable buttons
            saveCodingButton.Enabled = true;
            resetCodingButton.Enabled = true;
        }
        
        private void OnCodingValueChanged(string optionName, object value)
        {
            try
            {
                modifiedCodingDecoder = modifiedCodingDecoder.SetOptionValue(optionName, value);
                
                // Update button text to indicate changes
                bool hasChanges = !modifiedCodingDecoder.BitField.Equals(originalCodingDecoder.BitField);
                saveCodingButton.Text = hasChanges ? "Save Coding*" : "Save Coding";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid value for {optionName}: {ex.Message}", "Validation Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                
                // Reset control to original value
                ResetSingleControl(optionName);
            }
        }
        
        private void ResetSingleControl(string optionName)
        {
            if (!codingControls.TryGetValue(optionName, out var control))
                return;
                
            if (control is ComboBox comboBox)
            {
                comboBox.SelectedItem = originalCodingDecoder.GetOptionValue(optionName);
            }
            else if (control is NumericUpDown numericUpDown)
            {
                numericUpDown.Value = originalCodingDecoder.GetOptionRawValue(optionName);
            }
        }

        private void Logger_CodingDataUpdated(T6eCodingDecoder decoder)
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try
                {
                    Invoke(new Action(() => Logger_CodingDataUpdated(decoder)));
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                return;
            }

            UpdateCodingView();
        }
        
        private void SaveCodingButton_Click(object sender, EventArgs e)
        {
            if (modifiedCodingDecoder == null)
                return;
                
            var result = MessageBox.Show(
                "Are you sure you want to save the coding changes?\n\n" +
                "This will create a backup of the current coding and attempt to write the new coding to the ECU.\n\n" +
                "WARNING: Incorrect coding can cause vehicle malfunction!",
                "Save Coding Confirmation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
                
            if (result != DialogResult.Yes)
                return;
                
            try
            {
                // Create backup file
                var backupFileName = $"coding_backup_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var backupPath = Path.Combine(Directory.GetCurrentDirectory(), backupFileName);
                
                var backupContent = $"Coding Backup - {DateTime.Now}\n" +
                    $"Original Coding Data: {originalCodingDecoder.ToHexString()}\n" +
                    $"Original BitField: 0x{originalCodingDecoder.BitField:X16}\n\n" +
                    "Original Configuration:\n" +
                    originalCodingDecoder.ToString() + "\n\n" +
                    $"Modified Coding Data: {modifiedCodingDecoder.ToHexString()}\n" +
                    $"Modified BitField: 0x{modifiedCodingDecoder.BitField:X16}\n\n" +
                    "Modified Configuration:\n" +
                    modifiedCodingDecoder.ToString();
                
                File.WriteAllText(backupPath, backupContent);
                
                // Write coding data to ECU
                bool writeSuccess = false;
                string errorMessage = "";
                
                try
                {
                    if (logger?.IsConnected == true)
                    {
                        var (success, error) = logger.WriteCodingToECU(modifiedCodingDecoder);
                        writeSuccess = success;
                        if (!success && !string.IsNullOrEmpty(error))
                        {
                            errorMessage = error;
                        }
                    }
                    else
                    {
                        errorMessage = "Logger is not connected to ECU.";
                    }
                }
                catch (Exception writeEx)
                {
                    errorMessage = $"Exception during ECU write: {writeEx.Message}";
                }
                
                string message;
                if (writeSuccess)
                {
                    message = $"✓ Coding successfully written to ECU!\n\n" +
                        $"Backup saved to: {backupFileName}\n\n" +
                        $"Written to ECU:\n" +
                        $"High bytes: {BitConverter.ToString(modifiedCodingDecoder.CodingDataHigh).Replace("-", " ")}\n" +
                        $"Low bytes: {BitConverter.ToString(modifiedCodingDecoder.CodingDataLow).Replace("-", " ")}\n\n" +
                        "The ECU has been updated with the new coding configuration.";
                    
                    MessageBox.Show(message, "Coding Written Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    message = $"⚠ Failed to write coding to ECU\n\n" +
                        $"Error: {errorMessage}\n\n" +
                        $"Backup saved to: {backupFileName}\n\n" +
                        $"Attempted to write:\n" +
                        $"High bytes: {BitConverter.ToString(modifiedCodingDecoder.CodingDataHigh).Replace("-", " ")}\n" +
                        $"Low bytes: {BitConverter.ToString(modifiedCodingDecoder.CodingDataLow).Replace("-", " ")}\n\n" +
                        "Please check the connection and try again.";
                    
                    MessageBox.Show(message, "Coding Write Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                
                // Reset the modified state
                originalCodingDecoder = modifiedCodingDecoder;
                saveCodingButton.Text = "Save Coding";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving coding: {ex.Message}", "Save Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        private void ResetCodingButton_Click(object sender, EventArgs e)
        {
            if (originalCodingDecoder == null)
                return;
                
            var result = MessageBox.Show(
                "Are you sure you want to reset all coding changes?",
                "Reset Coding Confirmation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
                
            if (result != DialogResult.Yes)
                return;
                
            // Reset modified decoder to original
            modifiedCodingDecoder = originalCodingDecoder;
            
            // Reset all controls
            foreach (var optionName in originalCodingDecoder.GetAvailableOptions())
            {
                ResetSingleControl(optionName);
            }
            
            // Reset button text
            saveCodingButton.Text = "Save Coding";
        }
    }
}
