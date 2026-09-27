namespace LogiWheelForge.Models;

public readonly record struct TelemetryFrame(
    bool IsRaceOn,
    uint TimestampMilliseconds,
    float EngineMaxRpm,
    float EngineIdleRpm,
    float CurrentEngineRpm,
    DateTimeOffset ReceivedAt,
    string ProtocolVariant = "Forza Horizon Dash",
    int? CarOrdinal = null,
    byte? Gear = null,
    byte? Accelerator = null,
    TelemetryGame Game = TelemetryGame.Forza);
