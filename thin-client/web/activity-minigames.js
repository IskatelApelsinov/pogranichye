(function(root,factory){
  const api=factory();
  if(typeof module==='object' && module.exports) module.exports=api;
  if(root) root.PogranichyeActivityMinigames=api;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  function clamp(value,min,max){ return Math.max(min,Math.min(max,value)); }
  function skillLevel(me,name){
    const raw=Number(me&&me.skills&&me.skills[name]);
    return clamp(Number.isFinite(raw)?raw:0,0,100);
  }
  function fishing(me){
    const skill=skillLevel(me,'fisher');
    return {skill,zoneWidth:18+skill*0.27,speed:1-skill*0.0025};
  }
  function chopping(me){
    const skill=skillLevel(me,'woodcutter');
    return {skill,threshold:70-skill*0.2,speed:1-skill*0.0025};
  }
  function fieldwork(me){
    const skill=skillLevel(me,'farmer');
    return {skill,zoneWidth:18+skill*0.25,speed:1-skill*0.0025};
  }
  function forging(me){
    const skill=skillLevel(me,'smith');
    return {skill,threshold:72-skill*0.22,speed:1-skill*0.0025};
  }
  function building(me){
    const skill=skillLevel(me,'builder');
    return {skill,threshold:70-skill*0.2,speed:1-skill*0.0025};
  }
  function mining(me){
    const skill=skillLevel(me,'miner');
    return {skill,zoneWidth:16+skill*0.24,speed:1-skill*0.0025};
  }
  function foraging(me){
    const skill=skillLevel(me,'herbalist');
    return {skill,zoneWidth:20+skill*0.22,speed:1-skill*0.0025};
  }
  return {skillLevel,fishing,chopping,fieldwork,forging,building,mining,foraging};
});
