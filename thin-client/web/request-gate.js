(function(root,factory){
  const Gate=factory();
  if(typeof module==='object' && module.exports) module.exports=Gate;
  if(root) root.PogranichyeRequestGate=Gate;
})(typeof globalThis!=='undefined'?globalThis:this,function(){
  'use strict';
  class RequestGate {
    constructor(send,capacity=6,timeoutMs=15000){
      if(typeof send!=='function') throw new TypeError('send callback is required');
      this.send=send;
      this.capacity=Math.max(1,capacity|0);
      this.timeoutMs=Math.max(1,timeoutMs|0);
      this.queue=[];
      this.active=null;
      this.connectionId=null;
    }
    get pendingCount(){ return this.queue.length+(this.active?1:0); }
    get activeItem(){ return this.active; }
    enqueue(label,payload,now=Date.now()){
      if(!payload || typeof payload.reqid!=='string' || !payload.reqid) return false;
      if((this.active&&this.active.reqid===payload.reqid)||this.queue.some(x=>x.reqid===payload.reqid)) return true;
      if(this.pendingCount>=this.capacity) return false;
      this.queue.push({label:label||'Действие',payload,reqid:payload.reqid,sentAt:null,lastConnection:null,retries:0,timedOut:false});
      this.dispatch(now);
      return true;
    }
    setConnection(connectionId,now=Date.now(),ackReqid){
      if(connectionId===null || connectionId===undefined) return;
      this.connectionId=connectionId;
      if(this.active && this.active.reqid===ackReqid) return;
      if(this.active && this.active.lastConnection!==connectionId){
        this.active.retries=0;
        this.active.timedOut=false;
        this.sendActive(now);
        return;
      }
      this.dispatch(now);
    }
    disconnect(){ this.connectionId=null; }
    dispatch(now=Date.now()){
      if(this.active || this.connectionId===null || !this.queue.length) return false;
      this.active=this.queue.shift();
      return this.sendActive(now);
    }
    sendActive(now=Date.now()){
      if(!this.active || this.connectionId===null) return false;
      let sent=false;
      try{ sent=this.send(this.active.payload)!==false; }catch(_){ sent=false; }
      if(!sent){ this.active.lastConnection=null; this.active.sentAt=null; return false; }
      this.active.lastConnection=this.connectionId;
      this.active.sentAt=now;
      return true;
    }
    accept(result,now=Date.now()){
      if(!this.active || !result || typeof result.reqid!=='string' || result.reqid!==this.active.reqid) return null;
      const done=this.active;
      this.active=null;
      this.dispatch(now);
      return done;
    }
    tick(now=Date.now()){
      if(!this.active || this.connectionId===null || this.active.sentAt===null || now-this.active.sentAt<this.timeoutMs) return null;
      if(this.active.retries<1){
        this.active.retries++;
        this.sendActive(now);
        return {item:this.active,retried:true};
      }
      if(!this.active.timedOut){
        this.active.timedOut=true;
        return {item:this.active,retried:false};
      }
      return null;
    }
    clear(){ this.queue=[]; this.active=null; this.connectionId=null; }
  }
  return RequestGate;
});
