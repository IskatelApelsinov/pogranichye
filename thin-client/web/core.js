// Ядро игры v1.10 для тонкого клиента: дорога, бой, плетения, просьбы, ремесло, отряд, рынок.
// Формат прежний (ADR 0002): всё тапами, без ходьбы. Сервер авторитетен: клиент шлёт намерение и
// рисует только подтверждённое кадром. Подключается из index.html после основного скрипта.
'use strict';

const RU = {
  axe:'топор', sword:'меч', bow:'лук', rod:'удочка', torch:'факел', lantern:'фонарь', knife:'нож',
  cloak:'плащ', boots:'сапоги', hat:'шапка', jerkin:'кожаная куртка', shield:'щит', arrow:'стрелы',
  bandage:'повязка', potion:'снадобье', stew:'уха', roast:'жаркое',
  bread:'хлеб', fish:'рыба', wood:'дерево', meat:'мясо', hide:'шкура', wheat:'зерно', veg:'овощи',
  ore:'руда', metal:'железо', leather:'кожа', herb:'травы', flour:'мука', game:'дичь', cheese:'сыр',
  egg:'яйца', milk:'молоко', wool:'шерсть', cloth:'сукно', tools:'инструмент', hay:'сено',
  feather:'перо', flax:'лён', linen:'холст', remedy:'лекарство', honey:'мёд', mead:'медовуха',
};
const ru = k => RU[k] || k;
const HAND = ['axe','sword','knife','bow','rod','torch','lantern'];
const WEAR = ['cloak','boots','hat','jerkin','shield'];
const USE  = ['stew','roast','potion','bandage'];
const EAT  = ['bread','fish','meat','cheese','veg','game','egg'];
const esc = t => String(t ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

// перерисовываем карточку, только если её содержимое изменилось (иначе тап попадает в исчезнувшую кнопку)
const htmlCache = {};
function setHTML(id, html){
  const el = document.getElementById(id);
  if(!el || htmlCache[id] === html) return;
  htmlCache[id] = html; el.innerHTML = html;
}
function show(id, on){ const el=document.getElementById(id); if(el) el.classList.toggle('hidden', !on); }
const sbtn = (label, js, dis, cls) =>
  `<button class="${cls||''}" style="width:auto;padding:6px 10px" ${dis?'disabled':''} onclick="${js}">${label}</button>`;

function coreRender(s){
  const me = s.me || {};
  const road = !!me.journey;
  show('villagecard', !road); show('locscreen', !road); show('fightcard', !road);
  show('craftcard', !road); show('roadcard', road); show('mapcard', !road);
  drawRoad(s); drawFeed(s); drawFight(s); drawWeave(s); drawQuests(s); drawCraft(s); drawParty(s); drawLiveMap(s); drawStats(s);
}

// ---------- лента событий ----------
let feedSeq = 0; const feedLog = [];
function drawFeed(s){
  ((s.me||{}).feed||[]).forEach(it => { if(it.seq > feedSeq){ feedSeq = it.seq; feedLog.push(it); } });
  while(feedLog.length > 6) feedLog.shift();
  const ico = {hit:'⚔', miss:'💨', kill:'💀', hurt:'🩸', block:'🛡', dodge:'↩', ko:'🌑', weave:'✨', backfire:'💥',
               learn:'📖', loot:'🎒', road:'🛤', quest:'📜', craft:'🔨', party:'🤝'};
  show('feedcard', feedLog.length > 0);
  setHTML('feedcard', '<b class="sub">что произошло</b>' + feedLog.slice().reverse().map((it,i) =>
    `<div style="font-size:14px;opacity:${1-i*0.13}">${ico[it.kind]||'•'} ${esc(it.text)}${it.dmg?` <span class="sub">(${it.dmg})</span>`:''}</div>`).join(''));
}

// ---------- дорога ----------
const OPT = {fight:'⚔ Драться', pay:'💰 Откупиться', flee:'🏃 Бежать', scare:'🔥 Отпугнуть', talk:'💬 Поговорить',
             trade:'🍞 Купить хлеба', pass:'➡ Пройти мимо', rest:'🔥 Отдохнуть у огня', gather:'🌿 Набрать трав'};
function drawRoad(s){
  const j = (s.me||{}).journey; if(!j) return;
  let h = `<div class="row between"><b>🛤 Дорога в ${esc(j.to_name)}</b><span class="sub">ещё ~${j.eta} с</span></div>`;
  h += `<div class="bar"><div style="width:${Math.round(j.progress*100)}%;background:#c9a24a"></div></div>`;
  h += `<div style="color:#ead9b0;margin:6px 0">${esc(j.scene)}</div>`;
  if(j.note) h += `<div class="sub">${esc(j.note)}</div>`;
  if(j.event){
    h += `<div class="mg"><div style="margin-bottom:8px">${esc(j.event.text)}</div><div class="acts">`;
    if(j.follow) h += '<span class="sub">Решает вожак отряда…</span>';
    else j.event.options.forEach(o => { h += sbtn(OPT[o]||o, `send({op:'journey',action:'choose',choice:'${o}'})`, false, o==='fight'?'primary':''); });
    h += '</div></div>';
  }
  setHTML('roadcard', h);
}

// карта: родной край — пешком по тракту; через горы — только врата
function drawTravel(s){
  const me = s.me||{}, world = me.world||[], hereId = String((s.place||{}).id), rg = me.region||{nodes:[],roads:[]};
  const here = world.find(p => String(p.id) === hereId) || {outer:false};
  const other = world.filter(p => p.outer !== here.outer);
  const ST = {here:'ты здесь', visited:'бывал', known:'слышал', unknown:'неведомо'};
  let h = `<b class="sub">🗺 Карта края</b> <span class="sub">(видно только то, где был и о чём слышал; остальное — туман)</span>`;
  h += '<canvas id="regionmap" width="640" height="360" style="width:100%;height:auto;border-radius:6px;border:1px solid #5c4a35;margin:6px 0;background:#2b3327"></canvas>';
  h += '<div class="acts">';
  (rg.nodes||[]).filter(n => n.state !== 'here').forEach(n => {
    const road = (rg.roads||[]).find(r => (r.a===n.id&&r.b===hereId)||(r.b===n.id&&r.a===hereId));
    h += sbtn(`${n.tier==='city'?'🏰':n.state==='unknown'?'❔':'🏘'} ${esc(n.name||'куда ведёт тракт')} <span class="sub">· ${ST[n.state]}${road?' · '+road.len+' вёрст':''}</span>`,
              `send({op:'journey',target:'${n.id}'})`, !!me.journey);
  });
  h += '</div>';
  if(other.length){
    h += `<div style="margin-top:10px"><b class="sub">🌀 Врата через горы</b> <span class="sub">(${here.outer?'домой, в родной край':'во внешний мир'} — пешком не дойти)</span><div class="acts">`;
    other.slice(0,12).forEach(p => { h += sbtn(`${p.tier==='city'?'🏰':'🏘'} ${esc(p.name)}`, `send({op:'travel',target:'${p.id}'})`, !!me.journey); });
    h += '</div></div>';
  }
  setHTML('travelcard', h);
  const cv = document.getElementById('regionmap'); if(!cv || !(rg.nodes||[]).length) return;
  const g = cv.getContext('2d'), xs = rg.nodes.map(n=>n.x), ys = rg.nodes.map(n=>n.y);
  const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys), pad = 60;
  const k = Math.min((cv.width-2*pad)/Math.max(1,x1-x0), (cv.height-2*pad)/Math.max(1,y1-y0));
  const X = x => pad + (x-x0)*k, Y = y => cv.height - pad - (y-y0)*k, by = {}; rg.nodes.forEach(n => by[n.id] = n);
  g.clearRect(0,0,cv.width,cv.height); g.lineWidth = 3;
  (rg.roads||[]).forEach(r => { const a = by[r.a], b = by[r.b]; if(!a||!b) return;
    g.strokeStyle = (a.state==='unknown'||b.state==='unknown') ? '#6b6250' : '#b89a6a'; g.setLineDash((a.state==='unknown'||b.state==='unknown')?[6,6]:[]);
    g.beginPath(); g.moveTo(X(a.x),Y(a.y)); g.lineTo(X(b.x),Y(b.y)); g.stroke(); });
  g.setLineDash([]); g.textAlign = 'center'; g.font = 'bold 16px Georgia';
  rg.nodes.forEach(n => { g.fillStyle = n.state==='here'?'#6ab0ff':n.state==='visited'?'#e8dcc0':n.state==='known'?'#b9a57c':'#5a5548';
    g.beginPath(); g.arc(X(n.x),Y(n.y), n.tier==='city'?11:8, 0, 7); g.fill();
    g.fillStyle = '#fff3d6'; g.fillText(n.name||'?', X(n.x), Y(n.y)-14); });
}

