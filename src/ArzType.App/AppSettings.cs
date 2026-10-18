using System.Text.Json;
using Microsoft.Win32;

namespace ArzType.App;

/// <summary>Persisted options. Never contains typed text — learned words are stored as fingerprints only.</summary>
internal sealed class AppSettings
{
    public bool Enabled { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool FixOnEnter { get; set; } = true;
    public bool DoubleShiftFix { get; set; } = true;
    public bool PauseKeyFix { get; set; } = true;
    public bool LearnFromFixes { get; set; } = true;
    public bool AllApps { get; set; }
    public int Sensitivity { get; set; }               // -2 … +2
    public bool FirstRunDone { get; set; }
    public long TotalFixes { get; set; }
    public long TotalKeysSaved { get; set; }
    public List<string> ExcludedApps { get; set; } = [.. SafetyGate.DefaultExclusions];
    public List<string> NeverFingerprints { get; set; } = [];
    public Dictionary<string, string> AlwaysFingerprints { get; set; } = [];

    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArzType");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Folder => Dir;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* settings must never break typing */ }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void ApplyStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue("ArzType", $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue("ArzType", throwOnMissingValue: false);
        }
        catch { }
    }
}
