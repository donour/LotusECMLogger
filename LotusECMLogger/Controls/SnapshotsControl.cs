using LotusECMLogger.Services;
using System.ComponentModel;

namespace LotusECMLogger.Controls
{
	/// <summary>
	/// User control for downloading point-in-time snapshots of ECU memory (currently the
	/// flash-resident Learned Data, Calibration, and Program regions) to binary files via the
	/// T6 RMA read protocol. An ECU version selector determines which generation's memory map
	/// (addresses/lengths) is used, since T4e/K4/T4/T6(T6e) each lay out flash differently.
	/// </summary>
	public partial class SnapshotsControl : UserControl
	{
		private sealed record EcuVariantOption(EcuVariant Variant, string Label)
		{
			public override string ToString() => Label;
		}

		private static readonly EcuVariantOption[] EcuVariantOptions =
		[
			new(EcuVariant.T4e, "T4e"),
			new(EcuVariant.K4, "K4"),
			new(EcuVariant.T4, "T4"),
			new(EcuVariant.T6, "T6"),
		];

		private readonly IT6RMAService rmaService;

		private bool isLoggerActive;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsLoggerActive
		{
			get => isLoggerActive;
			set
			{
				isLoggerActive = value;
				downloadLearnedDataButton.Enabled = !isLoggerActive;
				downloadCalibrationButton.Enabled = !isLoggerActive;
				downloadProgramButton.Enabled = !isLoggerActive;
			}
		}

		private EcuVariant SelectedVariant =>
			ecuVariantComboBox.SelectedItem is EcuVariantOption option ? option.Variant : EcuVariant.T6;

		public SnapshotsControl(IT6RMAService rmaService)
		{
			this.rmaService = rmaService;

			InitializeComponent();

			// Every button reads "Download"; the group box titles say which region, so the three
			// buttons stay the same width instead of being sized by their labels.
			GuiIcons.ApplyToButton(downloadLearnedDataButton, GuiIcons.Download);
			GuiIcons.ApplyToButton(downloadCalibrationButton, GuiIcons.Download);
			GuiIcons.ApplyToButton(downloadProgramButton, GuiIcons.Download);

			// The unlock requirement is the same for all three regions, so one notice sits at the
			// top of the tab rather than being repeated in every region description.
			WrapLabelToContainer(unlockNoticeLabel);
			WrapLabelToContainer(learnedDataInfoLabel);
			WrapLabelToContainer(calibrationInfoLabel);
			WrapLabelToContainer(programInfoLabel);

			ecuVariantComboBox.Items.AddRange(EcuVariantOptions);
			ecuVariantComboBox.SelectedItem = EcuVariantOptions[^1]; // T6 / T6e: this app's primary target

			Dock = DockStyle.Fill;
		}

		/// <summary>
		/// Pins a wrapping label's width to its container, so that the auto-sizing group box
		/// around it grows by a line when the text wraps.
		/// </summary>
		/// <remarks>
		/// An auto-sizing container measures its children before it constrains their width, so a
		/// plain AutoSize label always measures as a single line no matter how narrow the tab gets.
		/// The group box would then size itself to that one line and clip the download button row
		/// underneath as soon as the text actually wrapped — which the longest of the three
		/// descriptions (Program) did well before the window got narrow. Giving the label an
		/// explicit maximum width makes its measured height the wrapped height, which the row,
		/// the table and the group box all then account for.
		/// </remarks>
		private static void WrapLabelToContainer(Label label)
		{
			Control container = label.Parent!;

			void Update(object? sender, EventArgs e)
			{
				int width = container.ClientSize.Width - container.Padding.Horizontal - label.Margin.Horizontal;

				// Only on a real change: MaximumSize triggers another layout pass, and the width it
				// is derived from never depends on the label's own height, so this settles.
				if (width > 0 && label.MaximumSize.Width != width)
					label.MaximumSize = new Size(width, 0);
			}

			container.SizeChanged += Update;
			Update(container, EventArgs.Empty);
		}

		private async void DownloadLearnedDataButton_Click(object? sender, EventArgs e)
		{
			await DownloadSnapshotAsync(
				rmaService.DownloadLearnedDataAsync,
				"learned data",
				"LearnedData",
				downloadLearnedDataButton,
				learnedDataProgressBar,
				learnedDataStatusValueLabel);
		}

