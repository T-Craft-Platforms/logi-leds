using System.IO;
using System.Text.Json;
using LogiLeds.Models;

namespace LogiLeds.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogiLeds",
            "settings.json");
    }

    public async Task<LedProfileSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_settingsPath)) return LedProfileSettings.Defaults;
            LedProfileSettings? settings;
            await using (var stream = File.OpenRead(_settingsPath))
            {
                settings = await JsonSerializer.DeserializeAsync<LedProfileSettings>(stream, JsonOptions,
                    cancellationToken);
            }

            if (settings is not null && (settings.SchemaVersion < LedProfileSettings.CurrentSchemaVersion ||
                                         settings.TelemetryGames is not { Length: > 0 }))
            {
                if (settings.SchemaVersion < 4)
                {
                    settings = settings with
                    {
                        FirstLedPercent = LedProfileSettings.DefaultFirstLedPercent,
                        RedlinePercent = LedProfileSettings.DefaultRedlinePercent,
                        AdvancedThresholds = LedMath.BuildRecommendedThresholds(5),
                        CloseToTray = true,
                        MinimizeToTray = true,
                        ReadyAnimation = true
                    };
                }

                // Existing installations keep their Forza UDP endpoint, even
                // when the old file did not contain a schema version.
                settings = settings with
                {
                    SchemaVersion = LedProfileSettings.CurrentSchemaVersion,
                    TelemetryGames = settings.TelemetryGames is { Length: > 0 }
                        ? settings.TelemetryGames
                        : [TelemetryGameSettings.DefaultForza with
                        {
                            BindAddress = settings.BindAddress, Port = settings.Port
                        }]
                };
                try
                {
                    await SaveAsync(settings, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
            }

            return settings is not null && settings.TryValidate(out _) ? settings : LedProfileSettings.Defaults;
        }
        catch (JsonException)
        {
            return LedProfileSettings.Defaults;
        }
        catch (IOException)
        {
            return LedProfileSettings.Defaults;
        }
        catch (UnauthorizedAccessException)
        {
            return LedProfileSettings.Defaults;
        }
    }

    public async Task SaveAsync(LedProfileSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out var error)) throw new ArgumentException(error, nameof(settings));

        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        // Do not use a dot-prefixed temp file on Windows. Some managed-folder
        // policies deny hidden filenames even when the containing directory is
        // writable, which previously made every settings save fail silently.
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream,
                    settings with { SchemaVersion = LedProfileSettings.CurrentSchemaVersion }, JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _settingsPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }
}
