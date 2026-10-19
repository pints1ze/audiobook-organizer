using System.ComponentModel;
using System.Diagnostics;
using AudiobookOrganizer.Api.Configuration;
using Microsoft.Extensions.Options;

namespace AudiobookOrganizer.Api.Services;

public class FfmpegException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Runs ffmpeg or ffprobe with an argument list (never a shell string) and returns stdout.
/// Throws <see cref="FfmpegException"/> when the executable is missing, times out or exits non-zero.
/// </summary>
public class FfmpegRunner(IOptions<ToolSettings> options)
{
    private readonly ToolSettings _settings = options.Value;

    public string Ffmpeg(IEnumerable<string> arguments) => Run(_settings.FfmpegPath, arguments);

    public string Ffprobe(IEnumerable<string> arguments) => Run(_settings.FfprobePath, arguments);

    private string Run(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // closed below so ffmpeg never waits on a prompt
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new FfmpegException(
                $"'{executable}' was not found or couldn't start. Install ffmpeg or set the Tools paths in configuration.", ex);
        }

        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromSeconds(_settings.TimeoutSeconds)))
        {
            process.Kill(entireProcessTree: true);
            throw new FfmpegException($"'{executable}' timed out after {_settings.TimeoutSeconds} s.");
        }

        process.WaitForExit(); // flush the redirected streams
        if (process.ExitCode != 0)
            throw new FfmpegException($"'{executable}' exited with code {process.ExitCode}: {stderr.Result.Trim()}");

        return stdout.Result;
    }
}
