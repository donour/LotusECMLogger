using LotusECMLogger.Models;
using LotusECMLogger.Services;
using System.ComponentModel;
using System.Diagnostics;

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

		// True while control selections are updated programmatically, so the
		// SelectedIndexChanged handlers don't treat those updates as user edits.
		private bool isSyncingControls;

		private bool isLoggerActive;
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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

		/// <summary>
		/// Design-time constructor. The Windows Forms designer requires a public
		/// parameterless constructor to instantiate the control on its design surface.
		/// It is never used at runtime; the coding service is supplied via the other constructor.
		/// </summary>
		public EcuCodingControl() : this(null!)
		{
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

			SetupCodingControls();
		}

		/// <summary>
		/// Binds the statically declared coding rows (labels, combo boxes and validation
		/// labels defined in the designer) to their coding options and populates the combo
		/// choices from the decoder metadata. The rows are laid out at design time so they
		/// are visible without any data being read; this only wires up behaviour and choices.
		/// </summary>
		private void SetupCodingControls()
		{
			// A zero-valued decoder exposes the option metadata (names and possible
			// values) without needing any coding data to be read from the ECU.
			var metadata = new T6eCodingDecoder(0UL);

			isSyncingControls = true;
			try
			{
				foreach (Control child in codingScrollPanel.Controls)
				{
					if (child.Tag is not string optionName)
						continue;

					switch (child)
					{
						case ComboBox comboBox:
							codingControls[optionName] = comboBox;
							if (!metadata.IsOptionNumeric(optionName))
							{
								comboBox.Items.Clear();
								comboBox.Items.AddRange(metadata.GetOptionPossibleValues(optionName));
							}
							comboBox.SelectedIndexChanged += (s, e) =>
							{
								if (isSyncingControls)
									return;
								var selectedItem = comboBox.SelectedItem?.ToString();
								if (selectedItem != null && !selectedItem.StartsWith("Invalid raw value:", StringComparison.Ordinal))
									OnCodingValueChanged(optionName, selectedItem);
							};
							break;

						case Label label when label.Name.StartsWith("codingValidation", StringComparison.Ordinal):
							codingValidationLabels[optionName] = label;
							break;

						case Label label:
							codingLabels[optionName] = label;
							break;
					}
				}
			}
			finally
			{
				isSyncingControls = false;
			}

			Debug.Assert(
				codingControls.Count == metadata.GetAvailableOptions().Length,
				"EcuCodingControl designer rows are out of sync with T6eCodingDecoder options.");
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

			isSyncingControls = true;
			try
			{
				foreach (var optionName in originalCodingDecoder.GetAvailableOptions())
				{
					if (codingControls.TryGetValue(optionName, out var control))
						SyncControlToDecoder(control, optionName, originalCodingDecoder);
				}
			}
			finally
			{
				isSyncingControls = false;
			}

			saveCodingButton.Enabled = true;
			resetCodingButton.Enabled = true;
			ApplyValidationHighlights();
			UpdateBitFieldLabel();
		}

		/// <summary>
		/// Updates a single coding row control to reflect the value held by the decoder.
		/// Callers are responsible for suppressing change notifications where needed.
		/// </summary>
		private static void SyncControlToDecoder(Control control, string optionName, T6eCodingDecoder decoder)
		{
			if (control is ComboBox comboBox)
			{
				// Remove any "Invalid raw value" marker left over from a previous load.
				for (int i = comboBox.Items.Count - 1; i >= 0; i--)
				{
					if (comboBox.Items[i] is string s && s.StartsWith("Invalid raw value:", StringComparison.Ordinal))
						comboBox.Items.RemoveAt(i);
				}

				var possibleValues = decoder.GetOptionPossibleValues(optionName);
				var rawValue = decoder.GetOptionRawValue(optionName);
				if (rawValue >= 0 && rawValue < possibleValues.Length)
				{
					comboBox.SelectedIndex = rawValue;
				}
				else
				{
					comboBox.Items.Add($"Invalid raw value: {rawValue}");
					comboBox.SelectedIndex = comboBox.Items.Count - 1;
				}
			}
			else if (control is NumericUpDown numericUpDown)
			{
				numericUpDown.Value = decoder.GetOptionRawValue(optionName);
			}
		}

		private void OnCodingValueChanged(string optionName, object value)
		{
			// Ignore changes made before any coding data has been read (the rows are
			// visible and interactive at design time / before a read for previewing).
			if (originalCodingDecoder == null || modifiedCodingDecoder == null)
				return;

			try
			{
				modifiedCodingDecoder = modifiedCodingDecoder.SetOptionValue(optionName, value);
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
			if (originalCodingDecoder == null || !codingControls.TryGetValue(optionName, out var control))
				return;

			isSyncingControls = true;
			try
			{
				SyncControlToDecoder(control, optionName, originalCodingDecoder);
			}
			finally
			{
				isSyncingControls = false;
			}
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
					validationLabel.Text = "⚠"; // warning marker; full detail is in the tooltip
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
