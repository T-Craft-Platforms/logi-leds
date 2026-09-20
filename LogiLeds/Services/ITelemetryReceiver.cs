using LogiLeds.Models;

namespace LogiLeds.Services;

public interface ITelemetryReceiver : IAsyncDisposable
{
    event Action<ForzaTelemetryFrame>? FrameReceived;
    event Action<string>? ErrorOccurred;

    bool IsRunning { get; }
    Task StartAsync(LedProfileSettings settings, CancellationToken cancellationToken = default);
    Task StopAsync();
}
