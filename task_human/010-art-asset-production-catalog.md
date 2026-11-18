!В работе
# 010 — Производство и импорт ассетов: 3D-модели, звук, анимации и текстуры

**Исполнитель:** владелец проекта (генерация/отбор ассетов и локальная проверка Unity).  
**Поддержка:** ChatGPT — подготовка промптов, Unity asset pipeline и интеграция; Claude — только если понадобится серверный контракт/описание существующих DTO.  
**Подсистема:** art production / Unity 6 URP / asset pipeline.  
**Проект:** «Пограничье», историко-бытовой реализм, мир Ёф.  
**Статус:** !В работе

## Цель

Последовательно собрать собственную библиотеку качественных игровых ресурсов для вертикальной демки и дальнейшего расширения мира: персонажи, одежда, здания, интерьер, предметы, природа, животные, инструменты, оружие, аудио, анимации и PBR-материалы.

Это производственная задача для владельца: открыть генераторы (Meshy.ai и подходящие инструменты для звука/анимации/текстур), создать варианты по промптам ниже, выбрать лучшие, экспортировать и проверить их в Unity.

Не пытаться сгенерировать всё сразу. Сначала создать эталонные ассеты P0, проверить художественный стандарт и только после этого масштабировать библиотеку.

## Обязательные правила проекта

1. Сервер — единственный источник истины для игровых сущностей, их количества, положения, состояния и действий. Не создавать в Unity локальных NPC, животных, дома, предметы мира или POI «для заполнения пустоты».
2. Генерируемые модели — визуальные ресурсы/префабы для отображения сущностей, которые пришли от сервера. Клиентская растительность и декор допустимы только там, где это разрешено существующими server descriptors/seed и текущим контрактом.
3. Не менять существующие ID, названия поселений, держав и регионов.
4. Не менять масштабы серверных footprint/layout ради подгонки модели. Подгонять визуальный asset под реальные размеры DTO; если размеров не хватает — зафиксировать вопрос, не выдумывать.
5. Историко-бытовой реализм Восточной Европы, без фэнтези-перегруза, мультяшности, пластиковой кожи, чрезмерно чистых материалов и современных предметов.
6. Unity 6, URP. Единица Unity — 1 метр. Ориентация, pivot, масштаб, коллайдеры и LOD должны быть предсказуемыми.
7. Не использовать чужие модели/звуки без проверки лицензии. Для каждого стороннего ресурса сохранить автора, URL, лицензию, дату загрузки и ограничения. Предпочитать CC0 или лицензии, явно разрешающие коммерческое использование.
8. Не включать baked lighting/shadows в Base Color. Для материалов сохранять PBR maps.
9. Сохранять исходники генерации отдельно от импортированных Unity-копий.
10. Один эталонный ассет должен пройти импорт и runtime-проверку до массового производства похожих ассетов.

## Общие технические ориентиры

- Персонаж: взрослый рост примерно 1.65–1.90 м; эталонный мужчина 1.78 м. Проверять реальные пропорции.
- Обычный NPC: LOD0 ориентир 25–45 тыс. треугольников; герой до 45–70 тыс.
- Малый инструмент: 2–8 тыс. треугольников; меч 3–10 тыс.
- Небольшой дом: 8–20 тыс.; крупная корчма 20–45 тыс.; дерево 2–8 тыс.; куст 0.5–2.5 тыс. треугольников на LOD0.
- Персонажи: 2K для эталонного уникального персонажа, 1K для массовых NPC после проверки; здания 1–2K; деревья/кусты atlas 1–2K; инструменты 512–1K; мелкие props 256–512.
- Использовать LODGroup для заметных объектов; коллайдеры простые и только там, где нужны взаимодействие/столкновение.
- URP/Lit, корректные normals/tangents, UV без наложений там, где нужны уникальные карты. Packed ORM допускается (R=AO, G=Roughness, B=Metallic) при согласовании импортёра.
- Для звука отдельные группы Music / Ambience / SFX / Voices; WAV master и OGG runtime. Позиционные SFX преимущественно mono, музыка/общий ambience stereo.
- Все action sounds запускать по подтверждённому игровому событию/состоянию, а не просто по нажатию клавиши.

## 1. 3D-персонажи

### P0 — эталонные персонажи

