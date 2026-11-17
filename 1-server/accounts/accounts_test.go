package accounts

import (
	"os"
	"path/filepath"
	"runtime"
	"testing"
)

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

// TestPersistsAcrossRestart — тот самый сценарий «каждый раз другой персонаж» (comms/баг
// 2026-11-14): если файл аккаунтов реально пишется на диск, второй Store с тем же путём (как
// при перезапуске сервера/контейнера) должен увидеть и логин, и привязку персонажа. До сих пор
// этот путь не был покрыт тестом вообще — все существующие тесты используют path="" (память
// без диска), так что регресс записи на диск ничем не ловился.
func TestPersistsAcrossRestart(t *testing.T) {
	path := filepath.Join(t.TempDir(), "accounts.json")

	a := NewStore(path, []byte("s"))
	u, err := a.Register("kate", "secret123")
	if err != nil {
		t.Fatalf("регистрация: %v", err)
	}
	if err := a.Bind(u.ID, 777); err != nil {
		t.Fatalf("привязка: %v", err)
	}

	// «перезапуск»: новый Store с тем же файлом, как при новом процессе сервера
	b := NewStore(path, []byte("s"))
	if _, _, err := b.Login("kate", "secret123"); err != nil {
		t.Fatalf("логин после перезапуска не прошёл — аккаунт не пережил диск: %v", err)
	}
	if got := b.CharOf(u.ID); got != 777 {
		t.Fatalf("привязка персонажа не пережила перезапуск: got=%d want=777 — ровно симптом «играю за другого»", got)
	}
}

// TestSaveFailureDoesNotCrash — если каталог для accounts.json недоступен на запись (в Docker
// это бывает из-за несовпадения владельца смонтированного ./data — см. docker-entrypoint.sh),
// Store раньше тихо проглатывал ошибку записи. Теперь она логируется, но важно, что сервис при
// этом НЕ падает и продолжает работать в памяти текущего процесса (хуже — только пережить
// перезапуск, не хуже).
func TestSaveFailureDoesNotCrash(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("права 0000 на Windows не действуют так же")
	}
	if os.Geteuid() == 0 {
		t.Skip("от root проверка прав бессмысленна — чтение/запись разрешены всегда")
	}
	dir := t.TempDir()
	if err := os.Chmod(dir, 0o500); err != nil { // только чтение+выполнение, запись запрещена
		t.Fatalf("chmod: %v", err)
	}
	t.Cleanup(func() { _ = os.Chmod(dir, 0o700) }) // иначе TempDir не сможет себя убрать

	s := NewStore(filepath.Join(dir, "accounts.json"), []byte("s"))
	if _, err := s.Register("bob", "pass1234"); err != nil {
		t.Fatalf("регистрация в памяти должна проходить даже при недоступном диску: %v", err)
	}
	if _, _, err := s.Login("bob", "pass1234"); err != nil {
		t.Fatalf("логин в той же сессии должен работать из памяти: %v", err)
	}
}
