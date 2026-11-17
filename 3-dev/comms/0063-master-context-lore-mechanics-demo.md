# 0063 — Master context: lore, mechanics and playable demo completion

Дата: 2026-11-10. От: ChatGPT. Кому: Claude Fable 5.2. Копия: владелец проекта.
Основание: запрос владельца после PR #199/#200 и последующих #203–#206. Это долговременная передача контекста для нового чата/модели. Не считать этот файл заменой первоисточников: сначала прочитать указанные документы полностью.

## 0. Задача и ожидаемый результат

Нужно не просто сделать очередной технический PR. Твоя задача — восстановить и сохранить ранее согласованный замысел «Пограничья», проверить все текущие реализации и затем довести проект до **действительно играбельной, воспроизводимой демо-версии**.

Работай в таком порядке:

1. Найди и прочитай все исходные документы о лоре, мире и механиках, перечисленные ниже, включая архив старого прототипа и коммиты/PR, если документ уже изменялся.
2. Составь реестр канона: подтверждённые решения владельца; старые числовые параметры/прототипные детали, требующие подтверждения; идеи ассистентов, которые никогда не были утверждены; уже реализованные системы; только декоративные элементы.
3. Сверь реестр с текущим main и тестами. Не называй механику готовой по наличию кнопки, модели, комментария или старого прототипа.
4. Подготовь gap analysis рабочей демо: что реально работает end-to-end, что частично работает, что только presentation, что сломано/не проверено.
5. После фиксации этого контекста выполняй небольшие вертикальные PR, закрывающие критические пробелы. Цель — играбельная демо, а не бесконечное расширение бэклога.
6. Веди один обновляемый документ статуса демо: фактический статус, тесты, известные дефекты, точные инструкции запуска, следующий PR и критерии приёмки.

Не начинай с нового глобального рефакторинга. Сначала восстанови контекст и факты.

## 1. Первоисточники, которые нужно прочитать целиком

### Основные документы
- `3-dev/docs/СКЕЛЕТ-ЖИВОГО-МИРА.md` — основной дизайн нового живого ядра, архитектура ECS, разум NPC, экономика, сохранение, мир и дорожная карта. Важен раздел 13 с дополнениями на 2026-11-02: ранняя часть документа отстаёт от последующего кода.
- `3-dev/handoff/2026-11-02-claude-to-chatgpt.md` — первоначальное распределение ролей, протокол, состояние сервера и известные расхождения.
- `3-dev/archive/legacy-prototype/ИДЕИ-СЛЕДУЮЩАЯ-ИТЕРАЦИЯ.md` — старый большой backlog/дизайн-план. Это важный источник механик, но его статус в шапке был «ПЛАНИРОВАНИЕ»: ничего не считать реализованным только по этому файлу.
- `3-dev/archive/legacy-prototype/world/magic.py` и связанные `world/body.py`, `world/player.py`, `world/economy.py`, `world/life.py`, `world/social.py`, `world/travel.py`, `world/quests.py` — исходный прототип механик. Использовать как референс и источник прежних решений, не копировать автоматически старую реализацию.
- `3-dev/docs/ПРОТОКОЛ-КЛИЕНТ-СЕРВЕР.md` — актуальный wire contract. Текущая линия v1.11; не менять без отдельного согласования.
- `3-dev/docs/adr/0003-world-model-and-demo-boundary.md` — географический канон: несколько деревень демо-зоны, физические дороги, горный барьер и gateway к большому миру; `op:"travel"` не является обычным способом перемещения Unity.
- `3-dev/comms/0024-chatgpt-unity-gameplay-design-proposal.md` — подробное предложение по Unity gameplay, бою, крафту, рыбалке, луку, магии и учебной деревне.
- `3-dev/comms/0027-chatgpt-to-claude-multiplayer-village-and-beer.md` — сценарий двух игроков, authoritative presence, solo beer и совместный тост.
- `3-dev/comms/0028-chatgpt-to-claude-alife-and-conference-demo.md` — A-Life и критерии демо на 5–10 минут.
- `3-dev/comms/0008-claude-mage-awakening-design.md` — ранний дизайн пробуждения магов.
- `3-dev/comms/0051-chatgpt-to-claude-followup-nav-talk.md`, `0052-chatgpt-to-claude-server-hardening-recheck.md`, `0054-chatgpt-to-claude-unity-demo-progress.md`, `0059-chatgpt-to-fable52-next-vertical-slice.md` — последующие уточнения и handoff. Прочитать вместе с ответами Claude/Fable в GitHub PR discussions.
- `2-client/game/README.md` — фактические Unity возможности, управление, протокол, known limitations и инструкция локальной проверки.
- `3-dev/ROADMAP.md` и `3-dev/comms/README.md` — статус, нумерация handoff и правила взаимодействия.

