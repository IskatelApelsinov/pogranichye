// Пакет accounts — служба аккаунтов. По замыслу Kate она ОТДЕЛЬНА от мира и НИЧЕГО про мир
// не знает: только пользователи, вход и то, каким персонажем управляет пользователь. Мир-сервер
// доверяет её токену и подключает игрока к телу. Регистрация едина для любых клиентов (веб,
// Unity, мобильный) — все ходят в одну службу.
//
// Токен — HMAC-подпись «userID|срок» (подделать без секрета нельзя). Пароли — соль + растяжка
// SHA-256. ВАЖНО (пометка для Kate-безопасника): для прода пароли держать на bcrypt/argon2, а
// службу вынести отдельным процессом за TLS; здесь прототип, намеренно без внешних зависимостей.
package accounts

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"
)

var (
	ErrExists   = errors.New("такой пользователь уже есть")
	ErrNoUser   = errors.New("нет такого пользователя")
	ErrBadPass  = errors.New("неверный пароль")
	ErrBadToken = errors.New("недействительный токен")
)

const (
	stretch  = 60000           // итераций растяжки пароля
	tokenTTL = 720 * time.Hour // срок жизни токена (30 дней)
)

// User — запись пользователя. Пароль не хранится — только соль и растянутый хеш.
type User struct {
	ID      uint64 `json:"id"`
	Login   string `json:"login"`
	Salt    string `json:"salt"`
	Hash    string `json:"hash"`
	CharID  uint64 `json:"char_id"` // каким ПЕРСОНАЖЕМ мира управляет (0 — ещё не привязан)
	Created int64  `json:"created"`
}

// Store — хранилище аккаунтов (в памяти + JSON-файл). Потокобезопасно.
type Store struct {
	mu     sync.RWMutex
	secret []byte
	path   string
	nextID uint64
	byID   map[uint64]*User
	byName map[string]*User
}

// NewStore создаёт/загружает хранилище. secret — ключ подписи токенов (из env в проде).
func NewStore(path string, secret []byte) *Store {
	s := &Store{secret: secret, path: path, nextID: 1,
		byID: map[uint64]*User{}, byName: map[string]*User{}}
	s.load()
	if len(s.secret) == 0 {
		s.secret = make([]byte, 32)
		_, _ = rand.Read(s.secret) // эфемерный ключ, если не задан (токены живут до перезапуска)
	}
	return s
}

func (s *Store) hashPass(salt, pass string) string {
	h := []byte(salt + pass)
	for i := 0; i < stretch; i++ { // растяжка: дороже перебор
		sum := sha256.Sum256(h)
		h = sum[:]
	}
	return base64.StdEncoding.EncodeToString(h)
}

// Register заводит пользователя. Логин нормализуется (нижний регистр, обрезка пробелов).
func (s *Store) Register(login, pass string) (*User, error) {
	login = strings.ToLower(strings.TrimSpace(login))
	if login == "" || len(pass) < 4 {
		return nil, errors.New("логин пуст или пароль короче 4 символов")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if _, ok := s.byName[login]; ok {
		return nil, ErrExists
	}
	saltRaw := make([]byte, 12)
	_, _ = rand.Read(saltRaw)
	salt := base64.StdEncoding.EncodeToString(saltRaw)
	u := &User{ID: s.nextID, Login: login, Salt: salt, Hash: s.hashPass(salt, pass),
		Created: time.Now().Unix()}
	s.nextID++
	s.byID[u.ID] = u
	s.byName[login] = u
	s.save()
	return u, nil
}

// Login проверяет пароль и выдаёт токен.
func (s *Store) Login(login, pass string) (string, *User, error) {
	login = strings.ToLower(strings.TrimSpace(login))
	s.mu.RLock()
	u := s.byName[login]
	s.mu.RUnlock()
	if u == nil {
		return "", nil, ErrNoUser
	}
	want := s.hashPass(u.Salt, pass)
	if subtle.ConstantTimeCompare([]byte(want), []byte(u.Hash)) != 1 {
		return "", nil, ErrBadPass
	}
	return s.issue(u.ID), u, nil
}

// issue подписывает токен «userID|expiry».
func (s *Store) issue(uid uint64) string {
	exp := time.Now().Add(tokenTTL).Unix()
	body := fmt.Sprintf("%d|%d", uid, exp)
	mac := hmac.New(sha256.New, s.secret)
	mac.Write([]byte(body))
	sig := base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
	return base64.RawURLEncoding.EncodeToString([]byte(body)) + "." + sig
}

// Validate проверяет подпись и срок, возвращает userID.
func (s *Store) Validate(token string) (uint64, error) {
	parts := strings.SplitN(token, ".", 2)
	if len(parts) != 2 {
		return 0, ErrBadToken
	}
	bodyRaw, err := base64.RawURLEncoding.DecodeString(parts[0])
	if err != nil {
		return 0, ErrBadToken
	}
	mac := hmac.New(sha256.New, s.secret)
	mac.Write(bodyRaw)
	wantSig := base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
	if subtle.ConstantTimeCompare([]byte(wantSig), []byte(parts[1])) != 1 {
		return 0, ErrBadToken
	}
	var uid uint64
	var exp int64
	if _, err := fmt.Sscanf(string(bodyRaw), "%d|%d", &uid, &exp); err != nil {
		return 0, ErrBadToken
	}
	if time.Now().Unix() > exp {
		return 0, ErrBadToken
	}
	return uid, nil
}

// Bind привязывает пользователя к персонажу мира (его «тело»). Служба хранит лишь id —
// что это за персонаж и что он умеет, знает МИР, не аккаунты.
func (s *Store) Bind(uid, charID uint64) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	u := s.byID[uid]
	if u == nil {
		return ErrNoUser
	}
	u.CharID = charID
	s.save()
	return nil
}

// CharOf возвращает id персонажа пользователя (0 — не привязан).
func (s *Store) CharOf(uid uint64) uint64 {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if u := s.byID[uid]; u != nil {
		return u.CharID
	}
	return 0
}

// --- персистентность (простой JSON-файл) ---

type fileModel struct {
	NextID uint64  `json:"next_id"`
	Users  []*User `json:"users"`
}

func (s *Store) load() {
	if s.path == "" {
		return
	}
	data, err := os.ReadFile(s.path)
	if err != nil {
		return
	}
	var fm fileModel
	if json.Unmarshal(data, &fm) != nil {
		return
	}
	s.nextID = fm.NextID
	for _, u := range fm.Users {
		s.byID[u.ID] = u
		s.byName[u.Login] = u
	}
}

func (s *Store) save() {
	if s.path == "" {
		return
	}
	fm := fileModel{NextID: s.nextID}
	for _, u := range s.byID {
		fm.Users = append(fm.Users, u)
	}
	if data, err := json.MarshalIndent(fm, "", " "); err == nil {
		_ = os.WriteFile(s.path, data, 0600)
	}
}

// Count — сколько пользователей (для диагностики).
func (s *Store) Count() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.byID)
}

var _ = strconv.Itoa // зарезервировано для будущих численных полей
