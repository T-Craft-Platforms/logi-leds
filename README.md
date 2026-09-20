# LogiLeds

LogiLeds is a polished Windows wheel companion that drives Logitech RPM/shift LEDs from Forza Data Out telemetry. It uses managed HID communication, contains no Logitech SDK binaries, and keeps all model-specific behavior in validated JSON wheel definitions.

## Supported wheels

- Logitech G27
- Logitech G29 Driving Force
- Logitech G923 PlayStation/PC and Xbox/PC variants
- Logitech PRO Racing Wheel PlayStation/PC and Xbox/PC variants
- Logitech RS50 Base

G29 has received a real-hardware verification pass. Other listed models use protocol-backed definitions and are marked as compatibility targets in the app until a real-device smoke test is recorded.

Additional Logitech RPM wheels can be added without recompiling by placing a unique, schema-compatible JSON file in `%LOCALAPPDATA%\LogiLeds\Wheels`. Definitions are data-only and may select only the built-in `classic-bitmask` or `hidpp-level` transports.

## Supported telemetry

LogiLeds accepts the common RPM prefix from these Forza Data Out formats:

- Forza Motorsport 7 (`Sled` and `Dash`)
- Forza Horizon 4, 5, and 6 (`Dash`)
- Forza Motorsport (2023) (`Sled` and `Dash`)

New installations listen on all IPv4 interfaces at UDP port `1024`, which supports games on this PC as well as Xbox or another PC. Configure the game's Data Out target to this PC's LAN address and the same port. Use `127.0.0.1` only when the game and LogiLeds run on the same PC and that game version supports localhost.

## Profiles and app behavior

- Smart mode defaults to a progressive 65–89% light range with a 90% redline, matching the early shift-light ramp used by Forza. It can learn a stable per-car shift point after three consistent full-throttle shift/limiter events.
- Advanced mode exposes one ordered activation threshold per controllable LED group plus a separate redline.
- Whole-strip redline blinking, automatic control startup, ready animation, minimize-to-tray, and close-to-tray are enabled by default.
- Light, dark, and Windows-following themes are available from Settings.
- Per-wheel profiles are stored in `%LOCALAPPDATA%\LogiLeds\profiles`; learned car calibration is stored separately in `calibrations.json`.

## Build and test

Requirements: Windows 10/11 x64 and the .NET 10 SDK.

```powershell
dotnet build .\LogiLeds.sln -c Release
dotnet test .\LogiLeds.Tests\LogiLeds.Tests.csproj -c Release
dotnet publish .\LogiLeds\LogiLeds.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish
```

Logitech G HUB or Logitech's current wheel driver should remain installed for normal device/force-feedback operation. LogiLeds opens only the LED-capable HID interface and does not configure steering, force feedback, pedals, firmware, or onboard profiles.
