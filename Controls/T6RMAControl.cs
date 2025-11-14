using LotusECMLogger.Services;
using System.Text;

namespace LotusECMLogger.Controls
{
	/// <summary>
	/// User control for T6 RMA (Read Memory Address) logging
	/// Allows users to log specific ECU memory addresses to CSV files
	/// </summary>
	public sealed class T6RMAControl : UserControl
	{
		private readonly IT6RMAService rmaService;
		private readonly TableLayoutPanel layoutPanel;
		private readonly GroupBox configGroupBox;
		private readonly GroupBox statusGroupBox;
		private readonly GroupBox dataGroupBox;

		// Configuration controls
		private readonly Label addressLabel;
		private readonly TextBox addressTextBox;
		private readonly Label lengthLabel;
		private readonly NumericUpDown lengthNumeric;
		private readonly Label intervalLabel;
		private readonly NumericUpDown intervalNumeric;
		private readonly Label csvPathLabel;
		private readonly TextBox csvPathTextBox;
		private readonly Button browseCsvButton;
		private readonly Button startButton;
		private readonly Button stopButton;

		// Status controls
		private readonly Label statusLabel;
		private readonly Label statusValueLabel;
		private readonly Label samplesLabel;
		private readonly Label samplesValueLabel;
		private readonly Label lastUpdateLabel;
		private readonly Label lastUpdateValueLabel;

		// Data display controls
		private readonly TextBox dataTextBox;

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

			Dock = DockStyle.Fill;
			Padding = new Padding(10);

			// Main layout
			layoutPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				AutoSize = true
			};
			layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Configuration
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Status
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Data display

			// === Configuration Group ===
			configGroupBox = new GroupBox
			{
				Text = "Memory Read Configuration",
				Dock = DockStyle.Fill,
				AutoSize = true,
				Padding = new Padding(10)
			};

			var configLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 5,
				AutoSize = true
			};
			configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Labels
			configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Inputs
			configLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Buttons

			// Row 0: Address
			addressLabel = new Label { Text = "Memory Address (hex):", AutoSize = true, Anchor = AnchorStyles.Left };
			addressTextBox = new TextBox { Width = 200, Text = "0x40000000" };
			configLayout.Controls.Add(addressLabel, 0, 0);
			configLayout.Controls.Add(addressTextBox, 1, 0);

			// Row 1: Length
			lengthLabel = new Label { Text = "Length (bytes):", AutoSize = true, Anchor = AnchorStyles.Left };
			lengthNumeric = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 4, Width = 100 };
			configLayout.Controls.Add(lengthLabel, 0, 1);
			configLayout.Controls.Add(lengthNumeric, 1, 1);

			// Row 2: Interval
			intervalLabel = new Label { Text = "Polling Interval (ms):", AutoSize = true, Anchor = AnchorStyles.Left };
			intervalNumeric = new NumericUpDown { Minimum = 10, Maximum = 10000, Value = 100, Width = 100 };
			configLayout.Controls.Add(intervalLabel, 0, 2);
			configLayout.Controls.Add(intervalNumeric, 1, 2);

			// Row 3: CSV Path
			csvPathLabel = new Label { Text = "CSV Output File:", AutoSize = true, Anchor = AnchorStyles.Left };
			csvPathTextBox = new TextBox { Dock = DockStyle.Fill, Text = "rma_log.csv" };
			browseCsvButton = new Button { Text = "Browse...", AutoSize = true };
			browseCsvButton.Click += BrowseCsvButton_Click;

			var csvPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = true
			};
			csvPanel.Controls.Add(csvPathTextBox);
			csvPanel.Controls.Add(browseCsvButton);

			configLayout.Controls.Add(csvPathLabel, 0, 3);
			configLayout.Controls.Add(csvPanel, 1, 3);
			configLayout.SetColumnSpan(csvPanel, 2);

			// Row 4: Start/Stop buttons
			var buttonPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AutoSize = true,
				Margin = new Padding(0, 8, 0, 0)
			};

			startButton = new Button { Text = "Start Logging", AutoSize = true, Enabled = true };
			startButton.Click += StartButton_Click;

			stopButton = new Button { Text = "Stop Logging", AutoSize = true, Enabled = false };
			stopButton.Click += StopButton_Click;

			buttonPanel.Controls.Add(startButton);
			buttonPanel.Controls.Add(stopButton);

			configLayout.Controls.Add(buttonPanel, 1, 4);
			configLayout.SetColumnSpan(buttonPanel, 2);

			configGroupBox.Controls.Add(configLayout);

			// === Status Group ===
			statusGroupBox = new GroupBox
			{
				Text = "Logging Status",
				Dock = DockStyle.Fill,
				AutoSize = true,
				Padding = new Padding(10),
				Margin = new Padding(0, 10, 0, 0)
			};

			var statusLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 2,
				RowCount = 3,
				AutoSize = true
			};
			statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

			statusLabel = new Label { Text = "Status:", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
			statusValueLabel = new Label { Text = "Idle", AutoSize = true };
			statusLayout.Controls.Add(statusLabel, 0, 0);
			statusLayout.Controls.Add(statusValueLabel, 1, 0);

			samplesLabel = new Label { Text = "Samples Collected:", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
			samplesValueLabel = new Label { Text = "0", AutoSize = true };
			statusLayout.Controls.Add(samplesLabel, 0, 1);
			statusLayout.Controls.Add(samplesValueLabel, 1, 1);

			lastUpdateLabel = new Label { Text = "Last Update:", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
			lastUpdateValueLabel = new Label { Text = "N/A", AutoSize = true };
			statusLayout.Controls.Add(lastUpdateLabel, 0, 2);
			statusLayout.Controls.Add(lastUpdateValueLabel, 1, 2);

			statusGroupBox.Controls.Add(statusLayout);

			// === Data Display Group ===
			dataGroupBox = new GroupBox
			{
				Text = "Latest Data",
				Dock = DockStyle.Fill,
				Padding = new Padding(10),
				Margin = new Padding(0, 10, 0, 0)
			};

			dataTextBox = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ScrollBars = ScrollBars.Both,
				Font = new Font("Consolas", 9),
				ReadOnly = true,
				Text = "No data received yet..."
			};

			dataGroupBox.Controls.Add(dataTextBox);

			// Add all groups to main layout
			layoutPanel.Controls.Add(configGroupBox, 0, 0);
			layoutPanel.Controls.Add(statusGroupBox, 0, 1);
			layoutPanel.Controls.Add(dataGroupBox, 0, 2);

			Controls.Add(layoutPanel);
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

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				rmaService.DataReceived -= OnDataReceived;
				rmaService.ErrorOccurred -= OnErrorOccurred;
				rmaService?.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
