using System.Text.Json;

namespace MapEditor;

/// <summary>What the editor remembers between runs: %LOCALAPPDATA%\BarMapEditor\settings.json.</summary>
sealed class EditorSettings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarMapEditor", "settings.json");

    /// <summary>BAR's maps folder, for "copy into BAR's maps folder" on Save As.</summary>
    public string? MapsFolder { get; set; }

    /// <summary>Whether Save As copies the map there.</summary>
    public bool CopyToMaps { get; set; }

    /// <summary>Whether each map opened adds its assets to the asset library.</summary>
    public bool CollectAssets { get; set; } = true;

    /// <summary>Whether the bottom panel (asset browser and log) shows, and how tall it is.</summary>
    public bool AssetBrowser { get; set; } = true;

    public int AssetBrowserHeight { get; set; } = 220;

    /// <summary>Whether the Beautify panel is open (a tab beside the tools).</summary>
    public bool BeautifyPanel { get; set; }

    /// <summary>Where the last map was opened from.</summary>
    public string? LastOpenFolder { get; set; }

    /// <summary>World Machine's program (for its Professional edition's automation) and the project it builds.</summary>
    public string? WorldMachineProgram { get; set; }

    public string? WorldMachineProject { get; set; }

    /// <summary>Each map's World Machine exchange folder, by its archive's name.</summary>
    public Dictionary<string, string> WorldMachineFolders { get; set; } = [];

    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath)) ?? new EditorSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file only loses preferences.
        }
        return new EditorSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences that can't be saved are simply forgotten.
        }
    }
}
