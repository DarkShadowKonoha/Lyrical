"""JSON-in/JSON-out bridge for Better Lyrics' QQ word-timed lyric provider.

Input:  {"title": ..., "artist": ..., "album": ..., "durationSeconds": ...}
Output: {"synced": "Enhanced LRC...", "wordTimed": true} or {"synced": null, "wordTimed": false}

The QQ endpoint may return cached results without an API key; uncached coverage is
not guaranteed. Fail closed so the C# app can fall back to LRCLIB.
"""
from __future__ import annotations

import html
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from typing import Any

API_URL = "https://api.betterlyrics.org/qq/getLyrics"
USER_AGENT = "VerciForWindows/0.2"


def normalize(value: str) -> str:
    value = re.sub(r"\([^)]*\)|\[[^]]*\]", " ", value or "")
    return re.sub(r"[^a-z0-9]+", " ", value.lower()).strip()


def timestamp(ms: int) -> str:
    ms = max(0, int(ms))
    minutes, rem = divmod(ms, 60_000)
    seconds, millis = divmod(rem, 1000)
    return f"{minutes:02d}:{seconds:02d}.{millis // 10:02d}"


def extract_qrc(payload: Any) -> str | None:
    """Extract QRC from the provider's JSON lyrics field/XML wrapper."""
    if isinstance(payload, dict):
        payload = payload.get("lyrics") or payload.get("lyric") or payload.get("data")
    if not isinstance(payload, str) or not payload.strip():
        return None
    text = payload.strip()
    # Some responses return QRC directly.
    if not text.startswith("<") and re.search(r"(?m)^\[\d+,\d+\]", text):
        return text
    # QQ responses are commonly wrapped in a QrcInfos XML-like document. The
    # LyricContent attribute contains embedded newlines and may not be strict XML.
    match = re.search(r'LyricContent="(.*?)"\s*/>', text, flags=re.DOTALL)
    if match:
        return html.unescape(match.group(1)).replace("\\n", "\n")
    # Be tolerant of alternate wrappers.
    match = re.search(r"<LyricContent[^>]*>(.*?)</LyricContent>", text, flags=re.DOTALL)
    if match:
        return html.unescape(match.group(1))
    return None


LINE_RE = re.compile(r"^\[(\d+)\s*,\s*(\d+)\](.*)$")
CHUNK_RE = re.compile(r"\((\d+)\s*,\s*(\d+)\)")


def qrc_to_enhanced_lrc(qrc: str) -> str | None:
    """Convert QQ QRC line/word offsets (milliseconds) to Enhanced LRC."""
    output: list[tuple[int, str]] = []
    genuine_word_count = 0
    for raw in qrc.splitlines():
        match = LINE_RE.match(raw.strip())
        if not match:
            continue
        line_start = int(match.group(1))
        body = match.group(3)
        chunks: list[tuple[int, str]] = []
        cursor = 0
        for timing in CHUNK_RE.finditer(body):
            text = body[cursor:timing.start()]
            offset = int(timing.group(1))
            # Empty/whitespace chunks are separators, not words.
            if text.strip():
                chunks.append((offset, text))
                genuine_word_count += 1
            cursor = timing.end()
        if not chunks:
            # Some QRC variants may omit a timing tuple after the final chunk;
            # without a timestamp for that chunk it cannot be word-synced.
            continue
        rendered = "".join(f"<{timestamp(start)}>{text}" for start, text in chunks)
        output.append((line_start, f"[{timestamp(line_start)}]{rendered}"))
    if not output or genuine_word_count < 2:
        return None
    output.sort(key=lambda item: item[0])
    result = "\n".join(line for _, line in output)
    return result if re.search(r"<\d{1,3}:\d{2}(?:[.:]\d{1,3})?>", result) else None


def fetch_qq(title: str, artist: str, album: str, duration_seconds: float) -> str | None:
    params = {"s": title, "a": artist}
    if album:
        params["al"] = album
    if duration_seconds > 0:
        params["d"] = str(int(round(duration_seconds)))
    url = API_URL + "?" + urllib.parse.urlencode(params)
    request = urllib.request.Request(
        url,
        headers={"User-Agent": USER_AGENT, "Accept": "application/json"},
        method="GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=7) as response:
            if response.status != 200:
                return None
            payload = json.loads(response.read().decode("utf-8", errors="replace"))
    except (urllib.error.URLError, TimeoutError, OSError, json.JSONDecodeError) as exc:
        print(f"Better Lyrics QQ lookup failed: {type(exc).__name__}: {exc}", file=sys.stderr)
        return None
    qrc = extract_qrc(payload)
    return qrc_to_enhanced_lrc(qrc) if qrc else None


def main() -> int:
    try:
        request = json.load(sys.stdin)
        title = str(request.get("title") or "").strip()
        artist = str(request.get("artist") or "").strip()
        album = str(request.get("album") or "").strip()
        try:
            duration = float(request.get("durationSeconds") or 0)
        except (TypeError, ValueError):
            duration = 0.0
        if not title or not artist:
            print(json.dumps({"synced": None, "wordTimed": False}))
            return 0
        synced = fetch_qq(title, artist, album, duration)
        print(json.dumps({"synced": synced, "wordTimed": bool(synced)}, ensure_ascii=False))
        return 0
    except Exception as exc:
        print(f"Better Lyrics QQ worker failed: {type(exc).__name__}: {exc}", file=sys.stderr)
        print(json.dumps({"synced": None, "wordTimed": False}))
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
