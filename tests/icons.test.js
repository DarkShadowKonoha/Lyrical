const assert = require('assert');
const I = require('../web/icons.js');
let n = 0; const t = (name, fn) => { fn(); n++; console.log('ok -', name); };
t('finds plain words and ignores punctuation and case', () => {
  assert.strictEqual(I.iconFor('Love,'), '❤️');
  assert.strictEqual(I.iconFor('FIRE!'), '🔥');
});
t('handles plurals and endings', () => {
  assert.strictEqual(I.iconFor('stars'), '⭐');
  assert.strictEqual(I.iconFor('kisses'), '💋');
  assert.strictEqual(I.iconFor('dancing'), '💃');
  assert.strictEqual(I.iconFor('burned'), '🔥');
  assert.strictEqual(I.iconFor('running'), '🏃');
  assert.strictEqual(I.iconFor("night's"), '🌙');
});
t('skips short and unmapped words', () => {
  assert.strictEqual(I.iconFor('the'), null);
  assert.strictEqual(I.iconFor('a'), null);
  assert.strictEqual(I.iconFor('whatever'), null);
  assert.strictEqual(I.iconFor(''), null);
});
console.log(n + ' icon tests passed');