		private async void DownloadCalibrationButton_Click(object? sender, EventArgs e)
		{
			await DownloadSnapshotAsync(
				rmaService.DownloadCalibrationAsync,
				"calibration",
				"Calibration",
				downloadCalibrationButton,
				calibrationProgressBar,
				calibrationStatusValueLabel);
		}

		private async void DownloadProgramButton_Click(object? sender, EventArgs e)
		{
			await DownloadSnapshotAsync(
				rmaService.DownloadProgramAsync,
				"program",
				"Program",
				downloadProgramButton,
				programProgressBar,
				programStatusValueLabel);
		}

		/// <summary>
		/// Shared flow for every snapshot type: block while logging is active, probe the ECU
		/// unlock state, prompt for a save location, then download with live progress. The
		/// currently selected <see cref="EcuVariant"/> is passed to <paramref name="download"/>
		/// and folded into the default filename.
		/// </summary>
		/// <param name="download">The service call that performs the actual RMA read.</param>
		/// <param name="label">Lowercase description used in status/result messages (e.g. "calibration").</param>
		/// <param name="filePrefix">Prefix for the default timestamped .bin filename.</param>
		private async Task DownloadSnapshotAsync(
			Func<EcuVariant, string, IProgress<(int bytesRead, int totalBytes)>, Task<bool>> download,
			string label,
			string filePrefix,
			Button button,
			ProgressBar progressBar,
			Label statusLabel)
		{
			if (isLoggerActive || rmaService.IsLogging)
			{
				MessageBox.Show($"Cannot download {label} while logging is active. Please stop logging first.",
					"Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			EcuVariant variant = SelectedVariant;

			button.Enabled = false;
			statusLabel.Text = "Checking ECU unlock state...";
			statusLabel.ForeColor = Color.Blue;

			bool unlocked;
			try
			{
				unlocked = await Task.Run(() => rmaService.IsEcuUnlocked());
			}
			catch (Exception ex)
			{
				statusLabel.Text = "Error";
				statusLabel.ForeColor = Color.Red;
				button.Enabled = true;
				MessageBox.Show($"Error probing ECU unlock state: {ex.Message}",
					"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			if (!unlocked)
			{
				statusLabel.Text = "ECU locked";
				statusLabel.ForeColor = Color.Red;
				button.Enabled = true;
				MessageBox.Show($"The ECU did not respond to the unlock probe. Unlock the ECU before downloading {label}.",
					"ECU Locked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			using var saveDialog = new SaveFileDialog
			{
				Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
				DefaultExt = "bin",
				FileName = LoggerPaths.TimestampedPath($"{filePrefix}_{variant}", "bin")
			};

			if (saveDialog.ShowDialog() != DialogResult.OK)
			{
				statusLabel.Text = "Idle";
				statusLabel.ForeColor = Color.Black;
				button.Enabled = true;
				return;
			}

			progressBar.Value = 0;
			statusLabel.Text = "Downloading...";
			statusLabel.ForeColor = Color.Blue;

			var progress = new Progress<(int bytesRead, int totalBytes)>(p =>
			{
				progressBar.Maximum = Math.Max(1, p.totalBytes);
				progressBar.Value = Math.Min(p.bytesRead, progressBar.Maximum);
				statusLabel.Text = $"Downloading... {p.bytesRead}/{p.totalBytes} bytes";
			});

			try
			{
				bool success = await download(variant, saveDialog.FileName, progress);

				if (success)
				{
					statusLabel.Text = "Download complete";
					statusLabel.ForeColor = Color.Green;
					MessageBox.Show($"Saved to {saveDialog.FileName}",
						"Download Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
				{
					statusLabel.Text = "Download failed";
					statusLabel.ForeColor = Color.Red;
					MessageBox.Show("No response from ECU. Ensure the vehicle is connected and the ECU is unlocked.",
						"Download Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			catch (Exception ex)
			{
				statusLabel.Text = "Error";
				statusLabel.ForeColor = Color.Red;
				MessageBox.Show($"Error downloading {label}: {ex.Message}",
					"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				button.Enabled = true;
			}
		}
	}
}