- [ ] M01 Базовый взрослый мужчина, полный рост, нейтральная A-pose.
- [ ] F01 Базовая взрослая женщина, полный рост, нейтральная A-pose.
- [ ] M02 Вариант мужчины: худощавый.
- [ ] M03 Вариант мужчины: крепкий/плотный.
- [ ] F02 Вариант женщины: худощавая.
- [ ] F03 Вариант женщины: крепкая.
- [ ] H01 Базовый скелет Humanoid и тест ретаргетинга.
- [ ] C01 Набор 6–10 лиц/голов с разными, но естественными чертами.
- [ ] H02 Набор волос: короткие, средней длины, длинные, собранные.
- [ ] H03 Бороды/усы: гладко выбритый, щетина, короткая борода, длинная борода, усы.
- [ ] H04 Возрастные варианты: пожилой мужчина и пожилая женщина.
- [ ] H05 Подросток и ребёнок — только после проверки пропорций и анимационного пайплайна.

**Промпт Meshy — базовый мужчина (сгенерировать первым):**

"Create a realistic game-ready full-body adult male character for a grounded historical Eastern European medieval life simulation RPG. Age 30–35, height exactly 1.78 meters, average sturdy build shaped by everyday manual work, natural anatomically correct proportions, believable non-heroic face, subtle asymmetry and skin imperfections. Everyday rural clothing: loose undyed linen shirt, muted brown/olive wool tunic, simple leather belt, plain trousers, worn leather ankle boots. Historically plausible 12th–15th century-inspired construction without claiming exact museum reconstruction. Neutral symmetrical A-pose, arms slightly away from torso, hands relaxed with separated fingers, feet grounded. Full body, isolated character, no props, no weapon, no pedestal, no environment. Clean animation-friendly topology with proper loops at shoulders, elbows, wrists, hips, knees, ankles and neck; clean UVs; PBR materials; no baked shadows; game-ready optimized mesh. Unity Humanoid rig compatible. Target 25k–45k triangles for LOD0. Export FBX plus Base Color, Normal, Roughness and Metallic/ORM maps."

**Промпт Meshy — базовая женщина:**

"Create a realistic full-body adult female inhabitant of a grounded medieval Eastern European rural settlement, age 28–35, height 1.68 meters, natural average build, anatomically believable proportions and face, subtle imperfections. Practical historically inspired everyday clothing: linen blouse, wool overdress or simple tunic, long skirt, modest apron, leather shoes, muted natural dyes (undyed linen, brown, muted green, dark red). No fantasy costume, no modern fashion, no jewelry overload. Neutral A-pose, relaxed separated fingers, feet grounded, full body isolated, no props or pedestal. Clean animation-ready topology, Unity Humanoid compatible rig, UV unwrapped, PBR textures, no baked lighting, optimized game-ready mesh."

**Промпт Meshy — вариация лица/головы:**

"Realistic human head and neck for a grounded medieval Eastern European adult NPC, natural facial anatomy, subtle asymmetry, believable skin pores and age detail, neutral expression, no makeup, no fantasy features, no exaggerated beauty, clean topology with facial edge loops suitable for basic facial animation, UV unwrapped, PBR skin material, isolated head, neutral lighting."

**Промпт Meshy — причёска:**

"Historically plausible medieval Eastern European [male/female] hairstyle, natural hair volume and strand clumping, practical everyday grooming, game-ready optimized hair mesh, clean silhouette, no modern haircut styling, no ornaments unless specified, compatible with humanoid character head, PBR material, isolated asset."

### P1 — одежда и профессии

- [ ] CL01 Комплект крестьянина (мужской).
- [ ] CL02 Комплект крестьянки (женский).
- [ ] CL03 Комплект кузнеца: фартук, плотная рубаха, нарукавники.
- [ ] CL04 Комплект плотника.
- [ ] CL05 Комплект охотника.
- [ ] CL06 Комплект рыбака.
- [ ] CL07 Комплект корчмаря.
- [ ] CL08 Комплект торговца.
- [ ] CL09 Комплект стражника.
- [ ] CL10 Комплект путешественника.
- [ ] CL11 Зимняя одежда: плащ, меховая накидка, шапка, рукавицы.
- [ ] CL12 Дождевой плащ/накидка.
- [ ] CL13 Обувь и головные уборы отдельными вариантами.
- [ ] CL14 Пояса, сумки, кошели и небольшие аксессуары.

