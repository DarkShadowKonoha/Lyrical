const { JSDOM } = require('jsdom');
const path = require('path');
const assert = require('assert');
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const dom = await JSDOM.fromFile(path.resolve(__dirname, '../web/index.html'), { runScripts: 'dangerously', resources: 'usable', pretendToBeVisual: true });
  const w = dom.window, d = w.document;
  const errors = []; w.addEventListener('error', e => errors.push(e.message));
  await wait(500);
  const sel = d.getElementById('style');
  console.log('styles in dropdown:', [...sel.options].map(o => o.value).join(', '));
  await wait(3200); // demo position ~3.7s => line 0 sung, line 1 not yet

  for (const name of [...sel.options].map(o => o.value)) {
    sel.value = name; sel.dispatchEvent(new w.Event('change'));
    await wait(450);
    const body = d.body;
    const now = d.querySelectorAll('.line.now');
    assert.strictEqual(body.dataset.style, name, name + ': data-style');
    assert.strictEqual(now.length, 1, name + ': exactly one active line, got ' + now.length);
    const lit = [...now[0].querySelectorAll('.w')].filter(s => parseFloat(s.style.getPropertyValue('--p') || 0) > 0).length;
    assert.ok(lit >= 1, name + ': some words lit');
    let extra = '';
    if (name === 'hopper') { assert.ok(d.getElementById('hop').parentNode === now[0], 'hop marker lives in active line'); extra = ' | marker in active line'; }
    if (name === 'lens') { const t = now[0].style.transform, f1 = d.querySelector('.line.f1'); assert.ok(/scale\(1\.000\)/.test(t), 'active lens line full size'); assert.ok(/scale\(0\.7/.test(f1.style.transform), 'next line shrinks'); extra = ' | next line ' + f1.style.transform.match(/scale\([^)]*\)/)[0]; }
    if (name === 'drift') { assert.ok(/translate3d\([^)]*120px\)/.test(now[0].style.transform), 'active line pushed forward'); assert.ok(d.getElementById('lines').style.getPropertyValue('--ry') !== '', 'wall sways'); extra = ' | sway ' + d.getElementById('lines').style.getPropertyValue('--ry'); }
    if (name === 'icons') { const icons = now[0].querySelectorAll('.ic'); extra = ' | icon words in line: ' + icons.length + ', sung: ' + now[0].querySelectorAll('.wd.sung').length; }
    console.log('ok', name, '| lit words:', lit, extra);
  }
  // hopper moves on to the next line: previous leaves, next arrives
  sel.value = 'hopper'; sel.dispatchEvent(new w.Event('change'));
  await wait(5200); // ~9s into the demo => line 1 should now be active, line 0 past
  const all = [...d.querySelectorAll('.line')];
  const n = all.findIndex(e => e.classList.contains('now'));
  console.log('active line index:', n, '| previous:', all[n - 1].className.replace('line ', ''), '| next:', all[n + 1].className.replace('line ', ''));
  assert.ok(n > 0 && all[n - 1].classList.contains('p1') && all[n + 1].classList.contains('f1'), 'previous line leaves, next arrives');
  const hopEl = d.getElementById('hop');
  assert.ok(hopEl.parentNode === all[n], 'marker moved to the new line');
  // keyboard cycling
  d.dispatchEvent(new w.KeyboardEvent('keydown', { key: 's', bubbles: true }));
  w.dispatchEvent(new w.KeyboardEvent('keydown', { key: 's' }));
  await wait(100);
  console.log('after S key:', d.body.dataset.style);
  console.log('script errors:', errors.length ? errors : 'none');
  w.close(); process.exit(errors.length ? 1 : 0);
})().catch(e => { console.error('FAIL', e.message); process.exit(1); });
