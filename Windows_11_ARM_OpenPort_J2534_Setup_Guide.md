# Windows 11 ARM64 OpenPort 2.0 J2534 Setup Guide

This guide documents a **working Windows 11 ARM64 setup** for using an OpenPort 2.0-compatible J2534 adapter with a **32-bit x86 J2534 application**, such as LotusECMLogger.

The tested adapter identifies as:

```text
USB\VID_0403&PID_CC4D
```

The final working stack is:

```text
32-bit x86 application
        ↓
32-bit J2534 DLL
        ↓
32-bit libusb
        ↓
Windows ARM64 WinUSB
        ↓
OpenPort 2.0-compatible adapter
        ↓
Vehicle CAN bus
```

> [!WARNING]
> This setup was validated for J2534 communication, diagnostics, logging, and read operations. ECU flashing is less forgiving than logging or diagnostics. Validate stability thoroughly before attempting any write or flash operation.

---

## 1. Confirm the Adapter Hardware ID

Plug the adapter into the PC.

Open **Device Manager** and identify the device that appears when you plug or unplug the adapter.

Go to:

**Properties → Details → Hardware Ids**

Confirm the adapter reports:

```text
USB\VID_0403&PID_CC4D
```

Do not follow these exact instructions if your VID/PID is different.

---

## 2. Confirm Your Application Is 32-bit

This method works because the J2534 application is **32-bit x86**.

Windows 11 ARM can emulate the x86 application and load a 32-bit J2534 DLL, while the USB kernel driver remains native ARM64 through Microsoft WinUSB.

```text
32-bit x86 application
        ↓
x86 J2534 DLL
        ↓
x86 libusb
        ↓
ARM64 WinUSB
        ↓
OpenPort 2.0
```

---

## 3. Install the Standard Tactrix Package

Install the OpenPort 2.0 driver/J2534 package from Tactrix.

Typical installation path:

```text
C:\Program Files (x86)\OpenECU\OpenPort 2.0
```

The standard package includes the original 32-bit Tactrix J2534 DLL:

```text
C:\Program Files (x86)\OpenECU\OpenPort 2.0\drivers\openport 2.0\op20pt32.dll
```

This package is useful for the OpenPort files and samples, but the original Tactrix J2534 DLL was **not** the final working solution on Windows ARM.

Do not rely on the old Tactrix `openport.sys` x86/x64 driver for ARM64.

---

## 4. Check BitLocker Before Changing Secure Boot

Open **Command Prompt as Administrator** and run:

```cmd
manage-bde -status C:
```

If BitLocker is enabled, make sure you have your recovery key before making UEFI/Secure Boot changes.

To display the protectors:

```cmd
manage-bde -protectors -get C:
```

Save the 48-digit recovery password somewhere outside the PC.

> [!CAUTION]
> Do not post or share your BitLocker recovery key.

Suspend BitLocker protection:

```cmd
manage-bde -protectors -disable C:
```

Verify:

```cmd
manage-bde -status C:
```

You want to see:

```text
Protection Status: Protection Off
```

The drive may still show:

```text
Percentage Encrypted: 100.0%
```

That is normal. The drive remains encrypted; protection is only suspended.

---

## 5. Disable Secure Boot

Reboot into UEFI/BIOS.

Disable:

```text
Secure Boot
```

Boot back into Windows.

If BitLocker automatically resumes after the reboot, suspend it again:

```cmd
manage-bde -protectors -disable C:
```

---

## 6. Enable Windows Test Signing

Open **Command Prompt as Administrator** and run:

```cmd
bcdedit /set testsigning on
```

Expected response:

```text
The operation completed successfully.
```

Reboot Windows.

You can verify Test Signing with:

```cmd
bcdedit
```

Look for:

```text
testsigning    Yes
```

---

## 7. Bind the Adapter to WinUSB with Zadig

Run **Zadig as Administrator**.

Go to:

**Options → List All Devices**

Select:

```text
OpenPort 2.0
```

Before changing the driver, confirm:

```text
USB ID: 0403 : CC4D
```

> [!WARNING]
> Make sure you are changing the driver only for `VID_0403&PID_CC4D`. Do not replace the driver for your USB root hub, keyboard, mouse, storage device, or another USB device.

