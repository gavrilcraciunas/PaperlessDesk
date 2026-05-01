namespace PaperlessDesktop.Shared;

public class AppSettings
{
    public string LastDirectory { get; set; } = "";
    public List<string> RecentFiles { get; set; } = new();
    public bool DarkMode { get; set; } = false;
    public string DefaultOutputDir { get; set; } = "";
    public string DefaultQuality { get; set; } = "ebook";
    public string DefaultOcrLang { get; set; } = "ron+eng";
    public bool ShowPageCount { get; set; } = true;

    private static string SettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            var path = SettingsPath();
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath(),
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void AddRecent(string path)
    {
        RecentFiles.Remove(path);
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > AppConstants.MaxRecentFiles)
            RecentFiles = RecentFiles.Take(AppConstants.MaxRecentFiles).ToList();
    }
}
