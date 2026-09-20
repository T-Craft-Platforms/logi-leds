namespace LogiLeds.Models;

public sealed record WheelDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int VendorId { get; init; } = 0x046d;
    public int[] ProductIds { get; init; } = [];
    public string Transport { get; init; } = "classic-bitmask";
    public int OutputReportId { get; init; }
    public int OutputReportLength { get; init; } = 17;
    public int HidppFeatureIndex { get; init; } = 0x0b;
    public bool RequiresArm { get; init; }
    public int PhysicalLedCount { get; init; } = 10;
    public int ControlGroupCount { get; init; } = 5;
    public string Direction { get; init; } = "left-to-right";
    public string[] Colors { get; init; } = [];
    public double DefaultFirstPercent { get; init; } = 65;
    public double DefaultRedlinePercent { get; init; } = 90;
    public bool HardwareVerified { get; init; }

    public bool TryValidate(out string error)
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DisplayName))
        { error = "Definition identity or schema is invalid."; return false; }
        if (VendorId != 0x046d || ProductIds.Length == 0 || ProductIds.Any(x => x is < 0 or > 0xffff))
        { error = "Only valid Logitech USB devices are allowed."; return false; }
        if (Transport is not ("classic-bitmask" or "hidpp-level"))
        { error = $"Transport '{Transport}' is not allowlisted."; return false; }
        if (OutputReportId is < 0 or > 0xff)
        { error = "Output report ID is invalid."; return false; }
        if (OutputReportLength is < 4 or > 64)
        { error = "Output report length is invalid."; return false; }
        if (PhysicalLedCount is < 1 or > 32 || ControlGroupCount is < 1 or > 10 || Colors.Length != PhysicalLedCount)
        { error = "LED count, groups, or color map is invalid."; return false; }
        if (Colors.Any(x => x.Length != 7 || x[0] != '#' || !int.TryParse(x[1..], System.Globalization.NumberStyles.HexNumber, null, out _)))
        { error = "LED colors must use #RRGGBB."; return false; }
        if (DefaultFirstPercent < 0 || DefaultFirstPercent >= DefaultRedlinePercent || DefaultRedlinePercent > 100)
        { error = "Recommended RPM thresholds are invalid."; return false; }
        error = string.Empty;
        return true;
    }
}

public sealed record WheelCatalogResult(IReadOnlyList<WheelDefinition> Definitions, IReadOnlyList<string> Diagnostics);
