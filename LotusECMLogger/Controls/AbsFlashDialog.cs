using LotusECMLogger.Services;

namespace LotusECMLogger.Controls;

/// <summary>Operator-facing ABS firmware selection and guarded programming dialog.</summary>
public sealed class AbsFlashDialog : Form
{
    private readonly IAbsService service;
    private readonly TextBox firmwarePath = new() { Dock = DockStyle.Fill };
    private readonly ComboBox driver = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox acknowledge = new() { Text = "I understand this image's trailer/recovery acceptance is unresolved and real ECU acceptance is not proven.", AutoSize = true };
    private readonly Label details = new() { AutoSize = false, Dock = DockStyle.Fill, Height = 110 };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly Button flash = new() { Text = "Flash ABS firmware", AutoSize = true, Enabled = false };
    private readonly Button browse = new() { Text = "Select HEX…", AutoSize = true };
    private readonly Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
    private AbsFirmwareImage? image;
    private bool flashing;

    public AbsFlashDialog(IAbsService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Text = "ABS Firmware Flasher";
        MinimumSize = new Size(700, 430);
        StartPosition = FormStartPosition.CenterParent;
        browse.Click += (_, _) => Browse();
        driver.Items.Add("Select a J2534 driver");
        try { driver.Items.AddRange(J2534Session.DiscoverDriverFileNames().Cast<object>().ToArray()); } catch { }
        driver.SelectedIndex = 0;
        firmwarePath.TextChanged += (_, _) => LoadPreview();
        flash.Click += async (_, _) => await FlashAsync();
        AcceptButton = flash;
        CancelButton = close;

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(firmwarePath, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);
        var driverRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 0, 12, 6) };
        driverRow.Controls.Add(new Label { Text = "J2534 driver:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        driverRow.Controls.Add(driver);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        bottom.Controls.Add(close); bottom.Controls.Add(flash);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(12) };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(new Label { Text = "Select the exact ABS Intel HEX file. Optional .manifest.json metadata beside it is loaded automatically.", Dock = DockStyle.Fill, AutoSize = true }, 0, 0);
        content.Controls.Add(details, 0, 1);
        content.Controls.Add(acknowledge, 0, 2);
        content.Controls.Add(progress, 0, 3);
        Controls.Add(content); Controls.Add(bottom); Controls.Add(driverRow); Controls.Add(pathRow);
        details.Text = "No firmware selected.";
        FormClosing += (_, e) => { if (flashing) { e.Cancel = true; MessageBox.Show(this, "Wait for the ABS programming operation to finish before closing.", "ABS firmware", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "Intel HEX (*.hex;*.ihx;*.ihex)|*.hex;*.ihx;*.ihex|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) firmwarePath.Text = dialog.FileName;
    }

    private void LoadPreview()
    {
        image = null;
        flash.Enabled = false;
        if (string.IsNullOrWhiteSpace(firmwarePath.Text)) { details.Text = "No firmware selected."; return; }
        try
        {
            image = AbsFirmwareImage.Load(firmwarePath.Text);
            string sidecarPath = Path.Combine(Path.GetDirectoryName(image.SourcePath) ?? "", Path.GetFileNameWithoutExtension(image.SourcePath) + ".manifest.json");
            string metadata = File.Exists(sidecarPath) ? "loaded" : "not supplied";
            details.Text = $"SHA-256: {image.Sha256}\r\nAddress range: 0x{image.StartAddress:X8}–0x{image.EndAddressExclusive - 1:X8}\r\n{image.Bytes.Count:N0} bytes in {image.BlockCount:N0} blocks (256-byte blocks; final block is not padded).\r\nOptional metadata: {metadata}\r\nINTEGRITY WARNING: {image.Manifest.IntegrityNote}";
            flash.Enabled = true;
        }
        catch (Exception error) { details.Text = $"Cannot load firmware: {error.Message}"; }
    }

    private async Task FlashAsync()
    {
        if (image is null || !acknowledge.Checked) { MessageBox.Show(this, "Select a valid image and acknowledge the integrity warning.", "ABS flash", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (MessageBox.Show(this, "This will place the ABS into programming mode and erase its application flash. Continue?", "Confirm ABS programming", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        flash.Enabled = false;
        flashing = true;
        firmwarePath.ReadOnly = true; browse.Enabled = false; driver.Enabled = false; acknowledge.Enabled = false; close.Enabled = false;
        try
        {
            string selectedPath = image.SourcePath;
            string selectedHash = image.Sha256;
            var options = new AbsFlashOptions { ConfirmUnresolvedIntegrity = true, ExpectedImageSha256 = selectedHash, DriverFileName = driver.SelectedItem!.ToString()! };
            var uiProgress = new Progress<AbsFlashProgress>(value =>
            {
                progress.Maximum = Math.Max(1, value.TotalBytes);
                progress.Value = Math.Min(progress.Maximum, value.BytesSent);
                details.Text = $"{value.Phase}\r\n{value.BytesSent:N0}/{value.TotalBytes:N0} bytes; block {value.BlockNumber}/{value.BlockCount}\r\nSHA-256: {value.ImageSha256}";
            });
            var result = await Task.Run(() => service.FlashFirmware(selectedPath, options, uiProgress, CancellationToken.None));
            string message = result.success ? $"ABS programming completed. {result.result.BytesSent:N0} bytes sent.\r\nAudit: {result.result.AuditLogPath}\r\n\r\n{result.result.IntegrityWarning}" : $"ABS programming did not complete: {result.errorMessage}\r\n\r\n{result.result.IntegrityWarning}";
            MessageBox.Show(this, message, "ABS firmware result", MessageBoxButtons.OK, result.success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            if (result.success) DialogResult = DialogResult.OK;
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "ABS firmware error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { flashing = false; firmwarePath.ReadOnly = false; browse.Enabled = true; driver.Enabled = true; acknowledge.Enabled = true; close.Enabled = true; flash.Enabled = image is not null; }
    }
}
