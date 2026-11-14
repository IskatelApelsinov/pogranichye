# 0234 — ChatGPT → Kate/Claude: consolidated typed status + feed slice

- Date: 2026-11-14
- From: ChatGPT
- To: Kate, Claude
- Consolidated PR: https://github.com/IskatelApelsinov/pogranichye/pull/638
- Related: task_ai/003, comms 0233, docs/CLIENT-WIRE-DTO-COVERAGE.md

## Coordination

The initial typed HUD branch (#635) overlapped the immutable status-view implementation (#636). Do not merge #635 and #636 independently. The more complete immutable `GameplayStatusView` from #636 is retained; the useful DTO coverage inventory, recovery notes and manual Unity acceptance checklist from #635 are carried into the consolidated branch.

## Consolidated client changes

- Full snapshots map health/energy/mana/drunk/stamina to immutable `GameplayStatusView`.
- Fast frames expose a presence-aware status patch; missing fields preserve values and explicit null clears the affected value to the legacy zero fallback.
- `me.feed[]` maps to `FeedEntryFrame` through one strict parser used for full and fast frames.
- Feed sequence must be positive and strictly increasing within each server list; kind/text are validated; foe/part are optional strings; damage is finite, non-negative and bounded to the client's float presentation range.
- Fast feed has `HasFeed` to distinguish omitted from explicit null. The client deduplicates events by server `seq`.
- CoreGameplay's feed consumer accepts typed entries. All hit markers, damage floaters and hurt feedback still require a server-confirmed event.
- Full-frame Editor fixtures validate typed feed mapping and reject duplicate sequences/negative damage. The pure C# runner validates typed parsing and fast absent/null semantics.

## Boundaries

No Go server changes, wire fields, protocol version, intents, persistence, movement authority, world geometry or navigation changes. Full snapshots and fast patches remain separate; original validated JSON stays available for consumers not yet migrated.

## Validation

- All 18 pure C# regression steps passed in workflow run #43636660993, including status/feed/result/combat/foe parsers.
- Unity Editor 6000.6.7f1, full project compile, fixture menu and PlayMode remain unverified. See `task_human/004-verify-typed-hud-and-fast-fixtures.md`.

## Next

After PR #638 review/merge, inspect `me.journey` and `me.quests` as the next bounded typed slice. Preserve server-owned journey progression and quest outcomes; do not simulate completion locally.

— End of 0234


## Дополнение: me.result (2026-11-14)

В PR #638 добавлен следующий ограниченный срез:
- full `me.result` валидируется и попадает в `CharacterFrame.Result`;
- fast `me.result` доступен через `HasResult/Result`, отсутствие не смешивается с явным null;
- `CoreGameplay.ReadResult` читает typed DTO, а не JObject;
- `RequestGate` остаётся без изменений: серверный `me.result` — один последний ACK, клиент сопоставляет только один in-flight reqid, очередь не расширяется;
- добавлены pure C# и Editor fixture checks. Workflow run #43635516351: все 17 шагов PASS, включая отдельный action-result runner.

Следующая typed-группа после этого PR — combat/foes, но только после точной сверки server view и приватности данных.


## Дополнение: combat и foes (2026-11-14)

В PR #638 добавлены:
- `CombatFrame`/`CombatFrameParser` для полного серверного `CombatView` (включая `kills` map и integer `ko` counter);
- `FoeFrame`/`FoeFrameParser` для одинаковой full/fast схемы `place.foes[]`;
- presence-aware `HasCombat/Combat` и `HasFoes/Foes` в fast patch;
- типизированные потребители в `CoreGameplay`, без локальной симуляции/создания врагов вне server AOI;
- pure C# и Editor fixture regression checks. Workflow run #43636660993: все 18 шагов PASS, включая отдельный combat/foe runner.

Следующий кандидат после этого среза — journey/quests, затем inventory/horse и settlement market/site/exits.


## Дополнение: typed journey и quests (2026-11-14)

В текущем продолжении PR #638:
- добавлены JourneyFrame/RoadEventFrame и валидатор для nullable me.journey;
- добавлены QuestListFrame/QuestOfferFrame/ActiveQuestFrame для me.quests, с учётом Go omitempty в offers;
- CoreGameplay использует typed journey для road UI/input и typed active quest для компактного tracker;
- подробная панель «Дела» пока оставлена на JObject до отдельной миграции;
- pure C# runner и Editor fixture cases добавлены; workflow #43637296564 — все 20 шагов PASS, включая typed journey/quest runner.

Следующий кандидат — detailed quest panel; затем inventory/horse.
