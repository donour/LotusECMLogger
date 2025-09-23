using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
	public sealed class ObdResetControl : UserControl
	{
		private readonly IObdResetService resetService;
		private readonly TableLayoutPanel layoutPanel;
		private readonly FlowLayoutPanel actionsPanel;
		private readonly Label infoLabel;
		private readonly Button resetButton;

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

			Dock = DockStyle.Fill;

			layoutPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				Padding = new Padding(10)
			};
			layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			infoLabel = new Label
			{
				AutoSize = true,
				Text = "OBD-II Learned Data Reset\nThis sends a Mode 0x11 request to the ECM.",
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
			};

			actionsPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				Margin = new Padding(0, 8, 0, 0)
			};

			resetButton = new Button { Text = "Perform Reset", AutoSize = true };
			resetButton.Click += (s, e) => HandleResetClick();
			actionsPanel.Controls.Add(resetButton);

			layoutPanel.Controls.Add(infoLabel, 0, 0);
			layoutPanel.Controls.Add(actionsPanel, 0, 1);

			Controls.Add(layoutPanel);

			Resize += (s, e) => UpdateWrappedLabelWidth();
			UpdateWrappedLabelWidth();
		}

		private void UpdateWrappedLabelWidth()
		{
			// Reserve padding from layoutPanel.Padding and some breathing room
			int horizontalPadding = layoutPanel.Padding.Left + layoutPanel.Padding.Right;
			int maxWidth = Math.Max(50, layoutPanel.ClientSize.Width - horizontalPadding);
			infoLabel.MaximumSize = new Size(maxWidth, 0);
		}

		private void HandleResetClick()
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