// характеристики, хмель, дар
function drawStats(s){
  const me = s.me||{}, st = me.stats; if(!st){ show('statcard', false); return; }
  show('statcard', true);
  const N = {strength:'сила', agility:'ловкость', endurance:'выносливость', intellect:'ум', perception:'восприятие', will:'воля'};
  const wv = me.weave||{};
  let h = '<b class="sub">каков ты сам</b> <span class="sub">(растёт от дела, потолка нет)</span><div style="margin-top:4px">' +
    Object.keys(N).map(k => `<span class="tag">${N[k]} ${st[k]}</span>`).join('') + '</div>';
  if(me.drunk > 0) h += `<div class="sub" style="margin-top:4px">🍺 хмель ${me.drunk}${me.drunk>30?' — руки и мысли неточны':''}</div>`;
  h += `<div class="sub" style="margin-top:4px">${wv.gift ? '✨ В тебе есть дар: ум удержит и сложный узор.' : 'Дара нет: по силам лишь самые простые узоры. Учиться можно в местах силы и у магов.'}</div>`;
  const bag = me.bag, hz = me.horse, stb = (s.place||{}).stable, stash = me.stash||{};
  if(bag) h += `<div class="sub" style="margin-top:6px">🎒 сума: ${bag.load} из ${bag.cap}${bag.over?' — <b style="color:#c96b58">перегруз: идёшь медленно, сложи лишнее на лошадь или продай</b>':''}</div>`;
  if(hz){
    h += `<div style="margin-top:6px">🐴 <b>${esc(hz.name)}</b> <span class="sub">· силы ${hz.stam}${hz.mounted?' · ты в седле':''}${hz.here?'':' · осталась в другом селе'} · седельная сумка ${hz.bag_load}/${hz.bag_cap}</span></div>`;
    h += '<div>' + Object.entries(hz.bag||{}).map(([g,v]) => `<span class="tag">${ru(g)} ×${v}</span>`).join('') + '</div><div class="acts" style="grid-template-columns:1fr 1fr 1fr">';
    if(hz.here){
      h += sbtn(hz.mounted?'⬇ Слезть':'⬆ В седло', `send({op:'horse',action:'${hz.mounted?'dismount':'mount'}'})`) + sbtn('🌾 Покормить', `send({op:'horse',action:'feed'})`);
      Object.entries(stash).filter(([g,v]) => v>=1).slice(0,5).forEach(([g]) => { h += sbtn('на лошадь: '+ru(g), `send({op:'horse',action:'stow',good:'${g}',qty:1})`); });
      Object.keys(hz.bag||{}).slice(0,5).forEach(g => { h += sbtn('с лошади: '+ru(g), `send({op:'horse',action:'unstow',good:'${g}',qty:1})`); });
    }
    h += '</div>';
  } else if(stb && typeof curLoc!=='undefined' && curLoc==='granary'){
    h += '<div class="acts">' + sbtn(`🐴 Купить лошадь у конюха (${stb.price}⌾, в конюшне ${stb.horses})`, `send({op:'horse',action:'buy'})`, stb.horses<1 || (me.coins||0)<stb.price) + '</div>';
  }
  if(wv.can_learn) h += '<div class="acts">' + sbtn('🧘 Созерцать ману (учиться)', `send({op:'act',action:'meditate'})`) + '</div>';
  setHTML('statcard', h);
}

