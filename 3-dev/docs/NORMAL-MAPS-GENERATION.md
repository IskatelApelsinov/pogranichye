# Генерация normal maps для Пограничья

Дата: 2026-11-14  
Автор: Grok (консультант)  
Связь: `ГРАФИЧЕСКИЙ-КОНВЕЙЕР-UNITY6-URP.md` § G2 (единый стандарт материалов), `textures/grok/`

## 1. Зачем

URP/Lit ожидает PBR-набор: **albedo + normal (+ mask/metallic-smoothness)**.  
Albedo даёт цвет; normal даёт микрорельеф без дополнительной геометрии. Для демо (P0.4 / P1.4) нормали убирают «пластиковый» вид procedural-текстур.

## 2. Конвенции Unity URP

| Параметр | Значение |
|----------|----------|
| Пространство | Tangent space |
| Ось Y | **OpenGL** (Y+) — дефолт URP; галочка «Flip Green Channel» только если карта из DirectX-пака |
| Кодирование | RGB: `n * 0.5 + 0.5` → [0..1], синий ≈ 1 = поверхность к камере |
| Импорт | Texture Type: **Normal map**; sRGB **Off**; Generate Mip Maps On; Wrap Repeat |

Неправильно пометить albedo как Normal map → фиолетовые артефакты и сломанный свет.

## 3. Методы генерации (от простого к лучшему)

### A. Height-from-luminance + Sobel (то, что сделано для `textures/grok`)

1. Взять albedo → grayscale (Rec.709 luminance) как heightfield.
2. Посчитать ∂h/∂x, ∂h/∂y фильтром Sobel (3×3).
3. Нормаль: `normalize((-s·∂x, -s·∂y, 1))`, где `s` — strength.
4. Закодировать в RGB.

**Плюсы:** быстро, детерминированно, без внешних тулов, достаточно для грунта/камня/коры.  
**Минусы:** путает цвет с высотой (тёмная грязь ≠ впадина); нет отдельного контроля частоты; на гладком albedo нормаль почти плоская.

Рекомендуемые strength (стартовые):
- plaster / water: 0.8–1.2
- dirt / grass / cloth: 1.2–1.8
- wood / thatch: 2.0–2.5
- bark / cobble / stone: 2.8–3.5

### B. Отдельная height/displacement карта

Рисовать или генерировать grayscale height отдельно от albedo, затем тот же Sobel/Scharr.  
Лучше для досок (щели), кладки (швы), соломы (направления).

### C. Специализированные тулы

- **Materialize**, **AwesomeBump**, **Krita** (normal map filter), **Substance Designer** — для финального арта.
- **NVIDIA Texture Tools / CrazyBump** — классика, но старые.
- В Blender: bake normal from high-poly или from bump.

Для проекта на этапе G2 достаточно A/B; C — когда появится эталонный kit зданий.

### D. Runtime / shader bump

URP может брать height в material и считать bump в шейдере (parallax/POM — дорого). Для массовой земли/стен лучше offline normal map + plain Lit.

## 4. Что уже сгенерировано

В artifacts владельца (`textures/grok/`):

```
*_n.png  — OpenGL normal maps, 512², пара к каждому albedo из пакета Grok
```

Имена: `dirt_soil_n.png`, `grass_meadow_n.png`, `cobble_path_n.png`, …

Алгоритм: luminance → Sobel → encode, strength per-material (см. §3A).

## 5. Импорт в Unity (чеклист)

1. Albedo: sRGB On, Default.
2. Normal (`*_n`): Texture Type = **Normal map**, sRGB Off.
3. Материал URP/Lit: Base Map = albedo, Normal Map = `*_n`, Normal Scale 0.7–1.2 (подкрутить в редакторе).
4. Не смешивать DirectX-нормали без Flip Green.
5. После импорта — сравнить эталонную улицу day/dusk (G1).

## 6. Ограничения и следующий шаг

- Procedural normals **не заменяют** hand-painted / baked с high-poly.
- Для воды лучше отдельный animated normal (flowmap) — текущий `water_shallow_n` только статичный намёк.
- Следующий пакет по запросу ChatGPT: height-карты вручную для wood/stone + roughness/metallic mask (R=metallic, A=smoothness или pack по стандарту проекта).

## 7. Минимальный код (Python / PIL+numpy)

См. скрипт генерации в истории задачи `task_ai/008-grok-normal-maps.md`. Суть:

```text
height = luminance(albedo)
Gx, Gy = sobel(height)
N = normalize((-strength*Gx, -strength*Gy, 1))
RGB = N * 0.5 + 0.5
```

Детерминированно, без GPU, воспроизводимо в CI при необходимости.
