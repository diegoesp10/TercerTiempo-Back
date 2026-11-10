using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Infrastructure.Videos;

namespace TercerTiempo.Application.Tests;

// Compresión real con el ffmpeg instalado en la máquina (en la imagen del servicio de vídeo siempre lo está):
// TT_RUN_FFMPEG_TESTS=1. Sin la variable no hace nada.
public class FfmpegIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tt-ffmpeg-" + Guid.NewGuid().ToString("N"));

    private static bool Enabled => Environment.GetEnvironmentVariable("TT_RUN_FFMPEG_TESTS") == "1";

    [Fact]
    public async Task Compresses_a_clip_keeps_its_duration_and_publishes_it_in_the_storage()
    {
        if (!Enabled) return;

        var (storage, transcoder) = Build();
        Assert.True(transcoder.IsAvailable);
        var key = await GenerateClipAsync(storage, seconds: 3, height: 720);

        var probe = await transcoder.ProbeAsync(key, default);
        Assert.Equal("h264", probe.VideoCodec);
        Assert.Equal(720, probe.Height);
        Assert.InRange(probe.DurationSeconds, 2.5, 3.5);

        var reported = new List<int>();
        var result = await transcoder.TranscodeAsync(key, new TranscodeProfile(28, "ultrafast", 360, 64), (percent, _) =>
        {
            reported.Add(percent);
            return Task.CompletedTask;
        }, default);

        Assert.Equal(360, result.Height);
        Assert.InRange(result.DurationSeconds, 2.5, 3.5);
        var published = Path.Combine(_root, result.StorageKey);
        Assert.True(File.Exists(published));
        Assert.Equal(new FileInfo(published).Length, result.SizeBytes);
        Assert.EndsWith(".mp4", result.StorageKey);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "processing")));
        Assert.All(reported, p => Assert.InRange(p, 0, 99));
    }

    [Fact]
    public async Task Cancelled_compression_leaves_nothing_behind()
    {
        if (!Enabled) return;

        var (storage, transcoder) = Build();
        var key = await GenerateClipAsync(storage, seconds: 20, height: 720);

        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transcoder.TranscodeAsync(key, new TranscodeProfile(28, "veryslow", null, 64), (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token));

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "processing")));
        Assert.Equal(0, await transcoder.CleanUpAsync(default));
    }

    private (FileSystemVideoStorage Storage, FfmpegTranscoder Transcoder) Build()
    {
        Directory.CreateDirectory(_root);
        var storage = new FileSystemVideoStorage(Options.Create(new VideoOptions { Directory = _root }));
        var transcoder = new FfmpegTranscoder(storage, Options.Create(new VideoProcessingOptions()), NullLogger<FfmpegTranscoder>.Instance);
        return (storage, transcoder);
    }

    // Un clip sintético (barras de prueba y un tono) codificado deprisa: lo que subiría una cámara, en pequeño.
    private async Task<string> GenerateClipAsync(FileSystemVideoStorage storage, int seconds, int height)
    {
        var key = $"clip-{Guid.NewGuid():N}.mp4";
        var info = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[]
                 {
                     "-y", "-nostdin", "-loglevel", "error",
                     "-f", "lavfi", "-i", $"testsrc=duration={seconds}:size={height * 16 / 9}x{height}:rate=25",
                     "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
                     "-c:v", "libx264", "-preset", "ultrafast", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest",
                     storage.PathOf(key),
                 })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, errors);
        return key;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
