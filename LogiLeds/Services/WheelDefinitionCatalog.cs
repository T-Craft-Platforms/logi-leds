using System.Text.Json;
using System.IO;
using LogiLeds.Models;

namespace LogiLeds.Services;

public sealed class WheelDefinitionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private readonly string _bundledPath;
    private readonly string _userPath;

    public WheelDefinitionCatalog(string? bundledPath = null, string? userPath = null)
    {
        _bundledPath = bundledPath ?? Path.Combine(AppContext.BaseDirectory, "WheelDefinitions");
        _userPath = userPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogiLeds", "Wheels");
    }

    public WheelCatalogResult Load()
    {
        var definitions = new List<WheelDefinition>();
        var diagnostics = new List<string>();
        LoadDirectory(_bundledPath, false, definitions, diagnostics);
        Directory.CreateDirectory(_userPath);
        LoadDirectory(_userPath, true, definitions, diagnostics);
        return new WheelCatalogResult(definitions, diagnostics);
    }

    private static void LoadDirectory(string path, bool userDefinition, List<WheelDefinition> definitions, List<string> diagnostics)
    {
        if (!Directory.Exists(path))
        {
            if (!userDefinition) diagnostics.Add($"Bundled wheel directory was not found: {path}");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var definition = JsonSerializer.Deserialize<WheelDefinition>(File.ReadAllText(file), JsonOptions);
                if (definition is null) { diagnostics.Add($"{Path.GetFileName(file)}: definition is empty."); continue; }
                if (!definition.TryValidate(out var error))
                { diagnostics.Add($"{Path.GetFileName(file)}: {error}"); continue; }
                if (definitions.Any(x => string.Equals(x.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
                { diagnostics.Add($"{Path.GetFileName(file)}: wheel id '{definition.Id}' is already defined."); continue; }
                definitions.Add(definition);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { diagnostics.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
        }
    }
}
