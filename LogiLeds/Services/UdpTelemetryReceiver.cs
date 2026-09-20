using System.Net;
using System.Net.Sockets;
using LogiLeds.Models;

namespace LogiLeds.Services;

public sealed class UdpTelemetryReceiver(TimeProvider? timeProvider = null) : ITelemetryReceiver
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private UdpClient? _client;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    public event Action<ForzaTelemetryFrame>? FrameReceived;
    public event Action<string>? ErrorOccurred;

    public bool IsRunning => _receiveTask is { IsCompleted: false };

    public async Task StartAsync(LedProfileSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out var validationError))
        {
            throw new ArgumentException(validationError, nameof(settings));
        }

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;

            var endpoint = new IPEndPoint(IPAddress.Parse(settings.BindAddress), settings.Port);
            var client = new UdpClient(AddressFamily.InterNetwork);
            try
            {
                client.Client.ExclusiveAddressUse = true;
                client.Client.Bind(endpoint);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            _client = client;
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = ReceiveLoopAsync(client, _receiveCts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            var cts = _receiveCts;
            var client = _client;
            var receiveTask = _receiveTask;
            _receiveCts = null;
            _client = null;
            _receiveTask = null;

            cts?.Cancel();
            client?.Dispose();
            if (receiveTask is not null)
            {
                try { await receiveTask; }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
            }
            cts?.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cancellationToken);
                if (ForzaTelemetryParser.TryParse(result.Buffer, _timeProvider.GetUtcNow(), out var frame))
                {
                    FrameReceived?.Invoke(frame);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (SocketException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Telemetry receiver stopped: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
    }
}
