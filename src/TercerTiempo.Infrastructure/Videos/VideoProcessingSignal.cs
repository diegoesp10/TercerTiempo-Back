using TercerTiempo.Application.Abstractions.Videos;

namespace TercerTiempo.Infrastructure.Videos;

// En memoria: quien sube o borra vídeos y el proceso que los comprime viven en el mismo servicio.
internal sealed class VideoProcessingSignal : IVideoProcessingSignal
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _tracked = [];
    private TaskCompletionSource _wake = NewSignal();

    public void Wake()
    {
        lock (_lock) _wake.TrySetResult();
    }

    public void Cancel(Guid videoId)
    {
        CancellationTokenSource? cancellation;
        lock (_lock) _tracked.TryGetValue(videoId, out cancellation);
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Terminó justo ahora: no hay nada que parar.
        }
    }

    // Un aviso llegado mientras nadie esperaba no se pierde: la siguiente espera vuelve enseguida.
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task signal;
        lock (_lock) signal = _wake.Task;

        using var delay = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await Task.WhenAny(signal, Task.Delay(timeout, delay.Token));
        await delay.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_wake.Task.IsCompleted) _wake = NewSignal();
        }
    }

    public IDisposable Track(Guid videoId, out CancellationToken cancellation)
    {
        var source = new CancellationTokenSource();
        lock (_lock) _tracked[videoId] = source;
        cancellation = source.Token;
        return new Tracking(this, videoId, source);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Tracking(VideoProcessingSignal owner, Guid videoId, CancellationTokenSource source) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._lock) owner._tracked.Remove(videoId);
            source.Dispose();
        }
    }
}
