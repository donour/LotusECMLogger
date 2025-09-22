using System.Diagnostics;

namespace LotusECMLogger
{
    public partial class CLIRunnerForm : Form
    {
        private readonly TextBox programPathTextBox;
        private readonly TextBox argumentsTextBox;
        private readonly TextBox inputFileTextBox;
        private readonly TextBox workingDirectoryTextBox;
        private readonly Button browseProgramButton;
        private readonly Button browseInputButton;
        private readonly Button browseWorkingDirButton;
        private readonly Button cmdButton;
        private readonly Button closeButton;
        private readonly Label statusLabel;

        public CLIRunnerForm()
        {

            // Initialize controls
            programPathTextBox = new TextBox
            {
                Location = new Point(120, 20),
                Size = new Size(350, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,             
                Text = "EFI_PROT.EXE"
            };

            argumentsTextBox = new TextBox
            {
                Location = new Point(120, 55),
                Size = new Size(350, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                PlaceholderText = "Additional command line arguments (optional)"
            };

            inputFileTextBox = new TextBox
            {
                Location = new Point(120, 90),
                Size = new Size(350, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true
            };

            workingDirectoryTextBox = new TextBox
            {
                Location = new Point(120, 125),
                Size = new Size(350, 23),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "C:\\Program Files (x86)\\T6_ECU_FIX"
            };

            browseProgramButton = new Button
            {
                Text = "Browse...",
                Location = new Point(480, 18),
                Size = new Size(80, 27),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            browseProgramButton.Click += BrowseProgramButton_Click;

            browseInputButton = new Button
            {
                Text = "Browse...",
                Location = new Point(480, 88),
                Size = new Size(80, 27),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            browseInputButton.Click += BrowseInputButton_Click;

            browseWorkingDirButton = new Button
            {
                Text = "Browse...",
                Location = new Point(480, 123),
                Size = new Size(80, 27),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            browseWorkingDirButton.Click += BrowseWorkingDirButton_Click;

            cmdButton = new Button
            {
                Text = "Launch Program",
                Location = new Point(15, 165),
                Size = new Size(120, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            cmdButton.Click += CmdButton_Click;

            closeButton = new Button
            {
                Text = "Close",
                Location = new Point(145, 165),
                Size = new Size(80, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            closeButton.Click += CloseButton_Click;

            statusLabel = new Label
            {
                Text = "Ready",
                Location = new Point(15, 190),
                Size = new Size(545, 20),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            // Labels
            var programLabel = new Label
            {
                Text = "Program:",
                Location = new Point(15, 23),
                AutoSize = true
            };

            var argsLabel = new Label
            {
                Text = "Arguments:",
                Location = new Point(15, 58),
                AutoSize = true
            };

            var inputLabel = new Label
            {
                Text = "Input File:",
                Location = new Point(15, 93),
                AutoSize = true
            };

            var workingDirLabel = new Label
            {
                Text = "Working Dir:",
                Location = new Point(15, 128),
                AutoSize = true
            };

            // Add controls to form
            Controls.AddRange(new Control[] {
                programLabel, programPathTextBox, browseProgramButton,
                argsLabel, argumentsTextBox,
                inputLabel, inputFileTextBox, browseInputButton,
                workingDirLabel, workingDirectoryTextBox, browseWorkingDirButton,
                cmdButton, closeButton, statusLabel
            });

            // Form properties
            Text = "CLI Runner";
            Size = new Size(580, 250);
            MinimumSize = new Size(580, 250);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
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
                Filter = "All files (*.*)|*.*",
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

            try
            {
                // Build the command line for cmd.exe
                string cmdArgs = $"/K \"{programPath}\"";

                // Add arguments if any
                var args = argumentsTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(inputFileTextBox.Text))
                {
                    if (!string.IsNullOrEmpty(args))
                        args += " ";
                    args += $"\"{inputFileTextBox.Text}\"";
                }

                if (!string.IsNullOrEmpty(args))
                {
                    cmdArgs = $"/K \"{programPath}\" {args}";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true, // Use shell execute to show command prompt
                    CreateNoWindow = false, // Show the command prompt window
                    WindowStyle = ProcessWindowStyle.Normal
                };

                // Set working directory if specified
                if (!string.IsNullOrWhiteSpace(workingDirectoryTextBox.Text))
                {
                    startInfo.WorkingDirectory = workingDirectoryTextBox.Text;
                }

                statusLabel.Text = "Launching program via CMD...";

                var process = Process.Start(startInfo);
                if (process != null)
                {
                    statusLabel.Text = $"Program launched via CMD (PID: {process.Id})";
                }
                else
                {
                    statusLabel.Text = "Program launch via CMD returned null process";
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
