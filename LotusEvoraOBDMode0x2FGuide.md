# OBD-II Mode 0x2F (InputOutputControlByIdentifier) Guide

## For the Lotus Evora ECU (T6e Platform)

This guide covers UDS Service 0x2F as defined by ISO 14229-1 and its specific implementation on the Lotus Evora T6e engine ECU (firmware variants E132E0288 / C132E0278 / B13200091).

---

## 1. What Is Mode 0x2F?

Mode 0x2F (**InputOutputControlByIdentifier**) is a UDS diagnostic service that allows a scan tool to directly command ECU actuators and override I/O signals. It is used for production-line end-of-line testing, workshop troubleshooting, and component verification.

Typical use cases:

- Firing individual fuel injectors to check spray pattern
- Test-firing ignition coils to verify spark
- Cycling the fuel pump relay
- Commanding the radiator fan to a specific duty cycle
- Activating warning lamps (MIL, oil pressure)
- Actuating the exhaust bypass valve
- Testing the EVAP purge solenoid

The service allows a tester to take temporary control of an output away from the ECU's normal control logic. When the test ends (either explicitly or by timeout), normal ECU control resumes automatically.

---

## 2. CAN Bus Addressing

On the Lotus Evora, OBD-II communication uses standard 11-bit CAN identifiers on FlexCAN A:

| Direction | CAN ID | Description |
|-----------|--------|-------------|
| Tester -> ECU (broadcast) | `0x7DF` | Functional addressing (all ECUs) |
| Tester -> ECU (physical) | `0x7E0` | Physical addressing (engine ECU) |
| ECU -> Tester | `0x7E8` | Engine ECU response |

Mode 0x2F requests should use physical addressing (`0x7E0`) since they target a specific ECU.

---

## 3. Message Format

### 3.1 Request Message

The request is sent as an ISO-TP (ISO 15765-2) message over CAN. For most mode 0x2F PIDs on the Evora, the request fits in a single CAN frame.

**Single-frame format (most actuator commands):**

```
Byte 0: ISO-TP PCI byte (0x04 or 0x05 = single frame, length 4 or 5)
Byte 1: Service ID = 0x2F
Byte 2: PID high byte
Byte 3: PID low byte
Byte 4: Control parameter (actuator value)
Byte 5: (optional, for 2-byte parameters like tachometer)
```

The Lotus Evora firmware does not use the standard UDS `inputOutputControlParameter` sub-function codes (returnControlToECU=0x00, shortTermAdjustment=0x03, etc.) as a separate byte. Instead, the control parameter byte directly carries the commanded value:

- **`0x00`** = Turn off / return control to ECU
- **`0xFF`** = Turn on (for on/off actuators like relays and lamps)
- **`0x01`-`0xFE`** = Duty cycle or analog value (for proportional actuators)

**Example: Turn on fuel pump relay (PID 0x141)**
```
CAN ID: 0x7E0
Data:   04 2F 01 41 FF 00 00 00
        ^  ^  ^---^ ^
        |  |  PID   value (0xFF = ON)
        |  SID
        ISO-TP single frame, 4 bytes
```

**Example: Turn off fuel pump relay**
```
CAN ID: 0x7E0
Data:   04 2F 01 41 00 00 00 00
                       ^
                       value (0x00 = OFF / return to ECU control)
```

**Example: Command radiator fan to 50% duty cycle (PID 0x150)**
```
CAN ID: 0x7E0
Data:   04 2F 01 50 80 00 00 00
                       ^
                       0x80 = 128/255 = ~50% duty cycle
```

**Example: Command tachometer (PID 0x128, 2-byte value)**
```
CAN ID: 0x7E0
Data:   05 2F 01 28 03 E8 00 00
        ^            ^---^
        5 bytes      0x03E8 = 1000; actual RPM = 1000 * 4 = 4000 RPM
```

### 3.2 Supported PID Query

To discover which actuator PIDs are available, send a query with a PID at a range boundary (0x100, 0x120, 0x140, 0x160, 0x180). The ECU responds with a 4-byte bitmask indicating which PIDs in the next 32-PID range are supported.

**Request: Query supported PIDs 0x101-0x11F**
```
CAN ID: 0x7E0
Data:   04 2F 01 00 00 00 00 00
              ^---^
              PID 0x100 = "which PIDs 0x101-0x11F are supported?"
```

