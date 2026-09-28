using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Tests;

[TestClass]
public sealed class LedIndicatorServiceTests
{
    [TestMethod]
    public async Task ActiveFrame_DrivesWheelLevel_AndStopClearsLeds()
    {
        var receiver = new FakeTelemetryReceiver();
        var wheel = new FakeWheelController();
        await using var service = CreateService(receiver, wheel);
        await service.LoadSettingsAsync();
        service.InitializeWindow(1);
        await service.StartAsync();
        receiver.Emit(new TelemetryFrame(true, 10, 10_000, 900, 8_500, DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => wheel.SetCalls > 0);
        Assert.IsTrue(wheel.LastLevel is >= 1 and <= 5);
        await service.StopAsync();
        Assert.IsTrue(wheel.ClearCalls > 0);
        Assert.IsFalse(receiver.IsRunning);
    }

    [TestMethod]
    public async Task StaleTelemetry_ClearsLeds()
    {
        var receiver = new FakeTelemetryReceiver();
        var wheel = new FakeWheelController();
        await using var service = CreateService(receiver, wheel);
        await service.LoadSettingsAsync();
        service.InitializeWindow(1);
        await service.StartAsync();
        receiver.Emit(new TelemetryFrame(true, 10, 9_000, 900, 8_000, DateTimeOffset.UtcNow));
        await WaitUntilAsync(() => wheel.SetCalls > 0);
        wheel.ClearCalls = 0;
        await WaitUntilAsync(() => wheel.ClearCalls > 0, TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task Snapshot_UsesSelectedPreviewWheel_WhenNoPhysicalWheelIsConnected()
    {
        var receiver = new FakeTelemetryReceiver();
        var wheel = new FakeWheelController(false);
        await using var service = CreateService(receiver, wheel);
        AppSnapshot? snapshot = null;
        service.SnapshotChanged += (_, current) => snapshot = current;

        await service.LoadSettingsAsync();
        service.InitializeWindow(1);
        service.SetPreviewWheel("fake");
        await service.StartAsync();
        receiver.Emit(new TelemetryFrame(true, 10, 10_000, 900, 8_500, DateTimeOffset.UtcNow));

        await WaitUntilAsync(() => snapshot?.PreviewWheel?.Id == "fake" && snapshot.IlluminatedLedCount > 0);
        Assert.IsFalse(snapshot!.IsWheelConnected);
        Assert.AreEqual("fake", snapshot.PreviewWheel!.Id);
    }

    private static LedIndicatorService CreateService(FakeTelemetryReceiver receiver, FakeWheelController wheel)
    {
        var path = Path.Combine(Path.GetTempPath(), "LogiWheelForge.Tests", Guid.NewGuid() + ".json");
        return new LedIndicatorService(receiver, wheel, new SettingsStore(path),
            redlineLearner: new SmartRedlineLearner(path + ".calibrations"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var limit = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));
        while (!condition() && DateTime.UtcNow < limit) await Task.Delay(20);
        Assert.IsTrue(condition(), "Condition was not reached before the timeout.");
    }

    private sealed class FakeTelemetryReceiver : ITelemetryReceiver
    {
        public event Action<TelemetryFrame>? FrameReceived;

        public event Action<string>? ErrorOccurred
        {
            add { }
            remove { }
        }

        public bool IsRunning { get; private set; }

        public Task StartAsync(LedProfileSettings settings, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }

        public void Emit(TelemetryFrame frame)
        {
            FrameReceived?.Invoke(frame);
        }
    }

    private sealed class FakeWheelController : IWheelLedController
    {
        private static readonly WheelDefinition Definition = new()
        {
            Id = "fake", DisplayName = "Fake G29", ProductIds = [1], PhysicalLedCount = 10, ControlGroupCount = 5,
            Colors = Enumerable.Repeat("#FF0000", 10).ToArray()
        };

        public FakeWheelController(bool isConnected = true)
        {
            IsConnected = isConnected;
        }

        public int SetCalls { get; private set; }
        public int ClearCalls { get; set; }
        public int LastLevel { get; private set; }

        public bool IsConnected { get; private set; }
        public string WheelName => Definition.DisplayName;
        public string StatusMessage => "Wheel ready";
        public WheelDefinition? CurrentDefinition => IsConnected ? Definition : null;
        public IReadOnlyList<WheelDefinition> AvailableDefinitions => [Definition];
        public IReadOnlyList<string> DefinitionDiagnostics => [];

        public bool Initialize(nint windowHandle)
        {
            return true;
        }

        public void SetPreferredWheel(string? wheelId)
        {
        }

        public void Refresh()
        {
        }

        public void RefreshNow()
        {
        }

        public bool SetLevel(int illuminatedGroups)
        {
            SetCalls++;
            LastLevel = illuminatedGroups;
            return true;
        }

        public void ClearLeds()
        {
            ClearCalls++;
        }

        public Task TestLedsAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PlayReadyAnimationAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Shutdown()
        {
            IsConnected = false;
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}