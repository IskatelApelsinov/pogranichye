const assert=require('node:assert/strict');
const Activity=require('../activity-minigames.js');

const noviceFishing=Activity.fishing({skills:{fisher:0}});
const expertFishing=Activity.fishing({skills:{fisher:100}});
assert.equal(noviceFishing.zoneWidth,18);
assert.equal(expertFishing.zoneWidth,45);
assert.ok(expertFishing.speed<noviceFishing.speed);

const noviceChopping=Activity.chopping({skills:{woodcutter:0}});
const expertChopping=Activity.chopping({skills:{woodcutter:100}});
assert.equal(noviceChopping.threshold,70);
assert.equal(expertChopping.threshold,50);
assert.ok(expertChopping.speed<noviceChopping.speed);

assert.equal(Activity.fishing({skills:{fisher:500}}).zoneWidth,45);
assert.equal(Activity.chopping({skills:{woodcutter:-5}}).threshold,70);
assert.equal(Activity.fishing({skills:{fisher:'bad'}}).skill,0);
assert.equal(Activity.chopping(null).skill,0);
const noviceField=Activity.fieldwork({skills:{farmer:0}});
const expertField=Activity.fieldwork({skills:{farmer:100}});
assert.equal(noviceField.zoneWidth,18);
assert.equal(expertField.zoneWidth,43);
assert.ok(expertField.speed<noviceField.speed);

const noviceForge=Activity.forging({skills:{smith:0}});
const expertForge=Activity.forging({skills:{smith:100}});
assert.equal(noviceForge.threshold,72);
assert.equal(expertForge.threshold,50);
assert.ok(expertForge.speed<noviceForge.speed);

console.log('activity-minigames: skill scaling checks passed');