**Response:**
```
CAN ID: 0x7E8
Data:   06 6F 01 00 FC 00 00 01
        ^  ^  ^---^ ^------^  ^
        |  |  PID   bitmask   01 = more PIDs in next range
        |  positive response SID
        ISO-TP single frame, 6 bytes
```

The bitmask `0xFC000001` decodes as:
- Bit 31 (0x80000000) = PID 0x101 -- not set
- Bit 30 (0x40000000) = PID 0x102 -- not set
  Wait -- `0xFC` = `11111100` binary, so:
- Bit 31 = PID 0x101 -- supported (1)
- Bit 30 = PID 0x102 -- supported (1)
- Bit 29 = PID 0x103 -- supported (1)
- Bit 28 = PID 0x104 -- supported (1)
- Bit 27 = PID 0x105 -- supported (1)
- Bit 26 = PID 0x106 -- supported (1)
- Bit 25 = PID 0x107 -- not supported (0)
- Bit 24 = PID 0x108 -- not supported (0)
- Last byte `0x01` = Bit 0 = PID 0x120 -- supported (indicates more ranges follow)

**Chained queries:** When the response has PID 0xX20 (the last bit) set to 1, it means the ECU supports another range of PIDs. Send a follow-up query at the next boundary:

```
0x100 -> discover PIDs 0x101-0x11F (injectors 1-6)
0x120 -> discover PIDs 0x121-0x13F (VVTi, EVAP, tach, TC light)
0x140 -> discover PIDs 0x141-0x15F (fuel pump, lambda, fans, exhaust flap, etc.)
0x160 -> discover PIDs 0x161-0x17F (coils 1-6, VIMS)
0x180 -> discover PIDs 0x181-0x19F (cranking relay, break-in flag)
```

### 3.3 Positive Response

The ECU responds with SID `0x6F` followed by the echoed PID and the current actuator status:

```
Byte 0: ISO-TP PCI
Byte 1: 0x6F (positive response)
Byte 2: PID high byte (echo)
Byte 3: PID low byte (echo)
Byte 4: Status value (echoed command or current state)
```

### 3.4 Negative Response

When preconditions are not met, the ECU sends a negative response:

```
Byte 0: ISO-TP PCI (0x03 = 3 bytes)
Byte 1: 0x7F (negative response)
Byte 2: 0x2F (echoed SID)
Byte 3: 0x22 (NRC: conditionsNotCorrect)
```

NRC `0x22` means the ECU's preconditions for the requested test are not satisfied (e.g., engine is still running when it must be off).

---

## 4. Timing and Message Repetition

### 4.1 Inactivity Timeout (Critical)

The Evora ECU implements a **mode 0x2F inactivity watchdog timer** (`CAL_obd_ii_mode2f_timeout`, type `u16_time_100ms`). Every time a mode 0x2F command is received, this timer is reloaded to its calibrated value. The timer is decremented at 200 Hz (every 5 ms).

**When the timer reaches zero, ALL active mode 0x2F tests are immediately cancelled:**
- All flags in `obd_ii_mode2f_flags_enabled` are cleared to 0
- All flags in `obd_ii_mode2f_flags_state` are cleared to 0
- All actuator command values (VVTi, tach, EVAP purge, cooling fan, CAC pump) are zeroed
- Normal ECU control of all outputs resumes

**This means you MUST periodically re-send mode 0x2F commands to keep actuator tests alive.** If the ECU does not receive any mode 0x2F request within the timeout period, all tests are automatically stopped as a safety measure.

### 4.2 Recommended Repeat Rate

To keep an actuator test active, re-send the same mode 0x2F command at a regular interval well within the timeout period. A practical approach:

- **Repeat interval: 500 ms to 1000 ms** (safe margin for typical timeout calibration)
- The exact timeout value depends on the calibration data at address `0x4000ca8c`. The type is `u16_time_100ms`, meaning the value is in units of 100 ms. A typical calibration of, say, 50 would give a 5-second timeout.
- Always choose a repeat rate that is at most **half** the timeout value to provide margin for CAN bus latency and scheduling jitter.

### 4.3 Engine-Off Delay Timer

