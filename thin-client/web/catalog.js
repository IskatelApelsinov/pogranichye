// Каталог мира и окрестности (v1.22): чистая логика клиента-менеджера. Что продают, что можно взять в
// руку, надеть, съесть и как это называется — приходит с сервера (/api/catalog), а не зашито в
// клиенте. Места без жителей вокруг — из кадра (place.sites). Здесь нет ни цен «по памяти», ни
// выдуманных мест: пустой каталог или пустой список — значит, показывать нечего.
(function(root,factory){
  const C=factory();
  if(typeof module==='object' && module.exports) module.exports=C;
  if(root) root.PogCatalog=C;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  const items=cat=>(cat&&Array.isArray(cat.items))?cat.items.filter(it=>it&&typeof it.item==='string'):[];
  // Название вещи или товара: сначала каталог сервера, потом запасной словарь клиента, потом сам ключ.
  function name(cat,fallback,key){
    if(!key) return '';
    const n=cat&&cat.names&&cat.names[key];
    return (typeof n==='string'&&n)?n:((fallback&&fallback[key])||key);
  }
  // Что продают у этого места села: [{item,name,price}].
  function waresAt(cat,loc){
    return items(cat).filter(it=>it.at===loc&&Number(it.price)>0)
      .map(it=>({item:it.item,name:it.name||it.item,price:Number(it.price)}));
  }
  // Списки для сумы: что берут в руку, что надевают, что едят/прикладывают из сумы и из закромов.
  function lists(cat,fallback){
    const all=items(cat);
    if(!all.length) return fallback;
    const use=new Set(Array.isArray(cat.usables)?cat.usables:[]), known=new Set(all.map(it=>it.item));
    return {
      hand:all.filter(it=>it.hand).map(it=>it.item),
      wear:all.filter(it=>it.slot).map(it=>it.item),
      use:all.filter(it=>use.has(it.item)).map(it=>it.item),          // готовое — лежит в суме
      eat:[...use].filter(k=>!known.has(k)).sort(),                    // сырое — лежит в закромах
    };
  }
  const SITE_ICON={camp:'🔥',spring:'💧',berry:'🫐',herbs:'🌿',thicket:'🌲',outcrop:'⛏',pond:'🎣',hut:'🛖',barrow:'🗿',ruins:'🏚',den:'🕳',cave:'🕳',tower:'🗼',grove:'🌳'};
  const ACT_LABEL={rest:'🔥 Привал',drink:'💧 Напиться',explore:'🔎 Обыскать',forage:'🧺 Собрать'};
  const GIVES={herb:'травы',wood:'дрова',ore:'руду',fish:'рыбу',veg:'ягоды'};
  function distText(m){ m=Number(m)||0; return m<950?Math.round(m/10)*10+' м':(m/1000).toFixed(1).replace('.',',')+' км'; }
  // Строки карточки «Окрестности»: только то, что прислал сервер.
  function siteRows(sites,walk){
    const going=walk&&walk.site||'';
    return (Array.isArray(sites)?sites:[]).filter(s=>s&&typeof s.id==='string'&&s.name).map(s=>{
      const acts=(Array.isArray(s.acts)?s.acts:[]).filter(a=>ACT_LABEL[a]).map(a=>({
        action:a,
        label:a==='forage'&&GIVES[s.gives]?'🧺 Собрать '+GIVES[s.gives]+(s.left!==undefined?' (осталось '+s.left+')':''):ACT_LABEL[a],
        disabled:a==='forage'&&s.left!==undefined&&Number(s.left)<1,
      }));
      return {id:s.id,icon:SITE_ICON[s.kind]||'📍',name:String(s.name),where:s.here?'ты здесь':distText(s.dist)+' '+(s.dir||''),
        here:!!s.here,going:going===s.id,acts:s.here?acts:[],about:acts.map(a=>a.label.replace(/ \(осталось.*\)$/,'')).join(' · ')};
    });
  }
  // «Чьи это земли» (v1.24): одна-две строки про державу села. null — вольная земля.
  function realmLines(r){
    if(!r||!r.name) return ['Вольная земля — над селом нет державы.'];
    const out=[(r.is_capital?'Столица державы «':'Земли державы «')+r.name+'»'+(!r.is_capital&&r.capital?' · столица — '+r.capital:'')+
      (r.size?' · сёл под рукой: '+r.size:'')+(r.tax?' · десятина '+Math.round(Number(r.tax)*100)+'%':'')];
    if(r.war_with) out.push('⚔ Война с державой «'+r.war_with+'»');
    const a=Array.isArray(r.allies)?r.allies:[], v=Array.isArray(r.rivals)?r.rivals:[];
    if(a.length) out.push('🤝 Союзники: '+a.join(', '));
    if(v.length) out.push('⚠ Во вражде: '+v.join(', '));
    return out;
  }
  // Обозы неподалёку (v1.24): что везут, откуда и куда, где они сейчас.
  function caravanRows(cs,cat){
    return (Array.isArray(cs)?cs:[]).filter(c=>c&&c.good).map(c=>
      '🐎 Обоз: '+(c.good_name&&c.good_name!==c.good?c.good_name:name(cat,c.good))+' ×'+Math.round(Number(c.amount)||0)+
      ' · '+(c.from||'?')+' → '+(c.to||'?')+' · '+distText(c.dist)+' '+(c.dir||''));
  }
  return {name,waresAt,lists,siteRows,distText,realmLines,caravanRows};
});