**Промпт одежды:**

"Historically grounded medieval Eastern European [PROFESSION] clothing set, practical layered linen and wool garments, hand-stitched seams, natural fabric folds, subtle wear, historically plausible cut, muted natural dyes, separate wearable modular clothing pieces fitted to a realistic humanoid base body, clean game-ready geometry, UVs, PBR fabric/leather materials, no mannequin, no fantasy armor, no modern elements."

## 2. Архитектура и интерьер

### P0 — видимые в вертикальной демке

- [ ] B01 Эталонный небольшой деревянный жилой дом.
- [ ] B02 Эталонная корчма снаружи.
- [ ] B03 Внутренний зал корчмы.
- [ ] B04 Стойка корчмы.
- [ ] B05 Дверь с настоящим проёмом и открыванием.
- [ ] B06 Окно с настоящим сквозным проёмом, рамой, стеклом/ставнями и внутренней стороной.
- [ ] B07 Стол, лавки и стулья.
- [ ] B08 Кровать, сундук, полки.
- [ ] B09 Очаг/печь с безопасной точкой размещения света и визуальным огнём.
- [ ] B10 Крыша из дранки/соломы.
- [ ] B11 Деревянный забор и калитка.
- [ ] B12 Колодец.

**Промпт дома:**

"Modular realistic medieval Eastern European rural timber house, believable 1:1 human scale, hand-hewn logs and timber joinery, weathered wood, limewashed clay infill where appropriate, steep shingle or thatch roof, functional entrance with a real open doorway, real window holes through the wall (not dark window decals or cubes placed on top), visible wall thickness, interior and exterior surfaces, simple grounded construction, no fantasy styling. Game-ready modular asset, clean topology, UVs, PBR textures, separate roof/walls/door/window modules, pivots suitable for Unity, no surrounding village, no terrain, no baked lighting."

**Промпт корчмы:**

"Grounded medieval Eastern European village tavern, modest but welcoming timber building, believable 1:1 scale, wide entrance, real window openings that transmit daylight, visible structural beams, weathered timber, plaster, shingle roof, chimney, practical sign bracket, no fantasy inn clichés, no oversized props, no modern elements. Modular game-ready exterior and separate interior kit, clean UVs, PBR, Unity URP, no baked lighting."

**Промпт модульного интерьера:**

"Modular medieval Eastern European tavern interior kit: timber plank floor, timber and plaster walls, structural beams, real door and window openings, bar counter, sturdy tables, benches, shelves, hearth surround, practical storage. Historically grounded, worn by use, correct human scale, separate modular pieces with consistent pivots, game-ready low-to-mid poly topology, UVs, PBR materials, no people, no lighting baked into textures."

### P1 — мастерские и поселение

- [ ] B13 Кузница.
- [ ] B14 Пекарня.
- [ ] B15 Мастерская плотника.
- [ ] B16 Мастерская гончара.
- [ ] B17 Амбар.
- [ ] B18 Сарай.
- [ ] B19 Конюшня.
- [ ] B20 Баня.
- [ ] B21 Торговая лавка/рынок.
- [ ] B22 Склад.
- [ ] B23 Двухэтажный жилой дом.
- [ ] B24 Городской дом.
- [ ] B25 Ворота/частокол.
- [ ] B26 Каменный фундамент/стена.
- [ ] B27 Лестницы, крыльца, балконы.
- [ ] B28 Модульные крыши, фронтоны, трубы, водостоки.
- [ ] B29 Дымоходы и вентиляционные детали.

**Промпт мастерской:**

"Historically grounded medieval Eastern European [WORKSHOP TYPE], practical working building, authentic timber framing, stone or clay hearth/furnace where appropriate, weathered surfaces, realistic human scale, functional doors and windows with actual openings, modular construction kit, separate structural parts and props, optimized game asset, clean UVs, PBR, no fantasy elements, no people."

**Промпт дверей и окон:**

"Modular medieval timber door/window assembly, real architectural opening sized for a 1.78m adult, visible wall reveal and frame depth, believable hinges and wooden construction, optional shutters, separate open/closed door mesh if needed, correct pivot at hinge, game-ready optimized mesh, PBR wood, no wall behind the opening."

