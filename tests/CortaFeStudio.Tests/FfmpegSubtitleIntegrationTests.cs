using System.Diagnostics;
using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;

namespace CortaFeStudio.Tests;

public sealed class FfmpegSubtitleIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cortafe-ffmpeg", Guid.NewGuid().ToString("N"));
    private static string Ffmpeg => File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CortaFeStudio.Api", "tools", "ffmpeg.exe"))
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "CortaFeStudio.Api", "tools", "ffmpeg.exe")) : "ffmpeg";

    [Fact]
    public async Task Ffmpeg_AceitaRenderComLegendaEditada()
    {
        if (!FfmpegAvailable()) return;
        Directory.CreateDirectory(_directory);
        var clip = new ClipCandidate
        {
            Start = 0, End = 2, OutputPreset = "vertical",
            SubtitleTrack = new SubtitleTrack { Blocks = [new SubtitleBlock { Start = .1, End = 1.5, Text = "Você está aqui" }] }
        };
        var ass = MediaPipeline.BuildAss([], clip);
        await File.WriteAllTextAsync(Path.Combine(_directory, "captions.ass"), ass);

        var result = await RunAsync(["-v", "error", "-f", "lavfi", "-i", "color=c=black:s=320x568:d=0.25", "-vf", "scale=320:568,subtitles=captions.ass", "-f", "null", "-"]);

        Assert.Equal(0, result);
        Assert.Contains("Você está aqui", ass);
    }

    [Fact]
    public async Task Ffmpeg_AceitaRenderSemFiltroDeLegenda()
    {
        if (!FfmpegAvailable()) return;
        Directory.CreateDirectory(_directory);
        var filter = MediaPipeline.ComposeVideoFilter("eq=contrast=1", "scale=320:568", null);
        var result = await RunAsync(["-v", "error", "-f", "lavfi", "-i", "color=c=black:s=320x568:d=0.25", "-vf", filter, "-f", "null", "-"]);
        Assert.Equal(0, result);
        Assert.DoesNotContain("subtitles", filter);
    }

    [Fact]
    public async Task Ffmpeg_AceitaIdentidadeOldSchoolCompleta()
    {
        if (!FfmpegAvailable()) return;
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "marca.txt"), "AJ | AMADO JESUS");
        var clip = new ClipCandidate { WatermarkText = "AJ | AMADO JESUS" };
        var framing = $"scale=320:568,{RenderFilterFactory.SignatureMotion(320, 568)}";
        var branding = RenderFilterFactory.Branding(clip, "marca.txt", ":font='Arial'");
        var filter = MediaPipeline.ComposeVideoFilter("null", framing, null, branding, RenderFilterFactory.CreativeLook(.5));

        var result = await RunAsync(["-v", "error", "-f", "lavfi", "-i", "color=c=gray:s=320x568:d=0.5", "-vf", filter, "-f", "null", "-"]);

        Assert.Equal(0, result);
    }

    private bool FfmpegAvailable()
    {
        try { using var process = Process.Start(new ProcessStartInfo(Ffmpeg, "-version") { UseShellExecute = false, CreateNoWindow = true }); return process is not null && process.WaitForExit(3000) && process.ExitCode == 0; }
        catch { return false; }
    }

    private async Task<int> RunAsync(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Ffmpeg) { WorkingDirectory = _directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var diagnostic = await error;
        Assert.True(process.ExitCode == 0, diagnostic);
        return process.ExitCode;
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
