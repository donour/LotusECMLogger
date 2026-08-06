using System.Text;

namespace LotusECMLogger
{
    public partial class HelpDialog : Form
    {
        private readonly TreeView navigationTree;
        private readonly RichTextBox contentBox;

        public HelpDialog()
        {
            InitializeComponent();

            // Set form properties
            Text = "LotusECMLogger - User Guide";
            Size = new Size(900, 600);
            MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterParent;

            // Create split container. SplitterDistance is assigned after the panels are populated
            // and the container is docked: setting it in the initializer applies it against the
            // control's default 150 px width, where it is clamped and then re-proportioned on the
            // first layout — which left the navigation tree occupying most of the dialog.
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.Fixed3D
            };

            // Navigation tree
            navigationTree = new TreeView
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10F)
            };
            navigationTree.AfterSelect += NavigationTree_AfterSelect;

            // Content box
            contentBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Segoe UI", 10F),
                Padding = new Padding(10),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            splitContainer.Panel1.Controls.Add(navigationTree);
            splitContainer.Panel2.Controls.Add(contentBox);
            Controls.Add(splitContainer);

            // Keep the navigation pane at a fixed width so resizing the dialog grows the content
            // pane, which is where the reading happens.
            splitContainer.SplitterDistance = 240;
            splitContainer.FixedPanel = FixedPanel.Panel1;
            splitContainer.Panel1MinSize = 180;

            // Populate navigation
            PopulateNavigation();

            // Select first item
            if (navigationTree.Nodes.Count > 0)
            {
                navigationTree.SelectedNode = navigationTree.Nodes[0];
            }
        }

        private void PopulateNavigation()
        {
            navigationTree.Nodes.Clear();

            // Create navigation structure
            var overview = navigationTree.Nodes.Add("overview", "Overview");
            var gettingStarted = navigationTree.Nodes.Add("gettingstarted", "Getting Started");
            var features = navigationTree.Nodes.Add("features", "Features");

            // Add feature sub-nodes
            features.Nodes.Add("vehicleinfo", "Extended Vehicle Information");
            features.Nodes.Add("livedata", "Live Data Logging");
            features.Nodes.Add("highspeed", "High-Speed Logging");
            features.Nodes.Add("ecucoding", "ECU Coding");
            features.Nodes.Add("setvin", "Set VIN");
            features.Nodes.Add("dynomode", "Dyno Mode");
            features.Nodes.Add("dtc", "Diagnostic Trouble Codes");
            features.Nodes.Add("learneddata", "Learned Data Reset");

            // ABS/ESP is a separate module with four distinct procedure groups, so it gets an
            // overview page plus one page per sub-tab rather than a single long topic.
            var abs = features.Nodes.Add("absdiag", "ABS/ESP Diagnostics");
            abs.Nodes.Add("absmodule", "ABS Module Info & Faults");
            abs.Nodes.Add("abslivestate", "ABS Live Internal State");
            abs.Nodes.Add("abstelemetry", "ABS Wheel Speed Telemetry");
            abs.Nodes.Add("absactuation", "ABS Pump & Valve Routines");

            features.Nodes.Add("t6rma", "T6 RMA Logging");
            features.Nodes.Add("livetuning", "T6 Live Tuning");
            features.Nodes.Add("flasher", "T6E Calibration Flasher");
            features.Nodes.Add("erasemodel", "Erase Model Info");

            var adapters = navigationTree.Nodes.Add("adapters", "Supported Adapters");
            var troubleshooting = navigationTree.Nodes.Add("troubleshooting", "Troubleshooting");

            features.Expand();
        }

        private void NavigationTree_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node == null) return;

            contentBox.Clear();

            switch (e.Node.Name)
            {
                case "overview":
                    ShowOverview();
                    break;
                case "gettingstarted":
                    ShowGettingStarted();
                    break;
                case "vehicleinfo":
                    ShowVehicleInfoHelp();
                    break;
                case "livedata":
                    ShowLiveDataHelp();
                    break;
                case "highspeed":
                    ShowHighSpeedHelp();
                    break;
                case "ecucoding":
                    ShowEcuCodingHelp();
                    break;
                case "setvin":
                    ShowSetVinHelp();
                    break;
                case "dynomode":
                    ShowDynoModeHelp();
                    break;
                case "dtc":
                    ShowDtcHelp();
                    break;
                case "learneddata":
                    ShowLearnedDataHelp();
                    break;
                case "absdiag":
                    ShowAbsOverviewHelp();
                    break;
                case "absmodule":
                    ShowAbsModuleHelp();
                    break;
                case "abslivestate":
                    ShowAbsLiveStateHelp();
                    break;
                case "abstelemetry":
                    ShowAbsTelemetryHelp();
                    break;
                case "absactuation":
                    ShowAbsActuationHelp();
                    break;
                case "t6rma":
                    ShowT6RmaHelp();
                    break;
                case "livetuning":
                    ShowLiveTuningHelp();
                    break;
                case "flasher":
                    ShowFlasherHelp();
                    break;
                case "erasemodel":
                    ShowEraseModelHelp();
                    break;
                case "adapters":
                    ShowAdaptersHelp();
                    break;
                case "troubleshooting":
                    ShowTroubleshooting();
                    break;
                default:
                    ShowOverview();
                    break;
            }

            contentBox.SelectionStart = 0;
            contentBox.ScrollToCaret();
        }

        private void AddHeading(string text, int level = 1)
        {
            contentBox.SelectionFont = new Font("Segoe UI", level == 1 ? 14F : 12F, FontStyle.Bold);
            contentBox.SelectionColor = Color.FromArgb(0, 102, 204);
            contentBox.AppendText(text + "\n\n");
        }

        private void AddSubheading(string text)
        {
            contentBox.SelectionFont = new Font("Segoe UI", 11F, FontStyle.Bold);
            contentBox.SelectionColor = Color.FromArgb(51, 51, 51);
            contentBox.AppendText(text + "\n");
        }

        private void AddParagraph(string text)
        {
            contentBox.SelectionFont = new Font("Segoe UI", 10F);
            contentBox.SelectionColor = Color.Black;
            contentBox.AppendText(text + "\n\n");
        }

        private void AddBulletPoint(string text)
        {
            contentBox.SelectionFont = new Font("Segoe UI", 10F);
            contentBox.SelectionColor = Color.Black;
            contentBox.AppendText("  • " + text + "\n");
        }

        private void ShowOverview()
        {
            AddHeading("LotusECMLogger - Overview");

            AddParagraph("LotusECMLogger is a free, open-source logging tool designed specifically for Lotus sports cars. It supports both standard OBD-II Mode 01 and manufacturer-specific OBD-II Mode 22, enabling you to capture a wide range of engine and vehicle data.");

            AddParagraph("With LotusECMLogger, you can log not only generic OBD-II parameters, but also Lotus-specific data such as variable cam control, knock control, and other advanced diagnostics. This makes it an invaluable tool for enthusiasts, tuners, and anyone interested in monitoring or troubleshooting their Lotus vehicle.");

            AddSubheading("Key Features:");
            AddBulletPoint("Supports OBD-II Mode 01: Standard parameters like RPM, speed, coolant temperature, etc.");
            AddBulletPoint("Supports OBD-II Mode 22: Manufacturer-specific channels, including advanced Lotus data.");
            AddBulletPoint("Capture Lotus-specific data: Log unique parameters such as variable cam control, knock control, and more.");
            AddBulletPoint("High-Speed Channel Logging: Stream internal ECU channels over CAN at up to 100 Hz - far faster than OBD-II - for tuning and transient analysis (requires firmware with the channel-logger facility).");
            AddBulletPoint("ECU Coding: Read and modify ECU configuration settings for Lotus T6e ECUs.");
            AddBulletPoint("Extended Vehicle Information: Retrieve VIN, ECU details, and calibration data.");
            AddBulletPoint("Set VIN: Program a new VIN to the ECU using OBD-II Mode 0x3B.");
            AddBulletPoint("Dyno Mode: Enable the ECU's diagnostic override to inhibit faults from external systems (such as ABS) during dyno runs.");
            AddBulletPoint("Diagnostic Trouble Codes: Read and clear DTCs from the ECU.");
            AddBulletPoint("Learned Data Reset: Clear adaptive learning values from the ECU.");
            AddBulletPoint("ABS/ESP Diagnostics: Read fault codes, identification, and live internal state from the Bosch ESP8 ABS module; log all four wheel speeds at 100 Hz; and run the pump/valve routines used for brake bleeding.");
            AddBulletPoint("T6 RMA Logging: Advanced memory address logging for development.");
            AddBulletPoint("T6 Live Tuning: Edit calibration values in ECU RAM in real time by monitoring a calibration file on disk, or upload a whole calibration into RAM in one operation (requires an unlocked ECU).");
            AddBulletPoint("T6E Calibration Flasher: Flash calibration files to the ECU.");
            AddBulletPoint("Erase Model Info: Clear stored model info after a firmware migration so the new firmware activates (Tools menu).");
            AddBulletPoint("Free and open source: No cost, no restrictions, and community-driven development.");
        }

        private void ShowGettingStarted()
        {
            AddHeading("Getting Started");

            AddSubheading("Requirements:");
            AddBulletPoint("A J2534-compliant pass-thru device (e.g., Tactrix OpenPort 2.0)");
            AddBulletPoint("USB connection to your computer");
            AddBulletPoint("OBD-II connection to your Lotus vehicle");
            AddBulletPoint("Windows operating system");

            AddParagraph("");

            AddSubheading("Quick Start:");
            AddParagraph("1. Connect your J2534 device to your computer via USB and to your vehicle's OBD-II port.");
            AddParagraph("2. Launch LotusECMLogger.");
            AddParagraph("3. Select an OBD configuration from the 'Config' dropdown in the Live Data tab.");
            AddParagraph("4. Click 'Start' to begin logging data. Data will be saved to a CSV file in the Documents\\LotusECMLogger folder.");
            AddParagraph("5. Click 'Stop' when you're done logging.");

            AddSubheading("Navigation:");
            AddParagraph("The application uses a tabbed interface to organize different diagnostic and logging functions. Click on each tab to access different features:");
            AddBulletPoint("Vehicle Information - Read VIN and ECU details, program a new VIN, enable Dyno Mode, and reset learned adaptations");
            AddBulletPoint("Live Data - Real-time parameter logging, with a Logging Config sub-tab for editing OBD configurations");
            AddBulletPoint("High-Speed Log - High-rate CAN channel logging (requires firmware with the channel-logger facility)");
            AddBulletPoint("ECU Coding - Modify ECU configuration");
            AddBulletPoint("Diagnostic Trouble Codes - Read and clear fault codes");
            AddBulletPoint("T6 RMA Logging - Advanced memory logging");
            AddBulletPoint("Live Tuning - Real-time calibration editing on unlocked ECUs");
            AddBulletPoint("ABS - Diagnostics for the ABS/ESP module, which is a separate computer from the engine ECU with its own fault memory (Evora; see the ABS/ESP Diagnostics topic)");

            AddSubheading("Output Files:");
            AddParagraph("All loggers write their output beneath a single folder: Documents\\LotusECMLogger. Live Data logs are named LiveData_<timestamp>.csv, T6 RMA logs T6RMA_<timestamp>.csv, and high-speed logs HighSpeed_<timestamp>.csv. Live Tuning calibration files default to the LiveTuning subfolder. The folder is created automatically the first time a log is written.");

            AddParagraph("");
            AddParagraph("Some advanced, rarely-used operations live in the Tools menu rather than a tab:");
            AddBulletPoint("T6E Calibration Flasher - Flash a calibration file to the ECU");
            AddBulletPoint("Erase Model Info - Activate a newly flashed firmware version by clearing the stored model info");
        }

        private void ShowLiveDataHelp()
        {
            AddHeading("Live Data Logging");

            AddParagraph("The Live Data tab displays real-time OBD-II parameters from your Lotus vehicle in an easy-to-read list format. This is the primary feature for monitoring and logging vehicle data during driving or dyno testing.");

            AddSubheading("How to Use:");
            AddParagraph("1. Select Configuration: Choose an OBD configuration from the dropdown menu. Different configurations contain different sets of parameters tailored for specific purposes (e.g., general logging, performance tuning, diagnostics).");
            AddParagraph("2. Start Logging: Click the 'Start' button to begin data collection. The application will connect to your vehicle's ECU and start reading parameters.");
            AddParagraph("3. Monitor Data: Watch real-time values update in the list view. The refresh rate is displayed in the status bar at the bottom.");
            AddParagraph("4. Stop Logging: Click 'Stop' when finished. The data will be saved to a CSV file.");

            AddSubheading("Output Files:");
            AddParagraph("Log files are automatically saved to the Documents\\LotusECMLogger folder with timestamps in the filename (e.g., LiveData_20250210_143022.csv). These CSV files can be opened in Excel, Google Sheets, or specialized data analysis tools.");

            AddSubheading("Supported Parameters:");
            AddBulletPoint("Standard OBD-II Mode 01: Engine RPM, vehicle speed, coolant temperature, intake air temperature, throttle position, fuel trim values, oxygen sensor data, and more.");
            AddBulletPoint("Lotus-Specific Mode 22: Variable cam timing angles, knock control retard, requested vs actual torque, boost pressure, lambda values, and other manufacturer-specific parameters.");

            AddSubheading("Tips:");
            AddBulletPoint("Choose configurations appropriate for your needs - larger parameter sets require more processing time.");
            AddBulletPoint("The refresh rate shown in the status bar indicates how many times per second the display updates.");
            AddBulletPoint("Data is logged at a higher rate than displayed for accurate time-series capture.");
        }

        private void ShowHighSpeedHelp()
        {
            AddHeading("High-Speed Logging");

            AddParagraph("The High-Speed Log tab streams internal ECU channels directly over CAN at up to 100 Hz per channel - far faster than the OBD-II Live Data tab. Instead of polling the ECU with request/response messages, it configures the ECU as a programmable sampler that autonomously broadcasts the channels you select. This makes it possible to capture fast transients such as per-cylinder ignition advance and knock retard, throttle and pedal movement, AFR, MAF, load, and torque.");

            AddSubheading("Requirements (what is needed to enable it):");
            AddBulletPoint("J2534 device and OBD-II connection: The same hardware used elsewhere in the app. High-speed logging communicates over raw CAN at 500 kbit/s.");
            AddBulletPoint("Firmware with the channel-logger facility: Not every ECU/firmware includes the high-speed channel-logger. Standard locked production calibrations generally do not. Use the 'Test Connection' button (below) or the 'HS LOGGER' indicator on the Extended Vehicle Information tab to confirm the facility is present before relying on it.");
            AddBulletPoint("Diagnostic CAN bus enabled: The calibration setting CAL_ecu_flexcan_diag_bus_select must be non-zero. If the diagnostic bus is disabled, the ECU will not respond to the logger commands even on capable firmware.");
            AddBulletPoint("A symbol database for your firmware version: The app ships databases for the supported firmware versions (for example C132E0278 and B13200091) and uses them to resolve each channel's address, size, scaling, and unit. Presets and the 'Add Channels...' browser are populated from this database.");

            AddSubheading("Test Connection:");
            AddParagraph("Click 'Test Connection' before logging to verify the ECU supports high-speed logging. It opens a short session and sends an identify request, then reports one of:");
            AddBulletPoint("Connected - channel logger present (green): The facility is available and you can log.");
            AddBulletPoint("Diagnostic bus is alive, but the ECU did not answer / unexpected protocol (orange): The bus is reachable but this firmware does not provide the high-speed channel-logger.");
            AddBulletPoint("No response (red): The diagnostic bus is not reachable. Check that the diagnostic bus is enabled, and verify the CAN wiring and 500 kbit/s connection.");
            AddParagraph("The Extended Vehicle Information tab shows the same result as an 'HS LOGGER' indicator after you load vehicle data.");

            AddSubheading("How to Use:");
            AddParagraph("1. Open the High-Speed Log tab.");
            AddParagraph("2. (Recommended) Click 'Test Connection' to confirm the ECU supports high-speed logging.");
            AddParagraph("3. Select a preset for your firmware version from the dropdown, or click 'Add Channels...' to search the symbol database and pick channels yourself.");
            AddParagraph("4. In the channel grid, tick the channels to log and set each one's sample rate (Hz).");
            AddParagraph("5. Choose the CSV output file. A timestamped default in Documents\\LotusECMLogger is provided; use 'Browse' to change it.");
            AddParagraph("6. Click 'Start'. The app configures the ECU and begins streaming and logging.");
            AddParagraph("7. Click 'Stop' when finished.");

            AddSubheading("Channels and Presets:");
            AddBulletPoint("Channels are internal ECU memory locations ('Data Labels'). Presets are saved, named channel sets for a specific firmware version (for example: per-cylinder ignition advance and knock retard, TPS, accelerator pedal, AFR, MAF, load, IAT, MAP, and torque).");
            AddBulletPoint("Scaling and units are derived automatically from the firmware's symbol database. Because these come from reverse-engineered type information, sanity-check a channel against a known reading before relying on it.");
            AddBulletPoint("Supported per-channel rates are 1, 2, 5, 10, 20, 50, and 100 Hz.");
            AddBulletPoint("The ECU has a finite capacity (a limited number of channels, groups, and bytes per frame). If your selection exceeds it, 'Start' reports the problem so you can reduce channels or rates.");

            AddSubheading("Configuration Is Not Saved on the ECU:");
            AddParagraph("The channel program lives in the ECU's RAM and is wiped on every power cycle or reboot. The app automatically re-sends the full configuration each time you click 'Start', so nothing persists between sessions. This is normal and means you can safely power-cycle the car between runs.");

            AddSubheading("Status Panel:");
            AddBulletPoint("Frames: Total number of stream frames received in the current session.");
            AddBulletPoint("Last Update: Time of the most recently received frame.");
            AddBulletPoint("Dropped: Frames dropped because logging to disk fell behind. This should stay 0. If it turns red, the writer could not keep up - use a faster or local drive (avoid network/synced folders), or reduce the number of channels.");

            AddSubheading("Output Files:");
            AddParagraph("Data is saved to CSV with columns: Timestamp (microsecond wall-clock), RelativeTime_ms (derived from the adapter's hardware timestamp for accurate inter-frame timing), Label, then one column per logged channel. Files are written to Documents\\LotusECMLogger by default (e.g., HighSpeed_20250210_143022.csv).");

            AddSubheading("How It Differs from Live Data:");
            AddParagraph("Live Data uses OBD-II request/response (Mode 01/22) and works on any compatible ECU, but is limited by polling. High-Speed Logging streams internal channels at a fixed, hardware-timestamped rate and is far faster, but requires firmware that includes the channel-logger facility.");

            AddSubheading("Notes and Caution:");
            AddBulletPoint("While logging, the PC is an active node on the vehicle CAN bus and sends configuration commands to the ECU. Use 'Test Connection' first to confirm a healthy link.");
            AddBulletPoint("High-speed logging holds the J2534 device for itself; other operations that need the device (for example the ECU unlock probe on the Vehicle Information tab) are skipped while a session is active.");
        }

        private void ShowEcuCodingHelp()
        {
            AddHeading("ECU Coding");

            AddParagraph("The ECU Coding tab allows you to read and modify ECU configuration settings for Lotus T6 ECUs. These settings control various vehicle features and behaviors that are not accessible through standard OBD-II parameters.");

            AddSubheading("How to Use:");
            AddParagraph("1. Read Codes: Click 'Read Codes' to retrieve the current coding configuration from your ECU. The application will display all available options with their current values.");
            AddParagraph("2. Modify Settings: Adjust the dropdown menus and numeric values to change configuration options. Common options include traction control settings, launch control parameters, and various vehicle features.");
            AddParagraph("3. Save Changes: After making modifications, click 'Save Changes' to write the new configuration to the ECU.");
            AddParagraph("4. Reset: Click 'Reset' to discard your changes and revert to the original values read from the ECU.");

            AddSubheading("Safety Features:");
            AddBulletPoint("Automatic Backup: Before writing any changes, the application creates a timestamped backup file containing both original and modified configurations.");
            AddBulletPoint("Confirmation Dialog: You must confirm before writing changes to the ECU.");
            AddBulletPoint("Logger Interlock: ECU coding operations are disabled while data logging is active to prevent conflicts.");
            AddBulletPoint("Bitfield Display: The current coding bitfield value is displayed for reference and verification.");

            AddSubheading("Important Warnings:");
            AddParagraph("WARNING: Incorrect coding can cause vehicle malfunction or affect drivability. Only modify settings if you understand their purpose and impact. Always keep backup files in case you need to restore original settings.");
            AddParagraph("The coding changes are stored in the ECU's non-volatile memory and persist across power cycles.");
        }

        private void ShowVehicleInfoHelp()
        {
            AddHeading("Extended Vehicle Information");

            AddParagraph("The Extended Vehicle Information tab retrieves static vehicle data such as VIN, ECU name, calibration ID, and calibration verification numbers. This information is queried using OBD-II Mode 09 and provides essential identification data about your vehicle's ECU and configuration. It also probes and reports whether the ECU is unlocked, which determines whether advanced operations are available.");

            AddSubheading("How to Use:");
            AddParagraph("1. Load Data: Click 'Load Vehicle Data' to query all available information from your ECU.");
            AddParagraph("2. View Results: Information is displayed in a list format with parameter names, values, and units.");

            AddSubheading("Available Information:");
            AddBulletPoint("Vehicle Identification Number (VIN): The unique 17-character identifier for your vehicle.");
            AddBulletPoint("ECU Name: The internal name/designation of your engine control unit.");
            AddBulletPoint("Calibration ID: Identifies the software calibration loaded in the ECU.");
            AddBulletPoint("Calibration Verification Numbers (CVN): A checksum value used to verify calibration integrity.");
            AddBulletPoint("In-Use Performance Tracking: Emissions-related tracking data (if available).");

            AddSubheading("ECU Unlock Status:");
            AddParagraph("After loading vehicle data, the tab probes whether the ECU is unlocked and shows the result in a colored indicator. An unlocked ECU is required for advanced operations such as Erase Model Info, T6 RMA Logging, and T6 Live Tuning.");
            AddBulletPoint("ECU: UNLOCKED (green) - The ECU answered a raw-CAN memory-access (RMA) probe. Unlocked-only features are available.");
            AddBulletPoint("ECU: LOCKED (red) - Vehicle data loaded, so the ECU is reachable, but it did not answer the RMA probe. The ECU is running a standard/locked calibration and unlocked-only features will not work.");
            AddBulletPoint("ECU: UNKNOWN (gray) - The unlock state could not be determined. This happens when no vehicle data loaded at all (ECU not reachable), when the probe errored, or when a logging session is active (the probe needs its own CAN channel, so it is skipped while the logger holds the device).");
            AddParagraph("The indicator refreshes each time you click 'Load Vehicle Data'. If it reads UNKNOWN while logging is active, stop the logger and load vehicle data again for a definite result.");

            AddSubheading("Use Cases:");
            AddBulletPoint("Verify your VIN matches vehicle documentation.");
            AddBulletPoint("Identify which ECU calibration is currently installed.");
            AddBulletPoint("Compare calibration IDs before and after reflashing.");
            AddBulletPoint("Document your ECU configuration for records or troubleshooting.");

            AddSubheading("Set VIN:");
            AddParagraph("The 'Set VIN' button on this tab opens a dialog for programming a new VIN into the ECU. See the 'Set VIN' help topic for details.");

            AddSubheading("Dyno Mode:");
            AddParagraph("The 'Dyno Mode' button enables the ECU's diagnostic override for dyno runs. See the 'Dyno Mode' help topic for details.");

            AddSubheading("Adaptations Reset:");
            AddParagraph("The 'Adaptations Reset' button performs an OBD-II Mode 0x11 learned data reset. See the 'Learned Data Reset' help topic for details.");
        }

        private void ShowDynoModeHelp()
        {
            AddHeading("Dyno Mode");

            AddParagraph("The 'Dyno Mode' button on the Vehicle Information tab enables the ECU's diagnostic override, commonly known as dyno mode. While active, the ECU inhibits fault reactions triggered by external systems such as ABS - useful on a chassis dyno, where the driven and undriven wheels turning at different speeds would otherwise raise faults and trigger torque intervention.");

            AddSubheading("How to Use:");
            AddParagraph("1. Stop any active logging session (the button is disabled while the logger is running).");
            AddParagraph("2. Click 'Dyno Mode' on the Vehicle Information tab.");
            AddParagraph("3. Confirm the warning dialog. The application sends the enable request and reports success or failure.");

            AddSubheading("How It Works:");
            AddParagraph("The application sends an OBD-II Mode 0x2F (output control) request for PID 0x0170. The request is sent several times and success is confirmed by the ECU's positive response.");

            AddSubheading("Important Notes:");
            AddBulletPoint("Dyno mode is not persistent: it is cleared when the vehicle is powered off. There is no explicit disable command - cycle the ignition to return to normal operation.");
            AddBulletPoint("Only enable dyno mode on a dyno or for controlled testing. Suppressing faults from systems such as ABS on the road removes safety interventions.");
            AddBulletPoint("The button is unavailable while data logging is active; stop the logger first.");
        }

        private void ShowSetVinHelp()
        {
            AddHeading("Set VIN");

            AddParagraph("The Set VIN dialog programs a new Vehicle Identification Number into the ECU using OBD-II Mode 0x3B. Open it from the 'Set VIN' button on the Extended Vehicle Information tab.");

            AddSubheading("What Can Be Changed:");
            AddParagraph("The Lotus firmware only allows positions 4–17 of the VIN to be rewritten. The first 3 characters (the WMI, World Manufacturer Identifier) are fixed at 'SCC' for Lotus and cannot be changed by this protocol. The dialog shows the WMI as a read-only field for reference and accepts the remaining 14 characters as editable input.");

            AddSubheading("VIN Format Requirements:");
            AddBulletPoint("Exactly 14 characters in the editable portion (17 total including the fixed WMI)");
            AddBulletPoint("Letters A–Z, excluding I, O, and Q (to avoid confusion with 1 and 0)");
            AddBulletPoint("Digits 0–9");
            AddBulletPoint("No spaces or punctuation");
            AddParagraph("Validation runs as you type. The Program button is disabled until the entry passes all checks; the status line below the input pinpoints the first offending character when the entry is invalid.");

            AddSubheading("How to Use:");
            AddParagraph("1. Click 'Load Vehicle Data' on the Extended Vehicle Information tab if you want the current VIN pre-populated.");
            AddParagraph("2. Click 'Set VIN' to open the programming dialog.");
            AddParagraph("3. Edit the 14-character remainder field. The WMI ('SCC') is shown read-only.");
            AddParagraph("4. Click 'Program' and confirm the warning dialog.");
            AddParagraph("5. Wait for the success message. The Extended Vehicle Information tab will automatically reload to show the new VIN.");

            AddSubheading("Engine Must Be Off:");
            AddParagraph("The Lotus firmware silently discards Mode 0x3B writes while the engine is running — the ECU still acknowledges every chunk with a positive response, but no bytes reach EEPROM. Stop the engine (ignition on, engine off) before programming.");
            AddParagraph("After programming completes, the application reads the VIN back via Mode 09 PID 02 and compares it to the request. If positions 4–17 do not match, the operation is reported as failed with the actual VIN read back from the ECU.");

            AddSubheading("Protocol Details:");
            AddParagraph("Mode 0x3B writes the VIN in four chunks, each carrying part of the 14 writable bytes:");
            AddBulletPoint("Sub-function 0x01: positions 4–7");
            AddBulletPoint("Sub-function 0x02: positions 8–11");
            AddBulletPoint("Sub-function 0x03: positions 12–15");
            AddBulletPoint("Sub-function 0x04: positions 16–17");
            AddParagraph("The firmware stages each chunk in RAM and commits the new VIN to EEPROM only after all four chunks have been received. The change persists across power cycles.");

            AddSubheading("Important Warnings:");
            AddBulletPoint("Lotus firmware checks for acceptable VINs — values it does not recognize as valid for the vehicle generation may affect features that depend on VIN-derived configuration (gear ratios, model detection, etc.).");
            AddBulletPoint("The change is written to ECU EEPROM and persists across power cycles. It is reversible only by programming the original VIN back.");
            AddBulletPoint("VIN programming is disabled while data logging is active. Stop the logger before using this feature.");
            AddBulletPoint("The WMI ('SCC') is enforced by the firmware. Any attempt to send a different WMI is ignored — the existing manufacturer code is preserved by the protocol.");
        }

        private void ShowDtcHelp()
        {
            AddHeading("Diagnostic Trouble Codes");

            AddParagraph("The Diagnostic Trouble Codes (DTC) tab provides functionality for reading and clearing diagnostic trouble codes from the ECU. This feature helps you diagnose issues and monitor fault codes stored in your vehicle's engine management system.");

            AddSubheading("How to Use:");
            AddParagraph("1. Read Codes: Click 'Read Codes' to retrieve stored (Mode 03) and permanent (Mode 0A) trouble codes from the ECU.");
            AddParagraph("2. View Details: Each code is displayed with its category and type (stored or permanent).");
            AddParagraph("3. Clear Codes: After addressing the underlying issues, click 'Clear Codes' to erase stored fault codes from the ECU memory.");

            AddSubheading("Understanding DTCs:");
            AddParagraph("Diagnostic trouble codes are alphanumeric codes that identify specific faults detected by the ECU. They follow a standard format:");
            AddBulletPoint("P-codes: Powertrain (engine and transmission)");
            AddBulletPoint("C-codes: Chassis (ABS, steering)");
            AddBulletPoint("B-codes: Body (airbags, climate control)");
            AddBulletPoint("U-codes: Network communication");

            AddSubheading("Important Notes:");
            AddParagraph("Clearing codes does not fix the underlying problem - it only erases the stored fault memory. If the problem persists, codes will return after driving the vehicle.");
            AddParagraph("Clearing codes (OBD-II Mode 04) also erases freeze frame data and resets readiness monitors to 'not ready'. Record any freeze frame information you need before clearing.");
            AddParagraph("Permanent codes cannot be cleared with 'Clear Codes'. The ECU erases them on its own once the fault stays absent for the required drive cycles.");
            AddParagraph("Some codes may require multiple drive cycles to reset monitoring readiness flags.");
        }

        private void ShowLearnedDataHelp()
        {
            AddHeading("Learned Data Reset");

            AddParagraph("The 'Adaptations Reset' button on the Vehicle Information tab performs an OBD-II Mode 0x11 reset to clear learned parameters from the ECU. This operation resets adaptive learning values, which may be necessary after certain repairs or modifications, though the ECU will need time to relearn optimal settings afterward.");

            AddSubheading("What is Learned Data?");
            AddParagraph("The ECU continuously adapts to your engine, fuel, and component wear by adjusting various parameters as you drive. On the Lotus T6 these adaptations are stored as 'learned values' in the ECU's EEPROM (protected by a checksum) so they persist across power cycles. They include:");
            AddBulletPoint("Octane scalers (per cylinder): Knock-based octane learning - one value per cylinder tracking how much knock-based fuel/timing correction has been accumulated. These are also shown on the Extended Vehicle Information tab.");
            AddBulletPoint("Knock retard learning: Learned ignition timing retard derived from knock sensor activity.");
            AddBulletPoint("Throttle tip-in / alpha-N load trim: A learned correction to the throttle-angle-and-speed (alpha-N) airflow model across RPM and throttle position, used when estimating engine load from throttle position. A reset re-seeds this to the base calibration.");
            AddBulletPoint("Torque-to-throttle (TPS) scaling: A learned mapping from requested torque to throttle position across RPM and load. A reset re-seeds this to the base calibration.");
            AddBulletPoint("Fuel trim learning (per bank): Long-term fuel adaptation, including learned lean-time and fuel-trim zones for each cylinder bank.");
            AddBulletPoint("Idle learning: Adaptive idle control for warm and cold conditions, including separate adaptation for when the A/C is on.");

            AddSubheading("When to Use:");
            AddParagraph("You may want to reset learned data after:");
            AddBulletPoint("Replacing the battery or ECU");
            AddBulletPoint("Major engine repairs or modifications");
            AddBulletPoint("Installing new sensors or fuel system components");
            AddBulletPoint("Experiencing persistent drivability issues");

            AddSubheading("How to Use:");
            AddParagraph("1. On the Vehicle Information tab, click 'Adaptations Reset' to initiate the learned data reset procedure.");
            AddParagraph("2. Confirm the operation in the warning dialog.");
            AddParagraph("3. Wait for confirmation that the reset was successful.");

            AddSubheading("After Reset:");
            AddParagraph("Following a learned data reset, your vehicle may experience slightly rough idle or hesitation until the ECU relearns optimal parameters. This is normal and typically resolves after 10-20 minutes of driving under various conditions.");

            AddSubheading("Warning:");
            AddParagraph("This operation cannot be reversed. The ECU will need time to relearn and may affect drivability temporarily.");
        }

        private void ShowAbsOverviewHelp()
        {
            AddHeading("ABS/ESP Diagnostics");

            AddParagraph("The ABS tab talks to the Bosch ESP8 ABS/ESP module - the brake and stability controller. This is a completely separate computer from the engine ECU that the rest of the application works with: it has its own fault memory, its own configuration, its own internal state, and it controls the hydraulic pump and solenoid valves in the ABS modulator.");

            AddParagraph("Because it is a different module from a different supplier, it speaks a different diagnostic language (KWP2000) on different CAN addresses than the engine ECU. None of the engine tabs can see ABS data, and nothing on this tab affects the engine ECU.");

            AddSubheading("What You Can Do Here:");
            AddBulletPoint("Module Info & Faults - Read ABS fault codes, the module's part numbers and serial, and its configuration records. Start here when the ABS or ESP warning lamp is on.");
            AddBulletPoint("Live Internal State - Read the controller's internal working values: its road-grip estimate, brake torque-vectoring pressures, valve positions, and hydraulic pressures.");
            AddBulletPoint("Wheel Speed Telemetry - Watch all four wheel speeds, vehicle speed, the brake switch, and the ESP/ABS intervention flags live at 100 Hz, with optional CSV logging.");
            AddBulletPoint("Pump & Valve Routines - Drive the ABS pump and valves for brake bleeding and hydraulic testing.");

            AddSubheading("Read-Only vs. Active Operations:");
            AddParagraph("Three of the four sub-tabs are strictly read-only - they ask the module questions and display the answers. The fourth, Pump & Valve Routines, physically operates the hydraulic unit and moves brake fluid. It is clearly marked, requires confirmation, and checks that the car is stationary before it will run.");

            AddParagraph("No operation on this tab changes anything the module stores permanently. There is deliberately no way to recode the module's variant configuration, write to its memory, or clear its fault codes from this application. Fault codes must be cleared with a tester that supports it, and only after the underlying fault is fixed.");

            AddSubheading("Wheel Speeds Without Any Risk:");
            AddParagraph("The Wheel Speed Telemetry sub-tab is worth knowing about even if you never use the rest. The ABS module continuously broadcasts wheel speeds on the CAN bus whether anyone is listening or not, so reading them involves transmitting nothing at all. That makes it completely safe to run while driving, and it is the best way to capture per-wheel data for diagnosing a wheel speed sensor, checking for a dragging brake, or logging a track session.");

            AddSubheading("Connection Requirements:");
            AddBulletPoint("The same J2534 device and OBD-II connection used by every other tab.");
            AddBulletPoint("Ignition ON. With the ignition off the module is asleep and will not answer or broadcast anything.");
            AddBulletPoint("Logging stopped. Every ABS operation needs the J2534 device to itself, so the buttons are disabled while a logging session is running.");

            AddSubheading("Vehicle Coverage:");
            AddParagraph("This support was developed against the Bosch ESP8.1 module fitted to the Lotus Evora (Bosch part number 0265951336, Lotus part number B132J0142). Other Lotus models use different ABS modules. The reads are harmless to attempt on any car - an unsupported module simply does not answer - but the decoded values and the actuation routines are specific to this module and should not be trusted elsewhere.");

            AddSubheading("Where Results Are Saved:");
            AddParagraph("Results are shown in the grid and also written to Documents\\LotusECMLogger\\abs-diagnostics.txt, overwritten on each run. The grid cannot be copy-pasted, so use that file when you want to save or share results. Telemetry logs and bus sniffs are written to their own files, described in their respective topics.");
        }

        private void ShowAbsModuleHelp()
        {
            AddHeading("ABS Module Info & Faults");

            AddParagraph("The first ABS sub-tab covers identification and fault reading, plus two tools for diagnosing the CAN connection to the module itself. All four operations are read-only.");

            AddSubheading("Read DTCs - ABS Fault Codes:");
            AddParagraph("Reads the fault codes stored in the ABS module's own fault memory. This is the single most useful button on the tab and the first thing to try when a warning lamp appears.");
            AddParagraph("Why you would use it: the ABS and ESP warning lamps tell you something is wrong but not what. The engine ECU's Diagnostic Trouble Codes tab cannot see ABS faults - they live in a different module. This reads them directly. Typical causes are a failed wheel speed sensor, a damaged sensor harness, a low brake fluid level, or a steering angle or yaw sensor fault.");
            AddParagraph("Each stored code is listed with its status byte expanded into readable flags - whether the fault is currently failing, whether it is confirmed or merely pending, whether it has failed since the memory was last cleared, and whether it is the one commanding the warning lamp. That last flag matters when several codes are stored: it tells you which one is actually lighting the dash.");
            AddParagraph("Codes are shown as raw hexadecimal values, for example 'DTC 0xC150'. No P/C/B/U letter is shown because the letter convention this particular firmware uses has not been confirmed, and printing a plausible-looking but wrong code would be worse than printing none. Look up the raw value in Lotus or Bosch service documentation for this module.");
            AddParagraph("This read needs no session setup and no security unlock - the module answers immediately - so it is quick and safe to run at any time with the car stationary and the ignition on.");

            AddSubheading("Read Info - Identification and Configuration:");
            AddParagraph("Reads the module's identity and its configuration records: serial number, the Lotus part number, the Bosch part number, and the configuration/coding bytes that tell the module which car it is fitted to.");
            AddParagraph("Why you would use it: to confirm which ABS module is actually installed before ordering a replacement, to check that a secondhand or replacement module matches your car, or to record your configuration before any work that might disturb it. It is also the fastest way to prove the diagnostic connection to the ABS is working end to end.");
            AddParagraph("The scan sweeps the module's identification and configuration record numbers and reports every one that returns data, so nothing is missed even where the published record numbering turned out not to match this firmware. Records are labelled where they have been identified and shown by record number where they have not.");
            AddParagraph("Single-byte configuration records are additionally decoded into their individual settings - axle and brake configuration, engine type, tyre size, market region, and ESP calibration selection. Treat that decode as a starting point rather than fact: the bit assignments are inferred from Bosch conventions and known differences between Evora variants, not confirmed from the firmware. The raw byte is always displayed alongside so you can interpret it yourself.");

            AddSubheading("Test Connection - Diagnostic Reachability:");
            AddParagraph("Sends harmless requests to the ABS and to other known addresses, and reports which modules answer. Nothing it sends changes any module's state.");
            AddParagraph("Why you would use it: when a read times out and you need to know whether the problem is the ABS module, the CAN bus, or the adapter. The report distinguishes the cases for you:");
            AddBulletPoint("If the bus shows no traffic at all, the adapter or the OBD connection is at fault, or the ignition is off.");
            AddBulletPoint("If the bus is alive and the engine ECU answers but the ABS does not, the bus and adapter are fine and the problem is specific to the ABS module or its diagnostic addressing.");
            AddBulletPoint("If the ABS telemetry line shows the module broadcasting, the ABS is powered and running even when it refuses diagnostic requests - which points at the addressing rather than at a dead module.");
            AddParagraph("The test finishes with a sweep across the standard diagnostic address range, reporting any module that answers. That is how the addresses this application uses for the ABS were originally narrowed down.");

            AddSubheading("Sniff Bus - Passive Address Discovery:");
            AddParagraph("Listens to the CAN bus for 40 seconds and transmits absolutely nothing. It first spends five seconds learning which addresses are broadcasting normally, then logs every frame that appears on any address that was not part of that background traffic.");
            AddParagraph("Why you would use it: to discover how another diagnostic tool talks to a module. Run it, and while it is capturing, use a commercial scan tool to read the ABS. The tool's conversation with the module stands out because those addresses are only active while it is talking. This is how the Evora's ABS diagnostic addresses were found after the published standard addresses turned out to be wrong.");
            AddParagraph("This is a developer and advanced-diagnostics tool. If you are only reading faults on a supported car you will never need it. Results are written to Documents\\LotusECMLogger\\abs-sniff.txt with a timestamped frame-by-frame log.");
        }

        private void ShowAbsLiveStateHelp()
        {
            AddHeading("ABS Live Internal State");

            AddParagraph("This sub-tab reads the ABS controller's internal working variables directly out of its memory - the values the stability control algorithms are computing right now. It is a read-only operation, but it reaches much deeper into the module than fault codes do.");

            AddParagraph("Click 'Read Live State' to take a snapshot. The application opens a diagnostic session, performs the module's security unlock, then reads each documented memory location and decodes it.");

            AddSubheading("What Gets Read:");
            AddBulletPoint("Road-surface grip estimate (mu) - The controller's live estimate of available tyre grip, from roughly 0.2 on ice to around 1.0 on dry tarmac. This single number drives much of the ABS and ESP behaviour, so seeing it is the clearest window into why the system is intervening as it does.");
            AddBulletPoint("EDC accumulators (left and right) - Brake torque-vectoring pressures. Electronic differential control brakes an individual inside wheel to help the car rotate. These values show how much brake pressure is being applied to each side for that purpose.");
            AddBulletPoint("Front and rear valve positions - The current state of the hydraulic solenoid valves, decoded into readable states: release (open), apply, apply-init, or hold (closed).");
            AddBulletPoint("Brake pressures, four channels - Hydraulic pressure in each brake circuit. Values below roughly 99 counts indicate an unpressurized circuit, which is flagged in the display.");
            AddBulletPoint("Variant coding byte - The module's vehicle configuration, decoded into its individual settings.");

            AddSubheading("Why You Would Use It:");
            AddBulletPoint("Diagnosing an ABS or ESP system that intervenes when it should not, or fails to intervene when it should. The grip estimate and intervention pressures show what the controller believes is happening.");
            AddBulletPoint("Confirming that the hydraulic unit responds during a bleed or a pressure test. The valve positions and pressures react in real time, which is the difference between assuming a routine worked and seeing that it did.");
            AddBulletPoint("Investigating brake torque vectoring behaviour, particularly on cars where it interacts with a driver-selectable drive mode.");
            AddBulletPoint("Reading the variant coding to confirm the module is configured for the right model variant.");

            AddSubheading("Reading the Results:");
            AddParagraph("Each row shows the decoded value, the memory address it came from, and the raw bytes returned. The raw bytes are always displayed so you can verify the interpretation yourself rather than trusting the decode blindly.");
            AddParagraph("Rows the module refuses to answer are reported individually as 'unavailable' with the reason, rather than failing the whole read. A partial result is still useful, and seeing exactly which locations were refused is itself diagnostic information.");
            AddParagraph("The summary row at the bottom reports how many locations were read successfully and which memory addressing format the module accepted.");

            AddSubheading("About the Security Unlock:");
            AddParagraph("Reading module memory requires a security unlock, which the application performs automatically using the module's published challenge-response algorithm. The unlock grants read access only. No operation in this application uses it to write anything, and the result of the unlock attempt is reported in the first rows of the results so you can see whether it succeeded.");

            AddSubheading("Accuracy Caveats:");
            AddParagraph("These memory locations and their interpretations come from firmware reverse engineering and have not all been confirmed against a running module. Two specific items are known to be uncertain:");
            AddBulletPoint("The mapping of the four pressure channels and two valve positions to specific wheels is inferred, not confirmed. To establish it yourself, run a single-wheel routine from the Pump & Valve sub-tab and watch which channel responds.");
            AddBulletPoint("The memory addressing format the module expects was documented ambiguously. The application tries the known candidates automatically and reports which one worked, so no configuration is needed either way.");
            AddParagraph("Treat the values as informative rather than authoritative, and sanity-check anything you intend to act on. This is a snapshot, not a continuous log - for continuous per-wheel data use the Wheel Speed Telemetry sub-tab instead.");
        }

        private void ShowAbsTelemetryHelp()
        {
            AddHeading("ABS Wheel Speed Telemetry");

            AddParagraph("The ABS module continuously broadcasts wheel speeds and status information on the CAN bus at 100 Hz, for the instrument cluster and engine ECU to use. This sub-tab decodes those broadcasts.");

            AddParagraph("The important consequence: reading this data requires transmitting nothing at all. The application only listens. There is no session to open, no security unlock, and no request that could confuse the module - so unlike almost every other diagnostic function in this application, this one is completely safe to run while driving.");

            AddSubheading("What Is Displayed:");
            AddBulletPoint("All four individual wheel speeds - left front, right front, left rear, right rear - each shown in km/h with the raw sensor count alongside.");
            AddBulletPoint("Vehicle speed as the ABS module calculates it, which is not simply one wheel's speed.");
            AddBulletPoint("Brake switch state - released, pressed, or faulty.");
            AddBulletPoint("ESP active - the stability control is currently intervening.");
            AddBulletPoint("ABS active - an anti-lock cycle is in progress.");
            AddBulletPoint("Torque reduction requested - the ABS is asking the engine to cut torque, used in the sportier drive modes.");
            AddBulletPoint("ESP warning lamp state.");
            AddBulletPoint("Frame counters and checksum verification for both wheel-speed messages, which confirm the data is arriving intact.");

            AddSubheading("Why You Would Use It:");
            AddBulletPoint("Diagnosing a wheel speed sensor fault. A failing sensor shows as a wheel reading zero, reading erratically, or dropping out at speed while the other three track together. This is far more direct than inferring it from a fault code.");
            AddBulletPoint("Finding a dragging brake or a binding wheel bearing. Coast in neutral and watch for one wheel consistently trailing the others.");
            AddBulletPoint("Verifying tyre or wheel changes. Different rolling circumferences show up as a consistent percentage offset between axles.");
            AddBulletPoint("Track and dyno logging. Per-wheel speed data records wheelspin, lockup, and exactly when ABS or ESP intervened - information the engine ECU's own logging cannot provide.");
            AddBulletPoint("Confirming the car is genuinely stationary before running an actuation routine.");

            AddSubheading("How to Use:");
            AddParagraph("1. Leave 'Log to CSV' ticked if you want a recording. A timestamped file is created in Documents\\LotusECMLogger automatically - existing logs are never overwritten.");
            AddParagraph("2. Click 'Start Monitor'. Values begin updating immediately if the ignition is on.");
            AddParagraph("3. Click 'Stop' when finished.");
            AddParagraph("The display updates about ten times a second, which is comfortable to read, while the CSV log captures the full broadcast rate for later analysis. The CSV contains one row per wheel-speed frame with all four wheel speeds, vehicle speed in both raw counts and km/h, the brake switch, and the intervention flags.");

            AddSubheading("About the Speed Values:");
            AddParagraph("The raw counts are exactly what the module transmits. Converting them to km/h requires a wheel-size multiplier that lives in the engine ECU, not the ABS, so the displayed km/h assumes the stock value. If your car has non-standard wheel or tyre sizes the absolute km/h figures will be proportionally off, but the raw counts and all wheel-to-wheel comparisons remain exact - and comparison is what most diagnosis actually depends on.");
            AddParagraph("A wheel showing '-' rather than a speed means the module is reporting that sensor's reading as unavailable, which is itself a strong indication of a sensor or wiring fault.");

            AddSubheading("Note:");
            AddParagraph("While the monitor is running it holds the J2534 device, so the other ABS operations are disabled until you stop it. If the monitor stops on its own with an error, the device connection was lost - check the adapter and the OBD connection.");
        }

        private void ShowAbsActuationHelp()
        {
            AddHeading("ABS Pump & Valve Routines");

            AddParagraph("This sub-tab commands the ABS module to run its hydraulic pump motor and cycle its solenoid valves. Unlike every other ABS function, this one physically operates the braking system and moves brake fluid.");

            AddParagraph("WARNING: These routines actuate the brake hydraulics. Run them only with the vehicle stationary and safely secured, the ignition on and the engine off, and the brake pedal released. Never run them on a moving vehicle. The module refuses to run them when it detects unsafe conditions, but do not rely on that alone.");

            AddSubheading("Why These Routines Exist:");
            AddParagraph("The ABS modulator contains chambers and passages that ordinary pedal-pumping cannot reach, because the valves that isolate them are closed during normal braking. Air trapped in the ABS unit therefore survives a conventional bleed and produces a soft or inconsistent pedal that no amount of bleeding at the calipers will fix. These routines open those valves and run the pump to circulate fluid through the parts of the system that are otherwise sealed off.");
            AddParagraph("You need this after any work that lets air into the ABS unit: replacing the modulator itself, opening a brake line upstream of it, or running the reservoir dry. For a routine caliper or pad service, a conventional bleed is sufficient and this is unnecessary.");

            AddSubheading("Available Routines:");
            AddBulletPoint("Full bleed sequence (3 phases) - The complete procedure, and the right choice for actual brake bleeding. It runs circulation, then a pressure hold, then a quick valve cycle, in the documented order and durations.");
            AddBulletPoint("Bleed circulation (0x03) - Opens the valves and runs the pump to circulate fluid. This is the phase that actually moves air out of the modulator.");
            AddBulletPoint("Pressure hold test (0x02) - Closes the valves and runs the pump to build and hold pressure. Used as a leak test and to check pedal feel; pressure that will not hold indicates a leak or a valve that is not sealing.");
            AddBulletPoint("Quick valve cycle (0x01) - Rapidly cycles the valves. Dislodges remaining bubbles, and lets you hear each valve click, which confirms the solenoids are working.");
            AddBulletPoint("Per-wheel cycle (0x05) - Cycles one wheel circuit at a time. Use this to isolate a single sticking valve, and to establish which pressure channel corresponds to which wheel.");
            AddBulletPoint("Full system test (0x10) - Exercises all wheels in sequence as a comprehensive hydraulic check.");

            AddSubheading("Preconditions:");
            AddParagraph("The module refuses to actuate unless the vehicle is stationary, the ignition is on with the engine off, the brake pedal is released, and no ABS or ESP intervention is active. Click 'Check Preconditions' to verify the observable ones before you start - it reads the module's own broadcast data and reports each condition individually, so a refusal tells you exactly which one is not met.");
            AddParagraph("The ignition-on-engine-off condition cannot be observed from ABS data and must be confirmed by you. The check reports it as such rather than silently assuming it.");
            AddParagraph("The same check runs automatically before any routine starts. If a condition is not met the routine is refused with a clear explanation and nothing is sent to the module.");

            AddSubheading("Brake Bleeding Procedure:");
            AddParagraph("1. Attach a pressure bleeder at about 2 bar and open all four bleed nipples.");
            AddParagraph("2. Confirm the vehicle is stationary and secure, ignition on, engine off, brake pedal released.");
            AddParagraph("3. Select 'Full bleed sequence (3 phases)' and click 'Run'.");
            AddParagraph("4. Read the confirmation dialog and confirm each listed condition.");
            AddParagraph("5. Watch the live status while it runs. The valve positions and brake pressures update continuously, so you can confirm the hydraulic unit is genuinely responding rather than assuming it.");
            AddParagraph("6. Close the nipples when the sequence completes, and check the reservoir level.");
            AddParagraph("Keep the reservoir topped up throughout. The pump moves a significant volume of fluid and drawing the reservoir empty introduces air into the master cylinder - undoing the entire job and creating more work than you started with.");

            AddSubheading("Monitoring While a Routine Runs:");
            AddParagraph("The grid updates about twice a second with per-wheel routine status from the module, plus live valve positions and brake pressures. This tells you whether the routine is actually doing anything:");
            AddBulletPoint("During bleed circulation the valves should read release (open).");
            AddBulletPoint("During the pressure hold test the valves should read hold (closed) and the pressure readings should rise.");
            AddBulletPoint("Valve positions that never change, or pressures that never move, indicate the hydraulic unit is not responding - worth investigating before you conclude the bleed was successful.");

            AddSubheading("Stopping Safely:");
            AddParagraph("Click 'Stop' at any time to end a routine early. The application always sends the stop command and returns the module to its normal state before finishing, including when a routine is cancelled, fails partway, or errors. This matters: a routine left running can strand the hydraulic unit with its valves in an intermediate position until the next power cycle, which can leave the brakes feeling wrong.");
            AddParagraph("If a routine reports that it did not complete, read the reported reason. The most common cause is the module refusing on preconditions - typically the brake pedal being pressed, or the car not being fully stationary.");

            AddSubheading("Important Notes:");
            AddBulletPoint("Always road-test carefully after any ABS bleeding work, starting with a firm pedal check at a standstill before moving, then low-speed braking in a safe area.");
            AddBulletPoint("These routines have been implemented from firmware documentation. Verify the pedal is firm and the brakes behave normally before driving the car anywhere.");
            AddBulletPoint("If the pedal is soft after a bleed, repeat the sequence. Air in the ABS modulator often takes more than one cycle to clear completely.");
            AddBulletPoint("If you are unsure about any part of this procedure, have the brakes bled by a qualified technician. Brakes are not a system to experiment on.");
        }

        private void ShowT6RmaHelp()
        {
            AddHeading("T6 RMA Logging");

            AddParagraph("The T6 RMA (Remote Memory Access) Logging tab enables direct reading of ECU memory addresses for advanced diagnostics and development. This is an advanced feature intended for developers and advanced users who need to monitor specific memory locations not available through standard OBD-II.");

            AddSubheading("Requirements:");
            AddParagraph("This feature requires a debug-enabled ECU with developer calibration. It will not work with standard production ECUs.");

            AddSubheading("How to Use:");
            AddParagraph("1. Memory Address: Enter the hexadecimal address you want to monitor (e.g., 0x40000000). Valid RAM addresses are typically in the range 0x40000000-0x4000FFFF.");
            AddParagraph("2. Length: Specify the number of bytes to read (1-255).");
            AddParagraph("3. Polling Interval: Set how often to read the address in milliseconds (10-10000ms).");
            AddParagraph("4. CSV Output File: Choose where to save the logged data. A timestamped default in Documents\\LotusECMLogger is provided (e.g., T6RMA_20250210_143022.csv).");
            AddParagraph("5. Start Logging: Click 'Start Logging' to begin reading and recording the memory contents.");
            AddParagraph("6. Stop Logging: Click 'Stop Logging' when finished.");

            AddSubheading("Data Display:");
            AddParagraph("The 'Latest Data' panel shows:");
            AddBulletPoint("Hex Dump: Raw hexadecimal values displayed in rows of 16 bytes");
            AddBulletPoint("ASCII Representation: Text representation of the data");
            AddBulletPoint("Numeric Interpretations: Values displayed as byte, int16, int32, and float");

            AddSubheading("Output:");
            AddParagraph("Data is logged as a time series to CSV format, allowing you to analyze memory contents over time. This is useful for reverse engineering, debugging custom calibrations, or monitoring internal ECU variables.");

            AddSubheading("Caution:");
            AddParagraph("This is an advanced feature. Reading from invalid memory addresses may cause unpredictable behavior or ECU communication errors. Only use this feature if you understand ECU memory architecture.");
        }

        private void ShowLiveTuningHelp()
        {
            AddHeading("T6 Live Tuning");

            AddParagraph("The Live Tuning tab enables real-time calibration editing on unlocked T6 ECUs. It synchronizes a calibration file on disk with the ECU's RAM: the application reads a region of ECU memory to a .cpt file, watches that file for changes, and automatically writes any modified 32-bit words back to the ECU while the engine is running. Edit the file in your calibration editor of choice and the changes take effect on the ECU within a fraction of a second of saving.");

            AddSubheading("Requirements:");
            AddBulletPoint("Unlocked ECU: Live tuning uses the raw-CAN RMA protocol, which standard locked calibrations do not answer. The 'ECU' indicator on the Vehicle Information tab shows whether the ECU is unlocked.");
            AddBulletPoint("Valid RAM region: Addresses must lie in the ECU's calibration RAM range (0x40000000-0x4000FFFF). The memory presets (from config\\liveTuning\\memoryConfig.json) provide known-good regions for supported firmware versions.");
            AddBulletPoint("Logging stopped: Live tuning holds the J2534 device for itself and cannot run alongside a logging session.");

            AddSubheading("How to Use (Read & Start):");
            AddParagraph("1. Select a memory preset for your firmware, or enter a base address and length manually.");
            AddParagraph("2. Choose an output directory. The default is Documents\\LotusECMLogger\\LiveTuning.");
            AddParagraph("3. Click 'Read & Start'. The application reads the ECU memory region into a timestamped .cpt file and immediately begins monitoring it.");
            AddParagraph("4. Open the .cpt file in your calibration editor and make changes. Each time you save, the changed words are written to the ECU automatically (the file is scanned every 100 ms).");
            AddParagraph("5. Click 'Stop Monitoring' when finished.");

            AddSubheading("Using an Existing File:");
            AddParagraph("If you already have a .cpt file that matches the ECU's current calibration, select it under 'Calibration File' and click 'Start Monitoring' to begin synchronizing without re-reading the ECU. The file must correspond to the configured base address, otherwise writes will land at the wrong locations.");

            AddSubheading("Upload to ECU (Whole-Calibration Upload):");
            AddParagraph("Where monitoring writes individual words as you edit them, 'Upload to ECU' writes an entire .cpt into ECU RAM in one operation - the inverse of 'Read & Start'. Use it to swap in a complete calibration you prepared earlier, or to restore a saved one, without flashing.");
            AddParagraph("1. Select the file under 'Calibration File' and confirm the base address is right for it (the memory presets set this for you).");
            AddParagraph("2. Click 'Upload to ECU' and read the confirmation dialog, which shows the exact address range that will be overwritten.");
            AddParagraph("3. Watch the progress bar and the status log. The upload can be cancelled while it runs.");
            AddParagraph("The file's own size decides how much is written - no more and no less. The length box on this tab applies to reading, not uploading; if the two disagree the confirmation dialog says so, because that usually means the selected preset does not match the file.");

            AddSubheading("How an Upload Protects You:");
            AddParagraph("Two checks run around every upload, because writing a whole calibration into a running engine has more ways to go wrong than editing one value.");
            AddBulletPoint("Before writing: the first 32 bytes of the file are compared against the same bytes already in ECU memory. If they differ, the file almost certainly belongs to a different calibration, a different ECU, or a different memory region, and the upload stops before sending anything. Both headers are shown so you can see what differs, and you can override the check if you genuinely mean to replace the running calibration with an unrelated one.");
            AddBulletPoint("After writing: the region is read back and compared against the file. RMA writes are fire-and-forget - the ECU never acknowledges them - so this read-back is the only proof that every word actually landed. Any mismatch is reported with a count and the first differing addresses.");
            AddParagraph("These checks answer different questions. The read-back confirms that what was sent arrived intact; it cannot tell that the wrong file was sent. The pre-flight check is the one that catches that.");

            AddSubheading("Upload Cautions:");
            AddBulletPoint("The transfer is not atomic. It takes several seconds, and until it finishes the engine is running on a mix of the old and new calibrations. Prefer to upload with the engine off, or at idle in a safe place - not under load.");
            AddBulletPoint("Cancelling mid-upload leaves that mix in place. Upload again to finish, or cycle the ignition to reload the flashed calibration.");
            AddBulletPoint("If the ECU is locked it silently discards the writes. The application probes the unlock state first and refuses rather than letting you discover this from a failed verification.");
            AddBulletPoint("'Upload to ECU' stays greyed out until a valid file is selected under 'Calibration File', and is disabled while monitoring is active. Note that 'Read & Start' does not put the file it creates into that box - browse to it if you want to upload a file you just read.");

            AddSubheading("Important Notes:");
            AddBulletPoint("Changes are written to ECU RAM only - they are lost on power-off and do not modify the flashed calibration. To make a change permanent, flash it with the T6E Calibration Flasher.");
            AddBulletPoint("The status log shows every word written, so you can verify each edit as it is applied.");
            AddBulletPoint("Live tuning modifies a running engine's calibration. Make small, deliberate changes and understand each parameter before editing it.");
        }

        private void ShowFlasherHelp()
        {
            AddHeading("T6E Calibration Flasher");

            AddParagraph("The T6E Calibration Flasher provides a convenient interface for flashing ECU calibrations to Lotus T6 engine control units. Access this feature from the Tools menu: Tools > T6E Calibration Flasher.");

            AddParagraph("Note: Flashing a new firmware version does not update the ECU's stored model info. After migrating to a different firmware version, run Tools > Erase Model Info to let the new firmware claim the model identity. See the 'Erase Model Info' help topic.");

            AddSubheading("Supported File Formats:");
            AddBulletPoint(".CRP files: Encrypted calibration files ready for flashing");
            AddBulletPoint(".CPT files: Plain calibration files that are automatically converted to CRP format");

            AddSubheading("How It Works:");
            AddParagraph("The tool supports both .CRP and .CPT file formats. When you select a .CPT file, it automatically converts it to XTEA-encrypted .CRP format (CRP08) before flashing to ensure compatibility with the ECU's flash programming protocol.");

            AddSubheading("How to Use:");
            AddParagraph("1. Program: Specify the path to the flash programming tool (typically EFI_PROT.EXE).");
            AddParagraph("2. Input File: Browse and select your .CRP or .CPT calibration file.");
            AddParagraph("3. Working Directory: Set the working directory where the flash tool is located (typically C:\\Program Files (x86)\\T6_ECU_FIX).");
            AddParagraph("4. Launch: Click 'Launch Program' to start EFI_PROT. The flash tool opens in its own console window and carries out the actual programming from there.");

            AddSubheading("How EFI_PROT Works:");
            AddParagraph("LotusECMLogger does not flash the ECU itself. It prepares the calibration file (converting CPT to CRP if needed) and then launches EFI_PROT.EXE - the external T6 flash programming utility - with that file. All communication with the ECU happens inside EFI_PROT's own console window, which stays open after the application hands off to it.");
            AddBulletPoint("Select a J2534 device: When EFI_PROT starts, it prompts you to choose the J2534 pass-thru device to use for the flash. Make sure your device is connected before launching.");
            AddBulletPoint("Power off the vehicle: The flashing procedure requires the vehicle to be powered off. Follow EFI_PROT's prompts for the correct ignition state during the process.");
            AddBulletPoint("Mind the timeout: EFI_PROT must establish communication with the ECU within a limited time window. If it does not, the ECU locks out flashing. An ignition cycle does NOT clear this lockout - you must remove power from the ECU or wait several minutes before it will accept flashing again. Have your device connected and be ready to proceed promptly once you launch.");

            AddSubheading("Important Safety Notes:");
            AddBulletPoint("Ensure you have a backup of your current calibration before flashing.");
            AddBulletPoint("Never disconnect power or the OBD connection during flashing - this can brick your ECU.");
            AddBulletPoint("If the flash times out and the ECU locks out, an ignition cycle will not help - remove power from the ECU or wait several minutes before retrying.");
            AddBulletPoint("Only flash calibrations intended for your specific ECU and vehicle configuration.");
            AddBulletPoint("Flashing calibrations may void warranties or violate emissions regulations.");

            AddSubheading("Automatic Conversion:");
            AddParagraph("If you select a .CPT file, the tool will automatically convert it to .CRP format using the T6 XTEA encryption key before initiating the flash process. You don't need to manually convert files.");
        }

        private void ShowEraseModelHelp()
        {
            AddHeading("Erase Model Info");

            AddParagraph("Erase Model Info clears the model identification stored in the ECU's variant coding so the currently installed firmware can re-initialize it. It is available from the Tools menu: Tools > Erase Model Info.");

            AddSubheading("Why You Need This (Firmware Migration):");
            AddParagraph("The model info is a copy of the calibration's program version string, held in the ECU's coding EEPROM. Flashing a new firmware or calibration does NOT update this stored value — the old model string is left in place, so the ECU reports a program-version mismatch and the newly flashed firmware is not fully activated.");
            AddParagraph("Erasing the model info blanks the field (fills it with 0xFF). On its next coding-initialization cycle, the firmware detects the blank field and automatically re-seeds it from the freshly flashed calibration's program version, committing the update to EEPROM. This is the step that 'activates' a new firmware version.");
            AddParagraph("In short: when migrating to a new firmware version, flash the calibration first, then run Erase Model Info so the new firmware claims the model identity. This is normally needed only once per firmware migration, not during everyday use.");

            AddSubheading("Requirements:");
            AddBulletPoint("Unlocked ECU: This operation requires an unlocked/developer calibration. On a standard locked ECU the command is silently ignored. The tool verifies the ECU is unlocked before sending the command.");
            AddBulletPoint("Correct firmware version selected: The dialog needs to know which firmware is installed so it can target the right command register address. Selecting the wrong version writes to the wrong address.");
            AddBulletPoint("Logging stopped: Erase Model Info is disabled while data logging is active. The menu item stays greyed out until you stop the logger.");

            AddSubheading("How to Use:");
            AddParagraph("1. Stop any active logging session.");
            AddParagraph("2. Open Tools > Erase Model Info.");
            AddParagraph("3. Select the firmware version currently installed on the ECU. The dialog displays the resolved coding_cmd address so you can verify the selection.");
            AddParagraph("4. Click 'Erase Model Info' and confirm the warning dialog.");
            AddParagraph("5. The tool confirms the ECU is unlocked and then issues the erase command. A confirmation message appears when complete.");
            AddParagraph("6. Re-read the ECU coding, or reload Extended Vehicle Information, to verify the model info now reflects the new firmware.");

            AddSubheading("How It Works:");
            AddParagraph("The tool issues an RMA (Remote Memory Access) write of the erase-model command (0x04) to the firmware's coding command register at the address for the selected firmware version. The ECU's coding handler fills the model field with 0xFF and flags an EEPROM write, which is committed on its next cycle. The firmware then re-seeds the field from the installed calibration version on the following coding-initialization pass.");

            AddSubheading("Important Warnings:");
            AddBulletPoint("This operation cannot be undone. The previous model string is overwritten, and the firmware re-seeds it from the installed calibration.");
            AddBulletPoint("Selecting the wrong firmware version targets the wrong memory address. Confirm the installed version before proceeding.");
            AddBulletPoint("The change is written to ECU EEPROM and persists across power cycles.");
        }

        private void ShowAdaptersHelp()
        {
            AddHeading("Supported Adapters");

            AddParagraph("LotusECMLogger works with J2534-compliant pass-thru devices connected via USB. The J2534 standard ensures compatibility across different hardware manufacturers.");

            AddSubheading("Popular Adapters:");

            AddSubheading("Tactrix OpenPort 2.0 (discontinued)");
            AddParagraph("A widely used J2534 device known for its reliability and performance. The OpenPort 2.0 was one of the most popular choices among enthusiasts and professional tuners, but it has been discontinued and is no longer manufactured.");
            AddBulletPoint("Fully J2534 compliant");
            AddBulletPoint("Supports multiple vehicle protocols");
            AddBulletPoint("Extensive community support");
            AddBulletPoint("No longer in production - check the used market");

            AddSubheading("TopDon RLink X3");
            AddParagraph("A currently available J2534-compliant pass-thru device that works with LotusECMLogger. The required J2534 driver is not included with the device - download and install it from TopDon before connecting.");
            AddBulletPoint("J2534 compliant");
            AddBulletPoint("Requires the J2534 driver download from TopDon");
            AddBulletPoint("A readily available alternative to the discontinued OpenPort 2.0");

            AddSubheading("Requirements:");
            AddBulletPoint("Device must be J2534 compliant");
            AddBulletPoint("Manufacturer drivers must be installed");
            AddBulletPoint("USB connection to computer");
            AddBulletPoint("OBD-II connection to vehicle");

            AddSubheading("Troubleshooting Adapter Issues:");
            AddParagraph("If your adapter is not working:");
            AddBulletPoint("Ensure the latest drivers are installed from the manufacturer's website");
            AddBulletPoint("Try a different USB port or USB cable");
            AddBulletPoint("Ensure your vehicle's ignition is on but engine may be off");
        }

        private void ShowTroubleshooting()
        {
            AddHeading("Troubleshooting");

            AddSubheading("Connection Issues:");

            AddParagraph("Problem: 'No J2534 device found' or 'Failed to connect'");
            AddBulletPoint("Ensure your J2534 device is connected via USB");
            AddBulletPoint("Install or update device drivers from manufacturer");
            AddBulletPoint("Check Windows Device Manager for hardware issues");
            AddBulletPoint("Try unplugging and reconnecting the device");
            AddBulletPoint("Restart the application after connecting the device");

            AddParagraph("Problem: 'ECM communication timeout'");
            AddBulletPoint("Verify your vehicle's ignition is on");
            AddBulletPoint("Check OBD-II connection is secure");
            AddBulletPoint("Ensure vehicle battery has sufficient charge");
            AddBulletPoint("Try disconnecting other devices from the OBD-II port");
            AddBulletPoint("Some vehicles require engine to be running for certain operations");

            AddSubheading("Logging Issues:");

            AddParagraph("Problem: 'Failed to start logger' or 'No data being logged'");
            AddBulletPoint("Select a valid OBD configuration from the dropdown");
            AddBulletPoint("Ensure J2534 device is connected before starting");
            AddBulletPoint("Check that no other software is using the J2534 device");
            AddBulletPoint("Verify the selected configuration is compatible with your vehicle");

            AddParagraph("Problem: 'Slow refresh rate' or 'Choppy data'");
            AddBulletPoint("Reduce the number of parameters being logged");
            AddBulletPoint("Try a different USB port (USB 3.0 recommended)");
            AddBulletPoint("Close other applications that might be using resources");
            AddBulletPoint("Check for USB driver updates");

            AddSubheading("ECU Coding Issues:");

            AddParagraph("Problem: 'Failed to read coding' or 'Failed to write coding'");
            AddBulletPoint("Stop any active logging sessions first");
            AddBulletPoint("Ensure ignition is on with engine off");
            AddBulletPoint("Some coding operations require specific ECU states");
            AddBulletPoint("Not all Lotus ECUs support coding modifications");
            AddBulletPoint("Check that you have a T6 ECU (coding may not work on older models)");

            AddSubheading("ABS/ESP Issues:");

            AddParagraph("Problem: 'No response from ABS module (timeout)'");
            AddBulletPoint("Ensure the ignition is ON - with it off the ABS module is asleep and answers nothing");
            AddBulletPoint("Stop any active logging session; ABS operations need the J2534 device to themselves");
            AddBulletPoint("Stop the ABS telemetry monitor if it is running - it also holds the device");
            AddBulletPoint("Run 'Test Connection' on the ABS tab. If the engine ECU answers but the ABS does not, the bus and adapter are fine and the problem is specific to the ABS module");
            AddBulletPoint("Check whether 'ABS telemetry' shows as broadcasting in the Test Connection results. If it does, the module is powered and running, and the issue is with diagnostic addressing rather than a dead module");
            AddBulletPoint("ABS support targets the Bosch ESP8 module fitted to the Evora. Other Lotus models use different ABS modules that will not answer these requests");

            AddParagraph("Problem: An actuation routine is refused");
            AddBulletPoint("Click 'Check Preconditions' - it reports each condition separately so you can see which one is not met");
            AddBulletPoint("The most common causes are the brake pedal being pressed or the vehicle not being fully stationary");
            AddBulletPoint("The engine must be OFF with the ignition ON; the module refuses to actuate with the engine running");
            AddBulletPoint("If no telemetry is seen at all, the module is asleep - switch the ignition on");

            AddParagraph("Problem: Live State rows show 'unavailable'");
            AddBulletPoint("Check the SecurityAccess row near the top of the results - memory reads require the unlock to have succeeded");
            AddBulletPoint("Individual locations may be refused by the module while others succeed; this is reported per row and a partial result is still usable");
            AddBulletPoint("The reason for each refusal is shown in the Detail column - 'conditionsNotCorrect' points at vehicle state, 'securityAccessDenied' at the unlock");

            AddSubheading("Live Tuning Issues:");

            AddParagraph("Problem: 'Upload to ECU' is greyed out");
            AddBulletPoint("Select a file under 'Calibration File' - the button stays disabled until that box holds a path to a file that exists");
            AddBulletPoint("'Read & Start' does not fill that box in with the file it creates, so browse to the file even if you just read it from the ECU");
            AddBulletPoint("Stop monitoring if it is running - upload needs the J2534 device to itself");
            AddBulletPoint("Check the base address parses as 8 hex digits; the button also requires a valid address");

            AddParagraph("Problem: Upload stops with a calibration mismatch");
            AddBulletPoint("The first 32 bytes of the file do not match ECU memory, so the file is probably for a different calibration, ECU, or memory region. Nothing has been written at this point");
            AddBulletPoint("Compare the two headers shown in the dialog, then check the selected file and the base address - a preset that does not match the file is the usual cause");
            AddBulletPoint("If the file really is meant to replace the running calibration with a different one, answer Yes to upload anyway");

            AddParagraph("Problem: Upload finishes but verification reports mismatched bytes");
            AddBulletPoint("Some writes did not land. ECU RAM now matches neither the old calibration nor the file - upload again, or cycle the ignition to reload the flashed calibration");
            AddBulletPoint("A locked ECU discards writes silently; confirm the 'ECU' indicator on the Vehicle Information tab reads UNLOCKED");
            AddBulletPoint("Check the OBD-II and USB connections - a marginal link drops frames, and RMA writes are never acknowledged, so nothing retries them automatically");

            AddSubheading("General Tips:");
            AddBulletPoint("Always ensure your vehicle battery is fully charged before diagnostic operations");
            AddBulletPoint("Keep your J2534 device drivers up to date");
            AddBulletPoint("Don't interrupt operations like ECU coding or calibration flashing");
            AddBulletPoint("Check file permissions if you encounter file save errors");
            AddBulletPoint("Consult vehicle-specific forums for known compatibility issues");

            AddSubheading("Getting Help:");
            AddParagraph("If you continue experiencing issues:");
            AddBulletPoint("Check the project GitHub page for known issues and solutions");
            AddBulletPoint("Review log files in the Documents\\LotusECMLogger folder for error details");
            AddBulletPoint("Consult Lotus enthusiast forums for vehicle-specific guidance");
            AddBulletPoint("Ensure you're using the latest version of LotusECMLogger");
        }
    }
}
