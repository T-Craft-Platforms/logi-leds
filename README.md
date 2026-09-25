# LogiLeds

LogiLeds is a Windows wheel companion that drives Logitech RPM/shift LEDs from Forza Data Out and BeamNG.drive OutGauge telemetry. It uses managed HID communication, contains no Logitech SDK binaries, and keeps all model-specific behavior in validated JSON wheel definitions.

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

BeamNG.drive is supported through its built-in OutGauge UDP protocol.

In Settings → Telemetry, choose Auto to listen for every configured game, or select one game. Use Add game and Manage to set a separate bind address and UDP port for each game. New installations include Forza at `0.0.0.0:1024`; BeamNG.drive defaults to `0.0.0.0:4444` when added. The ports must differ. Configure each game's telemetry target to this PC's LAN address and its matching port. Use `127.0.0.1` only when the game and LogiLeds run on the same PC and the game supports localhost.

For BeamNG.drive, enable **OutGauge UDP protocol** under **Options → Other → Protocols** and set its target port to the BeamNG port shown in LogiLeds. [BeamNG's OutGauge format](https://documentation.beamng.com/modding/protocols/) sends current RPM but no maximum RPM, so enter the current vehicle's maximum RPM in BeamNG's Manage dialog. Update it when switching to a vehicle with a different engine limit.

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

## Releases

There are two ways to release from `main`:

```powershell
# Explicit version: create and push a vX.Y.Z tag.
git tag v1.2.3
git push origin v1.2.3
```

Or run **Release** from the Actions tab using **Run workflow**. No version input is required; GitVersion calculates the next version from `GitVersion.yml`, and the workflow creates and pushes the corresponding tag after validation and packaging succeed.

Both paths publish a self-contained Windows x64 ZIP and create the matching GitHub release if one does not already exist. Release notes follow `.github/RELEASE_NOTES_TEMPLATE.md` and are generated from commits since the previous version tag. The workflow requires repository Actions permissions to allow `GITHUB_TOKEN` to write contents so it can push the calculated tag and create the release.

Logitech G HUB or Logitech's current wheel driver should remain installed for normal device/force-feedback operation. LogiLeds opens only the LED-capable HID interface and does not configure steering, force feedback, pedals, firmware, or onboard profiles.