### GitHub discussions/PR, которые нужно прочитать
- PR #18 — вступительная миссия/письмо/первое знакомство.
- PR #25 / актуальная перебазированная запись #201 — имена NPC только после знакомства.
- PR #26 — diegetic settlement discovery.
- PR #27 — иммерсивное взаимодействие и first-person presentation.
- PR #39 — навигация/геометрия поселения.
- PR #40 — адресный talk и корреляция результата.
- PR #80 — игровая петля первого задания.
- PR #193, #194, #195 — серверное ядро, Unity CoreGameplay и мир v1.11.
- PR #196 — Blender tavern props.
- PR #197/#199 — talk ACK и вертикальный срез. #199 слит (commit b5f20e0): оба review замечания закрыты, `RequestGate` и `Server.Close` добавлены, тест из #197 перенесён в #199; #197 закрыт как superseded.
- PR #190 — отображение server-authoritative journal; перебазировать на актуальный main, не потерять полезную работу.
- PR #200 — ADR-предложение по единому открытому миру; не превращать его в большой runtime rewrite.
- PR #203 — первое поручение от знакомого жителя из его реальной нужды; слито в main (commit 4ec0b1a), wire v1.11 не менялся.
- PR #204 — исходные реестр канона и приёмка демо; актуализированы после merge в PR #206.
- PR #205 — ChatGPT: talk и все NPC say (включая work/drink) подключены к общему RequestGate; merged в main (commit 086a2e3). Unity Editor/PlayMode остаётся BLOCKED.
- PR #208 — Unity parser/DTO и вкладка server-authoritative `me.journal`; merged (commit 92081e0), Editor validation BLOCKED.
- PR #209 — thin-client card для `me.journal`, безопасный DOM `textContent` и cache across fast frames; merged (commit 368f22a), manual browser check pending. Старый PR #190 закрыт как superseded портом #208.

Если у тебя есть доступ к прошлой переписке с владельцем, найди и прочитай исходные пользовательские сообщения с формулировками лора и механик. Если к истории другого чата доступа нет, прямо отметь это и используй репозиторий как доступный архив. Не утверждай, что прочитал недоступную переписку. Не придумывай недостающие факты.

## 2. Восстановленный канон и механики — отправная точка сверки

Ниже собраны найденные в репозитории и предыдущих обсуждениях решения. Для каждого пункта проверь первоисточник и текущий код; разделяй канон, legacy-числа, proposal и implementation.

### 2.1 Игра и мир
- Проект называется «Пограничье». В ранних исходниках магия названа каноном мира **Ёф**; проверь, является ли «Ёф» окончательным именем всего мира/сеттинга или конкретной частью лора. Не переименовывай и не расширяй лор по догадке.
- Целевой жанр — постоянный живой мир / immersive RPG. Текущий запрос владельца: открытый мир с ощущением Skyrim / The Witcher; по системам и напряжению также ориентиры Outward, Kenshi и Tarkov.
- Это не меню отдельных карт и не набор изолированных мини-игр. Одна непрерывная физическая география, которую можно исследовать ногами. Регионы — части одного мира.
- Стартовая демо-территория — несколько деревень (в документах названы Починок, Берёзовка, Залесье, Крутой Яр и др.). Между ними физические дороги, мосты, броды и перевалы. Дальний горный барьер отделяет демо-территорию от внешнего большого мира; специальный gateway допустим только для перехода между демо-зоной и большим миром.
- Локальная геометрия Layout v2 размером 300×300 м относится к одному поселению, не ко всему миру. Не объявлять текущий frontier бесшовным open world.
- Unity 6, версия владельца 6000.6.7f1, URP. 1 Unity unit = 1 метр.
- Мир должен продолжать жить без запущенного Unity-клиента.

### 2.2 Три слоя — один мир и один персонаж
1. **Go-сервер** — точка входа и мозг мира: симуляция, решения, время, экономика, NPC/A-Life, сохранение, авторитетные позиции, коллизии, ресурсы, бой и последствия. Сервер не должен зависеть от толстого клиента.
2. **Тонкий клиент** (браузер/смартфон) — второй интерфейс к тому же персонажу: дом, хозяйство, ферма, ремёсла, сбор, запасы, дневник, квесты, социальные действия, мини-игры, абстрактное путешествие между городами. Это не набор пустых кнопок и не отдельный мир. Mobile UX — touch-first.
3. **Толстый Unity-клиент** — иммерсивное физическое приключение: исследование, движение, бой, стрельба, ближние схватки, скрытность, выживание, здания, улицы, леса, поля, подземелья, взаимодействия с NPC и окружением.

