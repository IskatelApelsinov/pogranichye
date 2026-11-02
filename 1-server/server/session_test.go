package server

import "testing"

// Одно тело — одна сессия: второй заход тем же игроком, пока тело под управлением, отклоняется;
// после освобождения — снова можно; чужую резервацию посторонним sid не снять.
func TestReserveBodyNoDoubleControl(t *testing.T) {
	s := New("session-test", 2, "", []byte("x"), "")
	if _, err := s.acc.Register("kate", "secret1"); err != nil {
		t.Fatalf("регистрация: %v", err)
	}
	tok, _, err := s.acc.Login("kate", "secret1")
	if err != nil {
		t.Fatalf("вход: %v", err)
	}
	uid, err := s.acc.Validate(tok)
	if err != nil {
		t.Fatalf("токен: %v", err)
	}

	cid1, sid1, st1 := s.reserveBody(uid)
	if st1 != "ok" || cid1 == 0 {
		t.Fatalf("первая сессия должна получить тело: статус=%s cid=%d", st1, cid1)
	}
	// второй одновременный заход тем же игроком — тело занято
	cid2, _, st2 := s.reserveBody(uid)
	if st2 != "busy" {
		t.Fatalf("второй заход должен быть отклонён как занятый, а не %q", st2)
	}
	if cid2 != cid1 {
		t.Fatalf("это должно быть то же тело: %d vs %d", cid2, cid1)
	}
	// посторонний sid не снимает чужую резервацию
	s.release(cid1, 999999)
	if _, _, st := s.reserveBody(uid); st != "busy" {
		t.Fatal("чужим sid резервацию снять нельзя — тело должно остаться занятым")
	}
	// своя сессия освобождает — и тогда переподключение разрешено
	s.release(cid1, sid1)
	cid3, _, st3 := s.reserveBody(uid)
	if st3 != "ok" || cid3 != cid1 {
		t.Fatalf("после освобождения переподключение в то же тело должно пройти: статус=%s cid=%d", st3, cid3)
	}
}
