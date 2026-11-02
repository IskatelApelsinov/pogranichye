package sim

import (
	"pogranichye/core"
	"pogranichye/dice"
)

// Имена без потолка: есть курированные пулы (для колорита), но и ПРОЦЕДУРНАЯ сборка — так имён
// людей, держав и мест хватит на сколь угодно большой мир. Фамилия строится из КОРНЯ + родового
// суффикса, согласованного по полу; наследование ребёнком работает через восстановление корня.

var (
	maleNames = []string{"Бажен", "Боримир", "Боян", "Велислав", "Всеволод", "Горазд",
		"Добрыня", "Драгомир", "Ждан", "Миролюб", "Путята", "Радим", "Светозар", "Твердята",
		"Ярополк", "Любим", "Мстислав", "Остромир", "Станимир", "Берислав"}
	femaleNames = []string{"Берегиня", "Велимира", "Горислава", "Голуба", "Добронега",
		"Забава", "Лада", "Милана", "Отрада", "Румяна", "Светлана", "Ярослава", "Млада",
		"Синеока", "Благана", "Весняна", "Дарёна", "Златоцвета", "Любава", "Снежана"}

	// части для ПРОЦЕДУРНЫХ двусоставных славянских имён (приставка + окончание по полу)
	nameHead  = []string{"Свято", "Яро", "Влади", "Добро", "Миро", "Все", "Радо", "Люб", "Благо", "Стани", "Боре", "Твердо", "Вели", "Пере", "Чести", "Драго", "Моги", "Рати", "Любо", "Злато"}
	nameMaleT = []string{"слав", "мир", "полк", "гнев", "волод", "бор", "дан", "жир", "рад", "свет"}
	nameFemT  = []string{"слава", "мира", "нега", "гнева", "дана", "цвета", "рада", "света", "яна", "вера"}

	// КОРНИ родовых прозваний (фамилий). Пул большой; плюс изредка корень синтезируется (ниже).
	surnameRoots = []string{"Крив", "Хомяк", "Болот", "Тихон", "Зайц", "Смол", "Сед", "Мельник",
		"Стриж", "Волк", "Град", "Неждан", "Мороз", "Сокол", "Соболь", "Бел", "Черн", "Злат",
		"Дуб", "Камен", "Реч", "Медвеж", "Лис", "Воронц", "Жар", "Снег", "Горяч", "Бобр", "Журавл",
		"Голуб", "Перепёлк", "Коршун", "Рыб", "Бортник", "Гончар", "Кузнец", "Пекар", "Ткач", "Пастух"}
	// родовые суффиксы [мужская, женская]; семья суффикса у корня постоянна → наследование стабильно
	surSuffix = [][2]string{{"ов", "ова"}, {"ев", "ева"}, {"ин", "ина"}, {"ский", "ская"}, {"ной", "ная"}}

	// державы: прилагательное края + «тип» державы — процедурно, без потолка
	realmAdj  = []string{"Залесск", "Понизовск", "Горск", "Светл", "Дик", "Вольн", "Срединн", "Заречн", "Полуночн", "Полуденн", "Студён", "Краснолесск", "Белозёрск", "Черноборск", "Привольн"}
	realmKind = []string{"ая земля", "ая держава", "ая волость", "ая украйна", "ая сторона", "ое княжество", "ий удел", "ая отчина"}

	traits = []string{"Крепкий", "Ловкий", "Трудолюбивый", "Выносливый", "Бережливый",
		"Хозяйственный", "Общительный", "Удачливый", "Дотошный", "обычный"}
	roles       = []string{"farmer", "fisher", "cook", "smith", "herbalist", "hunter", "woodcutter", "weaver", "miner", "herder", "mage"}
	roleWeights = []float64{30, 9, 11, 6, 5, 9, 9, 5, 7, 12, 2} // пахарей больше всего; магов мало
)

// pickRole — ремесло по весам (пахари чаще прочих).
func pickRole(s *dice.Stream) string {
	i := s.Weighted(roleWeights)
	if i < 0 {
		i = 0
	}
	return roles[i]
}