// ---------- сума и закрома ----------
function drawInv(s){
  const me = s.me||{}, inv = me.inv||{items:{},hand:'',worn:{}}, stash = me.stash||{};
  const items = Object.entries(inv.items||{}).filter(([k,v])=>v>0);
  const goods = Object.entries(stash).filter(([k,v])=>v>=1);
  let h = `<b class="sub">сума</b> <span class="sub">· в руке: ${ru(inv.hand)||'пусто'} · надето: ${Object.values(inv.worn||{}).map(ru).join(', ')||'ничего'}</span>`;
  h += '<div style="margin-top:6px">' + (items.length ? items.map(([k,v])=>`<span class="tag">${ru(k)} ×${v}</span>`).join('') : '<span class="sub">пусто</span>') + '</div>';
  h += '<div class="acts" style="grid-template-columns:1fr 1fr 1fr">';
  HAND.forEach(it => { if((inv.items||{})[it]>0 && inv.hand!==it) h += sbtn('в руку: '+ru(it), `send({op:'equip',item:'${it}'})`); });
  if(inv.hand) h += sbtn('убрать из руки', `send({op:'equip',item:''})`);
  WEAR.forEach(it => { if((inv.items||{})[it]>0 && !Object.values(inv.worn||{}).includes(it)) h += sbtn('надеть: '+ru(it), `send({op:'wear',item:'${it}'})`); });
  USE.forEach(it => { if((inv.items||{})[it]>0) h += sbtn((it==='potion'||it==='bandage'?'🩹 ':'🍲 ')+ru(it), `send({op:'use',item:'${it}'})`); });
  h += '</div>';
  h += '<div style="margin-top:8px"><b class="sub">закрома</b> ' + (goods.length ? goods.map(([k,v])=>`<span class="tag">${ru(k)} ×${Math.floor(v)}</span>`).join('') : '<span class="sub">пусто</span>') + '</div>';
  const eat = EAT.filter(g => (stash[g]||0) >= 1);
  if(eat.length) h += '<div class="acts" style="grid-template-columns:1fr 1fr 1fr">' + eat.map(g => sbtn('съесть: '+ru(g), `send({op:'use',item:'${g}'})`)).join('') + '</div>';
  setHTML('invcard', h);
}

