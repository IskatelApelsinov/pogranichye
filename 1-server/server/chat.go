package server

import (
	"encoding/json"
	"strings"
	"time"
	"unicode/utf8"

	"pogranichye/core"
	"pogranichye/sim"
)

const (
	chatQueueSize = 32
	chatLocalRange = 35.0
	chatWhisperRange = 8.0
	chatMinInterval = 700 * time.Millisecond
)

type chatPeer struct {
	events chan string
	lastSent time.Time
}

func (s *Server) registerChat(id core.ID) <-chan string {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.chatPeers == nil { s.chatPeers = make(map[core.ID]*chatPeer) }
	p := &chatPeer{events: make(chan string, chatQueueSize)}
	s.chatPeers[id] = p
	return p.events
}

func (s *Server) unregisterChat(id core.ID) {
	s.mu.Lock()
	delete(s.chatPeers, id)
	s.mu.Unlock()
}

// sendChat validates identity, range and rate on the authoritative server. Delivery is
// non-blocking: a slow recipient drops its oldest queued event instead of stalling the world.
func (s *Server) sendChat(senderID core.ID, channel, rawText, rawTarget string) {
	text := strings.TrimSpace(rawText)
	if text == "" || !utf8.ValidString(text) || utf8.RuneCountInString(text) > 500 { return }
	if channel != "local" && channel != "whisper" { return }

	s.mu.Lock()
	defer s.mu.Unlock()
	sender := s.world.Get(senderID)
	peer := s.chatPeers[senderID]
	if sender == nil || peer == nil { return }
	now := time.Now()
	if !peer.lastSent.IsZero() && now.Sub(peer.lastSent) < chatMinInterval { return }

	sp := sim.ScenePos(sender)
	spl := core.Place(sender)
	if sp == nil || spl == nil || spl.Settlement == 0 { return }

	var targetID core.ID
	if channel == "whisper" {
		targetID = parseID(rawTarget)
		target := s.world.Get(targetID)
		tp := sim.ScenePos(target)
		tpl := core.Place(target)
		if targetID == 0 || target == nil || s.chatPeers[targetID] == nil || tp == nil ||
			tpl == nil || tpl.Settlement != spl.Settlement ||
			distanceSquared(sp.X, sp.Y, tp.X, tp.Y) > chatWhisperRange*chatWhisperRange {
			return
		}
	}

	s.chatSeq++
	seq := s.chatSeq
	senderIdent := core.Ident(sender)
	senderName, senderDesc := "", "местный"
	if senderIdent != nil {
		senderName = strings.TrimSpace(senderIdent.Name + " " + senderIdent.Surname)
		senderDesc = sim.DescFor(senderIdent.Role)
	}
	recipients := make([]core.ID, 0, len(s.chatPeers))
	if channel == "whisper" {
		recipients = append(recipients, senderID, targetID)
	} else {
		for id, recipient := range s.chatPeers {
			if recipient == nil || s.world.Get(id) == nil { continue }
			e := s.world.Get(id)
			pl, pos := core.Place(e), sim.ScenePos(e)
			if pl == nil || pos == nil || pl.Settlement != spl.Settlement { continue }
			if distanceSquared(sp.X, sp.Y, pos.X, pos.Y) <= chatLocalRange*chatLocalRange {
				recipients = append(recipients, id)
			}
		}
	}
	eventFor := func(recipientID core.ID) string {
		name := ""
		desc := senderDesc
		if recipientID == senderID || sim.Knows(s.world.Get(recipientID), senderID) {
			name = senderName
			desc = ""
		}
		b, _ := json.Marshal(map[string]any{
			"type": "chat", "seq": seq, "sent_at": now.UnixMilli(),
			"channel": channel, "from_id": eid(senderID), "from_name": name,
			"from_desc": desc, "text": text,
		})
		return string(b)
	}
	for _, id := range recipients {
		p := s.chatPeers[id]
		if p == nil { continue }
		event := eventFor(id)
		select {
		case p.events <- event:
		default:
			select { case <-p.events: default: }
			select { case p.events <- event: default: }
		}
	}
	peer.lastSent = now
}

func distanceSquared(ax, ay, bx, by float64) float64 {
	dx, dy := ax-bx, ay-by
	return dx*dx + dy*dy
}
