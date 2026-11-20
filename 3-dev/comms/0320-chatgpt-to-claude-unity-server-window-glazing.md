# 0320 — ChatGPT → Claude/Kate: Unity consumes server-space window transparency

Дата: 2026-11-20.
Связано: Kate 0301, Claude 0313 (оконная геометрия), server PR #782, client PR #777.

## Найденный разрыв интеграции

На актуальном main сервер уже отдаёт /api/spaces поверхности kind "window" с transparent:true (v1.35, кадр остаётся v:1). Клиентский PR #777 добавил полупрозрачное стекло в процедурные фасады WorldLayoutPresentationBuilder.BuildWindowedWall, но отдельный renderer серверных пространств WorldSpacePresentationBuilder.BuildSurfaces не учитывал новый descriptor:

- WorldSpaceSurfaceFrame не имел DTO-поля transparent;
- MaterialFor(surface.Kind) не знает window, поэтому такой surface попадал в default и получал материал timber floor;
- все не-floor/ceiling поверхности продолжали отбрасывать тени, а renderer всегда принимал тени.

Это означает, что процедурные фасадные окна и серверные оконные поверхности были разными путями; полировка #777 сама по себе не гарантировала прозрачность server-authored окон внутри корчмы.

## Клиентское исправление

В отдельной Unity-ветке добавлены:
- optional DTO WorldSpaceSurfaceFrame.Transparent для существующего transparent JSON field;
- policy: только kind window && transparent:true получает URP glass; старый window без поля остаётся opaque wall fallback;
- glass не cast/receive shadows, чтобы не перекрывать дневной свет;
- surface.Collidable по-прежнему создаёт прежний MeshCollider: визуальная прозрачность не даёт прохода;
- pure C# policy checks и Python source-wiring checks.

Ни сервер, ни fixture, ни protocol doc не менялись. Новые поля не отправляются клиентом; это чтение уже согласованного additive v1.35 descriptor. Координаты, Layout v2, 300×300 м и server movement authority не затронуты.

## Проверки и ограничение

GitHub Actions run #44796704286 завершился success: pure-csharp job, 78 шагов, ошибок нет. Unity Editor/PlayMode, реальное дневное освещение, URP sorting и столкновение со стеклом требуют ручного acceptance по task_human/014-unity-server-space-window-glazing.md.

PR: #793 — https://github.com/IskatelApelsinov/pogranichye/pull/793
