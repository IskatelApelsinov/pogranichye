// Самоход к месту села (v1.22): чистая логика клиента-менеджера. Клиент называет цель, сервер ведёт
// тело и подтверждает: me.walk — «иду {to,left}», me.at — «стою у места». Здесь нет ни позиции, ни
// прибытия «по таймеру»: экран места меняется только по серверному me.at.
(function(root,factory){
  const W=factory();
  if(typeof module==='object' && module.exports) module.exports=W;
  if(root) root.WalkState=W;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  const WAIT_FRAMES=4; // столько полных кадров ждём, что сервер начнёт вести тело, прежде чем сказать «не пройти»
  function initial(){ return {target:null,waited:0}; }
  // Тап по месту. Возвращает {state, send, legacy}: send — что отправить в op:"goto" (null — ничего,
  // '' — отменить путь), legacy — сервер старый (нет me.at), место меняется сразу, как раньше.
  function onTap(state,key,me){
    if(!me || me.at===undefined) return {state:initial(),send:key,legacy:true};
    const walking=me.walk&&me.walk.to;
    if(walking===key) return {state:initial(),send:'',legacy:false};            // повторный тап — отмена
    if(!walking && me.at===key) return {state:initial(),send:null,legacy:false}; // уже здесь
    return {state:{target:key,waited:0},send:key,legacy:false};
  }
  // Полный кадр сервера. Возвращает {state, arrived, message, target}: arrived — место, у которого
  // сервер подтвердил тело (null — без изменений); message — строка для игрока с %s на месте названия.
  function onFrame(state,me,curLoc){
    const none={state,arrived:null,message:null,target:null};
    if(!me || me.at===undefined) return none;
    if(me.walk && me.walk.to){
      const left=Math.max(0,Math.round(Number(me.walk.left)||0));
      return {state:{target:me.walk.to,waited:0},arrived:null,message:'🚶 Иду: %s — осталось '+left+' м',target:me.walk.to};
    }
    if(state.target){
      if(me.at===state.target) return {state:initial(),arrived:state.target,message:'Пришёл: %s',target:state.target};
      if(state.waited+1>=WAIT_FRAMES) return {state:initial(),arrived:me.at||null,message:'Туда сейчас не пройти: %s',target:state.target};
      return {state:{target:state.target,waited:state.waited+1},arrived:null,message:null,target:state.target};
    }
    // без цели: тело могло переместиться не отсюда (другой клиент, возврат после обрыва связи)
    if(me.at && me.at!==curLoc) return {state,arrived:me.at,message:null,target:me.at};
    return none;
  }
  return {initial,onTap,onFrame,WAIT_FRAMES};
});
