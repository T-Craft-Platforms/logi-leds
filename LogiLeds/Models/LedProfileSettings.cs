using System.Net;
using System.Net.Sockets;

namespace LogiLeds.Models;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public enum RpmProfileMode
{
    Easy,
    Advanced
}

public sealed record LedProfileSettings
{
    public const int CurrentSchemaVersion = 4;
    public const string DefaultBindAddress = "0.0.0.0";
    public const int DefaultPort = 1024;
    public const double DefaultFirstLedPercent = 65;
    public const double DefaultRedlinePercent = 90;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string BindAddress { get; init; } = DefaultBindAddress;
    public int Port { get; init; } = DefaultPort;
    public double FirstLedPercent { get; init; } = DefaultFirstLedPercent;
    public double RedlinePercent { get; init; } = DefaultRedlinePercent;
    public bool BlinkAtRedline { get; init; } = true;
    public bool LearnPerCarShift { get; init; } = true;
    public bool AutoStartControl { get; init; } = true;
    public bool MinimizeToTray { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
    public bool UsePointerCursors { get; init; } = true;
    public bool ReadyAnimation { get; init; } = true;
    public AppTheme Theme { get; init; } = AppTheme.System;
    public RpmProfileMode ProfileMode { get; init; } = RpmProfileMode.Easy;
    public string GameTitle { get; init; } = "Auto";
    public string? PreferredWheelId { get; init; }
    public double[] AdvancedThresholds { get; init; } = [];
    public double WindowWidth { get; init; } = 1180;
    public double WindowHeight { get; init; } = 760;
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public bool WindowMaximized { get; init; }

    public static LedProfileSettings Defaults => new();

    public bool TryValidate(out string error)
    {
        if (!IPAddress.TryParse(BindAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            error = "Bind address must be a valid IPv4 address.";
            return false;
        }

        if (Port is < 1 or > 65535)
        {
            error = "UDP port must be between 1 and 65535.";
            return false;
        }

        if (!double.IsFinite(FirstLedPercent) || FirstLedPercent < 0 || FirstLedPercent >= 100)
        {
            error = "First LED must be between 0% and 99.9%.";
            return false;
        }

        if (!double.IsFinite(RedlinePercent) || RedlinePercent <= 0 || RedlinePercent > 100)
        {
            error = "Redline must be between 0.1% and 100%.";
            return false;
        }

        if (FirstLedPercent >= RedlinePercent)
        {
            error = "First LED must be below redline.";
            return false;
        }

        if (AdvancedThresholds.Any(x => !double.IsFinite(x) || x < 0 || x >= RedlinePercent) ||
            !AdvancedThresholds.SequenceEqual(AdvancedThresholds.OrderBy(x => x)))
        {
            error = "Advanced thresholds must be ordered and below redline.";
            return false;
        }

        if (!double.IsFinite(WindowWidth) || !double.IsFinite(WindowHeight) || WindowWidth < 900 || WindowHeight < 620)
        {
            error = "Window dimensions are invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}