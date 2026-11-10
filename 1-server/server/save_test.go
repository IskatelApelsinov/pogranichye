package server

import (
	"path/filepath"
	"testing"
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
