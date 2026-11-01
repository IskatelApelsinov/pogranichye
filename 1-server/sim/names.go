package sim

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Славянские имена/прозвища. Пулы небольшие, но фамилия СОГЛАСУЕТСЯ по полу (в прототипе
// был «Всеволод Тихонова» — мужчина с женской формой; здесь мужская/женская формы родовые).
var (
	maleNames = []string{"Бажен", "Боримир", "Боян", "Велислав", "Всеволод", "Горазд",
		"Добрыня", "Драгомир", "Ждан", "Миролюб", "Путята", "Радим", "Светозар", "Твердята",
		"Ярополк", "Любим", "Мстислав", "Остромир", "Станимир", "Берислав"}
	femaleNames = []string{"Берегиня", "Велимира", "Горислава", "Голуба", "Добронега",
		"Забава", "Лада", "Милана", "Отрада", "Румяна", "Светлана", "Ярослава", "Млада",
		"Синеока", "Благана", "Весняна", "Дарёна", "Златоцвета", "Любава", "Снежана"}
	// родовое прозвание: [мужская форма, женская форма]
	surnames = [][2]string{
		{"Кривонос", "Кривонос"}, {"Хомяков", "Хомякова"}, {"Болотов", "Болотова"},
		{"Тихонов", "Тихонова"}, {"Зайцев", "Зайцева"}, {"Смолов", "Смолова"},
		{"Седов", "Седова"}, {"Мельник", "Мельник"}, {"Стрижак", "Стрижак"},
		{"Волков", "Волкова"}, {"Градов", "Градова"}, {"Нежданов", "Неждановна"},
	}
	traits = []string{"Крепкий", "Ловкий", "Трудолюбивый", "Выносливый", "Бережливый",
		"Хозяйственный", "Общительный", "Удачливый", "Дотошный", "обычный"}
	// ремёсла. farmer и miner добавлены: без пахарей не сеют хлеб, без рудокопов нет руды.
	// веса подобраны так, чтобы деревня кормила себя (много пахарей, поменьше прочих).
	// «mage» здесь НЕТ: магом не рождаются, роль даётся при ПРОБУЖДЕНИИ дара (см. magic.go).
	roles       = []string{"farmer", "fisher", "cook", "smith", "herbalist", "hunter", "woodcutter", "weaver", "miner", "herder"}
	roleWeights = []float64{30, 9, 11, 6, 5, 9, 9, 5, 7, 12} // пахарей больше всего
)

// pickRole — ремесло по весам (пахари чаще прочих). Отдельно от roles, чтобы менять баланс.
func pickRole(s *dice.Stream) string {
	i := s.Weighted(roleWeights)
	if i < 0 {
		i = 0
	}
	return roles[i]
}

// surnameFor выдаёт родовое прозвание в нужной форме по полу.
func surnameFor(s *dice.Stream, sex string) string {
	pair := surnames[s.Intn(len(surnames))]
	if sex == "ж" {
		return pair[1]
	}
	return pair[0]
}

// makeIdentity лепит согласованную личность (пол задаётся заранее, т.к. от него зависят
// имя и фамилия). surname можно передать (наследование рода ребёнком) — иначе берётся свой.
func makeIdentity(s *dice.Stream, sex, surname, role string) *core.IdentC {
	var name string
	if sex == "ж" {
		name = femaleNames[s.Intn(len(femaleNames))]
	} else {
		name = maleNames[s.Intn(len(maleNames))]
	}
	if surname == "" {
		surname = surnameFor(s, sex)
	}
	if role == "" {
		role = roles[s.Intn(len(roles))]
	}
	return &core.IdentC{
		Name:    name,
		Surname: surname,
		Sex:     sex,
		Role:    role,
		Trait:   traits[s.Intn(len(traits))],
	}
}

// inheritedSurname возвращает родовое прозвание в форме пола ребёнка, взяв корень родителя.
// Простое правило: если родительская мужская форма оканчивается на распространённый суффикс,
// женская строится заменой; иначе ищем пару в таблице. Для v0 ищем по таблице, падая на свою.
func inheritedSurname(s *dice.Stream, parentSurname, childSex string) string {
	for _, p := range surnames {
		if parentSurname == p[0] || parentSurname == p[1] {
			if childSex == "ж" {
				return p[1]
			}
			return p[0]
		}
	}
	return surnameFor(s, childSex)
}

func randomSex(s *dice.Stream) string {
	if s.Chance(0.5) {
		return "ж"
	}
	return "м"
}
