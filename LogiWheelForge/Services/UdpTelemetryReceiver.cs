using System.Net;
using System.Net.Sockets;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed class UdpTelemetryReceiver(TimeProvider? timeProvider = null) : ITelemetryReceiver
{
    private readonly List<UdpClient> _clients = [];
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private CancellationTokenSource? _receiveCts;
    private Task[] _receiveTasks = [];

    public event Action<TelemetryFrame>? FrameReceived;
    public event Action<string>? ErrorOccurred;

    public bool IsRunning => _clients.Count > 0;

    public async Task StartAsync(LedProfileSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out var validationError))
            throw new ArgumentException(validationError, nameof(settings));

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;

            var configuredGames = settings.TelemetryGames.Where(game =>
                game.Enabled && settings.TelemetryWatch.Watches(game.Game)).ToArray();
            var clients = new List<(TelemetryGameSettings Game, UdpClient Client)>();
            try
            {
                foreach (var game in configuredGames)
                {
                    var client = new UdpClient(AddressFamily.InterNetwork);
                    clients.Add((game, client));
                    client.Client.ExclusiveAddressUse = true;
                    client.Client.Bind(new IPEndPoint(IPAddress.Parse(game.BindAddress), game.Port));
                }
            }
            catch
            {
                foreach (var (_, client) in clients) client.Dispose();
                throw;
            }

            _receiveCts = new CancellationTokenSource();
            _clients.AddRange(clients.Select(item => item.Client));
            _receiveTasks = clients.Select(item => ReceiveLoopAsync(item.Client, item.Game, _receiveCts.Token))
                .ToArray();
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
            var clients = _clients.ToArray();
            var receiveTasks = _receiveTasks;
            _receiveCts = null;
            _clients.Clear();
            _receiveTasks = [];

            cts?.Cancel();
            foreach (var client in clients) client.Dispose();
            try
            {
                await Task.WhenAll(receiveTasks);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            cts?.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
    }

    private async Task ReceiveLoopAsync(UdpClient client, TelemetryGameSettings game,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(cancellationToken);
                var receivedAt = _timeProvider.GetUtcNow();
                var parsed = game.Game == TelemetryGame.Forza
                    ? ForzaTelemetryParser.TryParse(result.Buffer, receivedAt, out var frame)
                    : BeamNgOutGaugeParser.TryParse(result.Buffer, game.MaxRpm, receivedAt, out frame);
                if (parsed)
                    FrameReceived?.Invoke(frame);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Telemetry receiver stopped: {ex.Message}");
        }
    }
}