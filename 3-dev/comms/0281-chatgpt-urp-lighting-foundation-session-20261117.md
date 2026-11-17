# 0281 — ChatGPT: капитальный аудит и фундамент освещения Unity 6 URP

Дата: 2026-11-17
Исполнитель: ChatGPT
Репозиторий: IskatelApelsinov/pogranichye
Связано: PR #699, comms/0191, comms/0276–0280, issue #80.

## Проверено в начале сессии

- main: 865224f865a3be588bb497d68e014d8e807ca0d3.
- Последние изменения main: PR #698 (server-produced fixtures башни/пещеры), #697 (contextual activities/party/horse controls), #696 (path ribbons/locomotion), #695 (slope-aware trails).
- Прочитан полностью Claude comms/0191 (URP visual audit), comms/0276 и comms/0277; прочитан полный новый ответ Claude comms/0280.
- Issues #39 и #40 закрыты; #80 остаётся открытым. #651 остаётся открытым и требует end-to-end thin-client navigation/reconnect.
- Проверены WorldEnvironmentBuilder.cs, VerticalSliceController.cs, WorldLayoutPresentationBuilder.cs, EntityViewRegistry.cs, README и план графического конвейера.
- Подтверждено: в коде уже есть runtime global URP Volume (ACES, restrained ColorAdjustments/WhiteBalance, bloom disabled, subtle vignette) и camera renderPostProcessing. Старый comms/0191 был написан до этого изменения; его утверждение об отсутствии Volume устарело.
- Подтверждено: server-generated spaces/layouts собираются в runtime, поэтому наличие Static renderer не означает baked lightmaps/GI.

## Основные выводы по освещению

1. Критичный визуальный недостаток — AmbientMode.Flat. Один ambient color одинаково заполнял верх/горизонт/землю и делал теневые фасады плоскими.
2. VerticalSliceController.SetupPresentationScene и WorldEnvironmentBuilder конкурировали за RenderSettings; sun/fog не имели единственного владельца.
3. Старый sun discovery мог оставить сцену без directional light, если уже существовали только point lights.
4. Runtime-generated geometry не имеет готового bake; APV/lightmaps нельзя объявлять рабочим решением без bake/matching geometry pipeline.
5. Текущая система использует distance fog, а не height/volumetric fog.
6. Серверный час передаётся как integer hour; визуальная интерполяция между часовыми значениями остаётся отдельной задачей, чтобы избежать ступенчатого движения солнца.

## Изменения

PR: https://github.com/IskatelApelsinov/pogranichye/pull/699
Ветка: chatgpt/urp-lighting-foundation-v2-20261117

- WorldAmbientPalette.cs: чистая policy с отдельными sky/horizon/ground цветами для дня, сумерек и ночи.
- WorldEnvironmentBuilder.cs: Trilight ambient; гарантируется один directional sun; включены мягкие primary shadows; RenderSettings сохраняются и восстанавливаются при Cleanup.
- VerticalSliceController.cs: удалено конкурирующее создание солнца и настройка ambient/fog; единственный владелец runtime освещения — WorldEnvironmentBuilder.
- WorldAmbientPaletteCheck.cs: pure C# regression checks на градиенты, ночную яркость, сумеречный оттенок, finite/range и clamp.
- unity-independent-csharp-checks.yml: добавлен новый test step и path filters.
- Добавлен Unity .meta для WorldAmbientPalette.cs.
- 3-dev/docs/ОСВЕЩЕНИЕ-UNITY6-URP.md: архитектура sun/ambient/interiors/GI/APV/reflections/fog/Volume, стартовые бюджеты и визуальная приёмка 06:00/09:00/13:00/18:00/22:00 + ночная корчма.
- README обновлён.

Коммиты:
- 67b5a6ac28f03fbe8af0a5f8cbf674e8bac30d26 — ambient lighting foundation.
- b2002b82a97e86dc42c4b085f2ea2ea3006b87de — единый владелец sun/fog/ambient.

Wire contract v1, server clock, world coordinates, movement authority и server simulation не менялись.

## Проверки и ограничения

- GitHub compare base..head: 2 commits, 8 files, 296 additions / 27 deletions.
- PR #699 открыт как Draft, mergeable=true при повторном чтении PR metadata.
- На момент записи workflow runs/status ещё не вернулись (пустые массивы). Не заявлять CI green.
- В локальном окружении нет mcs/csc/dotnet; pure C# check будет выполнен GitHub Actions.
- Unity Editor 6000.6.7f1, PlayMode, Lighting bake, Rendering Debugger и GPU Profiler не запускались. Нет визуального подтверждения качества света.
- URP Asset/QualitySettings/LightingSettings asset в source scaffold не найден; его нужно создать/назначить в Editor, иначе нельзя подтвердить фактические shadow-map/HDR/renderer settings.

## Следующая конкретная задача

1. Получить результат CI PR #699 и устранить любые ошибки.
2. В Unity Editor проверить sun shadows, Trilight, volume mask, day/night и таверну.
3. Затем сделать отдельный clock interpolation policy для плавного движения солнца между целыми server hours.
4. Сохранить воспроизводимый URP Asset и coordinated Low/Medium/High quality profiles; только после этого рассматривать APV для fixed authored prefab interiors.
5. Вернуться к issue #80: реальный tutorial task → turn-in → tavern → beer/co-drink.

Merge не выполнялся.