After the engine stops, the ECU enforces a cooldown delay (`CAL_ecu_obd_mode2F_timer`, type `u16_time_5ms`) before accepting actuator tests. The `obd_ii_mode2f_200hz` function decrements this timer at 200 Hz. Until it reaches zero, most mode 0x2F PIDs will respond with NRC `0x22` (conditionsNotCorrect).

**Practical implication:** After turning off the engine, wait a few seconds before sending actuator test commands. If you receive NRC `0x22`, continue retrying at 1-second intervals until the ECU accepts the command.

### 4.4 Per-Test Duration Timers (Injectors and Coils)

Injector and coil test-firing PIDs use individual countdown timers (`obd_ii_mode2f_test_timers[0..5]`). These are loaded from calibration values when the test starts and decremented at 200 Hz. When a timer reaches zero, that specific test automatically ends and the `coil_driver_state_machine()` function restores normal driver state.

**For injectors:** The test duration is loaded from calibration at `DAT_4000ca96` (type `u16_time_5ms`). The injector fires repeatedly at a cadence set by `DAT_4000ca92` until the timer expires.

**For coils:** The test duration is loaded from calibration at `DAT_4000caae` (type `u16_time_5ms`). The coil fires repeatedly at a cadence set by `DAT_4000caad` until the timer expires.

To restart an injector or coil test after it auto-expires, send the command again. However, note that an injector/coil test that has already been fired (tracked by `DAT_40002388` flags) will not re-initialize via the same request -- the flags must be cleared first, typically by the timeout expiry or by sending value `0x00` to cancel the test.

### 4.5 Mutual Exclusion

**Injectors and coils cannot be tested simultaneously.** The firmware enforces this:
- Injector PIDs (0x101-0x106) check that no coil tests are active: `(obd_ii_mode2f_flags_enabled & 0xFC0) == 0`
- Coil PIDs (0x161-0x166) check that no injector tests are active: `(obd_ii_mode2f_flags_enabled & 0x3F) == 0`

If you attempt to activate an injector while a coil test is running (or vice versa), the ECU will send NRC `0x22`.

---

## 5. Available PIDs on the Lotus Evora

### 5.1 PID Range 0x100 (Supported PID Query + Injectors)

| PID | Actuator | Value Format | Preconditions |
|-----|----------|-------------|---------------|
| `0x100` | Supported PIDs 0x101-0x11F | Query (no value) | Always accepted |
| `0x101` | Injector 1 test fire | `0x00`=off, nonzero=on | Engine off, no coil test active |
| `0x102` | Injector 2 test fire | `0x00`=off, nonzero=on | Engine off, no coil test active |
| `0x103` | Injector 3 test fire | `0x00`=off, nonzero=on | Engine off, no coil test active |
| `0x104` | Injector 4 test fire | `0x00`=off, nonzero=on | Engine off, no coil test active |
| `0x105` | Injector 5 test fire | `0x00`=off, nonzero=on | Engine off, no coil test active |
| `0x106` | Injector 6 test fire | `0x00`=off, nonzero=on | Engine off, no coil test active |

### 5.2 PID Range 0x120 (VVTi, EVAP, Tach, TC)

| PID | Actuator | Value Format | Preconditions |
|-----|----------|-------------|---------------|
| `0x120` | Supported PIDs 0x121-0x13F | Query (no value) | Always accepted |
| `0x121` | VVTi solenoid (both banks) | `0x00`=off, `0x04`-`0xFF`=duty cycle | Engine off |
| `0x127` | EVAP purge valve | `0x00`=off, `0x04`-`0xFF`=duty cycle | Engine off |
| `0x128` | Tachometer commanded RPM | 2-byte value: RPM/4 (big-endian) | Engine off |
| `0x12A` | Traction control dash light | `0x00`=off, `0xFF`=on | TC feature must be coded on |

**VVTi / EVAP note:** Values below `0x04` are clamped to `0x04` (minimum duty cycle of ~1.6%).

**Tachometer note:** The 2-byte value is RPM divided by 4. For example, to command 3000 RPM, send `0x02 0xEE` (750 decimal = 3000/4). Maximum is 9000 RPM (value 36000/4 = 0x2328). Values above 9000 RPM are clamped to 9000.

### 5.3 PID Range 0x140 (Fuel Pump, Lamps, Fans, Exhaust, Misc.)

