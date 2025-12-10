using LotusECMLogger.Services;
using System.Text;

namespace LotusECMLogger.Controls
{
	/// <summary>
	/// User control for T6 RMA (Remote Memory Access) logging
	/// Allows users to log specific ECU memory addresses to CSV files
	/// </summary>
	public partial class T6RMAControl : UserControl
	{
		private readonly IT6RMAService rmaService;

		private bool isLoggerActive;
		private int sampleCount;

		public bool IsLoggerActive
		{
			get => isLoggerActive;
			set
			{
				isLoggerActive = value;
				UpdateUIState();
			}
		}

		public T6RMAControl(IT6RMAService rmaService)
		{
			this.rmaService = rmaService;
			this.rmaService.DataReceived += OnDataReceived;
			this.rmaService.ErrorOccurred += OnErrorOccurred;

			InitializeComponent();

			// Set default CSV path with timestamp
			string defaultCsvPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\T6RMA_{DateTime.Now:yyyyMMddTHHmmss}.csv";
			csvPathTextBox.Text = defaultCsvPath;

			Dock = DockStyle.Fill;
		}

		partial void DisposeManaged()
		{
			rmaService.DataReceived -= OnDataReceived;
			rmaService.ErrorOccurred -= OnErrorOccurred;
			rmaService?.Dispose();
		}

		private void BrowseCsvButton_Click(object? sender, EventArgs e)
		{
			using var saveDialog = new SaveFileDialog
			{
				Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
				DefaultExt = "csv",
				FileName = csvPathTextBox.Text
			};

			if (saveDialog.ShowDialog() == DialogResult.OK)
			{
				csvPathTextBox.Text = saveDialog.FileName;
			}
		}

		private void StartButton_Click(object? sender, EventArgs e)
		{
			if (isLoggerActive)
			{
				MessageBox.Show("Cannot start T6 RMA logging while main logger is active. Please stop the logger first.",
					"Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			try
			{
				// Parse address
				string addressText = addressTextBox.Text.Trim();
				if (addressText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				{
					addressText = addressText[2..];
				}

				if (!uint.TryParse(addressText, System.Globalization.NumberStyles.HexNumber, null, out uint address))
				{
					MessageBox.Show("Invalid memory address. Please enter a valid hexadecimal address (e.g., 0x40000000).",
						"Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				byte length = (byte)lengthNumeric.Value;
				int interval = (int)intervalNumeric.Value;
				string csvPath = csvPathTextBox.Text.Trim();

				if (string.IsNullOrWhiteSpace(csvPath))
				{
					MessageBox.Show("Please specify a CSV output file path.",
						"Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				// Reset sample counter
				sampleCount = 0;
				samplesValueLabel.Text = "0";

				// Start logging
				rmaService.StartLogging(address, length, interval, csvPath);

				statusValueLabel.Text = $"Logging 0x{address:X8} ({length} bytes)";
				statusValueLabel.ForeColor = Color.Green;

				UpdateUIState();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to start logging: {ex.Message}",
					"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void StopButton_Click(object? sender, EventArgs e)
		{
			try
			{
				rmaService.StopLogging();

				statusValueLabel.Text = "Stopped";
				statusValueLabel.ForeColor = Color.Black;

				UpdateUIState();

				MessageBox.Show($"Logging stopped. {sampleCount} samples collected.",
					"Logging Stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Error stopping logging: {ex.Message}",
					"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OnDataReceived(object? sender, T6RMADataEventArgs e)
		{
			// Update UI on UI thread
			if (InvokeRequired)
			{
				BeginInvoke(() => OnDataReceived(sender, e));
				return;
			}

			sampleCount++;
			samplesValueLabel.Text = sampleCount.ToString();
			lastUpdateValueLabel.Text = e.Timestamp.ToString("HH:mm:ss.fff");

			// Format data display
			var sb = new StringBuilder();
			sb.AppendLine($"Timestamp: {e.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
			sb.AppendLine($"Address: 0x{e.MemoryAddress:X8}");
			sb.AppendLine($"Length: {e.DataLength} bytes");
			sb.AppendLine();
			sb.AppendLine("Hex Data:");

			// Display data in rows of 16 bytes
			for (int i = 0; i < e.Data.Length; i += 16)
			{
				sb.Append($"  {i:X4}:  ");

				// Hex values
				for (int j = 0; j < 16 && i + j < e.Data.Length; j++)
				{
					sb.Append($"{e.Data[i + j]:X2} ");
					if (j == 7) sb.Append(" ");
				}

				// Padding
				int remaining = 16 - Math.Min(16, e.Data.Length - i);
				sb.Append(new string(' ', remaining * 3 + (remaining > 8 ? 1 : 0)));

				// ASCII representation
				sb.Append("  |");
				for (int j = 0; j < 16 && i + j < e.Data.Length; j++)
				{
					char c = (char)e.Data[i + j];
					sb.Append(char.IsControl(c) ? '.' : c);
				}
				sb.AppendLine("|");
			}

			// Add numeric interpretations
			sb.AppendLine();
			sb.AppendLine("Numeric Interpretations:");
			if (e.Data.Length >= 1)
				sb.AppendLine($"  Byte (unsigned): {e.Data[0]}");
			if (e.Data.Length >= 2)
				sb.AppendLine($"  Int16 (LE): {BitConverter.ToInt16(e.Data, 0)}");
			if (e.Data.Length >= 4)
			{
				sb.AppendLine($"  Int32 (LE): {BitConverter.ToInt32(e.Data, 0)}");
				sb.AppendLine($"  Float (LE): {BitConverter.ToSingle(e.Data, 0):F6}");
			}

			dataTextBox.Text = sb.ToString();
		}

		private void OnErrorOccurred(object? sender, string errorMessage)
		{
			// Update UI on UI thread
			if (InvokeRequired)
			{
				BeginInvoke(() => OnErrorOccurred(sender, errorMessage));
				return;
			}

			statusValueLabel.Text = "Error";
			statusValueLabel.ForeColor = Color.Red;

			MessageBox.Show($"Logging error: {errorMessage}",
				"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		private void UpdateUIState()
		{
			bool logging = rmaService.IsLogging;

			// Configuration controls disabled while logging
			addressTextBox.Enabled = !logging && !isLoggerActive;
			lengthNumeric.Enabled = !logging && !isLoggerActive;
			intervalNumeric.Enabled = !logging && !isLoggerActive;
			csvPathTextBox.Enabled = !logging && !isLoggerActive;
			browseCsvButton.Enabled = !logging && !isLoggerActive;

			// Button states
			startButton.Enabled = !logging && !isLoggerActive;
			stopButton.Enabled = logging;

			if (!logging && !isLoggerActive)
			{
				statusValueLabel.Text = "Idle";
				statusValueLabel.ForeColor = Color.Black;
			}
		}

	}
}