// famOf — семья суффикса, постоянная для корня (чтобы род всегда звучал одинаково и наследовался).
func famOf(root string) int {
	sum := 0
	for _, r := range root {
		sum += int(r)
	}
	return sum % len(surSuffix)
}

// surnameFromRoot — фамилия из корня в форме пола.
func surnameFromRoot(root, sex string) string {
	f := surSuffix[famOf(root)]
	if sex == "ж" {
		return root + f[1]
	}
	return root + f[0]
}

// pickRoot — корень фамилии: из пула, а изредка синтезированный (чтобы родов было без потолка).
func pickRoot(s *dice.Stream) string {
	if s.Chance(0.2) {
		return synthRoot(s)
	}
	return surnameRoots[s.Intn(len(surnameRoots))]
}

var rootSyll = []string{"бел", "чер", "свет", "мор", "зор", "вет", "лад", "гор", "дол", "яр", "рад", "буй", "тих", "крас", "сереб", "золот", "камен", "брус", "вербо", "ольх"}

func synthRoot(s *dice.Stream) string {
	r := []rune(rootSyll[s.Intn(len(rootSyll))] + rootSyll[s.Intn(len(rootSyll))])
	r[0] = upperRune(r[0]) // с заглавной
	return string(r)
}

func upperRune(r rune) rune {
	if r >= 'а' && r <= 'я' {
		return r - ('а' - 'А')
	}
	return r
}

// recoverRoot — восстановить корень из готовой фамилии (срезав известный суффикс, длинные раньше),
// чтобы ребёнок унаследовал род в своей форме. Не нашли суффикс — берём как есть.
func recoverRoot(surname string) string {
	var all []string
	for _, p := range surSuffix {
		all = append(all, p[1], p[0]) // женские (длиннее) первыми
	}
	best := ""
	for _, suf := range all {
		if len(suf) > len(best) && hasSuffix(surname, suf) && len(surname) > len(suf) {
			best = suf
		}
	}
	if best != "" {
		return surname[:len(surname)-len(best)]
	}
	return surname
}

func hasSuffix(s, suf string) bool {
	return len(s) >= len(suf) && s[len(s)-len(suf):] == suf
}

// surnameFor — свежее родовое прозвание в форме пола.
func surnameFor(s *dice.Stream, sex string) string { return surnameFromRoot(pickRoot(s), sex) }

// givenName — личное имя: курированный пул или процедурная двусоставная сборка (без потолка).
func givenName(s *dice.Stream, sex string) string {
	if s.Chance(0.5) {
		head := nameHead[s.Intn(len(nameHead))]
		if sex == "ж" {
			return head + nameFemT[s.Intn(len(nameFemT))]
		}
		return head + nameMaleT[s.Intn(len(nameMaleT))]
	}
	if sex == "ж" {
		return femaleNames[s.Intn(len(femaleNames))]
	}
	return maleNames[s.Intn(len(maleNames))]
}

// makeIdentity лепит согласованную личность. surname можно передать (наследование) — иначе свой.
func makeIdentity(s *dice.Stream, sex, surname, role string) *core.IdentC {
	if surname == "" {
		surname = surnameFor(s, sex)
	}
	if role == "" {
		role = roles[s.Intn(len(roles))]
	}
	return &core.IdentC{
		Name:    givenName(s, sex),
		Surname: surname,
		Sex:     sex,
		Role:    role,
		Trait:   traits[s.Intn(len(traits))],
	}
}

// inheritedSurname — род ребёнка в его форме: восстанавливаем корень родителя и склоняем по полу.
func inheritedSurname(s *dice.Stream, parentSurname, childSex string) string {
	if parentSurname == "" {
		return surnameFor(s, childSex)
	}
	return surnameFromRoot(recoverRoot(parentSurname), childSex)
}

// procRealmName — процедурное имя державы (без потолка списка).
func procRealmName(g *dice.Stream) string {
	return realmAdj[g.Intn(len(realmAdj))] + realmKind[g.Intn(len(realmKind))]
}

func randomSex(s *dice.Stream) string {
	if s.Chance(0.5) {
		return "ж"
	}
	return "м"
}
