using System.Diagnostics;
using LotusECMLogger.Services.Broker;

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

        /// <summary>How the flasher names itself when the broker refuses someone else.</summary>
        internal const string LeaseHolder = "the T6 flasher";

        // The app's adapter broker. EFI_PROT opens the adapter itself, so the app keeps it closed while it runs.
        private readonly J2534Broker broker = J2534Broker.Shared;

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

        private async void CmdButton_Click(object? sender, EventArgs e)
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
                // Calibration CPT files are always under 32KB; larger files are not calibrations
                const int MaxCalibrationSize = 32 * 1024;
                long fileSize = new FileInfo(fileToFlash).Length;
                if (fileSize >= MaxCalibrationSize)
                {
                    MessageBox.Show(
                        $"The selected CPT file is {fileSize:N0} bytes, which exceeds the 32 KB limit for calibration files.\n\n" +
                        "This tool only supports calibration CPT files. The selected file may be a full flash image or another non-calibration type and cannot be flashed here.",
                        "Unsupported CPT File",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

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
                    bool conversionSuccess = CrpCreator.Create(
                        crpFilePath: tempCrpFile,
                        ecuType: CrpCreator.EcuType.T6,
                        calFilePath: fileToFlash);

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

            // TODO: EFI_PROT.EXE is an old 32-bit console program and almost certainly reads its arguments in the ANSI code
            // page, not Unicode. A CRP path with characters outside that code page (an accented user name, a localized
            // OneDrive folder) would reach it as '?' and fail. Investigate with such a path; if it fails, mitigate, e.g.
            // copy the CRP to an ASCII-only path before launching, or refuse such paths with an explanation.
            // EFI_PROT opens the J2534 adapter itself, so the app closes its own use of it first (DeviceReleased,
            // J2534-session-broker-plan.md §3.4) and keeps every tab off it until the flasher's window closes.
            // Refused while anything in the app holds the adapter, which the flasher could then not open.
            BrokerLease released;
            try
            {
                released = broker.Acquire(LeaseKind.DeviceReleased, LeaseHolder);
            }
            catch (BrokerBusyException ex)
            {
                MessageBox.Show($"{ex.Message} Stop it before flashing: the flash program needs the adapter to itself.",
                    "Adapter In Use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                statusLabel.Text = "Adapter in use";
                return;
            }

            try
            {
                Process? process;
                try
                {
                    process = StartFlasher(programPath, fileToFlash, workingDirectoryTextBox.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error launching the flash program: {ex.Message}", "Launch Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    statusLabel.Text = "Error occurred";
                    return;
                }
                if (process is null)
                {
                    statusLabel.Text = "Program launch failed";
                    return;
                }

                // One flasher at a time: two would fight over the J2534 device mid-flash.
                cmdButton.Enabled = false;
                statusLabel.Text = $"Flasher running in its own window (PID {process.Id}). Follow its prompts there.";
                statusLabel.ForeColor = SystemColors.ControlText;
                using (process)
                {
                    try { await process.WaitForExitAsync(); }
                    catch (Exception ex) { Debug.WriteLine($"T6EFlasher: waiting for the flasher failed: {ex.Message}"); }
                }
            }
            finally
            {
                // Even when this window was closed first: the adapter stays released for as long as EFI_PROT ran.
                released.Dispose();
            }
            if (IsDisposed) return;
            cmdButton.Enabled = true;
            statusLabel.Text = "Flasher window closed. Check its output to confirm the flash succeeded.";
        }

        /// <summary>
        /// Runs the flasher the way the vendor's Prog_T6_ECU_FIX.bat does (the program and the file, then PAUSE, so its
        /// window stays open for its final output), without writing a script: one cmd.exe with the paths passed as
        /// environment variables. cmd substitutes them in a single pass, so a '%' in a path is not expanded again, and
        /// the quotes around them keep '&amp;' and the like literal. /d skips AutoRun commands, /v:off keeps '!' literal,
        /// and /s strips only the outer quotes.
        /// </summary>
        private static Process? StartFlasher(string programPath, string fileToFlash, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                // A console child of a GUI app gets its own visible window.
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = "/d /v:off /s /c \"\"%EFI_PROGRAM%\" \"%EFI_FILE%\" & pause\"",
            };
            if (!string.IsNullOrWhiteSpace(workingDirectory)) startInfo.WorkingDirectory = workingDirectory;
            startInfo.Environment["EFI_PROGRAM"] = programPath;
            startInfo.Environment["EFI_FILE"] = fileToFlash;
            return Process.Start(startInfo);
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            Close();
        }
    }
}
