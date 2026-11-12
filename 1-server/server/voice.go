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

func voiceRoom(e *core.Entity) string {
	if e == nil { return "" }
	p := core.Place(e)
	if p == nil || p.Settlement == 0 { return "" }
	return eid(p.Settlement)
}

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
	case "join", "leave", "offer", "answer", "ice", "ice_complete":
	default: return
	}
	if (action == "offer" || action == "answer") && strings.TrimSpace(sdp) == "" { return }
	if action == "ice" && strings.TrimSpace(candidate) == "" { return }
	if action == "ice_complete" && strings.TrimSpace(candidate) != "" { return }
	if len(mid) > 256 || mline < 0 || mline > 64 { return }

	s.mu.Lock()
	defer s.mu.Unlock()
	sender := s.world.Get(from)
	peer := s.chatPeers[from]
	if sender == nil || peer == nil { return }
	spl := core.Place(sender)
	if spl == nil || spl.Settlement == 0 { return }
	now := time.Now()

	if action == "join" {
		if peer.voice { return }
		peer.voice = true
		for id, other := range s.chatPeers {
			if id == from || other == nil || !other.voice || !voiceNearby(sender, s.world.Get(id)) { continue }
			room := eid(spl.Settlement)
			s.queueVoiceLocked(from, map[string]any{"type":"voice_signal","action":"peer_joined","signaling_type":"welcome","from_id":eid(id),"room":room,"sent_at":now.UnixMilli()})
			s.queueVoiceLocked(id, map[string]any{"type":"voice_signal","action":"peer_joined","signaling_type":"join","from_id":eid(from),"room":room,"sent_at":now.UnixMilli()})
		}
		return
	}
	if action == "leave" {
		if !peer.voice { return }
		peer.voice = false
		for id, other := range s.chatPeers {
			if id == from || other == nil || !other.voice || !voiceNearby(sender, s.world.Get(id)) { continue }
			s.queueVoiceLocked(id, map[string]any{"type":"voice_signal","action":"peer_left","from_id":eid(from),"room":voiceRoom(sender),"sent_at":now.UnixMilli()})
		}
		return
	}
	if !peer.voice || (!peer.lastVoiceSignal.IsZero() && now.Sub(peer.lastVoiceSignal) < voiceSignalMinInterval) { return }
	targetID := parseID(rawTarget)
	target := s.world.Get(targetID)
	targetPeer := s.chatPeers[targetID]
	if targetID == 0 || target == nil || targetPeer == nil || !targetPeer.voice || !voiceNearby(sender, target) { return }

	event := map[string]any{
		"type":"voice_signal", "action":action, "from_id":eid(from), "room":eid(spl.Settlement), "sent_at":now.UnixMilli(),
	}
	switch action {
	case "offer", "answer":
		event["sdp"] = sdp
	case "ice":
		event["candidate"] = candidate
		event["sdp_mid"] = mid
		event["sdp_mline_index"] = mline
	case "ice_complete":
		event["sdp_mid"] = mid
		event["sdp_mline_index"] = mline
	}
	s.queueVoiceLocked(targetID, event)
	peer.lastVoiceSignal = now
}

// pruneVoicePeersLocked ends demo P2P links once their souls leave the authoritative
// voice radius. Called from fast-frame generation while s.mu is already held.
func (s *Server) pruneVoicePeersLocked() {
	for id, peer := range s.chatPeers {
		if peer == nil || !peer.voice { continue }
		e := s.world.Get(id)
		outOfRange := false
		for otherID, other := range s.chatPeers {
			if otherID == id || other == nil || !other.voice { continue }
			if !voiceNearby(e, s.world.Get(otherID)) { outOfRange = true; break }
		}
		if !outOfRange { continue }
		peer.voice = false
		now := time.Now().UnixMilli()
		for otherID, other := range s.chatPeers {
			if otherID == id || other == nil || !other.voice { continue }
			s.queueVoiceLocked(otherID, map[string]any{"type":"voice_signal","action":"peer_left","from_id":eid(id),"room":voiceRoom(s.world.Get(otherID)),"sent_at":now})
			s.queueVoiceLocked(id, map[string]any{"type":"voice_signal","action":"peer_left","from_id":eid(otherID),"room":voiceRoom(e),"sent_at":now})
		}
	}
}