| PID | Actuator | Value Format | Preconditions |
|-----|----------|-------------|---------------|
| `0x140` | Supported PIDs 0x141-0x15F | Query (no value) | Always accepted |
| `0x141` | Fuel pump relay | `0x00`=off, `0xFF`=on | Engine off |
| `0x143` | Wideband O2 sensor heater | `0x00`=off, nonzero=on | Engine off |
| `0x144` | Actuator (unknown) | `0x00`=off, nonzero=on | Engine off |
| `0x146` | Actuator (unknown) | `0x00`=off, `0xFF`=on | Engine off |
| `0x147` | MIL (Check Engine Light) | `0x00`=off, nonzero=on | Engine off |
| `0x148` | Oil pressure warning lamp | `0x00`=off, nonzero=on | Engine off |
| `0x149` | Warning lamp (unknown) | `0x00`=off, nonzero=on | Engine off |
| `0x14C` | Engine bay cooling fan | `0x00`=off, `0xFF`=on | Engine off |
| `0x14E` | Exhaust bypass valve/flap | `0x00`=off, `0xFF`=on | Vehicle stationary (speed=0); engine may be running |
| `0x150` | Radiator fan duty cycle | `0x00`=off, `0x01`-`0xFF`=duty cycle | Engine off |
| `0x151` | CAC water pump duty cycle | `0x00`=off, `0x01`-`0xFF`=duty cycle | Engine off; CAC feature coded on (supercharged models) |
| `0x152` | Actuator (unknown, guarded) | `0x00`=off, `0xFF`=on | Engine off; additional hardware guard |
| `0x153` | IPS transmission oil pump | `0x00`=off, nonzero=on | No preconditions (always accepted) |

**Exhaust bypass valve note:** PID `0x14E` is unique in that it can be activated while the engine is running, as long as vehicle speed is zero. This allows testing the exhaust flap while at idle.

### 5.4 PID Range 0x160 (Ignition Coils + VIMS)

| PID | Actuator | Value Format | Preconditions |
|-----|----------|-------------|---------------|
| `0x160` | Supported PIDs 0x161-0x17F | Query (no value) | Always accepted |
| `0x161` | Ignition coil 1 test fire | `0x00`=off, nonzero=on | Engine off, no injector test active |
| `0x162` | Ignition coil 2 test fire | `0x00`=off, nonzero=on | Engine off, no injector test active |
| `0x163` | Ignition coil 3 test fire | `0x00`=off, nonzero=on | Engine off, no injector test active |
| `0x164` | Ignition coil 4 test fire | `0x00`=off, nonzero=on | Engine off, no injector test active |
| `0x165` | Ignition coil 5 test fire | `0x00`=off, nonzero=on | Engine off, no injector test active |
| `0x166` | Ignition coil 6 test fire | `0x00`=off, nonzero=on | Engine off, no injector test active |
| `0x167` | Variable intake manifold (VIMS) solenoid | `0x00`=off, nonzero=on | Engine off |

### 5.5 PID Range 0x180 (Cranking, Misc.)

| PID | Actuator | Value Format | Preconditions |
|-----|----------|-------------|---------------|
| `0x180` | Supported PIDs 0x181-0x19F | Query (no value) | Always accepted |
| `0x184` | Cranking relay test | `0x00`=off, nonzero=on | Ignition OFF and engine speed = 0 |
| `0x185` | Engine break-in distance complete flag | `0x00`=read only, nonzero=set flag | No preconditions |

**PID `0x170`** (not in the supported PID bitmask but handled in firmware): Sets an OBD diagnostics override flag. This does not control a physical actuator.

---

## 6. Scan Tool Implementation Sequence

### 6.1 Discovering Available PIDs

```
1. Send: [2F 01 00 00]        -> Get supported PIDs 0x101-0x11F
2. If response byte 6 bit 0 = 1:
   Send: [2F 01 20 00]        -> Get supported PIDs 0x121-0x13F
3. If response byte 6 bit 0 = 1:
   Send: [2F 01 40 00]        -> Get supported PIDs 0x141-0x15F
4. If response byte 6 bit 0 = 1:
   Send: [2F 01 60 00]        -> Get supported PIDs 0x161-0x17F
5. If response byte 6 bit 0 = 1:
   Send: [2F 01 80 00]        -> Get supported PIDs 0x181-0x19F
6. Continue until the continuation bit is 0.
```

