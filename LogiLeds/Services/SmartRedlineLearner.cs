using System.Globalization;
using System.IO;
using System.Text.Json;
using LogiLeds.Models;

namespace LogiLeds.Services;

public sealed class SmartRedlineLearner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, double> _learned = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _learnedLock = new();
    private readonly string _path;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly Dictionary<string, LearningState> _states = new(StringComparer.OrdinalIgnoreCase);

    public SmartRedlineLearner(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiLeds", "calibrations.json");
    }

    public IReadOnlyList<CarTrainingMapping> GetMappings()
    {
        lock (_learnedLock)
        {
            return _learned
                .Select(pair => TryParseKey(pair.Key, out var mapping)
                    ? mapping with { LearnedRedlinePercent = pair.Value }
                    : null)
                .Where(mapping => mapping is not null)
                .Cast<CarTrainingMapping>()
                .OrderBy(mapping => mapping.GameTitle)
                .ThenBy(mapping => mapping.CarOrdinal)
                .ToArray();
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path)) return;
            await using var stream = File.OpenRead(_path);
            var values =
                await JsonSerializer.DeserializeAsync<Dictionary<string, double>>(stream, JsonOptions,
                    cancellationToken);
            if (values is null) return;
            lock (_learnedLock)
            {
                foreach (var pair in values.Where(x => x.Value is >= 70 and <= 100)) _learned[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    public double? Observe(ForzaTelemetryFrame frame, string gameTitle)
    {
        if (!frame.IsRaceOn || frame.EngineMaxRpm <= 0 || frame.CarOrdinal is null) return null;
        var key = BuildKey(frame, gameTitle);
        double existing;
        lock (_learnedLock)
        {
            _learned.TryGetValue(key, out existing);
        }

        if (existing > 0) return existing;
        if (frame.Accelerator is not byte throttle || throttle < 235 || frame.Gear is not byte gear ||
            gear == 0) return null;
        if (!_states.TryGetValue(key, out var state)) _states[key] = state = new LearningState();
        state.Peak = Math.Max(state.Peak, frame.CurrentEngineRpm);
        var eventEnded = state.LastRpm > 0 &&
                         (frame.CurrentEngineRpm < state.Peak * .92f || (state.LastGear > 0 && gear != state.LastGear));
        state.LastRpm = frame.CurrentEngineRpm;
        state.LastGear = gear;
        if (!eventEnded) return null;
        var percent = state.Peak / frame.EngineMaxRpm * 100d;
        state.Peak = frame.CurrentEngineRpm;
        if (percent is < 75 or > 100) return null;
        state.Candidates.Add(percent);
        if (state.Candidates.Count > 5) state.Candidates.RemoveAt(0);
        if (state.Candidates.Count < 3 || state.Candidates.Max() - state.Candidates.Min() > 2) return null;
        var ordered = state.Candidates.OrderBy(x => x).ToArray();
        var learned = Math.Round(ordered[ordered.Length / 2], 1);
        lock (_learnedLock)
        {
            _learned[key] = learned;
        }

        _ = SaveAsync();
        return learned;
    }

    public double? Get(ForzaTelemetryFrame frame, string gameTitle)
    {
        lock (_learnedLock)
        {
            return _learned.TryGetValue(BuildKey(frame, gameTitle), out var value) ? value : null;
        }
    }

    public async Task ClearAsync()
    {
        lock (_learnedLock)
        {
            _learned.Clear();
        }

        _states.Clear();
        await SaveAsync();
    }

    private static string BuildKey(ForzaTelemetryFrame frame, string gameTitle)
    {
        return $"{gameTitle}|{frame.ProtocolVariant}|{frame.CarOrdinal}|{Math.Round(frame.EngineMaxRpm / 50f) * 50:0}";
    }

    private static bool TryParseKey(string key, out CarTrainingMapping mapping)
    {
        mapping = default!;
        var parts = key.Split('|');
        if (parts.Length != 4 || !int.TryParse(parts[2], out var ordinal) ||
            !float.TryParse(parts[3], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var maximumRpm))
            return false;

        mapping = new CarTrainingMapping(parts[0], parts[1], ordinal, maximumRpm, 0);
        return true;
    }

    private async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temp = _path + ".tmp";
            Dictionary<string, double> snapshot;
            lock (_learnedLock)
            {
                snapshot = new Dictionary<string, double>(_learned, StringComparer.OrdinalIgnoreCase);
            }

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

    private sealed class LearningState
    {
        public float Peak { get; set; }
        public float LastRpm { get; set; }
        public byte LastGear { get; set; }
        public List<double> Candidates { get; } = [];
    }
}

public sealed record CarTrainingMapping(
    string GameTitle,
    string ProtocolVariant,
    int CarOrdinal,
    float EngineMaxRpm,
    double LearnedRedlinePercent);