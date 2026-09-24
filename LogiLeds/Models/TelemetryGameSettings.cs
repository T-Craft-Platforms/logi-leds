using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;

namespace LogiLeds.Models;

public enum TelemetryGame
{
    Forza,
    BeamNg
}

public enum TelemetryWatchMode
{
    Auto,
    Forza,
    BeamNg
}

public static class TelemetryWatchModeExtensions
{
    public static bool Watches(this TelemetryWatchMode mode, TelemetryGame game) => mode switch
    {
        TelemetryWatchMode.Auto => true,
        TelemetryWatchMode.Forza => game == TelemetryGame.Forza,
        TelemetryWatchMode.BeamNg => game == TelemetryGame.BeamNg,
        _ => false
    };
}

public sealed record TelemetryGameSettings
{
    public TelemetryGame Game { get; init; }
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; }
    public int MaxRpm { get; init; } = 7000;

    [JsonIgnore] public string Name => Game == TelemetryGame.Forza ? "Forza" : "BeamNG.drive";
    [JsonIgnore] public string Endpoint => $"{BindAddress}:{Port}";
    [JsonIgnore] public string Detail => Game == TelemetryGame.BeamNg
        ? $"OutGauge · {Endpoint} · {MaxRpm:N0} max RPM"
        : $"Data Out · {Endpoint}";

    public static TelemetryGameSettings DefaultForza => new() { Game = TelemetryGame.Forza, Port = 1024 };
    public static TelemetryGameSettings DefaultBeamNg => new() { Game = TelemetryGame.BeamNg, Port = 4444 };

    public bool TryValidate(out string error)
    {
        if (!IPAddress.TryParse(BindAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            error = $"{Name}: enter a valid IPv4 bind address.";
            return false;
        }

        if (Port is < 1 or > 65535)
        {
            error = $"{Name}: UDP port must be between 1 and 65535.";
            return false;
        }

        if (Game == TelemetryGame.BeamNg && MaxRpm is < 1000 or > 30000)
        {
            error = "BeamNG.drive: maximum RPM must be between 1,000 and 30,000.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
