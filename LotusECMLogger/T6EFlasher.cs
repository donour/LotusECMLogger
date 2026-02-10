using System.Diagnostics;

namespace LotusECMLogger
{
    public partial class T6EFlasher : Form
    {
        private readonly TextBox programPathTextBox;
        private readonly TextBox inputFileTextBox;
        private readonly Button browseInputButton;
        private readonly TextBox workingDirectoryTextBox;
        private readonly Button browseProgramButton;
        private readonly Button browseWorkingDirButton;
        private readonly Button cmdButton;
        private readonly Button closeButton;
        private readonly Label statusLabel;

        public T6EFlasher()
        {
            // Create main table layout panel with margins
			var mainTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(20),
                ColumnCount = 3,
				RowCount = 6,
				AutoSize = false
            };

            // Configure column styles (Label : TextBox : Button)
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); // Labels column
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // TextBox column
            mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); // Button column

            // Configure row styles
			mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Program row
			mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Input file row
			mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Help row
			mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Working dir row
			mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Buttons row
			mainTable.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Status row

            // Initialize controls
            var programLabel = new Label
            {
                Text = "Program:",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 5, 0)
            };

            programPathTextBox = new TextBox
            {
                Text = "EFI_PROT.EXE",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 5, 5)
            };

            browseProgramButton = new Button
            {
                Text = "Browse...",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            browseProgramButton.Click += BrowseProgramButton_Click;

            var inputFileLabel = new Label
            {
                Text = "Input File:",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 5, 0)
            };

            inputFileTextBox = new TextBox
            {
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 5, 5)
            };

            browseInputButton = new Button
            {
                Text = "Browse...",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            browseInputButton.Click += BrowseInputButton_Click;

			var inputFileHelpLabel = new Label
            {
                Text = "Select a CRP or CPT file. CPT files will be automatically converted to CRP before flashing.",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleLeft,
				Dock = DockStyle.Fill,
				AutoSize = true,
				Margin = new Padding(0, 0, 5, 2)
            };

            var workingDirLabel = new Label
            {
                Text = "Working Dir:",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 5, 0)
            };

            workingDirectoryTextBox = new TextBox
            {
                Text = "C:\\Program Files (x86)\\T6_ECU_FIX",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 5, 5)
            };

            browseWorkingDirButton = new Button
            {
                Text = "Browse...",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 5, 0, 5)
            };
            browseWorkingDirButton.Click += BrowseWorkingDirButton_Click;

            // Button panel for Launch and Close buttons
			var buttonPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
				AutoSize = true,
                Margin = new Padding(0, 10, 0, 0)
            };

            cmdButton = new Button
            {
                Text = "Launch Program",
                Size = new Size(120, 35),
                Margin = new Padding(0, 0, 10, 0)
            };
            cmdButton.Click += CmdButton_Click;

            closeButton = new Button
            {
                Text = "Close",
                Size = new Size(80, 35)
            };
            closeButton.Click += CloseButton_Click;

            buttonPanel.Controls.Add(cmdButton);
            buttonPanel.Controls.Add(closeButton);

            // Status label
			statusLabel = new Label
            {
                Text = "Ready",
                TextAlign = ContentAlignment.MiddleLeft,
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 15, 0, 0),
                ForeColor = Color.DarkGreen
            };

            // Add controls to table (column, row)
            mainTable.Controls.Add(programLabel, 0, 0);
            mainTable.Controls.Add(programPathTextBox, 1, 0);
            mainTable.Controls.Add(browseProgramButton, 2, 0);

            mainTable.Controls.Add(inputFileLabel, 0, 1);
            mainTable.Controls.Add(inputFileTextBox, 1, 1);
            mainTable.Controls.Add(browseInputButton, 2, 1);

			// Add help label aligned with textboxes (span columns 1-2)
			mainTable.SetColumnSpan(inputFileHelpLabel, 2);
			mainTable.Controls.Add(inputFileHelpLabel, 1, 2);

            mainTable.Controls.Add(workingDirLabel, 0, 3);
            mainTable.Controls.Add(workingDirectoryTextBox, 1, 3);
            mainTable.Controls.Add(browseWorkingDirButton, 2, 3);

            // Span button panel across columns
            mainTable.SetColumnSpan(buttonPanel, 3);
            mainTable.Controls.Add(buttonPanel, 0, 4);

            // Span status label across columns
            mainTable.SetColumnSpan(statusLabel, 3);
            mainTable.Controls.Add(statusLabel, 0, 5);

            // Add the main table to the form
            Controls.Add(mainTable);

            // Form properties
            Text = "T6E Calibration Flasher";
            Size = new Size(650, 350);
            MinimumSize = new Size(650, 350);
            StartPosition = FormStartPosition.CenterParent;
			MaximizeBox = true;
            Padding = new Padding(10);
        }

        private void BrowseProgramButton_Click(object? sender, EventArgs e)
        {
            using var openFileDialog = new OpenFileDialog
            {
                Title = "Select CLI Program",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                programPathTextBox.Text = openFileDialog.FileName;
            }
        }

        private void BrowseInputButton_Click(object? sender, EventArgs e)
        {
            using var openFileDialog = new OpenFileDialog
            {
                Title = "Select Input File",
                Filter = "Calibration files (*.crp;*.cpt)|*.crp;*.cpt|CRP files (*.crp)|*.crp|CPT files (*.cpt)|*.cpt|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                inputFileTextBox.Text = openFileDialog.FileName;
            }
        }


        private void BrowseWorkingDirButton_Click(object? sender, EventArgs e)
        {
            using var folderBrowserDialog = new FolderBrowserDialog
            {
                Description = "Select Working Directory",
                ShowNewFolderButton = true
            };

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                workingDirectoryTextBox.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void CmdButton_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(programPathTextBox.Text))
            {
                MessageBox.Show("Please select a program to run.", "No Program Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Validate input file if specified
            if (string.IsNullOrWhiteSpace(inputFileTextBox.Text))
            {
                MessageBox.Show("Missing Input File", "Invalid Input File",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!File.Exists(inputFileTextBox.Text))
            {
                MessageBox.Show("The specified input file does not exist.", "Invalid Input File",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Validate working directory if specified
            if (!string.IsNullOrWhiteSpace(workingDirectoryTextBox.Text))
            {
                if (!Directory.Exists(workingDirectoryTextBox.Text))
                {
                    MessageBox.Show("The specified working directory does not exist.", "Invalid Working Directory",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            // Check if program exists (either as full path or in working directory)
            string programPath = programPathTextBox.Text;
            bool programExists = File.Exists(programPath);

            if (!programExists && !string.IsNullOrWhiteSpace(workingDirectoryTextBox.Text))
            {
                // Try to find the program in the working directory
                string workingDirProgramPath = Path.Combine(workingDirectoryTextBox.Text, programPath);
                if (File.Exists(workingDirProgramPath))
                {
                    programPath = workingDirProgramPath;
                    programExists = true;
                }
            }

            if (!programExists && !Path.IsPathRooted(programPath))
            {
                // If it's not a full path and not found in working directory,
                // let the shell try to find it in PATH (don't validate existence)
                programExists = true;
            }

            if (!programExists)
            {
                MessageBox.Show("The selected program does not exist.", "Program Not Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Handle CPT to CRP conversion if needed
            string fileToFlash = inputFileTextBox.Text;
            string? tempCrpFile = null;

            if (Path.GetExtension(fileToFlash).Equals(".cpt", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    statusLabel.Text = "Converting CPT to CRP...";
                    statusLabel.ForeColor = Color.Blue;
                    Application.DoEvents();

                    // Generate CRP filename in the same directory as the CPT file
                    string cptDirectory = Path.GetDirectoryName(fileToFlash) ?? Path.GetTempPath();
                    string cptFileName = Path.GetFileNameWithoutExtension(fileToFlash);
                    tempCrpFile = Path.Combine(cptDirectory, $"{cptFileName}_converted.crp");

                    // Convert CPT to CRP
                    bool conversionSuccess = CptToCrpConverter.Convert(fileToFlash, tempCrpFile);

                    if (!conversionSuccess)
                    {
                        MessageBox.Show("Failed to convert CPT to CRP format. Check console for details.",
                            "Conversion Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        statusLabel.Text = "Conversion failed";
                        statusLabel.ForeColor = Color.Red;
                        return;
                    }

                    fileToFlash = tempCrpFile;
                    statusLabel.Text = $"Converted to: {Path.GetFileName(tempCrpFile)}";
                    statusLabel.ForeColor = Color.Green;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during CPT to CRP conversion: {ex.Message}",
                        "Conversion Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "Conversion error";
                    statusLabel.ForeColor = Color.Red;
                    return;
                }
            }

            try
            {
                // Create a batch file to execute the command with proper argument handling
                string batchFilePath = Path.Combine(Path.GetTempPath(), $"T6EFlasher_{Guid.NewGuid()}.bat");

                using (var writer = new StreamWriter(batchFilePath))
                {
                    writer.WriteLine($"@echo on");
                    writer.WriteLine($"cd /d \"{workingDirectoryTextBox.Text}\"");
                    if (!string.IsNullOrEmpty(fileToFlash))
                    {
                        writer.WriteLine($"\"{programPath}\" \"{fileToFlash}\"");
                    }
                    else
                    {
                        writer.WriteLine($"\"{programPath}\"");
                    }
                    writer.WriteLine($"pause");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = batchFilePath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                string displayArgs = !string.IsNullOrEmpty(fileToFlash) ? $"\"{fileToFlash}\"" : "";
                statusLabel.Text = $"Launching: \"{programPath}\" {displayArgs}".Trim();

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    statusLabel.Text = $"Program launched successfully (PID: {process.Id})";

                    // Clean up the batch file after a delay
                    Task.Delay(1000).ContinueWith(_ =>
                    {
                        try { File.Delete(batchFilePath); }
                        catch { /* Ignore cleanup errors */ }
                    });
                }
                else
                {
                    statusLabel.Text = "Program launch failed";
                    try { File.Delete(batchFilePath); }
                    catch { /* Ignore cleanup errors */ }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching program via CMD: {ex.Message}", "Launch Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusLabel.Text = "Error occurred";
            }
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            Close();
        }
    }
}
