using System.IO;
using System.Text.Json;
using LogiLeds.Models;

namespace LogiLeds.Services;

public sealed class WheelProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly SemaphoreSlim _namedProfilesGate = new(1, 1);

    public WheelProfileStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiLeds", "profiles");
    }

    public async Task<WheelProfile> LoadAsync(WheelDefinition wheel, LedProfileSettings legacy,
        CancellationToken cancellationToken = default)
    {
        var fallback = new WheelProfile
        {
            WheelId = wheel.Id, Mode = legacy.ProfileMode, FirstLedPercent = legacy.FirstLedPercent,
            RedlinePercent = legacy.RedlinePercent, BlinkAtRedline = legacy.BlinkAtRedline,
            AdvancedThresholds = legacy.AdvancedThresholds.Length == wheel.ControlGroupCount
                ? legacy.AdvancedThresholds
                : LedMath.BuildRecommendedThresholds(wheel.ControlGroupCount)
        };
        var path = GetPath(wheel.Id);
        try
        {
            if (!File.Exists(path)) return fallback;
            await using var stream = File.OpenRead(path);
            var profile = await JsonSerializer.DeserializeAsync<WheelProfile>(stream, JsonOptions, cancellationToken);
            // Profiles from the preview schema used provisional thresholds;
            // use the migrated settings fallback so Smart mode follows Forza.
            return profile is not null && profile.SchemaVersion >= WheelProfile.CurrentSchemaVersion &&
                   profile.WheelId == wheel.Id && profile.TryValidate(wheel.ControlGroupCount, out _)
                ? profile
                : fallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    public async Task SaveAsync(WheelProfile profile, int groupCount, CancellationToken cancellationToken = default)
    {
        if (!profile.TryValidate(groupCount, out var error)) throw new ArgumentException(error, nameof(profile));
        Directory.CreateDirectory(_directory);
        var path = GetPath(profile.WheelId);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp,
            JsonSerializer.Serialize(profile with { SchemaVersion = WheelProfile.CurrentSchemaVersion }, JsonOptions),
            cancellationToken);
        File.Move(temp, path, true);
    }

    public async Task<IReadOnlyList<WheelProfile>> ListNamedAsync(string wheelId,
        CancellationToken cancellationToken = default)
    {
        await _namedProfilesGate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await ReadNamedProfilesAsync(cancellationToken);
            return profiles.Where(x => x.WheelId == wheelId)
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally
        {
            _namedProfilesGate.Release();
        }
    }

    public async Task<WheelProfile> SaveNamedAsync(WheelProfile profile, int groupCount,
        CancellationToken cancellationToken = default)
    {
        var name = profile.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 48)
            throw new ArgumentException("Profile name must contain 1 to 48 characters.", nameof(profile));
        if (!profile.TryValidate(groupCount, out var error)) throw new ArgumentException(error, nameof(profile));

        await _namedProfilesGate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await ReadNamedProfilesAsync(cancellationToken);
            var id = string.IsNullOrWhiteSpace(profile.ProfileId) ? Guid.NewGuid().ToString("N") : profile.ProfileId;
            if (profiles.Any(x => x.WheelId == profile.WheelId && x.ProfileId != id &&
                                  string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A profile with that name already exists for this wheel.");
            var saved = profile with
            {
                SchemaVersion = WheelProfile.CurrentSchemaVersion, ProfileId = id, Name = name
            };
            var index = profiles.FindIndex(x => x.ProfileId == id && x.WheelId == profile.WheelId);
            if (index >= 0) profiles[index] = saved;
            else profiles.Add(saved);
            await WriteNamedProfilesAsync(profiles, cancellationToken);
            return saved;
        }
        finally
        {
            _namedProfilesGate.Release();
        }
    }

    public async Task DeleteNamedAsync(string wheelId, string profileId,
        CancellationToken cancellationToken = default)
    {
        await _namedProfilesGate.WaitAsync(cancellationToken);
        try
        {
            var profiles = await ReadNamedProfilesAsync(cancellationToken);
            if (profiles.RemoveAll(x => x.WheelId == wheelId && x.ProfileId == profileId) > 0)
                await WriteNamedProfilesAsync(profiles, cancellationToken);
        }
        finally
        {
            _namedProfilesGate.Release();
        }
    }

    private async Task<List<WheelProfile>> ReadNamedProfilesAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, "named-profiles.json");
        if (!File.Exists(path)) return [];
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<WheelProfile>>(stream, JsonOptions, cancellationToken) ??
                   [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Saved RPM profiles could not be read. The profile file may be damaged.", ex);
        }
    }

    private async Task WriteNamedProfilesAsync(List<WheelProfile> profiles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "named-profiles.json");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(profiles, JsonOptions), cancellationToken);
        File.Move(temp, path, true);
    }

    private string GetPath(string wheelId)
    {
        return Path.Combine(_directory, $"{wheelId}.json");
    }
}