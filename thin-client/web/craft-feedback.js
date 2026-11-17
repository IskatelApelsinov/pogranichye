(function(root,factory){
  const api=factory();
  if(typeof module==='object' && module.exports) module.exports=api;
  if(root) root.PogranichyeCraftFeedback=api;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  class CraftFeedback {
    constructor(){ this.pending=new Map(); this.notice=null; }
    begin(reqid,recipeId){
      if(typeof reqid!=='string'||!reqid||typeof recipeId!=='string'||!recipeId) return false;
      if(this.pending.has(reqid)) return false;
      this.pending.set(reqid,recipeId);
      this.notice=null;
      return true;
    }
    cancel(reqid){ return this.pending.delete(reqid); }
    isPending(recipeId){
      for(const id of this.pending.values()) if(id===recipeId) return true;
      return false;
    }
    resolve(result){
      if(!result||typeof result.reqid!=='string'||!this.pending.has(result.reqid)) return null;
      const recipeId=this.pending.get(result.reqid);
      this.pending.delete(result.reqid);
      this.notice={recipeId,ok:!!result.ok,reason:result.reason||''};
      return this.notice;
    }
    stockFor(recipe,me){
      const stash=me&&me.stash&&typeof me.stash==='object'?me.stash:null;
      const items=me&&me.inv&&me.inv.items&&typeof me.inv.items==='object'?me.inv.items:null;
      const read=(source,key)=>{
        if(!source) return null;
        const value=Number(source[key]);
        return Number.isFinite(value)&&value>0?value:0;
      };
      const needs=Object.entries(recipe&&recipe.needs&&typeof recipe.needs==='object'?recipe.needs:{}).map(([good,required])=>({
        good,
        required:Number.isFinite(Number(required))?Math.max(0,Number(required)):0,
        available:read(stash,good)
      }));
      return {
        needs,
        output:{
          stash:read(stash,recipe&&recipe.gives),
          inventory:read(items,recipe&&recipe.gives)
        }
      };
    }
  }
  return CraftFeedback;
});