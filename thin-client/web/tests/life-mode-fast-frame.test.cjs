'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname, '..', 'core.js'), 'utf8');
const match = source.match(/function coreRender\(s\)\{[\s\S]*?\n\}/);
assert.ok(match, 'coreRender must remain a directly testable function');

function invokeCoreRender(frame) {
  const calls = [];
  const names = [
    'drawClock', 'drawFeed', 'drawFight', 'show', 'drawRoad', 'drawSites',
    'drawGather', 'drawLife', 'drawDev', 'drawRealm', 'drawWeave',
    'drawQuests', 'drawCraft', 'drawParty', 'drawLiveMap', 'drawStats'
  ];
  const stubs = names.map(name => (...args) => calls.push({ name, args }));
  const render = new Function(...names, match[0] + '; return coreRender;')(...stubs);
  render(frame);
  return calls.map(call => call.name);
}

test('fast frames refresh only fast-backed panels and preserve life/dev cards', () => {
  const calls = invokeCoreRender({ fast: true, me: { pos: {}, combat: {}, feed: [] } });
  assert.deepEqual(calls, ['drawClock', 'drawFeed', 'drawFight']);
  assert.ok(!calls.includes('drawLife'));
  assert.ok(!calls.includes('drawDev'));
});

test('full frames still refresh life mode, vigor and developer panels', () => {
  const calls = invokeCoreRender({ day: 1, hour: 8, me: { mode: {}, vigor: {}, dev: {} } });
  assert.ok(calls.includes('drawLife'));
  assert.ok(calls.includes('drawDev'));
  assert.ok(calls.includes('drawSites'));
  assert.ok(calls.includes('drawGather'));
  assert.ok(calls.includes('drawClock'));
});