// рынок: живой прилавок с ценами
function marketUI(M, s){
  M.className = 'mg';
  const rows = ((s.place||{}).market||[]), stash = (s.me||{}).stash||{};
  let h = `<b class="sub">прилавок</b> <span class="sub">· 💰 ${(s.me||{}).coins??0}</span>`;
  if(!rows.length) h += '<div class="sub">пусто</div>';
  rows.slice(0,14).forEach(r => {
    h += `<div class="nb"><span>${ru(r.good)} <span class="sub">· на складе ${r.stock} · у тебя ${Math.floor(stash[r.good]||0)}</span></span><span>` +
         sbtn(`купить ${r.price}`, `send({op:'trade',good:'${r.good}',side:'buy',qty:1})`, r.stock<1) + ' ' +
         sbtn(`продать ${r.sell}`, `send({op:'trade',good:'${r.good}',side:'sell',qty:1})`, (stash[r.good]||0)<1) + '</span></div>';
  });
  M.innerHTML = h;
}

// ---------- бой ----------
let selFoe = '', aimSel = 'torso', styleSel = 'slash';
function pickFoe(id){ selFoe = id; if(typeof last!=='undefined' && last) drawFight(last), drawWeave(last); }
function setAim(a){ aimSel = a; if(last) drawFight(last); }
function setStyle(a){ styleSel = a; if(last) drawFight(last); }
function drawFight(s){
  const me = s.me||{}, c = me.combat||{}, pos = me.pos||{x:0,y:0};
  const foes = ((s.place||{}).foes||[]).filter(f => f.state!=='dead')
    .map(f => Object.assign({d: Math.hypot(f.x-pos.x, f.y-pos.y)}, f)).sort((a,b)=>a.d-b.d);
  if(selFoe && !foes.find(f=>f.id===selFoe)) selFoe = '';
  const hunting = foes.some(f => f.aware && f.state==='chase');
  let h = `<div class="row between"><b>⚔ Зверь и лихой люд</b><span class="sub">оружие: ${ru(c.weapon)||'кулаки'}${c.weapon==='bow'?` · стрел ${c.arrows||0}`:''}${c.shield?` · щит ${c.shield}`:''}</span></div>`;
  if(hunting) h += '<div style="color:#e39b84">На тебя идут! Дерись — или уходи в деревню: туда зверь не сунется.</div>';
  if(!foes.length) h += '<div class="sub">Вокруг тихо. Зверь держится леса, заречья и рудника.</div>';
  foes.slice(0,6).forEach(f => {
    const st = f.state==='chase'?'нападает':f.state==='flee'?'убегает':f.state==='eat'?'ест приманку':'бродит';
    const fl = [f.blind?'ослеплён':'', f.slow?'хромает':'', f.weak?'ранен в лапу':'', f.stun?'оглушён':''].filter(Boolean).join(', ');
    h += `<div class="nb" style="${f.id===selFoe?'background:#4a3f2c':''}"><span><b>${esc(f.name)}</b> <span class="sub">· ${Math.round(f.d)} м · ${st}${fl?' · '+fl:''}</span>
      <div class="bar" style="width:120px;margin:2px 0 0"><div style="width:${Math.round(100*f.hp/f.max)}%;background:#c96b58"></div></div></span><span>` +
      sbtn(f.id===selFoe?'🎯 цель':'цель', `pickFoe('${f.id}')`, false, f.id===selFoe?'primary':'') + ' ' +
      sbtn('вплотную', `pickFoe('${f.id}');send({op:'goto',target:'${f.id}',qty:2})`) + ' ' +
      sbtn('на выстрел', `pickFoe('${f.id}');send({op:'goto',target:'${f.id}',qty:16})`) + '</span></div>';
  });
  const parts = {head:'голова', torso:'корпус', arm:'рука/лапа', leg:'нога'};
  const styles = c.weapon==='bow' ? {drawn:'полный натяг', snap:'навскидку'} : {slash:'рубящий', thrust:'укол', heavy:'тяжёлый'};
  if(!styles[styleSel]) styleSel = Object.keys(styles)[0];
  h += '<div class="sub" style="margin-top:8px">куда бить</div><div class="row">' +
       Object.entries(parts).map(([k,v]) => sbtn(v, `setAim('${k}')`, false, aimSel===k?'primary':'')).join('') + '</div>';
  h += '<div class="sub" style="margin-top:6px">как бить</div><div class="row">' +
       Object.entries(styles).map(([k,v]) => sbtn(v, `setStyle('${k}')`, false, styleSel===k?'primary':'')).join('') + '</div>';
  h += '<div class="acts">' +
       sbtn(c.weapon==='bow'?'🏹 Выстрелить':'⚔ Ударить', `send({op:'attack',target:selFoe,aim:aimSel,style:styleSel})`, !selFoe||c.cd>0, 'primary') +
       sbtn(c.guard?'🛡 блок держу':'🛡 Блок', `send({op:'guard',on:${!c.guard}})`, false, c.guard?'primary':'') +
       sbtn('↩ Уворот', `send({op:'dodge'})`) +
       sbtn(c.feint?'🎭 финт готов':'🎭 Финт', `send({op:'trick',trick:'feint'})`, c.feint) +
       sbtn('🏖 Песок в глаза', `send({op:'trick',trick:'sand'})`) +
       sbtn('🤜 Оттолкнуть', `send({op:'trick',trick:'shove'})`) +
       sbtn('🥩 Приманка', `send({op:'trick',trick:'bait'})`, ((me.stash||{}).meat||0)<1) +
       sbtn(c.sneak?'🤫 крадусь':'🤫 Красться', `send({op:'trick',trick:'sneak'})`, false, c.sneak?'primary':'') + '</div>';
  const st = {aggressive:'яростная', balanced:'ровная', defensive:'осторожная'};
  h += '<div class="sub" style="margin-top:6px">стойка</div><div class="row">' +
       Object.entries(st).map(([k,v]) => sbtn(v, `send({op:'stance',stance:'${k}'})`, false, c.stance===k?'primary':'')).join('') + '</div>';
  const kills = Object.entries(c.kills||{}).filter(([k,v])=>v>0);
  if(kills.length) h += `<div class="sub" style="margin-top:6px">повержено: ${kills.map(([k,v])=>`${k} ×${v}`).join(', ')}</div>`;
  setHTML('fightcard', h);
}

