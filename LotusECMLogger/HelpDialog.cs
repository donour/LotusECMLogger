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

            // Create split container
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 200,
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
            AddBulletPoint("T6 RMA Logging: Advanced memory address logging for development.");
            AddBulletPoint("T6 Live Tuning: Edit calibration values in ECU RAM in real time by monitoring a calibration file on disk (requires an unlocked ECU).");
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

            AddParagraph("The ECU Coding tab allows you to read and modify ECU configuration settings for Lotus T6e ECUs. These settings control various vehicle features and behaviors that are not accessible through standard OBD-II parameters.");

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
            AddParagraph("The ECU continuously adapts to your engine, fuel, and component wear by adjusting various parameters as you drive. On the Lotus T6e these adaptations are stored as 'learned values' in the ECU's EEPROM (protected by a checksum) so they persist across power cycles. They include:");
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

            AddParagraph("The Live Tuning tab enables real-time calibration editing on unlocked T6e ECUs. It synchronizes a calibration file on disk with the ECU's RAM: the application reads a region of ECU memory to a .cpt file, watches that file for changes, and automatically writes any modified 32-bit words back to the ECU while the engine is running. Edit the file in your calibration editor of choice and the changes take effect on the ECU within a fraction of a second of saving.");

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

            AddSubheading("Important Notes:");
            AddBulletPoint("Changes are written to ECU RAM only - they are lost on power-off and do not modify the flashed calibration. To make a change permanent, flash it with the T6E Calibration Flasher.");
            AddBulletPoint("The status log shows every word written, so you can verify each edit as it is applied.");
            AddBulletPoint("Live tuning modifies a running engine's calibration. Make small, deliberate changes and understand each parameter before editing it.");
        }

        private void ShowFlasherHelp()
        {
            AddHeading("T6E Calibration Flasher");

            AddParagraph("The T6E Calibration Flasher provides a convenient interface for flashing ECU calibrations to Lotus T6e engine control units. Access this feature from the Tools menu: Tools > T6E Calibration Flasher.");

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
            AddBulletPoint("Check that you have a T6e ECU (coding may not work on older models)");

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
