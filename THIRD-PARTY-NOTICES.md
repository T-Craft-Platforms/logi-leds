# Third-party notices

## HidSharp 2.6.4

LogiWheel Forge uses HidSharp through a pinned NuGet package for managed HID device discovery and report transport.

- Project: https://github.com/IntergatedCircuits/HidSharp
- Package: https://www.nuget.org/packages/HidSharp/2.6.4
- License: Apache License 2.0

No HidSharp or Logitech SDK binary is committed to this repository. NuGet restores HidSharp from the package lock file during build.

## Logitech protocol references

The LED-only protocol adapters were implemented from public protocol documentation and open-source driver research. LogiWheel Forge does not redistribute Logitech drivers, firmware, SDK libraries, or G HUB components.

- Logitech G29 command reference: https://github.com/nightmode/logitech-g29/blob/main/docs/api.md
- Logitech TrueForce wheel protocol research: https://github.com/mescon/logitech-trueforce-linux-driver/blob/master/docs/PROTOCOL_SPECIFICATION.md
