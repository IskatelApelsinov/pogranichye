package server

import (
	"encoding/json"
	"testing"
	"time"

	"pogranichye/core"
	"pogranichye/sim"
)

func TestVoiceSignalingRequiresNearbyActivePeers(t *testing.T) {
	s := NewStarter("voice-signal-test", "", []byte("secret"), "")
	ids := make([]core.ID, 0, 2)
	s.world.Each("place", func(e *core.Entity) {
		if len(ids) < 2 && core.Mind(e) != nil { ids = append(ids, e.ID) }
	})
	if len(ids) != 2 { t.Fatal("need two residents") }
	base := core.Place(s.world.Get(ids[0])).Settlement
	for i,id := range ids {
		e:=s.world.Get(id); core.Place(e).Settlement=base
		p:=sim.EnsureScenePos(s.world,e); p.X=float64(i*5); p.Y=0
	}
	queues:=[]<-chan string{s.registerChat(ids[0]),s.registerChat(ids[1])}
	read:=func(ch <-chan string) map[string]any {
		t.Helper()
		select {
		case raw:=<-ch:
			var event map[string]any
			if err:=json.Unmarshal([]byte(raw),&event);err!=nil{t.Fatalf("bad signaling JSON: %v",err)}
			return event
		case <-time.After(100*time.Millisecond): t.Fatal("voice signaling event not delivered"); return nil
		}
	}
	s.signalVoice(ids[0],"join","","","","",0)
	s.signalVoice(ids[1],"join","","","","",0)
	aJoined,bJoined:=read(queues[0]),read(queues[1])
	if aJoined["type"]!="voice_signal" || aJoined["action"]!="peer_joined" || aJoined["from_id"]!=eid(ids[1]) || aJoined["signaling_type"]!="join" ||
		bJoined["action"]!="peer_joined" || bJoined["from_id"]!=eid(ids[0]) || bJoined["signaling_type"]!="welcome" {
		t.Fatalf("join discovery mismatch: A=%v B=%v",aJoined,bJoined)
	}
	s.signalVoice(ids[0],"offer",eid(ids[1]),"v=0\r\no=demo","","",0)
	offer:=read(queues[1])
	if offer["action"]!="offer" || offer["from_id"]!=eid(ids[0]) || offer["sdp"]!="v=0\r\no=demo" {
		t.Fatalf("offer not relayed: %v",offer)
	}
	s.mu.Lock(); s.chatPeers[ids[1]].lastVoiceSignal=time.Now().Add(-time.Second); s.mu.Unlock()
	s.signalVoice(ids[1],"answer",eid(ids[0]),"v=0\r\no=answer","","",0)
	answer:=read(queues[0])
	if answer["action"]!="answer" || answer["from_id"]!=eid(ids[1]) { t.Fatalf("answer not relayed: %v",answer) }
	s.mu.Lock(); s.chatPeers[ids[0]].lastVoiceSignal=time.Now().Add(-time.Second); s.mu.Unlock()
	s.signalVoice(ids[0],"ice_complete",eid(ids[1]),"","","audio",0)
	complete:=read(queues[1])
	if complete["action"]!="ice_complete" || complete["sdp_mid"]!="audio" { t.Fatalf("ICE completion not relayed: %v",complete) }
	s.signalVoice(ids[1],"leave","","","","",0)
	left:=read(queues[0])
	if left["action"]!="peer_left" || left["from_id"]!=eid(ids[1]) { t.Fatalf("leave not broadcast: %v",left) }
}

func TestVoiceSignalingRejectsOutOfRangeAndUnjoinedSender(t *testing.T) {
	s:=NewStarter("voice-range-test","",[]byte("secret"),"")
	ids:=make([]core.ID,0,2)
	s.world.Each("place",func(e *core.Entity){if len(ids)<2&&core.Mind(e)!=nil{ids=append(ids,e.ID)}})
	if len(ids)!=2{t.Fatal("need two residents")}
	base:=core.Place(s.world.Get(ids[0])).Settlement
	for i,id:=range ids{e:=s.world.Get(id);core.Place(e).Settlement=base;p:=sim.EnsureScenePos(s.world,e);p.X=float64(i*60);p.Y=0}
	q1:=s.registerChat(ids[0]);q2:=s.registerChat(ids[1])
	s.signalVoice(ids[0],"offer",eid(ids[1]),"v=0","","",0) // not joined
	select{case <-q2:t.Fatal("unjoined sender signaled");default:}
	s.signalVoice(ids[0],"join","","","","",0)
	s.signalVoice(ids[1],"join","","","","",0)
	select{case <-q1:t.Fatal("out-of-range peer discovered");default:}
	select{case <-q2:t.Fatal("out-of-range peer discovered");default:}
	s.signalVoice(ids[0],"offer",eid(ids[1]),"v=0","","",0)
	select{case <-q2:t.Fatal("out-of-range SDP relayed");default:}
}
