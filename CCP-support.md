# CCP (CAN Calibration Protocol) support in Lotus T6 ECUs and the IPS TCU

The EFI Technology firmware in the Lotus T6/T6e engine ECUs, and in the IPS gearbox TCU, contains a
**CCP 2.1 slave**. CCP is the ASAM standard (1990s, the predecessor of XCP) that a PC tool uses over CAN
to read and write ECU memory and to stream measurement data (DAQ).

Earlier notes in this repo called it a proprietary "high-speed channel logger"
(`romraider-defs - Copy/disassembly_references/ChannelLogger.md`). It is CCP: the slave identifies itself as
`Ccp_T6` (or `Ccp_GD8` / `Ccp_TCU`), answers GET_CCP_VERSION with 2.1, and every command code, reply
layout and error code it implements matches the CCP 2.1 specification.

**Provenance: it is Vector Informatik's CCP driver, version 1.42 (October 2003), integrated by EFI.**
The evidence, in increasing order of specificity:
- **Version byte:** byte 7 of the EXCHANGE_ID reply is 0x8E = 142 = `CCP_DRIVER_VERSION`.
- **State layout:** the RAM state block is Vector's `ccp_t` (`Crm[8]`, `SessionStatus`, `SendStatus`, `MTA[2]`,
  `Queue`, `DaqList[]`, `CheckSumSize`), with the same status-bit values.
- **Option behaviour:** several behaviours that only exist under specific driver build options are present
  (overrun bit in the PID, no MTA in the DNLOAD reply, 16-bit checksum size).
- **Shared bug:** the driver's element-index bug is reproduced.

