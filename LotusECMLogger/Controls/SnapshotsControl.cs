using LotusECMLogger.Services.Ccp;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace LotusECMLogger.Controls
{
	/// <summary>
	/// Downloads point-in-time snapshots of a T6 (2011+) ECU's flash - the Learned Data, Calibration and Program
	/// regions - to .bin files over CCP, on the application's shared session. CCP reads need no unlock, and each read
	/// is checked against the ECU's own checksum of the region before it is saved. A download's button turns into
	/// Cancel while it runs (the program takes several minutes).
	/// </summary>
	public partial class SnapshotsControl : UserControl
	{
		private readonly CcpSessionHost host;
		private bool isLoggerActive;
		private CancellationTokenSource? running;
		private Button? runningButton;

		/// <summary>Asks where to save, given the suggested name; null cancels. Replaceable so UI tests can answer it.</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal Func<string, string?> ChooseFile { get; set; }

		/// <summary>Tells the user how a download ended (message, title, icon). Replaceable so UI tests can read it.</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal Action<string, string, MessageBoxIcon> Notify { get; set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsLoggerActive
		{
			get => isLoggerActive;
			set
			{
				isLoggerActive = value;
				UpdateButtons();
			}
		}

		public SnapshotsControl(CcpSessionHost host)
		{
			this.host = host ?? throw new ArgumentNullException(nameof(host));

			InitializeComponent();

			// Every button reads "Download"; the group box titles say which region, so the three
			// buttons stay the same width instead of being sized by their labels.
			GuiIcons.ApplyToButton(downloadLearnedDataButton, GuiIcons.Download);
			GuiIcons.ApplyToButton(downloadCalibrationButton, GuiIcons.Download);
			GuiIcons.ApplyToButton(downloadProgramButton, GuiIcons.Download);

			WrapLabelToContainer(noticeLabel);
			WrapLabelToContainer(learnedDataInfoLabel);
			WrapLabelToContainer(calibrationInfoLabel);
			WrapLabelToContainer(programInfoLabel);

			ChooseFile = suggested =>
			{
				using var saveDialog = new SaveFileDialog
				{
					Filter = "Binary Files (*.bin)|*.bin|All Files (*.*)|*.*",
					DefaultExt = "bin",
					FileName = suggested,
				};
				return saveDialog.ShowDialog(this) == DialogResult.OK ? saveDialog.FileName : null;
			};
			Notify = (text, title, icon) => MessageBox.Show(this, text, title, MessageBoxButtons.OK, icon);

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

		private async void DownloadLearnedDataButton_Click(object? sender, EventArgs e) =>
			await DownloadAsync(T6FlashRegion.LearnedData, downloadLearnedDataButton, learnedDataProgressBar, learnedDataStatusValueLabel);

		private async void DownloadCalibrationButton_Click(object? sender, EventArgs e) =>
			await DownloadAsync(T6FlashRegion.Calibration, downloadCalibrationButton, calibrationProgressBar, calibrationStatusValueLabel);

		private async void DownloadProgramButton_Click(object? sender, EventArgs e) =>
			await DownloadAsync(T6FlashRegion.Program, downloadProgramButton, programProgressBar, programStatusValueLabel);

		/// <summary>One download at a time; its button cancels it, the others wait.</summary>
		private void UpdateButtons()
		{
			foreach (var button in new[] { downloadLearnedDataButton, downloadCalibrationButton, downloadProgramButton })
				button.Enabled = running is null ? !isLoggerActive : ReferenceEquals(button, runningButton);
		}

		private static void SetStatus(Label label, string text, Color color)
		{
			label.Text = text;
			label.ForeColor = color;
		}

		/// <summary>
		/// Shared flow for every region: join the CCP session and identify the ECU, ask where to save (named after
		/// its build), then read the region with live progress, check it against the ECU's checksum and save it.
		/// </summary>
		private async Task DownloadAsync(T6FlashRegion region, Button button, ProgressBar progressBar, Label statusLabel)
		{
			if (running is not null)
			{
				if (ReferenceEquals(button, runningButton)) running.Cancel();
				return;
			}
			if (isLoggerActive)
			{
				MessageBox.Show($"Cannot download {region.Name} while logging is active. Please stop logging first.",
					"Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			using var cancel = new CancellationTokenSource();
			(running, runningButton) = (cancel, button);
			button.Text = "Cancel";
			GuiIcons.ApplyToButton(button, GuiIcons.Stop);
			UpdateButtons();
			try
			{
				SetStatus(statusLabel, "Connecting...", Color.Blue);
				await using var lease = await host.AcquireAsync(cancel.Token);
				var identification = await lease.Master.IdentifyAsync(cancellationToken: cancel.Token);
				string build = identification.Build?.Build ?? identification.CalibrationId?.Trim() ?? "T6";

				if (ChooseFile(LoggerPaths.TimestampedPath($"{region.FilePrefix}_{build}", "bin")) is not { } path)
				{
					SetStatus(statusLabel, "Idle", Color.Black);
					return;
				}

				progressBar.Maximum = region.Length;
				progressBar.Value = 0;
				SetStatus(statusLabel, "Downloading...", Color.Blue);
				var progress = new Progress<int>(read =>
				{
					progressBar.Value = Math.Min(read, progressBar.Maximum);
					statusLabel.Text = read >= region.Length
						? "Checking against the ECU's checksum..."
						: string.Create(CultureInfo.CurrentCulture, $"Downloading... {read:N0}/{region.Length:N0} bytes");
				});
				var elapsed = Stopwatch.StartNew();
				var bytes = await CcpFlashSnapshot.ReadAsync(lease.Master, region, progress, cancel.Token);
				LoggerPaths.EnsureParentDirectory(path);
				await File.WriteAllBytesAsync(path, bytes, CancellationToken.None);

				SetStatus(statusLabel, string.Create(CultureInfo.CurrentCulture, $"Saved ({elapsed.Elapsed.TotalSeconds:0} s, checksum OK)"), Color.Green);
				Notify($"Saved {bytes.Length:N0} bytes of {region.Name} to {path}.\n\nThe ECU's checksum of the region matches the file.",
					"Download Complete", MessageBoxIcon.Information);
			}
			catch (OperationCanceledException) when (cancel.IsCancellationRequested)
			{
				SetStatus(statusLabel, "Cancelled", Color.Black);
			}
			catch (CcpTimeoutException ex)
			{
				SetStatus(statusLabel, "No response", Color.Red);
				Notify($"The ECU did not answer over CCP: {ex.Message}\n\nSwitch the ignition on and check the connection. " +
					"Snapshots need a T6 ECU (2011 on) whose calibration has CCP enabled.",
					"Download Failed", MessageBoxIcon.Error);
			}
			catch (Exception ex)
			{
				SetStatus(statusLabel, "Error", Color.Red);
				Notify($"Error downloading {region.Name}: {ex.Message}", "Error", MessageBoxIcon.Error);
			}
			finally
			{
				(running, runningButton) = (null, null);
				if (!IsDisposed)
				{
					button.Text = "Download";
					GuiIcons.ApplyToButton(button, GuiIcons.Download);
					UpdateButtons();
				}
			}
		}
	}
}