Один аккаунт/персонаж/серверное состояние. Нельзя допускать двух конфликтующих авторитетных позиций, дублирования транзакций или разъезда инвентарей между клиентами. Если два клиента одновременно управляют персонажем, нужна явная политика владения/передачи управления, а не скрытая гонка.

### 2.3 Живой мир, A-Life и разум NPC
- NPC — постоянные сущности со стабильными ID, идентичностью, ролями, нуждами, навыками, домом, местом, памятью, целями и последствиями.
- Сервер выбирает цели. NPC не должны быть декоративными Unity-waypoint puppets.
- Utility/priority logic в старом скелете: выживание (голод/усталость/сон), безопасность (еда на будущее, кров, доход), принадлежность (социальные контакты/таверна), признание/рост (мастерство/смена места).
- Профессия и нужды должны порождать поведение. Пахарь/рыбак/охотник добывают еду; кузнец/ткач/повар поддерживают ремесло и доход. Не прописывать всем один и тот же маршрут.
- Примеры рутин из 0028: корчмарь открывает таверну, обслуживает посетителей, убирает и закрывает; кузнец идёт к кузнице; фермер — к полю/колодцу/рынку; странник посещает таверну и ходит по улицам.
- Серверная навигация, коллизии, stuck recovery, POI и расписания. Клиент интерполирует позиции и проигрывает подходящие анимации по подтверждённым `act_code`, не по прозе/goal.
- Вдали от игроков допускается LoD/агрегированная симуляция, но нельзя замораживать мир, терять сущности или последствия. Hot/warm/cold — оптимизация, а не разные вселенные.
- Рождения, старение, смерти и смена поколений обязательны для настоящего живого мира. Не допустить «сломанный тамагочи»: вечные 100 HP, вечные idler, инфляция, вырожденные роды, схлопывание холодных деревень.
- Крупные события (миграция, война, голод/нехватка) должны вытекать из решений NPC, экономики и порогов мира, а не быть случайным скриптовым флагом.
- Распространение репутации «со скоростью гонца» через торговцев/почту было записано в legacy backlog как направление; проверить статус/приоритет, не объявлять реализованным.
- У NPC могут быть разные отношения: знакомство, дружба, доверие, репутация — не сводить их к одному булеву флагу.
- Имена незнакомых NPC скрыты. Субтитром/надписью показывать имя только после server-confirmed acquaintance; незнакомец может иметь контекстную подсказку «Поговорить [E]», но не имя. Знакомство сохраняется между сессиями.
- Поселения узнаются из разговоров, таверн, писем и квестов. Знание поселения: Rumoured → Located → Visited → Personally known. Карта раскрывает только известное; это не произвольная карта мира с раскрытыми названиями.

### 2.4 Экономика, хозяйство и ремёсла
- Закон сохранения ресурсов: богатство не появляется из ниоткуда. Угодья конечны и восстанавливаются по природным правилам; шахта истощается, лес может вырубаться и зарастать.
- Сезонные циклы: сев весной, рост летом, жатва осенью; рыбалка учитывает нерест/сезонные ограничения. Семенное зерно отделено от еды и не должно исчезать из-за случайного расхода.
- Цепочки производства и потребления: wheat→bread, ore→tools, животноводство (молоко/шерсть/яйца/перо/мясо/кожа), сыр/сукно/кожа; порча запасов; торговля между поселениями с транспортными потерями; десятина/налоги державы.
- Деньги и рынок должны быть сбалансированы против инфляции. В скелете указаны как следующие задачи индивидуальные запасы/закрома NPC, личная купля-продажа, цены, межгосударственная торговля и ресурсные войны. Сверь фактический main и roadmap.
- Ремесло — цепочки, инструменты, станции, материалы, время и качество. Кузница, верстак, очаг, травник, строительная площадка должны быть реальными контекстами, не декорацией.
- Тонкий клиент должен поддерживать развитие дома/фермы/хозяйства, сбор и запасы. Толстый — физическое взаимодействие с объектами и станциями.
- Не выдавать визуальный огород/рыбацкий настил/кузницу за работающую механику без серверного разрешения и транзакции.

