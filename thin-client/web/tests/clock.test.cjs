const test=require('node:test'), assert=require('node:assert');
const fs=require('fs'), path=require('path');
const src=fs.readFileSync(path.join(__dirname,'..','core.js'),'utf8');
const m=src.match(/function clockText\(day, hour\)\{[\s\S]*?\n\}/);
const clockText=new Function(m[0]+'; return clockText;')();
test('world clock shows server day and hour, never device time',()=>{
  assert.equal(clockText(12,7),'☀️ День 12 · 07:00 · утро');
  assert.equal(clockText(3,23),'🌙 День 3 · 23:00 · ночь');
  assert.equal(clockText(undefined,undefined),'🕰 время мира неизвестно');
  assert.equal(clockText(1,24),'🕰 время мира неизвестно');
  assert.ok(!/new Date|Date\.now\(\)\.getHours|getHours/.test(m[0]));
});
