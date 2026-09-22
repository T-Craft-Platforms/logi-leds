using LogiLeds.Models;

namespace LogiLeds.Services;

public interface ITelemetryReceiver : IAsyncDisposable
{
    bool IsRunning { get; }
    event Action<ForzaTelemetryFrame>? FrameReceived;
    event Action<string>? ErrorOccurred;
    Task StartAsync(LedProfileSettings settings, CancellationToken cancellationToken = default);
    Task StopAsync();
}