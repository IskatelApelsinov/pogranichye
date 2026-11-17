package core

// Базовые свойства живого мира. Это НЕ исчерпывающий список — это стартовый набор.
// Новое свойство (тело по частям, инвентарь, репутация, род, вера...) добавляется здесь
// или в своём пакете одной структурой + Register в init(): ядро и системы не меняются.
// Каждый компонент — указатель-структура с Kind() и регистрацией (для снимка без потерь).

func init() {
	Register("ident", func() Component { return &IdentC{} })
	Register("vitals", func() Component { return &VitalsC{} })
	Register("drives", func() Component { return &DrivesC{} })
	Register("mind", func() Component { return &MindC{} })
	Register("skills", func() Component { return &SkillsC{Skills: map[string]float64{}} })
	Register("purse", func() Component { return &PurseC{Debts: map[string]float64{}} })
	Register("home", func() Component { return &HomeC{} })
	Register("place", func() Component { return &PlaceC{} })
	Register("settlement", func() Component { return &SettlementC{Store: map[string]float64{}} })
	Register("region", func() Component { return &RegionC{} })
}

// IdentC — кто это: имя и родовое, пол, ремесло, черта, происхождение. Фамилия согласуется
// по полу на уровне генерации (в прототипе был баг «Всеволод Тихонова» — больше не повторяем).
type IdentC struct {
	Name    string `json:"name"`
	Surname string `json:"surname"`
	Nick    string `json:"nick"`
	Sex     string `json:"sex"` // "м" | "ж"
	Role    string `json:"role"`
	Trait   string `json:"trait"`
	Origin  string `json:"origin"`
}

func (c *IdentC) Kind() string { return "ident" }

// VitalsC — тело и нужды (0..100, кроме возраста в годах). Возраст реально растёт, здоровье
// реально кончается — в мёртвом прототипе этого не было (у всех вечные 100), оттого и застой.
type VitalsC struct {
	Age    float64 `json:"age"`
	Health float64 `json:"health"`
	Hunger float64 `json:"hunger"`
	Energy float64 `json:"energy"`
	Morale float64 `json:"morale"`
}

func (c *VitalsC) Kind() string { return "vitals" }

// DrivesC — тяги A-life, которые гонят поведение (а не скрипт): тяга к людям, жажда роста,
// обустроенность. Разум читает их и ставит цель. Высокая амбиция без роста → человек уходит.
type DrivesC struct {
	Ambition float64 `json:"ambition"`
	Social   float64 `json:"social"`
	Dwelling float64 `json:"dwelling"`
}

func (c *DrivesC) Kind() string { return "drives" }

// MindC — «мысли» (блок 1.4): текущая цель, до какого часа она держится, и короткая память
// о последнем действии. Разум (пакет mind) выбирает цель по нуждам и тягам; система лишь
// исполняет. Так НПС живёт, а не ждёт команды.
type MindC struct {
	Goal      string `json:"goal"`       // eat | rest | work | socialize | seek_home | wander | leave — НАМЕРЕНИЕ
	GoalUntil int64  `json:"goal_until"` // час мира, до которого цель в силе
	LastAct   string `json:"last_act"`   // человекочитаемый ИСХОД последнего действия (для показа)
	ActCode   string `json:"act_code"`   // СТАБИЛЬНЫЙ машинный код подтверждённого занятия (для анимаций клиента)
	// Internal A-Life routine state; persisted with the world, not exposed as a new wire field.
	RoutineGoal     string `json:"routine_goal,omitempty"`
	RoutinePhase    string `json:"routine_phase,omitempty"`
	RoutineSince    int64  `json:"routine_since,omitempty"`
	RoutineProgress int64  `json:"routine_progress,omitempty"`
	// DesperateDays — подряд дней честного труда без своего дома, которые не вытащили из долга
	// (см. sim.LifeSystem/becomeBandit): счётчик пути в разбой. Внутреннее состояние A-Life, как и
	// Routine*-поля выше — тоже не новое проводное поле, лишь персистентность мира.
	DesperateDays int64 `json:"desperate_days,omitempty"`
}

func (c *MindC) Kind() string { return "mind" }

// SkillsC — умения по имени. Растут от дела, с замедлением (нельзя раздуть до бесконечности,
// как вышло в прототипе с cooking 433).
type SkillsC struct {
	Skills map[string]float64 `json:"skills"`
}

func (c *SkillsC) Kind() string { return "skills" }

// Train повышает умение с насыщением: чем выше навык, тем медленнее прирост.
func (c *SkillsC) Train(name string, amount float64) {
	if c.Skills == nil {
		c.Skills = map[string]float64{}
	}
	cur := c.Skills[name]
	c.Skills[name] = cur + amount/(1.0+cur/50.0)
}