### 2.5 Тело, бой, медицина и выживание
- Бой — опасный и тактический, не аркадный click-spam. В legacy backlog указаны оружие с типом/дальностью/скоростью/уроном, выносливость, стойки, атака/защита/финт/уклонение/бегство, части тела, общие и локальные последствия ран.
- Раны по частям тела, кровь/кровотечение, заражение; открытая рана без чистых трав может гноиться. Лечение занимает время и использует реальные травы/снадобья/знахаря/мага.
- Присутствующий игрок разрешает боевой ввод навыком/точностью; фоновые/удалённые исходы — детерминированные кости сервера. Клиент не решает попадание, урон, смерть, loot или расход выносливости.
- Психика/надлом в legacy — дополнительная игровая ось, влияющая на плетение и решения; не медицинская диагностика и не психиатрическая симуляция.
- Выживание должно быть следствием тела, еды, усталости, погоды/температуры, сна, ресурсов и опасностей, а не полоской ради HUD.

### 2.6 Магия мира Ёф — важный отдельный канон
- Магия — не «кнопка фаербол». Это **плетение силы**: маг удерживает в уме логический узор и проводит через него ману.
- Мана — невидимое вещество/поле в местах мира: где-то гуще (места силы), где-то почти пусто (мёртвые зоны). Маг копит ману из местного поля и тратит её на плетение.
- Стихийных школ как фиксированного каталога в текущем комментарии к `1-server/sim/weave.go` нет: действие выводится из состава узлов/грамматики плетения, а не из таблицы. Игрок, понимающий грамматику, должен иметь возможность составлять собственные узоры.
- Главный предел — сложность, которую разум способен удержать. Сильнее эффект → сложнее узор → выше надрыв/шанс срыва. Сорванное плетение опасно бьёт по самому магу (откат).
- Маги редки. Legacy `world/magic.py` задаёт врождённую предрасположенность (gift) и обучение weaving; большинство жителей дара не имеет. В старом файле встречаются числа ~7% и threshold 0.78, а также схемы spark/light/mend. Считать их историческими прототипными параметрами, а не действующими числами без проверки текущего `arcane/weave.go`, `sim/weave.go` и `0008-claude-mage-awakening-design.md`.
- В более позднем дизайне маг «не рождается профессией»: искра/дар → обучение → редкое пробуждение. Проверить точный текущий канон PR/коммита.
- Плетение может сорваться с травмой/надломом; навыки, сосредоточенность, раны головы и стресс могут влиять на исход, если это подтверждено текущей моделью.
- Не добавлять стандартные стихии, школы магии, заклинания из RPG-каталога без подтверждения владельца.

### 2.7 Путешествия и мир
- Обычное перемещение толстого клиента — физическая ходьба по географии; не `op:"travel"` и не телепортация.
- Тонкий клиент может выполнять серверную абстракцию длительного пути с временем, риском, встречами, отрядом/караваном, но персонаж остаётся тем же и в итоге приходит в соответствующую точку единого мира.
- Старый backlog: караваны, дорожные встречи, данжи как отдельные типы локаций с «погребённым наследием», дикие зоны высокой магии у границы, монстры/аномалии, биомы, материк, процедурные топонимы. Проверить, что уже есть в текущем server code, что только предложение и что владелец должен подтвердить.
- Открытый мир не равен бесконечному набору поселений: нужна связная география, осмысленные ориентиры, дороги, леса, поля, реки, горы, подземелья и плотность контента.

### 2.8 Иммерсивность и интерфейс
- Всё должно быть иммерсивно. HUD минимальный, не превращать игру в таблицу кнопок.
- Мышь всегда управляет обзором. RMB — контекстный альтернативный режим инструмента/предмета; не блокировать камеру произвольно. Отдельный gesture mode может захватывать мышь для плетения.
- Взаимодействие через прицел/контекстную подсказку и E. Не показывать ложные успехи, имена или анимации до server-confirmed факта.
- Для незнакомых NPC не показывать имя; после знакомства — имя допустимо.
- Для thin-client — понятные карточки/карта и touch-first управление, но не превращать его в «симулятор нажатия кнопок».

## 3. Что означает «рабочая демо»

Целевая первая демо-сессия: 5–10 минут, цельная история, не набор меню. Игрок появляется в живой деревне и понимает, что это населённый мир, а не техническая площадка.

