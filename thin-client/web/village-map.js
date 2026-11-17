(function(root,factory){
  const api=factory();
  if(typeof module!=='undefined'&&module.exports)module.exports=api;
  if(root)root.PogranichyeVillageMap=api;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
'use strict';
const CELLS={grass:[0,0],path:[1,0],water:[3,0],crops:[0,1],planks:[1,1],tree:[0,2],cottage:[1,2],tavern:[2,2],forge:[3,2],market:[0,3],well:[1,3],barrel:[2,3]};
const LABELS={well:'площадь',tavern:'корчма',river:'река',woods:'лес',garden:'поля',smithy:'кузня',granary:'амбар',mine:'рудник',hearth:'очаг',market:'лавка'};
const ROLES={tavernkeeper:'#704a62',smith:'#59616a',farmer:'#667a42',herbalist:'#477b58',cook:'#9b6940',hunter:'#526044',fisher:'#4c7180'};
const FOES={hare:'#d9c9a8',fox:'#d77a3f',boar:'#49372a',wolf:'#858d91',bear:'#342820',bandit:'#a94b42'};
const num=v=>v===null||v===undefined||v===''?null:(Number.isFinite(Number(v))?Number(v):null);
function project(x,y,half,w,h){
  half=num(half);x=num(x);y=num(y);w=num(w);h=num(h);
  if(!(half>0)||x===null||y===null||!(w>0)||!(h>0))return null;
  const k=Math.min(w,h)/(2*half);
  return {x:(w-2*half*k)/2+(x+half)*k,y:(h-2*half*k)/2+(half-y)*k};
}
function unproject(px,py,half,w,h){
  half=num(half);px=num(px);py=num(py);w=num(w);h=num(h);
  if(!(half>0)||px===null||py===null||!(w>0)||!(h>0))return null;
  const k=Math.min(w,h)/(2*half),ox=(w-2*half*k)/2,oy=(h-2*half*k)/2;
  return {x:(px-ox)/k-half,y:half-(py-oy)/k};
}
function blocked(layout,x,y){
  const half=num(layout&&layout.world_half);
  if(!(half>0)||x < -half||x > half||y < -half||y > half)return true;
  for(const b of Array.isArray(layout.buildings)?layout.buildings:[])
    if(x>=Number(b.x0)&&x<=Number(b.x1)&&y>=Number(b.y0)&&y<=Number(b.y1))return true;
  const r=layout.river;
  return !!(r&&y>=Number(r.y0)&&y<=Number(r.y1)&&(x<Number(r.bridge_x0)||x>Number(r.bridge_x1)));
}
function hash(x,y){
  let h=(Math.imul(Math.round(x*10),374761393)+Math.imul(Math.round(y*10),668265263))|0;
  h=Math.imul(h^(h>>>13),1274126177);return (h^(h>>>16))>>>0;
}
function nearPoi(layout,x,y,ignore,radius){
  for(const [key,p] of Object.entries(layout.pois||{})){
    if(key===ignore||!p)continue;
    const px=num(p.x),py=num(p.y);
    if(px!==null&&py!==null&&Math.hypot(px-x,py-y)<radius)return true;
  }
  return false;
}
// v1.19: у каждого села своя раскладка. Сгенерированная (plan не starter) несёт угодья (zones) и
// дороги (roads) — рисуем по ним, а не по зашитым координатам стартового села.
function generated(layout){return !!(layout&&layout.plan&&layout.plan!=='starter'&&Array.isArray(layout.zones));}
function zonesOf(layout){
  return (Array.isArray(layout&&layout.zones)?layout.zones:[]).filter(z=>z&&[z.x0,z.y0,z.x1,z.y1].every(v=>num(v)!==null))
    .map(z=>({kind:String(z.kind||''),x0:Number(z.x0),y0:Number(z.y0),x1:Number(z.x1),y1:Number(z.y1)}));
}
function zoneDecorations(layout){
  const out=[];
  for(const z of zonesOf(layout)){
    if(z.kind==='forest'||z.kind==='copse'){
      for(let gx=z.x0+6;gx<=z.x1-4;gx+=12)for(let gy=z.y0+6;gy<=z.y1-4;gy+=13){
        const seed=hash(gx,gy);if(seed%5===0)continue;
        const x=gx+(seed>>>3)%5-2,y=gy+(seed>>>7)%5-2;
        if(!blocked(layout,x,y)&&!nearPoi(layout,x,y,'',7))out.push({kind:'tree',x,y,size:20+seed%9});
      }
    }else if(z.kind==='field'){
      for(let gx=z.x0+6;gx<=z.x1-4;gx+=9)for(let gy=z.y0+6;gy<=z.y1-4;gy+=8){
        if(hash(gx,gy)%3===0)continue;
        if(!blocked(layout,gx,gy)&&!nearPoi(layout,gx,gy,'',5))out.push({kind:'crop',x:gx,y:gy,size:18});
      }
    }
  }
  const tavern=layout.pois&&layout.pois.tavern;
  if(tavern&&!blocked(layout,Number(tavern.x)+4,Number(tavern.y)))out.push({kind:'barrel',x:Number(tavern.x)+4,y:Number(tavern.y),size:18});
  const woods=layout.pois&&layout.pois.woods;
  if(woods&&!blocked(layout,Number(woods.x)+5,Number(woods.y)-4))out.push({kind:'wood',x:Number(woods.x)+5,y:Number(woods.y)-4,size:22});
  return out;
}
function decorations(layout){
  const out=[];if(!layout||Number(layout.version)!==2||!(num(layout.world_half)>0))return out;
  if(generated(layout))return zoneDecorations(layout);
  for(let gx=-138;gx<=-76;gx+=12)for(let gy=-65;gy<=65;gy+=13){
    const seed=hash(gx,gy);if(seed%5===0)continue;
    const x=gx+(seed>>>3)%5-2,y=gy+(seed>>>7)%5-2;
    if(!blocked(layout,x,y)&&!nearPoi(layout,x,y,'woods',7))out.push({kind:'tree',x,y,size:20+seed%9});
  }
  const garden=layout.pois&&layout.pois.garden;
  if(garden&&num(garden.x)!==null&&num(garden.y)!==null)
    for(let col=-2;col<=2;col++)for(let row=-2;row<=2;row++){
      const x=Number(garden.x)+col*7,y=Number(garden.y)+row*6;
      if(!blocked(layout,x,y))out.push({kind:'crop',x,y,size:20});
    }
  const tavern=layout.pois&&layout.pois.tavern;
  if(tavern)for(const [dx,dy] of [[-12,-3],[10,-4]]){
    const x=Number(tavern.x)+dx,y=Number(tavern.y)+dy;
    if(!blocked(layout,x,y))out.push({kind:'barrel',x,y,size:18});
  }
  const woods=layout.pois&&layout.pois.woods;
  if(woods&&!blocked(layout,Number(woods.x)+8,Number(woods.y)-4))
    out.push({kind:'wood',x:Number(woods.x)+8,y:Number(woods.y)-4,size:22});
  return out;
}
function validPos(p){return p&&num(p.x)!==null&&num(p.y)!==null;}
function buildScene(layout,snapshot,selected){
  if(!layout||!(num(layout.world_half)>0))return null;
  const buildings=(Array.isArray(layout.buildings)?layout.buildings:[]).filter(b=>[b.x0,b.y0,b.x1,b.y1].every(v=>num(v)!==null))
    .map(b=>({kind:String(b.kind||'house'),x0:Number(b.x0),y0:Number(b.y0),x1:Number(b.x1),y1:Number(b.y1)}));
  const pois=Object.entries(layout.pois||{}).filter(([,p])=>validPos(p)).map(([key,p])=>({key,x:Number(p.x),y:Number(p.y),label:LABELS[key]||key}));
  const place=snapshot&&snapshot.place||{},me=snapshot&&snapshot.me||{};
  const neighbors=(Array.isArray(place.neighbors)?place.neighbors:[]).filter(n=>validPos(n.pos)).map(n=>({
    x:Number(n.pos.x),y:Number(n.pos.y),facing:num(n.pos.facing)||0,player:!!n.player,role:String(n.role||''),activity:String(n.act_code||n.act||'')
  }));
  const foes=(Array.isArray(place.foes)?place.foes:[]).filter(f=>num(f.x)!==null&&num(f.y)!==null).map(f=>({
    id:f.id,kind:String(f.kind||'beast'),x:Number(f.x),y:Number(f.y),state:String(f.state||''),aware:!!f.aware,selected:f.id===selected
  }));
  const river=layout.river||{};
  return {version:Number(layout.version)||0,half:Number(layout.world_half),villageHalf:num(layout.village_half)||0,buildings,pois,neighbors,foes,
    player:validPos(me.pos)?{x:Number(me.pos.x),y:Number(me.pos.y),facing:num(me.pos.facing)||0}:null,
    river:{y0:num(river.y0),y1:num(river.y1),bridgeX0:num(river.bridge_x0),bridgeX1:num(river.bridge_x1)},
    generated:generated(layout),zones:zonesOf(layout),
    roads:(Array.isArray(layout.roads)?layout.roads:[]).filter(r=>Array.isArray(r)&&r.length===4&&r.every(v=>num(v)!==null)).map(r=>r.map(Number)),
    decorations:decorations(layout)};
}
function sprite(ctx,atlas,name,x,y,w,h,fallback){
  const cell=CELLS[name];
  if(atlas&&atlas.complete&&Number(atlas.naturalWidth)>=384&&cell)
    ctx.drawImage(atlas,cell[0]*96,cell[1]*96,96,96,x,y,w,h);
  else{ctx.fillStyle=fallback||'#71834b';ctx.fillRect(x,y,w,h);}
}
function background(ctx,s,atlas,w,h){
  if(atlas&&atlas.complete&&Number(atlas.naturalWidth)>=384)
    for(let y=0;y<h;y+=32)for(let x=0;x<w;x+=32)sprite(ctx,atlas,'grass',x,y,32,32);
  else{ctx.fillStyle='#71834b';ctx.fillRect(0,0,w,h);}
  if(s.generated){
    const tint={forest:'rgba(42,74,45,.42)',copse:'rgba(42,74,45,.34)',field:'rgba(196,170,84,.30)',meadow:'rgba(150,178,96,.22)',rock:'rgba(110,104,96,.45)',pond:'#477d83'};
    for(const z of s.zones){
      const a=project(Math.max(z.x0,-s.half),Math.min(z.y1,s.half),s.half,w,h),b=project(Math.min(z.x1,s.half),Math.max(z.y0,-s.half),s.half,w,h);
      if(a&&b&&tint[z.kind]){ctx.fillStyle=tint[z.kind];ctx.fillRect(a.x,a.y,b.x-a.x,b.y-a.y);}
    }
    if(s.villageHalf>0){
      const a=project(-s.villageHalf,s.villageHalf,s.half,w,h),b=project(s.villageHalf,-s.villageHalf,s.half,w,h);
      if(a&&b){ctx.fillStyle='rgba(190,171,112,.13)';ctx.fillRect(a.x,a.y,b.x-a.x,b.y-a.y);}
    }
    // дороги — настоящий граф сервера (по нему ходят жители)
    ctx.lineCap='round';ctx.lineJoin='round';
    for(const pass of [['#765b3d',7],['#b99a6c',4]]){
      ctx.strokeStyle=pass[0];ctx.lineWidth=pass[1];ctx.beginPath();
      for(const r of s.roads){const a=project(r[0],r[1],s.half,w,h),b=project(r[2],r[3],s.half,w,h);if(a&&b){ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);}}
      ctx.stroke();
    }
  }else if(s.version===2){
    const edge=project(-70,0,s.half,w,h);
    if(edge){ctx.fillStyle='rgba(42,74,45,.42)';ctx.fillRect(0,0,edge.x,h);}
    if(s.villageHalf>0){
      const a=project(-s.villageHalf,s.villageHalf,s.half,w,h),b=project(s.villageHalf,-s.villageHalf,s.half,w,h);
      if(a&&b){ctx.fillStyle='rgba(190,171,112,.13)';ctx.fillRect(a.x,a.y,b.x-a.x,b.y-a.y);}
    }
    // Decorative trails only: /api/layout does not expose the authoritative nav graph.
    const trails=[
    [[-33,12],[-24,5],[-14,-1],[0,-4]],[[0,-4],[14,-3],[22,4],[29,14]],
    [[0,-4],[-1,12],[-2,28]],[[0,-4],[12,-13],[25,-23],[33,-32]],
    [[33,-32],[50,-27],[70,-20]],[[0,-4],[0,-40],[0,-62],[0,-74]],
    [[0,-4],[-38,0],[-65,7],[-95,10]],[[29,14],[55,27],[90,40]]
    ];
    ctx.lineCap='round';ctx.lineJoin='round';
    for(const trail of trails){
      ctx.beginPath();trail.forEach((v,i)=>{const p=project(v[0],v[1],s.half,w,h);if(p){if(i===0)ctx.moveTo(p.x,p.y);else ctx.lineTo(p.x,p.y);}});
      ctx.strokeStyle='#765b3d';ctx.lineWidth=10;ctx.stroke();ctx.strokeStyle='#b99a6c';ctx.lineWidth=6;ctx.stroke();
    }
  }
  if(s.river.y0!==null&&s.river.y1!==null&&s.river.y1>-s.half&&s.river.y0<s.half){
    const top=project(0,s.river.y1,s.half,w,h),bottom=project(0,s.river.y0,s.half,w,h);
    if(top&&bottom){
      for(let y=top.y;y<bottom.y;y+=26)for(let x=0;x<w;x+=26)sprite(ctx,atlas,'water',x,y,26,26,'#477d83');
      const left=project(s.river.bridgeX0,s.river.y1,s.half,w,h),right=project(s.river.bridgeX1,s.river.y0,s.half,w,h);
      if(left&&right){
        ctx.fillStyle='#5b402c';ctx.fillRect(left.x-2,top.y-2,right.x-left.x+4,bottom.y-top.y+4);
        for(let y=top.y+2;y<bottom.y;y+=5){ctx.strokeStyle='#c39a60';ctx.lineWidth=1.5;ctx.beginPath();ctx.moveTo(left.x,y);ctx.lineTo(right.x,y);ctx.stroke();}
      }
    }
  }
}
function drawBuildings(ctx,s,atlas,w,h){
  for(const b of s.buildings){
    const c=project((b.x0+b.x1)/2,(b.y0+b.y1)/2,s.half,w,h),a=project(b.x0,b.y1,s.half,w,h),z=project(b.x1,b.y0,s.half,w,h);
    if(!c||!a||!z)continue;
    const bw=Math.max(27,(z.x-a.x)*1.18),bh=Math.max(24,(z.y-a.y)*1.24);
    ctx.fillStyle='rgba(27,25,20,.30)';ctx.fillRect(c.x-bw/2+3,c.y-bh/2+5,bw,bh);
    const key=b.kind==='tavern'?'tavern':b.kind==='smithy'?'forge':b.kind==='market'?'market':'cottage';
    sprite(ctx,atlas,key,c.x-bw/2,c.y-bh/2,bw,bh,'#9b6940');
  }
}
function drawProps(ctx,s,atlas,w,h){
  for(const d of s.decorations){
    const p=project(d.x,d.y,s.half,w,h);if(!p)continue;
    if(d.kind==='tree'){
      ctx.fillStyle='rgba(25,28,19,.25)';ctx.beginPath();ctx.arc(p.x+2,p.y+d.size*.22,d.size*.34,0,Math.PI*2);ctx.fill();
      sprite(ctx,atlas,'tree',p.x-d.size/2,p.y-d.size*.82,d.size,d.size,'#3f613c');
    }else if(d.kind==='crop')sprite(ctx,atlas,'crops',p.x-11,p.y-8,22,16,'#8d7044');
    else if(d.kind==='barrel')sprite(ctx,atlas,'barrel',p.x-8,p.y-10,16,20,'#81512f');
    else if(d.kind==='wood')sprite(ctx,atlas,'planks',p.x-12,p.y-5,24,10,'#95623a');
  }
}
function drawWell(ctx,s,atlas,w,h){
  const poi=s.pois.find(p=>p.key==='well');if(!poi)return;
  const p=project(poi.x,poi.y,s.half,w,h);if(p)sprite(ctx,atlas,'well',p.x-15,p.y-17,30,34,'#85877b');
}
function drawLabels(ctx,s,w,h){
  ctx.font='bold 12px Georgia,serif';ctx.textAlign='center';ctx.textBaseline='middle';
  for(const poi of s.pois){
    const p=project(poi.x,poi.y,s.half,w,h);if(!p)continue;
    ctx.lineWidth=3;ctx.strokeStyle='rgba(32,28,22,.78)';
    if(ctx.strokeText)ctx.strokeText(poi.label,p.x,p.y-18);
    ctx.fillStyle='#fff0cf';ctx.fillText(poi.label,p.x,p.y-18);
  }
}
function drawPerson(ctx,n,p){
  ctx.save();ctx.translate(p.x,p.y);ctx.rotate(-n.facing*Math.PI/180);
  ctx.fillStyle='rgba(24,22,18,.34)';ctx.beginPath();ctx.arc(1,5,6,0,Math.PI*2);ctx.fill();
  ctx.fillStyle=n.player?'#6ab0ff':(ROLES[n.role.toLowerCase()]||'#9b7952');ctx.fillRect(-4,-1,8,10);
  ctx.fillStyle=n.player?'#f1d7a6':'#e5b989';ctx.beginPath();ctx.arc(0,-5,3.5,0,Math.PI*2);ctx.fill();
  ctx.fillStyle='#4d3828';ctx.fillRect(-3.5,-9,7,2.5);ctx.fillStyle='#45372a';ctx.fillRect(-3,8,2,4);ctx.fillRect(1,8,2,4);
  if(n.player){ctx.strokeStyle='#f5d58e';ctx.lineWidth=1.5;ctx.beginPath();ctx.arc(0,1,8,0,Math.PI*2);ctx.stroke();}
  ctx.restore();
}
function drawFoe(ctx,f,p){
  if(f.state==='dead'){ctx.strokeStyle='rgba(30,25,20,.65)';ctx.lineWidth=2;ctx.strokeRect(p.x-3,p.y-3,6,6);return;}
  const size=f.kind==='bear'?9:f.kind==='boar'?7:5.5;
  ctx.fillStyle='rgba(22,20,17,.32)';ctx.beginPath();ctx.arc(p.x+1,p.y+3,size+1,0,Math.PI*2);ctx.fill();
  ctx.fillStyle=FOES[f.kind]||'#c96b58';ctx.beginPath();ctx.arc(p.x,p.y,size,0,Math.PI*2);ctx.fill();
  ctx.beginPath();ctx.arc(p.x+size*.65,p.y-size*.42,size*.48,0,Math.PI*2);ctx.fill();
  if(f.aware||f.selected){ctx.strokeStyle=f.selected?'#ffe9a8':'#ff5a44';ctx.lineWidth=2.5;ctx.beginPath();ctx.arc(p.x,p.y,size+4,0,Math.PI*2);ctx.stroke();}
}
function drawPlayer(ctx,p){
  ctx.save();ctx.translate(p.x,p.y);ctx.rotate(-p.facing*Math.PI/180);
  ctx.fillStyle='#6ab0ff';ctx.strokeStyle='#fff';ctx.lineWidth=1.5;
  ctx.beginPath();ctx.moveTo(10,0);ctx.lineTo(-6,6);ctx.lineTo(-6,-6);ctx.closePath();ctx.fill();ctx.stroke();ctx.restore();
}
function viewProject(x,y,half,w,h,view){
  const p=project(x,y,half,w,h);if(!p)return null;
  const z=Math.max(1,Math.min(4,num(view&&view.zoom)||1)),px=num(view&&view.panX)||0,py=num(view&&view.panY)||0;
  return {x:w/2+(p.x-w/2)*z+px,y:h/2+(p.y-h/2)*z+py};
}
function viewUnproject(px,py,half,w,h,view){
  const z=Math.max(1,Math.min(4,num(view&&view.zoom)||1)),ox=num(view&&view.panX)||0,oy=num(view&&view.panY)||0;
  return unproject((px-w/2-ox)/z+w/2,(py-h/2-oy)/z+h/2,half,w,h);
}
function draw(canvas,layout,snapshot,atlas,selected,view){
  if(!canvas||typeof canvas.getContext!=='function')return false;
  const s=buildScene(layout,snapshot,selected);if(!s)return false;
  const ctx=canvas.getContext('2d');if(!ctx)return false;
  const w=canvas.width,h=canvas.height;ctx.imageSmoothingEnabled=false;
  const z=Math.max(1,Math.min(4,num(view&&view.zoom)||1)),px=num(view&&view.panX)||0,py=num(view&&view.panY)||0;
  ctx.save();ctx.translate(w/2+px,h/2+py);ctx.scale(z,z);ctx.translate(-w/2,-h/2);
  background(ctx,s,atlas,w,h);drawProps(ctx,s,atlas,w,h);drawBuildings(ctx,s,atlas,w,h);drawWell(ctx,s,atlas,w,h);drawLabels(ctx,s,w,h);
  for(const n of s.neighbors){const p=project(n.x,n.y,s.half,w,h);if(p)drawPerson(ctx,n,p);}
  for(const f of s.foes){const p=project(f.x,f.y,s.half,w,h);if(p)drawFoe(ctx,f,p);}
  if(s.player){const p=project(s.player.x,s.player.y,s.half,w,h);if(p)drawPlayer(ctx,s.player,p);}
  const k=Math.min(w,h)/(2*s.half),ox=(w-2*s.half*k)/2,oy=(h-2*s.half*k)/2;
  ctx.strokeStyle='rgba(241,229,201,.72)';ctx.lineWidth=2;ctx.strokeRect(ox+1,oy+1,s.half*2*k-2,s.half*2*k-2);
  ctx.restore();
  return true;
}
return {project,unproject,viewProject,viewUnproject,isBlocked:blocked,buildDecorations:decorations,buildScene,draw};
});
