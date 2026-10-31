# Lyrical for Windows (working name)

Word-by-word synced lyrics for whatever is playing on Windows. Milestone 1 of the development plan:
media detection, lyric lookup, the sync engine and one display style in a regular window.

## Try the display without Windows
Open `web/index.html` in any browser. It runs a looping demo with original sample lyrics.
Keys: `Space` pause (demo), `[` and `]` shift sync by 50 ms, `0` reset.

## Display styles
Pick one from the dropdown (move the mouse to reveal it, or press `S` to cycle). The choice is remembered.
- **Focus** - scrolling column; the sung line is large, neighbours shrink and fade.
- **Hopper** - one line at a time; a bar hops from word to word, the old line lifts away and the next arrives.
- **Lens** - lines bow and shrink away from the sung word like a convex lens.
- **Drift** - lines sit at different depths in a slowly swaying 3D wall; the sung word comes forward.
- **Icons** - one line at a time; a matched emoji pops in above each word as it starts (starter dictionary in `web/icons.js`).

## Run the web tests (Node 18+)
    npm test                             # engine + icon tests, no dependencies
    npm install && npm run test:styles   # renders every style in jsdom

## Build the Windows app
Requires Windows 10 1809+ or 11, the .NET 8 SDK, and the WebView2 runtime (included with Windows 11).

    cd src/Lyrical.App
    dotnet run

Play a song in Spotify or Apple Music. Keys in the window: `F11` full screen, `Ctrl+T` pin on top,
`[` / `]` sync offset.

## How it fits together
- `src/Lyrical.App` (C#, WPF + WebView2): reads the Windows media session, fetches lyrics from LRCLIB
  with a local cache, and sends three kinds of messages to the page: `track`, `lyrics`, `clock`.
- `web/engine.js`: LRC and Enhanced LRC parsing, word-time estimation, playback clock with drift easing,
  and active line/word lookup. Unit-tested in Node.
- `web/index.html`: the renderer. Runs its own frame loop from clock anchors sent by the app.

## Known limits of this milestone
- LRCLIB gives line-level timing only, so word times are estimated from word length. Real word-level
  timing needs another provider (plan phase 2). The parser already accepts Enhanced LRC word tags.
- Styles are CSS/DOM based. A true GPU lens shader (WebGL) is a possible later upgrade.
- No tray icon, settings window, album-art colours, installer or licensing yet.
- The C# project was written without access to a Windows machine and has not been compiled yet.

## Word-timed lyrics provider (Python sidecar)

Lyrical tries the Better Lyrics QQ endpoint first for genuine word-level timing, then falls back to LRCLIB for line-synced lyrics. The worker converts QQ QRC timing (milliseconds) into Enhanced LRC, which the existing renderer already understands. The app and renderer remain C# / JavaScript; Python is only a small provider bridge.

### Install / run on Windows

From the repository root, run:

```powershell
.\\providers\\betterlyrics\\setup.ps1
$env:Lyrical_PYTHON = (Resolve-Path .\\.venv\\Scripts\\python.exe).Path
cd .\\src\\Lyrical.App
dotnet run
```

The worker uses Python's standard library only; it does not require `musicxmatch-api` or any `pip` dependencies. If you already have Python installed, you can also set `Lyrical_PYTHON` directly to the full path of `python.exe`.

### Provider limitations

- The Better Lyrics QQ endpoint returned HTTP 200 and a cached QRC response during the initial diagnostic, but uncached availability is not guaranteed. It may return no result, require different coverage, or change behavior.
- TTML was not integrated because uncached TTML requests returned HTTP 401 without an API key.
- The worker only returns a result when QRC word timing can be parsed; otherwise LRCLIB is used and the renderer estimates word timing from line timing.
- Existing cached Enhanced-LRC results are reused. Cached line-only lyrics are retried against the word-timing provider so they can be upgraded.
- Lyrics display and redistribution rights must be confirmed before distributing Lyrical. Provider access does not itself grant those rights.

### Tests

```powershell
python -m unittest discover -s tests -p "test_*.py"
npm test
npm install; npm run test:styles
```

## Constellation lyric styles

The renderer includes five constellation-inspired display modes in the style picker:

- **Constellation · Fixed** — stable, ordered layout; every lyric word is a star and adjacent words are connected.
- **Constellation · Evolving** — words and links are revealed as playback advances, giving the impression that the constellation is being drawn live.
- **Constellation · Hybrid** — keeps the complete path present while emphasizing the active neighborhood and fading distant words.
- **Constellation · Nebula** — hybrid layout with a subtle purple/blue nebula atmosphere.
- **Constellation · Orbit** — hybrid layout with extra emphasis around the currently sung word.

All five modes use the same parsed lyric sequence and playback word timestamps. The three primary layout choices (Fixed, Evolving, Hybrid) are independent options in the existing style picker; no separate provider or API key is needed. The layout is recalculated when lyrics or window dimensions change. The current word is bright, previous words fade, and the ordered connecting path remains visible.


### Constellation camera-follow animation

Constellation styles now use a smooth camera-follow effect: the currently sung word is brought to the center of the viewport and enlarged, while the connected lyric field moves behind it. Previous words dim as the active word changes, and a subtle ambient star field drifts in the background. The word layout remains deterministic, so the sequence stays connected and readable rather than randomly reshuffling. Reduced-motion preferences disable the movement.


## Constellation readability controls

The renderer includes a **Lyrics** visibility setting:
- **Full constellation** keeps all words visible.
- **Fade by distance** progressively fades words and connections farther from the active word.
- **3 / 5 / 8 previous and next** keeps a bounded word window around the active word to reduce clutter.

The **Size** setting scales the UI from 100% to 200%, with 125% selected by default for readability on high-resolution displays such as 2560×1440. Preferences are saved locally in the WebView.

These settings apply to the constellation renderer; non-constellation layouts retain their existing layout behavior.