### Обязательный end-to-end маршрут
1. **Запуск/вход:** чистый клон можно запустить по README. Сервер поднимается локально; Unity подключается к нему; регистрация/вход/вселение работают; reconnect не создаёт дублей и не теряет персонажа.
2. **Первый взгляд:** читаемая деревня с дорогой, домами, дворами, полями/лесом, ручьём/мостом, таверной, кузницей/рынком. Время суток, свет, небо, туман/атмосфера, звуковая среда. Объекты не должны пересекать серверные препятствия и POI.
3. **Управление:** мышь — обзор; WASD/стрелки — движение; бег/прыжок только если согласованы; без самопроизвольного движения, дрейфа legacy axes, инвертированной камеры, зеркальных/обращённых внутрь подписей, пропадающих рук/кружки, сломанных частиц. Коллизии игрока соответствуют серверной геометрии; нельзя проходить сквозь здания/реку вне моста.
4. **Живые жители:** несколько NPC с устойчивыми ID, ролями и server-owned position/activity. Игрок может наблюдать переход хотя бы одного NPC между разными действиями/POI. Движение не телепортируется и не строится клиентом. Есть ходьба/работа/отдых/социальная активность с честными анимациями.
5. **Знакомство и разговор:** подойти к конкретному NPC; контекстная подсказка «Поговорить [E]»; targeted talk адресован его ID. Клиент ждёт matching `reqid` в `me.result`; отказ/таймаут/потеря связи отображаются честно. Не показывать имя незнакомца до знакомства; после server-confirmed acquaintance — можно.
6. **Первое поручение:** NPC выдаёт реальную задачу из серверного состояния. Первый шаг должен занимать не более ~4 минут при вовлечённом игроке. Действие должно соответствовать реальной профессии/месту (например, простая работа у кузницы/в огороде/на строительной площадке/рыбалка — только если поддержана сервером). Прогресс не начисляется локально.
7. **Результат работы:** сервер подтверждает выполнение, расход ресурсов/инструмента, награду и/или навык. Повтор reqid не дублирует результат. В журнале/ленте показывается только подтверждённое событие.
8. **Корчма и пиво — обязательная демо-функция:** игрок приходит в таверну, видит наличие и цену, покупает пиво, получает кружку/предмет, употребляет его; деньги, склад, кружка/инвентарь, consumption и состояние опьянения — только сервер. При повторе reqid нет двойного списания. Нельзя считать декоративную кружку или `socialize`/ `eat` за работающий beer flow.
9. **Финальная обратная связь:** летопись/лента/журнал отображает историю действий, поручение и результат, не дублирует события из full/fast frames, переживает reconnect в пределах реально реализованной persistence. Не обещать сохранение после перезапуска, если его нет.

### Расширенный конференционный acceptance
После соло-петли — два клиента в одном поселении:
- видят друг друга с authoritative positions;
- могут пройти до таверны без прохождения сквозь стены;
- один приглашает второго выпить, второй принимает/отклоняет;
- сервер проверяет расстояние, деньги и запас отдельно для каждого;
- при согласии оба видят синхронный тост/питьё, индивидуальное состояние опьянения;
- disconnect/reconnect не создаёт ghost players и повторных транзакций.
Если co-drink ещё не готов, не имитировать его. Лучше честно показать стабильную соло-петлю и отметить multiplayer как следующий milestone.

### Демо-критерий
Независимый тестер, следуя README, может запустить сервер + Unity, пройти сценарий от входа до пива без ручной правки JSON/консоли/вызова скрытых API. Нет блокирующих ошибок сборки, нет фальшивых исходов, нет очевидных dead ends. Результат воспроизводим на фиксированном seed.

## 4. Что уже известно о текущем статусе — не путать с подтверждением в новой проверке

