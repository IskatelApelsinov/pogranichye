'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const W=require('../walk-state.js');

test('tap sends the goal and the screen changes only on server arrival',()=>{
  let st=W.initial();
  const tap=W.onTap(st,'tavern',{at:'well',walk:null});
  assert.deepEqual(tap,{state:{target:'tavern',waited:0},send:'tavern',legacy:false});
  st=tap.state;
  // сервер ведёт тело: экран места НЕ меняется
  let f=W.onFrame(st,{at:'',walk:{to:'tavern',left:31.6}},'well');
  assert.equal(f.arrived,null);
  assert.equal(f.message,'🚶 Иду: %s — осталось 32 м');
  st=f.state;
  // сервер подтвердил прибытие
  f=W.onFrame(st,{at:'tavern',walk:null},'well');
  assert.equal(f.arrived,'tavern');
  assert.equal(f.message,'Пришёл: %s');
  assert.deepEqual(f.state,W.initial());
});

test('retarget, cancel and already-here taps',()=>{
  // смена цели в пути — просто новая цель
  assert.equal(W.onTap({target:'woods',waited:0},'smithy',{at:'',walk:{to:'woods',left:80}}).send,'smithy');
  // повторный тап по той же цели — отмена
  assert.deepEqual(W.onTap({target:'woods',waited:0},'woods',{at:'',walk:{to:'woods',left:80}}),{state:W.initial(),send:'',legacy:false});
  // уже у места — серверу ничего не шлём
  assert.equal(W.onTap(W.initial(),'well',{at:'well',walk:null}).send,null);
});

test('server refusal is reported instead of pretending to arrive',()=>{
  let st={target:'mine',waited:0}, f;
  for(let i=0;i<W.WAIT_FRAMES-1;i++){ f=W.onFrame(st,{at:'well',walk:null},'well'); assert.equal(f.message,null); st=f.state; }
  f=W.onFrame(st,{at:'well',walk:null},'well');
  assert.equal(f.message,'Туда сейчас не пройти: %s');
  assert.equal(f.target,'mine');
  assert.deepEqual(f.state,W.initial());
});

test('reconnect or another client: screen follows the confirmed place; old servers keep instant switch',()=>{
  assert.equal(W.onFrame(W.initial(),{at:'smithy',walk:null},'well').arrived,'smithy');
  assert.equal(W.onFrame(W.initial(),{at:'',walk:null},'well').arrived,null);       // в поле экран не дёргаем
  assert.deepEqual(W.onTap(W.initial(),'tavern',{pos:{x:0,y:0}}),{state:W.initial(),send:'tavern',legacy:true});
  assert.equal(W.onFrame(W.initial(),{pos:{x:0,y:0}},'well').arrived,null);
});

test('page wires navigation through WalkState, not a local location switch',()=>{
  const html=fs.readFileSync(path.join(__dirname,'..','index.html'),'utf8');
  assert.match(html,/<script src="\/walk-state\.js"><\/script>/);
  assert.match(html,/WalkState\.onTap\(walkState,key,last&&last\.me\)/);
  assert.match(html,/function render\(s\)\{ last=s; syncWalk\(s\);/);
  assert.doesNotMatch(html,/function goTo\(key\)\{\s*curLoc=key;/);
});
