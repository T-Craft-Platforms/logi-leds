using System.IO;
using System.Text.Json;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed class InputMapperProfileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path;

    public InputMapperProfileStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogiWheelForge", "input-mapper-profiles.json");
    }

    public async Task<IReadOnlyList<InputMapperProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = File.OpenRead(_path);
        var profiles = await JsonSerializer.DeserializeAsync<InputMapperProfile[]>(stream, Options, cancellationToken) ?? [];
        Validate(profiles);
        return profiles;
    }

    public async Task SaveAsync(IReadOnlyList<InputMapperProfile> profiles, CancellationToken cancellationToken = default)
    {
        Validate(profiles);
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"input-mapper.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, profiles, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Validate(IReadOnlyList<InputMapperProfile> profiles)
    {
        if (profiles.Select(profile => profile.Id).Distinct().Count() != profiles.Count)
            throw new ArgumentException("Profile IDs must be unique.");
        foreach (var profile in profiles)
            if (!profile.TryValidate(out var error)) throw new ArgumentException($"{profile.Name}: {error}");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in profiles.Where(profile => profile.Enabled).SelectMany(profile => profile.ProcessPaths))
            if (!paths.Add(Path.GetFullPath(path)))
                throw new ArgumentException($"An executable can belong to only one enabled profile: {path}");
    }
}
