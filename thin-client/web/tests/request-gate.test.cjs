'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const RequestGate=require('../request-gate.js');

test('serializes result-bearing actions and dispatches FIFO after matching ACK',()=>{
  const sent=[];
  const gate=new RequestGate(p=>{sent.push(p.reqid);return true;},6,100);
  gate.setConnection(1,0);
  assert.equal(gate.enqueue('first',{op:'craft',reqid:'a'},1),true);
  assert.equal(gate.enqueue('second',{op:'act',reqid:'b'},2),true);
  assert.deepEqual(sent,['a']);
  assert.equal(gate.accept({reqid:'wrong',ok:true},3),null);
  assert.deepEqual(sent,['a']);
  assert.equal(gate.accept({reqid:'a',ok:true},4).label,'first');
  assert.deepEqual(sent,['a','b']);
  assert.equal(gate.accept({reqid:'b',ok:false,reason:'no'},5).label,'second');
  assert.equal(gate.pendingCount,0);
});

test('reconnect replays the exact same reqid and does not release the queue',()=>{
  const sent=[];
  const gate=new RequestGate(p=>{sent.push(p.reqid);return true;},6,100);
  gate.setConnection(1,0);
  gate.enqueue('fish',{op:'act',action:'fish',reqid:'stable-id'},1);
  gate.enqueue('craft',{op:'craft',reqid:'next-id'},2);
  gate.disconnect();
  gate.setConnection(2,50);
  assert.deepEqual(sent,['stable-id','stable-id']);
  assert.equal(gate.activeItem.reqid,'stable-id');
  assert.equal(gate.pendingCount,2);
  gate.accept({reqid:'stable-id',ok:true},60);
  assert.deepEqual(sent,['stable-id','stable-id','next-id']);
});

test('matching ACK in first reconnect frame avoids unnecessary replay',()=>{
  const sent=[];
  const gate=new RequestGate(p=>{sent.push(p.reqid);return true;},6,100);
  gate.setConnection(1,0);
  gate.enqueue('one',{reqid:'one'},1);
  gate.setConnection(2,50,'one');
  assert.deepEqual(sent,['one']);
  assert.equal(gate.accept({reqid:'one',ok:true},51).reqid,'one');
});

test('timeout retries the same request id and keeps later actions blocked',()=>{
  const sent=[];
  const gate=new RequestGate(p=>{sent.push(p.reqid);return true;},6,100);
  gate.setConnection(1,0);
  gate.enqueue('one',{reqid:'same'},1);
  gate.enqueue('two',{reqid:'next'},2);
  assert.equal(gate.tick(102).retried,true);
  assert.deepEqual(sent,['same','same']);
  const timeout=gate.tick(203);
  assert.equal(timeout.retried,false);
  assert.equal(gate.activeItem.reqid,'same');
  assert.equal(gate.pendingCount,2);
  assert.deepEqual(sent,['same','same']);
});

test('deduplicates queued ids and enforces bounded capacity',()=>{
  const sent=[];
  const gate=new RequestGate(p=>{sent.push(p.reqid);return true;},2,100);
  gate.setConnection(1,0);
  assert.equal(gate.enqueue('one',{reqid:'1'},1),true);
  assert.equal(gate.enqueue('one duplicate',{reqid:'1'},2),true);
  assert.equal(gate.enqueue('two',{reqid:'2'},3),true);
  assert.equal(gate.enqueue('overflow',{reqid:'3'},4),false);
  assert.equal(gate.pendingCount,2);
  assert.deepEqual(sent,['1']);
});

test('does not discard an action while disconnected before its first send',()=>{
  const sent=[];
  const gate=new RequestGate(p=>{sent.push(p.reqid);return true;},2,100);
  assert.equal(gate.enqueue('queued',{reqid:'offline'},0),true);
  assert.equal(sent.length,0);
  gate.setConnection(7,10);
  assert.deepEqual(sent,['offline']);
});
