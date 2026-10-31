using System.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Lyrical;

/// <summary>
/// Watches the Windows media session (Spotify, Apple Music, browsers...) through
/// GlobalSystemMediaTransportControls. Events fire on background threads.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly CancellationTokenSource _cts = new();
    private string _lastKey = "";

    public event Action<TrackInfo?>? TrackChanged;
    /// <summary>positionMs (already advanced to "now"), playing</summary>
    public event Action<double, bool>? ClockChanged;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => Attach(_manager.GetCurrentSession());
        Attach(_manager.GetCurrentSession());
        _ = PollLoopAsync(_cts.Token);
    }

    /// <summary>Re-emit the current track (used when the page reloads).</summary>
    public void Resend()
    {
        _lastKey = "";
        _ = RefreshAsync();
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnChanged;
            _session.PlaybackInfoChanged -= OnChanged;
            _session.TimelinePropertiesChanged -= OnChanged;
        }
        _session = session;
        _lastKey = "";
        if (_session == null)
        {
            TrackChanged?.Invoke(null);
            return;
        }
        _session.MediaPropertiesChanged += OnChanged;
        _session.PlaybackInfoChanged += OnChanged;
        _session.TimelinePropertiesChanged += OnChanged;
        _ = RefreshAsync();
    }

    private void OnChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var session = _session;
            if (session == null) return;

            var props = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var durationMs = (long)(timeline.EndTime - timeline.StartTime).TotalMilliseconds;
            var info = new TrackInfo(props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "", durationMs);

            if (info.Key != _lastKey)
            {
                _lastKey = info.Key;
                info = info with { ArtworkDataUrl = await ReadArtworkDataUrlAsync(props.Thumbnail) };
                TrackChanged?.Invoke(string.IsNullOrWhiteSpace(info.Title) ? null : info);
            }
            EmitClock(session);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Media refresh failed: {ex.Message}");
        }
    }

    // Keep artwork local and bounded. WebView2 receives an in-memory data URL,
    // avoiding a separate image host, cache, or provider dependency.
    private static async Task<string?> ReadArtworkDataUrlAsync(IRandomAccessStreamReference? thumbnail)
    {
        const ulong maxArtworkBytes = 2 * 1024 * 1024;
        if (thumbnail == null) return null;

        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > maxArtworkBytes) return null;

            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[(int)stream.Size];
            reader.ReadBytes(bytes);
            return $"data:image/*;base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Album art read failed: {ex.Message}");
            return null;
        }
    }

    private void EmitClock(GlobalSystemMediaTransportControlsSession session)
    {
        var timeline = session.GetTimelineProperties();
        var playing = session.GetPlaybackInfo().PlaybackStatus ==
                      GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var pos = timeline.Position;
        // Players report position at LastUpdatedTime; advance it to now while playing.
        if (playing && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
        {
            var age = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            if (age > TimeSpan.Zero && age < TimeSpan.FromMinutes(10)) pos += age;
        }
        ClockChanged?.Invoke(pos.TotalMilliseconds, playing);
    }

    // Some players update the timeline rarely, so also send a clock anchor every second.
    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var s = _session;
                if (s != null) EmitClock(s);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"Poll loop stopped: {ex.Message}"); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
