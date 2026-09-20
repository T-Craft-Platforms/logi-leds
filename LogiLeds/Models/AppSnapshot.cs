namespace LogiLeds.Models;

public enum ReadinessState { ControlPaused, SearchingForWheel, WaitingForTelemetry, Ready, Driving, TelemetryStale, NeedsAttention }

public sealed record AppSnapshot(
    bool IsRunning,
    bool IsTelemetryConnected,
    bool IsWheelConnected,
    bool IsRaceOn,
    float CurrentRpm,
    float MaximumRpm,
    int IlluminatedLedCount,
    bool IsFlashing,
    string WheelName,
    string StatusMessage,
    ReadinessState State = ReadinessState.ControlPaused,
    string TelemetryFormat = "—",
    WheelDefinition? Wheel = null,
    double? LearnedRedlinePercent = null);
