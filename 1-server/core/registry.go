package core

import "sync"

// Реестр компонентов: ключ (Kind) → как создать пустой компонент этого вида. Нужен,
// чтобы снимок мира восстанавливался без потерь: читая {kind: json}, ядро знает, во что
// именно разворачивать сырой json. Каждый пакет-компонент регистрирует себя в init(),
// поэтому добавление нового свойства не требует правок ядра — только собственный Register.
var (
	regMu    sync.RWMutex
	registry = map[string]func() Component{}
)

// Register объявляет вид компонента. make должен возвращать СВЕЖИЙ нулевой компонент
// нужного конкретного типа (указатель), в который затем разворачивается json снимка.
func Register(kind string, make func() Component) {
	regMu.Lock()
	defer regMu.Unlock()
	registry[kind] = make
}

// newComponent создаёт пустой компонент по виду (nil, если вид неизвестен — такой
// компонент снимок пропустит, но не упадёт: неизвестное свойство не рушит загрузку).
func newComponent(kind string) Component {
	regMu.RLock()
	defer regMu.RUnlock()
	if mk, ok := registry[kind]; ok {
		return mk()
	}
	return nil
}

// KnownKinds — список зарегистрированных видов (для диагностики/инструментов разработки).
func KnownKinds() []string {
	regMu.RLock()
	defer regMu.RUnlock()
	out := make([]string, 0, len(registry))
	for k := range registry {
		out = append(out, k)
	}
	return out
}
