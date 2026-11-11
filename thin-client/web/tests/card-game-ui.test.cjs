const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
const core = fs.readFileSync(path.join(root, 'core.js'), 'utf8');

// Card-game intents must share the result gate; otherwise single-slot me.result can be overwritten.
assert.match(html, /'card_game'\]\)/);
assert.match(html, /card_game:'Карточная игра'/);

// The tavern redraw signature includes the whole public game view, so each server move refreshes the hand/table.
assert.match(html, /JSON\.stringify\(me\.card_game\|\|null\)/);
assert.match(html, /drawCardTable\(M,me\)/);

// Both games use only the public me.card_game fields and the server's action/index contract.
for (const action of ['start', 'hit', 'stand', 'attack', 'defend', 'take', 'done']) {
  assert.ok(html.includes(`action:'${action}'`), `missing card-game action: ${action}`);
}
assert.match(html, /dealer_cards/);
assert.match(html, /game\.player/);
assert.match(html, /game\.table/);

// No client-side deck, shuffle, stake settlement, or dealer hand implementation.
assert.doesNotMatch(html, /dealerHand|dealer_hand|shuffleDeck|settleCardGame/);

// Purse and removal of worn gear are exposed in the same server-backed inventory controls.
assert.match(core, /purse:'кожаный кошель'/);
assert.match(core, /const WEAR = \[[^\]]*'purse'/);
assert.match(core, /send\(\{op:'unwear',item:'\$\{it\}'\}\)/);

console.log('card-game-ui: thin-client card table and inventory controls passed');
