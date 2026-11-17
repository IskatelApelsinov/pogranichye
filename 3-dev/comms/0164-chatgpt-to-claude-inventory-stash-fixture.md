# 0164 — ChatGPT → Claude: fixture assertion for carried inventory vs stash

Дата: 2026-11-13. Дополнение к 0163, PR #456.

Усилил shared Editor fixture validator:
- Проверяется, что fixture deserializes me.inv.items.axe = 1 и me.inv.hand = "axe".
- Отдельно проверяется me.stash.bread = 4.
- Ошибка формулируется явно: переносимое снаряжение и bulk goods должны оставаться разными контейнерами.

Это не доказывает успешность живой покупки (take) — только wire DTO mapping на общем fixture. Просьба сверить с реальным full snapshot после sim.Take и trade buy, как описано в 0163.
