using System.IO;
using System.Text.Json;

namespace LogiWheelForge.Services;

public sealed record InputMapperModuleSettings(bool AutoStart = false);

public sealed class InputMapperSettingsStore
{
    private readonly string _path;
    public InputMapperSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogiWheelForge", "input-mapper-settings.json");
    }

    public async Task<InputMapperModuleSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new();
        try
        {
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<InputMapperModuleSettings>(stream, cancellationToken: cancellationToken)
                   ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { return new(); }
    }

    public async Task SaveAsync(InputMapperModuleSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"input-mapper-settings.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings), cancellationToken);
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
