# Dimmer

System tray app for Windows that adjusts external monitor brightness with **Alt + MouseWheel**.

Works via DDC/CI over NvAPI (NVIDIA GPU required). Tested with MSI Optix G273 on DisplayPort.

## Requirements

- Windows 10/11
- NVIDIA GPU with recent driver (loads `nvapi64.dll` from the driver directory)

## Usage

- Hold **Alt** and scroll the mouse wheel to change brightness.
- Right-click the tray icon → **Exit** to quit.
- Runs in the system tray; single instance enforced.
- Brightness is restored after sleep/wake (I2C re-initialized on power resume).

## Build

Compile with Roslyn `csc` (VS 2022):

```
csc -nologo -out:dimmer.exe -win32icon:dimmer.ico -target:winexe dimmer.cs
```

## CLI tool

`setbright.exe <0-100>` sets brightness directly for diagnostics:

```
csc -nologo -out:setbright.exe setbright.cs
```

## How it works

- Loads `nvapi64.dll` and uses `NvAPI_I2CWrite` / `NvAPI_I2CRead` to talk DDC/CI.
- Sets VCP feature `0x10` (brightness).
- I2C commands are throttled: the monitor bus needs ~500 ms between commands.
- On resume from sleep the driver library is reloaded to recover I2C state.

## Disclaimer

DDC/CI brightness control depends on your monitor. If your display is not a
DDC/CI-capable monitor over a supported link (e.g. HDMI/DVI without DDC), this
tool may not work.
