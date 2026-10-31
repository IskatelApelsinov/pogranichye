package accounts

import "testing"

func TestRegisterLoginToken(t *testing.T) {
	s := NewStore("", []byte("test-secret"))
	u, err := s.Register("Kate", "secret123")
	if err != nil {
		t.Fatalf("регистрация: %v", err)
	}
	if _, err := s.Register("kate", "other"); err != ErrExists {
		t.Fatalf("повторная регистрация должна отвергаться (регистр не важен), а дала: %v", err)
	}
	// вход неверным паролем
	if _, _, err := s.Login("kate", "wrong"); err != ErrBadPass {
		t.Fatalf("неверный пароль должен отвергаться, а дал: %v", err)
	}
	// вход верным паролем → токен
	tok, u2, err := s.Login("KATE", "secret123")
	if err != nil || u2.ID != u.ID {
		t.Fatalf("вход верным паролем не удался: %v", err)
	}
	// токен валиден и указывает на того же пользователя
	uid, err := s.Validate(tok)
	if err != nil || uid != u.ID {
		t.Fatalf("валидация токена: uid=%d err=%v", uid, err)
	}
}

func TestBadTokenRejected(t *testing.T) {
	s := NewStore("", []byte("secret-A"))
	u, _ := s.Register("bob", "pass1234")
	tok, _, _ := s.Login("bob", "pass1234")

	// подделка: тот же токен под другим секретом не проходит
	other := NewStore("", []byte("secret-B"))
	if _, err := other.Validate(tok); err == nil {
		t.Fatal("токен, подписанный чужим секретом, не должен валидироваться")
	}
	// мусор
	if _, err := s.Validate("garbage.token"); err == nil {
		t.Fatal("мусорный токен должен отвергаться")
	}
	_ = u
}

func TestBindCharacter(t *testing.T) {
	s := NewStore("", []byte("s"))
	u, _ := s.Register("ann", "pass1234")
	if s.CharOf(u.ID) != 0 {
		t.Fatal("до привязки персонажа быть не должно")
	}
	if err := s.Bind(u.ID, 4242); err != nil {
		t.Fatalf("привязка: %v", err)
	}
	if s.CharOf(u.ID) != 4242 {
		t.Fatal("после привязки CharOf должен вернуть id персонажа")
	}
}