// ---------- плетения ----------
const GLYPH = {knot:'◉', thread:'〰', loop:'∞', fork:'⑂', barb:'▲', seal:'▣'};
let pattern = [];
function wAdd(n){ if(pattern.length < 12) pattern.push(n); if(last) drawWeave(last); }
function wDel(){ pattern.pop(); if(last) drawWeave(last); }
function wClr(){ pattern = []; if(last) drawWeave(last); }
function wSet(i){ const k=(((last||{}).me||{}).weave||{}).known||[]; if(k[i]) pattern = k[i].nodes.slice(); if(last) drawWeave(last); }
function wCast(){ if(!pattern.length) return; send({op:'weave',nodes:pattern,target:selFoe}); }
function weaveRead(p){
  const n = {}; p.forEach(x => n[x] = (n[x]||0)+1);
  const closed = p[0]==='knot' && p[p.length-1]==='seal' && n.seal===1;
  const cx = p.length + (n.fork||0) + (closed?0:3), cost = 4*p.length + 4*(n.knot||0);
  let what = 'свет над ладонью';
  if(n.barb) what = `ранит${n.thread?' издали':' вблизи'}${n.fork?`, целей: ${1+n.fork}`:''}${n.loop?', потом жжёт':''}`;
  else if(n.loop && n.thread) what = 'заживляет раны';
  else if(n.loop) what = `щит${n.fork?' на весь отряд':''}`;
  else if(n.thread) what = 'отбрасывает и оглушает';
  return {closed, cx, cost, what};
}
function drawWeave(s){
  const me = s.me||{}, wv = me.weave; if(!wv){ show('weavecard', false); return; }
  show('weavecard', true);
  let h = `<div class="row between"><b>✨ Плетение</b><span class="sub">мана ${me.mana??0} · ум держит сложность ${wv.capacity}</span></div>`;
  h += '<div class="sub">Собери узор из узлов: начни узлом, закончи печатью. Чем сложнее узор, тем вернее он сорвётся и ударит по тебе.</div>';
  h += `<div style="font-size:26px;letter-spacing:6px;min-height:40px;margin:6px 0;padding:4px 8px;background:#1d1b16;border:1px solid #5c4a35;border-radius:6px">${pattern.map(n=>GLYPH[n]).join('')||'<span class="sub" style="font-size:14px;letter-spacing:0">узор пуст</span>'}</div>`;
  if(pattern.length){
    const r = weaveRead(pattern), over = r.cx - wv.capacity;
    const col = over <= -1 ? '#8da96a' : over <= 1 ? '#d99b4e' : '#c96b58';
    const risk = over <= -1 ? 'удержишь' : over <= 1 ? 'на пределе' : 'надорвёшься';
    h += `<div style="font-size:14px">Сделает: <b>${r.what}</b>. Сложность <b style="color:${col}">${r.cx} — ${risk}</b>, маны ${r.cost}.${r.closed?'':' <span style="color:#c96b58">Узор не замкнут!</span>'}</div>`;
  }
  h += '<div class="acts" style="grid-template-columns:1fr 1fr 1fr">' +
       (wv.nodes||[]).map(n => `<button style="width:auto;padding:6px 4px" title="${esc(n.hint)}" onclick="wAdd('${n.id}')"><span style="font-size:18px">${GLYPH[n.id]||''}</span> ${esc(n.name)}<br><span class="sub" style="font-size:11px">${esc(n.hint)}</span></button>`).join('') + '</div>';
  h += '<div class="acts" style="grid-template-columns:2fr 1fr 1fr">' +
       sbtn(selFoe?'✨ Сплести в цель':'✨ Сплести', 'wCast()', !pattern.length, 'primary') + sbtn('⌫ узел', 'wDel()') + sbtn('сброс', 'wClr()') + '</div>';
  h += '<div class="sub" style="margin-top:8px">знаю наизусть (держатся увереннее)</div><div class="row">' +
       (wv.known||[]).map((k,i) => sbtn(`${esc(k.name)} <span class="sub">${k.nodes.map(n=>GLYPH[n]).join('')}</span>`, `wSet(${i})`)).join('') + '</div>';
  setHTML('weavecard', h);
}

