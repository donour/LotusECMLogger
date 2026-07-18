using System.Text;

namespace LotusECMLogger
{
    /// <summary>
    /// Modal dialog for inspecting a T6 .CRP flash container. The user opens a CRP file,
    /// which is decrypted via <see cref="CrpUnpacker"/> and its chunk metadata shown as text.
    /// The decrypted firmware/calibration payloads can then be extracted to .bin files.
    /// This is a read-only inspection tool; it never talks to the ECU.
    /// </summary>
    public sealed class CrpUnpackerDialog : Form
    {
        private readonly Label filePathLabel;
        private readonly TextBox contentsTextBox;
        private readonly Button extractButton;

        private string? loadedFilePath;
        private CrpUnpacker.CrpContents? loadedContents;

        public CrpUnpackerDialog()
        {
            Text = "Unpack CRP File";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(520, 360);

            var openButton = new Button
            {
                Text = "Open CRP…",
                Location = new Point(12, 12),
                Size = new Size(110, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            openButton.Click += openButton_Click;

            filePathLabel = new Label
            {
                AutoEllipsis = true,
                Location = new Point(134, 19),
                Size = new Size(574, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = "No file loaded."
            };

            contentsTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9F),
                Location = new Point(12, 56),
                Size = new Size(696, 416),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = SystemColors.Window
            };

            extractButton = new Button
            {
                Text = "Extract .bin…",
                Enabled = false,
                Location = new Point(494, 480),
                Size = new Size(120, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            extractButton.Click += extractButton_Click;

            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Location = new Point(620, 480),
                Size = new Size(88, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            Controls.Add(openButton);
            Controls.Add(filePathLabel);
            Controls.Add(contentsTextBox);
            Controls.Add(extractButton);
            Controls.Add(closeButton);
            CancelButton = closeButton;
        }

        private void openButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Open CRP File",
                Filter = "CRP files (*.crp)|*.crp|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                loadedContents = CrpUnpacker.Unpack(dialog.FileName);
                loadedFilePath = dialog.FileName;
                filePathLabel.Text = dialog.FileName;
                contentsTextBox.Text = FormatContents(dialog.FileName, loadedContents);
                extractButton.Enabled = loadedContents.Chunks.Count > 0;
            }
            catch (Exception ex)
            {
                loadedContents = null;
                loadedFilePath = null;
                extractButton.Enabled = false;
                filePathLabel.Text = dialog.FileName;
                contentsTextBox.Text = string.Empty;
                MessageBox.Show(
                    $"Failed to unpack CRP file:\r\n\r\n{ex.Message}",
                    "Unpack Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void extractButton_Click(object? sender, EventArgs e)
        {
            if (loadedFilePath is null || loadedContents is null)
                return;

            using var dialog = new FolderBrowserDialog
            {
                Description = "Select a folder to extract the decrypted .bin payloads into",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                CrpUnpacker.Extract(loadedFilePath, dialog.SelectedPath);
                MessageBox.Show(
                    $"Extracted {loadedContents.Chunks.Count} payload(s) to:\r\n{dialog.SelectedPath}",
                    "Extract Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to extract payloads:\r\n\r\n{ex.Message}",
                    "Extract Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Builds the human-readable summary shown in the text box.
        /// </summary>
        private static string FormatContents(string filePath, CrpUnpacker.CrpContents contents)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"File   : {Path.GetFileName(filePath)}");
            sb.AppendLine($"Chunks : {contents.ChunkCount} (1 TOC + {contents.Chunks.Count} data)");
            sb.AppendLine();

            int index = 1;
            foreach (CrpUnpacker.CrpChunk chunk in contents.Chunks)
            {
                sb.AppendLine($"[Chunk {index}] {chunk.Name}");
                sb.AppendLine($"  Description : {chunk.Description}");
                sb.AppendLine($"  ECU Id      : {chunk.EcuId}");

                string addr = $"0x{chunk.EcuAddress:X}";
                if (chunk.RealAddress is uint real)
                    addr += $" (real 0x{real:X6})";
                sb.AppendLine($"  Address     : {addr}");

                sb.AppendLine($"  Data size   : {chunk.Data.Length} bytes (0x{chunk.Data.Length:X})");
                sb.AppendLine($"  Version     : min {chunk.MinVersion}, max {chunk.MaxVersion}");
                sb.AppendLine($"  CAN bitrate : {chunk.CanBitrate} kbit/s");
                sb.AppendLine(
                    $"  CAN IDs     : remote 0x{chunk.CanRemoteId1:X}/0x{chunk.CanRemoteId2:X}, " +
                    $"local 0x{chunk.CanLocalId1:X}/0x{chunk.CanLocalId2:X}");
                sb.AppendLine($"  XTEA salt   : {BitConverter.ToString(chunk.XteaSalt).Replace('-', ' ')}");
                sb.AppendLine();
                index++;
            }

            return sb.ToString();
        }
    }
}
