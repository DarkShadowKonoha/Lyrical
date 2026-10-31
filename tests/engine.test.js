const assert = require('assert');
const E = require('../web/engine.js');
let n = 0; const t = (name, fn) => { fn(); n++; console.log('ok -', name); };

t('parses line-synced LRC and ignores metadata tags', () => {
  const d = E.parseLrc('[ar:Someone]\n[00:10.00] Hello brave world\n[00:14.50]Second line here');
  assert.strictEqual(d.lines.length, 2);
  assert.strictEqual(d.lines[0].startMs, 10000);
  assert.strictEqual(d.lines[1].startMs, 14500);
  assert.strictEqual(d.hasWordTiming, false);
  assert.strictEqual(d.lines[0].words.length, 3);
});
t('handles multiple timestamps on one line and sorts', () => {
  const d = E.parseLrc('[01:00.00][00:05.00] chorus\n[00:30.00] verse');
  assert.deepStrictEqual(d.lines.map(l => l.startMs), [5000, 30000, 60000]);
});
t('fraction digits: .5 is 500ms, .05 is 50ms', () => {
  assert.strictEqual(E.toMs('0', '01', '5'), 1500);
  assert.strictEqual(E.toMs('0', '01', '05'), 1050);
  assert.strictEqual(E.toMs('0', '01', '50'), 1500);
});
t('parses enhanced LRC word tags', () => {
  const d = E.parseLrc('[00:10.00] <00:10.00>Hello <00:10.60>brave <00:11.20>world\n[00:13.00] next');
  assert.strictEqual(d.hasWordTiming, true);
  const w = d.lines[0].words;
  assert.deepStrictEqual(w.map(x => x.startMs), [10000, 10600, 11200]);
  assert.strictEqual(w[0].endMs, 10600);
  assert.strictEqual(w[2].endMs, 13000);
});
t('merges syllable tags into one word', () => {
  const d = E.parseLrc('[00:01.00] <00:01.00>Won<00:01.30>der <00:01.80>land');
  assert.deepStrictEqual(d.lines[0].words.map(w => w.text), ['Wonder', 'land']);
  assert.strictEqual(d.lines[0].words[0].startMs, 1000);
});
t('estimated word times stay inside the sung span, in order', () => {
  const d = E.parseLrc('[00:00.00] one two three four\n[01:00.00] later');
  const l = d.lines[0];
  assert.ok(l.sungEndMs <= 1200 + 20 * 90);
  for (let i = 1; i < l.words.length; i++) assert.ok(l.words[i].startMs >= l.words[i - 1].startMs);
  assert.ok(l.sungEndMs < 60000, 'does not stretch across instrumental gap');
});
t('empty lines act as gaps with no words', () => {
  const d = E.parseLrc('[00:05.00] hi there\n[00:09.00]\n[00:12.00] again');
  assert.strictEqual(d.lines[1].words.length, 0);
});
t('locate finds line, word and progress', () => {
  const d = E.parseLrc('[00:10.00] <00:10.00>Hello <00:11.00>world\n[00:13.00] next');
  assert.deepStrictEqual(E.locate(d, 5000), { line: -1, word: -1, progress: 0 });
  const a = E.locate(d, 10500);
  assert.strictEqual(a.line, 0); assert.strictEqual(a.word, 0); assert.ok(Math.abs(a.progress - 0.5) < 1e-9);
  const b = E.locate(d, 11500);
  assert.strictEqual(b.word, 1); assert.ok(Math.abs(b.progress - 0.25) < 1e-9);
  assert.strictEqual(E.locate(d, 13100).line, 1);
});
t('clock extrapolates and freezes on pause', () => {
  const c = new E.PlaybackClock();
  c.anchor(1000, true, 0);
  assert.strictEqual(c.positionAt(500), 1500);
  c.anchor(1500, false, 500);
  assert.strictEqual(c.positionAt(900), 1500);
});
t('clock eases small corrections and snaps on seeks', () => {
  const c = new E.PlaybackClock();
  c.anchor(0, true, 0);
  c.anchor(1000 - 100, true, 1000); // player says 900, we predicted 1000: ease down
  assert.ok(Math.abs(c.positionAt(1000) - 1000) < 1e-9, 'continuous at anchor');
  assert.ok(Math.abs(c.positionAt(1500) - 1400) < 1e-9, 'eased out after 500ms');
  c.anchor(60000, true, 2000); // seek
  assert.strictEqual(c.positionAt(2000), 60000);
});
t('normalizeTitle strips edition noise', () => {
  assert.strictEqual(E.normalizeTitle('Song - 2011 Remaster'), 'Song');
  assert.strictEqual(E.normalizeTitle('Song (feat. Someone)'), 'Song');
  assert.strictEqual(E.normalizeTitle('Song - Live at Venue'), 'Song');
  assert.strictEqual(E.normalizeTitle('Plain Title'), 'Plain Title');
});
console.log(n + ' tests passed');
