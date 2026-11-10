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
  drawRoad(s); drawFeed(s); drawFight(s); drawWeave(s); drawQuests(s); drawCraft(s); drawParty(s); drawLiveMap(s);
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
  const me = s.me||{}, world = me.world||[], hereId = (s.place||{}).id;
  const visited = new Set((me.known_places||[]).filter(p=>p.visited).map(p=>p.id));
  const known = new Set((me.known_places||[]).map(p=>p.id));
  const here = world.find(p => String(p.id) === String(hereId)) || {outer:false};
  const sameSide = world.filter(p => p.outer === here.outer && String(p.id) !== String(hereId));
  const other = world.filter(p => p.outer !== here.outer);
  let h = `<b class="sub">🛤 дороги отсюда</b> <span class="sub">(путь занимает время, в дороге всякое бывает)</span><div class="acts">`;
  sameSide.slice(0,30).forEach(p => {
    h += sbtn(`${p.tier==='city'?'🏰':'🏘'} ${esc(p.name)}${visited.has(String(p.id))?' ✓':known.has(String(p.id))?' ·':''}`,
              `send({op:'journey',target:'${p.id}'})`, !!me.journey);
  });
  h += '</div>';
  if(other.length){
    h += `<div style="margin-top:10px"><b class="sub">🌀 Врата через горы</b> <span class="sub">(${here.outer?'домой, в родной край':'во внешний мир'} — пешком не дойти)</span><div class="acts">`;
    other.slice(0,40).forEach(p => { h += sbtn(`${p.tier==='city'?'🏰':'🏘'} ${esc(p.name)}`, `send({op:'travel',target:'${p.id}'})`, !!me.journey); });
    h += '</div></div>';
  }
  setHTML('travelcard', h);
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
    const needs = Object.entries(r.needs||{}).map(([g,n]) => `${ru(g)} ×${n}`).join(', ');
    const pending = craftFeedback.isPending(r.id);
    h += `<div class="nb"><span>${esc(r.name)} <span class="sub">· нужно: ${needs} → ${ru(r.gives)}${r.qty>1?' ×'+r.qty:''}${pending?' · ожидаем ответ мира':''}</span></span>` +
         sbtn(pending?'⏳ ожидаем…':'сделать', `craftRecipe('${r.id}')`, !r.can || pending, r.can?'primary':'') + '</div>';
  });
  const notice = craftFeedback.notice;
  if(notice) h += `<div class="sub" style="margin-top:8px;color:${notice.ok?'#b7d69a':'#e5a18a'}">${notice.ok?'✓ Ремесло подтверждено сервером.':'⚠ '+esc(notice.reason||'Сервер отказал в изготовлении.')}</div>`;
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
function drawLiveMap(s){
  const cv = document.getElementById('livemap'); if(!cv) return;
  if(!LAY && !layAsked){ layAsked = true; fetch('/api/layout').then(r => r.json()).then(j => { LAY = j; if(last) drawLiveMap(last); }).catch(() => { layAsked = false; }); }
  if(!LAY || !s.place){ return; }
  const W = cv.width, H = cv.height, half = LAY.world_half, k = W / (2 * half);
  const X = x => (x + half) * k, Y = y => (half - y) * k, g = cv.getContext('2d');
  g.fillStyle = '#5f7a3c'; g.fillRect(0, 0, W, H);
  g.fillStyle = '#3c5a2e'; g.fillRect(0, 0, X(-70), H);                       // западный лес
  g.fillRect(0, Y(-94), W, H - Y(-94));                                         // заречье
  g.fillStyle = '#7d8a4e'; const vh = LAY.village_half; g.fillRect(X(-vh), Y(vh), 2 * vh * k, 2 * vh * k);
  const r = LAY.river; g.fillStyle = '#3f6f9a'; g.fillRect(0, Y(r.y1), W, (r.y1 - r.y0) * k);
  g.fillStyle = '#7a5a36'; g.fillRect(X(r.bridge_x0), Y(r.y1) - 2, (r.bridge_x1 - r.bridge_x0) * k, (r.y1 - r.y0) * k + 4);
  g.fillStyle = '#4a3a2a'; (LAY.buildings || []).forEach(b => g.fillRect(X(b.x0), Y(b.y1), (b.x1 - b.x0) * k, (b.y1 - b.y0) * k));
  g.font = 'bold 17px Georgia'; g.textAlign = 'center'; g.fillStyle = '#fff3d6';
  const names = {well:'площадь', tavern:'корчма', river:'река', woods:'лес', garden:'поля', smithy:'кузня', granary:'амбар', mine:'рудник', hearth:'очаг', market:'лавка'};
  MAPLOC.forEach(n => { const p = LAY.pois[n]; if(p) g.fillText(names[n], X(p.x), Y(p.y) - 8); });
  (s.place.neighbors || []).forEach(n => { if(!n.pos) return; g.fillStyle = n.player ? '#ffd27a' : '#e8dcc0'; g.beginPath(); g.arc(X(n.pos.x), Y(n.pos.y), n.player ? 6 : 4, 0, 7); g.fill(); });
  const col = {hare:'#d9c9a8', fox:'#e07a34', boar:'#3a2c20', wolf:'#9a9da4', bear:'#241812', bandit:'#b02a22'};
  (s.place.foes || []).forEach(f => {
    if(f.state === 'dead'){ g.strokeStyle = '#0006'; g.strokeRect(X(f.x) - 2, Y(f.y) - 2, 4, 4); return; }
    g.fillStyle = col[f.kind] || '#c96b58'; g.beginPath(); g.arc(X(f.x), Y(f.y), f.kind === 'bear' ? 9 : 6, 0, 7); g.fill();
    if(f.aware || f.id === selFoe){ g.strokeStyle = f.id === selFoe ? '#ffe9a8' : '#ff5a44'; g.lineWidth = 2.5; g.beginPath(); g.arc(X(f.x), Y(f.y), 12, 0, 7); g.stroke(); }
  });
  const p = (s.me || {}).pos; if(p){ const a = -p.facing * Math.PI / 180;
    g.save(); g.translate(X(p.x), Y(p.y)); g.rotate(a); g.fillStyle = '#6ab0ff'; g.strokeStyle = '#fff'; g.lineWidth = 1;
    g.beginPath(); g.moveTo(13, 0); g.lineTo(-8, 8); g.lineTo(-8, -8); g.closePath(); g.fill(); g.stroke(); g.restore(); }
  if(!cv.onclick) cv.onclick = ev => {
    const rc = cv.getBoundingClientRect(), mx = (ev.clientX - rc.left) * W / rc.width, my = (ev.clientY - rc.top) * H / rc.height;
    const wx = mx / k - half, wy = half - my / k, st = last || {};
    let best = null, bd = 12;
    ((st.place || {}).foes || []).forEach(f => { const d = Math.hypot(f.x - wx, f.y - wy); if(f.state !== 'dead' && d < bd){ bd = d; best = f; } });
    if(best){ pickFoe(best.id); return; }
    let loc = null; bd = 30;
    MAPLOC.forEach(n => { const q = LAY.pois[n]; if(!q) return; const d = Math.hypot(q.x - wx, q.y - wy); if(d < bd){ bd = d; loc = n; } });
    if(loc) goTo(loc);
  };
}