Decode each 4-byte bitmask: bit 31 (MSB of byte 3 in response) = first PID in range, bit 0 (LSB of byte 6 in response) = last PID / continuation flag.

### 6.2 Activating an Actuator

```
1. Verify engine is off (for most PIDs) -- wait for engine-off delay
2. Send the mode 0x2F command with the desired PID and value
3. Check for positive response (0x6F) vs negative response (0x7F)
4. If positive: start a repeat timer
5. Re-send the same command every 500-1000 ms to prevent timeout
6. To stop: send the same PID with value 0x00, or simply stop sending
   (the ECU timeout will automatically cancel the test)
```

### 6.3 Typical Session Flow

```python
# Pseudocode for actuating the fuel pump

def activate_fuel_pump(can_bus):
    while test_active:
        # Send mode 0x2F, PID 0x0141, value 0xFF (on)
        can_bus.send(id=0x7E0, data=[0x04, 0x2F, 0x01, 0x41, 0xFF, 0x00, 0x00, 0x00])

        response = can_bus.receive(id=0x7E8, timeout=100ms)
        if response[1] == 0x7F:
            print(f"Negative response, NRC: {response[3]:02X}")
            break

        # Wait before re-sending (keep-alive)
        sleep(500ms)

    # Deactivate: send value 0x00
    can_bus.send(id=0x7E0, data=[0x04, 0x2F, 0x01, 0x41, 0x00, 0x00, 0x00, 0x00])
```

---

## 7. Safety Considerations

- **Most actuator tests require the engine to be OFF.** The ECU will reject requests with NRC `0x22` if the engine is running.
- **The inactivity timeout is a safety net.** If communication is lost, all actuator tests automatically stop. Do not attempt to defeat this mechanism.
- **Injector and coil tests are mutually exclusive.** Cancel one type before starting the other.
- **Injector and coil tests have a finite duration** set by calibration. They will auto-stop after the calibrated time even if you keep re-sending the command. The test-fired flag must be cleared (by timeout or explicit cancel) before the same cylinder can be re-tested.
- **The exhaust bypass valve (0x14E) can be tested with the engine running** at zero vehicle speed. Exercise caution with this test as it affects exhaust backpressure.
- **PID 0x184 (cranking relay) requires ignition OFF** (not just engine off). This is a stricter condition than other PIDs.

---

## 8. Troubleshooting

| Symptom | Cause | Resolution |
|---------|-------|------------|
| NRC `0x22` on all PIDs | Engine still running or engine-off delay not elapsed | Turn off engine and wait several seconds |
| NRC `0x22` on injector PID | A coil test is currently active | Cancel coil test first (send value `0x00`) |
| NRC `0x22` on coil PID | An injector test is currently active | Cancel injector test first |
| NRC `0x22` on PID `0x184` | Ignition is ON | Turn ignition fully OFF |
| Actuator stops after a few seconds | Mode 0x2F timeout expired | Increase message repeat rate |
| Injector/coil won't re-fire | Test-fired flag still set from previous test | Send value `0x00` to clear, wait for timeout, then retry |
| PID `0x151` not in supported list | CAC pump not coded for this vehicle | Only available on supercharged models |
| PID `0x12A` not in supported list | TC not coded for this vehicle | Only available on models with traction control coding |
| No response at all | Wrong CAN ID or bus | Verify using CAN ID `0x7E0` on the correct CAN bus |

---

## 9. References

- ISO 14229-1 (UDS - Part 1: Application layer) -- defines Service 0x2F InputOutputControlByIdentifier
- ISO 15765-2 (ISO-TP) -- transport protocol for multi-frame CAN messaging
- Lotus T6e ECU firmware disassembly (E132E0288, C132E0278) -- source of Lotus-specific PID definitions and behavior
- `obd_ii_mode2F_processing()` -- main request handler function
- `obd_ii_mode2f_200hz()` -- periodic actuator execution and timeout management
- `obd_ii_set_mode2f_supported_pids()` -- supported PID bitmask initialization
- `obd_mode2f_init_actuator_test()` -- eTPU channel initialization for injector/coil tests
