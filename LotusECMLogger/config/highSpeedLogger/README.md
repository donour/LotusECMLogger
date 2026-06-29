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
- Files are JSONC (comments and trailing commas allowed) and are copied to the app output directory,
  so no rebuild is needed to add or edit a preset.

## Safety

The PC becomes an active node on the vehicle CAN bus at 500 kbit/s and sends configuration commands to
the ECU. Configure with the **engine off and the vehicle stationary**, and ensure the diagnostic bus is
enabled (`CAL_ecu_flexcan_diag_bus_select` ≠ 0). Use **Test Connection** first. Scaling/units are
derived from the symbol types and should be sanity-checked against a known reading before relying on
them; live bus I/O is bench-untested.