Set the target driver to:

```text
WinUSB (Microsoft)
```

Click **Replace Driver** (or **Install Driver**, depending on what Zadig shows).

### Why Test Mode Is Needed

Before Test Signing was enabled, Zadig failed with:

```text
0x800B0109
Driver package catalog file certificate does not belong to Trusted Root Certificates,
and Code Integrity is enforced.
```

The generated package was self-signed by libwdi, so normal Windows ARM64 Code Integrity rejected it.

---

## 8. Verify the WinUSB Driver

After the driver change, the adapter should no longer appear as a generic serial COM device.

It should appear as:

```text
OpenPort 2.0
```

Open:

**Device Manager → OpenPort 2.0 → Properties → Driver → Driver Details**

Confirm:

```text
C:\Windows\System32\drivers\winusb.sys
```

Also confirm the hardware ID remains:

```text
USB\VID_0403&PID_CC4D
```

Do not try to execute `winusb.sys` from Command Prompt. It is a driver file, not an executable program.

---

## 9. Do Not Use PCAN-PassThru for This Adapter

PEAK PCAN-PassThru may install correctly on Windows ARM64, including a 32-bit J2534 DLL such as:

```text
PCANPT32.dll
```

However, PCAN-PassThru is for **PEAK PCAN hardware**.

This adapter identifies as:

```text
VID_0403
PID_CC4D
```

and presents as an OpenPort-compatible device.

Therefore this combination is not correct:

```text
OpenPort adapter
    ↓
PEAK PCAN driver
```

The PEAK software can be ignored or removed for this setup.

---

## 10. Download the Windows ARM OpenPort J2534 Project

Use the Windows ARM OpenPort J2534 project:

```text
openport2-winarm-j2534
```

Example extraction path:

```text
C:\Users\<USERNAME>\Downloads\openport2-winarm-j2534-main
```

Open **Command Prompt as Administrator** and change to the repository directory:

```cmd
cd /d C:\Users\<USERNAME>\Downloads\openport2-winarm-j2534-main
```

Run:

```cmd
extras\install_windows_arm.cmd -InstallMissingPrereqs -NoEvoScan
```

---

## 11. Install the Build Prerequisites

On the first run, the installer may install:

```text
7-Zip ARM64
Visual Studio 2022 Build Tools
Desktop C++ workload
```

The first run may stop with:

```text
The C++ compiler still was not found after Build Tools installation.
Close and reopen the terminal, then re-run this installer.
```

If that happens:

1. Close Command Prompt completely.
2. Open a new **Administrator Command Prompt**.
3. Return to the repository:

```cmd
cd /d C:\Users\<USERNAME>\Downloads\openport2-winarm-j2534-main
```

4. Run the installer again:

```cmd
extras\install_windows_arm.cmd -InstallMissingPrereqs -NoEvoScan
```

The second run should detect the newly installed C++ toolchain and continue.

---

## 12. Verify the ARM J2534 Files

A successful install creates:

```text
C:\J2534\OpenPort\j2534.dll
C:\J2534\OpenPort\libusb-1.0.dll
```

Verify with:

```cmd
dir C:\J2534\OpenPort
```

The tested working files were:

```text
j2534.dll          37,376 bytes
libusb-1.0.dll    136,192 bytes
```

---

## 13. Verify the J2534 Registry Entry

Because the application is 32-bit, check the 32-bit J2534 registry path:

```cmd
reg query "HKLM\SOFTWARE\WOW6432Node\PassThruSupport.04.04" /s
```

The working provider should look similar to:

```text
HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PassThruSupport.04.04\RomRaider - OP2 J2534
```

with:

```text
FunctionLibrary    REG_SZ       C:\J2534\OpenPort\j2534.dll
ConfigApplication REG_SZ
Name               REG_SZ       Openport 2.0 J2534
Vendor             REG_SZ       RomRaider
CAN                REG_DWORD    0x1
ISO14230           REG_DWORD    0x1
ISO15765           REG_DWORD    0x1
ISO9141            REG_DWORD    0x1
```

This is the J2534 provider the application should use.

---

## 14. Remove Competing J2534 Providers During Testing

