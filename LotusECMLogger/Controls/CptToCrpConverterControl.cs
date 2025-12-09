namespace LotusECMLogger.Controls
{
	public sealed class CptToCrpConverterControl : UserControl
	{
		private readonly TableLayoutPanel layoutPanel;
		private readonly FlowLayoutPanel actionsPanel;
		private readonly Label infoLabel;
		private readonly TextBox cptFileTextBox;
		private readonly Button browseCptButton;
		private readonly TextBox crpFileTextBox;
		private readonly Button browseCrpButton;
		private readonly Button convertButton;
		private readonly Label statusLabel;

		public CptToCrpConverterControl()
		{
			Dock = DockStyle.Fill;

			layoutPanel = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 6,
				Padding = new Padding(10)
			};
			layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Info label
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // CPT file input
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // CRP file input
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Convert button
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));  // Status label
			layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Filler

			infoLabel = new Label
			{
				AutoSize = true,
				Text = "CPT to CRP Converter\n\nConvert .CPT calibration files to .CRP format for ECU flashing.\nImplements T6 calrom packing with XTEA encryption.",
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
				Margin = new Padding(0, 0, 0, 20)
			};

			// CPT file selection
			var cptPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				Margin = new Padding(0, 0, 0, 10)
			};

			var cptLabel = new Label
			{
				Text = "Source CPT File:",
				AutoSize = true,
				Anchor = AnchorStyles.Left,
				Margin = new Padding(0, 7, 10, 0)
			};

			cptFileTextBox = new TextBox
			{
				Width = 350,
				ReadOnly = true,
				Anchor = AnchorStyles.Left
			};

			browseCptButton = new Button
			{
				Text = "Browse...",
				AutoSize = true,
				Margin = new Padding(5, 0, 0, 0)
			};
			browseCptButton.Click += BrowseCptButton_Click;

			cptPanel.Controls.Add(cptLabel);
			cptPanel.Controls.Add(cptFileTextBox);
			cptPanel.Controls.Add(browseCptButton);

			// CRP file selection
			var crpPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				Margin = new Padding(0, 0, 0, 10)
			};

			var crpLabel = new Label
			{
				Text = "Output CRP File:",
				AutoSize = true,
				Anchor = AnchorStyles.Left,
				Margin = new Padding(0, 7, 10, 0)
			};

			crpFileTextBox = new TextBox
			{
				Width = 350,
				ReadOnly = true,
				Anchor = AnchorStyles.Left
			};

			browseCrpButton = new Button
			{
				Text = "Browse...",
				AutoSize = true,
				Margin = new Padding(5, 0, 0, 0)
			};
			browseCrpButton.Click += BrowseCrpButton_Click;

			crpPanel.Controls.Add(crpLabel);
			crpPanel.Controls.Add(crpFileTextBox);
			crpPanel.Controls.Add(browseCrpButton);

			// Convert button
			actionsPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Top,
				AutoSize = true,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				Margin = new Padding(0, 10, 0, 10)
			};

			convertButton = new Button
			{
				Text = "Convert to CRP",
				AutoSize = true,
				Enabled = false
			};
			convertButton.Click += ConvertButton_Click;
			actionsPanel.Controls.Add(convertButton);

			// Status label
			statusLabel = new Label
			{
				AutoSize = true,
				Text = "",
				ForeColor = System.Drawing.Color.Green,
				Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
				Margin = new Padding(0, 10, 0, 0)
			};

			layoutPanel.Controls.Add(infoLabel, 0, 0);
			layoutPanel.Controls.Add(cptPanel, 0, 1);
			layoutPanel.Controls.Add(crpPanel, 0, 2);
			layoutPanel.Controls.Add(actionsPanel, 0, 3);
			layoutPanel.Controls.Add(statusLabel, 0, 4);

			Controls.Add(layoutPanel);

			Resize += (s, e) => UpdateWrappedLabelWidth();
			UpdateWrappedLabelWidth();
		}

		private void UpdateWrappedLabelWidth()
		{
			int horizontalPadding = layoutPanel.Padding.Left + layoutPanel.Padding.Right;
			int maxWidth = Math.Max(50, layoutPanel.ClientSize.Width - horizontalPadding);
			infoLabel.MaximumSize = new Size(maxWidth, 0);
		}

		private void BrowseCptButton_Click(object? sender, EventArgs e)
		{
			using var openFileDialog = new OpenFileDialog
			{
				Title = "Select CPT File",
				Filter = "CPT Files (*.cpt)|*.cpt|All Files (*.*)|*.*",
				FilterIndex = 1
			};

			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				cptFileTextBox.Text = openFileDialog.FileName;

				// Auto-suggest CRP file name
				if (string.IsNullOrEmpty(crpFileTextBox.Text))
				{
					string directory = Path.GetDirectoryName(openFileDialog.FileName) ?? "";
					string fileName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
					crpFileTextBox.Text = Path.Combine(directory, fileName + ".crp");
				}

				UpdateConvertButtonState();
				statusLabel.Text = "";
			}
		}

		private void BrowseCrpButton_Click(object? sender, EventArgs e)
		{
			using var saveFileDialog = new SaveFileDialog
			{
				Title = "Save CRP File",
				Filter = "CRP Files (*.crp)|*.crp|All Files (*.*)|*.*",
				FilterIndex = 1,
				FileName = Path.GetFileName(crpFileTextBox.Text)
			};

			if (saveFileDialog.ShowDialog() == DialogResult.OK)
			{
				crpFileTextBox.Text = saveFileDialog.FileName;
				UpdateConvertButtonState();
				statusLabel.Text = "";
			}
		}

		private void UpdateConvertButtonState()
		{
			convertButton.Enabled = !string.IsNullOrEmpty(cptFileTextBox.Text) &&
									!string.IsNullOrEmpty(crpFileTextBox.Text);
		}

		private void ConvertButton_Click(object? sender, EventArgs e)
		{
			string cptPath = cptFileTextBox.Text;
			string crpPath = crpFileTextBox.Text;

			if (string.IsNullOrEmpty(cptPath) || string.IsNullOrEmpty(crpPath))
			{
				return;
			}

			if (!File.Exists(cptPath))
			{
				statusLabel.ForeColor = System.Drawing.Color.Red;
				statusLabel.Text = "Error: CPT file does not exist.";
				return;
			}

			try
			{
				convertButton.Enabled = false;
				convertButton.Text = "Converting...";
				statusLabel.Text = "Converting...";
				statusLabel.ForeColor = System.Drawing.Color.Blue;

				Application.DoEvents();

				bool success = CptToCrpConverter.Convert(cptPath, crpPath);

				if (success)
				{
					statusLabel.ForeColor = System.Drawing.Color.Green;
					statusLabel.Text = $"Success! CRP file created: {Path.GetFileName(crpPath)}";

					MessageBox.Show(
						$"Conversion completed successfully!\n\nOutput file: {crpPath}",
						"Conversion Success",
						MessageBoxButtons.OK,
						MessageBoxIcon.Information);
				}
				else
				{
					statusLabel.ForeColor = System.Drawing.Color.Red;
					statusLabel.Text = "Conversion failed. Check console for details.";

					MessageBox.Show(
						"Conversion failed. Please check the console output for error details.",
						"Conversion Failed",
						MessageBoxButtons.OK,
						MessageBoxIcon.Error);
				}
			}
			catch (Exception ex)
			{
				statusLabel.ForeColor = System.Drawing.Color.Red;
				statusLabel.Text = $"Error: {ex.Message}";

				MessageBox.Show(
					$"An error occurred during conversion:\n\n{ex.Message}",
					"Conversion Error",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
			finally
			{
				convertButton.Enabled = true;
				convertButton.Text = "Convert to CRP";
			}
		}
	}
}