## 3. Природа, ландшафтные ассеты и животные

### P0 — базовая среда

- [ ] N01 Дуб взрослый.
- [ ] N02 Берёза взрослая.
- [ ] N03 Ель взрослая.
- [ ] N04 Сосна взрослая.
- [ ] N05 Набор кустарников 3–5 силуэтов.
- [ ] N06 Папоротник.
- [ ] N07 Низкая трава.
- [ ] N08 Высокая трава.
- [ ] N09 Полевые цветы.
- [ ] N10 Пни и поваленные ветви.
- [ ] N11 Камни/валуны 5–8 форм.
- [ ] N12 Грибные группы.
- [ ] N13 Мох и лесная подстилка.

**Промпт дерева:**

"Realistic temperate Eastern European [OAK/BIRCH/SPRuce/PINE] tree, botanically believable branching and silhouette, natural trunk proportions, detailed bark, season-neutral foliage, no stylized fantasy shapes. Game-ready environment asset, 1:1 scale, optimized mesh, separate trunk and foliage materials, clean UVs, PBR textures, LOD0/LOD1/LOD2 variants, simple collision trunk, no ground plane, no scene."

**Промпт куста/травы:**

"Realistic temperate Eastern European [BUSH/FERN/GRASS/WILDFLOWER], natural irregular silhouette, believable leaf shapes, muted natural color variation, game-ready optimized mesh, foliage material suitable for URP, wind animation pivot at base, LOD variants, no pot, no background."

### P1 — животные

- [ ] A01 Лошадь.
- [ ] A02 Корова.
- [ ] A03 Свинья.
- [ ] A04 Овца.
- [ ] A05 Коза.
- [ ] A06 Собака.
- [ ] A07 Кошка.
- [ ] A08 Курица и петух.
- [ ] A09 Гусь и утка.
- [ ] A10 Олень.
- [ ] A11 Кабан.
- [ ] A12 Волк.
- [ ] A13 Лиса.
- [ ] A14 Заяц.
- [ ] A15 Медведь.
- [ ] A16 Ворон, сорока и мелкие птицы.

**Промпт животного:**

"Realistic anatomically accurate [ANIMAL] native or historically plausible to temperate Eastern Europe, natural proportions and coat/feather detail, neutral standing pose, full body isolated, clean deformation topology, riggable skeleton, UV unwrapped, PBR textures, game-ready optimized mesh, no stylization, no accessories, no background."

## 4. Предметы, мебель, инструменты и оружие

### P0 — корчма и быт

- [ ] P01 Пивная деревянная/глиняная кружка — основной вариант.
- [ ] P02 Кружка с наполненным пивом (отдельное состояние/меш).
- [ ] P03 Кувшин.
- [ ] P04 Бутыль.
- [ ] P05 Деревянная миска.
- [ ] P06 Ложка и простая посуда.
- [ ] P07 Бочка.
- [ ] P08 Корзина.
- [ ] P09 Мешок.
- [ ] P10 Сундук.
- [ ] P11 Стол и лавка.
- [ ] P12 Свеча/подсвечник.
- [ ] P13 Масляная лампа.
- [ ] P14 Факел.
- [ ] P15 Очаг/дрова.
- [ ] P16 Полки и кухонная утварь.

**Промпт кружки:**

"Single realistic medieval Eastern European tavern beer mug, sturdy handcrafted wooden or coarse ceramic vessel, believable hand-held scale (approximately 12–16 cm tall), subtle wear, physically plausible rim and handle, separate clean mesh, UV unwrapped, PBR wood/ceramic material, game-ready optimized topology, isolated on neutral background, no liquid unless requested, no text, no modern design."

### P1 — профессии и экипировка

- [ ] P17 Молот кузнеца.
- [ ] P18 Наковальня.
- [ ] P19 Щипцы и клещи.
- [ ] P20 Топор.
- [ ] P21 Пила.
- [ ] P22 Рубанок/стамеска.
- [ ] P23 Лопата.
- [ ] P24 Мотыга.
- [ ] P25 Серп.
- [ ] P26 Вилы/грабли.
- [ ] P27 Рыболовная сеть/удочка.
- [ ] P28 Лук и стрелы.
- [ ] P29 Меч.
- [ ] P30 Щит.
- [ ] P31 Простое копьё.
- [ ] P32 Кожаный пояс/ножны/сумка.
- [ ] P33 Седло и конская упряжь.
- [ ] P34 Телега и колёса.
- [ ] P35 Дрова, сено, связки хвороста.

