'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const VillageMap=require('../village-map.js');
const layout={
  version:2,world_half:150,village_half:50,
  buildings:[
    {name:'Корчма',kind:'tavern',x0:-40,y0:14,x1:-26,y1:26},
    {name:'Кузница',kind:'smithy',x0:22,y0:16,x1:36,y1:28},
    {name:'Амбар',kind:'granary',x0:-10,y0:30,x1:6,y1:42},
    {name:'Лавка',kind:'market',x0:-6,y0:6,x1:8,y1:16}
  ],
  river:{y0:-90,y1:-78,bridge_x0:-6,bridge_x1:6},
  pois:{tavern:{x:-33,y:12},smithy:{x:29,y:14},granary:{x:-2,y:28},market:{x:1,y:4},
    well:{x:0,y:-4},garden:{x:70,y:-20},river:{x:0,y:-74},woods:{x:-95,y:10},mine:{x:90,y:40}}
};
test('north-up projection round-trips world corners and centre',()=>{
  assert.deepEqual(VillageMap.project(-150,150,150,640,640),{x:0,y:0});
  assert.deepEqual(VillageMap.project(150,-150,150,640,640),{x:640,y:640});
  assert.deepEqual(VillageMap.project(0,0,150,640,640),{x:320,y:320});
  const p=VillageMap.unproject(188,412,150,640,640);
  assert.ok(Math.abs(p.x+61.875)<1e-9);assert.ok(Math.abs(p.y+43.125)<1e-9);
});
test('decorations are deterministic and never occupy blocked server geometry',()=>{
  const a=VillageMap.buildDecorations(layout),b=VillageMap.buildDecorations(layout);
  assert.deepEqual(a,b);assert.ok(a.some(p=>p.kind==='tree'));assert.ok(a.some(p=>p.kind==='crop'));
  for(const p of a){assert.equal(VillageMap.isBlocked(layout,p.x,p.y),false);assert.ok(Math.abs(p.x)<=150&&Math.abs(p.y)<=150);}
});
test('live markers use server positions and never copy hidden NPC names',()=>{
  const snapshot={me:{pos:{x:4,y:-8,facing:90}},place:{
    neighbors:[{id:'n1',known:false,name:'secret',description:'житель',role:'smith',pos:{x:12,y:5,facing:180}},{id:'n2',role:'farmer'}],
    foes:[{id:'fox-1',kind:'fox',x:-22,y:31,aware:true},{id:'bad',kind:'wolf',x:'bad',y:0}]
  }};
  const s=VillageMap.buildScene(layout,snapshot,'fox-1');
  assert.deepEqual(s.player,{x:4,y:-8,facing:90});assert.equal(s.neighbors.length,1);assert.equal(s.foes.length,1);
  assert.deepEqual(s.neighbors[0],{x:12,y:5,facing:180,player:false,role:'smith',activity:''});
  assert.equal(s.foes[0].selected,true);assert.equal('name' in s.neighbors[0],false);
});
test('null coordinates are not mistaken for world origin',()=>{
  const s=VillageMap.buildScene(layout,{place:{
    neighbors:[{role:'smith',pos:{x:null,y:4}},{role:'farmer',pos:{x:2,y:null}}],
    foes:[{id:'null-foe',kind:'fox',x:null,y:0}]
  },me:{pos:{x:null,y:0}}},null);
  assert.equal(s.neighbors.length,0);assert.equal(s.foes.length,0);assert.equal(s.player,null);
});
test('missing optional state is safe',()=>{
  const s=VillageMap.buildScene(layout,{place:{},me:{}},null);
  assert.equal(s.neighbors.length,0);assert.equal(s.foes.length,0);assert.equal(s.player,null);
});
test('renderer draws atlas sprites and server entities without browser globals',()=>{
  const calls={drawImage:0,fillRect:0,fillText:0,arc:0,strokeRect:0},ctx={};
  for(const name of ['drawImage','fillRect','fillText','strokeText','strokeRect','beginPath','arc','fill','stroke','moveTo','lineTo','closePath','save','restore','translate','rotate'])
    ctx[name]=()=>{if(calls[name]!==undefined)calls[name]++;};
  const canvas={width:640,height:640,getContext:()=>ctx},atlas={complete:true,naturalWidth:384};
  const snapshot={me:{pos:{x:0,y:0,facing:0}},place:{neighbors:[{role:'farmer',pos:{x:8,y:4}}],foes:[{id:'boar-1',kind:'boar',x:-18,y:10,aware:true}]}};
  assert.equal(VillageMap.draw(canvas,layout,snapshot,atlas,'boar-1'),true);
  assert.ok(calls.drawImage>20);assert.ok(calls.fillText>0);assert.ok(calls.arc>0);assert.ok(calls.strokeRect>0);
});


test('zoom and pan projection round-trips world coordinates',()=>{
  const view={zoom:2.5,panX:73,panY:-41};
  const screen=VillageMap.viewProject(-42,27,150,640,640,view);
  const world=VillageMap.viewUnproject(screen.x,screen.y,150,640,640,view);
  assert.ok(Math.abs(world.x+42)<1e-9);
  assert.ok(Math.abs(world.y-27)<1e-9);
});

test('zoom viewport keeps map centre fixed at one-to-one scale',()=>{
  const view={zoom:1,panX:0,panY:0};
  const p=VillageMap.viewProject(0,0,150,640,640,view);
  assert.deepEqual(p,{x:320,y:320});
});
