using System.IO;
using System.Text.Json;
using AutoClickerPro.Models;

namespace AutoClickerPro.Services;

/// <summary>
/// Handles saving/loading MacroProfile objects as JSON files under %AppData%/AutoClickerPro/Profiles.
/// Isolated behind an interface-free simple class so the ViewModel doesn't deal with file I/O directly.
/// </summary>
public sealed class ProfileService
{
    private readonly string _profilesDir;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProfileService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _profilesDir = Path.Combine(appData, "AutoClickerPro", "Profiles");
        Directory.CreateDirectory(_profilesDir);
    }

    public string ProfilesDirectory => _profilesDir;

    public IEnumerable<string> ListProfileNames() =>
        Directory.EnumerateFiles(_profilesDir, "*.json")
                 .Select(Path.GetFileNameWithoutExtension)
                 .Where(n => n != null)!
                 .Select(n => n!)
                 .OrderBy(n => n);

    public void Save(MacroProfile profile)
    {
        string path = PathFor(profile.Name);
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    public MacroProfile? Load(string name)
    {
        string path = PathFor(name);
        if (!File.Exists(path)) return null;

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MacroProfile>(json);
    }

    public void Delete(string name)
    {
        string path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    private string PathFor(string name)
    {
        // Sanitize the profile name so it's always a safe file name.
        string safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "Profile";
        return Path.Combine(_profilesDir, safe + ".json");
    }
}
