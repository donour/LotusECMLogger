using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
	public partial class EcuCodingControl : UserControl
	{
		private readonly IEcuCodingService service;

		private T6eCodingDecoder? originalCodingDecoder;
		private T6eCodingDecoder? modifiedCodingDecoder;
		private readonly Dictionary<string, Control> codingControls = new Dictionary<string, Control>();
		private readonly Dictionary<string, Label> codingLabels = new Dictionary<string, Label>();
		private readonly Dictionary<string, Label> codingValidationLabels = new Dictionary<string, Label>();
		private readonly ToolTip validationToolTip;

		private bool isLoggerActive;
		public bool IsLoggerActive
		{
			get => isLoggerActive;
			set
			{
				isLoggerActive = value;
				readCodesButton.Enabled = !isLoggerActive;
				writeCodesButton.Enabled = !isLoggerActive && modifiedCodingDecoder != null;
			}
		}

		public EcuCodingControl(IEcuCodingService service)
		{
			this.service = service;
			InitializeComponent();
			components ??= new System.ComponentModel.Container();
			validationToolTip = new ToolTip(components);

			Dock = DockStyle.Fill;

			GuiIcons.ApplyToButton(readCodesButton,   GuiIcons.Read);
			GuiIcons.ApplyToButton(writeCodesButton,  GuiIcons.Write);
			GuiIcons.ApplyToButton(saveCodingButton,  GuiIcons.Save);
			GuiIcons.ApplyToButton(resetCodingButton, GuiIcons.Refresh);
		}

		private void readCodesButton_Click(object? sender, EventArgs e)
		{
			if (IsLoggerActive)
			{
				MessageBox.Show("Cannot read codes while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			try
			{
				readCodesButton.Enabled = false;
				readCodesButton.Text = "Reading...";

				LoadCodingDecoder(service.ReadCoding());
				MessageBox.Show("Coding data successfully read from ECU!", "Read Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (InvalidEcuCodingDataException ex)
			{
				if (ShowInvalidCodingDataDialog(ex) == InvalidCodingLoadChoice.LoadAnyway)
				{
					LoadCodingDecoder(new T6eCodingDecoder(ex.CodingDataLow, ex.CodingDataHigh, validate: false));
				}
			}
			catch (System.Exception ex)
			{
				MessageBox.Show($"Failed to read coding data: {ex.Message}", "Read Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				readCodesButton.Enabled = !IsLoggerActive;
				readCodesButton.Text = "Read Codes";
			}
		}

		private void LoadCodingDecoder(T6eCodingDecoder decoder)
		{
			originalCodingDecoder = decoder;
			modifiedCodingDecoder = originalCodingDecoder;

			UpdateCodingView();
			writeCodesButton.Enabled = !IsLoggerActive;
			UpdateBitFieldLabel();
		}

		private enum InvalidCodingLoadChoice
		{
			Cancel,
			LoadAnyway
		}

		private InvalidCodingLoadChoice ShowInvalidCodingDataDialog(InvalidEcuCodingDataException exception)
		{
			using var dialog = new Form
			{
				Text = "Invalid ECU Coding Data",
				FormBorderStyle = FormBorderStyle.FixedDialog,
				StartPosition = FormStartPosition.CenterParent,
				MinimizeBox = false,
				MaximizeBox = false,
				ShowInTaskbar = false,
				ClientSize = new Size(520, 190)
			};

			var messageLabel = new Label
			{
				AutoSize = false,
				Location = new Point(16, 16),
				Size = new Size(488, 106),
				Text =
					"The ECU coding data is invalid and may not match a supported configuration.\r\n\r\n" +
					$"{exception.InnerException?.Message ?? exception.Message}\r\n\r\n" +
					"You can cancel, or load the raw coding data anyway to correct it before writing changes."
			};

			var cancelButton = new Button
			{
				Text = "Cancel",
				DialogResult = DialogResult.Cancel,
				Size = new Size(100, 28),
				Location = new Point(298, 146)
			};

			var loadAnywayButton = new Button
			{
				Text = "Load anyway",
				DialogResult = DialogResult.OK,
				Size = new Size(106, 28),
				Location = new Point(404, 146)
			};

			dialog.Controls.Add(messageLabel);
			dialog.Controls.Add(cancelButton);
			dialog.Controls.Add(loadAnywayButton);
			dialog.CancelButton = cancelButton;

			return dialog.ShowDialog(FindForm()) == DialogResult.OK
				? InvalidCodingLoadChoice.LoadAnyway
				: InvalidCodingLoadChoice.Cancel;
		}

		private void writeCodesButton_Click(object? sender, EventArgs e)
		{
			if (IsLoggerActive)
			{
				MessageBox.Show("Cannot write codes while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			if (modifiedCodingDecoder == null)
			{
				MessageBox.Show("No coding data loaded. Please read codes first.", "No Data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			var result = MessageBox.Show(
				"Are you sure you want to write the coding changes to the ECU?\n\n" +
				"This will create a backup of the current coding and write the new coding to the ECU.\n\n" +
				"WARNING: Incorrect coding can cause vehicle malfunction!",
				"Write Coding Confirmation",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Warning);

			if (result != DialogResult.Yes)
				return;

			try
			{
				writeCodesButton.Enabled = false;
				writeCodesButton.Text = "Writing...";

				var backupFileName = $"coding_backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt";
				var backupPath = Path.Combine(Directory.GetCurrentDirectory(), backupFileName);

				var backupContent = $"Coding Backup - {System.DateTime.Now}\n" +
					$"Original Coding Data: {originalCodingDecoder!.ToHexString()}\n" +
					$"Original BitField: 0x{originalCodingDecoder.BitField:X16}\n\n" +
					"Original Configuration:\n" +
					originalCodingDecoder.ToString() + "\n\n" +
					$"Modified Coding Data: {modifiedCodingDecoder.ToHexString()}\n" +
					$"Modified BitField: 0x{modifiedCodingDecoder.BitField:X16}\n\n" +
					"Modified Configuration:\n" +
					modifiedCodingDecoder.ToString();

				File.WriteAllText(backupPath, backupContent);

				var (success, errorMessage) = service.WriteCoding(modifiedCodingDecoder);

				if (success)
				{
					var message = $"\u2713 Coding successfully written to ECU!\n\n" +
						$"Backup saved to: {backupFileName}\n\n" +
						"Written to ECU:\n" +
						$"High bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataHigh).Replace("-", " ")}\n" +
						$"Low bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataLow).Replace("-", " ")}\n\n" +
						"The ECU has been updated with the new coding configuration.";

					MessageBox.Show(message, "Coding Written Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);

					originalCodingDecoder = modifiedCodingDecoder;
					saveCodingButton.Text = "Save Changes";
					UpdateBitFieldLabel();
				}
				else
				{
					var message = $"\u26A0 Failed to write coding to ECU\n\n" +
						$"Error: {errorMessage}\n\n" +
						$"Backup saved to: {backupFileName}\n\n" +
						"Attempted to write:\n" +
						$"High bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataHigh).Replace("-", " ")}\n" +
						$"Low bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataLow).Replace("-", " ")}\n\n" +
						"Please check the connection and try again.";

					MessageBox.Show(message, "Coding Write Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
			catch (System.Exception ex)
			{
				MessageBox.Show($"Error writing coding: {ex.Message}", "Write Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				writeCodesButton.Enabled = !IsLoggerActive;
				writeCodesButton.Text = "Write Codes";
			}
		}

		private void saveCodingButton_Click(object? sender, EventArgs e)
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
				var backupFileName = $"coding_backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt";
				var backupPath = Path.Combine(Directory.GetCurrentDirectory(), backupFileName);

				var backupContent = $"Coding Backup - {System.DateTime.Now}\n" +
					$"Original Coding Data: {originalCodingDecoder!.ToHexString()}\n" +
					$"Original BitField: 0x{originalCodingDecoder.BitField:X16}\n\n" +
					"Original Configuration:\n" +
					originalCodingDecoder.ToString() + "\n\n" +
					$"Modified Coding Data: {modifiedCodingDecoder.ToHexString()}\n" +
					$"Modified BitField: 0x{modifiedCodingDecoder.BitField:X16}\n\n" +
					"Modified Configuration:\n" +
					modifiedCodingDecoder.ToString();

				File.WriteAllText(backupPath, backupContent);

				bool writeSuccess = false;
				string errorMessage = "";

				try
				{
					var (success, error) = service.WriteCoding(modifiedCodingDecoder);
					writeSuccess = success;
					if (!success && !string.IsNullOrEmpty(error))
						errorMessage = error;
				}
				catch (System.Exception writeEx)
				{
					errorMessage = $"Exception during ECU write: {writeEx.Message}";
				}

				string message;
				if (writeSuccess)
				{
					message = $"\u2713 Coding successfully written to ECU!\n\n" +
						$"Backup saved to: {backupFileName}\n\n" +
						"Written to ECU:\n" +
						$"High bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataHigh).Replace("-", " ")}\n" +
						$"Low bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataLow).Replace("-", " ")}\n\n" +
						"The ECU has been updated with the new coding configuration.";

					MessageBox.Show(message, "Coding Written Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
				{
					message = $"\u26A0 Failed to write coding to ECU\n\n" +
						$"Error: {errorMessage}\n\n" +
						$"Backup saved to: {backupFileName}\n\n" +
						"Attempted to write:\n" +
						$"High bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataHigh).Replace("-", " ")}\n" +
						$"Low bytes: {System.BitConverter.ToString(modifiedCodingDecoder.CodingDataLow).Replace("-", " ")}\n\n" +
						"Please check the connection and try again.";

					MessageBox.Show(message, "Coding Write Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}

				originalCodingDecoder = modifiedCodingDecoder;
				saveCodingButton.Text = "Save Changes";
				UpdateBitFieldLabel();
			}
			catch (System.Exception ex)
			{
				MessageBox.Show($"Error saving coding: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void resetCodingButton_Click(object? sender, EventArgs e)
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

			modifiedCodingDecoder = originalCodingDecoder;
			foreach (var optionName in originalCodingDecoder.GetAvailableOptions())
				ResetSingleControl(optionName);

			saveCodingButton.Text = "Save Changes";
			ApplyValidationHighlights();
			UpdateBitFieldLabel();
		}

		private void UpdateCodingView()
		{
			if (originalCodingDecoder == null)
				return;

			codingScrollPanel.Controls.Clear();
			codingControls.Clear();
			codingLabels.Clear();
			codingValidationLabels.Clear();

			var optionNames = originalCodingDecoder.GetAvailableOptions();
			int yPosition = 10;

			foreach (var optionName in optionNames)
			{
				Label label = new()
				{
					Text = optionName + ":",
					Size = new Size(250, 23),
					Location = new Point(10, yPosition),
					Anchor = AnchorStyles.Top | AnchorStyles.Left
				};
				codingScrollPanel.Controls.Add(label);
				codingLabels[optionName] = label;

				Control control;
				if (originalCodingDecoder.IsOptionNumeric(optionName))
				{
					var numericUpDown = new NumericUpDown
					{
						Size = new Size(100, 23),
						Location = new Point(270, yPosition),
						Minimum = 0,
						Maximum = 999,
						Value = originalCodingDecoder.GetOptionRawValue(optionName)
					};
					numericUpDown.ValueChanged += (s, e) => OnCodingValueChanged(optionName, (int)numericUpDown.Value);
					numericUpDown.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					control = numericUpDown;
				}
				else
				{
					var comboBox = new ComboBox
					{
						Size = new Size(200, 23),
						Location = new Point(270, yPosition),
						DropDownStyle = ComboBoxStyle.DropDownList
					};
					var possibleValues = originalCodingDecoder.GetOptionPossibleValues(optionName);
					comboBox.Items.AddRange(possibleValues);
					var rawValue = originalCodingDecoder.GetOptionRawValue(optionName);
					if (rawValue >= 0 && rawValue < possibleValues.Length)
					{
						comboBox.SelectedIndex = rawValue;
					}
					else
					{
						comboBox.Items.Add($"Invalid raw value: {rawValue}");
						comboBox.SelectedIndex = comboBox.Items.Count - 1;
					}
					comboBox.SelectedIndexChanged += (s, e) =>
					{
						var selectedItem = comboBox.SelectedItem?.ToString();
						if (selectedItem != null && !selectedItem.StartsWith("Invalid raw value:", StringComparison.Ordinal))
						{
							OnCodingValueChanged(optionName, selectedItem);
						}
					};
					comboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					control = comboBox;
				}

				codingScrollPanel.Controls.Add(control);
				codingControls[optionName] = control;

				var validationLabel = new Label
				{
					AutoEllipsis = true,
					ForeColor = Color.Firebrick,
					Location = new Point(490, yPosition + 3),
					Size = new Size(280, 20),
					Visible = false
				};
				codingScrollPanel.Controls.Add(validationLabel);
				codingValidationLabels[optionName] = validationLabel;

				yPosition += 35;
			}

			saveCodingButton.Enabled = true;
			resetCodingButton.Enabled = true;
			ApplyValidationHighlights();
			UpdateBitFieldLabel();
		}

		private void OnCodingValueChanged(string optionName, object value)
		{
			try
			{
				modifiedCodingDecoder = modifiedCodingDecoder!.SetOptionValue(optionName, value);
				bool hasChanges = !modifiedCodingDecoder.BitField.Equals(originalCodingDecoder!.BitField);
				saveCodingButton.Text = hasChanges ? "Save Changes*" : "Save Changes";
				ApplyValidationHighlights();
				UpdateBitFieldLabel();
			}
			catch (System.Exception ex)
			{
				MessageBox.Show($"Invalid value for {optionName}: {ex.Message}", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				ResetSingleControl(optionName);
			}
		}

		private void ResetSingleControl(string optionName)
		{
			if (!codingControls.TryGetValue(optionName, out var control))
				return;

			if (control is ComboBox comboBox)
			{
				var rawValue = originalCodingDecoder!.GetOptionRawValue(optionName);
				var possibleValues = originalCodingDecoder.GetOptionPossibleValues(optionName);
				if (rawValue >= 0 && rawValue < possibleValues.Length)
					comboBox.SelectedIndex = rawValue;
				else
					comboBox.SelectedItem = $"Invalid raw value: {rawValue}";
			}
			else if (control is NumericUpDown numericUpDown)
				numericUpDown.Value = originalCodingDecoder!.GetOptionRawValue(optionName);
		}

		private void ApplyValidationHighlights()
		{
			if (modifiedCodingDecoder == null)
				return;

			foreach (var label in codingLabels.Values)
				label.ForeColor = SystemColors.ControlText;

			foreach (var control in codingControls.Values)
			{
				control.BackColor = SystemColors.Window;
				validationToolTip.SetToolTip(control, string.Empty);
			}

			foreach (var validationLabel in codingValidationLabels.Values)
			{
				validationLabel.Text = string.Empty;
				validationLabel.Visible = false;
				validationToolTip.SetToolTip(validationLabel, string.Empty);
			}

			var messagesByOption = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var issue in modifiedCodingDecoder.GetValidationIssues())
			{
				foreach (var optionName in issue.OptionNames)
				{
					if (!messagesByOption.TryGetValue(optionName, out var messages))
					{
						messages = new List<string>();
						messagesByOption[optionName] = messages;
					}
					messages.Add(issue.Message);
				}
			}

			foreach (var (optionName, messages) in messagesByOption)
			{
				var message = string.Join(Environment.NewLine, messages.Distinct());

				if (codingLabels.TryGetValue(optionName, out var label))
				{
					label.ForeColor = Color.Firebrick;
					validationToolTip.SetToolTip(label, message);
				}

				if (codingControls.TryGetValue(optionName, out var control))
				{
					control.BackColor = Color.MistyRose;
					validationToolTip.SetToolTip(control, message);
				}

				if (codingValidationLabels.TryGetValue(optionName, out var validationLabel))
				{
					validationLabel.Text = "Invalid";
					validationLabel.Visible = true;
					validationToolTip.SetToolTip(validationLabel, message);
				}
			}
		}

		private void UpdateBitFieldLabel()
		{
			if (modifiedCodingDecoder != null)
				bitFieldLabel.Text = $"BitField: 0x{modifiedCodingDecoder.BitField:X16}";
			else
				bitFieldLabel.Text = "BitField: -";
		}
	}
}
