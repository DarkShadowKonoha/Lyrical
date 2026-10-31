using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;

namespace Lyrical;

public sealed record LyricsResult(string? Synced, string? Plain, bool Instrumental);

/// <summary>
/// Finds lyrics for a track: local cache first, Better Lyrics QQ for genuine word timing,
/// then LRCLIB for line-synced lyrics.
/// </summary>
public sealed class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(7);
    private readonly string _cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lyrical", "cache");

    internal sealed record CacheEntry(LyricsResult? Result, DateTime FetchedUtc);

    private sealed class LrclibItem
    {
        [JsonPropertyName("trackName")] public string? TrackName { get; set; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
        [JsonPropertyName("duration")] public double? Duration { get; set; }
        [JsonPropertyName("instrumental")] public bool? Instrumental { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        // LRCLIB asks clients to identify themselves. Replace with your app's real name and URL.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LyricalForWindows/0.1 (set-your-project-url)");
        return c;
    }

    public async Task<LyricsResult?> GetAsync(TrackInfo track, CancellationToken ct)
    {
        var title = TrackNormalizer.Title(track.Title);
        var key = CacheKey(title, track);
        var cached = await ReadCacheAsync(key);
        if (cached != null)
        {
            // Keep genuine Enhanced-LRC results fast, but allow older LRCLIB-only
            // cache entries to be upgraded when a word-timing provider becomes available.
            if (cached.Result != null && HasWordTiming(cached.Result.Synced))
                return cached.Result;
            if (cached.Result == null && DateTime.UtcNow - cached.FetchedUtc < NegativeTtl)
                return null;
        }

        var result = await FetchAsync(title, track, ct);
        await WriteCacheAsync(key, new CacheEntry(result, DateTime.UtcNow));
        return result;
    }

    private async Task<LyricsResult?> FetchAsync(string title, TrackInfo track, CancellationToken ct)
    {
        // 1) Prefer genuine word-timed lyrics from Better Lyrics' QQ provider.
        // The Python bridge uses only the standard library; if it fails or returns
        // malformed/untimed data, continue to LRCLIB.
        var wordTimed = await TryFetchBetterLyricsAsync(title, track, ct);
        if (wordTimed != null && !string.IsNullOrWhiteSpace(wordTimed.Synced))
            return wordTimed;

        var seconds = (int)Math.Round(track.DurationMs / 1000.0);

        // 2) Exact LRCLIB match (line-level timing fallback).
        var url = "https://lrclib.net/api/get" +
                  $"?track_name={Uri.EscapeDataString(title)}" +
                  $"&artist_name={Uri.EscapeDataString(track.Artist)}" +
                  $"&album_name={Uri.EscapeDataString(track.Album)}" +
                  $"&duration={seconds}";
        using (var resp = await Http.GetAsync(url, ct))
        {
            if (resp.IsSuccessStatusCode)
            {
                var item = await resp.Content.ReadFromJsonAsync<LrclibItem>(cancellationToken: ct);
                if (item != null) return ToResult(item);
            }
            else if (resp.StatusCode != HttpStatusCode.NotFound)
            {
                Debug.WriteLine($"LRCLIB get failed: {(int)resp.StatusCode}");
                return null; // transient failure: do not negative-cache an error as "no lyrics"
            }
        }

        // 3) Search, accepting only a close duration match so we never show another version's timings.
        var searchUrl = "https://lrclib.net/api/search" +
                        $"?track_name={Uri.EscapeDataString(title)}" +
                        $"&artist_name={Uri.EscapeDataString(track.Artist)}";
        var items = await Http.GetFromJsonAsync<List<LrclibItem>>(searchUrl, ct) ?? new();
        var best = items
            .Where(i => !string.IsNullOrEmpty(i.SyncedLyrics) && i.Duration.HasValue &&
                        Math.Abs(i.Duration.Value - seconds) <= 3)
            .OrderBy(i => Math.Abs(i.Duration!.Value - seconds))
            .FirstOrDefault();
        return best != null ? ToResult(best) : null;
    }


    private sealed record BetterLyricsRequest(string Title, string Artist, string Album, double DurationSeconds);
    private sealed record BetterLyricsResponse(string? Synced, bool WordTimed);

    private static async Task<LyricsResult?> TryFetchBetterLyricsAsync(
        string title, TrackInfo track, CancellationToken ct)
    {
        var worker = Path.Combine(AppContext.BaseDirectory, "providers", "betterlyrics", "worker.py");
        if (!File.Exists(worker))
        {
            Debug.WriteLine("Better Lyrics worker not found; using LRCLIB fallback.");
            return null;
        }

        // Set Lyrical_PYTHON to a Python executable path to use a project virtual environment.
        // Defaults to 'python', which is expected to be on PATH during development.
        var python = Environment.GetEnvironmentVariable("Lyrical_PYTHON");
        if (string.IsNullOrWhiteSpace(python)) python = "python";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = python,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(worker)!
            }
        };
        process.StartInfo.ArgumentList.Add(worker);

        try
        {
            if (!process.Start()) return null;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var request = new BetterLyricsRequest(title, track.Artist, track.Album, track.DurationMs / 1000.0);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Json));
            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(9));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                if (ct.IsCancellationRequested) throw;
                Debug.WriteLine("Better Lyrics QQ lookup timed out; using LRCLIB fallback.");
                return null;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr)) Debug.WriteLine($"Better Lyrics QQ: {stderr.Trim()}");
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout)) return null;

            var response = JsonSerializer.Deserialize<BetterLyricsResponse>(stdout.Trim(), Json);
            if (response is { WordTimed: true } && !string.IsNullOrWhiteSpace(response.Synced))
            {
                Debug.WriteLine("Using Better Lyrics QQ word-timed lyrics.");
                return new LyricsResult(response.Synced, null, false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Better Lyrics QQ unavailable; using LRCLIB fallback: {ex.Message}");
        }

        return null;
    }

    private static bool HasWordTiming(string? lrc) =>
        !string.IsNullOrWhiteSpace(lrc) &&
        System.Text.RegularExpressions.Regex.IsMatch(
            lrc, @"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>");

    private static LyricsResult ToResult(LrclibItem i) =>
        new(string.IsNullOrWhiteSpace(i.SyncedLyrics) ? null : i.SyncedLyrics,
            string.IsNullOrWhiteSpace(i.PlainLyrics) ? null : i.PlainLyrics,
            i.Instrumental ?? false);

    // ---- cache ----
    private static string CacheKey(string title, TrackInfo t)
    {
        var raw = $"betterlyrics-qq-v1|{title}|{t.Artist}|{t.Album}|{t.DurationMs / 1000}".ToLowerInvariant();
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private async Task<CacheEntry?> ReadCacheAsync(string key)
    {
        try
        {
            var path = Path.Combine(_cacheDir, key + ".json");
            if (!File.Exists(path)) return null;
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<CacheEntry>(fs);
        }
        catch (Exception ex) { Debug.WriteLine($"Cache read failed: {ex.Message}"); return null; }
    }

    private async Task WriteCacheAsync(string key, CacheEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_cacheDir);
            var path = Path.Combine(_cacheDir, key + ".json");
            await using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, entry);
        }
        catch (Exception ex) { Debug.WriteLine($"Cache write failed: {ex.Message}"); }
    }
}
