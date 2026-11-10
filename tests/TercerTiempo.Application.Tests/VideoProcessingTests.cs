using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TercerTiempo.Application.Abstractions.Videos;
using TercerTiempo.Application.Videos.Processing;
using TercerTiempo.Domain.Common;
using TercerTiempo.Domain.Matches;
using TercerTiempo.Domain.Teams;
using TercerTiempo.Domain.Videos;
using TercerTiempo.Infrastructure.Videos;

namespace TercerTiempo.Application.Tests;

public class TranscodePlanTests
{
    private static readonly VideoProcessingOptions Options = new();

    [Fact]
    public void Phone_recording_at_1080p_is_compressed_without_changing_its_resolution()
    {
        var decision = TranscodePlan.Decide(new VideoProbe(5760, 1920, 1080, 8_200_000, "h264", "aac", "mov,mp4,m4a,3gp,3g2,mj2"), Options);

        var transcode = Assert.IsType<TranscodeDecision.Transcode>(decision);
        Assert.Null(transcode.Profile.MaxHeight);
        Assert.Equal(22, transcode.Profile.Crf);
        Assert.Equal("medium", transcode.Profile.Preset);
    }

    [Fact]
    public void Already_light_h264_mp4_is_kept_as_uploaded()
    {
        var decision = TranscodePlan.Decide(new VideoProbe(5760, 1920, 1080, 3_500_000, "h264", "aac", "mov,mp4,m4a,3gp,3g2,mj2"), Options);
        Assert.IsType<TranscodeDecision.Keep>(decision);
    }

    [Fact]
    public void Anything_above_the_maximum_height_is_scaled_down_to_it()
    {
        var decision = TranscodePlan.Decide(new VideoProbe(5760, 3840, 2160, 3_000_000, "h264", "aac", "mov,mp4,m4a,3gp,3g2,mj2"), Options);
        Assert.Equal(1080, Assert.IsType<TranscodeDecision.Transcode>(decision).Profile.MaxHeight);
    }

    [Fact]
    public void Other_codecs_or_containers_are_converted_even_when_light()
    {
        Assert.IsType<TranscodeDecision.Transcode>(TranscodePlan.Decide(new VideoProbe(60, 1280, 720, 2_000_000, "vp9", "opus", "matroska,webm"), Options));
        Assert.IsType<TranscodeDecision.Transcode>(TranscodePlan.Decide(new VideoProbe(60, 1280, 720, 2_000_000, "hevc", "aac", "mov,mp4,m4a,3gp,3g2,mj2"), Options));
    }
}

public class FfmpegTranscoderTests
{
    [Fact]
    public void Arguments_keep_the_resolution_unless_a_maximum_height_is_given()
    {
        var keep = FfmpegTranscoder.Arguments("in.mp4", "out.mp4", new TranscodeProfile(22, "medium", null, 128)).ToList();
        Assert.DoesNotContain("-vf", keep);
        Assert.Equal("out.mp4", keep[^1]);
        Assert.Contains("+faststart", keep);
        Assert.Equal("22", keep[keep.IndexOf("-crf") + 1]);
        Assert.Equal("medium", keep[keep.IndexOf("-preset") + 1]);
        Assert.Equal("128k", keep[keep.IndexOf("-b:a") + 1]);

        var scaled = FfmpegTranscoder.Arguments("in.mp4", "out.mp4", new TranscodeProfile(22, "medium", 1080, 128)).ToList();
        Assert.Equal("scale=-2:1080", scaled[scaled.IndexOf("-vf") + 1]);
    }

    [Theory]
    [InlineData("out_time_us=120000000", 120d)]
    [InlineData("out_time_ms=1500000", 1.5d)]
    [InlineData("frame=12", null)]
    [InlineData("out_time_us=N/A", null)]
    [InlineData("progress=continue", null)]
    public void Progress_lines_give_the_seconds_already_encoded(string line, double? expected) =>
        Assert.Equal(expected, FfmpegTranscoder.ProgressSeconds(line));

