using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Tool '{toolName}' check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Runs an external tool and captures stdout/stderr.
    /// Lines are reported to <paramref name="progress"/> in real-time.
    /// NOTE: We use async ReadLine() loops — never mix BeginOutputReadLine()
    ///       with ReadToEndAsync() on the same stream.
    /// </summary>
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
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Could not start {toolName}");

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            // Read stdout asynchronously, forwarding each line to progress
            async Task ReadStreamAsync(System.IO.StreamReader reader, StringBuilder sb, bool report)
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    sb.AppendLine(line);
                    if (report) progress?.Report(line);
                }
            }

            var stdoutTask = ReadStreamAsync(proc.StandardOutput, stdoutBuilder, report: true);
            var stderrTask = ReadStreamAsync(proc.StandardError,  stderrBuilder, report: false);

            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw;
            }

            // Drain any remaining output after process exits
            await Task.WhenAll(stdoutTask, stderrTask);

            var output = stdoutBuilder.ToString();
            var error  = stderrBuilder.ToString();

            Logger.Info($"{toolName} exited with code {proc.ExitCode}");
            if (!string.IsNullOrWhiteSpace(error))
                Logger.Warn($"{toolName} stderr: {error}");

            return (proc.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            Logger.Warn($"{toolName} was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to run {toolName}", ex);
            throw;
        }
    }
}
