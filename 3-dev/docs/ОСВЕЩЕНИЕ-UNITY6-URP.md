# Освещение «Пограничья» — техническая архитектура и план приёмки

Дата: 2026-11-17. Цель: историко-бытовой реализм в Unity 6.6 / URP 17.6 при серверной генерации поселений.

## 1. Source audit: текущее состояние

Проверены WorldEnvironmentBuilder.cs, VerticalSliceController.cs, WorldLayoutPresentationBuilder.cs, EntityViewRegistry.cs и README.

- Runtime создаёт procedural skybox, звёзды, облака, ветер, пыльцу и низинную дымку.
- WorldAtmosphereCycle принимает серверный час через SetServerHour, иначе работает preview-цикл; меняет солнце, sky tint, ambient и fog.
- Уже существует один global URP Volume: ACES, умеренные ColorAdjustments/WhiteBalance, Bloom выключен, слабая vignette. Камера включает renderPostProcessing.
- Очаги/фонари/экипированные источники используют локальные lights; большинство point lights без теней.
- Главный дефект: AmbientMode.Flat равномерно заполнял все поверхности одним цветом. Это уплощает объём и ослабляет ощущение направления света. Controller повторно назначал такой же flat fill.
- FindSun мог вернуть null, если в сцене был point light и поэтому bootstrap не создавал directional light.
- Server-authored layout/terrain строятся в runtime. Наличие MeshRenderer.isStatic не создаёт lightmap и не означает baked GI.
- В репозитории не найден подтверждённый URP Asset/Quality/LightingSettings asset. Следовательно, включение shadow maps, HDR, MSAA и renderer features пока не доказано.

## 2. Рекомендуемая архитектура света

### Главный свет
Один realtime directional sun отвечает за дневной direct light и основные тени. Он движется/меняет цвет только через WorldAtmosphereCycle. В URP Asset требуется Main Light Shadows. Тени — только от солнца; не добавлять shadow maps от каждой свечи и лампы. Shadow distance и cascades настраивать по quality profile.

### Ambient
Для runtime-generated settlements использовать AmbientMode.Trilight: Sky / Equator-Horizon / Ground. Днём небо холоднее, горизонт нейтральнее, земля темнее; ночью весь ambient заметно слабее, но не нулевой; на рассвете/закате добавляется ограниченный тёплый компонент. Это направленный fill, не GI и не цветовой bounce.

### Интерьеры
Эмиссия делает сам материал ярким, но не освещает соседние объекты. Настоящий local light нужен только у очага, лампы над столом или рабочего места. Point/spot lights по умолчанию без теней, с коротким радиусом и ограничением активных источников рядом с игроком. Дальние окна/фонари — emissive geometry без Light component. В таверне нужен тёплый локальный свет, но холодный ambient от окон/дверей сохраняет читаемость теней.

### GI и APV
URP runtime-generated spaces не имеют заранее запечённых lightmaps. Не заявлять APV как решение для случайно генерируемых зданий: APV требует корректного bake, matching geometry и sampling. Использовать baked GI/APV позже для фиксированных авторских prefab-сцен/интерьеров после отдельного воспроизводимого bake pipeline. Сейчас честная база: realtime sun + Trilight + несколько unshadowed local lights.

### Reflections
Не создавать realtime Reflection Probe на каждый дом. Начать с skybox/environment reflections. Для ключевой таверны допустим один baked/custom cubemap probe после проверки. Металл, вода и мокрая грязь требуют согласованного roughness/smoothness, иначе мир станет пластиковым/зеркальным.

### Fog
Текущий ExponentialSquared — distance fog, не height fog и не volumetric fog. Он смягчает дальний план, но не должен скрывать NPC, POI и ориентиры. Не добавлять тяжёлый volumetric fog или прозрачные туманные плоскости глобально; ранее были URP артефакты белых пятен. Погодные частицы держать локальными и bounded.

### Volume / exposure
Global Volume — grading/tonemapping, не источник света. ACES оставить как старт. Сохранять детали дерева, кожи, земли и лица; Bloom пока выключен, vignette минимальна. Local volumes для помещений — только после проверки blending и Volume Mask; экспозиция не должна прыгать в дверном проёме.

## 3. Стартовые бюджеты (не текущие фактические настройки)

| Элемент | Low | Medium | High |
|---|---:|---:|---:|
| Shadow distance | 35–45 м | 60–80 м | 90–120 м |
| Sun cascades | 1–2 | 2 | 4 |
| Shadowed realtime lights | 1 sun | 1 sun | 1 sun |
| Nearby local lights | 2–4 | 4–6 | 6–8 |
| Local light shadows | 0 | 0 | 0–1 только после профилирования |
| Bloom | off | off/very low | low после проверки |
| AO | off | low если доступно | low/medium после GPU профиля |

Это исходные бюджеты для измерения, а не обещание FPS. Качество должно переключать согласованные профили, а не независимые случайные значения.

## 4. Эталонная приёмка

Маршрут 40–60 м из server Layout/Spaces: открытая улица → двор/деревья → вход в корчму → стол/очаг → обратно на улицу. Снять одинаковые кадры:
- 06:00: холодный рассвет, длинные тени.
- 09:00: мягкий дневной.
- 13:00: нейтральный максимум.
- 18:00: тёплый закат.
- 22:00: тёмная улица, читаемые фонари/окна.
- Ночная корчма: локальный очаг, читаемые лица/предметы, тёмные углы, нет sun leaks.

Проверить контактные тени NPC, acne/peter-panning, каскадные скачки, клиппинг окон, читаемость UI, стабильность света на chunk seam, GPU frame time, число realtime lights/shadow casters.

В Unity 6000.6.7f1 назначить URP 17.6 asset в Graphics и каждый активный Quality level; проверить Main Light Shadows/HDR. Создать VerticalSlice scene, открыть Lighting и Rendering Debugger, выполнить PlayMode. APV не включать без реального bake test.

## 5. Изменения текущей итерации

- Flat ambient заменён Trilight sky/horizon/ground palette.
- Палитра вынесена в чистую WorldAmbientPalette policy с отдельными тестами.
- WorldEnvironmentBuilder гарантирует directional sun даже при наличии point lights, включает мягкие primary shadows и сохраняет/восстанавливает прежние RenderSettings при teardown.
- Убран конфликтующий flat ambient из VerticalSliceController.
- Wire contract, серверное время и авторитетность не меняются.

## 6. Следующие этапы

1. Владелец запускает Unity Editor и присылает скриншоты/дамп URP Asset и Lighting settings.
2. Сохранить воспроизводимый URP Asset и согласованные Quality profiles в репозитории.
3. Проверить directional shadow maps и интерьерный свет.
4. Добавить локальные interior light rigs с лимитом активных lights.
5. Утвердить PBR/reflection; затем оценить APV для авторских prefab-сцен.
6. Только после профилирования рассматривать volumetric fog, SSAO и дополнительные renderer features.

## 7. Ограничения

В этом окружении нет Unity Editor/Batchmode. Скриншоты, bake, PlayMode, GPU Profiler и фактический FPS не заявляются. CI проверит только математику палитры, не визуальный результат URP.
