using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PaperlessDesktop.Services;

public class ToolManager
{
    public static bool IsToolAvailable(string toolName)
    {
        try
        {
            var psi = new ProcessStartInfo(toolName, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var proc = Process.Start(psi))
            {
                if (proc == null) return false;
                proc.WaitForExit(3000); // 3-second timeout
                return proc.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Tool '{toolName}' check failed: {ex.Message}");
            return false;
        }
    }

    public static async Task<(int exitCode, string output, string error)> RunToolAsync(
        string toolName, string arguments,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        Logger.Info($"Running: {toolName} {arguments}");

        try
        {
            var psi = new ProcessStartInfo(toolName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {toolName}"))
            {
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                // Hook output for progress reporting
                var outputLines = new List<string>();
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                        progress?.Report(e.Data);
                    }
                };
                proc.BeginOutputReadLine();

                // Wait with timeout/cancellation
                var exitTask = proc.WaitForExitAsync(ct);
                try
                {
                    await exitTask;
                }
                catch (OperationCanceledException)
                {
                    proc.Kill();
                    throw;
                }

                var output = await stdoutTask;
                var error = await stderrTask;

                Logger.Info($"{toolName} exited with code {proc.ExitCode}");
                if (!string.IsNullOrEmpty(error))
                    Logger.Warn($"{toolName} stderr: {error}");

                return (proc.ExitCode, output, error);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to run {toolName}", ex);
            throw;
        }
    }
}
