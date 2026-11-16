# Задача 024 — Серверная симуляция стамины и authoritative movement profile

См. полный handoff: `3-dev/comms/0271-chatgpt-to-claude-server-stamina-and-movement-profile.md`.

Приоритет:
1. Вынести пассивную регенерацию stamina из зависимости от входящего movement-intent в server simulation tick.
2. Проверить отсутствие двойного расхода/восстановления и добавить unit tests.
3. Аудировать сериализацию движения: `ScenePosC.Mult` серверный (`json:"-"`), а Unity hardcode-ит walk/jog/run speed. Предложить additive optional movement profile в frame v:1, обновить fixture/docs/tests.
4. Составить таблицу authoritative source/formula/frame field для HP/max HP, stamina/max, energy, hunger/thirst, movement speed, encumbrance/mount modifiers, injuries/effects.

Не трогать клиентскую раскладку: она уже реализована в текущей ветке ChatGPT. Не менять frame contract breaking-образом, не создавать вторые компоненты для тех же характеристик, не переносить authority в Unity. Unity prediction остаётся presentation-only и корректируется сервером.