**Промпт инструмента:**

"Historically plausible medieval Eastern European [TOOL], accurate functional construction, realistic dimensions and wear, wood and forged iron materials, no ornamental fantasy details, isolated single object, clean optimized game-ready mesh, correct pivot/origin for hand attachment, UVs, PBR textures, no hands, no background."

## 5. Анимации

Анимации можно создавать через Meshy/анимационные сервисы/Blender. Сначала проверить один базовый Humanoid rig и ретаргетинг; не производить полный набор до проверки скелета.

### P0 — обязательные анимации человека

- [ ] AN01 Idle neutral — спокойная стойка, loop.
- [ ] AN02 Idle variation — перенос веса, взгляд, дыхание.
- [ ] AN03 Walk forward — цикл ходьбы.
- [ ] AN04 Run forward — цикл бега.
- [ ] AN05 Start/stop walk-run.
- [ ] AN06 Turn left/right.
- [ ] AN07 Sit down on bench.
- [ ] AN08 Seated idle at table.
- [ ] AN09 Stand up from bench.
- [ ] AN10 Drink from mug — поднести кружку, отпить, опустить.
- [ ] AN11 Hold mug / seated drinking idle.
- [ ] AN12 Talk gesture — спокойный разговор руками.
- [ ] AN13 Listen — слушать собеседника.
- [ ] AN14 Pick up object.
- [ ] AN15 Open/close door.
- [ ] AN16 Work with hammer.
- [ ] AN17 Chop wood.
- [ ] AN18 Dig ground.
- [ ] AN19 Use hoe / farm work.
- [ ] AN20 Basic sword attack.
- [ ] AN21 Basic block.
- [ ] AN22 Hit reaction.
- [ ] AN23 Fall / get up.

**Промпт анимации человека:**

"Create a grounded realistic human [ACTION] animation for a standard Unity Humanoid skeleton. Natural biomechanics, believable weight transfer, feet remain planted where appropriate, subtle secondary motion, restrained non-theatrical gestures, consistent character proportions, clean looping cycle if requested, no camera movement, no environment, no prop unless specified. Preserve retargetability and avoid foot sliding."

**Промпт питья пива:**

"Realistic seated medieval tavern drinking animation for a Unity Humanoid character: character sits naturally on a bench at a table, reaches for a mug, grips it with believable hand contact, lifts it to the mouth, tilts the mug slightly, takes one or two measured sips, lowers the mug and places it back on the table. Natural elbow and shoulder motion, no exaggerated acting, stable seated hips and feet, clean hand-to-prop interaction, no camera movement."

**Промпт разговора:**

"Subtle natural idle conversation animation for a grounded medieval villager, small hand gestures, head nods, natural eye/head direction changes, slight breathing and weight shifts, no exaggerated cartoon gestures, loopable, Unity Humanoid compatible."

### P1 — бытовые и животные

- [ ] AN24 Sweep / carry / lift / push.
- [ ] AN25 Sit at hearth / eat / cook.
- [ ] AN26 Blacksmith work set.
- [ ] AN27 Carpenter work set.
- [ ] AN28 Fishing / farming work set.
- [ ] AN29 Horse idle/walk/trot/gallop.
- [ ] AN30 Cow idle/walk/graze.
- [ ] AN31 Dog idle/walk/run/sit.
- [ ] AN32 Cat idle/walk/sleep.
- [ ] AN33 Chicken peck/walk/idle.
- [ ] AN34 Bird fly/takeoff/land.
- [ ] AN35 Deer graze/walk/flee.
- [ ] AN36 Wolf walk/run/idle.

**Промпт животной анимации:**

"Natural anatomically correct [ANIMAL ACTION] animation for a game-ready quadruped skeleton. Realistic weight shift and foot placement, species-appropriate gait, restrained natural motion, loopable where appropriate, no camera movement, no environment, preserve proportions, no sliding."

## 6. Звуки и голосовая атмосфера

