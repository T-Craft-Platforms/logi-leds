using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public interface IMapperOutput
{
    bool Press(string output, bool mouseButton = false);
    bool Release(string output, bool mouseButton = false);
    void ReleaseAll();
    bool Move(int x, int y);
    bool Scroll(int clicks);
}

public interface IWheelResistanceController : IDisposable
{
    bool IsAvailable { get; }
    string Status { get; }
    bool Start(string wheelId, MapperResistance settings);
    void UpdatePosition(double steeringPercent);
    void Hold(double targetPercent, double strength);
    void ReleaseHold();
    void Stop();
}

/// <summary>Deterministic rule transitions. Caller owns the timer and foreground guard.</summary>
public sealed class MapperRuleEngine
{
    private sealed class RuleState
    {
        public double? Last;
        public double Travel;
        public int LastDirection;
        public bool Active;
        public bool Toggled;
    }
    private sealed record Pulse(MapperRule Rule, DateTimeOffset Until);
    private readonly IMapperOutput _output;
    private readonly IWheelResistanceController _force;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, RuleState> _states = [];
    private readonly Queue<MapperRule> _pulses = [];
    private readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);
    private InputMapperProfile? _profile;
    private Pulse? _currentPulse;
    private double _scrollRemainder;

    public MapperRuleEngine(IMapperOutput output, IWheelResistanceController force, TimeProvider? clock = null)
    {
        _output = output; _force = force; _clock = clock ?? TimeProvider.System;
    }

    public int DroppedPulses { get; private set; }
    public void Activate(InputMapperProfile profile, string wheelId)
    {
        Deactivate();
        _profile = profile;
        foreach (var rule in profile.Rules) _states[rule.Id] = new RuleState();
        if (profile.Resistance.Enabled) _force.Start(wheelId, profile.Resistance);
    }

    public void Deactivate()
    {
        _profile = null;
        _states.Clear(); _values.Clear(); _pulses.Clear(); _currentPulse = null;
        _output.ReleaseAll();
        _force.Stop();
        _scrollRemainder = 0;
    }

    public void Update(WheelInputSample sample)
    {
        if (_profile is null) return;
        _values[sample.Control] = sample.Percent;
        if (sample.Control.Equals("Steering", StringComparison.OrdinalIgnoreCase))
            _force.UpdatePosition(sample.Percent);
        foreach (var rule in _profile.Rules.Where(rule =>
                     rule.Control.Equals(sample.Control, StringComparison.OrdinalIgnoreCase)))
        {
            var state = _states[rule.Id];
            var value = ApplyCurve(sample.Percent, rule);
            switch (rule.Trigger)
            {
                case MapperTriggerKind.Button:
                    Transition(rule, state, value >= 50);
                    break;
                case MapperTriggerKind.AxisRange:
                    var hysteresis = state.Active ? rule.HysteresisPercent : 0;
                    Transition(rule, state, value >= rule.MinPercent - hysteresis &&
                                             value <= rule.MaxPercent + hysteresis);
                    break;
                case MapperTriggerKind.AxisStep:
                    if (state.Last is { } previous)
                    {
                        var delta = value - previous;
                        var direction = Math.Sign(delta);
                        if (direction != 0)
                        {
                            if (direction != state.LastDirection) state.Travel = 0;
                            state.LastDirection = direction;
                            var eligible = direction == rule.Direction &&
                                           (rule.StepMode == MapperStepMode.Movement ||
                                            Math.Abs(value) > Math.Abs(previous) && Math.Sign(value) == direction);
                            if (eligible && Math.Abs(delta) >= rule.DeadZonePercent / 10)
                            {
                                state.Travel += Math.Abs(delta);
                                var steps = Math.Min(20, (int)(state.Travel / rule.StepPercent));
                                state.Travel -= steps * rule.StepPercent;
                                for (var i = 0; i < steps; i++) Fire(rule, state);
                            }
                        }
                    }
                    state.Last = value;
                    break;
            }
        }
    }

    public void Tick()
    {
        if (_profile is null) return;
        var now = _clock.GetUtcNow();
        if (_currentPulse is not null && now >= _currentPulse.Until)
        {
            _output.Release(_currentPulse.Rule.Output, _currentPulse.Rule.Action == MapperActionKind.MouseButton);
            _currentPulse = null;
        }
        if (_currentPulse is null && _pulses.TryDequeue(out var rule))
        {
            if (_output.Press(rule.Output, rule.Action == MapperActionKind.MouseButton))
                _currentPulse = new Pulse(rule, now.AddMilliseconds(rule.DurationMs));
        }
        foreach (var rule in _profile.Rules.Where(rule => rule.Action is MapperActionKind.MouseMove or MapperActionKind.MouseScroll &&
                     rule.Trigger == MapperTriggerKind.AxisRange && _states[rule.Id].Active))
        {
            if (!_values.TryGetValue(rule.Control, out var value)) continue;
            var amount = ApplyCurve(value, rule) / 100 * rule.OutputScale;
            if (rule.Action == MapperActionKind.MouseMove)
            {
                var pixels = (int)Math.Round(amount);
                if (pixels != 0)
                    _output.Move(rule.Output.Equals("Vertical", StringComparison.OrdinalIgnoreCase) ? 0 : pixels,
                        rule.Output.Equals("Vertical", StringComparison.OrdinalIgnoreCase) ? pixels : 0);
            }
            else
            {
                _scrollRemainder += amount / 60;
                var clicks = (int)Math.Truncate(_scrollRemainder);
                if (clicks != 0) { _output.Scroll(clicks); _scrollRemainder -= clicks; }
            }
        }
    }

    private void Transition(MapperRule rule, RuleState state, bool active)
    {
        if (active == state.Active) return;
        state.Active = active;
        if (active) Fire(rule, state);
        else
        {
            if (rule.Mode == MapperActionMode.Hold && rule.Action is MapperActionKind.Key or MapperActionKind.MouseButton)
                _output.Release(rule.Output, rule.Action == MapperActionKind.MouseButton);
            if (rule.Action == MapperActionKind.HoldTarget && rule.ForceRelease == MapperForceRelease.RangeExit)
                _force.ReleaseHold();
        }
    }

    private void Fire(MapperRule rule, RuleState state)
    {
        if (rule.Action == MapperActionKind.HoldTarget)
        {
            _force.Hold(rule.TargetPercent, Math.Abs(rule.OutputScale)); return;
        }
        if (rule.Action == MapperActionKind.ReleaseTarget) { _force.ReleaseHold(); return; }
        if (rule.Action is not (MapperActionKind.Key or MapperActionKind.MouseButton)) return;
        var mouse = rule.Action == MapperActionKind.MouseButton;
        switch (rule.Mode)
        {
            case MapperActionMode.Hold: _output.Press(rule.Output, mouse); break;
            case MapperActionMode.Toggle:
                state.Toggled = !state.Toggled;
                if (state.Toggled) _output.Press(rule.Output, mouse);
                else _output.Release(rule.Output, mouse);
                break;
            case MapperActionMode.Tap:
                if (_pulses.Count < 12) _pulses.Enqueue(rule);
                else DroppedPulses++;
                break;
        }
    }

    private static double ApplyCurve(double value, MapperRule rule)
    {
        var magnitude = Math.Abs(value);
        if (magnitude <= rule.DeadZonePercent) return 0;
        var fraction = (magnitude - rule.DeadZonePercent) / (100 - rule.DeadZonePercent);
        return Math.Sign(value) * Math.Pow(fraction, rule.CurveExponent) * 100;
    }
}
