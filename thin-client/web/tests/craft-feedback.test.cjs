'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const CraftFeedback=require('../craft-feedback.js');
const RequestGate=require('../request-gate.js');

test('only matching server reqid resolves a pending recipe',()=>{
  const state=new CraftFeedback();
  state.begin('craft-1','sword');
  assert.equal(state.resolve({reqid:'other',ok:true}),null);
  assert.equal(state.isPending('sword'),true);
  assert.deepEqual(state.resolve({reqid:'craft-1',ok:true}),{recipeId:'sword',ok:true,reason:''});
  assert.equal(state.isPending('sword'),false);
});

test('keeps repeated recipe clicks pending until its own ACK',()=>{
  const state=new CraftFeedback();
  state.begin('craft-1','metal');
  state.begin('craft-2','metal');
  state.resolve({reqid:'craft-1',ok:true});
  assert.equal(state.isPending('metal'),true);
  state.resolve({reqid:'craft-2',ok:true});
  assert.equal(state.isPending('metal'),false);
});

test('shows server rejection reason and never invents a successful craft',()=>{
  const state=new CraftFeedback();
  state.begin('craft-3','potion');
  assert.deepEqual(state.resolve({reqid:'craft-3',ok:false,reason:'не хватает сырья'}),
    {recipeId:'potion',ok:false,reason:'не хватает сырья'});
  assert.equal(state.notice.ok,false);
  assert.equal(state.notice.reason,'не хватает сырья');
});

test('cancel removes a request that could not enter the shared request gate',()=>{
  const state=new CraftFeedback();
  state.begin('craft-4','bow');
  assert.equal(state.cancel('craft-4'),true);
  assert.equal(state.isPending('bow'),false);
  assert.equal(state.resolve({reqid:'craft-4',ok:true}),null);
});


test('new attempt clears an older result notice',()=>{
  const state=new CraftFeedback();
  state.begin('craft-5','metal');
  state.resolve({reqid:'craft-5',ok:true});
  assert.ok(state.notice);
  state.begin('craft-6','sword');
  assert.equal(state.notice,null);
});

test('craft display reads ingredients from server stash and output from authoritative stores',()=>{
  const state=new CraftFeedback();
  const view=state.stockFor({needs:{ore:2,wood:1},gives:'metal'}, {
    stash:{ore:1,wood:3,metal:2},
    inv:{items:{metal:1}}
  });
  assert.deepEqual(view.needs,[
    {good:'ore',required:2,available:1},
    {good:'wood',required:1,available:3}
  ]);
  assert.deepEqual(view.output,{stash:2,inventory:1});
});

test('missing optional inventory fields stay unknown rather than looking like zero stock',()=>{
  const state=new CraftFeedback();
  const view=state.stockFor({needs:{ore:2},gives:'metal'},{});
  assert.deepEqual(view.needs,[{good:'ore',required:2,available:null}]);
  assert.deepEqual(view.output,{stash:null,inventory:null});
});

test('two rapid craft requests remain FIFO and each ACK resolves only its own recipe',()=>{
  const sent=[];
  const gate=new RequestGate(payload=>{sent.push(payload.reqid);return true;},6,100);
  const feedback=new CraftFeedback();
  gate.setConnection(1,0);
  const enqueueCraft=(reqid,item)=>{
    assert.equal(feedback.begin(reqid,item),true);
    assert.equal(gate.enqueue('Ремесло',{op:'craft',item,reqid},1),true);
  };
  enqueueCraft('craft-metal','metal');
  enqueueCraft('craft-sword','sword');
  assert.deepEqual(sent,['craft-metal']);
  const first={reqid:'craft-metal',ok:true};
  const firstNotice=feedback.resolve(first);
  assert.equal(firstNotice.recipeId,'metal');
  assert.equal(gate.accept(first,2).reqid,'craft-metal');
  assert.deepEqual(sent,['craft-metal','craft-sword']);
  assert.equal(feedback.isPending('sword'),true);
  const second={reqid:'craft-sword',ok:false,reason:'не хватает железа'};
  const secondNotice=feedback.resolve(second);
  assert.deepEqual(secondNotice,{recipeId:'sword',ok:false,reason:'не хватает железа'});
  assert.equal(gate.accept(second,3).reqid,'craft-sword');
  assert.equal(gate.pendingCount,0);
});
