// Пакет dice — «наши кости»: детерминированный, платформо-стабильный генератор
// случайности (блок 1.3 дерева). Портирован с прототипа (world/dice.py), алгоритм
// тот же, чтобы мир вёл себя предсказуемо на любой машине и бросок можно было
// воспроизвести по seed. Никакого math/rand: его поток не воспроизводим между
// версиями Go, а нам нужна железная повторяемость (реплеи, честные игры, тесты).
//
// Устройство:
//
//	splitmix64 — крошечный, быстрый PRNG, состояние = одно 64-битное число.
//	Seed64     — из мирового семени + имени потока лепим стартовое состояние (SHA-256).
//	Stream     — независимый поток случайности с удобными бросками (d6, шанс, выбор...).
//	Hub        — выдаёт именованные потоки: добавил новую систему — не сдвинул чужой бросок.
package dice

import (
	"crypto/sha256"
	"encoding/binary"
	"math"
	"sort"
)

const (
	phi  uint64 = 0x9E3779B97F4A7C15 // золотое сечение — классический шаг splitmix64
	mask        = ^uint64(0)
)

// splitmix64 — один шаг генератора. Возвращает новое состояние и выданное число.
func splitmix64(state uint64) (uint64, uint64) {
	state += phi
	z := state
	z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
	z = (z ^ (z >> 27)) * 0x94D049BB133111EB
	z = z ^ (z >> 31)
	return state, z
}

// Seed64 собирает 64-битное семя из мирового семени и произвольного «ключа» потока
// (имя системы, id сущности, номер раунда...). Через SHA-256, чтобы близкие ключи
// («hunt»/«hunt2», aid 1/2) давали совсем разные потоки, а не соседние.
func Seed64(world string, key ...string) uint64 {
	h := sha256.New()
	h.Write([]byte(world))
	for _, k := range key {
		h.Write([]byte{0}) // разделитель, чтобы "ab"+"c" != "a"+"bc"
		h.Write([]byte(k))
	}
	sum := h.Sum(nil)
	return binary.BigEndian.Uint64(sum[:8])
}

// Stream — независимый поток случайности. Хранит одно число состояния, поэтому
// дёшево сериализуется и возобновляется с той же точки (сохранения, реплеи).
type Stream struct {
	state uint64
}

// NewStream создаёт поток из готового 64-битного семени.
func NewStream(seed uint64) *Stream { return &Stream{state: seed} }

// next выдаёт очередное сырое 64-битное число потока.
func (s *Stream) next() uint64 {
	ns, z := splitmix64(s.state)
	s.state = ns
	return z
}

// State / Restore — для сохранения и точного возобновления потока.
func (s *Stream) State() uint64     { return s.state }
func (s *Stream) Restore(st uint64) { s.state = st }

// Float — вещественное в [0,1).
func (s *Stream) Float() float64 {
	// 53 старших бита → равномерно в [0,1), как это делают качественные RNG
	return float64(s.next()>>11) / (1 << 53)
}

// Intn — целое в [0,n). При n<=0 возвращает 0.
func (s *Stream) Intn(n int) int {
	if n <= 0 {
		return 0
	}
	return int(s.next() % uint64(n))
}

// Between — целое в [a,b] включительно.
func (s *Stream) Between(a, b int) int {
	if b < a {
		a, b = b, a
	}
	return a + s.Intn(b-a+1)
}

// Uniform — вещественное в [a,b).
func (s *Stream) Uniform(a, b float64) float64 { return a + (b-a)*s.Float() }

// Roll — бросок кости с sides гранями, результат 1..sides (d6 по умолчанию зовётся Roll(6)).
func (s *Stream) Roll(sides int) int {
	if sides < 1 {
		sides = 1
	}
	return 1 + s.Intn(sides)
}

// Chance — true с вероятностью p (0..1).
func (s *Stream) Chance(p float64) bool { return s.Float() < p }