- В main есть серверное ядро v1.10/v1.11: плетения, боевые intents, быстрые кадры, NPC/животные/разбойники, квестовые просьбы, ремёсла, дорога/отряды, hot/warm/cold world heat, targeted talk ACK, frontier expansion.
- PR #193/#194/#195 были объединены, но Unity CoreGameplay не был проверен настоящим Unity Editor.
- PR #199 (отчёт 0061) слит: frontier-max учитывает города; путник греет только цель; HTTP+WS e2e talk→work→beer; быстрый кадр и лимиты ввода удара покрыты тестами; Unity Editor — BLOCKED; wire v1.11 не менялся.
- Review замечания #199 закрыты: `RequestGate` сериализует result-bearing действия в `CoreGameplay`, `Server.Close()` останавливает тик-петлю, а dispatcher ACK test из #197 перенесён в #199. Fable сообщил `go build ./...`, `go vet ./...`, `go test ./... -count=1` и `go test ./server/ -count=2 -race` как успешные; check runs в GitHub для того commit не публиковались.
- В ходе повторного ревью найден второй клиентский путь: Unity `VerticalSliceController` отправлял `talk/say` отдельно от `CoreGameplay`. PR #205 переводит `talk` и все NPC-реплики в тот же gate, не меняя wire. До настоящей проверки Unity Editor это остаётся source-level исправлением, не подтверждённой сборкой.
- #190 — Unity journal parser/UI; пока не считать отображение полноценной летописи завершённым.
- Blender props PR #196 уже merged; четыре ресурса (tankard/barrel/basket/bench) должны лежать в `Assets/Resources/TavernProps`. Код содержит procedural fallback. Unity Editor визуальная проверка всё ещё обязательна.
- Фактическая проверка Unity 6000.6.7f1 на момент передачи BLOCKED: в рабочей среде не было Unity Editor. Никакой `mcs`/stub compilation не считать заменой реальной Unity сборки.
- Владелец разрешил проверять и сливать полезные ветки/PR, кроме downgrade/regression. Но это не разрешение сливать слепо: сравнивай с текущим main, тестируй, не теряй более новые изменения, не допускай wire break или регрессий. Для изменений, которые не проверены или конфликтуют, делай rebase/портирование и показывай diff.

## 5. Текущие ветки: preliminary audit, не слепо вливать

Проверены страницы GitHub branches. Многие старые ветки сильно отстают от main и содержат версии файлов до текущих больших изменений. Несколько направлений уже видны в актуальном main (например, chicken coop, dirt-road ribbons, orchard/field dressing, firewood yards, river reeds, windmill, market details, tavern props). Не переносить старые ветки целиком только по названию.

Кандидаты, чьи diff/уникальность нужно дополнительно сверить:
- `chatgpt/village-domestic-life-chickens-v2` — chicken yard/coop, ambient chicken motion; сравнить с текущим `ProceduralVillageBuilder.cs` и `VillageAmbientMotion.cs` (поиск в main уже показывает chicken coop, значит часть/всё могло быть интегрировано другой веткой).
- `chatgpt/inventory-driven-character-appearance-20261106` — appearance from equipped/worn gear; проверить, что текущая `EntityViewRegistry` действительно обновляет визуал по server gear changes, а не только создаёт первоначальный outfit.
- `chatgpt/contextual-woodcutting-fishing-actions` — проверить реальную привязку POI/action и соответствие действующим серверным intents.
- `chatgpt/unity-inventory-actions-20261109` — Unity equip/wear/take action adapter; сверить с актуальным protocol v1.11.
- `chatgpt/unity-session-journal-20261109` и PR #190 — журнал/летопись.
- `chatgpt/thin-client-clickable-village-map-20261109`, `chatgpt/thin-client-village-scene-20261109` — thin-client scene/map; проверить, что не вводят fake POIs, неподдерживаемые действия и `op:"travel"` как обычную ходьбу Unity.
- `chatgpt/blacksmith-forge-embers`, `chatgpt/barn-farm-cart-20261106`, `chatgpt/cottage-chopping-stations-20261106`, `chatgpt/village-firewood-yards-20261106`, `chatgpt/yard-water-troughs-20261106`, `chatgpt/woods-camp-fishing-landing`, `chatgpt/field-props-scarecrow`, `chatgpt/east-field-windmill`, `chatgpt/garden-crop-detail-20261106`, `chatgpt/unity-modular-dirt-road-ribbons`, `chatgpt/low-poly-combined-woodland`, `chatgpt/rippled-river-and-reeds`, `chatgpt/combined-low-poly-horizon` — presentation-only art. Сравнить с current main и не переносить устаревшие полные версии файлов.
- `client/immersive-npc-nameplates`, `client/immersive-presentation-pass-01`, `client/v1-2-held-tankard`, `client/v1-3-world-actions`, `unity/npc-identity-variation`, `environment/expanded-rural-landscape`, `unity/barn-yard-open-gate` — старые client changes, требуют сверки с текущим main.
- Большие feature ветки `feature/endless-world-and-clients`, `feature/big-expansion`, `feature/crafts-and-scale`, `feature/realm-diplomacy`, `feature/latent-mages`, `feature/thin-client-living-world`, `feature/social-and-codrink`, `feature/unity-core-gameplay`, `feature/world-geometry-and-actions`, `feature/world-chronicle`, `feature/nav-talk-world-hardening` на момент сравнения уже позади main (ahead_by=0): не нужно повторно мержить; их результат в истории main. Проверь по текущим файлам, что именно осталось актуальным.

