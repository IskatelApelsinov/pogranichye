'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const C=require('../catalog.js');

const cat={v:1,names:{backpack:'заплечный мешок',herb:'травы',spear:'копьё'},usables:['bread','pie','salve'],
  items:[{item:'spear',name:'копьё',at:'smithy',price:11,hand:true},{item:'backpack',name:'заплечный мешок',at:'market',price:14,slot:'back',carry:30},
    {item:'jerkin',name:'кожаная куртка',slot:'armor'},{item:'pie',name:'пирог'},{item:'salve',name:'мазь от ран'}]};

test('shop, names and inventory lists come from the server catalogue',()=>{
  assert.deepEqual(C.waresAt(cat,'market'),[{item:'backpack',name:'заплечный мешок',price:14}]);
  assert.deepEqual(C.waresAt(cat,'smithy'),[{item:'spear',name:'копьё',price:11}]);
  assert.deepEqual(C.waresAt(cat,'well'),[]);
  assert.deepEqual(C.waresAt(null,'market'),[]);                     // нет каталога — нет выдуманной лавки
  assert.equal(C.name(cat,{axe:'топор'},'backpack'),'заплечный мешок');
  assert.equal(C.name(cat,{axe:'топор'},'axe'),'топор');              // запасной словарь
  assert.equal(C.name(null,{},'mystery'),'mystery');
  const l=C.lists(cat,{hand:['x']});
  assert.deepEqual(l.hand,['spear']);
  assert.deepEqual(l.wear,['backpack','jerkin']);
  assert.deepEqual(l.use,['pie','salve']);                             // из сумы
  assert.deepEqual(l.eat,['bread']);                                   // из закромов
  assert.deepEqual(C.lists(null,{hand:['x']}),{hand:['x']});          // без каталога — прежние списки
});

test('nearby places show only server data; actions appear only when standing there',()=>{
  const sites=[{id:'w:1:2',name:'Травяной луг',kind:'herbs',dist:812,dir:'на севере',acts:['forage'],gives:'herb',left:0,here:false},
    {id:'w:3:4',name:'Стоянка у камня',kind:'camp',dist:4,dir:'на юге',acts:['rest','взлом'],here:true},
    {id:'77',name:'Старая башня',kind:'tower',dist:2600,dir:'на западе',acts:['explore','rest'],here:false},{name:'без id'}];
  const rows=C.siteRows(sites,{to:'',site:'77',left:2600});
  assert.equal(rows.length,3);
  assert.equal(rows[0].where,'810 м на севере');
  assert.deepEqual(rows[0].acts,[]);                                   // не дошёл — дел нет
  assert.equal(rows[0].about,'🧺 Собрать травы');
  assert.equal(rows[1].here,true);
  assert.deepEqual(rows[1].acts.map(a=>a.action),['rest']);            // неизвестное дело не показываем
  assert.equal(rows[2].going,true);
  assert.equal(rows[2].where,'2,6 км на западе');
  const here=C.siteRows([{id:'w:1:2',name:'Травяной луг',kind:'herbs',acts:['forage'],gives:'herb',left:0,here:true}],null);
  assert.equal(here[0].acts[0].disabled,true);                         // запас выбран
  assert.equal(here[0].acts[0].label,'🧺 Собрать травы (осталось 0)');
  assert.deepEqual(C.siteRows(undefined,null),[]);
});

test('page uses the catalogue instead of hard-coded shop lists',()=>{
  const html=fs.readFileSync(path.join(__dirname,'..','index.html'),'utf8');
  const core=fs.readFileSync(path.join(__dirname,'..','core.js'),'utf8');
  assert.match(html,/<script src="\/catalog\.js"><\/script>/);
  assert.match(html,/PogCatalog\.waresAt\(CAT,L\.key\)/);
  assert.doesNotMatch(html,/op:'take',item:'sword'/);
  assert.match(core,/fetch\('\/api\/catalog'\)/);
  assert.match(core,/PogCatalog\.siteRows\(/);
});
