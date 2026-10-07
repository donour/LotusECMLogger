# CCP client: implementation plan

Design notes for adding a CCP 2.1 master to LotusECMLogger. The ECU side is described in
[`CCP-support.md`](CCP-support.md); this document covers the PC side.

**Status (2026-10-07): Phase 1 implemented; session orchestration and UI remain planned.**
The protocol core, target/build profiles, transport contract, J2534 transport and deterministic fake slave
are implemented. Cross-platform tests exercise the command sequence, firmware quirks and runtime-registry format.
The existing high-speed logger is not ported yet. Run `dotnet test LotusECMLogger.Ccp.Tests`.

The decisions below were made with the project owner; the reasons are recorded so they can be revisited deliberately rather than by accident.

---

## Contents
1. [Starting point: the high-speed logger is already a CCP DAQ master](#1-starting-point-the-high-speed-logger-is-already-a-ccp-daq-master)
2. [Decisions](#2-decisions)
3. [Architecture](#3-architecture)
4. [Phases](#4-phases)
5. [Live tuning: findings that shape the design](#5-live-tuning-findings-that-shape-the-design)
6. [Table definitions: A2L](#6-table-definitions-a2l)
7. [Risks and things to verify](#7-risks-and-things-to-verify)
8. [Initial implementation and validation](#8-initial-implementation-and-validation)

---

## 1. Starting point: the high-speed logger is already a CCP DAQ master

`LotusECMLogger/Services/HighSpeedLogService.cs` was written from `ChannelLogger.md`, before the protocol was
identified as CCP. It speaks CCP's DAQ subset under the old names, and it has been validated on a live car
(2026-06-27).

| Name in the existing code | CCP 2.1 name |
|---|---|
| `CmdOpenSession` 0x01 / `CmdEndSession` 0x07 | CONNECT / DISCONNECT |
| `CmdInitGroup` 0x14 | GET_DAQ_SIZE |
| `CmdSelectSlot` 0x15 / `CmdSetChannel` 0x16 | SET_DAQ_PTR / WRITE_DAQ |
| `CmdConfigure` 0x06 mode 2 | START_STOP (prepare) |
| `CmdStartStopAll` 0x08 | START_STOP_ALL |
| `CmdIdentify` 0x17 | EXCHANGE_ID |
| `ChannelLoggerMagic` `0x0003008E` | not a magic number: EXCHANGE_ID bytes 4–7 = data type 0x00, resource availability 0x03 (CAL+DAQ), protection 0x00, driver version 0x8E |
| group / frame / label / slot / divider | DAQ list / ODT / PID / event channel / prescaler |
| `HighSpeedLogPlanner.RateTable` | event channels 0–9 at 100/100/50/50/20/20/10/10/2/1 Hz, prescaler 2 for 5 Hz |

What it does not do:
- Commands: GET_CCP_VERSION, TEST, and the memory commands (SET_MTA, UPLOAD, SHORT_UP, DNLOAD/DNLOAD_6,
  BUILD_CHKSUM).
- Targets: only T6 2011+ on 0x350/0x351. The planner hard-codes 10 lists × 12 ODTs and PID = `list*12`; the
  2010 car (0x200/0x201, 1 × 10) and the IPS TCU (0x360/0x361) are not supported.
- Build identification.
- Concurrency: `ReadAck` (`HighSpeedLogService.cs:555`) discards every frame that isn't the reply it's
  waiting for. Memory can't be read or written while DAQ is streaming.

## 2. Decisions

| Decision | Choice | Why |
|---|---|---|
| Existing high-speed logger | Port it onto the new CCP layer | One CCP implementation; streaming and memory access can share the bus. Guarded by a byte-identical regression test (Phase 3). |
| Targets in v1 | T6 2011+, 2010 Evora C132E0044, IPS TCU C132F0395 | All three run the same Vector driver; only IDs and DAQ sizes differ (`CCP-support.md` §2). |
| Calibration writes | In v1, with guards | Wanted for live tuning. |
| Interactive live tuning | In scope (Phase 6) | Table editor over a calibration file, synced to ECU RAM over CCP, with a live operating-point cursor. |
| Table definition format | **A2L files**, "for now" | Standard format, also usable in CANape/INCA/VISION. Accepts the limits in §6. |
| Source of A2L files | Supplied by the project owner later | The app only *reads* A2L. No generator in scope; if one is built, it is a C# tool in this solution. |
| Existing RMA-based T6 Live Tuning tab and Snapshots | Unchanged; no CCP back end for them | Out of scope for now. |
| A2L for the high-speed logger | No: it keeps the symbol CSVs | Limits scope; can move later. |

## 3. Architecture

New code goes under `LotusECMLogger/Services/Ccp/`.

**Protocol core (pure, no I/O)**
- `CcpCommandCode`, `CcpError` (0x22, 0x30–0x33), `CcpException`.
- `CcpFrame`: builds 8-byte command frames (CRO) and parses replies `[0xFF, ERR, CTR, …]` and DAQ frames
  `[PID, data…]`, where PID bit 7 is the Vector overrun flag.

**Target profiles**
- `CcpTarget`: CRO/DTO IDs, DAQ lists × ODTs, first-PID rule, event-channel rates, whether an enable byte exists.

  | Target | CRO / DTO | Lists × ODTs | First PID | Events |
  |---|---|---|---|---|
  | T6 2011+ | 0x350 / 0x351 | 10 × 12 | list×12 | 0–9 |
  | 2010 Evora C132E0044 | 0x200 / 0x201 | 1 × 10 | 0 | 0 only (100 Hz) |
  | IPS TCU C132F0395 | 0x360 / 0x361 | 10 × 12 | list×12 | 0–9 |

- `CcpBuildProfile`: per-build CAL_base/size, enable-byte address and `CAL_prog_version` offset
  and a verified symbol-entry-array address (`CCP-support.md` §7). When an A2L is loaded, its `IF_DATA ASAP1B_CCP`, `MEMORY_SEGMENT` and `EPK` take
  precedence for describing the target, after validation against the identified build. A2L memory segments
  must not expand the verified writable range. Unsupported layout/conversion/addressing features disable
  editing of the affected characteristic rather than being silently skipped. The built-in profiles are the fallback
  so the CCP tab and logger work without an A2L.

**Runtime variable registry (located, 2026-10-07)**
- The T6 firmware contains a nameless address/size export registry. The owner supplied a T6-V00BU
  table at 0x000C02B8: 32-byte build ID, 12-byte version, then 753 six-byte records
  `{address: u32 big-endian, size: u8, reserved: u8=0}` at +0x2C. Total size is 4562 bytes,
  excluding any subsequent zero terminator. Decode the six-byte wire stride explicitly rather than
  relying on native struct alignment. Sizes include 1/2/4 and whole objects of 5/6/8/10/32 bytes.
- The sibling project's `lib/ltacc.py:read_varptr_list` already discovers it: send raw CAN 0x53 with
  four zero bytes (DLC 4), receive a four-byte big-endian entries pointer on 0x7A0, then read six-byte
  records until a six-zero-byte terminator. This is the unlock-gated raw-memory interface, not CCP.
  The T6 RX handler selects this behavior by DLC 4; it does not require the four payload bytes to be zero.
- Decompile-confirmed entry pointers: B13200091 0x000C02E4, C132E0278 0x000CE984,
  E132E0288 0x000D2EF4. `CcpBuildProfile.SymbolEntriesAddress` holds only these verified fallbacks.
  Once CCP is enabled, read the same flash registry through SET_MTA/UPLOAD using the matched profile,
  without opening an additional RMA session or requiring the RMA unlock. Read the 44 bytes preceding
  entries for build identity. Entry counts/headers must be verified per build, not copied from V00BU.
- `T6SymbolRegistry` decodes both counted header/table dumps and bounded zero-terminated entry arrays,
  validates T6 flash/RAM ranges, and retains indexes, aliases and exact byte sizes. Synthetic tests
  exercise the supplied 753-entry layout; actual image bytes/count are not present in the C export.
- The B13200091 freeze-frame code uses `entries[(pid & 0x3ff)]` and reads the exported size to select
  1/2/4-byte captures. This establishes diagnostic use; the registry is not merely unused linker residue.
  The existing Python client also uses indexes 253/254 to locate CAL RAM and its size, and documents
  RPM at index 31. Verify these semantic bindings in each build before treating indexes as stable.
- Keep physical metadata (address/exported byte count) separate from name, signedness, units, scaling
  and table/input relationships. Validate catalog bindings against the registry; do not infer a name
  from size alone. Sizes greater than four are exported objects, not legal WRITE_DAQ widths: split
  into supported 1/2/4-byte elements and ODTs (seven payload bytes maximum), and reconstruct explicitly.
  Registry membership does not authorize calibration writes or prove complete coverage of every global.

**Transport**
- `ICcpTransport`: send an 8-byte CRO; deliver received frames with the adapter's hardware timestamp.
- `J2534CcpTransport`: raw CAN through `J2534Session`, PASS filter on the target's DTO ID.
- `FakeCcpSlave` (test project): an in-memory slave that reproduces this firmware's behaviour, so the master
  can be tested without hardware:
  - commands other than CONNECT/TEST are ignored silently until connected;
  - the DNLOAD reply does not contain the post-incremented MTA0;
  - SET_DAQ_PTR element 7 overwrites element 0 of the next ODT;
  - PID bit 7 is set after an overrun;
  - BUILD_CHKSUM replies late (256 bytes per main-loop pass).

**`CcpMaster` (the session)**
- **One receive loop.** Frames starting 0xFF go to the waiting command, matched on CTR; 0xFE is
  handled as an event packet; valid target PIDs go to DAQ subscribers. This is what lets memory access run during streaming.
- **One command in flight.** CTR matching is not concurrency control: the slave has one CRM buffer,
  shared MTA0, shared DAQ pointer and a checksum worker that uses MTA1 and the CRM buffer. Serialise
  complete operations (SET_MTA plus all transfers/read-back; SET_DAQ_PTR plus WRITE_DAQ; EXCHANGE_ID plus
  UPLOAD). Hold the command gate until BUILD_CHKSUM completes; SHORT_UP would otherwise replace its
  working pointer. After timeout/cancellation, mark the session uncertain and recover before another
  memory operation; never blindly retry a post-incrementing DNLOAD/UPLOAD. DAQ reception continues.
- **DAQ ownership.** Allocate lists centrally for logger and cursor subscribers. GET_DAQ_SIZE clears the
  selected list and resets the shared transmit queue, so configure allocations before streaming, or
  explicitly pause/reconfigure all consumers and report the gap. Stop only the releasing consumer's
  lists; DISCONNECT/end-session belongs to the session owner after the last consumer releases it.
  Use bounded subscriber queues so UI/disk work cannot block the receive loop.
- **One instance is shared** by the CCP tab and the High-Speed Log tab, as `IT6RMAService` is shared between
  Snapshots and T6 RMA Logging. Two tabs opening separate `J2534Session`s would conflict over the device.
- **Session commands:** CONNECT (station 0x0000 only), TEST, DISCONNECT, GET_CCP_VERSION, and EXCHANGE_ID
  followed by UPLOAD of the ID string (`Ccp_T6`, `Ccp_GD8`, `Ccp_TCU`).
- **Transfer validation:** UPLOAD/SHORT_UP/DNLOAD sizes are constrained by the master to 1–5;
  DNLOAD_6 is exactly 6. The firmware does not enforce the five-byte limit. Reject zero-length checksum
  commands too: they leave the background worker idle and never receive a reply.
- **Memory reads:** SET_MTA then repeated 5-byte UPLOADs; SHORT_UP for single values.
- **Memory writes:** DNLOAD_6 / DNLOAD, always followed by read-back. The DNLOAD reply's MTA field is never
  used, because this firmware leaves it stale (`CCP-support.md` §4).
- **Checksum:** BUILD_CHKSUM split into blocks under 64 KiB, because the firmware silently truncates the size
  to 16 bits. The 16-bit additive checksum is also computed locally to compare. The timeout grows with
  block size.
- **DAQ:** GET_DAQ_SIZE, SET_DAQ_PTR (elements 0–6 only), WRITE_DAQ, START_STOP, START_STOP_ALL.
- **Identification:** the ID string gives the target family. `CAL_prog_version` (CAL_base + 0x59D4, verified
  in C132E0278 and A138E0112 only) is then read for each known CAL_base. If nothing matches, the user picks
  the build.

## 4. Phases

| # | Phase | Hardware needed | Touches existing code |
|---|---|---|---|
| 1 | Protocol core, target profiles, transport, `FakeCcpSlave`. Tests replay the §10 example session from `CCP-support.md` byte for byte. | No | No |
| 2 | `CcpMaster`: session, memory, checksum, DAQ, identification. Tests against the fake slave, including memory access while DAQ is streaming. | No | No |
| 3 | Port `HighSpeedLogService` onto `CcpMaster`. The planner takes limits and rates from the target profile and uses the first PID from GET_DAQ_SIZE. Rename to CCP terms internally (UI wording can stay). `Identify()` decodes EXCHANGE_ID fields. **Regression gate:** a test asserts the T6 CRO frames for the sample presets are identical command fields to the current output, after normalising its short payloads to DLC 8 with zero padding. The old builder emits e.g. a four-byte CONNECT; the new transport always emits eight. Also preserve AEM polling, timestamps and bounded CSV queues. | One live re-check afterwards | Yes |
| 4 | CCP tab (top level, sharing `CcpMaster` with High-Speed Log): connect panel (target, version, ID, resources/protection, build, CAL range); read a memory region to a hex view or `.bin`; checksum a region against a file. | For final checks | Adds a tab |
| 5 | Guarded writes (see below). | Yes | No |
| 6 | Live tuning (see below). | Yes | No |

**Phase 5 guards**
- Writes are limited to the CAL RAM range (from the A2L `MEMORY_SEGMENT`, else the build profile).
- An unidentified build disables writes unless the user explicitly turns on an override.
- A confirmation dialog before writing covers engine off and the fact that changes are lost at power-off.
- A backup `.bin` of the region being changed is saved first.
- Every write is read back, and mismatches are reported.
- Single values and whole calibration images can be written. Preserve the active CCP enable/bus-select
  byte during normal writes, including whole-image uploads; changing it can disable or move CCP mid-write.
  An intentional change requires a separate operation after all other writes and verification.
- Validate address plus length without 32-bit wrap, build identity and characteristic layout before
  each operation. Failed verification/partial writes stop live sync and invalidate the affected state.

**Phase 6 sub-phases**
- **6a, A2L reader.** See §6.
- **6b, calibration image.** `CalibrationImage` loads a raw CAL-region image or a 64 KB Snapshots calibration
  download (detected by size); offset = address − CAL_base. Undo/redo; tracks bytes changed against the
  original file. **Save As only**, with a backup; the source file is never overwritten.
- **6c, live sync** (`LiveTuningSession` on the shared `CcpMaster`).
  - On start, read the whole CAL region (about 27 KB on 2017+ builds) and compare it with the file. The user
    then sends the file to the ECU, takes the ECU's values into the editor, or cancels.
  - *Live* mode writes each edit immediately; *Hold* mode stages edits until Apply.
  - Changed bytes are grouped into contiguous runs, written with DNLOAD and read back. Each cell shows in
    sync / pending / failed.
  - Axis edits that would make an axis non-increasing are blocked (the lookups require increasing raw axes).
- **6d, editor UI** (a sub-tab of the CCP tab). Table browser with search, units filter and "changed" markers.
  Grid with heatmap colouring and axis headers in real units. Selection operations: set, add, multiply,
  percent change, interpolate, smooth. Paste from Excel, undo/redo, comparison against the original file.
- **6e, live operating point.** Stream each table's `InputQuantity` measurements over DAQ (alongside the
  edits, through the shared `CcpMaster`). Highlight the active cell and the four cells being interpolated,
  with a trail of recent hits. Tables with `NO_INPUT_QUANTITY` can be edited but show no cursor.

## 5. Live tuning: findings that shape the design

All from `references/C132E0278.c` and the symbol catalogs in `LotusECMLogger/config/highSpeedLogger/database/`.
`references/` is gitignored, so the decompile and the line numbers cited below exist only in a local copy.

**The calibration file is a straight copy of CAL RAM.** `copyCAL2RAM` (`C132E0278.c:10127`) copies flash
0x20000 to CAL_base (0x40008654), 0x1A6B words = 0x69AC bytes. File offset = address − CAL_base, for both a
CAL-region dump and the 64 KB Snapshots calibration download. The CVN is a CRC16 over the CAL area computed
at boot (`C132E0278.c:10144`).

**The symbol catalogs already describe table structure by naming convention.** A table is a flat array
`CAL_name`, with axes `CAL_name_X_<qty>` and optionally `CAL_name_Y_<qty>`; the data length is X × Y. For
example `CAL_inj_warmup_factor_ips` is `u8_factor_1/64[192]` with a 12-entry load X axis and a 16-entry
coolant Y axis. C132E0278 has 235 X axes and 90 Y axes; B13200091 has 199 and 76. This is useful when writing
A2L files, though the app itself will read tables from the A2L.

**2D tables store X fastest.** `lookup_3D_uint8_interpolated` reads `lut[y * size_x + x]`
(`C132E0278.c:22512`), so each Y value is a row and X runs along it.

**Lookups compare raw values and transform their inputs.** `lookup_3D_uint8_interpolated` takes 16-bit
inputs but uses `input & 0xff` (`C132E0278.c:22481`), and compares against raw axis values. Each of the eight
lookup variants handles edges and repeated axis values in its own way.

**What the lookup inputs actually are.** A survey of every `lookup_*` call
(`tools/lookup_survey.py`; a text parse of the decompile, so counts are approximate) found
484 input arguments across 281 tables. These are lexical categories, not proof that an expression is
DAQ-observable: the script recognises generated local names but misses renamed locals. For example
`_load_for_xtau` and `_load_mass_per_time` are declared in `interrupt_timer_2000hz` and classified as globals.
Check declaration scope, RAM symbols, calculations and execution conditions before assigning an A2L input.
The percentages below describe the heuristic output; the observable shares require a scope-aware audit.
The script reports constants separately, but cannot recover conditions selecting alternate inputs:

| Kind | Share | Example | Observable over DAQ? |
|---|---|---|---|
| A. Bare identifier (heuristic global) | ~79% | `CAL_sensor_tmap_expected_temp` X ← `engine_speed_8bit` | Only after confirming a RAM symbol |
| B. Expression without recognised locals | ~14% | `CAL_ign_dwell_time` Y ← `sensor_adc_ecu_voltage >> 2 & 0xff` | Only after verifying every dependency is observable and reproducing its calculation |
| C. Constant input (~5%); conditional alternatives need manual analysis | ~5% | `CAL_ign_comp_tps_*` Y ← `accel_pedal_latched >> 2` or `cruise_tps_commanded >> 2`; `CAL_idle_comp_tps_while_cranking` X ← `baro >> 2` or `0xff` | Only with the condition modelled |
| D. Stack temporary or function return value | ~2% | `CAL_fuel_pump_command` Y ← `estimate_required_injector_flow()` (`C132E0278.c:57469`) | No |

Also:
- **49 tables are looked up from more than one place**, and 42 table axes get different inputs at different
  places.
- **Most lookup results are stored in a global** (e.g. `ign_comp_idle_speed1`). Streaming that output and
  comparing it with a PC-side lookup would verify an input definition. This self-check is not in the plan
  (A2L can't express it) but is the strongest way to catch wrong inputs.
- **DAQ cannot sample stack temporaries.** DAQ samples in the 2 kHz interrupt, not when the lookup runs, so a
  stack slot holds something else by then.

## 6. Table definitions: A2L

CURVE and MAP are ASAP2 (A2L) concepts, not part of the CCP protocol. CCP only moves bytes; the tool learns
where cells and axes are from the A2L.

**Reader (Phase 6a).** Our own tolerant parser for the subset we need. It reads `/begin … /end` blocks and
skips unrelated unknown metadata, so A2Ls from other tools still load. Unsupported features that
affect addresses, record layout, scaling, byte order or inverse conversion make the affected items
read-only/unavailable with an explanation. They must not silently fall back to editable defaults.
No existing .NET parser was evaluated for license or maintenance. It reads:
- `MOD_COMMON` (`BYTE_ORDER`);
- `MOD_PAR` (`EPK`, `ADDR_EPK`, `MEMORY_SEGMENT`);
- `CHARACTERISTIC`: `VALUE`, `VAL_BLK`, `CURVE`, `MAP`, `ASCII`;
- `AXIS_PTS`, and `AXIS_DESCR` with `COM_AXIS`, `STD_AXIS`, `FIX_AXIS_PAR`;
- `RECORD_LAYOUT`;
- `COMPU_METHOD`: `IDENTICAL`, `LINEAR`, `RAT_FUNC`, `TAB_VERB`, `FORM`;
- `MEASUREMENT`, including `VIRTUAL`;
- `InputQuantity`;
- `IF_DATA ASAP1B_CCP`.

Tests use a small hand-written sample A2L until real files are supplied.

**What the supplied A2L files should contain:**

| A2L item | Used for | If missing |
|---|---|---|
| `MOD_COMMON` `BYTE_ORDER MSB_FIRST` | decoding (the MPC5534 is big-endian) | reader assumes MSB_FIRST and warns |
| `MOD_PAR` `EPK` + `ADDR_EPK` (e.g. at `CAL_prog_version`) | checking the A2L matches the connected build | user confirms the build by hand |
| `MOD_PAR` `MEMORY_SEGMENT` for the CAL RAM range | Phase 5 write guard | falls back to `CCP-support.md` §7 |
| `IF_DATA ASAP1B_CCP` (CRO/DTO IDs, station 0, DAQ lists, `RASTER` events) | CCP settings | falls back to the built-in target profile |
| `CURVE`/`MAP` with `COM_AXIS` → `AXIS_PTS` | table layout (this firmware stores axes separately) | — |
| `FIX_AXIS_PAR` | the `lookup_*_noaxis(shift, …)` tables (raw axis spacing 2^(8−shift) within the unwrapped input range: these routines index `(input << shift) >> 8`. For shift 3 the spacing is 32, not 8) | — |
| `InputQuantity` on each `AXIS_DESCR`, pointing at a `MEASUREMENT` or `VIRTUAL` measurement | live cursor | table editable, no cursor |

The Vector sample A2L referenced in `CCP-support.md` §9 is `MSB_LAST` (C16x); the T6 needs `MSB_FIRST`.

**How the survey's input kinds map onto A2L:**
- A → only after verifying the identifier is a RAM global, `InputQuantity` pointing at a `MEASUREMENT`.
- B → after verifying dependency scope and observability, `InputQuantity` pointing at a `VIRTUAL` measurement with a formula (see the formula item in §7).
- C and D → `NO_INPUT_QUANTITY`.

**What A2L cannot express, dropped for now:**
- more than one input per axis (the 49 tables looked up from several places, some under conditions);
- the lookup-output self-check;
- lookup-specific behaviour (`& 0xff` masking, edge rules), which the app has to know per lookup variant.

A vendor `IF_DATA` block (ignored by other tools) could carry these later without leaving A2L.

## 7. Risks and things to verify

**To verify while building**
- **Runtime variable registry:** validate table header/count, the zero terminator and semantic index
  bindings against each actual firmware image before replacing inferred channel sizes. The decoder
  is implemented; session-driven discovery and catalog reconciliation remain Phase 2 work.
- **A2L map storage order:** which `RECORD_LAYOUT` index mode (`ROW_DIR` / `COLUMN_DIR`) matches the
  firmware's X-fastest layout. Pin it with a test against a known table such as
  `CAL_injtip_transient_throttle_response_time` (8 × 8).
- **A2L formulas:** whether ASAP2 formula syntax supports `>>`, `&` and similar, which type B inputs need. If
  not, those inputs fall back to `NO_INPUT_QUANTITY`.
- **Reflash checksum:** whether the flash/CRP path needs a checksum fixed when an edited calibration file is
  reflashed. Check before Save As output is used for reflashing.

**Needs hardware** (`CCP-support.md` §12)
- BUILD_CHKSUM reply timing on real ECUs.
- TEST/CONNECT to a non-zero station address. The master only ever uses station 0x0000, so this should not
  arise.
- One live logging re-check after the Phase 3 port, including concurrent reads and AEM polling.
- Explain the enable-byte prerequisite on connection failure: a disabled T6 cannot enable itself over CCP.
  Enabling requires an existing authorised calibration/reflash or separately unlocked RMA workflow.

**Operational risks**
- **Writes are live.** DNLOAD into CAL RAM changes engine behaviour on the next lookup.
- **Writes are not atomic.** A multi-cell edit takes several DNLOAD commands, so a lookup can see a table that
  is only partly updated. Hold mode only stages edits on the PC; Apply still sends multiple non-atomic
  writes. Recommend engine off for large edits; Hold is not an atomicity guarantee.
- **One J2534 device.** Everything that talks CCP goes through the shared `CcpMaster`.

**Coverage limits**
- Live tuning works only for builds with an A2L.
- The lookup survey covers C132E0278 only, the one decompile in `references/`.

## 8. Initial implementation and validation

Phase 1 code lives in `LotusECMLogger/Services/Ccp/`; the linked-source
`LotusECMLogger.Ccp.Tests` project runs on macOS/Linux/Windows without loading WinForms or J2534.
The fake slave models only these firmware families, not every CCP implementation. It advances checksum
work explicitly in 256-byte passes and emits DAQ frames deterministically, without a real-time scheduler. Oversized transfer buffer corruption and last-ODT metadata corruption
are explicitly outside the fake's coverage; they must be prevented by the master.
Session gates, timeout recovery, DAQ ownership and guarded write orchestration are Phase 2/5 work;
the low-level frame factories do not make arbitrary memory writes safe to expose in the UI.

Static cross-checks on 2026-10-07 used the sibling `LotusECU-T4e` exports:
`disassembly/evora/C132E0278/C132E0278.c`, its `_symbols.csv`,
`disassembly/evora/B13200091/B13200091.c` and `disassembly/evora/E132E0288/C132F0395.c`.
Decompile line numbers vary as labels are updated. No new hardware validation is claimed.

Validation on 2026-10-07: `dotnet test LotusECMLogger.Ccp.Tests --configuration Release`
and a Windows-targeted application cross-build with `-p:EnableWindowsTargeting=true`.
The transport compiles against J2534-Sharp.Core 2.0.4; adapter I/O still requires a Windows bench check.
