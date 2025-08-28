using LotusECMLogger.Services;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LotusECMLogger.Controls
{
	public sealed class EcuCodingControl : UserControl
	{
		private readonly IEcuCodingService service;
		private readonly Panel codingTopPanel;
		private readonly Panel codingScrollPanel;
		private readonly Button readCodesButton;
		private readonly Button writeCodesButton;
		private readonly Button saveCodingButton;
		private readonly Button resetCodingButton;
		private readonly Label bitFieldLabel;

		private T6eCodingDecoder? originalCodingDecoder;
		private T6eCodingDecoder? modifiedCodingDecoder;
		private readonly Dictionary<string, Control> codingControls = new Dictionary<string, Control>();

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

			Dock = DockStyle.Fill;

			codingTopPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
			codingScrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

			readCodesButton = new Button { Text = "Read Codes", Location = new Point(10, 5), Size = new Size(100, 30) };
			writeCodesButton = new Button { Text = "Write Codes", Location = new Point(120, 5), Size = new Size(100, 30), Enabled = false };
			saveCodingButton = new Button { Text = "Save Changes", Location = new Point(230, 5), Size = new Size(110, 30), Enabled = false };
			resetCodingButton = new Button { Text = "Reset", Location = new Point(350, 5), Size = new Size(80, 30), Enabled = false };
			bitFieldLabel = new Label { AutoSize = true, Location = new Point(450, 12), Text = "BitField: -" };

			readCodesButton.Click += (s, e) => HandleReadCodes();
			writeCodesButton.Click += (s, e) => HandleWriteCodes();
			saveCodingButton.Click += (s, e) => HandleSaveCoding();
			resetCodingButton.Click += (s, e) => HandleResetCoding();

			codingTopPanel.Controls.Add(readCodesButton);
			codingTopPanel.Controls.Add(writeCodesButton);
			codingTopPanel.Controls.Add(saveCodingButton);
			codingTopPanel.Controls.Add(resetCodingButton);
			codingTopPanel.Controls.Add(bitFieldLabel);

			Controls.Add(codingScrollPanel);
			Controls.Add(codingTopPanel);
		}

		private void HandleReadCodes()
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

				originalCodingDecoder = service.ReadCoding();
				modifiedCodingDecoder = originalCodingDecoder;

				UpdateCodingView();
				writeCodesButton.Enabled = !IsLoggerActive;
				UpdateBitFieldLabel();
				MessageBox.Show("Coding data successfully read from ECU!", "Read Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

		private void HandleWriteCodes()
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

		private void HandleSaveCoding()
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

		private void HandleResetCoding()
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
			UpdateBitFieldLabel();
		}

		private void UpdateCodingView()
		{
			if (originalCodingDecoder == null)
				return;

			codingScrollPanel.Controls.Clear();
			codingControls.Clear();

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
					comboBox.SelectedItem = originalCodingDecoder.GetOptionValue(optionName);
					comboBox.SelectedIndexChanged += (s, e) => OnCodingValueChanged(optionName, comboBox.SelectedItem?.ToString());
					comboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
					control = comboBox;
				}

				codingScrollPanel.Controls.Add(control);
				codingControls[optionName] = control;

				yPosition += 35;
			}

			saveCodingButton.Enabled = true;
			resetCodingButton.Enabled = true;
			UpdateBitFieldLabel();
		}

		private void OnCodingValueChanged(string optionName, object? value)
		{
			try
			{
				modifiedCodingDecoder = modifiedCodingDecoder!.SetOptionValue(optionName, value);
				bool hasChanges = !modifiedCodingDecoder.BitField.Equals(originalCodingDecoder!.BitField);
				saveCodingButton.Text = hasChanges ? "Save Changes*" : "Save Changes";
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
				comboBox.SelectedItem = originalCodingDecoder!.GetOptionValue(optionName);
			else if (control is NumericUpDown numericUpDown)
				numericUpDown.Value = originalCodingDecoder!.GetOptionRawValue(optionName);
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


