using System.Text.Json;

namespace ToolBuilder;

public sealed class UserPreferences
{
    public Dictionary<string, List<string>> SourcesByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> RecentFiles { get; set; } = [];
    private static string Storage => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBuilder", "preferences.json");
    public static UserPreferences Read()
    {
        try
        {
            var result = File.Exists(Storage) ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(Storage)) ?? new() : new();
            result.SourcesByFile = new(result.SourcesByFile, StringComparer.OrdinalIgnoreCase);
            return result;
        }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Storage)!);
        var temp = Storage + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, Storage, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
