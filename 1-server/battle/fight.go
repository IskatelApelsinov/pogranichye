package battle

import "pogranichye/dice"

// Зверьё и лихой люд — как в прототипе: мощь (сопротивление), добыча, опасность (ответный урон).
type Beast struct {
	Name   string
	Power  float64
	Meat   float64 // мяса с туши
	Hide   float64 // шкур
	Danger float64 // ответный урон при промахе/слабости
}

var Beasts = map[string]Beast{
	"hare":   {"заяц", 2, 3, 0, 1},
	"fox":    {"лиса", 4, 4, 1, 2},
	"boar":   {"кабан", 7, 12, 2, 5},
	"wolf":   {"волк", 8, 7, 2, 7},
	"bear":   {"медведь", 12, 18, 4, 10},
	"bandit": {"разбойник", 9, 0, 0, 8}, // добычи нет — отнимает; опасен
}

// Outcome — исход схватки для сводки/хроники.
type Outcome struct {
	Beast string
	Win   bool
	Meat  float64
	Hide  float64
	Hurt  float64 // сколько урона получил боец
	Part  string  // куда ранило
}

// Resolve — разрешить схватку на костях и навыке. skill — охота/бой (0..~100), quality — как
// хорошо провёл бой (у NPC ~ умение; у игрока позже — мини-игра). Рана ложится на тело body.
func Resolve(skill, quality float64, beastID string, body *BodyC, s *dice.Stream) Outcome {
	b, ok := Beasts[beastID]
	if !ok {
		return Outcome{}
	}
	if quality < 0 {
		quality = 0
	}
	if quality > 1 {
		quality = 1
	}
	atk := (1.0 + skill/20.0) * (0.4 + 0.6*quality) * s.Uniform(0.8, 1.2)
	foe := b.Power * s.Uniform(0.8, 1.2)
	out := Outcome{Beast: b.Name, Win: atk >= foe}

	// ответный урон тем сильнее, чем хуже бой; ранит в часть, выбранную костями
	hurt := b.Danger * (1.3 - quality)
	if out.Win {
		// чем увереннее победа (atk сильно больше foe), тем меньше задело —
		// мастер уходит с чистыми руками, новичок и в победе набьёт синяков
		margin := atk / foe
		hurt *= 0.4 / margin
	}
	if hurt > 0.5 && body != nil {
		part := partList[s.Intn(len(partList))]
		crit := s.Chance(0.12)
		if crit {
			hurt *= 1.8
		}
		body.hurt(part, hurt)
		out.Hurt = hurt
		out.Part = part
	}
	if out.Win {
		out.Meat = b.Meat * (0.5 + 0.5*quality)
		out.Hide = b.Hide
	}
	return out
}

// Tier подбирает зверя по «уровню опасности» выхода (охота/дорога). rng — наш поток.
func Pick(s *dice.Stream, tier string) string {
	var pool []string
	switch tier {
	case "small":
		pool = []string{"hare", "fox"}
	case "big":
		pool = []string{"boar", "wolf", "bear"}
	case "road":
		pool = []string{"wolf", "bandit", "boar"}
	default:
		pool = []string{"hare", "fox", "boar"}
	}
	return pool[s.Intn(len(pool))]
}