Some applications automatically load the first J2534 provider they find.

LotusECMLogger is one of them: it has no device picker and always uses the first provider registered under `PassThruSupport.04.04`. For LotusECMLogger this step is therefore required, not just a troubleshooting aid.

If multiple providers are installed, leave only the ARM OpenPort provider registered.

Back up a provider before deleting it.

Example for PEAK:

```cmd
reg export "HKLM\SOFTWARE\WOW6432Node\PassThruSupport.04.04\PEAK - PCAN" "%USERPROFILE%\Desktop\PEAK-PCAN-backup.reg"
```

Example for the original Tactrix provider:

```cmd
reg export "HKLM\SOFTWARE\WOW6432Node\PassThruSupport.04.04\Tactrix Inc. - OpenPort 2.0 J2534 ISO-CAN" "%USERPROFILE%\Desktop\Tactrix-OpenPort-backup.reg"
```

Delete the PEAK entry:

```cmd
reg delete "HKLM\SOFTWARE\WOW6432Node\PassThruSupport.04.04\PEAK - PCAN" /f
```

Delete the original Tactrix entry:

```cmd
reg delete "HKLM\SOFTWARE\WOW6432Node\PassThruSupport.04.04\Tactrix Inc. - OpenPort 2.0 J2534 ISO-CAN" /f
```

Verify:

```cmd
reg query "HKLM\SOFTWARE\WOW6432Node\PassThruSupport.04.04" /s
```

For troubleshooting, the cleanest configuration is:

```text
RomRaider - OP2 J2534
    FunctionLibrary = C:\J2534\OpenPort\j2534.dll
```

You can restore backed-up registry files later by importing them.

---

## 15. Why the Original Tactrix DLL Did Not Work

The original Tactrix DLL:

```text
op20pt32.dll
```

was tested from:

```text
C:\Program Files (x86)\OpenECU\OpenPort 2.0\drivers\openport 2.0\
```

The application detected it, but communication failed with:

```text
Device ID invalid
```

That indicates the legacy Tactrix DLL was loading, but it was not compatible with the new WinUSB/libusb path used on ARM.

Use:

```text
C:\J2534\OpenPort\j2534.dll
```

instead.

---

## 16. Enable J2534 Logging

Open **Command Prompt as Administrator**.

Run these as **two separate commands**:

```cmd
setx LOG_ENABLE "C:\J2534\op2.log" /M
```

```cmd
setx LIBUSB_DEBUG 3 /M
```

Each command should return:

```text
SUCCESS: Specified value was saved.
```

Completely close and reopen the J2534 application afterward so it inherits the new environment variables.

---

## 17. Test with Only the Adapter Connected

Before connecting the vehicle, try a J2534 operation.

The application will report a failure or timeout (the exact message depends on the application). That is expected when no ECU is connected.

Inspect the log:

```cmd
type C:\J2534\op2.log
```

A successful PC-side connection should show lines similar to:

```text
j2534.dll loaded
CPU Endian: little
Opening...
DeviceID 1 opened
Claimed Interface 1
Init acknowledged
Interface Opened
```

Version information should look similar to:

```text
fwVer : 1.17.4877
libVer: 3.0.1-dev (libusb-1.0.30.12037)
apiVer: 04.04
```

A CAN connection may show:

```text
protocolID: 5
flags:      00000000
baud:       500000
```

followed by:

```text
Connected
```

If the adapter is not connected to a vehicle, a later:

```text
LIBUSB_ERROR_TIMEOUT
```

is expected because there is no powered ECU/network responding.

---

## 18. Connect to the Vehicle

Connect the OpenPort adapter to:

- the Windows 11 ARM PC via USB
- the vehicle diagnostic connector

Turn the ignition **ON**. The engine can remain **OFF**.

Launch the J2534 application and try a read-only operation first.

A working setup should now show:

```text
Connected
```

with the vehicle attached.

The validated end-to-end chain is:

```text
32-bit x86 J2534 application
        ↓
Windows ARM x86 emulation
        ↓
J2534 API 04.04
        ↓
C:\J2534\OpenPort\j2534.dll
        ↓
libusb-1.0.dll
        ↓
ARM64 WinUSB
        ↓
OpenPort 2.0-compatible adapter
VID_0403 / PID_CC4D
        ↓
CAN at 500 kbit/s
        ↓
Vehicle ECU
```