func (c *SkillsC) Level(name string) float64 { return c.Skills[name] }

// PurseC — деньги и долги. Долги — по id того, кому должен (не по имени: имена не уникальны).
type PurseC struct {
	Coins float64            `json:"coins"`
	Debts map[string]float64 `json:"debts"`
}

func (c *PurseC) Kind() string { return "purse" }

// HomeC — жильё: нет/снимает/владеет, уют и к какому поселению привязан дом.
type HomeC struct {
	Status     string  `json:"status"` // none | rented | owned
	Comfort    float64 `json:"comfort"`
	Settlement ID      `json:"settlement"`
}

func (c *HomeC) Kind() string { return "home" }

// PlaceC — где сущность сейчас (id поселения). В пути Settlement==0, а Transit хранит маршрут
// (добавим компонентом позже — пример расширяемости).
type PlaceC struct {
	Settlement ID `json:"settlement"`
}

func (c *PlaceC) Kind() string { return "place" }

// SettlementC — поселение как сущность: имя, когда основано, общий склад, ступень роста,
// есть ли корчма и кто корчмарь. Город вырастает из деревни сменой Tier — civilization растёт.
type SettlementC struct {
	Name    string             `json:"name"`
	Founded int64              `json:"founded"` // день основания
	Tier    string             `json:"tier"`    // hamlet | village | town | city
	Store   map[string]float64 `json:"store"`
	Tavern  bool               `json:"tavern"`
	Keeper  ID                 `json:"keeper"`
	X       float64            `json:"x"`      // положение на карте мира (версты): нужно для путей,
	Y       float64            `json:"y"`      // торговли и досягаемости сборщиков налога
	Region  ID                 `json:"region"` // регион-контейнер (большой мир); 0 — вне региона
	Plague  float64            `json:"plague"` // интенсивность хвори (0 — здорово); от скученности+нехватки медицины
	Coin    float64            `json:"coin"`   // монеты в обороте рынка (общий котёл торга: покупатель платит сюда, работнику платят отсюда)
	Prices  map[string]float64 `json:"prices"` // текущие рыночные цены товаров (спрос/предложение)
	Mana    float64            `json:"mana"`   // плотность маны в этих местах (канон): >1 — место силы, <1 — мёртвая зона.
	//                                              Задаёт магию (мана мага пополняется из неё), где гуще водится опасное зверьё,
	//                                              и скорость магического лечения. Меняется мутацией мира (тяжёлое колдовство истощает).
	Ward   float64 `json:"ward"`   // оберег мага (0..1): отваживает хищников от села/стада; держится, пока маг его подновляет
	Strain float64 `json:"strain"` // внешнее давление хвори от РЕАЛЬНЫХ бед — войны и нетипичного климата (не сезон!); спадает само
}

func (c *SettlementC) Kind() string { return "settlement" }

// RegionC — регион-контейнер большого мира (провинция/край): группирует поселения по местности.
// Биом задаёт характер земли (где рыбное место, где руда). Из регионов складывается материк.
type RegionC struct {
	Name  string  `json:"name"`
	Biome string  `json:"biome"` // plain|forest|hills|coast|steppe
	X     float64 `json:"x"`     // центр края на карте мира
	Y     float64 `json:"y"`
	Mana  float64 `json:"mana"`  // базовая плотность маны края (места силы/мёртвые зоны); поселения наследуют её с разбросом
	Outer bool    `json:"outer"` // край внешнего мира (за горами стартовой демо-зоны) — не home region
}

func (c *RegionC) Kind() string { return "region" }

func Region(e *Entity) *RegionC { c, _ := e.Get("region").(*RegionC); return c }

// --- типизированные помощники: короче и безопаснее, чем Get+приведение ---

func Ident(e *Entity) *IdentC       { c, _ := e.Get("ident").(*IdentC); return c }
func Vitals(e *Entity) *VitalsC     { c, _ := e.Get("vitals").(*VitalsC); return c }
func Drives(e *Entity) *DrivesC     { c, _ := e.Get("drives").(*DrivesC); return c }
func Mind(e *Entity) *MindC         { c, _ := e.Get("mind").(*MindC); return c }
func Skills(e *Entity) *SkillsC     { c, _ := e.Get("skills").(*SkillsC); return c }
func Purse(e *Entity) *PurseC       { c, _ := e.Get("purse").(*PurseC); return c }
func Home(e *Entity) *HomeC         { c, _ := e.Get("home").(*HomeC); return c }
func Place(e *Entity) *PlaceC       { c, _ := e.Get("place").(*PlaceC); return c }
func Settle(e *Entity) *SettlementC { c, _ := e.Get("settlement").(*SettlementC); return c }