// ---------- просьбы ----------
function drawQuests(s){
  const q = (s.me||{}).quests||{offers:[],active:[]};
  const via = {board:'📌 доска у колодца', keeper:'🍺 слово корчмаря', npc:'🗣 просит'};
  let h = `<div class="row between"><b>📜 Просьбы</b><span class="sub">выполнено ${q.done||0} · слава здесь ${q.rep||0}</span></div>`;
  (q.active||[]).forEach(a => {
    const prog = a.kind==='deliver'||a.kind==='cull' ? ` <span class="sub">(${Math.min(a.have,a.qty)}/${a.qty})</span>` : '';
    h += `<div class="nb"><span>${a.ready?'✅':'⏳'} ${esc(a.text)}${prog}${a.coins?` <span class="gold">· ${a.coins}⌾</span>`:''}</span><span>` +
         sbtn('сдать', `send({op:'quest',action:'turnin',quest:'${a.id}'})`, !a.ready, 'primary') + ' ' +
         sbtn('✕', `send({op:'quest',action:'drop',quest:'${a.id}'})`) + '</span></div>';
  });
  if((q.offers||[]).length) h += '<div class="sub" style="margin-top:6px">о чём сейчас просят в селе</div>';
  (q.offers||[]).forEach(o => {
    h += `<div class="nb"><span><span class="sub">${via[o.via]||''}${o.via==='npc'?' '+esc(o.giver_name):''}:</span> ${esc(o.text)}${o.coins?` <span class="gold">· ${o.coins}⌾</span>`:''}</span>` +
         sbtn('взяться', `send({op:'quest',action:'accept',quest:'${o.id}'})`) + '</div>';
  });
  if(!(q.offers||[]).length && !(q.active||[]).length) h += '<div class="sub">Сейчас в селе всего хватает — никто ни о чём не просит.</div>';
  setHTML('questcard', h);
}

// ---------- ремесло ----------
const AT = {smithy:'⚒ Кузница', hearth:'🔥 Очаг', granary:'🌾 Мастерская у амбара'};
const craftFeedback = new PogranichyeCraftFeedback();
function craftRecipe(recipeId){
  const reqid = rid();
  if(!craftFeedback.begin(reqid, recipeId)) return;
  if(!send({op:'craft',item:recipeId,reqid})){
    craftFeedback.cancel(reqid);
    return;
  }
  drawCraft(last||{});
}
function craftQty(value){
  const amount=Number(value);
  if(!Number.isFinite(amount)) return '0';
  return Number.isInteger(amount)?String(amount):amount.toFixed(1);
}
function craftOutputStock(stock){
  const counts=[];
  if(stock.output.inventory!==null) counts.push('сума '+craftQty(stock.output.inventory));
  if(stock.output.stash!==null) counts.push('закрома '+craftQty(stock.output.stash));
  return counts.length?' · сейчас: '+counts.join(' / '):'';
}
function drawCraft(s){
  const me = s.me||{}, rec = me.recipes||[];
  craftFeedback.resolve(me.result);
  const here = rec.filter(r => r.here);
  let h = '<b>🔨 Ремесло</b> ';
  if(typeof curLoc!=='undefined' && curLoc==='mine') h += '<div class="acts">' + sbtn('⛏ Добыть руду', `send({op:'act',action:'mine'})`) + '</div>';
  if(typeof curLoc!=='undefined' && curLoc==='garden') h += '<div class="acts">' + sbtn('🌿 Собрать травы', `send({op:'act',action:'gather'})`) + '</div>';
  if(!here.length) h += '<span class="sub">Мастерят у кузни, очага и амбара. Сырьё: руда — на руднике, травы — у огородов, шкуры и мясо — с охоты.</span>';
  else h += `<span class="sub">${AT[here[0].at]||''}</span>`;
  here.forEach(r => {
    const stock = craftFeedback.stockFor(r, me);
    const needs = stock.needs.map(n =>
      `${ru(n.good)}: ${n.available===null?'?':craftQty(n.available)} / ${craftQty(n.required)}`).join(', ');
    const pending = craftFeedback.isPending(r.id);
    h += `<div class="nb"><span>${esc(r.name)} <span class="sub">· закрома / нужно: ${needs} → ${ru(r.gives)}${r.qty>1?' ×'+r.qty:''}${craftOutputStock(stock)}${pending?' · ожидаем ответ мира':''}</span></span>` +
         sbtn(pending?'⏳ ожидаем…':'сделать', `craftRecipe('${r.id}')`, !r.can || pending, r.can?'primary':'') + '</div>';
  });
  const notice = craftFeedback.notice;
  if(notice){
    if(notice.ok){
      const recipe = rec.find(r => r.id===notice.recipeId);
      const stock = recipe ? craftFeedback.stockFor(recipe, me) : null;
      const current = stock ? craftOutputStock(stock).replace(' · сейчас: ','; теперь ') : '';
      h += `<div class="sub" style="margin-top:8px;color:#b7d69a">✓ ${esc(recipe?recipe.name:ru(notice.recipeId))}: сервер подтвердил изготовление${esc(current)}.</div>`;
    } else {
      h += `<div class="sub" style="margin-top:8px;color:#e5a18a">⚠ ${esc(notice.reason||'Сервер отказал в изготовлении.')}</div>`;
    }
  }
  setHTML('craftcard', h);
}

