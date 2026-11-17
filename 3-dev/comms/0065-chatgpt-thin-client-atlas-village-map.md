# 0065 — ChatGPT: atlas-backed live village map for the thin client

Дата: 2026-11-10. От: ChatGPT. Кому: владелец проекта и Claude Fable 5.2.
Основание: main после #228, открытые #200/#229/#230 и мастер-контекст 0063. Implementation PR; server/wire changes отсутствуют.

## Что проверено перед работой

- Main: `3ff4eba3b49820c780c27cc38c9d6779c0ed6e7c` (#228).
- Открытые PR на старте: #200 (design-only ADR 0004), #229 (thin-client craft stock/ACK UX), #230 (Unity movement queue/input diagnostics). Текущая правка не пересекает их файлы/назначение.
- #39: Layout v2 остаётся 300×300 м; `/api/layout` отдаёт здания, реку, мост и POI, но не navgraph. Геометрию и навигацию не менял.
- #40: targeted talk/ACK остаётся на текущем v1.11; эта работа не меняет диалоги.
- #80: серверные состояния, задания и мини-игры не менялись; новые intents и локальные игровые результаты не добавлены.
- Проверены `thin-client/web/index.html`, `core.js`, `village-atlas.svg`, `village-scene.svg`, `ART-ASSETS.md`, `/api/layout` в `1-server/sim/world_geometry.go` и handler статических файлов.
- Атлас уже существовал, но live canvas рисовал геометрию прямоугольниками и показывал жителей/зверей простыми точками.

## Реализация

- Подключил существующий `village-atlas.svg` к live map 300×300 м.
- Добавил `village-map.js`: north-up projection; атласные тайлы земли/воды; спрайты домов, кузницы, рынка, колодца, деревьев, поля, бочек и дров; простые 2D-фигуры жителей, игрока и зверей.
- Позиции игрока/NPC/зверей берутся только из `me.pos`, `place.neighbors[].pos`, `place.foes[]`. Неизвестные имена NPC в renderer не копируются.
- Фоновые props детерминированы и отфильтрованы по текущим препятствиям/реке. Тропы и props — только presentation; тропы включаются только для Layout v2. Они не объявляются navgraph, collision или авторитетным маршрутом.
- Сохранены прежние клики: выбор зверя и переход к ближайшему POI. Обратная проекция клика использует ту же north-up трансформацию, что и отрисовка.
- Добавлены Node regression tests и CI workflow; расширены asset docs.

## Файлы PR #231

- `.github/workflows/thin-client-web-checks.yml`
- `thin-client/web/ART-ASSETS.md`
- `thin-client/web/core.js`
- `thin-client/web/index.html`
- `thin-client/web/village-map.js`
- `thin-client/web/tests/village-map.test.cjs`

## Ветка, коммиты, PR и проверки

- Ветка: `chatgpt/thin-client-atlas-village-map-20261110`.
- Основной implementation commit: `c0363ebf4b4fd6870c627bc60ce1a3380277c62e`.
- Последующие hardening/CI commits: `453fc1561e40830aa2626d10884c011f7f6c2419`, `dbe7789efeb103320644c4e43aece7a1bd0744dd`, `94a011f93cfb55483589126072b3fd1925d2eb87`, `67b7b0e6316525e467bcf91738170aca95a28368`.
- PR #231: https://github.com/IskatelApelsinov/pogranichye/pull/231 — открыт, draft, не слит.
- GitHub Actions run #42884918378: SUCCESS (final head); prior run #42884477403 also SUCCESS.
- `node --check thin-client/web/core.js`: passed.
- `node --check thin-client/web/village-map.js`: passed.
- `node --test thin-client/web/tests/*.test.cjs`: 16/16 passed, 0 failed. Покрыты RequestGate, craft feedback, map projection, deterministic props, server-only entity positions/privacy, optional state и renderer.
- Ручной браузерный/телефонный запуск не выполнялся. Unity Editor/PlayMode не запускались и для этой thin-client правки не требовались.

## Риски и следующая конкретная задача

- Нужна ручная проверка на телефоне и узком viewport: читаемость мелких спрайтов, точность tap по зверям/POI после CSS scaling, визуальный баланс слоёв.
- Тропы остаются иллюстрацией, пока сервер не начнёт выдавать навграф; не использовать их как обещание реальной проходимости.
- Следующий полезный implementation step: превратить экраны POI в более предметные мини-сцены/спрайтовые рабочие места с server-backed действиями, начиная с одной локации и не дублируя P3 craft PR #229.

Merge не выполнялся.
