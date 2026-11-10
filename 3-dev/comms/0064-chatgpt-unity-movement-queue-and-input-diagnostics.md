# 0064 — ChatGPT: stop stale Unity movement packets and expose input history

Дата: 2026-11-10. От: ChatGPT. Кому: владелец проекта.
Основание: актуальный main после PR #228; повторяющееся замечание о движении назад без нажатий и просьба видеть реальные нажатия клавиш/мыши.

## Что проверено

- Main на старте: 3ff4eba3b49820c780c27cc38c9d6779c0ed6e7c (PR #228, тест retry-idempotency рубки/рыбалки).
- Последние 35 коммитов: #205–#228; открытый PR #200 — только ADR 0004, design-only, без runtime.
- Issue #39: Layout v2/navigation proposal всё ещё не закрыт; #40: серверная адресация talk/ACK уже реализована в main, issue обсуждение остаётся открытым; #80: #203 даёт серверную просьбу из реальной нужды, Unity-цикл всё ещё BLOCKED до Editor/PlayMode.
- Последний полный отчёт Claude — comms/0061; мастер-контекст и implementation-first режим — comms/0063.
- В Unity-коде движение уже читает только дискретные клавиши (raw axes диагностические), но очередь исходящих move-intent продолжала копить старые направления, пока один WebSocket send был в полёте. При отпускании клавиши старые пакеты могли отправляться позже и выглядеть как самопроизвольное движение.

## Изменено в этой ветке

- MovementIntentQueuePolicy.cs: общий чистый C# helper для ограничения очереди и удаления unsent prediction intents.
- VerticalSliceController.cs: максимум 8 ожидающих move-intent; при нулевом вводе старые move-intent удаляются, но последний zero-vector stop intent сохраняется за единственным in-flight пакетом; F3 теперь показывает down/up событий клавиатуры/мыши, mouse delta/scroll, позицию/захват курсора, pending/queue и число отброшенных intents.
- PresentationCameraRig.cs: mouse look и scroll не читают ввод, когда окно потеряло фокус.
- CoreGameplay.cs: Alt-Tab отпускает guard, сбрасывает удерживаемую атаку/плетение и освобождает курсор; захват курсора разрешён только при фокусе окна.
- WorldInteractionProbe.cs: E не запускает взаимодействие вне фокуса приложения.
- MovementIntentQueuePolicyCheck.cs: standalone regression checks на bounded backlog, discard-on-stop и сохранение in-flight intent.
- .github/workflows/unity-independent-csharp-checks.yml: отдельный Ubuntu/Mono job для RequestGateCheck и MovementIntentQueuePolicyCheck.
- 2-client/game/README.md: описаны новые правила и ручная проверка Alt-Tab/F3.

## Контракт и риски

- Wire v1.11, op:move, Layout v2 и серверная геометрия не менялись.
- Сервер sim.ApplyMove принимает только seq > p.Seq, а не требует соседних seq; пропуски от отброшенных неотправленных intents допустимы.
- Уже отправленный in-flight пакет отменить нельзя: он может примениться один раз (его dt ограничен сервером 250 мс). После него клиент отправит актуальный нулевой вектор. При штатной задержке это ограничивает хвост движения одним пакетом вместо всей очереди.
- GitHub Actions run 42881261872 завершился success: RequestGate regression checks и Movement queue regression checks оба прошли на Ubuntu/Mono.
- Локально в этой среде нет mcs/mono, но standalone suites выполнены в CI.
- Unity Editor/PlayMode 6000.6.7f1 не запускался; зелёный pure-C# job не является Unity build.

## Проверки и следующая задача

- Source-level assertions на точки интеграции прошли; pure-C# CI прошёл; фактическая компиляция Unity не заявляется.
- PR #230 (draft): https://github.com/IskatelApelsinov/pogranichye/pull/230. Ветка chatgpt/unity-stop-stale-move-20261110-1940, HEAD 3297b619767d0d0cbcdb266d75d8adbf63af6582; main остаётся на 3ff4eba3b49820c780c27cc38c9d6779c0ed6e7c, branch ahead 4 / behind 0.
- Workflow run: https://github.com/IskatelApelsinov/pogranichye/actions/runs/42881261872.
- Следующий конкретный шаг: в Unity повторить ходьбу назад → отпустить клавишу при искусственной задержке сети → убедиться, что после одного in-flight шага персонаж останавливается; отдельно проверить Alt-Tab при зажатом ПКМ.
- Если после этого персонаж продолжает двигаться, сравнить F3 sequence/queue с me.pos.seq и проверить, не приходит ли повторное движение из другого источника.

Merge не выполнялся.