// ---------- отряд ----------
function drawParty(s){
  const me = s.me||{}, p = me.party||{members:[]};
  const players = ((s.place||{}).neighbors||[]).filter(n => n.player);
  const inParty = new Set((p.members||[]).map(m => m.id));
  if(!p.invite_from && !(p.members||[]).length && !players.length){ show('partycard', false); return; }
  show('partycard', true);
  let h = '<b>🤝 Отряд</b> <span class="sub">друзья идут по тракту вместе и дерутся плечом к плечу</span>';
  if(p.invite_from) h += `<div class="mg"><b>${esc(p.invite_from.who)}</b> зовёт в отряд <div class="acts">` +
       sbtn('вступить', `send({op:'party',action:'accept'})`, false, 'primary') + sbtn('нет', `send({op:'party',action:'leave'})`) + '</div></div>';
  (p.members||[]).forEach(m => { h += `<div class="nb"><span>${m.leader?'⭐ ':''}<b>${esc(m.name)}</b> <span class="sub">· здоровье ${m.health??'?'}${m.road?' · в пути':''}</span></span></div>`; });
  if((p.members||[]).length) h += '<div class="acts">' + sbtn('выйти из отряда', `send({op:'party',action:'leave'})`) + '</div>';
  players.filter(n => !inParty.has(n.id)).forEach(n => {
    h += `<div class="nb"><span>🧑 ${esc(n.known?n.name:n.desc)}</span>` + sbtn('позвать в отряд', `send({op:'party',action:'invite',target:'${n.id}'})`) + '</div>';
  });
  setHTML('partycard', h);
}

