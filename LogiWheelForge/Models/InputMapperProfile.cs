namespace LogiWheelForge.Models;

public enum MapperTriggerKind { Button, AxisRange, AxisStep }
public enum MapperStepMode { Movement, AwayFromCenter }
public enum MapperActionKind { Key, MouseButton, MouseMove, MouseScroll, HoldTarget, ReleaseTarget }
public enum MapperActionMode { Hold, Tap, Toggle }
public enum MapperForceRelease { RangeExit, Explicit }

/// <summary>Controls are logical names such as Steering, Accelerator, Brake, or Button1.</summary>
public sealed record MapperRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Control { get; init; } = "Steering";
    public MapperTriggerKind Trigger { get; init; } = MapperTriggerKind.AxisStep;
    public MapperStepMode StepMode { get; init; } = MapperStepMode.Movement;
    public double StepPercent { get; init; } = 5;
    public double MinPercent { get; init; } = 5;
    public double MaxPercent { get; init; } = 100;
    public double HysteresisPercent { get; init; } = 1;
    public double DeadZonePercent { get; init; }
    public double CurveExponent { get; init; } = 1;
    public int Direction { get; init; } = 1;
    public MapperActionKind Action { get; init; } = MapperActionKind.Key;
    public MapperActionMode Mode { get; init; } = MapperActionMode.Tap;
    public string Output { get; init; } = "D";
    public int DurationMs { get; init; } = 50;
    public double OutputScale { get; init; } = 1;
    public double ForceStrength { get; init; } = 25;
    public double TargetPercent { get; init; } = 5;
    public MapperForceRelease ForceRelease { get; init; } = MapperForceRelease.RangeExit;

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Control) ||
            !Enum.IsDefined(Trigger) || !Enum.IsDefined(StepMode) || !Enum.IsDefined(Action) ||
            !Enum.IsDefined(Mode) || !Enum.IsDefined(ForceRelease) || Direction is not (-1 or 1))
            error = "Rule identity, type, or direction is invalid.";
        else if (!double.IsFinite(StepPercent) || StepPercent is <= 0 or > 100 ||
                 !double.IsFinite(MinPercent) || MinPercent is < -100 or > 100 ||
                 !double.IsFinite(MaxPercent) || MaxPercent is < -100 or > 100 || MinPercent >= MaxPercent ||
                 !double.IsFinite(HysteresisPercent) || HysteresisPercent is < 0 or > 20 ||
                 !double.IsFinite(DeadZonePercent) || DeadZonePercent is < 0 or > 25 ||
                 !double.IsFinite(CurveExponent) || CurveExponent is < .1 or > 5 ||
                 !double.IsFinite(OutputScale) || OutputScale is < -100 or > 100 ||
                 !double.IsFinite(ForceStrength) || ForceStrength is < 0 or > 100 ||
                 !double.IsFinite(TargetPercent) || TargetPercent is < -100 or > 100 ||
                 DurationMs is < 10 or > 5000)
            error = "Rule percentages, curve, scale, or duration are out of range.";
        else if (Action is not (MapperActionKind.HoldTarget or MapperActionKind.ReleaseTarget) &&
                 string.IsNullOrWhiteSpace(Output))
            error = "Choose an output for the rule.";
        else if (Trigger == MapperTriggerKind.AxisStep && Mode == MapperActionMode.Hold &&
                 Action is (MapperActionKind.Key or MapperActionKind.MouseButton))
            error = "A step rule cannot hold a key or mouse button. Use an axis range.";
        else if (Trigger == MapperTriggerKind.AxisStep && Action == MapperActionKind.HoldTarget &&
                 ForceRelease == MapperForceRelease.RangeExit)
            error = "A step-triggered hold needs an explicit release rule.";
        return error.Length == 0;
    }
}

public sealed record MapperResistance
{
    public bool Enabled { get; init; }
    public double CenterStrength { get; init; } = 20;
    public double DampingStrength { get; init; } = 10;
    public bool DetentsEnabled { get; init; }
    public double DetentSpacingPercent { get; init; } = 5;
    public double DetentStrength { get; init; } = 25;

    public bool TryValidate() =>
        double.IsFinite(CenterStrength) && CenterStrength is >= 0 and <= 100 &&
        double.IsFinite(DampingStrength) && DampingStrength is >= 0 and <= 100 &&
        double.IsFinite(DetentSpacingPercent) && DetentSpacingPercent is > 0 and <= 50 &&
        double.IsFinite(DetentStrength) && DetentStrength is >= 0 and <= 100;
}

public sealed record InputMapperProfile
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "New profile";
    public bool Enabled { get; init; } = true;
    public string[] ProcessPaths { get; init; } = [];
    public MapperRule[] Rules { get; init; } = [];
    public MapperResistance Resistance { get; init; } = new();

    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(Id) ||
            string.IsNullOrWhiteSpace(Name) || Name.Length > 64)
            error = "Profile name or schema is invalid.";
        else if (ProcessPaths is null || ProcessPaths.Any(path => !Path.IsPathFullyQualified(path) ||
                     !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ||
                 ProcessPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ProcessPaths.Length)
            error = "Target processes must have distinct full executable paths.";
        else if (Rules is null || Rules.Select(rule => rule.Id).Distinct().Count() != Rules.Length ||
                 Rules.Any(rule => !rule.TryValidate(out _)))
            error = "A mapping rule is invalid or duplicated.";
        else if (Resistance is null || !Resistance.TryValidate())
            error = "Resistance settings are invalid.";
        return error.Length == 0;
    }
}
