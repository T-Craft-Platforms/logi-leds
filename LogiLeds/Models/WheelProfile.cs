namespace LogiLeds.Models;

public sealed record WheelProfile
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string? ProfileId { get; init; }
    public string? Name { get; init; }
    public required string WheelId { get; init; }
    public RpmProfileMode Mode { get; init; } = RpmProfileMode.Easy;
    public double FirstLedPercent { get; init; } = 65;
    public double RedlinePercent { get; init; } = 90;
    public bool BlinkAtRedline { get; init; } = true;
    public double[] AdvancedThresholds { get; init; } = [];

    public override string ToString()
    {
        return Name ?? "RPM profile";
    }

    public bool TryValidate(int groupCount, out string error)
    {
        if (string.IsNullOrWhiteSpace(WheelId))
        {
            error = "Wheel profile id is missing.";
            return false;
        }

        if (FirstLedPercent < 0 || FirstLedPercent >= RedlinePercent || RedlinePercent > 100)
        {
            error = "Wheel profile thresholds are invalid.";
            return false;
        }

        if (AdvancedThresholds.Length != groupCount || AdvancedThresholds.Any(x => x < 0 || x >= RedlinePercent) ||
            !AdvancedThresholds.SequenceEqual(AdvancedThresholds.OrderBy(x => x)))
        {
            error = "Advanced wheel thresholds are invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}