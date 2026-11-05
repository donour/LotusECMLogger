namespace LotusECMLogger
{
    /// <summary>The CCP tab's help pages: an overview plus one page per sub-tab, and recording and replay.</summary>
    public partial class HelpDialog
    {
        private void ShowCcpOverviewHelp()
        {
            AddHeading("CCP (Calibration Protocol)");

            AddParagraph("The CCP tab talks to the T6 engine ECU's built-in CCP 2.1 calibration interface - the protocol calibration engineers use. Over one CAN session it can identify the ECU, read and checksum its memory, write calibration RAM, stream internal variables, and edit calibration tables live while the engine runs. It is the only part of the application that can tune tables in real time with the ECU reading them.");

            AddSubheading("Requirements:");
            AddBulletPoint("A T6 engine ECU from 2011 on. CCP is part of the firmware; it does not need an unlocked ECU. (The 2010 Evora's ECU uses a different CCP setup that is not supported yet.)");
            AddBulletPoint("CCP enabled in the calibration: the CCP enable byte must select a CAN bus. Stock calibrations ship with it set, but a calibration that disables it cannot be enabled over CCP - it needs a calibration that sets it, flashed by other means.");
            AddBulletPoint("A J2534 device on the OBD-II port, as for every other tab. The session runs on CAN at 500 kbit/s (request 0x350, response 0x351).");
            AddBulletPoint("For table editing: an A2L table definition for the build in config\\a2l. The application ships them for C132E0278 (Evora GT430) and B132E0091 (2011 Evora NA). Every other feature works on any T6 build.");

            AddSubheading("Connecting:");
            AddParagraph("1. Switch the ignition on (the engine may run).");
            AddParagraph("2. Click 'Connect'. The tab connects, reads the ECU's identity and works out its build from the calibration ID. The panel lists the slave ID, CCP version, available resources, the build and its calibration RAM range.");
            AddParagraph("3. Optionally click 'Read Variable Table' to read the firmware's table of internal variables, which the Variables and Self-Test pages use.");
            AddParagraph("'Disconnect' releases the tab's hold on the session. If the calibration ID matches no known build, reading still works, but writes on the Memory page need a build chosen from its Build list - only do this if you know the firmware matches, because the build decides where calibration RAM is.");

            AddSubheading("One Session, Shared:");
            AddParagraph("The CCP tab, the High-Speed Log tab, a tuning recording and the Vehicle Information probe all share one CCP session on the J2534 device, so they can run at the same time. The session closes when the last of them lets go. The ECU has 10 DAQ lists (two each at 100, 50, 20 and 10 Hz, one each at 2 and 1 Hz) shared between everything that streams; the Traffic page shows who holds which.");

            AddSubheading("Pages:");
            AddBulletPoint("Memory - read memory to a hex view or a .bin file, checksum or compare it with a file, and write calibration RAM with backups.");
            AddBulletPoint("Tuning - open a calibration file and edit its tables in a grid and graph, with the edits written to the ECU as you make them, a cursor showing where the ECU is reading, and recording and replay.");
            AddBulletPoint("Variables - browse the firmware's variables and watch any of them live.");
            AddBulletPoint("Traffic - session counters, command latency, DAQ list owners and a decoded command trace.");
            AddBulletPoint("Self-Test - a read-only checklist of the connection, with a report you can save.");

            AddSubheading("While the Tuning Page Is Selected:");
            AddParagraph("The connection panel shrinks to its button row and the log to a one-line strip, giving the table editor most of the window. Click '▸ Log' to open the full log.");

            AddSubheading("Important:");
            AddBulletPoint("Everything written over CCP goes to calibration RAM: the ECU uses it on its next table lookup, and it is lost at power-off. Nothing here changes the flashed calibration. To keep a change, save the calibration file and flash it.");
            AddBulletPoint("The CCP tab and the older T6 Live Tuning tab are different tools. T6 Live Tuning uses the RMA protocol (unlocked ECUs) and an external editor; CCP Tuning has its own editor and works on stock firmware.");
        }

        private void ShowCcpMemoryHelp()
        {
            AddHeading("CCP - Memory");

            AddParagraph("The Memory page reads, checks and writes ECU memory over the CCP session.");

            AddSubheading("Reading:");
            AddBulletPoint("Enter an address (hex) and a length, or click 'Calibration Region' to fill in the build's calibration RAM range, then click 'Read'. The bytes appear in the hex view; 'Save .bin…' saves them.");
            AddBulletPoint("Reads are fast (about 3 KB per second on a car), so the whole calibration takes around 9 seconds.");

            AddSubheading("Checking:");
            AddBulletPoint("'Checksum' has the ECU compute a checksum of the region itself, which is much faster than reading it.");
            AddBulletPoint("'Compare with File…' compares that checksum with a file's, to see quickly whether the ECU holds a given calibration.");
            AddBulletPoint("'Diff with File…' reads the region and lists every byte that differs from the file, named by symbol and table cell where the build's symbol database knows them.");

            AddSubheading("Writing Calibration RAM:");
            AddParagraph("'Write Bytes at Address' writes a few hex bytes; 'Write Calibration File…' writes every byte of a calibration file that differs from the ECU. Both are guarded:");
            AddBulletPoint("Only the build's calibration RAM range can be written.");
            AddBulletPoint("The engine must be off: the write is refused while the engine turns, because a write of more than a few bytes is not atomic and a lookup could see a half-changed table.");
            AddBulletPoint("The CCP enable byte is never changed - changing it would cut the session off mid-write.");
            AddBulletPoint("A backup of the region is saved first, to Documents\\LotusECMLogger\\ccp-backups, and you confirm a summary of what will change.");
            AddBulletPoint("Every write is read back and checked. If the ECU's memory changed since you prepared the write, it stops and asks you to prepare it again.");
            AddParagraph("'Restore Backup…' writes a saved backup back, with the same guards. Writes are lost at power-off; to keep them, flash a calibration.");
        }

        private void ShowCcpTuningHelp()
        {
            AddHeading("CCP - Tuning");

            AddParagraph("The Tuning page is a table editor for the ECU's calibration, modelled on RaiderLite. Open a calibration file and its tables appear in a tree; edit them in a grid beside a graph. Start live sync and every change you make is written to the ECU as you make it, so you can tune with the engine running. A cursor shows where the ECU is reading the table you are looking at.");

            AddSubheading("Opening a Calibration:");
            AddBulletPoint("Click 'Open Calibration…' and pick a raw calibration image (.bin), a CRP calibration chunk, a 64 KB Snapshots calibration download, or a .CRP file. The build is recognised from the calibration ID.");
            AddBulletPoint("The build's A2L (config\\a2l\\<build>.a2l) is loaded with it and checked against the file. 'Open A2L…' loads a different one; it is refused if it does not fit the calibration.");
            AddBulletPoint("To edit what is in the ECU now, read the calibration region on the Memory page, save it, and open that file - or open your file and use 'Take ECU Values' (below).");

            AddSubheading("Finding Tables:");
            AddBulletPoint("The tree groups tables by subsystem (fuel, ignition, idle, throttle and so on). Type in the filter box to search names and descriptions; several words must all match.");
            AddBulletPoint("Show Tables, Axes, Values or All; 'Changed only' shows just what differs from the file as opened. Changed items are bold.");

            AddSubheading("The Grid:");
            AddBulletPoint("A map shows its X axis across the top and its Y axis down the left; a curve shows its axis above its values. Cells are coloured from lowest to highest value.");
            AddBulletPoint("A cell raised from the file has a red border, a lowered one a blue border. 'Changes' shows each cell's change instead of its value.");
            AddBulletPoint("While live sync runs, a corner mark shows a cell's state: orange pending, blue writing, red failed.");
            AddBulletPoint("Hover a cell for its value, raw value, address, the file's value and its ECU state. The line under the grid describes the current cell.");

            AddSubheading("Editing:");
            AddParagraph("Select cells (drag, Shift or Ctrl click; click the top-left corner or press Ctrl+A for the whole table), then:");
            AddBulletPoint("Type a number to set the selection, or press Enter in the Value box. 'Multiply' (or Ctrl+Enter, *) multiplies by the Value box.");
            AddBulletPoint("Fine steps (Ctrl+Shift+Up/Down, [ and ]) change by one raw count by default; coarse steps (Ctrl+Up/Down, + and _) by ten. Both step sizes can be changed.");
            AddBulletPoint("Interpolate 'Both' (Shift+I), 'Vertical' (Shift+V) or 'Horizontal' (Shift+H) fills the selection between its edges; 'Smooth' evens it out; 'Revert' puts the file's values back.");
            AddBulletPoint("Copy and Paste (Ctrl+C, Ctrl+V) exchange blocks with a spreadsheet. Undo and Redo (Ctrl+Z, Ctrl+Y) step back and forward through every edit.");
            AddBulletPoint("Select axis points to edit an axis. Axis points must stay in increasing order; an edit that breaks the order is refused.");
            AddBulletPoint("A value outside the table's limits is refused, and so is anything that would change the CCP enable byte.");

            AddSubheading("The Graph:");
            AddParagraph("'Graph' (Ctrl+G) shows the table beside the grid: lines, or a 3D surface you can turn by dragging. 'Transpose' draws lines per column instead of per row and 'Reset View' returns the surface to its default angle. Clicking a point selects its cell; right-click copies the graph as an image.");

            AddSubheading("Live Sync:");
            AddParagraph("1. Connect on the CCP tab and open the calibration that matches the ECU's build.");
            AddParagraph("2. Choose 'Hold edits' now if you want edits staged until you press 'Apply'; otherwise every edit, undo and redo is written as soon as you make it.");
            AddParagraph("3. Click 'Start Live Sync' and confirm. The ECU's calibration is read and backed up to Documents\\LotusECMLogger\\ccp-backups, then compared with the file.");
            AddParagraph("4. If they differ, choose 'Send File to ECU' (engine off - a whole file is not written atomically) or 'Take ECU Values' (copy the ECU's values into the editor as one undoable step).");
            AddParagraph("5. Edit. The status reads 'ECU in sync.' when everything has been written and read back.");
            AddParagraph("Before each batch of writes one checksum confirms the ECU still holds what the session last wrote. If it does not - another tool wrote to it, or a reboot restored the flashed calibration - sync stops rather than overwrite it blindly. A failed write also stops sync. Click 'Refresh from ECU' to read it again and carry on. 'Stop Sync' ends the session; the ECU keeps what was written.");

            AddSubheading("The Cursor:");
            AddParagraph("With 'Cursor' on, the grid outlines the cells the ECU is interpolating between, with a dot at the exact point and a fading trail, and the graph marks the same point. The line under the grid gives each input and the interpolated value. It works by streaming the variables that feed the table's axes (named in the A2L) at 20 Hz.");
            AddBulletPoint("'No live cursor' means no axis of the table has an input in the A2L - usually because the firmware reads it with different inputs in different places. The table can still be edited.");
            AddBulletPoint("A table with only one bound axis shows a line across the grid instead of a point.");
            AddBulletPoint("The cursor shows where the inputs are, not proof that the ECU is using that table at the moment: some tables apply only to one gearbox, mode or condition.");
            AddBulletPoint("Each table's cursor uses a DAQ list. Turn 'Cursor' off to free it, or record (below), which streams every input at once.");

            AddSubheading("Saving:");
            AddParagraph("'Save As…' writes the calibration in the layout it was opened in (a .CRP is saved as its calibration chunk). It never overwrites the file you opened, and an existing file of the same name is backed up first. To flash the result, build a CRP with Tools > Create CRP File… and use the T6E Calibration Flasher.");

            AddSubheading("Cautions:");
            AddBulletPoint("Live edits change a running engine. Make small, deliberate changes, and understand a table before you change it.");
            AddBulletPoint("Writes are not atomic: during a multi-cell edit the ECU can read a table that is only partly updated. 'Hold edits' only stages edits on the PC; Apply still writes them in several steps. Make large changes with the engine off.");
            AddBulletPoint("Everything written is lost at power-off. Save As keeps it.");
        }

        private void ShowCcpRecordingHelp()
        {
            AddHeading("CCP - Recording and Replay");

            AddParagraph("Record a tuning session while you drive or run the engine, then replay it to see where the ECU was reading each table and how long it spent at each cell. The recording also keeps every calibration write, so the replay shows the tables exactly as they were at each moment.");

            AddSubheading("Recording:");
            AddParagraph("1. Connect, open the calibration that matches the ECU, and (if you will edit) start live sync. Sync can also be started or stopped while recording.");
            AddParagraph("2. Choose what else to log in the list beside 'Record': 'Axis inputs only', or one of this build's High-Speed Log presets.");
            AddParagraph("3. Click 'Record'. The ECU's calibration is saved first (instantly from live sync's copy when one checksum confirms it, otherwise read in about 9 seconds), then logging starts. The bar shows the elapsed time and the number of writes.");
            AddParagraph("4. Tune and drive. Every write live sync makes is recorded.");
            AddParagraph("5. Click 'Stop Recording'. The calibration is saved again, if the session is still connected.");
            AddParagraph("Every variable an A2L axis reads is always recorded, so any table can be replayed afterwards, not just the ones you looked at. They take one 20 Hz DAQ list. While recording, the cursor reads the recording instead of streaming each table separately, so switching tables costs the log nothing.");

            AddSubheading("Recording Files:");
            AddParagraph("Recordings are saved in Documents\\LotusECMLogger\\recordings, named after the build and time. Each is four files:");
            AddBulletPoint("<name>.csv - the log, in the same format as the High-Speed Log, so it opens in any spreadsheet or log viewer.");
            AddBulletPoint("<name>.edits.csv - one line per write to calibration RAM: when, where, the bytes before and after, and the tables and cells it touched.");
            AddBulletPoint("<name>.cal-start.bin and <name>.cal-end.bin - the ECU's calibration when recording started and stopped. Either opens on the Tuning page like any calibration file.");

            AddSubheading("Replaying:");
            AddParagraph("1. Click 'Open Recording…' and pick a recording's .csv. (Stop live sync and recording first.)");
            AddParagraph("2. Pick a table. The grid shows it as it was at the playhead, with the cursor where the ECU was reading it.");
            AddParagraph("3. Click 'Play' (choose a speed from 0.25× to 16×), or click and drag on the timeline to move through the recording. Each orange tick on the timeline is a calibration write; the tables change as the playhead passes them.");
            AddParagraph("4. Click 'Close Recording' to return to the calibration that was open before.");
            AddParagraph("A replay is read-only. 'Save As…' still works: it saves the calibration as it was at the playhead.");

            AddSubheading("Cells Read:");
            AddParagraph("Turn on 'Cells read' to shade the shown table by how long the ECU spent reading each cell: white for little, deep orange for most, grey for never. Hover a cell for the time and its share. To count only part of the recording, hold Shift and drag across the timeline; double-click the timeline to count all of it again.");
            AddBulletPoint("Time is shared between the cells the lookup interpolates between, the same way the ECU blends them, so a point halfway between two cells counts half to each.");
            AddBulletPoint("If an axis was edited during the recording, the counting follows the edit from the moment it was written.");
            AddBulletPoint("A table with an axis that has no input spreads its time along that axis.");
            AddBulletPoint("Use it to see which cells matter for your driving, and which ones you would be changing blind.");

            AddSubheading("Older High-Speed Logs:");
            AddParagraph("Any High-Speed Log can be opened for replay. Logs made before this version do not say exactly how each channel was scaled, so replay takes the scaling from the A2L; channels it cannot match are left out. A log without a saved calibration uses the calibration open on the Tuning page, if it is the same build. Only tables whose inputs were logged can show a cursor or cells read.");
        }

        private void ShowCcpVariablesHelp()
        {
            AddHeading("CCP - Variables");

            AddParagraph("The Variables page lists the firmware's internal variables - from the build's symbol database, joined with the variable table the ECU reports - and watches any of them live.");

            AddSubheading("How to Use:");
            AddParagraph("1. Click 'Load Variables'. Tick 'Include calibration' to list calibration values as well as RAM variables. Type in the search box to filter; several words must all match.");
            AddParagraph("2. Select variables and click 'Add to Watch →'.");
            AddParagraph("3. Choose a rate and click 'Start Watch'. Values update live from a DAQ stream; 'Stop Watch' ends it.");
            AddParagraph("'Read Once' reads the selected variables a single time without streaming, which also works for values larger than 4 bytes. 'Remove' and 'Clear' edit the watch list.");

            AddSubheading("Notes:");
            AddBulletPoint("The watch uses DAQ lists shared with the High-Speed Log and the tuning cursor. If none is free at the chosen rate it says so; pick another rate or stop something else.");
            AddBulletPoint("Scaling and units come from the symbol database's types, which are reverse-engineered. Check a variable against a known reading before relying on it.");
        }

        private void ShowCcpTrafficHelp()
        {
            AddHeading("CCP - Traffic");

            AddParagraph("The Traffic page shows what the CCP session is doing, for diagnosing a slow or unreliable connection.");
            AddBulletPoint("Counters: commands, replies, error replies, timeouts, stray and malformed frames, DAQ frames and unrouted DAQ frames for the session.");
            AddBulletPoint("Latency: the last, mean, 95th percentile and worst command round trip. On a car with a good adapter it is under 2 ms.");
            AddBulletPoint("DAQ lists: which list belongs to whom (High-Speed Log, Variable watch, a tuning recording or cursor, the self-test), its rate, size, samples, overruns, drops and gaps. A gap means another consumer started streaming, which can cost a few frames.");
            AddBulletPoint("Trace: every command and reply, decoded. 'Pause' freezes it, 'Clear View' empties it and 'Save Trace…' writes it to a file. DAQ frames are counted, not traced.");
        }

        private void ShowCcpSelfTestHelp()
        {
            AddHeading("CCP - Self-Test");

            AddParagraph("The Self-Test runs a read-only checklist against the connected ECU and reports each result, so you can confirm a car works with the CCP features or share the report when it does not. Nothing is written to the ECU. On a car it takes about a minute, mostly reading calibration RAM.");

            AddSubheading("Checks:");
            AddBulletPoint("Session and identification: the slave ID, CCP version, resources and build.");
            AddBulletPoint("Variable table: the firmware's table of variables reads back intact.");
            AddBulletPoint("Command latency: how long commands take (a warning above a few milliseconds usually points at the adapter or its driver).");
            AddBulletPoint("Memory read and checksum: calibration RAM reads, and the ECU's checksum agrees with what was read.");
            AddBulletPoint("DAQ event rates and concurrent access: each event channel streams at its rate while memory is read. This needs every DAQ list, so it is skipped while the High-Speed Log, a variable watch, a cursor or a recording is running.");
            AddBulletPoint("Engine speed and session counters: a live value, and no timeouts or bad frames during the session.");

            AddParagraph("Click 'Run Self-Test', then 'Save Report…' to keep the results as a text file.");
        }
    }
}