Для звуков использовать реальные лицензированные библиотеки или генераторы аудио. Важны короткие отдельные слои, чтобы микшировать их в Unity. Не использовать узнаваемые голоса/персонажей из существующих игр. Бормотание NPC — неразборчивое, без имитации конкретной речи, пока нет утверждённой локализованной озвучки.

### P0 — звуки, нужные для демки

- [ ] S01 Шаги по утоптанной земле.
- [ ] S02 Шаги по траве.
- [ ] S03 Шаги по грязи.
- [ ] S04 Шаги по деревянному полу.
- [ ] S05 Шаги по камню.
- [ ] S06 Шаги по снегу.
- [ ] S07 Открытие/закрытие деревянной двери.
- [ ] S08 Скрип половиц.
- [ ] S09 Звон кружки о стол.
- [ ] S10 Наливание пива.
- [ ] S11 Питьё/глоток.
- [ ] S12 Поставить кружку.
- [ ] S13 Треск очага.
- [ ] S14 Тихий гул корчмы.
- [ ] S15 Разговоры посетителей — несколько слоёв неразборчивого бормотания.
- [ ] S16 Смех/одиночные возгласы.
- [ ] S17 Удар молота по металлу.
- [ ] S18 Удар топора по дереву.
- [ ] S19 Взмах меча.
- [ ] S20 Удар оружия по дереву/металлу/земле.
- [ ] S21 Короткие звуки экипировки/кожи.
- [ ] S22 UI feedback — только для интерфейсных подтверждений, ненавязчиво.

**Промпт звукового эффекта:**

"Create a high-quality isolated sound effect: [SOUND]. Historically grounded medieval Eastern European setting, natural acoustic recording, close and clear, no music, no speech unless specified, no modern background noise, short clean tail, no reverb baked in unless specified, export WAV 48 kHz 24-bit. Provide several subtle variations if possible."

**Промпт NPC crowd murmur:**

"Layered non-intelligible medieval tavern crowd walla, several adult voices talking quietly at different distances, occasional soft chuckle, natural overlapping conversational rhythm, no understandable words, no recognizable voice, no modern slang, no music, warm wooden interior acoustic, seamless 30–60 second loop, stereo WAV."

### P1 — региональные эмбиенты и мир

- [ ] S23 Дневной лес: птицы, ветер, листья.
- [ ] S24 Ночной лес: насекомые, редкие птицы, ветер.
- [ ] S25 Поле/луг.
- [ ] S26 Река/ручей.
- [ ] S27 Дождь по крыше и земле.
- [ ] S28 Гроза/гром.
- [ ] S29 Снег/метель.
- [ ] S30 Деревенская улица днём.
- [ ] S31 Деревенская улица ночью.
- [ ] S32 Рынок/торговые голоса.
- [ ] S33 Кузница — общий слой.
- [ ] S34 Конюшня/скотный двор.
- [ ] S35 Лошади: фырканье/ржание/копыта.
- [ ] S36 Собаки/кошки/домашняя птица.
- [ ] S37 Ветер в листве разных типов.
- [ ] S38 Шорох кустов/травы.
- [ ] S39 Колокол/церковный колокол — если соответствует месту и лору.
- [ ] S40 Далёкая телега/скрип колёс.

**Промпт природного эмбиента:**

"Natural immersive temperate Eastern European countryside ambience, [TIME OF DAY] in [BIOME/SEASON], realistic field-recording character, layered wind through birch/oak leaves, distant local birds and subtle insects, no music, no human voices, no traffic, no modern sounds, seamless stereo loop, WAV 48 kHz."

## 7. Музыка

Музыка должна быть оригинальной, региональной и ненавязчивой. Не копировать мелодии/аранжировки существующих игр и фильмов. Для разных регионов — разные тембровые и ритмические акценты после утверждения культурной карты.

- [ ] MU01 Ополье — основная сельская тема.
- [ ] MU02 Лес — редкая атмосферная тема.
- [ ] MU03 Корчма — тёплая акустическая народная инструментальная тема.
- [ ] MU04 Город — более плотная, но сдержанная тема.
- [ ] MU05 Ночное путешествие.
- [ ] MU06 Опасная территория.
- [ ] MU07 Бой — динамическая, не героическая.
- [ ] MU08 Магия/плетения — сдержанный необычный тембр.
- [ ] MU09 Праздник/ярмарка.
- [ ] MU10 Тишина/редкие музыкальные акценты для обычного исследования.