[§13](#13-verification-ccp-21-vs-alternatives) gives the full comparison against the ASAP CCP 2.1 specification
and the driver source, and the two alternative hypotheses that were tested and rejected.

The ECU also has a separate, non-CCP raw memory peek/poke interface (CAN 0x50–0x57). It is covered in
[§8](#8-the-other-interface-raw-memory-peekpoke-0x500x57).

Everything here comes from static analysis of the Ghidra programs in the shared `Lotus ECU` project. **Nothing
has been tested on a vehicle yet.** Items marked *(verified)* were checked in the decompiled code of that
build. Items marked *(inferred)* follow from identical code structure but were not re-read in that build.

---

## Contents
1. [What this means in practice](#1-what-this-means-in-practice)
2. [Variant matrix](#2-variant-matrix)
3. [Transport and enabling](#3-transport-and-enabling)
4. [Command reference (as implemented)](#4-command-reference-as-implemented)
5. [DAQ (streaming measurement)](#5-daq-streaming-measurement)
6. [Memory access, writes and security](#6-memory-access-writes-and-security)
7. [Per-build addresses](#7-per-build-addresses)
8. [The other interface: raw memory peek/poke (0x50–0x57)](#8-the-other-interface-raw-memory-peekpoke-0x500x57)
9. [Building an A2L / configuring a CCP tool](#9-building-an-a2l--configuring-a-ccp-tool)
10. [Example session](#10-example-session)
11. [Firmware references](#11-firmware-references)
12. [Open items](#12-open-items)
13. [Verification: CCP 2.1 vs alternatives](#13-verification-ccp-21-vs-alternatives)

---

## 1. What this means in practice

- **Standard tools work.** Vector CANape, ETAS INCA and ATI VISION all speak CCP natively. You describe the
  ECU in an **A2L (ASAP2) file**: CAN IDs, DAQ layout, and variable names/addresses/types/scaling. Open-source
  CCP masters are rare, but the protocol is small enough to drive with `python-can`.
- **CANape is the natural master.** The slave is Vector's own CCP driver, whose source header says it is "used by
  the CANape CAN Calibration Tool". Its non-spec behaviours (the PID bit-7 overrun marker, the DNLOAD reply without
  MTA0) are the ones CANape is built around. Other masters should work, but test those two points first (§12).
- **Measurement:** up to 10 DAQ lists × 12 ODTs × 7 bytes, streamed at fixed event rates up to 100 Hz
  (one list at 100 Hz on the 2010 car).
- **Calibration (writes):** the calibration runs from a RAM copy, so DNLOAD into that copy changes engine
  behaviour **live**. Changes are lost at power-off: there are no CCP flash commands.
- **No protection:** there is no seed/key. On the engine ECU the only gate is one calibration byte, which is
  probably 0 (disabled) on stock cars. The TCU is not gated at all in its receive path. See §6.
- **Addresses are per build.** DAQ and calibration entries are raw RAM addresses, and every software build
  places variables differently. **One A2L per firmware build.** EXCHANGE_ID returns the same string from
  every T6 build, so the tool must identify the build some other way (§9).

---

## 2. Variant matrix

| Program (Ghidra) | Vehicle / notes | CCP ID string | CRO (cmd) / DTO (resp+data) | Bus / gate | DAQ lists × ODTs | DAQ table layout |
|---|---|---|---|---|---|---|
| `C132E0044.fullbin.dump` | 2010 Evora NA | `Ccp_GD8` | **0x200 / 0x201** | FlexCAN A only; **no enable byte** | **1 × 10** | 8-byte {addr,size} entries, list stride 0x238 |
| `B13200091` | 2011 Evora NA (US) | `Ccp_T6` | 0x350 / 0x351 | A or C, cal byte `CAL_base+0x26` | 10 × 12 | 8-byte {addr,size} entries, list stride 0x2A8 |
| `C132E0077.fullbin` | 2016 Evora image | `Ccp_T6` | 0x350 / 0x351 | as B13200091 | 10 × 12 | as B13200091 |
| `BLOWN-MOTOR-…C132E0077….MPC` | 2016 Evora, third-party-modified B13200091 | `Ccp_T6` | 0x350 / 0x351 | as B13200091 | 10 × 12 | as B13200091 |
| `C132E0271.fullbin` | 2017 Evora 400 (US) | `Ccp_T6` | 0x350 / 0x351 | A or C, cal byte `CAL_base+0x298` | 10 × 12 | split arrays (addr u32[], size u8[]), list stride 0x1AC |
| `A132E0263.fullbin` | 2017 Evora GT430 | `Ccp_T6` | 0x350 / 0x351 | as C132E0271 | 10 × 12 | as C132E0271 |
| `C132E0278.fullbin` (+3 patched copies) | 2019 Evora GT430 (UK) | `Ccp_T6` | 0x350 / 0x351 | as C132E0271 | 10 × 12 | as C132E0271 |
| `E132E0288` | 2020–21 Evora GT (US) | `Ccp_T6` | 0x350 / 0x351 | as C132E0271 | 10 × 12 | as C132E0271 |
| `A138E0112.prog.bin` | Exige 410 | `Ccp_T6` | 0x350 / 0x351 | as C132E0271 | 10 × 12 | as C132E0271 |
| `C132F0395.fullbin` | **IPS gearbox TCU** | `Ccp_TCU` | **0x360 / 0x361** | A and C; **no gate in RX handler** | 10 × 12 | as B13200091 (stride 0x2A8) |
| `emira`, `emira_8900689277A.bdc` | Emira V6 (MPC5777C, different codebase) | `Ccp_G12` string present | **not located** | — | — | — |
| HC08 ETB monitor, Bosch ABS/ESP | — | none | — | — | — | — |

Notes:
- **C132E0077 vs B13200091:** the application region 0x40000–0xCFFFF of `C132E0077.fullbin` is byte-identical to
  `B13200091` (same MD5). It is the same program software with a different calibration.
- **BLOWN-MOTOR image:** B13200091 with modifications. The dev-mode unlock string compared in `copyCAL2RAM` was
  changed from `"WTF?"` to **`"FAST"`**, there is an added code blob near 0xCABA0, and the HC08CODE marker is
  zeroed. The CCP code is unchanged.
- **Same protocol everywhere:** the command set, reply format, error codes and CAN mailboxes are identical
  across all T6 builds *(verified in every build listed)*. The layout difference in the last column is internal:
  SET_DAQ_PTR/WRITE_DAQ hide it from a CCP master. It only matters if you write DAQ tables directly through
  peek/poke.

---

## 3. Transport and enabling

### CAN IDs and mailboxes (11-bit standard IDs)

| Build family | CRO in | DTO/CRM out (DAQ data) | DTO/CRM out (command replies) |
|---|---|---|---|
| T6 2011+ on FlexCAN A | 0x350 → A.MB12 | 0x351 ← A.MB23 | 0x351 ← A.MB24 |
| T6 2011+ on FlexCAN C | 0x350 → C.MB12 | 0x351 ← C.MB13 | 0x351 ← C.MB14 |
| 2010 C132E0044 | 0x200 → A.MB12 | 0x201 ← A.MB10 | 0x201 ← A.MB11 |
| IPS TCU C132F0395 | 0x360 → A.MB12 and C.MB12 | 0x361 ← A.MB23 / C.MB13 | 0x361 ← A.MB24 / C.MB14 |

Replies and DAQ data share one DTO identifier, as CCP intends; byte 0 tells them apart (0xFF = command reply, otherwise PID).

### The enable byte (T6 2011+)

`CAL_logger_high_speed_logger_enable` (typed `enum_flexcan_bus_select` in C132E0278) controls the CCP slave.
It is read live by the RX handlers, the TX functions and the 2 kHz scheduler:

| Value | Effect |
|---|---|
| 0 | CCP off: 0x350 ignored, DAQ scheduler skipped |
| 1 | CCP on **FlexCAN A**: the vehicle/OBD-II bus (J1962 pins 6/14) |
| 2 | CCP on **FlexCAN C**: the second CAN bus (not on the OBD connector) |

Location: `CAL_base + 0x26` (0x40008E4A) in B13200091/C132E0077, and `CAL_base + 0x298` (0x400088EC) in
C132E0271, A132E0263, C132E0278, E132E0288 and A138E0112. Turning it on means changing that byte in the
calibration and reflashing the calibration (normal tuning flow); its stock value depends on the calibration.
The 2010 car and the TCU have **no** such byte (the CCP handler is always active).

Bit timing and the FlexCAN C bitrate are covered in `ChannelLogger.md` (*CAN bus selection & configuration*).
FlexCAN A runs at the vehicle bus rate.

---

## 4. Command reference (as implemented)

The CRO is always 8 bytes: `[CMD, CTR, params…]`. Command replies (CRM) are `[0xFF, ERR, CTR, data…]`.

| Code | CCP name | Parameters used by this firmware | Reply / behaviour |
|---|---|---|---|
| 0x01 | CONNECT | bytes 2–3 = station address (Intel order) | Station **0x0000** (Vector `CCP_STATION_ADDR` = broadcast = 0): connects; reply `[FF,00,ctr,FE,00,00,00,00]`. From the fully closed state this first stops all DAQ (clean slate). A non-zero address while connected puts the session in "temporarily disconnected" (state 0x10) **without stopping DAQ**, and sends no reply (spec: CONNECT to another station temporarily disconnects this one). |
| 0x05 | TEST | bytes 2–3 = station address | Station 0: reply only. **Quirk inherited from the Vector driver:** TEST to a *different* address while connected temporarily disconnects, like CONNECT. The spec says TEST must not trigger any activity. |
| 0x07 | DISCONNECT | byte 2: 0 = temporary, 1 = end session | Temporary: session paused, DAQ keeps running. End: stop all DAQ lists. |
| 0x17 | EXCHANGE_ID | — | Reply `[FF,00,ctr, idLen, 0x00, 0x03, 0x00, 0x8E]`: ID length; resource availability 0x03 = **CAL + DAQ**; resource protection 0x00 = **nothing locked**. MTA0 is set to the ID string, to be read with UPLOAD. |
| 0x1B | GET_CCP_VERSION | — | `[FF,00,ctr, 2, 1]` → **CCP 2.1**. |
| 0x02 | SET_MTA | byte 2 = MTA number (**only 0**; else error 0x32), byte 3 = address extension (**ignored**), bytes 4–7 = address (MSB first) | No range check. |
| 0x03 | DNLOAD | byte 2 = size (≤5), bytes 3.. = data | Writes bytes at MTA0 and post-increments it. No range check (§6). **Spec deviation:** the reply should carry the post-incremented MTA0 in bytes 3–7. The driver only does that with `CCP_STANDARD`, which this build lacks, so bytes 3–7 are stale. |
| 0x23 | DNLOAD_6 | bytes 2–7 = 6 data bytes | Same as DNLOAD with size 6 (same reply deviation). |
| 0x04 | UPLOAD | byte 2 = size (≤5) | Reads from MTA0 into reply bytes 3..; post-increments MTA0. |
| 0x0F | SHORT_UP | byte 2 = size, byte 3 = ext (ignored), bytes 4–7 = address | Reads via MTA1. |
| 0x0E | BUILD_CHKSUM | bytes 4–5 = block size (**only the low 16 bits** of the spec's 32-bit size at bytes 2–5) | Starts at MTA0 (copied to MTA1). Computes a 16-bit additive checksum 256 bytes per main-loop pass (Vector `ccpBackground`), **then** sends the reply (size 2, checksum in bytes 4–5). Vector's 16-bit configuration replies 0x32 for blocks ≥64 KiB; **EFI's build drops that check and silently truncates**. |
| 0x11 | SELECT_CAL_PAGE | — | Stores MTA0 as the "active page" (the driver's `ccpSetCalPage` hook). **Cosmetic**: nothing else reads it; there is one page (the RAM copy). |
| 0x09 | GET_ACTIVE_CAL_PAGE | — | Returns the stored page address. |
| 0x14 | GET_DAQ_SIZE | byte 2 = DAQ list (bytes 4–7 DTO ID ignored) | Stops and clears the list. Reply byte 3 = ODTs in list (12; 10 on 2010), byte 4 = first PID (list×12; list×10 on 2010). |
| 0x15 | SET_DAQ_PTR | byte 2 = list, 3 = ODT, 4 = element | Range: list < 10 (only 0 on 2010), ODT < 12 (< 10 on 2010), element < 8 (**but an ODT holds only 7**; see §5). Error 0x31 if out of range. |
| 0x16 | WRITE_DAQ | byte 2 = size (1, 2 or 4), byte 3 = ext (ignored), bytes 4–7 = address | Writes the element selected by SET_DAQ_PTR. |
| 0x06 | START_STOP | byte 2 mode (0 stop, 1 start, 2 prepare), byte 3 list, byte 4 last ODT, byte 5 event channel, bytes 6–7 prescaler | Matches CCP. Needs GET_DAQ_SIZE first (else error 0x22). |
| 0x08 | START_STOP_ALL | byte 2: 0 stop, 1 start all "prepared" lists | Needs a list initialised (else 0x22). |

**Not implemented** (reply error 0x30): GET_SEED (0x12), UNLOCK (0x13), SET_S_STATUS (0x0C), GET_S_STATUS (0x0D),
CLEAR_MEMORY (0x10), PROGRAM (0x18), PROGRAM_6 (0x22), MOVE (0x19), DIAG_SERVICE (0x20),
ACTION_SERVICE (0x21), START/STOP data-polling variants. Configure your tool not to send them.

**Gate:** apart from CONNECT and TEST, every command is ignored silently (no reply) unless the session is connected.

**Error codes used:** 0x22 DAQ list init request (no GET_DAQ_SIZE yet), 0x30 unknown command, 0x31 command
syntax / bad mode / bad index, 0x32 parameter out of range (MTA ≠ 0), 0x33 access denied (DNLOAD failure).
These are the standard CCP codes.

---

## 5. DAQ (streaming measurement)

- **DTO format:** `[PID, up to 7 bytes]` with PID = list × 12 + ODT (list × 10 on 2010). Entries are packed in
  order at their sizes (1/2/4 bytes). Packing stops at the first empty entry or when 7 bytes are used: an entry
  that would overflow is **silently truncated**.
- **Overrun flag in the PID (Vector/CANape extension, not in the CCP spec):** if a list's frames were dropped
  (TX buffer full), **bit 7 of the PID is set** on its next frames. This is the Vector driver option
  `CCP_SEND_QUEUE_OVERRUN_INDICATION` ("use BIT7 of PID to indicate overruns, CANape special feature"). It is also
  why the driver caps lists × ODTs at 126 (here 120). CANape understands it; other tools may treat these as
  unknown PIDs. The spec's own mechanism, a 0xFE event message with code 0x01, is not used.
- **Element-index bug (inherited from Vector driver 1.42, `comIdx>7` check on a 7-entry ODT):** SET_DAQ_PTR accepts
  element 0–7, but each ODT has 7 slots (the spec allows "up to 7 element references").
  **Element 7 overwrites element 0 of the next ODT.** Use elements 0–6 only. *(verified, all builds)*
- **Event channels** (byte 5 of START_STOP): 10 event channels (0–9), each a countdown in the 2 kHz timer ISR.
  For B13200091 the documented base rates are **100, 100, 50, 50, 20, 20, 10, 10, 2, 1 Hz** for events 0–9.
  The same 10-slot structure exists in every 2011+ build and the TCU; the reload constants were not
  re-verified per build *(inferred)*. The prescaler (bytes 6–7, 0 is treated as 1) divides further.
  **2010 C132E0044:** one event channel (0), ticked from the **100 Hz** section of the timer ISR *(verified)*.
- **Start timing:** a freshly started list fires on the first tick of its event, then every `prescaler` ticks.
- **Bandwidth:** a TX buffer of 120 frames (10 on 2010) is drained one frame per tick/TX-complete interrupt.
  A list is enqueued only if all its frames fit; otherwise it is skipped and flagged (overflow bit above).
- **Persistence:** DAQ configuration is RAM-only and is wiped at every boot (`clear_can_logging_buffer`).

---

## 6. Memory access, writes and security

What the code does *(verified in A138E0112, C132E0278, B13200091; same code in the other builds)*:

- **Arbitrary addresses:** SET_MTA / SHORT_UP / WRITE_DAQ take any 32-bit address. The address extension byte
  is discarded (`chlog_decode_address` returns the raw address).
- **Unchecked reads and writes:** UPLOAD/SHORT_UP read and DNLOAD/DNLOAD_6 write with a plain byte loop.
  **There are no bounds checks.** The Vector driver has optional hooks for exactly this: `CCP_WRITE_PROTECTION` →
  `ccpCheckWriteAccess()`, `CCP_SEED_KEY` → GET_SEED/UNLOCK. **EFI built it with both disabled.**
- **No authentication:** no seed/key, and resource protection is advertised as 0x00. On T6 2011+ the
  only gate is the calibration enable byte (§3). In particular, **the CCP path does not check `ecu_unlocked`
  (the "WTF?" dev-mode unlock)**, unlike the raw peek/poke interface in §8.
- **TCU:** the receive handler for 0x360 has no gate.

What writes do, by region:

| Region | Effect of DNLOAD |
|---|---|
| Calibration RAM copy (`CAL_base` … `CAL_base+size`) | **Live calibration**: takes effect on the next use of that value. Lost at power-off. |
| Live variables (.data/.bss) | Usually overwritten again by the engine code within one loop; some flags/state can be forced. |
| CCP/DAQ tables, stack, other RAM | Can crash the ECU. |
| Peripheral registers (0xC3F…, 0xFFF…) | Direct hardware access (eTPU, SIU outputs, FlexCAN, …): **can fire outputs or knock the ECU off the bus**. |
| Flash (0x00000000–0x000FFFFF) | A plain store does not program MPC5534 flash; it is ignored at best and may raise an exception (reset). |

Recommendations:
- In the A2L, declare **only the calibration RAM range** as a writable CHARACTERISTIC area.
- Edit tables with the engine off where possible. Multi-byte table/axis edits are not atomic: a lookup can
  see a half-written table.
- To make a tune permanent, copy the edited RAM image back into the calibration file and reflash. Read it back
  with UPLOAD, or verify with BUILD_CHKSUM.

---

## 7. Per-build addresses

`CAL_base` is the destination of `copyCAL2RAM` (calibration copied from flash 0x20000). All CAL_* offsets are relative to it.

| Build | CAL_base / size | Enable byte | CCP ID string (RAM) | DAQ table base | CCP state flags | Dispatcher | RX handler(s) |
|---|---|---|---|---|---|---|---|
| C132E0044 (2010) | 0x40002768 / 0x4874 | — | 0x40001330 `Ccp_GD8` | ≈0x40009FD4 (`chlog_channel_table`) | `chlog_state_flags` | 0x92C4C | 0x961C8 (0x200) |
| B13200091 / C132E0077 | 0x40008E24 / 0x61D8 | 0x40008E4A (+0x26) | 0x40001490 | 0x400068FC | 0x40006528 | 0xA4540 | 0xA8FEC (A), 0xAC54C (C) |
| C132E0271 | 0x40008654 / 0x69AC | 0x400088EC (+0x298) | 0x40001588 | 0x40006AB0 | 0x400066D8 | 0xB0F40 | 0xB3FB8 (A), 0xB693C (C) |
| A132E0263 | 0x40008654 / 0x69AC | 0x400088EC | 0x40001588 | 0x40006C70 | 0x40006898 | 0xAF318 | 0xB2390 (A), 0xB4FA0 (C) |
| C132E0278 | 0x40008654 / 0x69AC | 0x400088EC | 0x40001588 | 0x40006C80 | 0x400068A8 | 0xAFDCC | 0xB2E44 (A), 0xB5A54 (C) |
| E132E0288 | 0x40008654 / 0x69AC | 0x400088EC | 0x40001700 | 0x40006E50 | — | 0xB4634 | 0xB76AC (A), 0xBA618 (C) |
| A138E0112 | 0x40008654 / 0x69AC | 0x400088EC | 0x400015E8 | 0x40006DB0 | 0x400069D8 | 0xB12C8 | 0xB4500 (A), 0xB707C (C) |
| C132F0395 (TCU) | 0x40008E58 / 0x61A8 | — | 0x40001420 `Ccp_TCU` | 0x40003854 | `chlog_streaming_control_flags` | 0x51358 | 0x54038 (A), 0x56528 (C) |

### Live variables: why one A2L per build is needed

Selected channels, from aligned code references of paired functions (agree/total votes in
[`ccp/ram_variable_map.tsv`](ccp/ram_variable_map.tsv); 945 variables). B13200091 values are that program's
own analyst labels.

| Variable (type) | B13200091 | C132E0271 | A132E0263 | C132E0278 | E132E0288 | A138E0112 |
|---|---|---|---|---|---|---|
| engine_speed_16bit (u16 rpm) | 0x4000163A | 0x4000175A | 0x40001762 | 0x40001762 | 0x400018D2 | 0x400017DA |
| obd_ii_engine_speed (u16 rpm/4) | 0x4000163C | 0x4000175C | 0x40001764 | 0x40001764 | 0x400018D4 | 0x400017DC |
| coolant_temp (u8 °C=x·5/8−40) | 0x400016FE | 0x4000182E | 0x40001836 | 0x40001836 | 0x400019A6 | 0x400018AE |
| temp_engine_air (u8 °C=x·5/8−40) | — | 0x40001832 | 0x4000183A | 0x4000183A | 0x400019AA | 0x400018B2 |
| air_temp_ambient (u8 °C=x·5/8−40) | — | 0x40001590 | 0x40001590 | 0x40001590 | 0x40001708 | 0x400015F0 |
| car_speed_x100 (u16 km/h/100) | 0x400018D8 | 0x40001A72 | 0x40001A92 | 0x40001A92 | 0x40001C02 | 0x40001B12 |
| load_mass_per_stroke (u8 4 mg/stroke) | 0x400019FB | 0x40001BAB | 0x40001BD3 | 0x40001BD3 | 0x40001D43 | 0x40001C5B |
| tps_16bit (u16 1/4095) | 0x40002242 | 0x400024E2 | 0x400024FA | 0x4000250A | 0x400026BA | 0x400025B2 |
| map (u16 mbar) | — | 0x40001874 | 0x4000187C | 0x4000187E | 0x400019EE | 0x400018F6 |
| afr_commanded (u16 AFR/100) | — | 0x4000196E | 0x40001976 | 0x40001976 | 0x40001AE6 | 0x400019F6 |
| ign_adv_target (i16 °/4) | — | 0x40001900 | 0x40001908 | 0x40001908 | 0x40001A78 | 0x40001988 |
| stft_bank1 / bank2 (i16 1/20) | 0x40001B1C / 1E | 0x40001CF4 / F6 | 0x40001D0C / 0E | 0x40001D0C / 0E | 0x40001E8C / 8E | 0x40001D94 / 96 |
| ltft_bank1 / bank2 (i16 0.1 %) | 0x40001B28 / 2A | 0x40001D00 / 02 | 0x40001D18 / 1A | 0x40001D18 / 1A | 0x40001E98 / 9A | 0x40001DA0 / A2 |

The offsets between builds are **not constant** (e.g. A132E0263 matches C132E0278 for some variables and not
others), so addresses must be looked up per variable, per build. Calibration addresses are more stable: C132E0278
and A138E0112 share ~96% of their calibration layout (see `disassembly/exige410/A138E0112/README.md`).

---

## 8. The other interface: raw memory peek/poke (0x50–0x57)

This is separate from CCP and EFI-specific. It is present in every T6 build and in the TCU (on other IDs). Details
in `ChannelLogger.md` and the `t6_dev_mode_can_logging` notes. Summary:

| | T6 ECUs (2010–Exige) | IPS TCU |
|---|---|---|
| Request IDs | 0x50/0x51/0x52 read 32/16/8-bit, 0x53 block read, 0x54–0x57 RAM write (FlexCAN A MB15) | 0x60… (MB15) |
| Reply ID | 0x7A0 (MB16; MB0 on 2010) | 0x7B0 (MB16) |
| Gate | `ecu_unlocked` = calibration bytes `"WTF?"` at `CAL_base`+0x61D4 (B13200091: 0x4000EFF8) / +0x69A6 (2017+: 0x4000EFFA); 2010: 0x40006FD6 | `tcu_unlocked` |
| Address limits | reads: RAM 0x40000000–0x4000FFFD or flash < 0xFFFFD; writes: RAM only | same |

The BLOWN-MOTOR image changes the unlock string to `"FAST"`.
On T6 2011+, CCP (§6) gives the same RAM read/write access **without** this unlock whenever the enable byte is non-zero.

---

## 9. Building an A2L / configuring a CCP tool

Minimum IF_DATA / device settings:

| Setting | T6 2011+ | 2010 C132E0044 | IPS TCU |
|---|---|---|---|
| Protocol | CCP 2.1 | CCP 2.1 | CCP 2.1 |
| CRO ID | 0x350 | 0x200 | 0x360 |
| DTO ID | 0x351 | 0x201 | 0x361 |
| Station address | 0x0000 | 0x0000 | 0x0000 |
| Byte order | MSB_FIRST (big-endian PowerPC) | same | same |
| Seed & key | none (do not send GET_SEED) | none | none |
| DAQ lists | 10 × 12 ODTs, first PID = list×12 | 1 × 10 ODTs, first PID 0 | 10 × 12 |
| ODT entries | 7 (use 0–6), sizes 1/2/4 | same | same |
| Event channels | 0–9 (rates §5) | 0 (100 Hz) | 0–9 |
| Writable (CAL) area | `CAL_base` … +size (§7) | 0x40002768 … +0x4874 | 0x40008E58 … +0x61A8 |

**Identifying the build:** EXCHANGE_ID returns `Ccp_T6` for every 2011+ engine ECU, so read a build string
with SHORT_UP after connecting:
- `CAL_prog_version`: calibration ID string, `CAL_base`+0x59D4 (verified in C132E0278 and A138E0112; check other builds); e.g. "LOTUS EVORA GT430 MAN MY19 ROW".
- The program's version string in flash. For C132E0278, `ecu_prog_version` is at 0xCE958.

Pick the A2L that matches.

**Template:** the Vector CCP driver 1.42 package (see §13 sources) includes a CANape sample project with an A2L
(`CCP/SAMPLES/C16X/CCP_TEST/CANAPE/CCPTEST.A2L`). Its module-level `IF_DATA ASAP1B_CCP` block, including the
`RASTER` event-channel definitions, is a starting point. Replace the CAN IDs, station address, DAQ list/ODT counts
and event channels with the values above. The sample targets a little-endian C16x (`BYTE_ORDER MSB_LAST`); the T6
needs `MSB_FIRST`.

**Generating content:** variable names/types come from the Ghidra labels. The unit typedefs (`u16_rspeed_rpm`,
`u8_temp_5/8-40c`, …) encode scaling directly as COMPU_METHODs. Calibration tables come from the CAL_* labels
(RomRaider defs already carry the same scaling). Per-build RAM addresses come from `ccp/ram_variable_map.tsv`.

---

## 10. Example session

T6 2011+, enable byte = 1, FlexCAN A. Log engine speed (u16) + coolant (u8) at 100 Hz on DAQ list 0.
Addresses are for **A138E0112**. Bytes are hex, CTR increments per command.

```
CRO 350: 01 00 00 00 .. .. .. ..     CONNECT station 0         → 351: FF 00 00 ...
CRO 350: 1B 01 02 01                 GET_CCP_VERSION 2.1       → 351: FF 00 01 02 01
CRO 350: 14 02 00 00 00 00 03 51     GET_DAQ_SIZE list 0       → 351: FF 00 02 0C 00   (12 ODTs, first PID 0)
CRO 350: 15 03 00 00 00              SET_DAQ_PTR list0 odt0 el0
CRO 350: 16 04 02 00 40 00 17 DA     WRITE_DAQ size 2 @0x400017DA (engine_speed_16bit)
CRO 350: 15 05 00 00 01              SET_DAQ_PTR list0 odt0 el1
CRO 350: 16 06 01 00 40 00 18 AE     WRITE_DAQ size 1 @0x400018AE (coolant_temp)
CRO 350: 06 07 02 00 00 00 00 01     START_STOP prepare list0, last ODT 0, event 0, prescaler 1
CRO 350: 08 08 01                    START_STOP_ALL start
DTO 351: 00 rpm_hi rpm_lo clt ...    PID 0 at 100 Hz (bit7 of PID set = frames were dropped)
...
CRO 350: 08 09 00                    START_STOP_ALL stop
CRO 350: 07 0A 01                    DISCONNECT end of session
```

Live calibration write (engine off): `SET_MTA 0 → <CAL address>`, then `DNLOAD n bytes`, then verify with `UPLOAD` or `BUILD_CHKSUM`.

---

## 11. Firmware references

Function addresses per build (names as labelled in each Ghidra program):

| Function | B13200091 | C132E0278 | A138E0112 | C132E0044 | C132F0395 (TCU) |
|---|---|---|---|---|---|
| Command dispatcher | `chlog_command_dispatch` 0xA4540 | 0xAFDCC | `logger_command_dispatch_0x350` 0xB12C8 | `chlog_command_dispatch` 0x92C4C | `chlog_command_handler` 0x51358 |
| GET_DAQ_SIZE (list init) | `chlog_group_init` 0xA3E00 | 0xAF660 | 0xB0B5C | 0x92530 | 0x50C14 |
| START_STOP prepare | `chlog_group_set_timing` 0xA3E84 | 0xAF6E4 | 0xB0BE0 | 0x925B4 | 0x50C98 |
| START_STOP start | `chlog_group_arm` 0xA3F30 | 0xAF790 | 0xB0C8C | 0x92660 | 0x50D44 |
| START_STOP_ALL start / stop | 0xA3F80 / 0xA4070 | 0xAF7E0 / 0xAF8D0 | 0xB0CDC / 0xB0DCC | 0x926B0 / 0x927A0 | 0x50D94 / 0x50E84 |
| DTO builder (ODT sample) | `flexcan_build_telemetry_message2` 0xA40D8 | 0xAF938 | `chlog_build_frame` 0xB0E34 | — | — |
| DAQ scheduler (per event) | `flexcan_counters_scheduler_tick` 0xA41D0 | 0xAFA5C | 0xB0F58 | via `chlog_scheduler_tick_2khz` 0x90D78 | 0x50FE8 |
| DNLOAD / UPLOAD core | `diag_stream_write` 0xA3D8C / `_read` 0xA3DEC | 0xAF5EC / 0xAF64C | 0xB0AE8 / 0xB0B48 | — | `chlog_write_memory_block` 0x50BA0 |
| BUILD_CHKSUM continuation | `diag_stream_tx_continue` 0xA4448 | 0xAFCD4 | `logger_region_checksum_continue` 0xB11D0 | — | — |
| DTO TX data / reply | 0xA39D8 / 0xA3B08 | 0xAF238 / 0xAF368 | 0xB0734 / 0xB0864 | 0x92264 / 0x922E8 | 0x50814 / 0x50930 |

Other builds: dispatcher C132E0271 0xB0F40, A132E0263 0xAF318, E132E0288 0xB4634. Their helpers follow the same order
(init, set-timing, arm, start-all, stop, stop-all) immediately before the dispatcher.

Analysis scripts (`disassembly/ghidra_scripts/`):
- `CcpSurvey.java`: finds the `Ccp_*` string, the dispatcher, the RX handlers, the DAQ strides and the calibration copy.
- `CcpSurvey2.java`: CAN IDs and mailboxes per function.
- `FindCcpDispatcher.java`: heuristic; it misses jump-table dispatchers.
- `CalCopyDecoder.java`: decodes `copyCAL2RAM`.

Some Ghidra labels predate the CCP identification. Their Vector CCP driver 1.42 equivalents (A138E0112 addresses):

| Ghidra label | Vector `ccp.c` name | Address |
|---|---|---|
| `logger_command_dispatch_0x350` | `ccpCommand()` | 0xB12C8 |
| `chlog_group_init` | `ccpClearDaqList()` (returns `CCP_MAX_ODT`, sets `SS_DAQ`, `ccpQueueInit`) | 0xB0B5C |
| `chlog_group_set_timing` | `ccpPrepareDaq()` (prescaler 0→1, cycle = 1, flags = `DAQ_FLAG_PREPARED`) | 0xB0BE0 |
| `chlog_group_arm` | `ccpStartDaq()` | 0xB0C8C |
| `chlog_start_all` | `ccpStartAllPreparedDaq()` | 0xB0CDC |
| `chlog_group_stop` / `chlog_stop_all` | `ccpStopDaq()` / `ccpStopAllDaq()` | 0xB0D4C / 0xB0DCC |
| `telemetry_queue_reset` / `chlog_tx_ring_push` | `ccpQueueInit()` / `ccpQueueWrite()` | 0xB09BC / 0xB09E0 |
| `chlog_build_frame` | `ccpSampleAndTransmitDtm()` | 0xB0E34 |
| `flexcan_counters_scheduler_tick` | `ccpDaq(eventChannel)` | 0xB0F58 |
| `logger_region_checksum_continue` (called via `flexcan_diagnostics_tx` in main) | `ccpBackground()` (checksum part) | 0xB11D0 |
| `diag_stream_write` / `diag_stream_read` | `ccpWriteMTA()` / `ccpReadMTA()` | 0xB0AE8 / 0xB0B48 |
| `chlog_decode_address` | `ccpGetPointer()` | 0xB1BC0 |
| `logger_store_pointer_cmd11` | `ccpSetCalPage()` (GET reads it back: `ccpGetCalPage`) | 0xB1B68 |
| `clear_can_logging_buffer` | `ccpInit()` (zero the whole `ccp` struct) | 0xB1A64 |
| `flexcan_c_tx_351_redirect2` | `ccpSendCrm()` | 0xB0994 |
| `logger_response` | `ccp.Crm[8]` | 0x400069D0 |
| `flexcan_tx_queue_state_flags` | `ccp.SessionStatus` (`SS_DAQ` 0x02, `SS_TMP_DISCONNECTED` 0x10, `SS_CONNECTED` 0x20, `SS_RUN` 0x80; 0x08 is EFI's TX-drain flag) | 0x400069D8 |
| (unlabelled) | `ccp.SendStatus` | 0x400069D9 |
| (unlabelled) / `logger_checksum_ptr` | `ccp.MTA[0]` / `ccp.MTA[1]` | 0x400069DC / 0x400069E0 |
| `chlog_tx_ring_count` / `_head` / `chlog_tx_ring` | `ccp.Queue.len` / `.rp` / `.msg[120]` | 0x400069E4 / E5 / E6 |
| `chlog_channel_table` + `chlog_group0_*` | `ccp.DaqList[10]` | 0x40006DB0 |
| `logger_checksum_bytes_remaining` | `ccp.CheckSumSize` (`CCP_WORD`) | 0x40007E6A |

EFI's changes to the driver:
- **Integration:** the calibration enable/bus-select byte and dual FlexCAN A/C routing.
- **Event channels:** 10 event channels ticked from the 2 kHz ISR.
- **DAQ list layout (2017+ builds):** split address and size arrays (stride 0x1AC) instead of Vector's
  `{ptr,siz}` entries (stride 0x2A8, kept in B13200091 and the TCU).
- **Session byte:** the extra 0x08 TX-drain bit.
- **CONNECT:** only stops DAQ when it wasn't already connected.
- **BUILD_CHKSUM:** no ≥64 KiB range check.
- **ID strings:** `Ccp_T6` / `Ccp_GD8` / `Ccp_TCU` (Vector's default is "ECU00001").

---

## 12. Open items

1. **Bench test:** the whole guide is from static analysis; confirm with a real CCP master.
2. **Enable byte in stock calibrations:** whether `CAL_logger_high_speed_logger_enable` is 0 on production cars.
   The Exige 410 image has no calibration.
3. **Event rates per build:** the 100/100/50/50/20/20/10/10/2/1 Hz figures come from B13200091 only.
4. **FlexCAN C bitrate and bus wiring per vehicle.**
5. **Emira:** `Ccp_G12` is in the image, but no dispatcher has been located (different VLE codebase, MPC5777C).
6. **TCU:** confirm that 0x360/0x361 are actually configured and reachable on the vehicle buses.
   Confirm the 0x60… peek/poke ID range and how `tcu_unlocked` is set.
7. **E132E0288 state-flag address** not extracted.
8. **Master compatibility:** with the chosen CCP master, check that DNLOAD writes are accepted despite the missing
   MTA0 in the reply, and that PIDs with bit 7 set (overrun) are handled (§4, §5, §13).
9. **Ghidra labels:** the `chlog_*` / `logger_*` / `diag_stream_*` labels could be renamed to the Vector driver names
   in §11, so exports read like `ccp.c`.
10. **Old notes:** `romraider-defs - Copy/disassembly_references/ChannelLogger.md` still describes this as a
    proprietary logger. It needs a pointer to this guide.

---

## 13. Verification: CCP 2.1 vs alternatives

Checked on 2026-10-05 against two references:
- the **ASAP CCP 2.1 specification** (H. Kleinknecht, 18 Feb 1999);
- the **Vector Informatik CCP driver 1.42** source (`CCP.C`/`CCP.H`, Oct 2003).

Firmware facts come from the decompiled dispatchers of A138E0112, C132E0278, B13200091 and C132E0044.

Method:
- **Spec:** text extracted from the PDF.
- **Driver:** `ccp1.42.zip` contains a self-extracting `ccp.exe`. Only its text sources (`CCP/CCP.C`, `CCP/CCP.H`,
  `CCP/CCPPAR.H`, `README.TXT`) were read out of the embedded zip; nothing was executed.
- **XCP codes:** the repository's XCP package is an installer that can't be read passively, so it was not run.
  The XCP command and error codes come from pyXCP instead.

### Hypotheses

- **H0, CCP 2.1** (ASAM/ASAP CCP, implemented with the Vector driver).
- **H1, XCP on CAN** (ASAM MCD-1 XCP, CCP's successor; same purpose, often confused with CCP).
- **H2, a proprietary EFI Technology protocol** that only resembles CCP or borrowed its name (the original
  "channel logger" reading of this code).

### Discriminating tests

| # | Test | H0 CCP 2.1 predicts | H1 XCP predicts | H2 proprietary predicts | Firmware | Verdict |
|---|---|---|---|---|---|---|
| 1 | Command codes | CONNECT 0x01, SET_MTA 0x02, DNLOAD 0x03, UPLOAD 0x04 … GET_CCP_VERSION 0x1B, DNLOAD_6 0x23 | CONNECT 0xFF, SET_MTA 0xF6, UPLOAD 0xF5, DOWNLOAD 0xF0, SET_DAQ_PTR 0xE2 … | arbitrary | 18 cases, all exactly the CCP codes; 0xFF-range commands are ignored | H0 |
| 2 | Reply framing | `[0xFF, ERR, CTR, …]`, CTR echoed; DAQ `[PID 0–0xFD, data]` | positive `[0xFF, data…]` with no error byte or CTR; errors `[0xFE, code]` | arbitrary | `[0xFF, ERR, CTR, …]` with CTR echoed; no 0xFE packets | H0 |
| 3 | Error code meanings | 0x22 DAQ init request, 0x30 unknown cmd, 0x31 syntax, 0x32 out of range, 0x33 access denied | 0x20 unknown, 0x21 syntax, 0x22 out of range, 0x24 access denied, 0x30 memory overflow, 0x31 generic, 0x32 verify | arbitrary | 0x22 used exactly for "no GET_DAQ_SIZE yet", 0x30 for unknown cmd, 0x31 bad mode/index, 0x32 MTA ≠ 0, 0x33 write failure | H0 (XCP semantics would be nonsensical) |
| 4 | Parameter layouts | spec positions: station address word at bytes 2–3 (Intel), address extension at byte 3, GET_DAQ_SIZE DTO-ID at 4–7, WRITE_DAQ sizes {1,2,4}, START_STOP mode/list/last/event/prescaler at 2/3/4/5/6–7 | different (no CTR; e.g. XCP DAQ uses SET_DAQ_LIST_MODE) | free choice | every implemented command matches the spec position for position, including fields the firmware **ignores** (address extension, DTO-ID) | H0; an in-house protocol would not reserve fields it never uses |
| 5 | EXCHANGE_ID semantics | reply `[len, type, avail mask, prot mask, x]`, CAL = bit 0, DAQ = bit 1; MTA0 set to the ID, read back with UPLOAD | GET_ID 0xFA, different layout | — | `[len, 0, 0x03, 0x00, 0x8E]`, MTA0 → "Ccp_T6" | H0 |
| 6 | Mandatory command set | all 11 non-optional CCP commands present | — | — | CONNECT, GET_CCP_VERSION, EXCHANGE_ID, SET_MTA, DNLOAD, UPLOAD, GET_DAQ_SIZE, SET_DAQ_PTR, WRITE_DAQ, START_STOP, DISCONNECT all present | H0 |
| 7 | Code-level identity with a known CCP stack | — | — | none expected | Vector driver 1.42 fingerprints: EXCHANGE_ID byte 7 = 0x8E = `CCP_DRIVER_VERSION` 142; CONNECT reply `Crm[3]=0xFE`; SET_MTA accepts only `mta < CCP_MAX_MTA-1`; `comIdx>7` element bug; overrun bit-7 PID logic and "skip list if queue can't hold it"; queue = `CCP_MAX_ODT×CCP_MAX_DAQ` (120; 10 on 2010); 256-byte checksum blocks with the n=0 wrap; `ccp_t` field order and `SS_*`/`DAQ_FLAG_*` values; `ccpDaqList_t` padding giving B13200091's 0x2A8 stride | H0; rules out an independent implementation |

### Version: 2.1 rather than 2.0 or older

The spec's revision history lists items added after 2.0 (proposal 2.01, Mar 1998), which became active in 2.1 (Feb 1999):
- the commands **TEST**, **GET_CCP_VERSION**, **GET_ACTIVE_CAL_PAGE** and **START_STOP_ALL**;
- **START_STOP with event channel + prescaler**.

The firmware implements all of these. GET_CCP_VERSION returns **2.1** (the driver's `CCP_VERSION_MAJOR/MINOR` = 2/1).
The spec also requires GET_ACTIVE_CAL_PAGE whenever SELECT_CAL_PAGE is implemented, and both are present.

### Result

**H0 confirmed: CCP 2.1, implemented with Vector's CCP driver v1.42. H1 (XCP) and H2 (proprietary) rejected.**

Remaining differences from the spec, all documented above:
- no MTA0 in DNLOAD replies;
- the PID bit-7 overrun marker, a Vector/CANape extension, used instead of 0xFE event messages;
- SET_DAQ_PTR accepts element 7;
- TEST to another station causes a temporary disconnect;
- BUILD_CHKSUM uses only 16 bits of the size.

The overrun bit is a CANape feature. A master that checks the MTA0 returned by DNLOAD could reject writes; confirm with the chosen tool on the bench.

### Sources

- ASAP Standard CCP, CAN Calibration Protocol Version 2.1, 18 Feb 1999:
  <https://automotivetechis.wordpress.com/wp-content/uploads/2012/06/ccp211.pdf>
- Vector CCP driver 1.42 (`ccp1.42.zip`, contains `CCP/CCP.C`, `CCP/CCP.H`, copyright Vector Informatik 2001–2003):
  <https://github.com/shijia132/CCP-XCP-source-code>
- Vector-derived CCP header (command/return codes, `SS_*`, `DAQ_FLAG_*`, `CCP_DRIVER_VERSION 142`):
  <https://github.com/quyong123/CanCom/blob/master/ccpparam.h>
- XCP command and error codes (pyXCP): <https://github.com/christoph2/pyxcp/blob/master/pyxcp/types.py>
- ASAM MCD-1 CCP overview: <https://www.asam.net/standards/detail/mcd-1-ccp/>
- Kvaser, CCP/XCP overview: <https://kvaser.com/about-can/higher-layer-protocols/ccpxcp/>
- Vector application note AN-IMC-1-001, *Integration of the Vector CCP Driver with a free CAN Driver*:
  <https://automotivetechis.wordpress.com/wp-content/uploads/2012/06/an-imc-1-001_integration_of-the_vector_ccp_driver_with_a_free_can_driver.pdf>