    [Fact]
    public void Probe_output_is_read_from_ffprobe_json()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080, "duration": "5781.700000" },
                { "codec_type": "audio", "codec_name": "aac" },
                { "codec_type": "data", "codec_name": "none" }
              ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "5781.733000", "size": "5936714785", "bit_rate": "8213412" }
            }
            """;

        var probe = FfmpegTranscoder.ParseProbe(json);

        Assert.Equal("h264", probe.VideoCodec);
        Assert.Equal("aac", probe.AudioCodec);
        Assert.Equal(1920, probe.Width);
        Assert.Equal(1080, probe.Height);
        Assert.Equal(5781.733, probe.DurationSeconds, 3);
        Assert.Equal(8_213_412, probe.BitrateBps);
        Assert.Contains("mp4", probe.Container);
    }

    [Fact]
    public void Bitrate_is_derived_from_size_and_duration_when_ffprobe_does_not_give_it()
    {
        const string json = """
            { "streams": [ { "codec_type": "video", "codec_name": "vp9", "width": 1280, "height": 720 } ],
              "format": { "format_name": "matroska,webm", "duration": "100", "size": "12500000" } }
            """;
        Assert.Equal(1_000_000, FfmpegTranscoder.ParseProbe(json).BitrateBps);
        Assert.Null(FfmpegTranscoder.ParseProbe(json).AudioCodec);
    }

    [Fact]
    public void A_file_without_video_stream_is_rejected() =>
        Assert.Throws<VideoTranscodeException>(() => FfmpegTranscoder.ParseProbe("""{ "streams": [ { "codec_type": "audio", "codec_name": "aac" } ], "format": { "duration": "10" } }"""));
}

public class VideoProcessorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Compressed_file_replaces_the_original_which_is_deleted_afterwards()
    {
        var world = new World();
        var video = world.Upload();
        world.Transcoder.Probe = new VideoProbe(5760, 1920, 1080, 8_000_000, "h264", "aac", "mov,mp4");
        world.Transcoder.Result = new TranscodeResult("new.mp4", 2_000_000_000, 5760.4, 1080);

        var claimed = await world.Processor.ClaimNextAsync(default);
        Assert.Same(video, claimed);
        Assert.Equal(VideoStatus.Processing, video.Status);

        await world.Processor.ProcessAsync(video, default);

        Assert.Equal(VideoStatus.Ready, video.Status);
        Assert.Equal("new.mp4", video.StorageKey);
        Assert.Equal(2_000_000_000, video.SizeBytes);
        Assert.Equal(6_000_000_000, video.OriginalSizeBytes);
        Assert.Equal("video/mp4", video.ContentType);
        Assert.Equal("partido.mp4", video.FileName);
        Assert.Equal(5760, video.DurationSeconds);
        Assert.Equal(1080, video.Height);
        Assert.Equal(["original.webm"], world.Storage.Deleted);
        Assert.Null(video.ProcessingError);
        Assert.True(world.Jobs.Finished!.Value.Succeeded);
        Assert.Contains("CD Vallobín - Real Titánico (partido completo)", world.Jobs.Finished!.Value.Summary);
        Assert.Null(await world.Processor.ClaimNextAsync(default));
    }

    [Fact]
    public async Task Progress_is_saved_at_most_every_few_seconds()
    {
        var world = new World();
        var video = world.Upload();
        world.Transcoder.Probe = new VideoProbe(100, 1920, 1080, 8_000_000, "h264", "aac", "mp4");
        world.Transcoder.Result = new TranscodeResult("new.mp4", 1_000_000, 100, 1080);
        world.Transcoder.ProgressSteps = [10, 20, 30];
        world.Transcoder.BeforeProgress = () => world.Clock.Now += TimeSpan.FromSeconds(3);

        await world.Processor.ClaimNextAsync(default);
        var savesBefore = world.UnitOfWork.Saves;
        await world.Processor.ProcessAsync(video, default);

        // 10 % (3 s, no), 20 % (6 s, sí), 30 % (9 s, no) y el final.
        Assert.Equal(savesBefore + 2, world.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Light_upload_is_kept_and_nothing_is_deleted()
    {
        var world = new World();
        var video = world.Upload();
        world.Transcoder.Probe = new VideoProbe(5400, 1920, 1080, 3_000_000, "h264", "aac", "mov,mp4");

        await world.Processor.ClaimNextAsync(default);
        await world.Processor.ProcessAsync(video, default);

        Assert.Equal(VideoStatus.Ready, video.Status);
        Assert.Equal("original.webm", video.StorageKey);
        Assert.Null(video.OriginalSizeBytes);
        Assert.Equal(5400, video.DurationSeconds);
        Assert.Empty(world.Storage.Deleted);
        Assert.False(world.Transcoder.TranscodeCalled);
    }

    [Fact]
    public async Task Failed_compression_keeps_serving_the_original_and_is_not_retried_forever()
    {
        var world = new World(maxAttempts: 1);
        var video = world.Upload();
        world.Transcoder.Probe = new VideoProbe(5400, 1920, 1080, 8_000_000, "h264", "aac", "mp4");
        world.Transcoder.Failure = new VideoTranscodeException("ffmpeg devolvió 1: sin espacio");

        await world.Processor.ClaimNextAsync(default);
        await world.Processor.ProcessAsync(video, default);

        Assert.Equal(VideoStatus.Failed, video.Status);
        Assert.Equal("original.webm", video.StorageKey);
        Assert.Equal("ffmpeg devolvió 1: sin espacio", video.ProcessingError);
        Assert.Empty(world.Storage.Deleted);
        Assert.False(world.Jobs.Finished!.Value.Succeeded);
        Assert.Equal("ffmpeg devolvió 1: sin espacio", world.Jobs.Finished!.Value.Error);
        Assert.Null(await world.Processor.ClaimNextAsync(default));
    }

    [Fact]
    public async Task Shutdown_mid_compression_puts_the_video_back_in_the_queue()
    {
        var world = new World();
        var video = world.Upload();
        world.Transcoder.Probe = new VideoProbe(5400, 1920, 1080, 8_000_000, "h264", "aac", "mp4");
        using var stopping = new CancellationTokenSource();
        world.Transcoder.OnTranscode = () => stopping.Cancel();

        await world.Processor.ClaimNextAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Processor.ProcessAsync(video, stopping.Token));

        Assert.Equal(VideoStatus.Uploaded, video.Status);
        Assert.Equal(0, video.ProcessingAttempts);
        Assert.Empty(world.Storage.Deleted);
    }

    [Fact]
    public async Task Deleting_the_video_while_compressing_discards_the_work_without_touching_it()
    {
        var world = new World();
        var video = world.Upload();
        world.Transcoder.Probe = new VideoProbe(5400, 1920, 1080, 8_000_000, "h264", "aac", "mp4");
        using var deleted = new CancellationTokenSource();
        world.Transcoder.OnTranscode = () =>
        {
            world.Videos.DeletedIds.Add(video.Id);
            deleted.Cancel();
        };

        await world.Processor.ClaimNextAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Processor.ProcessAsync(video, deleted.Token));

        Assert.Equal(VideoStatus.Processing, video.Status);
        Assert.Empty(world.Storage.Deleted);
        Assert.False(world.Jobs.Finished!.Value.Succeeded);
    }

    [Fact]
    public async Task A_video_that_ends_ready_enters_the_analysis_queue_and_a_failed_one_does_not()
    {
        var world = new World(analysis: true);
        var video = world.Upload();
        world.Transcoder.Probe = new VideoProbe(5760, 1920, 1080, 8_000_000, "h264", "aac", "mov,mp4");
        world.Transcoder.Result = new TranscodeResult("new.mp4", 2_000_000_000, 5760.4, 1080);

        await world.Processor.ClaimNextAsync(default);
        await world.Processor.ProcessAsync(video, default);

        var queued = Assert.Single(world.Analyses.Items);
        Assert.Equal((video.Id, VideoAnalysisStatus.Pending), (queued.VideoId, queued.Status));
        Assert.Equal(1, world.AnalysisSignal.Wakes);

        var failing = new World(maxAttempts: 1, analysis: true);
        var broken = failing.Upload();
        failing.Transcoder.Probe = new VideoProbe(5400, 1920, 1080, 8_000_000, "h264", "aac", "mp4");
        failing.Transcoder.Failure = new VideoTranscodeException("ffmpeg devolvió 1");

        await failing.Processor.ClaimNextAsync(default);
        await failing.Processor.ProcessAsync(broken, default);

        Assert.Empty(failing.Analyses.Items);
    }

    [Fact]
    public async Task Interrupted_videos_are_requeued_when_the_service_starts()
    {
        var world = new World();
        var video = world.Upload();
        video.StartProcessing(Start);

        Assert.Equal(1, await world.Processor.RequeueInterruptedAsync(default));
        Assert.Equal(VideoStatus.Uploaded, video.Status);
    }

    private sealed class World
    {
        public readonly ManualClock Clock = new() { Now = Start };
        public readonly FakeVideos Videos = new();
        public readonly FakeStorage Storage = new();
        public readonly FakeTranscoder Transcoder = new();
        public readonly FakeJobs Jobs = new();
        public readonly FakeUnitOfWork UnitOfWork = new();
        public readonly FakeAnalysisStore Analyses = new();
        public readonly FakeAnalysisSignal AnalysisSignal = new();
        public readonly VideoProcessor Processor;
        private readonly Match _match;

        // Con el análisis desactivado (como en Docker) la compresión no deja nada en la cola de análisis.
        public World(int maxAttempts = 2, bool analysis = false)
        {
            var home = Team.Create("CD Vallobín", "VAL", null);
            var away = Team.Create("Real Titánico", "TIT", null);
            _match = Match.Schedule(Guid.NewGuid(), 1, home.Id, away.Id, Start.AddDays(-3), null, null);
            var matches = Stub<IMatchStore>((name, _) => name == nameof(IMatchStore.GetByIdAsync) ? Task.FromResult<Match?>(_match) : throw new NotSupportedException(name));
            var teams = Stub<ITeamStore>((name, _) => name == nameof(ITeamStore.GetByIdsAsync) ? Task.FromResult<IReadOnlyList<Team>>([home, away]) : throw new NotSupportedException(name));
            var analysisQueue = new Videos.Analysis.VideoAnalysisQueue(
                Analyses, Microsoft.Extensions.Options.Options.Create(new VideoAnalysisOptions { Enabled = analysis }), AnalysisSignal, UnitOfWork, Clock);
            Processor = new VideoProcessor(Videos, matches, teams, Storage, Transcoder, Jobs, analysisQueue,
                Microsoft.Extensions.Options.Options.Create(new VideoProcessingOptions { MaxAttempts = maxAttempts }), UnitOfWork, Clock, NullLogger<VideoProcessor>.Instance);
        }

        public MatchVideo Upload()
        {
            var video = MatchVideo.Upload(Guid.NewGuid(), _match.HomeTeamId, _match.Id, _match.SeasonId, Guid.NewGuid(), VideoPart.Full,
                "Partido", "partido.webm", "video/webm", 6_000_000_000, "original.webm", Start.AddMinutes(-5));
            Videos.Items.Add(video);
            return video;
        }

        private static T Stub<T>(Func<string, object?[]?, object?> call) where T : class
        {
            var stub = System.Reflection.DispatchProxy.Create<T, ClubAsStaffTests.StoreProxy>();
            ((ClubAsStaffTests.StoreProxy)(object)stub).Call = call;
            return stub;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeVideos : IMatchVideoStore
    {
        public readonly List<MatchVideo> Items = [];
        public readonly HashSet<Guid> DeletedIds = [];

        public Task<MatchVideo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(v => v.Id == id));
        public Task<IReadOnlyList<MatchVideo>> GetByMatchAsync(Guid matchId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MatchVideo>>(Items.Where(v => v.MatchId == matchId).ToList());
        public Task<IReadOnlyList<MatchVideo>> GetBySquadsAsync(IEnumerable<Guid> squadIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(MatchVideo video) => Items.Add(video);
        public Task<MatchVideo?> GetNextPendingAsync(int maxAttempts, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Where(v => v.AwaitsProcessing(maxAttempts)).OrderBy(v => v.UploadedAt).FirstOrDefault());
        public Task<IReadOnlyList<MatchVideo>> GetProcessingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MatchVideo>>(Items.Where(v => v.Status == VideoStatus.Processing).ToList());
        public Task<bool> IsDeletedAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(DeletedIds.Contains(id));
    }

    private sealed class FakeStorage : IVideoStorage
    {
        public readonly List<string> Deleted = [];

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            Deleted.Add(storageKey);
            return Task.CompletedTask;
        }

        public Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> BeginUploadAsync(string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<long> AppendUploadAsync(string uploadKey, Stream chunk, long offset, long length, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> CommitUploadAsync(string uploadKey, string fileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DiscardUploadAsync(string uploadKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<long?> AvailableBytesAsync(CancellationToken cancellationToken) => Task.FromResult<long?>(null);
    }

    private sealed class FakeTranscoder : IVideoTranscoder
    {
        public VideoProbe Probe = null!;
        public TranscodeResult? Result;
        public Exception? Failure;
        public int[] ProgressSteps = [];
        public Action? BeforeProgress;
        public Action? OnTranscode;
        public bool TranscodeCalled;

        public bool IsAvailable => true;

        public Task<VideoProbe> ProbeAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult(Probe);

        public async Task<TranscodeResult> TranscodeAsync(string storageKey, TranscodeProfile profile, Func<int, CancellationToken, Task> progress, CancellationToken cancellationToken)
        {
            TranscodeCalled = true;
            OnTranscode?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            foreach (var step in ProgressSteps)
            {
                BeforeProgress?.Invoke();
                await progress(step, cancellationToken);
            }
            return Result ?? throw new InvalidOperationException("Sin resultado");
        }

        public Task<int> CleanUpAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class FakeJobs : IVideoJobLog
    {
        public string? StartedLabel;
        public (bool Succeeded, string Summary, string? Error)? Finished;

        public Task<Guid?> StartedAsync(string job, string label, CancellationToken cancellationToken)
        {
            StartedLabel = label;
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }

        public Task FinishedAsync(Guid? runId, bool succeeded, string summary, string? error, CancellationToken cancellationToken)
        {
            Finished = (succeeded, summary, error);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }
}