**Промпт музыки:**

"Original instrumental soundtrack for a grounded historical Eastern European medieval life simulation, [REGION/SCENE]. Acoustic folk-inspired palette such as gusli-like plucked strings, bowed fiddle, wooden flute, frame drum and restrained drones; organic human performance, modal melodic character, emotionally nuanced and understated, no modern synths, no cinematic trailer impacts, no vocals, no quotation or imitation of existing game/movie music. Seamless 60–120 second loop, provide stems if possible."

## 8. Текстуры и PBR-материалы

### P0 — материалы корчмы и деревни

- [ ] T01 Состаренная древесина стен.
- [ ] T02 Доски пола.
- [ ] T03 Деревянная крыша/дранка.
- [ ] T04 Солома/камыш.
- [ ] T05 Глиняная штукатурка.
- [ ] T06 Известковая побелка.
- [ ] T07 Грубый камень/фундамент.
- [ ] T08 Земля/утоптанная грязь.
- [ ] T09 Трава/луг.
- [ ] T10 Лён.
- [ ] T11 Шерсть.
- [ ] T12 Кожа.
- [ ] T13 Кованое железо.
- [ ] T14 Глина/керамика.
- [ ] T15 Стекло окна.
- [ ] T16 Древесина кружки/мебели.

### P1 — природные и специальные материалы

- [ ] T17 Кора дуба.
- [ ] T18 Кора берёзы.
- [ ] T19 Кора хвойных.
- [ ] T20 Листва атлас.
- [ ] T21 Хвоя.
- [ ] T22 Мох.
- [ ] T23 Папоротник.
- [ ] T24 Гравий.
- [ ] T25 Песок.
- [ ] T26 Глина/болото.
- [ ] T27 Снег.
- [ ] T28 Лёд.
- [ ] T29 Вода.
- [ ] T30 Ржавый металл.
- [ ] T31 Медь/бронза.
- [ ] T32 Мех.
- [ ] T33 Кирпич.
- [ ] T34 Кровельная черепица (если появится в соответствующей архитектуре).

**Промпт PBR-текстуры:**

"Create a seamless tileable physically based material for [MATERIAL], realistic historical Eastern European medieval surface, natural wear and subtle imperfections, neutral flat lighting, orthographic texture view, no perspective, no cast shadows, no baked highlights, no text, consistent scale. Output Base Color, Normal (OpenGL), Roughness and Ambient Occlusion; Metallic only where physically appropriate. 2K source, seamless edges, suitable for Unity URP/Lit."

**Промпт атласа листвы:**

"Create a clean foliage texture atlas for temperate Eastern European [TREE SPECIES], individual leaf clusters on a transparent background, natural botanical shapes and restrained color variation, evenly spaced non-overlapping islands, no branches, no shadows, no background, texture suitable for alpha-clipped foliage in Unity URP. Include matching normal map if supported."

## 9. Импорт и проверка в Unity

- [ ] Создать локальную структуру исходников/импорта по согласованному asset pipeline.
- [ ] Не перезаписывать существующие ассеты без проверки ссылок и GUID.
- [ ] Проверить FBX scale factor, rotation, pivot и applied transforms.
- [ ] Проверить, что мужчина имеет ровно ожидаемую высоту около 1.78 м.
- [ ] Проверить Humanoid rig и Avatar configuration.
- [ ] Проверить walk/run/idle ретаргетинг и отсутствие foot sliding.
- [ ] Проверить посадку за стол и контакт руки с кружкой.
- [ ] Проверить, что кружка подходит по масштабу руке персонажа.
- [ ] Проверить, что двери имеют правильный pivot на петле и не закрывают серверный проход.
- [ ] Проверить реальные оконные проёмы: дневной свет проходит внутрь; окно не является только текстурой/накладным кубом.
- [ ] Проверить материалы на URP/Lit, нормали и roughness.
- [ ] Проверить LODGroup и визуальные переходы.
- [ ] Проверить colliders; не создавать mesh collider на каждой травинке/мелкой детали.
- [ ] Проверить batching/instancing и отсутствие лишних material instances.
- [ ] Проверить AudioMixer группы Music/Ambience/SFX/Voices.
- [ ] Проверить 3D attenuation, spatial blend, max distance и отсутствие дублирующего локального/сетевого звука.
- [ ] Убедиться, что звуки действий воспроизводятся после подтверждённого события/состояния.
- [ ] Проверить производительность на густом поселении и синтетической нагрузке 100 игроков, если соответствующий тестовый стенд доступен.

