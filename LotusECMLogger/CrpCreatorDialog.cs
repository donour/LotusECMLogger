namespace LotusECMLogger
{
    /// <summary>
    /// Modal dialog for building a .CRP flash container from a calibration (calrom)
    /// file and/or a firmware (prog) file via <see cref="CrpCreator"/>. The user picks
    /// the target ECU type, selects one or both input files, and saves the resulting CRP.
    /// Each input is associated with the ECU's calrom/prog reference address automatically.
    /// This tool only reads local files and writes a CRP; it never talks to the ECU.
    /// </summary>
    public sealed class CrpCreatorDialog : Form
    {
        private readonly ComboBox ecuTypeCombo;
        private readonly TextBox calPathTextBox;
        private readonly TextBox progPathTextBox;
        private readonly Label calAddressLabel;
        private readonly Label progAddressLabel;
        private readonly Button createButton;

        private string? calFilePath;
        private string? progFilePath;

        public CrpCreatorDialog()
        {
            Text = "Create CRP File";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(600, 280);

            var ecuTypeLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 20),
                Text = "ECU type:"
            };

            ecuTypeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(96, 16),
                Size = new Size(200, 23)
            };
            ecuTypeCombo.SelectedIndexChanged += ecuTypeCombo_SelectedIndexChanged;

            // Calibration (calrom) row
            var calLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 68),
                Text = "Calibration (.CPT):"
            };

            calPathTextBox = new TextBox
            {
                ReadOnly = true,
                Location = new Point(16, 90),
                Size = new Size(420, 23),
                BackColor = SystemColors.Window
            };

            var calBrowseButton = new Button
            {
                Text = "Browse…",
                Location = new Point(444, 88),
                Size = new Size(90, 27)
            };
            calBrowseButton.Click += (_, _) => BrowseFor(isCalibration: true);

            var calClearButton = new Button
            {
                Text = "✕",
                Location = new Point(540, 88),
                Size = new Size(30, 27)
            };
            calClearButton.Click += (_, _) => ClearFile(isCalibration: true);

            calAddressLabel = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(16, 118),
                Text = string.Empty
            };

            // Firmware (prog) row
            var progLabel = new Label
            {
                AutoSize = true,
                Location = new Point(16, 152),
                Text = "Firmware (.BIN):"
            };

            progPathTextBox = new TextBox
            {
                ReadOnly = true,
                Location = new Point(16, 174),
                Size = new Size(420, 23),
                BackColor = SystemColors.Window
            };

            var progBrowseButton = new Button
            {
                Text = "Browse…",
                Location = new Point(444, 172),
                Size = new Size(90, 27)
            };
            progBrowseButton.Click += (_, _) => BrowseFor(isCalibration: false);

            var progClearButton = new Button
            {
                Text = "✕",
                Location = new Point(540, 172),
                Size = new Size(30, 27)
            };
            progClearButton.Click += (_, _) => ClearFile(isCalibration: false);

            progAddressLabel = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(16, 202),
                Text = string.Empty
            };

            createButton = new Button
            {
                Text = "Create CRP…",
                Enabled = false,
                Location = new Point(374, 236),
                Size = new Size(120, 32)
            };
            createButton.Click += createButton_Click;

            var closeButton = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Location = new Point(502, 236),
                Size = new Size(88, 32)
            };

            Controls.Add(ecuTypeLabel);
            Controls.Add(ecuTypeCombo);
            Controls.Add(calLabel);
            Controls.Add(calPathTextBox);
            Controls.Add(calBrowseButton);
            Controls.Add(calClearButton);
            Controls.Add(calAddressLabel);
            Controls.Add(progLabel);
            Controls.Add(progPathTextBox);
            Controls.Add(progBrowseButton);
            Controls.Add(progClearButton);
            Controls.Add(progAddressLabel);
            Controls.Add(createButton);
            Controls.Add(closeButton);
            CancelButton = closeButton;

            PopulateEcuTypes();
        }

        private void PopulateEcuTypes()
        {
            foreach (CrpCreator.CrpVariant variant in CrpCreator.AllVariants)
            {
                ecuTypeCombo.Items.Add(new EcuTypeItem(variant));
            }

            // Default to T6.
            for (int i = 0; i < ecuTypeCombo.Items.Count; i++)
            {
                if (((EcuTypeItem)ecuTypeCombo.Items[i]!).Variant.Type == CrpCreator.EcuType.T6)
                {
                    ecuTypeCombo.SelectedIndex = i;
                    return;
                }
            }
            ecuTypeCombo.SelectedIndex = 0;
        }

        private CrpCreator.CrpVariant SelectedVariant =>
            ((EcuTypeItem)ecuTypeCombo.SelectedItem!).Variant;

        private void ecuTypeCombo_SelectedIndexChanged(object? sender, EventArgs e)
        {
            UpdateAddressLabels();
        }

        private void BrowseFor(bool isCalibration)
        {
            using var dialog = new OpenFileDialog
            {
                Title = isCalibration ? "Select Calibration File" : "Select Firmware File",
                Filter = isCalibration
                    ? "Calibration files (*.cpt)|*.cpt|Binary files (*.bin)|*.bin|All files (*.*)|*.*"
                    : "Binary files (*.bin)|*.bin|Calibration files (*.cpt)|*.cpt|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            if (isCalibration)
            {
                calFilePath = dialog.FileName;
                calPathTextBox.Text = dialog.FileName;
            }
            else
            {
                progFilePath = dialog.FileName;
                progPathTextBox.Text = dialog.FileName;
            }

            UpdateAddressLabels();
            UpdateCreateButtonState();
        }

        private void ClearFile(bool isCalibration)
        {
            if (isCalibration)
            {
                calFilePath = null;
                calPathTextBox.Text = string.Empty;
            }
            else
            {
                progFilePath = null;
                progPathTextBox.Text = string.Empty;
            }

            UpdateAddressLabels();
            UpdateCreateButtonState();
        }

        private void UpdateAddressLabels()
        {
            if (ecuTypeCombo.SelectedItem is null)
                return;

            CrpCreator.CrpVariant variant = SelectedVariant;
            calAddressLabel.Text = calFilePath != null
                ? $"→ calrom address 0x{variant.CalAddress:X}, ECU id \"{variant.EcuId}\""
                : "Optional. Packed at the ECU's calrom address.";
            progAddressLabel.Text = progFilePath != null
                ? $"→ prog address 0x{variant.ProgAddress:X}, ECU id \"{variant.EcuId}\""
                : "Optional. Packed at the ECU's prog address.";
        }

        private void UpdateCreateButtonState()
        {
            createButton.Enabled = calFilePath != null || progFilePath != null;
        }

        private void createButton_Click(object? sender, EventArgs e)
        {
            if (calFilePath == null && progFilePath == null)
            {
                MessageBox.Show(
                    "Select a calibration file, a firmware file, or both.",
                    "Nothing to Pack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CrpCreator.CrpVariant variant = SelectedVariant;

            using var saveDialog = new SaveFileDialog
            {
                Title = "Save CRP File",
                Filter = "CRP files (*.crp)|*.crp|All files (*.*)|*.*",
                DefaultExt = "crp",
                AddExtension = true,
                FileName = SuggestOutputName()
            };
            if (saveDialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                bool success = CrpCreator.Create(saveDialog.FileName, variant.Type, calFilePath, progFilePath);
                if (success)
                {
                    MessageBox.Show(
                        $"Created CRP for {variant.DisplayName}:\r\n{saveDialog.FileName}",
                        "CRP Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        "CRP creation failed. See the log for details.",
                        "Create Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to create CRP:\r\n\r\n{ex.Message}",
                    "Create Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Suggests an output filename from whichever input is present.
        private string SuggestOutputName()
        {
            string? source = calFilePath ?? progFilePath;
            return source != null
                ? Path.GetFileNameWithoutExtension(source) + ".crp"
                : "output.crp";
        }

        /// <summary>ComboBox wrapper that shows the variant's display name.</summary>
        private sealed class EcuTypeItem
        {
            public CrpCreator.CrpVariant Variant { get; }

            public EcuTypeItem(CrpCreator.CrpVariant variant) => Variant = variant;

            public override string ToString() => Variant.DisplayName;
        }
    }
}
