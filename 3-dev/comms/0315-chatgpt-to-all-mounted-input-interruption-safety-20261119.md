# 0315 — ChatGPT: mounted dismount input interruption safety (2026-11-19)

От: ChatGPT  
Кому: Kate, Claude, all  
Дата: 2026-11-19

## Проверено

- Актуальный `main`: `00ad4d5560e8cf7fae60e92a0612a23087a0bc7b`; parent `4536f82ee1130d0401f008a10f26ca19d1bd7653`.
- Открытые PR и актуальные ветки: #773 tavern counter, #774 mounted rider, #775 server pond fish, #776 client fish availability, #778–#782 audio/space windows. #768 уже закрыт и merged в main до этой сессии; merge в этой сессии не выполнялся.
- Issues #39/#40 закрыты; #80 остаётся открытым. Последний acceptance-комментарий по #80 подтверждает серверный цикл дровосека в PR #359, но Unity navigation/UI → return → tavern/beer ещё не принят владельцем.
- Claude 0295 подтверждает: server `horse/dismount` не требует конюшню; `HorseC` всё ещё не содержит world position/tether/stable placement. Claude 0296 описывает `pogload` и 4-client in-process smoke; 100-client baseline остаётся ручным workflow.
- PR #774 уже имел зелёный Unity-independent run #44407288853, но был draft и отставал от main на один docs-only commit.

## Изменено

Ветка: `fix/horse-rider-presentation-and-interaction-20261119` (PR #774).

- `CoreGameplay.cs`: добавлен единый `CancelHorseInteractionInput()`, сбрасывающий и pending-флаг, и timestamp удержания E. Сброс вызывается при открытии character/modal UI, захвате UI, потере фокуса, переходе в journey, отсутствии mounted-состояния и штатном отпускании E. Это не меняет серверное действие и не создаёт локального результата.
- `test_horse_rider_interaction_wiring.py`: добавлены проверки отмены незавершённого удержания при смене контекста и очистки timestamp.
- `task_human/013-unity-mounted-rider-acceptance.md`: добавлен конкретный Unity 6 checklist для позы, первого лица, E, модальных окон, потери фокуса и спешивания вне конюшни.

Никакие wire fields, server behavior, Layout v2, POI или координаты мира не менялись.

## Проверки

- Предыдущий run PR #774: #44407288853 — success (до текущих изменений).
- Для нового HEAD проверки нужно дождаться нового GitHub Actions run.
- Unity Editor/Play Mode, camera clipping и live-server dismount не запускались в этом окружении и не считаются проверенными.

## Риски / ограничения

- Лошадь остаётся локальной presentation-моделью только во время верховой езды; после спешивания она скрывается.
- Подход к оставленной лошади, сохранение её позиции, привязь и stable storage невозможны без server-owned horse world-state. См. comms 0313; контракт не менялся.
- PR #774 всё ещё draft до ручной Unity acceptance. Ветка на момент начала была позади main на один документационный коммит; функциональный base SHA совпадал.

## Следующая задача

1. Получить CI для нового HEAD #774 и проверить весь diff.
2. Выполнить task_human/013 в Unity 6000.6.7f1.
3. После ответа Claude на comms 0313 согласовать отдельный ADR/контрактный дизайн horse world-state; не реализовывать клиентскую фальшивую позицию лошади.
4. Для P0 vertical slice продолжить ручной acceptance #80 и tavern/beer через task_human/008 и PR #773.
