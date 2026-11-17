package server

import (
	"os"
	"path/filepath"
	"testing"

	"pogranichye/core"
	"pogranichye/sim"
)

// Мир переживает перезапуск: сохранили → подняли новый сервер из файла → тот же мир (день, сущности).
func TestWorldSurvivesRestart(t *testing.T) {
	path := filepath.Join(t.TempDir(), "world.sav")
	a := NewStarter("save-seed", "", []byte("x"), "")
	for i := 0; i < 60; i++ {
		a.Step()
	}
	if err := a.SaveTo(path); err != nil {
		t.Fatal(err)
	}
	b := NewStarter("other-seed", "", []byte("x"), "")
	ok, err := b.LoadFrom(path)
	if err != nil || !ok {
		t.Fatalf("загрузка: ok=%v err=%v", ok, err)
	}
	if a.world.Count() != b.world.Count() || a.runner.Clock.TotalHours != b.runner.Clock.TotalHours {
		t.Fatalf("мир после загрузки другой: %d/%d сущностей, часы %d/%d", a.world.Count(), b.world.Count(), a.runner.Clock.TotalHours, b.runner.Clock.TotalHours)
	}
	// и дальше живёт так же (те же кости)
	a.Step()
	b.Step()
	if a.world.Count() != b.world.Count() {
		t.Fatal("после загрузки мир должен развиваться так же")
	}
	if ok, _ := NewStarter("x", "", []byte("x"), "").LoadFrom(filepath.Join(t.TempDir(), "none.sav")); ok {
		t.Fatal("нет файла — нет загрузки")
	}
}

// Летопись — компонент сущности: записи и Seq переживают disk snapshot/restart.
func TestJournalSurvivesWorldRestart(t *testing.T) {
	path := filepath.Join(t.TempDir(), "journal.sav")
	a := NewStarter("journal-save-seed", "", []byte("x"), "")
	player := a.world.Spawn(
		&core.IdentC{Name: "Путник"},
		&sim.JournalC{Seq: 2, Entries: []sim.JournalEntry{
			{Seq: 1, Day: 3, Kind: "met", Text: "Познакомился с жителем деревни."},
			{Seq: 2, Day: 4, Kind: "worked", Text: "Впервые поработал своими руками."},
		}},
	)
	if err := a.SaveTo(path); err != nil {
		t.Fatal(err)
	}
	b := NewStarter("other-journal-seed", "", []byte("x"), "")
	ok, err := b.LoadFrom(path)
	if err != nil || !ok {
		t.Fatalf("загрузка: ok=%v err=%v", ok, err)
	}
	loaded := b.world.Get(player.ID)
	if loaded == nil {
		t.Fatal("сущность игрока не пережила перезапуск")
	}
	journal, ok := loaded.Get("journal").(*sim.JournalC)
	if !ok || journal == nil {
		t.Fatal("компонент JournalC не пережил перезапуск")
	}
	if journal.Seq != 2 || len(journal.Entries) != 2 || journal.Entries[1].Kind != "worked" || journal.Entries[1].Day != 4 {
		t.Fatalf("летопись после загрузки повреждена: %#v", journal)
	}
}

func TestAutosaveZeroIntervalStillSavesOnClose(t *testing.T) {
	path := filepath.Join(t.TempDir(), "world.sav")
	s := NewStarter("autosave-disabled-periodic", "", []byte("x"), "")
	done := make(chan struct{})
	go func() {
		s.Autosave(path, 0)
		close(done)
	}()
	s.Close()
	<-done
	if info, err := os.Stat(path); err != nil || info.Size() == 0 {
		t.Fatalf("zero interval must still write a final world snapshot: info=%v err=%v", info, err)
	}
}
