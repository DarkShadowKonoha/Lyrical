// Lyrical for Windows - lyric engine: LRC parsing, word-timing estimation, playback clock, sync lookup.
// Pure functions and classes with no DOM access, so it runs in WebView2 and in Node tests.
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.LyricalEngine = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  const LEAD = /^((?:\s*\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\])+)\s*(.*)$/;
  const LINE_TS = /\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]/g;
  const WORD_TS = /<(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?>/g;

  function toMs(m, s, f) {
    const frac = f === undefined ? 0 : Number(String(f).padEnd(3, '0').slice(0, 3));
    return Number(m) * 60000 + Number(s) * 1000 + frac;
  }

  // Splits a line body into words. Enhanced LRC tags <mm:ss.xx> give real word starts.
  function parseBody(body) {
    const items = [];
    let tagged = false;
    let last = 0;
    let pendingStart = null;
    const push = (text, start) => {
      if (!text) return;
      const prev = items[items.length - 1];
      const glue = prev && !/\s$/.test(prev.raw) && !/^\s/.test(text);
      if (glue) { prev.raw += text; return; }
      items.push({ raw: text, startMs: start });
    };
    for (const m of body.matchAll(WORD_TS)) {
      push(body.slice(last, m.index), pendingStart);
      pendingStart = toMs(m[1], m[2], m[3]);
      tagged = true;
      last = m.index + m[0].length;
    }
    push(body.slice(last), pendingStart);
    const words = [];
    for (const it of items) {
      const parts = it.raw.split(/\s+/).filter(Boolean);
      parts.forEach((p, i) => words.push({ text: p, startMs: i === 0 ? it.startMs : null, endMs: null }));
    }
    return { words, tagged };
  }

  // Assign start/end to words that have none, spread by word length across the sung span of the line.
  function alignWords(line) {
    const words = line.words;
    if (!words.length) { line.sungEndMs = line.startMs; return line; }
    const chars = words.reduce((n, w) => n + w.text.length, 0);
    const lineSpan = Math.max(1, line.endMs - line.startMs);
    const sung = Math.min(lineSpan, Math.max(1200, chars * 90));
    if (words.every(w => w.startMs !== null)) {
      words.forEach((w, i) => { w.endMs = i + 1 < words.length ? words[i + 1].startMs : line.endMs; });
      line.sungEndMs = words[words.length - 1].endMs;
      return line;
    }
    const weight = w => Math.max(2, w.text.length);
    const total = words.reduce((n, w) => n + weight(w), 0);
    let t = line.startMs;
    for (const w of words) {
      const span = sung * weight(w) / total;
      w.startMs = Math.round(t);
      w.endMs = Math.round(t + span);
      t += span;
    }
    line.sungEndMs = words[words.length - 1].endMs;
    return line;
  }

  function parseLrc(text) {
    const lines = [];
    let hasWordTiming = false;
    for (const raw of String(text || '').split(/\r?\n/)) {
      const m = LEAD.exec(raw);
      if (!m) continue;
      const stamps = [...m[1].matchAll(LINE_TS)].map(x => toMs(x[1], x[2], x[3]));
      const { words, tagged } = parseBody(m[2]);
      if (tagged) hasWordTiming = true;
      for (const s of stamps) {
        lines.push({ startMs: s, endMs: 0, words: words.map(w => ({ ...w })), text: words.map(w => w.text).join(' ') });
      }
    }
    lines.sort((a, b) => a.startMs - b.startMs);
    lines.forEach((l, i) => {
      l.endMs = i + 1 < lines.length ? lines[i + 1].startMs : l.startMs + 6000;
      alignWords(l);
    });
    return { hasWordTiming, lines };
  }

  // Playback clock: extrapolates between player updates and eases small corrections.
  class PlaybackClock {
    constructor() { this.posMs = 0; this.at = 0; this.playing = false; this.rate = 1; this.err = 0; this.has = false; }
    raw(now) { return this.playing ? this.posMs + (now - this.at) * this.rate : this.posMs; }
    anchor(posMs, playing, now, rate = 1) {
      let err = 0;
      if (this.has && this.playing && playing) {
        const predicted = this.positionAt(now);
        err = predicted - posMs;
        if (Math.abs(err) >= 400) err = 0; // treat as a seek: snap
      }
      this.posMs = posMs; this.at = now; this.playing = playing; this.rate = rate; this.err = err; this.has = true;
    }
    positionAt(now) {
      const ease = Math.max(0, 1 - (now - this.at) / 500);
      return this.raw(now) + this.err * ease;
    }
  }

  // Finds the active line, word and word progress for a position.
  function locate(doc, posMs) {
    const lines = doc && doc.lines ? doc.lines : [];
    if (!lines.length || posMs < lines[0].startMs) return { line: -1, word: -1, progress: 0 };
    let lo = 0, hi = lines.length - 1;
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (lines[mid].startMs <= posMs) lo = mid; else hi = mid - 1;
    }
    const line = lines[lo];
    if (!line.words.length) return { line: lo, word: -1, progress: 0 };
    let w = -1;
    for (let i = 0; i < line.words.length; i++) if (line.words[i].startMs <= posMs) w = i; else break;
    if (w < 0) return { line: lo, word: -1, progress: 0 };
    const word = line.words[w];
    const span = Math.max(1, word.endMs - word.startMs);
    const progress = Math.min(1, Math.max(0, (posMs - word.startMs) / span));
    return { line: lo, word: w, progress };
  }

  // Removes edition noise so lyric lookups match: "Song - 2011 Remaster" -> "Song".
  function normalizeTitle(title) {
    return String(title || '')
      .replace(/\s*[-–]\s*(\d{4}\s*)?(remaster(ed)?|live|mono|stereo|single version|radio edit|deluxe|bonus track)[^()\[\]]*$/i, '')
      .replace(/\s*[(\[]\s*(\d{4}\s*)?(remaster(ed)?|live|mono|stereo|deluxe|bonus track|feat\.?[^)\]]*|with [^)\]]*)\s*[)\]]/ig, '')
      .replace(/\s{2,}/g, ' ').trim();
  }

  return { parseLrc, alignWords, PlaybackClock, locate, normalizeTitle, toMs };
});