// ---------- живая карта окрестностей (вид сверху) ----------
// Тот же мир, что видит толстый клиент: дома, река, жители, звери — по кадру сервера. Тап по зверю
// выбирает цель, тап по месту ведёт к ближайшей локации. Ходьбы по-прежнему нет (ADR 0002).
let LAY = null, layAsked = false;
const MAPLOC = ['well','tavern','river','woods','garden','smithy','granary','mine','hearth','market'];
let villageAtlas = null;
const villageMapView = {zoom:1,panX:0,panY:0};
let villageMapPointer = null, villageMapSuppressClick = false;
if(typeof Image !== 'undefined'){
  villageAtlas = new Image();
  villageAtlas.onload = () => { if(last) drawLiveMap(last); };
  villageAtlas.src = '/village-atlas.svg';
}
function clampVillageMapView(){
  villageMapView.zoom = Math.max(1,Math.min(4,villageMapView.zoom));
  const limit = 320 * (villageMapView.zoom - 1);
  villageMapView.panX = Math.max(-limit,Math.min(limit,villageMapView.panX));
  villageMapView.panY = Math.max(-limit,Math.min(limit,villageMapView.panY));
}
function bindVillageMap(cv){
  if(cv.dataset.mapControlsBound) return;
  cv.dataset.mapControlsBound='1';
  cv.addEventListener('wheel',ev=>{
    ev.preventDefault();
    const rect=cv.getBoundingClientRect(),sx=(ev.clientX-rect.left)*cv.width/rect.width,sy=(ev.clientY-rect.top)*cv.height/rect.height;
    const old=villageMapView.zoom,next=Math.max(1,Math.min(4,old*(ev.deltaY<0?1.2:1/1.2)));
    const anchorX=(sx-cv.width/2-villageMapView.panX)/old,anchorY=(sy-cv.height/2-villageMapView.panY)/old;
    villageMapView.zoom=next;villageMapView.panX=sx-cv.width/2-anchorX*next;villageMapView.panY=sy-cv.height/2-anchorY*next;
    clampVillageMapView();if(last)drawLiveMap(last);
  },{passive:false});
  cv.addEventListener('pointerdown',ev=>{
    if(ev.button!==0)return;
    villageMapPointer={id:ev.pointerId,x:ev.clientX,y:ev.clientY,panX:villageMapView.panX,panY:villageMapView.panY,moved:false};
    if(cv.setPointerCapture)cv.setPointerCapture(ev.pointerId);
  });
  cv.addEventListener('pointermove',ev=>{
    if(!villageMapPointer||villageMapPointer.id!==ev.pointerId)return;
    const dx=(ev.clientX-villageMapPointer.x)*cv.width/cv.getBoundingClientRect().width;
    const dy=(ev.clientY-villageMapPointer.y)*cv.height/cv.getBoundingClientRect().height;
    if(Math.abs(dx)+Math.abs(dy)>5)villageMapPointer.moved=true;
    if(villageMapPointer.moved){villageMapView.panX=villageMapPointer.panX+dx;villageMapView.panY=villageMapPointer.panY+dy;clampVillageMapView();if(last)drawLiveMap(last);}
  });
  const finish=ev=>{
    if(!villageMapPointer||villageMapPointer.id!==ev.pointerId)return;
    villageMapSuppressClick=villageMapPointer.moved;villageMapPointer=null;
  };
  cv.addEventListener('pointerup',finish);cv.addEventListener('pointercancel',finish);
}
function villageMapZoom(delta){
  villageMapView.zoom=Math.max(1,Math.min(4,villageMapView.zoom+delta));
  if(villageMapView.zoom===1){villageMapView.panX=0;villageMapView.panY=0;}
  clampVillageMapView();if(last)drawLiveMap(last);
}
function villageMapReset(){villageMapView.zoom=1;villageMapView.panX=0;villageMapView.panY=0;if(last)drawLiveMap(last);}
function drawLiveMap(s){
  const cv = document.getElementById('livemap'); if(!cv) return;
  bindVillageMap(cv);
  if(!LAY && !layAsked){ layAsked = true; fetch('/api/layout').then(r => r.json()).then(j => { LAY = j; if(last) drawLiveMap(last); }).catch(() => { layAsked = false; }); }
  if(!LAY || !s.place || !(Number(LAY.world_half)>0)) return;
  if(window.PogranichyeVillageMap) window.PogranichyeVillageMap.draw(cv,LAY,s,villageAtlas,selFoe,villageMapView);
  const zoomLabel=document.getElementById('mapzoomlevel');if(zoomLabel)zoomLabel.textContent=villageMapView.zoom.toFixed(1).replace('.0','')+'×';
  const W=cv.width,H=cv.height,half=Number(LAY.world_half);
  if(!cv.onclick) cv.onclick = ev => {
    if(villageMapSuppressClick){villageMapSuppressClick=false;return;}
    const rc=cv.getBoundingClientRect(),mx=(ev.clientX-rc.left)*W/rc.width,my=(ev.clientY-rc.top)*H/rc.height;
    const world=window.PogranichyeVillageMap
      ? window.PogranichyeVillageMap.viewUnproject(mx,my,half,W,H,villageMapView)
      : {x:mx/(W/(2*half))-half,y:half-my/(H/(2*half))};
    if(!world)return;
    const wx=world.x,wy=world.y,st=last||{};
    let best=null,bd=12/villageMapView.zoom;
    ((st.place||{}).foes||[]).forEach(f=>{const d=Math.hypot(f.x-wx,f.y-wy);if(f.state!=='dead'&&d<bd){bd=d;best=f;}});
    if(best){pickFoe(best.id);return;}
    let loc=null;bd=30/villageMapView.zoom;
    MAPLOC.forEach(n=>{const q=LAY.pois[n];if(!q)return;const d=Math.hypot(q.x-wx,q.y-wy);if(d<bd){bd=d;loc=n;}});
    if(loc)goTo(loc);
  };
}
function drawLiveMap(s){
  const cv = document.getElementById('livemap'); if(!cv) return;
  if(!LAY && !layAsked){ layAsked = true; fetch('/api/layout').then(r => r.json()).then(j => { LAY = j; if(last) drawLiveMap(last); }).catch(() => { layAsked = false; }); }
  if(!LAY || !s.place || !(Number(LAY.world_half)>0)) return;
  if(window.PogranichyeVillageMap) window.PogranichyeVillageMap.draw(cv,LAY,s,villageAtlas,selFoe);
  const W=cv.width,H=cv.height,half=Number(LAY.world_half);
  if(!cv.onclick) cv.onclick = ev => {
    const rc=cv.getBoundingClientRect(),mx=(ev.clientX-rc.left)*W/rc.width,my=(ev.clientY-rc.top)*H/rc.height;
    const world=window.PogranichyeVillageMap
      ? window.PogranichyeVillageMap.unproject(mx,my,half,W,H)
      : {x:mx/(W/(2*half))-half,y:half-my/(H/(2*half))};
    if(!world)return;
    const wx=world.x,wy=world.y,st=last||{};
    let best=null,bd=12;
    ((st.place||{}).foes||[]).forEach(f=>{const d=Math.hypot(f.x-wx,f.y-wy);if(f.state!=='dead'&&d<bd){bd=d;best=f;}});
    if(best){pickFoe(best.id);return;}
    let loc=null;bd=30;
    MAPLOC.forEach(n=>{const q=LAY.pois[n];if(!q)return;const d=Math.hypot(q.x-wx,q.y-wy);if(d<bd){bd=d;loc=n;}});
    if(loc)goTo(loc);
  };
}
