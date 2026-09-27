using System.Globalization;
using System.IO;
using System.Text.Json;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed class SmartRedlineLearner
{
    private const int TargetShifts = 5;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, LearningState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrainingData> _trainings = new(StringComparer.OrdinalIgnoreCase);

    public SmartRedlineLearner(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiWheelForge", "calibrations.json");
    }

    public IReadOnlyList<CarTrainingMapping> GetMappings()
    {
        lock (_gate)
            return _trainings.Select(pair => TryParseKey(pair.Key, out var mapping)
                    ? mapping with
                    {
                        LearnedRedlinePercent = Estimate(pair.Value.Candidates),
                        ShiftCount = pair.Value.Candidates.Count,
                        ConfidencePercent = Confidence(pair.Value.Candidates)
                    }
                    : null)
                .Where(mapping => mapping is { ProgressPercent: >= 10 })
                .Cast<CarTrainingMapping>()
                .OrderBy(mapping => mapping.GameTitle)
                .ThenBy(mapping => mapping.CarOrdinal)
                .ToArray();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path)) return;
            await using var stream = File.OpenRead(_path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            lock (_gate)
            {
                _trainings.Clear();
                foreach (var pair in document.RootElement.EnumerateObject())
                {
                    if (!TryParseKey(pair.Name, out _)) continue;
                    // Previous releases stored only the final percentage.
                    if (pair.Value.ValueKind == JsonValueKind.Number && pair.Value.TryGetDouble(out var legacy))
                    {
                        if (legacy is >= 70 and <= 100)
                            _trainings[pair.Name] = new TrainingData
                                { Candidates = Enumerable.Repeat(legacy, TargetShifts).ToList() };
                        continue;
                    }

                    if (pair.Value.ValueKind != JsonValueKind.Object ||
                        !pair.Value.TryGetProperty(nameof(TrainingData.Candidates), out var candidates) ||
                        candidates.ValueKind != JsonValueKind.Array) continue;
                    var values = candidates.EnumerateArray()
                        .Where(value => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _))
                        .Select(value => value.GetDouble())
                        .Where(value => double.IsFinite(value) && value is >= 70 and <= 100)
                        .TakeLast(8).ToList();
                    if (values.Count > 0) _trainings[pair.Name] = new TrainingData { Candidates = values };
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    public double? Observe(TelemetryFrame frame, string gameTitle)
    {
        if (!frame.IsRaceOn || frame.EngineMaxRpm <= 0 || frame.CarOrdinal is null ||
            !float.IsFinite(frame.CurrentEngineRpm) || frame.Gear is not byte gear || gear is < 1 or > 10 ||
            frame.Accelerator is not byte throttle) return null;

        var key = BuildKey(frame, gameTitle);
        var accepted = false;
        double? learned;
        lock (_gate)
        {
            if (!_states.TryGetValue(key, out var state)) _states[key] = state = new LearningState();
            if (state.LastFrameAt != default &&
                (frame.ReceivedAt - state.LastFrameAt).Duration() > TimeSpan.FromSeconds(3))
                state.Reset();

            if (state.LastGear is byte oldGear && gear != oldGear)
            {
                if (gear == oldGear + 1 && state.HighThrottleFrames >= 2 &&
                    frame.ReceivedAt - state.LastHighThrottleAt <= TimeSpan.FromMilliseconds(900))
                {
                    var percent = state.Peak / frame.EngineMaxRpm * 100d;
                    if (percent is >= 75 and <= 100 && state.Peak > frame.EngineIdleRpm * 1.5f)
                    {
                        if (!_trainings.TryGetValue(key, out var data)) _trainings[key] = data = new TrainingData();
                        data.Candidates.Add(percent);
                        if (data.Candidates.Count > 8) data.Candidates.RemoveAt(0);
                        accepted = true;
                    }
                }

                state.Reset();
            }

            if (throttle >= 220 && frame.CurrentEngineRpm >= frame.EngineMaxRpm * .72f)
            {
                state.HighThrottleFrames++;
                state.Peak = Math.Max(state.Peak, frame.CurrentEngineRpm);
                state.LastHighThrottleAt = frame.ReceivedAt;
            }

            state.LastGear = gear;
            state.LastFrameAt = frame.ReceivedAt;
            learned = _trainings.TryGetValue(key, out var training) ? Estimate(training.Candidates) : null;
        }

        if (accepted) _ = SaveAsync();
        return learned;
    }

    public double? Get(TelemetryFrame frame, string gameTitle)
    {
        lock (_gate)
            return _trainings.TryGetValue(BuildKey(frame, gameTitle), out var data) ? Estimate(data.Candidates) : null;
    }

    public bool IsCurrentCar(CarTrainingMapping mapping, TelemetryFrame frame, string gameTitle) =>
        string.Equals(BuildKey(frame, gameTitle), BuildKey(mapping), StringComparison.OrdinalIgnoreCase);

    public async Task ClearAsync()
    {
        lock (_gate)
        {
            _trainings.Clear();
            _states.Clear();
        }

        await SaveAsync();
    }

    internal async Task FlushAsync()
    {
        await _saveGate.WaitAsync();
        _saveGate.Release();
    }

    private static double? Estimate(IReadOnlyList<double> candidates)
    {
        if (candidates.Count < 3) return null;
        var cluster = BestCluster(candidates);
        if (cluster.Length < 3) return null;
        return Math.Round(cluster.Order().ElementAt(cluster.Length / 2), 1);
    }

    private static int Confidence(IReadOnlyList<double> candidates)
    {
        var cluster = BestCluster(candidates);
        return cluster.Length < 3
            ? 0
            : (int)Math.Round(Math.Min(1d, cluster.Length / (double)TargetShifts) *
                cluster.Length / candidates.Count * 100);
    }

    private static double[] BestCluster(IReadOnlyList<double> candidates) => candidates
        .Select(center => candidates.Where(value => Math.Abs(value - center) <= 2).ToArray())
        .OrderByDescending(cluster => cluster.Length)
        .ThenBy(cluster => cluster.Max() - cluster.Min())
        .FirstOrDefault() ?? [];

    private static string BuildKey(TelemetryFrame frame, string gameTitle) =>
        $"{gameTitle}|{frame.ProtocolVariant}|{frame.CarOrdinal}|{Math.Round(frame.EngineMaxRpm / 50f) * 50:0}";

    private static string BuildKey(CarTrainingMapping mapping) =>
        $"{mapping.GameTitle}|{mapping.ProtocolVariant}|{mapping.CarOrdinal}|{mapping.EngineMaxRpm:0}";

    private static bool TryParseKey(string key, out CarTrainingMapping mapping)
    {
        mapping = default!;
        var parts = key.Split('|');
        if (parts.Length != 4 || !int.TryParse(parts[2], out var ordinal) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var maximumRpm))
            return false;
        mapping = new CarTrainingMapping(parts[0], parts[1], ordinal, maximumRpm, null, 0, 0);
        return true;
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            Dictionary<string, TrainingData> snapshot;
            lock (_gate)
                snapshot = _trainings.ToDictionary(pair => pair.Key,
                    pair => new TrainingData { Candidates = [.. pair.Value.Candidates] },
                    StringComparer.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temp, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public sealed class TrainingData
    {
        public List<double> Candidates { get; set; } = [];
    }

    private sealed class LearningState
    {
        public float Peak { get; set; }
        public int HighThrottleFrames { get; set; }
        public byte? LastGear { get; set; }
        public DateTimeOffset LastHighThrottleAt { get; set; }
        public DateTimeOffset LastFrameAt { get; set; }

        public void Reset()
        {
            Peak = 0;
            HighThrottleFrames = 0;
            LastGear = null;
            LastHighThrottleAt = default;
        }
    }
}

public sealed record CarTrainingMapping(
    string GameTitle,
    string ProtocolVariant,
    int CarOrdinal,
    float EngineMaxRpm,
    double? LearnedRedlinePercent,
    int ShiftCount,
    int ConfidencePercent)
{
    public int ProgressPercent => Math.Min(100, ShiftCount * 20);
}