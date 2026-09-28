# LogiWheel Forge

LogiWheel Forge is a Windows toolkit for Logitech wheels. **LED Indicator** drives RPM/shift LEDs from Forza and BeamNG.drive telemetry. **Input Mapper** maps steering, pedals, and buttons to keyboard and mouse input for selected desktop applications, with optional centering, damping, detents, and hold-target resistance. The modules start and stop independently. The app uses managed HID communication and Windows APIs; it does not ship Logitech SDK binaries.

## Disclaimer

LogiWheel Forge is an independent project and is not affiliated with, endorsed, sponsored, or approved by Logitech International S.A. or its Logi brand, or by the developers or publishers of any supported games. Logitech, Logi, the supported game names, and other trademarks belong to their respective owners.

## Supported wheels

- Logitech G27
- Logitech G29 Driving Force
- Logitech G923 PlayStation/PC and Xbox/PC variants
- Logitech PRO Racing Wheel PlayStation/PC and Xbox/PC variants
- Logitech RS50 Base

G29 has received a real-hardware **LED** verification pass. Input mapping and force feedback require separate hardware verification. Other listed LED models remain compatibility targets until tested on real hardware.

Additional Logitech RPM wheels can be added without recompiling by placing a unique, schema-compatible JSON file in `%LOCALAPPDATA%\LogiWheelForge\Wheels`. Definitions are data-only and may select only the built-in `classic-bitmask` or `hidpp-level` transports.

Definition schema version 2 can also declare logical input controls by HID `usagePage` and `usage`, including inversion and whether an axis is centered. It can declare `forceFeedback.spring` and `forceFeedback.damper` as expected capabilities. Actual controls and effects are checked on the connected device. Set `hasLedOutput` to `false` for an input-only device, and `isPedalSet` to `true` for a separate USB pedal set. Wheels without a JSON definition can use discovered Logitech HID controls; unrecognized axes appear by usage number in the Input Mapper preview.

## Input Mapper

Choose **Input Mapper** in the sidebar, create a profile, then add one or more target executable paths with **Pick window** or manual entry. Each enabled executable can belong to only one profile. Save the profile and start the mapper. Mapping activates only while one of its target processes owns the foreground window. Settings has a separate **Start Input Mapper on launch** option; it is off for new installations.

New profiles include editable starter rules: clockwise steering steps tap `D`, counterclockwise steps tap `A`, accelerator holds `W`, and brake holds `S`. A steering step of 5% means five percentage points of normalized travel. **Movement** follows the direction the wheel moves, including while returning toward center; **AwayFromCenter** ignores return travel. Each tap duration, step size, range, dead zone, curve, and output can be changed. Key chords use WPF key names separated by `+`, such as `LeftCtrl+K`; mouse buttons use `Left`, `Right`, `Middle`, `X1`, or `X2`. Mouse movement uses `Horizontal` or `Vertical` as its output axis.

Resistance is off until enabled on a profile. Center and damper strengths are set per profile. Soft detents repeat at the configured spacing; a hold-target rule pulls toward a chosen steering percentage until its range ends or an explicit release rule fires. Effects stop when the profile loses focus, the wheel disconnects, or the mapper stops. The wheel driver must expose the corresponding DirectInput effects and allow exclusive force-feedback access. Keyboard and mouse injection uses Windows `SendInput`, which follows Windows integrity-level restrictions; an elevated target may reject input from a non-elevated mapper.

Input Mapper profiles and startup preferences are stored separately in `%LOCALAPPDATA%\LogiWheelForge\input-mapper-profiles.json` and `input-mapper-settings.json`.

## Supported telemetry

LogiWheel Forge's LED Indicator accepts the common RPM prefix from these Forza Data Out formats:

- Forza Motorsport 7 (`Sled` and `Dash`)
- Forza Horizon 4, 5, and 6 (`Dash`)
- Forza Motorsport (2023) (`Sled` and `Dash`)

BeamNG.drive is supported through its built-in OutGauge UDP protocol.

In Settings → Telemetry, choose Auto to listen for every configured game, or select one game. Use Add game and Manage to set a separate bind address and UDP port for each game. New installations include Forza at `0.0.0.0:1024`; BeamNG.drive defaults to `0.0.0.0:4444` when added. The ports must differ. Configure each game's telemetry target to this PC's LAN address and its matching port. Use `127.0.0.1` only when the game and LogiWheel Forge run on the same PC and the game supports localhost.

For BeamNG.drive, enable **OutGauge UDP protocol** under **Options → Other → Protocols** and set its target port to the BeamNG port shown in LogiWheel Forge. [BeamNG's OutGauge format](https://documentation.beamng.com/modding/protocols/) sends current RPM but no maximum RPM, so enter the current vehicle's maximum RPM in BeamNG's Manage dialog. Update it when switching to a vehicle with a different engine limit.

## Profiles and app behavior

- Smart mode defaults to a progressive 65–89% light range with a 90% redline, matching the early shift-light ramp used by Forza. It can learn a stable per-car shift point after three consistent full-throttle shift/limiter events.
- Advanced mode exposes one ordered activation threshold per controllable LED group plus a separate redline.
- Whole-strip redline blinking, automatic control startup, ready animation, minimize-to-tray, and close-to-tray are enabled by default.
- Light, dark, and Windows-following themes are available from Settings.
- Per-wheel profiles are stored in `%LOCALAPPDATA%\LogiWheelForge\profiles`; learned car calibration is stored separately in `calibrations.json`.

## Build and test

Requirements: Windows 10/11 x64 and the .NET 10 SDK.

```powershell
dotnet build .\LogiWheelForge.sln -c Release
dotnet test .\LogiWheelForge.Tests\LogiWheelForge.Tests.csproj -c Release
dotnet publish .\LogiWheelForge\LogiWheelForge.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish
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

Logitech G HUB or Logitech's current wheel driver should remain installed for normal device and force-feedback operation. LED Indicator opens only the LED-capable HID interface. Input Mapper reads shared HID input and uses Windows DirectInput for supported resistance effects; it does not alter firmware or onboard profiles.