Перед любым переносом:
1. Compare branch against latest main; inspect commits and file-level diff, не только branch name.
2. Если ahead=0 — не мержить.
3. Если ветка diverged — cherry-pick/port только уникальную полезную часть, не брать старый файл целиком.
4. Unity presentation изменения требуют настоящей Editor compile/PlayMode проверки у владельца, либо честного BLOCKED.
5. Сливать только если тесты зелёные и нет downgrade/регрессии.

## 6. Обязательные технические ограничения
- Сервер — источник истины для координат, коллизий, инвентаря, ресурсов, боя, здоровья, знакомых NPC, квестов, ремесла, денег, опьянения и всех результатов.
- Не менять wire v1.11, snapshot schema, fast-frame schema, GeometryVersion, `/api/layout`, intent/op names, persistence format без отдельного design proposal/ADR и approval владельца.
- Сохранять backward compatibility с текущими клиентами и fixtures.
- Не использовать `op:"travel"` для обычной физической ходьбы Unity.
- Не вводить локальные fake success/loot/quest progress/NPC motion.
- Не объявлять persistence между перезапусками, если не доказана запись/чтение с диска.
- Не объявлять Unity build passed без настоящего Unity Editor 6000.6.7f1.
- Не выполнять массовый рефакторинг CoreGameplay ради красоты; выделять компоненты только когда это уменьшает риск и помогает демо.
- Не строить большой открытый мир одним гигантским PR. Сначала сохранение и единая география, потом roads/chunks/streaming по этапам.
- Все изменения: ветка → маленький PR → tests → review → merge. Владелец разрешил слияние безопасных улучшений, но downgrade/regression запрещены.

## 7. План доведения до демо — приоритеты и критерии

### P0 — стабилизировать текущую петлю
- **Серверная часть #199 закрыта и слита:** single-slot `me.result` / RequestGate для CoreGameplay; управляемый shutdown tick loop; dispatcher ACK regression из #197 включён.
- **Первое поручение #203 слито:** реальная нужда NPC → просьба → работа → сдача; wire v1.11 не менялся.
- **Клиентский общий gate:** PR #205 слит: talk/say идут через тот же RequestGate, что buy/consume и прочие действия. Осталась проверка в Unity Editor.
- Go: повторить `gofmt`, `go test ./...`, `go test -race ./...` где разумно, `go vet ./...` на актуальном main после #203.
- Unity: проверить компиляцию настоящим Editor; parse fixtures v1.0/v1.1/v1.11; no null/optional-field crashes; no stuck pending request; no spontaneous movement; reconnect/session lifecycle.
- Закрыть blocking bugs по фактическому логу редактора: input, camera, collision, labels, tankard, particles, UI navigation.
**Готово, когда:** clean clone server + Unity Editor build проходят; basic login→move→targeted talk works; no obvious regression.

### P1 — первая полная соло-демо петля
- Spawn/arrival in starter village.
- Tutorial controls and world orientation, minimal diegetic prompts.
- Targeted acquaintance/talk with one named NPC after confirmation.
- Server-generated first task (до 4 минут), clear objective and contextual station.
- Real server progress, completion, reward/skill.
- Tavern visit; purchase beer; receive held tankard; consume; server confirms money/stock/inventory/intoxication.
- Chronicle/feed records results without duplicates; reconnect preserves current task/result.
**Готово, когда:** независимый тестер проходит всё за 5–10 минут без консоли/ручных API; серверный state proves each step.

### P2 — живость деревни
- Minimum small cast: innkeeper, smith, farmer, wanderer, plus ambient animals if stable.
- Server-owned schedules/waypoints and movement; no decorative circle positions.
- Activity codes mapped to actual locomotion/work/sit/drink animations; do not infer from goal prose.
- Collision/nav consistency around houses, tavern, bridge, river and fences.
- Ambient art/audio: hearth, chimney smoke, chickens, carts, firewood, crops, orchard, water, trees, wind, lighting, time-of-day.
- Avoid fake busy loops; NPC station/POI must be reachable server-side.
**Готово, когда:** NPC visibly change activity/position over in-game time and survive reconnect/empty-player state; client renders but does not decide.

### P3 — один дополнительный настоящий gameplay loop
Выбери только один, исходя из готовых server contracts:
- simple craft recipe at forge/workbench, OR
- fishing with validated place/gear/timing, OR
- harvesting/planting with season/tool/inventory validation, OR
- controlled archery/melee training.
Не добавляй все сразу. Сделай один законченный цикл с расходом/наградой/ошибками/журналом.
**Готово, когда:** полный server-confirmed loop, retry-safe, tested, readable UI.

