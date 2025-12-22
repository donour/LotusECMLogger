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
            features.Nodes.Add("livedata", "Live Data Logging");
            features.Nodes.Add("ecucoding", "ECU Coding");
            features.Nodes.Add("vehicleinfo", "Extended Vehicle Information");
            features.Nodes.Add("dtc", "Diagnostic Trouble Codes");
            features.Nodes.Add("learneddata", "Learned Data Reset");
            features.Nodes.Add("t6rma", "T6 RMA Logging");
            features.Nodes.Add("flasher", "T6E Calibration Flasher");

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
                case "livedata":
                    ShowLiveDataHelp();
                    break;
                case "ecucoding":
                    ShowEcuCodingHelp();
                    break;
                case "vehicleinfo":
                    ShowVehicleInfoHelp();
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
                case "flasher":
                    ShowFlasherHelp();
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
            AddBulletPoint("ECU Coding: Read and modify ECU configuration settings for Lotus T6e ECUs.");
            AddBulletPoint("Extended Vehicle Information: Retrieve VIN, ECU details, and calibration data.");
            AddBulletPoint("Diagnostic Trouble Codes: Read and clear DTCs from the ECU.");
            AddBulletPoint("Learned Data Reset: Clear adaptive learning values from the ECU.");
            AddBulletPoint("T6 RMA Logging: Advanced memory address logging for development.");
            AddBulletPoint("T6E Calibration Flasher: Flash calibration files to the ECU.");
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
            AddParagraph("4. Click 'Start' to begin logging data. Data will be saved to a CSV file in your Documents folder.");
            AddParagraph("5. Click 'Stop' when you're done logging.");

            AddSubheading("Navigation:");
            AddParagraph("The application uses a tabbed interface to organize different diagnostic and logging functions. Click on each tab to access different features:");
            AddBulletPoint("Extended Vehicle Information - Read VIN and ECU details");
            AddBulletPoint("Live Data - Real-time parameter logging");
            AddBulletPoint("ECU Coding - Modify ECU configuration");
            AddBulletPoint("Diagnostic Trouble Codes - Read and clear fault codes");
            AddBulletPoint("Learned Data Reset - Reset ECU adaptive values");
            AddBulletPoint("T6 RMA Logging - Advanced memory logging");
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
            AddParagraph("Log files are automatically saved to your Documents folder with timestamps in the filename (e.g., LotusECMLog20250210_143022.csv). These CSV files can be opened in Excel, Google Sheets, or specialized data analysis tools.");

            AddSubheading("Supported Parameters:");
            AddBulletPoint("Standard OBD-II Mode 01: Engine RPM, vehicle speed, coolant temperature, intake air temperature, throttle position, fuel trim values, oxygen sensor data, and more.");
            AddBulletPoint("Lotus-Specific Mode 22: Variable cam timing angles, knock control retard, requested vs actual torque, boost pressure, lambda values, and other manufacturer-specific parameters.");

            AddSubheading("Tips:");
            AddBulletPoint("Choose configurations appropriate for your needs - larger parameter sets require more processing time.");
            AddBulletPoint("The refresh rate shown in the status bar indicates how many times per second the display updates.");
            AddBulletPoint("Data is logged at a higher rate than displayed for accurate time-series capture.");
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

            AddParagraph("The Extended Vehicle Information tab retrieves static vehicle data such as VIN, ECU name, calibration ID, and calibration verification numbers. This information is queried using OBD-II Mode 09 and provides essential identification data about your vehicle's ECU and configuration.");

            AddSubheading("How to Use:");
            AddParagraph("1. Load Data: Click 'Load Vehicle Data' to query all available information from your ECU.");
            AddParagraph("2. View Results: Information is displayed in a list format with parameter names, values, and units.");

            AddSubheading("Available Information:");
            AddBulletPoint("Vehicle Identification Number (VIN): The unique 17-character identifier for your vehicle.");
            AddBulletPoint("ECU Name: The internal name/designation of your engine control unit.");
            AddBulletPoint("Calibration ID: Identifies the software calibration loaded in the ECU.");
            AddBulletPoint("Calibration Verification Numbers (CVN): A checksum value used to verify calibration integrity.");
            AddBulletPoint("In-Use Performance Tracking: Emissions-related tracking data (if available).");

            AddSubheading("Use Cases:");
            AddBulletPoint("Verify your VIN matches vehicle documentation.");
            AddBulletPoint("Identify which ECU calibration is currently installed.");
            AddBulletPoint("Compare calibration IDs before and after reflashing.");
            AddBulletPoint("Document your ECU configuration for records or troubleshooting.");
        }

        private void ShowDtcHelp()
        {
            AddHeading("Diagnostic Trouble Codes");

            AddParagraph("The Diagnostic Trouble Codes (DTC) tab provides functionality for reading and clearing diagnostic trouble codes from the ECU. This feature helps you diagnose issues and monitor fault codes stored in your vehicle's engine management system.");

            AddSubheading("How to Use:");
            AddParagraph("1. Read Codes: Click 'Read Codes' to retrieve all stored trouble codes from the ECU.");
            AddParagraph("2. View Details: Each code is displayed with its description and status.");
            AddParagraph("3. Clear Codes: After addressing the underlying issues, click 'Clear Codes' to erase stored fault codes from the ECU memory.");

            AddSubheading("Understanding DTCs:");
            AddParagraph("Diagnostic trouble codes are alphanumeric codes that identify specific faults detected by the ECU. They follow a standard format:");
            AddBulletPoint("P-codes: Powertrain (engine and transmission)");
            AddBulletPoint("C-codes: Chassis (ABS, steering)");
            AddBulletPoint("B-codes: Body (airbags, climate control)");
            AddBulletPoint("U-codes: Network communication");

            AddSubheading("Important Notes:");
            AddParagraph("Clearing codes does not fix the underlying problem - it only erases the stored fault memory. If the problem persists, codes will return after driving the vehicle.");
            AddParagraph("Some codes may require multiple drive cycles to reset monitoring readiness flags.");
        }

        private void ShowLearnedDataHelp()
        {
            AddHeading("Learned Data Reset");

            AddParagraph("The Learned Data Reset tab allows you to perform an OBD-II Mode 0x11 reset to clear learned parameters from the ECU. This operation resets adaptive learning values, which may be necessary after certain repairs or modifications, though the ECU will need time to relearn optimal settings afterward.");

            AddSubheading("What is Learned Data?");
            AddParagraph("The ECU continuously adapts to your driving style and component wear by adjusting various parameters. These adaptations are stored as 'learned values' and include:");
            AddBulletPoint("Fuel trim adjustments");
            AddBulletPoint("Idle speed control adaptations");
            AddBulletPoint("Throttle position learning");
            AddBulletPoint("Sensor offset corrections");

            AddSubheading("When to Use:");
            AddParagraph("You may want to reset learned data after:");
            AddBulletPoint("Replacing the battery or ECU");
            AddBulletPoint("Major engine repairs or modifications");
            AddBulletPoint("Installing new sensors or fuel system components");
            AddBulletPoint("Experiencing persistent drivability issues");

            AddSubheading("How to Use:");
            AddParagraph("1. Click 'Perform Reset' to initiate the learned data reset procedure.");
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
            AddParagraph("4. CSV Output File: Choose where to save the logged data. A default path is provided.");
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

        private void ShowFlasherHelp()
        {
            AddHeading("T6E Calibration Flasher");

            AddParagraph("The T6E Calibration Flasher provides a convenient interface for flashing ECU calibrations to Lotus T6e engine control units. Access this feature from the File menu: File > T6E Calibration Flasher.");

            AddSubheading("Supported File Formats:");
            AddBulletPoint(".CRP files: Encrypted calibration files ready for flashing");
            AddBulletPoint(".CPT files: Plain calibration files that are automatically converted to CRP format");

            AddSubheading("How It Works:");
            AddParagraph("The tool supports both .CRP and .CPT file formats. When you select a .CPT file, it automatically converts it to XTEA-encrypted .CRP format (CRP08) before flashing to ensure compatibility with the ECU's flash programming protocol.");

            AddSubheading("How to Use:");
            AddParagraph("1. Program: Specify the path to the flash programming tool (typically EFI_PROT.EXE).");
            AddParagraph("2. Input File: Browse and select your .CRP or .CPT calibration file.");
            AddParagraph("3. Working Directory: Set the working directory where the flash tool is located (typically C:\\Program Files (x86)\\T6_ECU_FIX).");
            AddParagraph("4. Launch: Click the launch button to start the flash process.");

            AddSubheading("Important Safety Notes:");
            AddBulletPoint("Ensure you have a backup of your current calibration before flashing.");
            AddBulletPoint("Never disconnect power or the OBD connection during flashing - this can brick your ECU.");
            AddBulletPoint("Only flash calibrations intended for your specific ECU and vehicle configuration.");
            AddBulletPoint("Flashing calibrations may void warranties or violate emissions regulations.");

            AddSubheading("Automatic Conversion:");
            AddParagraph("If you select a .CPT file, the tool will automatically convert it to .CRP format using the T6 XTEA encryption key before initiating the flash process. You don't need to manually convert files.");
        }

        private void ShowAdaptersHelp()
        {
            AddHeading("Supported Adapters");

            AddParagraph("LotusECMLogger works with J2534-compliant pass-thru devices connected via USB. The J2534 standard ensures compatibility across different hardware manufacturers.");

            AddSubheading("Popular Adapters:");

            AddSubheading("Tactrix OpenPort 2.0");
            AddParagraph("A widely used J2534 device known for its reliability and performance. The OpenPort 2.0 is one of the most popular choices among enthusiasts and professional tuners.");
            AddBulletPoint("Fully J2534 compliant");
            AddBulletPoint("Supports multiple vehicle protocols");
            AddBulletPoint("Regular driver updates");
            AddBulletPoint("Extensive community support");

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
            AddBulletPoint("Review application log files in Documents folder for error details");
            AddBulletPoint("Consult Lotus enthusiast forums for vehicle-specific guidance");
            AddBulletPoint("Ensure you're using the latest version of LotusECMLogger");
        }
    }
}
