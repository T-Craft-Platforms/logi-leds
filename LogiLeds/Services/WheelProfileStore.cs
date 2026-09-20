using System.Text.Json;
using LogiLeds.Models;
using System.IO;

namespace LogiLeds.Services;

public sealed class WheelProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    public WheelProfileStore(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiLeds", "profiles");

    public async Task<WheelProfile> LoadAsync(WheelDefinition wheel, LedProfileSettings legacy, CancellationToken cancellationToken = default)
    {
        var fallback = new WheelProfile
        {
            WheelId = wheel.Id, Mode = legacy.ProfileMode, FirstLedPercent = legacy.FirstLedPercent,
            RedlinePercent = legacy.RedlinePercent, BlinkAtRedline = legacy.BlinkAtRedline,
            AdvancedThresholds = legacy.AdvancedThresholds.Length == wheel.ControlGroupCount
                ? legacy.AdvancedThresholds : LedMath.BuildRecommendedThresholds(wheel.ControlGroupCount)
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
                   profile.WheelId == wheel.Id && profile.TryValidate(wheel.ControlGroupCount, out _) ? profile : fallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return fallback; }
    }

    public async Task SaveAsync(WheelProfile profile, int groupCount, CancellationToken cancellationToken = default)
    {
        if (!profile.TryValidate(groupCount, out var error)) throw new ArgumentException(error, nameof(profile));
        Directory.CreateDirectory(_directory);
        var path = GetPath(profile.WheelId); var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(profile with { SchemaVersion = WheelProfile.CurrentSchemaVersion }, JsonOptions), cancellationToken);
        File.Move(temp, path, true);
    }

    private string GetPath(string wheelId) => Path.Combine(_directory, $"{wheelId}.json");
}