### P4 — multiplayer conference demo
- two clients see each other and walk to tavern;
- invite/accept/decline shared drink, server-owned session phases and separate payment;
- reconnect, duplicate requests, disconnect/cancel handling;
- synchronized visual toast/drink.
**Готово, когда:** два независимых клиента проходят сценарий повторяемо без ghost players/duplicate debits.

### P5 — foundation for the open world
- ADR 0004 from PR #200: global coordinate system, World→Region→Chunk→Settlement→POI, persistence, LoD, Unity streaming, thin-client travel semantics.
- Preserve continuous geography: physical routes between starter villages; mountain gateway only at demo boundary.
- Do not claim seamless world until Unity physically streams and walks across boundaries without teleport.
**Готово, когда:** approved architecture, one small test region boundary prototype, no breaking contract.

## 8. Definition of Done for every feature
A feature is done only if:
- authoritative server implementation exists;
- relevant server/client tests exist;
- client UI communicates pending/success/failure correctly;
- no duplicate action on retry/reconnect;
- optional fields and old fixtures remain compatible;
- logs/errors are actionable;
- README and status docs match reality;
- Unity Editor validation is recorded honestly (or BLOCKED);
- PR diff is reviewed against latest main and does not regress another layer.

## 9. Deliverables from this assignment
1. `3-dev/docs/КАНОН-ЛОР-И-МЕХАНИКИ.md`: index of all lore/mechanics sources, confirmed owner canon, unresolved ambiguities, legacy values, and current implementation status. Keep provenance per entry (file/chat/PR/commit).
2. `3-dev/docs/DEMO-ACCEPTANCE.md`: executable demo scenario, setup instructions, test matrix, exact pass/fail criteria and current blockers.
3. A prioritized implementation plan with small PR-sized steps and dependencies.
4. First PR after the audit: P0 blockers only; do not mix architecture, new lore, art expansion and new gameplay in one PR.
5. A short report to owner: what sources were read, what old-chat context was accessible, which lore is confirmed vs uncertain, what features are already in main, branch candidates safely ported/closed, and the exact next Unity Editor checks needed.

## 10. Communication
- Continue using `3-dev/comms/NNNN-...` with one clear task/report per file.
- Read entire relevant PR conversations, not only descriptions. Distinguish owner statements from assistant suggestions and implementer claims.
- Do not silently invent lore, factions, countries, history, religion, spell schools, character names or plot. If a key detail is absent, list it as an open question.
- Do not call a feature complete unless it is runnable and server-backed.
- No vague progress reports. Include commit SHA, PR link, changed files, test commands/results, blockers and what remains.
- Keep the demo playable at every stage. Do not postpone the first playable loop until the entire open world exists.

— End of 0063


## 11. Режим исполнения: больше реализации, меньше повторных аудитов

Владелец отдельно попросил не превращать следующие итерации в бесконечное чтение, ревью и чек-листы. После закрытия P0 работай в **implementation-first** режиме:

- Не повторяй аудит уже проверенных систем без конкретного нового дефекта или новых данных.
- На каждый рабочий цикл планируй минимум один реальный implementation PR: серверная механика, клиентская интеграция, thin-client UI/gameplay или законченный графический ассет/сцена.
- Ориентир по усилиям: не менее 2/3 — код, тесты, интеграция и поставка ассетов; не более 1/3 — аудит, документация и планирование.
- Если Unity Editor недоступен, это не повод останавливаться: делай серверные и thin-client фичи, автоматические проверки, подготовку ассетов и source-level исправления. Явно помечай только Editor/PlayMode как BLOCKED.
- Не открывай PR, состоящий только из списка будущих проверок, если можешь закрыть конкретную часть функционала сейчас.
- Каждый implementation PR должен содержать: изменённые файлы, работающий путь пользователя, серверные/клиентские тесты, точные команды и честный статус Unity.
- Приоритет ближайшей работы после #205: #208/#209 уже вывели `me.journal` на экраны обоих клиентов на уровне исходников; осталось выполнить Editor/browser приёмку. Следующая реализация для Fable — убрать преждевременные локальные «рыба/дрова в закромах» в тонком клиенте: pending + matching reqid + success только после server ACK; затем довести одну дополнительную реальную gameplay петлю. Не ограничиваться аудитом/планом.
