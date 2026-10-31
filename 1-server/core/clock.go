package core

// HoursPerDay — сколько «часов» мирового времени в сутках. Тик мира = один час.
const HoursPerDay = 24

// YearDays — длина «года» мира в днях (быстрее реального, чтобы поколения и урожаи
// сменялись на игровых сроках). Отсюда считаются возраст и сезоны — один календарь на всё.
const YearDays = 90

// Season — время года. Делит год на четыре равные части: от этого зависят сев, жатва,
// нерест, холода. Экономика средневековья насквозь сезонна, поэтому сезон — свойство времени.
type Season int

const (
	Spring Season = iota // сев яровых, нерест рыбы
	Summer               // рост
	Autumn               // жатва
	Winter               // голодная пора, еда с запасов
)

var seasonNames = [4]string{"весна", "лето", "осень", "зима"}

func (s Season) String() string {
	if s < 0 || int(s) >= len(seasonNames) {
		return "?"
	}
	return seasonNames[s]
}

// Clock — мировое время в часах от начала. День/час/сезон считаются из него.
type Clock struct {
	TotalHours int64 `json:"total_hours"`
}

func (c Clock) Day() int64     { return c.TotalHours/HoursPerDay + 1 }
func (c Clock) Hour() int      { return int(c.TotalHours % HoursPerDay) }
func (c *Clock) Advance()      { c.TotalHours++ }
func (c Clock) IsNewDay() bool { return c.TotalHours%HoursPerDay == 0 }

// DayOfYear — день внутри года (0..YearDays-1).
func (c Clock) DayOfYear() int { return int((c.Day() - 1) % YearDays) }

// Year — порядковый год мира (с 1).
func (c Clock) Year() int64 { return (c.Day()-1)/YearDays + 1 }

// Season — текущее время года из дня года.
func (c Clock) Season() Season {
	return Season(c.DayOfYear() / (YearDays / 4))
}
