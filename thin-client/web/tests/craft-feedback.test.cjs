'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const CraftFeedback=require('../craft-feedback.js');

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
