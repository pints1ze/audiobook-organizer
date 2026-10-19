using AudiobookOrganizer.Api.Configuration;
using AudiobookOrganizer.Api.Services;
using Microsoft.Extensions.Options;

namespace AudiobookOrganizer.Api.Tests.Services;

public class FfmpegRunnerTests
{
    internal static FfmpegRunner Runner(ToolSettings? settings = null) =>
        new(Options.Create(settings ?? new ToolSettings()));

    [Fact]
    public void A_missing_executable_gives_a_clear_exception()
    {
        var runner = Runner(new ToolSettings { FfprobePath = "no-such-ffprobe-executable" });

        var ex = Assert.Throws<FfmpegException>(() => runner.Ffprobe(["-version"]));

        Assert.Contains("no-such-ffprobe-executable", ex.Message);
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void A_non_zero_exit_surfaces_stderr()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.m4b");

        var ex = Assert.Throws<FfmpegException>(() => Runner().Ffprobe(["-v", "error", missing]));

        Assert.Contains("exited with code", ex.Message);
        Assert.Contains("No such file", ex.Message);
    }

    [Fact]
    public void A_successful_run_returns_stdout()
    {
        var output = Runner().Ffprobe(["-version"]);

        Assert.StartsWith("ffprobe version", output);
    }
}
