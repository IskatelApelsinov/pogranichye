package server

import (
	"encoding/json"
	"testing"
	"time"

	"pogranichye/core"
	"pogranichye/sim"
)

func TestLocalChatIsSpatialAndWhisperIsPrivate(t *testing.T) {
	s := NewStarter("chat-test", "", []byte("secret"), "")
	ids := make([]core.ID, 0, 3)
	s.world.Each("place", func(e *core.Entity) {
		if len(ids) < 3 && core.Mind(e) != nil { ids = append(ids, e.ID) }
	})
	if len(ids) != 3 { t.Fatalf("need three residents, got %d", len(ids)) }
	base := core.Place(s.world.Get(ids[0])).Settlement
	for i, id := range ids {
		e := s.world.Get(id)
		core.Place(e).Settlement = base
		p := sim.EnsureScenePos(s.world, e)
		switch i { case 0: p.X,p.Y=0,0; case 1: p.X,p.Y=10,0; case 2: p.X,p.Y=60,0 }
	}
	queues := make([]<-chan string, 3)
	for i,id := range ids { queues[i] = s.registerChat(id) }

	s.sendChat(ids[0], "local", "  Всем доброго дня!  ", "")
	read := func(ch <-chan string) map[string]any {
		t.Helper()
		select {
		case raw := <-ch:
			var event map[string]any
			if err := json.Unmarshal([]byte(raw), &event); err != nil { t.Fatalf("bad event JSON: %v", err) }
			return event
		case <-time.After(100*time.Millisecond): t.Fatal("chat event not delivered"); return nil
		}
	}
	a, b := read(queues[0]), read(queues[1])
	if a["type"] != "chat" || a["text"] != "Всем доброго дня!" || b["text"] != a["text"] {
		t.Fatalf("unexpected local events: A=%v B=%v",a,b)
	}
	select { case got:=<-queues[2]: t.Fatalf("out-of-range resident received local chat: %s",got); default: }

	s.mu.Lock(); s.chatPeers[ids[0]].lastSent = time.Now().Add(-time.Second); s.mu.Unlock()
	s.sendChat(ids[0], "whisper", "Только тебе", eid(ids[1]))
	wa, wb := read(queues[0]), read(queues[1])
	if wa["channel"]!="whisper" || wb["channel"]!="whisper" || wb["text"]!="Только тебе" {
		t.Fatalf("unexpected whisper: A=%v B=%v",wa,wb)
	}
	select { case got:=<-queues[2]: t.Fatalf("third resident received whisper: %s",got); default: }
}

func TestChatRejectsOversizeAndSlowPeerDoesNotBlock(t *testing.T) {
	s := NewStarter("chat-backpressure", "", []byte("secret"), "")
	ids:=make([]core.ID,0,2)
	s.world.Each("place",func(e *core.Entity){if len(ids)<2 && core.Mind(e)!=nil {ids=append(ids,e.ID)}})
	if len(ids)!=2 {t.Fatal("need two residents")}
	base:=core.Place(s.world.Get(ids[0])).Settlement
	for i,id:=range ids { e:=s.world.Get(id); core.Place(e).Settlement=base; p:=sim.EnsureScenePos(s.world,e); p.X=float64(i); p.Y=0 }
	q1:=s.registerChat(ids[0]); q2:=s.registerChat(ids[1])
	s.sendChat(ids[0],"local",stringsOf('x',501),"")
	select {case <-q1:t.Fatal("oversize message accepted");default:}
	for i:=0;i<chatQueueSize;i++ { q2peer:=s.chatPeers[ids[1]]; q2peer.events<- "queued" }
	s.sendChat(ids[0],"local","still responsive","")
	select {case <-q1:case <-time.After(100*time.Millisecond):t.Fatal("slow peer blocked sender delivery")}
	select {case <-q2:default:t.Fatal("bounded queue unexpectedly empty")}
}

func stringsOf(r rune,n int) string { out:=make([]rune,n); for i:=range out {out[i]=r}; return string(out) }
