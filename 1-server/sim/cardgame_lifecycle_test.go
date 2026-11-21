package sim

import (
	"testing"

	"pogranichye/core"
)

// playingOchko раздаёт партию, которая остаётся незавершённой (15 у игрока, 17 у банкира), чтобы
// проверять жизненный цикл заброшенности, а не мгновенный исход.
func playingOchko() func([]OchkoCard) error {
	return fixedOchkoDeck(map[int]OchkoCard{
		51: {Code: "10♠", Value: 10}, 50: {Code: "5♣", Value: 5}, // игрок: 15
		49: {Code: "10♦", Value: 10}, 48: {Code: "7♥", Value: 7}, // банкир: 17
	})
}

// Активную недавно тронутую партию часовой свип не трогает — короткий обрыв связи она переживает.
func TestCardGameFreshSurvivesSweep(t *testing.T) {
	w := core.NewWorld("cg-fresh")
	e, _ := drinker(w, 100, 0)
	if ok, why := StartOchko(w, e, "start", playingOchko()); !ok {
		t.Fatalf("start: %s", why)
	}
	TouchCardGame(e, 100) // ход только что сделан на игровой час 100
	if CardGameStale(e, 100) {
		t.Fatal("только что тронутая партия не должна считаться заброшенной")
	}
	CardGameSystem{}.Hour(w, core.Clock{TotalHours: 101}, nil) // всего час спустя
	if g := Ochko(e); g == nil || g.Done {
		t.Fatalf("партию в пределах порога нельзя закрывать свипом: %#v", g)
	}
}

// Партия без ходов дольше порога закрывается часовым свипом; ставка заведению не возвращается.
func TestCardGameAbandonedBySweep(t *testing.T) {
	w := core.NewWorld("cg-abandon")
	e, _ := drinker(w, 100, 0)
	if ok, why := StartOchko(w, e, "start", playingOchko()); !ok {
		t.Fatalf("start: %s", why)
	}
	TouchCardGame(e, 100)
	coins := core.Purse(e).Coins // ставка уже списана при старте
	now := int64(100 + CardGameAbandonHours)
	if !CardGameStale(e, now) {
		t.Fatal("партия без ходов дольше порога должна считаться заброшенной")
	}
	CardGameSystem{}.Hour(w, core.Clock{TotalHours: now}, nil)
	g := Ochko(e)
	if g == nil || !g.Done || g.Result != "abandoned" {
		t.Fatalf("свип должен закрыть заброшенную партию: %#v", g)
	}
	if got := core.Purse(e).Coins; got != coins {
		t.Fatalf("брошенная ставка не возвращается заведению: было %.2f стало %.2f", coins, got)
	}
}

// Старое сохранение без поля Touched (==0): осиротевшая партия не должна держать игрока вечно.
func TestCardGameLegacyZeroTouchedIsStale(t *testing.T) {
	w := core.NewWorld("cg-legacy")
	e, _ := drinker(w, 100, 0)
	if ok, why := StartOchko(w, e, "start", playingOchko()); !ok {
		t.Fatalf("start: %s", why)
	}
	Ochko(e).Touched = 0 // как будто загружено из сохранения до появления поля
	if !CardGameStale(e, CardGameAbandonHours) {
		t.Fatal("партия с нулевым Touched за порогом должна быть заброшенной")
	}
}

// После того как партия заброшена (таймаут/обрыв), новую можно начать сразу — обработчик сам закрывает
// осиротевшую перед стартом (повторяем логику сервера через Abandon+Start).
func TestCardGameStartAfterAbandon(t *testing.T) {
	w := core.NewWorld("cg-restart")
	e, _ := drinker(w, 100, 0)
	if ok, why := StartOchko(w, e, "start-1", playingOchko()); !ok {
		t.Fatalf("start-1: %s", why)
	}
	TouchCardGame(e, 100)
	// до закрытия новая партия отклоняется — игрок «уже играет»
	if ok, why := StartOchko(w, e, "start-2", playingOchko()); ok || why == "" {
		t.Fatalf("поверх активной партии старт должен отклоняться: ok=%t why=%q", ok, why)
	}
	// партия простояла дольше порога — как сделал бы сервер, закрываем её и начинаем заново
	now := int64(100 + CardGameAbandonHours)
	if CardGameStale(e, now) {
		AbandonCardGame(e)
	}
	if ok, why := StartOchko(w, e, "start-3", playingOchko()); !ok {
		t.Fatalf("после закрытия заброшенной партии новая должна начаться: %s", why)
	}
	if g := Ochko(e); g == nil || g.Done {
		t.Fatalf("новая партия должна идти: %#v", g)
	}
}

// AbandonCardGame идемпотентна: на завершённой партии ничего не меняет и сообщает, что не закрывала.
func TestAbandonCardGameIdempotent(t *testing.T) {
	w := core.NewWorld("cg-idem")
	e, _ := drinker(w, 100, 0)
	if ok, why := StartOchko(w, e, "start", playingOchko()); !ok {
		t.Fatalf("start: %s", why)
	}
	if !AbandonCardGame(e) {
		t.Fatal("первый вызов должен закрыть активную партию")
	}
	res := Ochko(e).Result
	if AbandonCardGame(e) {
		t.Fatal("повторный вызов на завершённой партии не должен ничего менять")
	}
	if Ochko(e).Result != res {
		t.Fatal("повторный Abandon не должен переписывать исход")
	}
}

// Дурак проходит тот же жизненный цикл: незавершённая партия закрывается по таймауту свипом.
func TestCardGameDurakAbandonedBySweep(t *testing.T) {
	w := core.NewWorld("cg-durak")
	e, _ := drinker(w, 100, 0)
	if ok, why := StartDurak(w, e, "start", quietDurakDeck()); !ok {
		t.Fatalf("start durak: %s", why)
	}
	TouchCardGame(e, 100)
	now := int64(100 + CardGameAbandonHours)
	if !CardGameStale(e, now) {
		t.Fatal("незавершённая партия в дурака должна стать заброшенной за порогом")
	}
	CardGameSystem{}.Hour(w, core.Clock{TotalHours: now}, nil)
	if g := Durak(e); g == nil || !g.Done {
		t.Fatalf("свип должен закрыть заброшенную партию в дурака: %#v", g)
	}
}
