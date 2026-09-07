# High-Speed CAN Channel Logger — Presets & Symbol Catalog

The High-Speed Log tab logs ECU memory locations ("Data Labels") streamed over CAN. Channels can be
picked two ways:

1. **Add Channels… dialog** — browse/search the ECU's full symbol catalog and multi-select labels.
2. **Presets** (`*.json` in this folder) — a saved, named set of channels for an ECU.

## Symbol catalog (`database/`)

`database/<ECU>.csv` is a Ghidra symbol export for one ECU (e.g. `C132E0278.csv`, `B13200091.csv`).
The app loads the matching catalog for a preset's `ecuVersion`, keeps the loggable **RAM Data Labels**
(`0x40000000`–`0x4000FFFF`), and derives each channel's metadata from the CSV's **`Data Type`** column:

| Data Type form | Example | Derived |
|---|---|---|
| Encoded `(u\|i)(8\|16\|32)_<qty>_<scale>[±offset]<unit>` | `u8_temp_5/8-40c` | 1 B, ×0.625, −40, °C |
| | `u16_voltage_5/1023v` | 2 B, ×(5/1023), V |
| | `i16_angle_1/4deg` | 2 B signed, ×0.25, deg |
| | `u16_afr_1/100` | 2 B, ×0.01, AFR |
| | `u16_factor_1/1023` | 2 B, ×(100/1023), **%** (see below) |
| Base C | `uint16_t`, `int8_t`, `bool`, `pointer` | size + signedness; raw scale |
| Array `…[N]` | `u8_temp_5/8-40c[16]` | a table/map — hidden from logging by default |
| Unknown / `enum_*` | `enum_gear`, `cluster_data` | size from prefix or gap; raw |

The `Data Type` value before a unit is read as a **multiplier** (units-per-count), e.g. `10rpm` = ×10.
A few fixed-point types name the divisor instead (e.g. `u32_rspeed_1024rpm` likely means ÷1024); these
parse with the multiplier rule and may need the scale corrected — every channel is editable.

**`factor` types are normalized fractions shown as percent (0–100%).** The denominator is the full-scale
count, so `u16_factor_1/1023` (a 10-bit ADC reading like `tps_u16`) is scaled ×100/1023 and labelled
`%`, giving 0–100%. Types that already name a percent unit (`i16_factor_1/10pct`) or pre-scale to
percent (`u8_dutycycle_100/255`, `u8_percent_100/128-100`) are taken as-is — not multiplied again.

**Watch for symbols with a built-in offset.** Some coarse copies bake an offset into their type, e.g.
`engine_speed_3` is `u8_rspeed_125/4+500rpm` → ×31.25 **+500 rpm**, so it reads 500 at zero and is only
~31 rpm-resolution. Prefer the precise `engine_speed_2` (`u16_rspeed_rpm`, 1 rpm/count) for engine
speed; the sample presets use it.

To add a new ECU, drop its `<ECU>.csv` in `database/` (the file name, minus any `_symbols` suffix, is
the `ecuVersion`). These CSVs live in the project and are build-copied to the output `database/` dir.

## Preset schema

```jsonc
{
  "name": "GT430 Sample (C132E0278)",
  "description": "…",
  "ecuVersion": "C132E0278",          // selects database/C132E0278.csv
  "zt3Wideband": true,                 // optional: listen for ZT-3 broadcasts on CAN ID 0x05A
  "channels": [
    // Symbol reference — address/size/scale/offset/unit resolved from the catalog:
    { "symbol": "engine_speed_3", "rate": 100 },
    { "symbol": "coolant_temp",   "rate": 5, "defaultSelected": false },
    // Any derived field may be overridden inline:
    { "symbol": "tps_u16", "rate": 100, "unit": "%", "scale": 0.0977 },
    // Explicit (catalog-independent) channel — address required:
    { "name": "Custom", "address": "0x40001234", "size": 2, "signed": false,
      "scale": 1.0, "offset": 0.0, "unit": "raw", "rate": 50 }
  ]
}
```

- `rate` (Hz) sets the sample rate and marks the channel **selected**; add `"defaultSelected": false`
  to list it unchecked.
- Symbol-referenced channels whose symbol is missing from the catalog are skipped with a warning.
- **Every preset carries engine speed and a load channel** (`engine_speed_16bit` plus one of the
  `load_*` symbols). They are the axes every other trace is read against, so a log without them is
  hard to interpret after the fact.
- **A preset may only use channels from its own `ecuVersion` catalog.** Symbol names are checked at
  load time, but explicit `address` channels are not — an address copied from the other ECU's preset
  reads a plausible-looking wrong location silently. The symbol names differ between ECUs too
  (`map`/`manifold_pressure_calculated`, `iat`/`air_temp_intake`, `afr_commanded`/`afr_target`,
  `maf_flow`/`maf_flow_1`), so porting a preset means re-resolving every channel, not just the
  addresses.
- Files are JSONC (comments and trailing commas allowed) and are copied to the app output directory,
  so no rebuild is needed to add or edit a preset.

## Zeitronix Zt-3 CAN wideband

The optional **ZT-3 CAN** source listens passively for the controller's 8-byte broadcast on standard CAN
ID `0x05A` at 500 kbit/s. It adds four last-value-hold columns to each ECU stream row:

| Column | Payload | Decode |
|---|---|---|
| `ZT-3 Lambda (16-bit)` | bytes 0–1, big-endian | unsigned × 0.001 |
| `ZT-3 Lambda (8-bit)` | byte 2 | unsigned × 0.01 |
| `ZT-3 AFR` | byte 3 | unsigned × 0.1 |
| `ZT-3 O2 Status` | byte 7 | raw status code |

Two sample setups are included:

- **GT430 + ZT-3 Wideband Sample (C132E0278)** for the GT430 ECU.
- **S1 / NA + ZT-3 Wideband Sample (B132E0091)** for the S1 naturally aspirated car (resolved through
  the repository's `B13200091` symbol catalog).

Both presets check **ZT-3 CAN** automatically and log engine speed, load, throttle/pedal, commanded AFR,
MAP and IAT alongside the ZT-3 values. For a different ECU, choose its normal preset and check **ZT-3
CAN** manually. The controller must share the adapter's 500 kbit/s CAN bus; if no valid broadcast
arrives, the ZT-3 columns remain empty and ECU logging continues.

## Safety

The PC becomes an active node on the vehicle CAN bus at 500 kbit/s and sends configuration commands to
the ECU. Configure with the **engine off and the vehicle stationary**, and ensure the diagnostic bus is
enabled (`CAL_ecu_flexcan_diag_bus_select` ≠ 0). Use **Test Connection** first. Scaling/units are
derived from the symbol types and should be sanity-checked against a known reading before relying on
them; live bus I/O is bench-untested.
