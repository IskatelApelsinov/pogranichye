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
  }
  return CraftFeedback;
});