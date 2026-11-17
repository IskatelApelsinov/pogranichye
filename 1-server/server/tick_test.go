package server

import (
	"testing"
	"time"
)

func TestDefaultWorldTickIsOnePointFourX(t *testing.T) {
	for name, s := range map[string]*Server{
		"world":   New("clock-default", 2, "", []byte("secret"), ""),
		"starter": NewStarter("clock-starter", "", []byte("secret"), ""),
	} {
		if s.tickEach != 2571429*time.Millisecond {
			t.Errorf("%s default tick = %s, want 42m51.429s (1.4x world time)", name, s.tickEach)
		}
		s.SetTick(0)
		if s.tickEach != 2571429*time.Millisecond {
			t.Errorf("%s non-positive SetTick changed default: %s", name, s.tickEach)
		}
		s.SetTick(400 * time.Millisecond)
		if s.tickEach != 400*time.Millisecond {
			t.Errorf("%s explicit test tick = %s, want 400ms", name, s.tickEach)
		}
	}
}
