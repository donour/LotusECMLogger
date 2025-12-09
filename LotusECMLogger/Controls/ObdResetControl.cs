using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
	public partial class ObdResetControl : UserControl
	{
		private readonly IObdResetService resetService;

		private bool isLoggerActive;
		public bool IsLoggerActive
		{
			get => isLoggerActive;
			set
			{
				isLoggerActive = value;
				resetButton.Enabled = !isLoggerActive;
			}
		}

		public ObdResetControl(IObdResetService resetService)
		{
			this.resetService = resetService;
			InitializeComponent();

			Dock = DockStyle.Fill;
			UpdateWrappedLabelWidth();
		}

		private void ObdResetControl_Resize(object? sender, EventArgs e)
		{
			UpdateWrappedLabelWidth();
		}

		private void UpdateWrappedLabelWidth()
		{
			// Reserve padding from layoutPanel.Padding and some breathing room
			int horizontalPadding = layoutPanel.Padding.Left + layoutPanel.Padding.Right;
			int maxWidth = Math.Max(50, layoutPanel.ClientSize.Width - horizontalPadding);
			infoLabel.MaximumSize = new Size(maxWidth, 0);
		}

		private void resetButton_Click(object? sender, EventArgs e)
		{
			if (IsLoggerActive)
			{
				MessageBox.Show("Cannot perform reset while logging is active. Please stop the logger first.", "Logger Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			var result = MessageBox.Show(
				"Are you sure you need to perform an OBD-II learned data reset?\n\n" +
				"This operation cannot be reversed and may affect drivability until the ECU relearns.\n\n" +
				"Confirm to proceed.",
				"Confirm OBD-II Reset",
				MessageBoxButtons.YesNo,
				MessageBoxIcon.Warning);

			if (result != DialogResult.Yes)
				return;

			try
			{
				resetButton.Enabled = false;
				resetButton.Text = "Resetting...";

				var (success, error) = resetService.PerformLearningReset();
				if (success)
				{
					MessageBox.Show("OBD-II learned data reset request sent successfully.", "Reset Sent", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				else
				{
					MessageBox.Show($"Failed to send reset: {error}", "Reset Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
			catch (System.Exception ex)
			{
				MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				resetButton.Enabled = !IsLoggerActive;
				resetButton.Text = "Perform Reset";
			}
		}
	}
}


