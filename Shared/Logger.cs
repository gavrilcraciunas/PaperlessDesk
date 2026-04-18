using System;
using System.IO;
using System.Text;

namespace PaperlessDesktop.Shared;

public class Logger
{
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PaperlessDesktop", "logs", $"app_{DateTime.Now:yyyy-MM-dd}.log");

    static Logger()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message) => Log("ERROR", message);
    public static void Error(string message, Exception ex) => Log("ERROR", $"{message}\n{ex}");

    private static void Log(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            lock (typeof(Logger))
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* Silent fail */ }
    }
}
