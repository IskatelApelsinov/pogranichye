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
	voiceSignalMinInterval = 10 * time.Millisecond
	voiceMaxSignalBytes = 64 * 1024
)

func voiceNearby(a, b *core.Entity) bool {
	if a == nil || b == nil { return false }
	ap, bp := core.Place(a), core.Place(b)
	ax, bx := sim.ScenePos(a), sim.ScenePos(b)
	if ap == nil || bp == nil || ap.Settlement == 0 || ap.Settlement != bp.Settlement || ax == nil || bx == nil {
		return false
	}
	return distanceSquared(ax.X, ax.Y, bx.X, bx.Y) <= chatLocalRange*chatLocalRange
}

func (s *Server) queueVoiceLocked(to core.ID, event map[string]any) {
	peer := s.chatPeers[to]
	if peer == nil { return }
	payload, err := json.Marshal(event)
	if err != nil { return }
	message := string(payload)
	select {
	case peer.events <- message:
	default:
		select { case <-peer.events: default: }
		select { case peer.events <- message: default: }
	}
}

func (s *Server) signalVoice(from core.ID, action, rawTarget, sdp, candidate, mid string, mline int) {
	if len(sdp) > voiceMaxSignalBytes || len(candidate) > 4096 ||
		!utf8.ValidString(sdp) || !utf8.ValidString(candidate) { return }
	switch action {
	case "join", "leave", "offer", "answer", "ice":
	default: return
	}
	if (action == "offer" || action == "answer") && strings.TrimSpace(sdp) == "" { return }
	if action == "ice" && strings.TrimSpace(candidate) == "" { return }

	s.mu.Lock()
	defer s.mu.Unlock()
	sender := s.world.Get(from)
	peer := s.chatPeers[from]
	if sender == nil || peer == nil { return }
	now := time.Now()

	if action == "join" {
		if peer.voice { return }
		peer.voice = true
		for id, other := range s.chatPeers {
			if id == from || other == nil || !other.voice || !voiceNearby(sender, s.world.Get(id)) { continue }
			s.queueVoiceLocked(from, map[string]any{"type":"voice_signal","action":"peer_joined","from_id":eid(id),"sent_at":now.UnixMilli()})
			s.queueVoiceLocked(id, map[string]any{"type":"voice_signal","action":"peer_joined","from_id":eid(from),"sent_at":now.UnixMilli()})
		}
		return
	}
	if action == "leave" {
		if !peer.voice { return }
		peer.voice = false
		for id, other := range s.chatPeers {
			if id == from || other == nil || !other.voice || !voiceNearby(sender, s.world.Get(id)) { continue }
			s.queueVoiceLocked(id, map[string]any{"type":"voice_signal","action":"peer_left","from_id":eid(from),"sent_at":now.UnixMilli()})
		}
		return
	}
	if !peer.voice || (!peer.lastVoiceSignal.IsZero() && now.Sub(peer.lastVoiceSignal) < voiceSignalMinInterval) { return }
	targetID := parseID(rawTarget)
	target := s.world.Get(targetID)
	targetPeer := s.chatPeers[targetID]
	if targetID == 0 || target == nil || targetPeer == nil || !targetPeer.voice || !voiceNearby(sender, target) { return }

	event := map[string]any{
		"type":"voice_signal", "action":action, "from_id":eid(from), "sent_at":now.UnixMilli(),
	}
	switch action {
	case "offer", "answer":
		event["sdp"] = sdp
	case "ice":
		event["candidate"] = candidate
		event["sdp_mid"] = mid
		event["sdp_mline_index"] = mline
	}
	s.queueVoiceLocked(targetID, event)
	peer.lastVoiceSignal = now
}