## 10. Где хранить и как учитывать

Для каждого сгенерированного ресурса заполнить запись в каталоге ассетов:

| Поле | Что записать |
|---|---|
| ID | Уникальный ID из списка (например M01, S14, T01) |
| Название | Человеческое имя ресурса |
| Категория | Model / Audio / Animation / Texture |
| Генератор | Meshy / Blender / audio tool / другое |
| Prompt | Точная версия использованного промпта |
| Source | Путь к исходному файлу |
| Export | FBX / WAV / OGG / PNG / EXR и т. п. |
| License | CC0 / коммерческая лицензия / собственная генерация |
| Unity path | Фактический путь импорта |
| Status | Planned / Generated / Selected / Imported / Runtime checked |
| Notes | Дефекты, известные ограничения, версия |

Рекомендуемое разделение:
- исходники генерации хранить отдельно от Unity working copies;
- не складывать тяжёлые бинарники в Git без проверки текущей политики репозитория/LFS;
- не менять существующие importer scripts и каталоги без предварительного аудита;
- вести per-file license manifest для сторонних аудио/моделей.

## 11. Порядок выполнения

### Этап A — эталон (сначала)
- [ ] M01 базовый мужчина.
- [ ] F01 базовая женщина.
- [ ] B01 жилой дом.
- [ ] B02/B03 корчма и интерьер.
- [ ] P01 кружка.
- [ ] N01 дуб, N02 берёза, N03 ель, N05 куст.
- [ ] AN01 idle, AN03 walk, AN04 run, AN07 sit, AN10 drink.
- [ ] S01–S05 шаги, S07 дверь, S10–S12 пиво, S13 очаг, S14–S15 корчма.
- [ ] T01–T16 основные материалы.

### Этап B — тест в Unity
- [ ] Импортировать эталонный комплект.
- [ ] Собрать тестовую сцену только из существующих серверных DTO/fixtures, не создавая фиктивный runtime мир.
- [ ] Проверить масштаб, стиль, свет, окна, материалы, rig, анимации и звук.
- [ ] Зафиксировать проблемы и обновить промпты до массовой генерации.

### Этап C — расширение
- [ ] Профессии и одежда.
- [ ] Мастерские/дома/мебель.
- [ ] Животные и их анимации.
- [ ] Региональные природные наборы.
- [ ] Полный звук окружения и музыка.
- [ ] Дополнительные PBR-материалы.

## Критерии готовности

Задача не считается выполненной после одной генерации или скачивания файлов.

- [ ] Все P0 позиции созданы или явно помечены как отложенные с причиной.
- [ ] Есть каталог с точными промптами и версиями выбранных результатов.
- [ ] Для каждого стороннего ресурса есть проверенная лицензия.
- [ ] Эталонный персонаж импортируется в Unity 6 URP в масштабе 1:1.
- [ ] Humanoid rig и базовые анимации реально проверены в Unity.
- [ ] Оконные проёмы пропускают дневной свет, локальный ночной свет выглядит естественно.
- [ ] Звук не дублируется и запускается по игровым событиям.
- [ ] Нет новых локально созданных серверных сущностей.
- [ ] Есть список оставшихся дефектов и фактических результатов runtime-проверки.

## Результат / заметки владельца

Заполнять по мере выполнения:
- Дата:
- Сгенерированные ID:
- Лучшие результаты / ссылки:
- Импортированные Unity paths:
- Лицензии проверены:
- Что проверено в Unity:
- Проблемы / повторная генерация:
- Следующий шаг:

**Следующий владелец после генерации эталонного набора:** ChatGPT — аудит импортируемых ресурсов, настройка Unity pipeline и визуальная интеграция; владелец выполняет Unity Editor/runtime проверку. Серверные DTO и семантику сущностей не менять в рамках этой задачи.