// Choice — равновероятный выбор индекса из n элементов (-1 при n<=0).
func (s *Stream) Choice(n int) int {
	if n <= 0 {
		return -1
	}
	return s.Intn(n)
}

// Weighted — взвешенный выбор индекса по весам (возвращает -1, если сумма <= 0).
// Отрицательные веса считаются нулём.
func (s *Stream) Weighted(weights []float64) int {
	total := 0.0
	for _, w := range weights {
		if w > 0 {
			total += w
		}
	}
	if total <= 0 {
		return -1
	}
	r := s.Float() * total
	acc := 0.0
	for i, w := range weights {
		if w > 0 {
			acc += w
			if r < acc {
				return i
			}
		}
	}
	return len(weights) - 1
}

// Shuffle — перемешать n индексов (Фишер–Йейтс на нашем потоке); зовёт swap(i,j).
func (s *Stream) Shuffle(n int, swap func(i, j int)) {
	for i := n - 1; i > 0; i-- {
		j := int(s.next() % uint64(i+1))
		swap(i, j)
	}
}

// Normal — приближение нормального распределения (сумма 3 равномерных, по ЦПТ);
// пригодится для «естественной неровности» свойств при генерации жителей.
func (s *Stream) Normal(mean, std float64) float64 {
	sum := s.Float() + s.Float() + s.Float() - 1.5
	return mean + std*sum*math.Sqrt(2.0) // грубая, но дешёвая колоколообразная форма
}

// Hub раздаёт именованные потоки от общего мирового семени. Ключ потока —
// это имя (+необязательные под-ключи), так что добавление новой системы не сдвигает
// случайность уже существующих: у каждого свой поток.
type Hub struct {
	world   string
	streams map[string]*Stream
}

// NewHub создаёт хаб для мира с данным семенем.
func NewHub(worldSeed string) *Hub {
	return &Hub{world: worldSeed, streams: map[string]*Stream{}}
}

// Stream возвращает (создавая при первом обращении) именованный поток.
// Подключи любое число под-ключей: Stream("combat", aid) и т.п.
func (h *Hub) Stream(name string, sub ...string) *Stream {
	key := name
	for _, s := range sub {
		key += "\x00" + s
	}
	if st, ok := h.streams[key]; ok {
		return st
	}
	st := NewStream(Seed64(h.world, append([]string{name}, sub...)...))
	h.streams[key] = st
	return st
}

// Once — одноразовый поток: каждый раз свежий бросок от семени+ключа, состояние не хранится.
// Для «кинуть кость прямо сейчас, не заводя поток» (например, разовое событие).
func (h *Hub) Once(name string, sub ...string) *Stream {
	return NewStream(Seed64(h.world, append([]string{name}, sub...)...))
}

// Snapshot / LoadSnapshot — сохранить и восстановить состояния всех живых потоков,
// чтобы после загрузки мир продолжил ту же последовательность бросков.
func (h *Hub) Snapshot() HubState {
	keys := make([]string, 0, len(h.streams))
	for k := range h.streams {
		keys = append(keys, k)
	}
	sort.Strings(keys) // стабильный порядок — детерминированный снимок
	states := make(map[string]uint64, len(keys))
	for _, k := range keys {
		states[k] = h.streams[k].State()
	}
	return HubState{World: h.world, Streams: states}
}

// LoadHub восстанавливает хаб из снимка.
func LoadHub(st HubState) *Hub {
	h := &Hub{world: st.World, streams: map[string]*Stream{}}
	for k, s := range st.Streams {
		h.streams[k] = NewStream(s)
	}
	return h
}

// HubState — сериализуемый снимок хаба (попадает в общий снимок мира).
type HubState struct {
	World   string            `json:"world"`
	Streams map[string]uint64 `json:"streams"`
}

var _ = mask // держим mask экспортируемо-нейтральным (документированная константа ширины)
