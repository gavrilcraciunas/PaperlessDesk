using PaperlessDesktop.Shared;

namespace PaperlessDesktop.Services;

public class LicenseState
{
    public string FirstRun { get; set; } = "";
    public bool Activated { get; set; } = false;
    public string LicenseKey { get; set; } = "";
}

public class LicenseService
{
    private LicenseState _state;

    public LicenseService()
    {
        _state = Load();
        if (string.IsNullOrEmpty(_state.FirstRun))
        {
            _state.FirstRun = DateTime.Today.ToString("yyyy-MM-dd");
            Save();
        }
    }

    public bool IsActivated => _state.Activated;
    public int TrialDaysLeft => CalcTrialDaysLeft();
    public bool IsPro => _state.Activated || TrialDaysLeft > 0;
    public string LicenseStatusText => IsActivated
        ? "Activated Pro"
        : TrialDaysLeft > 0
            ? $"Trial — {TrialDaysLeft} days remaining"
            : "Free — Trial expired";

    public bool Activate(string userId, string key)
    {
        var k = key.Trim().ToUpperInvariant();
        if (k == AppConstants.MasterKey || k == ExpectedKey(userId))
        {
            _state.Activated = true;
            _state.LicenseKey = k;
            Save();
            return true;
        }
        return false;
    }

    private int CalcTrialDaysLeft()
    {
        if (!DateTime.TryParse(_state.FirstRun, out var start)) start = DateTime.Today;
        return Math.Max(0, AppConstants.TrialDays - (DateTime.Today - start.Date).Days);
    }

    private static string ExpectedKey(string userId)
    {
        var raw = userId.Trim().ToLower() + "|" + AppConstants.LicenseSalt;
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return $"PAPERLESS-{hex[..4]}-{hex[4..8]}";
    }

    private static string LicensePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "license.json");
    }

    private static LicenseState Load()
    {
        try
        {
            var p = LicensePath();
            if (!File.Exists(p)) return new LicenseState { FirstRun = DateTime.Today.ToString("yyyy-MM-dd") };
            return JsonSerializer.Deserialize<LicenseState>(File.ReadAllText(p)) ?? new LicenseState();
        }
        catch { return new LicenseState { FirstRun = DateTime.Today.ToString("yyyy-MM-dd") }; }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(LicensePath(),
            JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
