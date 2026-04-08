using System.ComponentModel;
using System.Globalization;
using LotusECMLogger.Services;

namespace LotusECMLogger.Controls
{
    public partial class LoggingConfigEditorControl : UserControl
    {
        public event Action<string>? ConfigurationSaved;

        private readonly BindingList<EditableRequestRow> requestRows = new();
        private MultiECUConfigurationJson currentConfig = LoggingConfigFileService.CreateDefaultConfig();
        private string? currentFilePath;
        private int currentEcuIndex = -1;
        private bool isDirty;
        private bool suppressDirtyTracking;
        private bool suppressSelectionEvents;

        public LoggingConfigEditorControl()
        {
            InitializeComponent();
            InitializeRequestGrid();
            Dock = DockStyle.Fill;
            LoadAvailableConfigurations();
            LoadInitialConfiguration();
        }

        private void InitializeRequestGrid()
        {
            requestsDataGridView.AutoGenerateColumns = false;
            requestsDataGridView.Columns.Clear();

            var typeColumn = new DataGridViewComboBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.Type),
                HeaderText = "Type",
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DataSource = new[] { "Mode01", "Mode22" },
                FillWeight = 70
            };
            requestsDataGridView.Columns.Add(typeColumn);
            requestsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.Name),
                HeaderText = "Name",
                FillWeight = 160
            });
            requestsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.Description),
                HeaderText = "Description",
                FillWeight = 150
            });
            requestsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.Category),
                HeaderText = "Category",
                FillWeight = 100
            });
            requestsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.Unit),
                HeaderText = "Unit",
                FillWeight = 70
            });
            requestsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.PidsText),
                HeaderText = "PIDs",
                FillWeight = 110
            });
            requestsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.PidHighText),
                HeaderText = "PID High",
                FillWeight = 75
            });
            requestsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(EditableRequestRow.PidLowText),
                HeaderText = "PID Low",
                FillWeight = 75
            });

            requestsDataGridView.DataSource = requestRows;
        }

        private void LoadInitialConfiguration()
        {
            if (configPickerComboBox.Items.Count > 0)
            {
                configPickerComboBox.SelectedIndex = 0;
                LoadConfigurationByName(configPickerComboBox.SelectedItem?.ToString());
                return;
            }

            BeginNewConfiguration();
        }

        private void LoadAvailableConfigurations(string? preferredConfigName = null)
        {
            suppressSelectionEvents = true;

            var existingSelection = preferredConfigName ?? configPickerComboBox.SelectedItem?.ToString();
            configPickerComboBox.Items.Clear();

            foreach (var config in MultiECUConfigurationLoader.GetAvailableConfigurations())
            {
                configPickerComboBox.Items.Add(config);
            }

            if (configPickerComboBox.Items.Count > 0)
            {
                var preferredIndex = existingSelection == null
                    ? 0
                    : configPickerComboBox.Items.IndexOf(existingSelection);
                configPickerComboBox.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
            }

            suppressSelectionEvents = false;
        }

        private void LoadConfigurationByName(string? configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                return;
            }

            try
            {
                currentConfig = LoggingConfigFileService.LoadEditableConfigFromName(configName);
                currentFilePath = LoggingConfigFileService.TryGetConfigPath(configName);
                BindConfigurationToUi();
                SetDirty(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load configuration '{configName}': {ex.Message}", "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BindConfigurationToUi()
        {
            suppressDirtyTracking = true;

            configNameTextBox.Text = currentConfig.Name;
            configDescriptionTextBox.Text = currentConfig.Description;
            filePathValueLabel.Text = currentFilePath ?? "Unsaved";

            RebuildEcuList();

            suppressDirtyTracking = false;
            UpdateTitleState();
            UpdateButtonState();
        }

        private void RebuildEcuList(int preferredIndex = 0)
        {
            suppressSelectionEvents = true;
            ecuListBox.Items.Clear();

            foreach (var group in currentConfig.Ecus ?? Enumerable.Empty<ECUGroupJson>())
            {
                ecuListBox.Items.Add(FormatEcuListItem(group));
            }

            if (ecuListBox.Items.Count > 0)
            {
                ecuListBox.SelectedIndex = Math.Clamp(preferredIndex, 0, ecuListBox.Items.Count - 1);
            }
            else
            {
                currentEcuIndex = -1;
                requestRows.Clear();
                ecuNameTextBox.Clear();
                requestIdTextBox.Clear();
                responseIdTextBox.Clear();
            }

            suppressSelectionEvents = false;

            if (ecuListBox.Items.Count > 0)
            {
                LoadEcuIntoEditor(ecuListBox.SelectedIndex);
            }

            UpdateButtonState();
        }

        private void LoadEcuIntoEditor(int index)
        {
            if (currentConfig.Ecus == null || index < 0 || index >= currentConfig.Ecus.Count)
            {
                currentEcuIndex = -1;
                return;
            }

            currentEcuIndex = index;
            var selectedGroup = currentConfig.Ecus[index];

            suppressDirtyTracking = true;
            ecuNameTextBox.Text = selectedGroup.Name;
            requestIdTextBox.Text = FormatUInt(selectedGroup.RequestId);
            responseIdTextBox.Text = FormatUInt(selectedGroup.ResponseId);

            requestRows.RaiseListChangedEvents = false;
            requestRows.Clear();
            foreach (var request in selectedGroup.Requests)
            {
                requestRows.Add(EditableRequestRow.FromRequest(request));
            }
            requestRows.RaiseListChangedEvents = true;
            requestRows.ResetBindings();
            suppressDirtyTracking = false;

            UpdateButtonState();
        }

        private void BeginNewConfiguration()
        {
            currentConfig = LoggingConfigFileService.CreateDefaultConfig();
            currentFilePath = null;
            BindConfigurationToUi();
            SetDirty(false);
        }

        private bool ConfirmDiscardUnsavedChanges()
        {
            if (!isDirty)
            {
                return true;
            }

            var result = MessageBox.Show(
                "You have unsaved logging config changes. Discard them?",
                "Unsaved Changes",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            return result == DialogResult.Yes;
        }

        private void RefreshConfigsButton_Click(object? sender, EventArgs e)
        {
            LoadAvailableConfigurations();
        }

        private void LoadConfigButton_Click(object? sender, EventArgs e)
        {
            if (suppressSelectionEvents)
            {
                return;
            }

            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            LoadConfigurationByName(configPickerComboBox.SelectedItem?.ToString());
        }

        private void NewConfigButton_Click(object? sender, EventArgs e)
        {
            if (!ConfirmDiscardUnsavedChanges())
            {
                return;
            }

            BeginNewConfiguration();
        }

        private void SaveConfigButton_Click(object? sender, EventArgs e)
        {
            SaveCurrentConfiguration(saveAs: false);
        }

        private void SaveAsConfigButton_Click(object? sender, EventArgs e)
        {
            SaveCurrentConfiguration(saveAs: true);
        }

        private void SaveCurrentConfiguration(bool saveAs)
        {
            requestsDataGridView.EndEdit();

            if (!PersistSelectedEcuEdits(out var validationError))
            {
                MessageBox.Show(validationError, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            currentConfig.Name = configNameTextBox.Text.Trim();
            currentConfig.Description = configDescriptionTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(currentConfig.Name))
            {
                MessageBox.Show("Please enter a configuration name before saving.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string? filePath = currentFilePath;
            if (saveAs || string.IsNullOrWhiteSpace(filePath))
            {
                filePath = PromptForSavePath();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return;
                }
            }

            try
            {
                LoggingConfigFileService.SaveEditableConfig(currentConfig, filePath);
                currentFilePath = filePath;

                var savedConfigName = Path.GetFileNameWithoutExtension(filePath);
                LoadAvailableConfigurations(savedConfigName);
                filePathValueLabel.Text = filePath;
                SetDirty(false);
                ConfigurationSaved?.Invoke(savedConfigName);

                MessageBox.Show($"Saved logging configuration to:\n{filePath}", "Configuration Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string? PromptForSavePath()
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                DefaultExt = "json",
                AddExtension = true,
                InitialDirectory = LoggingConfigFileService.GetPreferredConfigDirectory(),
                FileName = BuildSuggestedFileName()
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return null;
            }

            var selectedPath = Path.GetFullPath(dialog.FileName);
            var configDirectory = Path.GetFullPath(LoggingConfigFileService.GetPreferredConfigDirectory());
            var selectedDirectory = Path.GetDirectoryName(selectedPath);
            var isInsideConfigDirectory = !string.IsNullOrWhiteSpace(selectedDirectory)
                && (string.Equals(Path.GetFullPath(selectedDirectory), configDirectory, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFullPath(selectedDirectory).StartsWith($"{configDirectory}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

            if (!isInsideConfigDirectory)
            {
                MessageBox.Show(
                    $"Please save logging configs inside:\n{configDirectory}",
                    "Save Location Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            return selectedPath;
        }

        private string BuildSuggestedFileName()
        {
            var nameSource = string.IsNullOrWhiteSpace(configNameTextBox.Text)
                ? "custom-logging-config"
                : configNameTextBox.Text.Trim().ToLowerInvariant();

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(nameSource
                .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
                .ToArray());

            sanitized = string.Join("-", sanitized
                .Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries));

            return string.IsNullOrWhiteSpace(sanitized) ? "custom-logging-config.json" : $"{sanitized}.json";
        }

        private void EditorField_TextChanged(object? sender, EventArgs e)
        {
            SetDirty();
        }

        private void AddEcuButton_Click(object? sender, EventArgs e)
        {
            if (!PersistSelectedEcuEdits(out var error))
            {
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            currentConfig.Ecus ??= new List<ECUGroupJson>();
            currentConfig.Ecus.Add(new ECUGroupJson
            {
                Name = $"ECU {currentConfig.Ecus.Count + 1}",
                RequestId = ECUDefinition.ECM.RequestId,
                ResponseId = ECUDefinition.ECM.ResponseId,
                Requests = new List<OBDRequestJson>()
            });

            RebuildEcuList(currentConfig.Ecus.Count - 1);
            SetDirty();
        }

        private void RemoveEcuButton_Click(object? sender, EventArgs e)
        {
            if (currentConfig.Ecus == null || currentEcuIndex < 0 || currentEcuIndex >= currentConfig.Ecus.Count)
            {
                return;
            }

            if (currentConfig.Ecus.Count == 1)
            {
                MessageBox.Show("A logging configuration needs at least one ECU.", "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            currentConfig.Ecus.RemoveAt(currentEcuIndex);
            RebuildEcuList(Math.Max(0, currentEcuIndex - 1));
            SetDirty();
        }

        private void EcuListBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (suppressSelectionEvents)
            {
                return;
            }

            var requestedIndex = ecuListBox.SelectedIndex;
            if (requestedIndex == currentEcuIndex)
            {
                return;
            }

            if (!PersistSelectedEcuEdits(out var error))
            {
                suppressSelectionEvents = true;
                ecuListBox.SelectedIndex = currentEcuIndex;
                suppressSelectionEvents = false;
                MessageBox.Show(error, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (requestedIndex >= 0)
            {
                LoadEcuIntoEditor(requestedIndex);
            }
        }

        private bool PersistSelectedEcuEdits(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (currentConfig.Ecus == null || currentEcuIndex < 0 || currentEcuIndex >= currentConfig.Ecus.Count)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(ecuNameTextBox.Text))
            {
                errorMessage = "ECU name cannot be empty.";
                return false;
            }

            if (!TryParseUIntValue(requestIdTextBox.Text, out var requestId))
            {
                errorMessage = "Request ID must be a valid decimal or hex value like 0x7E0.";
                return false;
            }

            if (!TryParseUIntValue(responseIdTextBox.Text, out var responseId))
            {
                errorMessage = "Response ID must be a valid decimal or hex value like 0x7E8.";
                return false;
            }

            var convertedRequests = new List<OBDRequestJson>();
            for (int i = 0; i < requestRows.Count; i++)
            {
                var row = requestRows[i];
                if (!TryConvertRowToRequest(row, out var request, out errorMessage))
                {
                    errorMessage = $"Request {i + 1}: {errorMessage}";
                    return false;
                }

                convertedRequests.Add(request);
            }

            convertedRequests = OrderRequestsWithMode22First(convertedRequests);

            var targetGroup = currentConfig.Ecus[currentEcuIndex];
            targetGroup.Name = ecuNameTextBox.Text.Trim();
            targetGroup.RequestId = requestId;
            targetGroup.ResponseId = responseId;
            targetGroup.Requests = convertedRequests;

            suppressSelectionEvents = true;
            ecuListBox.Items[currentEcuIndex] = FormatEcuListItem(targetGroup);
            suppressSelectionEvents = false;
            ReloadOrderedRequestsIntoGrid(convertedRequests);
            return true;
        }

        private bool TryConvertRowToRequest(EditableRequestRow row, out OBDRequestJson request, out string errorMessage)
        {
            request = new OBDRequestJson();
            errorMessage = string.Empty;

            var normalizedType = row.Type?.Trim();
            if (!string.Equals(normalizedType, "Mode01", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedType, "Mode22", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Type must be Mode01 or Mode22.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.Name))
            {
                errorMessage = "Request name cannot be empty.";
                return false;
            }

            request.Type = normalizedType;
            request.Name = row.Name.Trim();
            request.Description = row.Description?.Trim() ?? string.Empty;
            request.Category = row.Category?.Trim() ?? string.Empty;
            request.Unit = row.Unit?.Trim() ?? string.Empty;

            if (string.Equals(normalizedType, "Mode01", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseByteList(row.PidsText, out var pids))
                {
                    errorMessage = "Mode01 requests need one or more PIDs separated by commas.";
                    return false;
                }

                request.Pids = pids;
                request.PidHigh = null;
                request.PidLow = null;
                return true;
            }

            if (!TryParseByteValue(row.PidHighText, out var pidHigh))
            {
                errorMessage = "PID High must be a valid byte value.";
                return false;
            }

            if (!TryParseByteValue(row.PidLowText, out var pidLow))
            {
                errorMessage = "PID Low must be a valid byte value.";
                return false;
            }

            request.Pids = null;
            request.PidHigh = pidHigh;
            request.PidLow = pidLow;
            return true;
        }

        private void AddMode01Button_Click(object? sender, EventArgs e)
        {
            requestRows.Add(new EditableRequestRow
            {
                Type = "Mode01",
                Name = "New Mode01 Request",
                PidsText = "0x0C"
            });
            SetDirty();
        }

        private void AddMode22Button_Click(object? sender, EventArgs e)
        {
            requestRows.Add(new EditableRequestRow
            {
                Type = "Mode22",
                Name = "New Mode22 Request",
                PidHighText = "0x00",
                PidLowText = "0x00"
            });
            SetDirty();
        }

        private void RemoveRequestButton_Click(object? sender, EventArgs e)
        {
            if (requestsDataGridView.CurrentRow?.DataBoundItem is not EditableRequestRow requestRow)
            {
                return;
            }

            requestRows.Remove(requestRow);
            SetDirty();
        }

        private void MoveRequestUpButton_Click(object? sender, EventArgs e)
        {
            MoveSelectedRequest(-1);
        }

        private void MoveRequestDownButton_Click(object? sender, EventArgs e)
        {
            MoveSelectedRequest(1);
        }

        private void MoveSelectedRequest(int direction)
        {
            if (requestsDataGridView.CurrentRow?.DataBoundItem is not EditableRequestRow requestRow)
            {
                return;
            }

            var currentIndex = requestRows.IndexOf(requestRow);
            if (currentIndex < 0)
            {
                return;
            }

            var newIndex = currentIndex + direction;
            if (newIndex < 0 || newIndex >= requestRows.Count)
            {
                return;
            }

            requestRows.RemoveAt(currentIndex);
            requestRows.Insert(newIndex, requestRow);
            requestsDataGridView.ClearSelection();
            requestsDataGridView.Rows[newIndex].Selected = true;
            SetDirty();
        }

        private void RequestsDataGridView_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (requestsDataGridView.IsCurrentCellDirty)
            {
                requestsDataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void RequestsDataGridView_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                SetDirty();
            }
        }

        private void RequestsDataGridView_UserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
        {
            SetDirty();
        }

        private void SetDirty(bool dirty = true)
        {
            if (suppressDirtyTracking)
            {
                return;
            }

            isDirty = dirty;
            UpdateTitleState();
            UpdateButtonState();
        }

        private void UpdateTitleState()
        {
            metadataGroupBox.Text = isDirty ? "Configuration Details *" : "Configuration Details";
        }

        private void UpdateButtonState()
        {
            var hasEcuSelection = currentEcuIndex >= 0;
            removeEcuButton.Enabled = (currentConfig.Ecus?.Count ?? 0) > 1;
            addMode01Button.Enabled = hasEcuSelection;
            addMode22Button.Enabled = hasEcuSelection;
            removeRequestButton.Enabled = hasEcuSelection && requestRows.Count > 0;
            moveRequestUpButton.Enabled = hasEcuSelection && requestRows.Count > 1;
            moveRequestDownButton.Enabled = hasEcuSelection && requestRows.Count > 1;
            saveConfigButton.Enabled = (currentConfig.Ecus?.Count ?? 0) > 0;
            saveAsConfigButton.Enabled = saveConfigButton.Enabled;
        }

        private static string FormatEcuListItem(ECUGroupJson group)
        {
            return $"{group.Name} [{FormatUInt(group.RequestId)}]";
        }

        private void ReloadOrderedRequestsIntoGrid(IReadOnlyCollection<OBDRequestJson> orderedRequests)
        {
            suppressDirtyTracking = true;
            requestRows.RaiseListChangedEvents = false;
            requestRows.Clear();
            foreach (var request in orderedRequests)
            {
                requestRows.Add(EditableRequestRow.FromRequest(request));
            }
            requestRows.RaiseListChangedEvents = true;
            requestRows.ResetBindings();
            suppressDirtyTracking = false;
            UpdateButtonState();
        }

        private static List<OBDRequestJson> OrderRequestsWithMode22First(IEnumerable<OBDRequestJson> requests)
        {
            return requests
                .Select((request, index) => new { request, index })
                .OrderBy(item => IsMode22(item.request) ? 0 : 1)
                .ThenBy(item => item.index)
                .Select(item => item.request)
                .ToList();
        }

        private static bool IsMode22(OBDRequestJson request)
        {
            return string.Equals(request.Type, "Mode22", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatUInt(uint value)
        {
            return $"0x{value:X}";
        }

        private static string FormatByte(byte value)
        {
            return $"0x{value:X2}";
        }

        private static bool TryParseUIntValue(string? text, out uint value)
        {
            value = 0;

            var normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(normalized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }

            if (uint.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return uint.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseByteValue(string? text, out byte value)
        {
            value = 0;

            if (!TryParseUIntValue(text, out var parsedValue) || parsedValue > byte.MaxValue)
            {
                return false;
            }

            value = (byte)parsedValue;
            return true;
        }

        private static bool TryParseByteList(string? text, out byte[] values)
        {
            values = Array.Empty<byte>();
            var tokens = (text ?? string.Empty)
                .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
            {
                return false;
            }

            var parsedValues = new List<byte>();
            foreach (var token in tokens)
            {
                if (!TryParseByteValue(token, out var value))
                {
                    return false;
                }

                parsedValues.Add(value);
            }

            values = parsedValues.ToArray();
            return true;
        }

        private sealed class EditableRequestRow
        {
            public string Type { get; set; } = "Mode22";
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string Unit { get; set; } = string.Empty;
            public string PidsText { get; set; } = string.Empty;
            public string PidHighText { get; set; } = string.Empty;
            public string PidLowText { get; set; } = string.Empty;

            public static EditableRequestRow FromRequest(OBDRequestJson request)
            {
                return new EditableRequestRow
                {
                    Type = string.IsNullOrWhiteSpace(request.Type) ? "Mode22" : request.Type,
                    Name = request.Name,
                    Description = request.Description,
                    Category = request.Category,
                    Unit = request.Unit,
                    PidsText = request.Pids == null ? string.Empty : string.Join(", ", request.Pids.Select(FormatByte)),
                    PidHighText = request.PidHigh.HasValue ? FormatByte(request.PidHigh.Value) : string.Empty,
                    PidLowText = request.PidLow.HasValue ? FormatByte(request.PidLow.Value) : string.Empty
                };
            }
        }
    }
}
