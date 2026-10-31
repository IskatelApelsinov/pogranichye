package core

// HoursPerDay — сколько «часов» мирового времени в сутках. Тик мира = один час.
const HoursPerDay = 24

// Clock — мировое время в часах от начала. День/час считаются из него.
type Clock struct {
	TotalHours int64 `json:"total_hours"`
}

func (c Clock) Day() int64     { return c.TotalHours/HoursPerDay + 1 }
func (c Clock) Hour() int      { return int(c.TotalHours % HoursPerDay) }
func (c *Clock) Advance()      { c.TotalHours++ }
func (c Clock) IsNewDay() bool { return c.TotalHours%HoursPerDay == 0 }
