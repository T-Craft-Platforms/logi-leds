using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Tests;

[TestClass]
public sealed class InputMapperTests
{
    [TestMethod]
    public void Profiles_RejectAnExecutableInTwoEnabledCollections()
    {
        var path = Path.GetFullPath("sample.exe");
        var first = new InputMapperProfile { Name = "First", ProcessPaths = [path] };
        var second = new InputMapperProfile { Name = "Second", ProcessPaths = [path.ToUpperInvariant()] };
        Assert.ThrowsException<ArgumentException>(() => InputMapperProfileStore.Validate([first, second]));
        InputMapperProfileStore.Validate([first, second with { Enabled = false }]);
    }

    [TestMethod]
    public void WheelDefinitions_AcceptInputOnlyAndRejectInvalidControlBindings()
    {
        var inputOnly = new WheelDefinition
        {
            SchemaVersion = 2, Id = "pedal", DisplayName = "Pedals", ProductIds = [1],
            HasLedOutput = false, PhysicalLedCount = 0, ControlGroupCount = 0,
            InputControls = [new WheelInputControl { Name = "Brake", UsagePage = 1, Usage = 0x32 }]
        };
        Assert.IsTrue(inputOnly.TryValidate(out _));
        Assert.IsFalse((inputOnly with { InputControls = [new WheelInputControl { Name = "Brake", Usage = 0 }] })
            .TryValidate(out _));
    }

    [TestMethod]
    public void SteeringSteps_TapForEachFivePercentAndReleaseOnStop()
    {
        var output = new FakeOutput();
        var force = new FakeForce();
        var clock = new TestClock();
        var engine = new MapperRuleEngine(output, force, clock);
        engine.Activate(new InputMapperProfile
        {
            Rules = [new MapperRule { Control = "Steering", StepPercent = 5, DeadZonePercent = 0,
                Output = "D", DurationMs = 50 }]
        }, "wheel");
        engine.Update(new WheelInputSample("Steering", 0, false));
        engine.Update(new WheelInputSample("Steering", 12, false));
        engine.Tick();
        Assert.AreEqual(1, output.Presses);
        clock.Advance(50);
        engine.Tick();
        Assert.AreEqual(2, output.Presses);
        engine.Deactivate();
        Assert.AreEqual(2, output.ReleaseAllCalls);
    }

    [TestMethod]
    public void SteeringAwayFromCenter_IgnoresReturnTravel()
    {
        var output = new FakeOutput();
        var engine = new MapperRuleEngine(output, new FakeForce());
        engine.Activate(new InputMapperProfile
        {
            Rules = [new MapperRule { Control = "Steering", StepPercent = 5, DeadZonePercent = 0,
                StepMode = MapperStepMode.AwayFromCenter, Output = "D" }]
        }, "wheel");
        engine.Update(new WheelInputSample("Steering", 0, false));
        engine.Update(new WheelInputSample("Steering", 10, false));
        engine.Tick();
        engine.Update(new WheelInputSample("Steering", 5, false));
        engine.Tick();
        Assert.AreEqual(1, output.Presses);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int ms) => _now = _now.AddMilliseconds(ms);
    }
    private sealed class FakeOutput : IMapperOutput
    {
        public string? LastError => null;
        public int Presses { get; private set; }
        public int ReleaseAllCalls { get; private set; }
        public bool Press(string output, bool mouseButton = false) { Presses++; return true; }
        public bool Release(string output, bool mouseButton = false) => true;
        public void ReleaseAll() => ReleaseAllCalls++;
        public bool Move(int x, int y) => true;
        public bool Scroll(int clicks) => true;
    }
    private sealed class FakeForce : IWheelResistanceController
    {
        public bool IsAvailable => true;
        public string Status => "ready";
        public bool Start(string wheelId, MapperResistance settings) => true;
        public void UpdatePosition(double steeringPercent) { }
        public void Hold(double targetPercent, double strength) { }
        public void ReleaseHold() { }
        public void Stop() { }
        public void Dispose() { }
    }
}