---

## 19. Re-enable BitLocker

BitLocker does **not** need to remain suspended once UEFI changes are complete.

Re-enable protection:

```cmd
manage-bde -protectors -enable C:
```

Verify:

```cmd
manage-bde -status C:
```

You want:

```text
Protection Status: Protection On
```

---

## 20. Test Mode and Secure Boot

The working WinUSB binding was installed through a Zadig/libwdi self-signed driver package.

The known-working configuration is:

```text
Secure Boot     OFF
Test Signing    ON
BitLocker       ON
```

BitLocker can remain enabled.

### Testing Whether Test Mode Is Still Required

Turn off Test Signing:

```cmd
bcdedit /set testsigning off
```

Reboot.

Check that `OpenPort 2.0` still appears normally in Device Manager with no warning icon.

If it still works, you can then try re-enabling Secure Boot in UEFI.

After re-enabling Secure Boot, verify the adapter again.

Under:

**Device Manager → OpenPort 2.0 → Driver → Driver Details**

you should still see:

```text
C:\Windows\System32\drivers\winusb.sys
```

To verify Secure Boot from Administrator PowerShell:

```powershell
Confirm-SecureBootUEFI
```

Expected result:

```text
True
```

If the OpenPort stops loading after Test Signing is disabled or Secure Boot is re-enabled, return to the known-working state:

```text
Secure Boot OFF
Test Signing ON
BitLocker ON
```

---

# Final Verified Configuration

| Component | Working Configuration |
|---|---|
| Operating system | Windows 11 ARM64 |
| Adapter | OpenPort 2.0-compatible |
| USB hardware ID | `USB\VID_0403&PID_CC4D` |
| USB driver | Microsoft `winusb.sys` |
| J2534 architecture | 32-bit x86 |
| J2534 API | 04.04 |
| J2534 provider | `RomRaider - OP2 J2534` |
| J2534 DLL | `C:\J2534\OpenPort\j2534.dll` |
| USB library | `C:\J2534\OpenPort\libusb-1.0.dll` |
| Tested application | LotusECMLogger |
| CAN bitrate | 500000 |
| Adapter firmware seen | `1.17.4877` |
| libusb seen | `3.0.1-dev (libusb-1.0.30.12037)` |

---

# Troubleshooting Quick Reference

## Zadig fails with `0x800B0109`

Cause: Code Integrity is enforcing driver package signature validation.

Fix:

1. Suspend BitLocker.
2. Disable Secure Boot.
3. Enable Test Signing.
4. Reboot.
5. Retry the Zadig WinUSB installation.

## Adapter appears as `USB Serial Device (COMx)`

The OpenPort is still using the generic serial binding.

Use Zadig and bind:

```text
VID_0403&PID_CC4D
```

to:

```text
WinUSB (Microsoft)
```

## J2534 application loads the wrong DLL

Check:

```cmd
reg query "HKLM\SOFTWARE\WOW6432Node\PassThruSupport.04.04" /s
```

Temporarily remove competing providers so only:

```text
RomRaider - OP2 J2534
```

remains.

## Application reports `Device ID invalid`

If you are using the original Tactrix:

```text
op20pt32.dll
```

switch to:

```text
C:\J2534\OpenPort\j2534.dll
```

from the Windows ARM OpenPort J2534 project.

## `LIBUSB_ERROR_TIMEOUT` with No Vehicle Connected

This is expected if the OpenPort USB/J2534 side is working but no powered ECU is connected.

Confirm the log first shows:

```text
DeviceID 1 opened
Claimed Interface 1
Interface Opened
Connected
```

Then test again with the adapter connected to the vehicle and ignition ON.

---

# Safety Notes

- Always keep your BitLocker recovery key before changing Secure Boot settings.
- Do not replace drivers for unrelated USB devices in Zadig.
- Verify `VID_0403&PID_CC4D` before changing the driver.
- Test diagnostics and read-only communication before attempting ECU writes.
- ECU flashing can brick a module if USB, J2534, vehicle power, or application communication fails during programming.
