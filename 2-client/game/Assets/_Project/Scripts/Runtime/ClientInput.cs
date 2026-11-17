using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Pogranichye.Client
{
    /// <summary>
    /// Central Input System action map for the thick client. Gameplay code consumes semantic
    /// actions instead of polling UnityEngine.Input or depending on legacy Input Manager axes.
    /// </summary>
    public sealed class ClientInput : MonoBehaviour
    {
        // High-polling mice can enqueue more raw events than the Input System's small default
        // per-update budget. When that budget is exceeded Unity discards the remaining events,
        // which is perceived as missed clicks/keys. Keep the limit configurable and raise it
        // once, before enabling the action map; this is a safety margin, not a substitute for
        // fixing main-thread stalls.
        [SerializeField, Min(65536)] private int maxInputEventBytes = 1048576;

        private InputActionMap _actions;
        private InputAction _move, _look, _zoom, _jump, _sprint, _jog, _interact;
        private InputAction _toggleHud, _toggleDiagnostics, _toggleFullscreen, _menu, _cancel, _toggleAutoWalk;
        private InputAction _openInventory, _openJournal, _openMap;
        private InputAction _weaveHold, _weaveRemove, _primary, _secondary;
        private InputAction _toggleHints, _dodge, _quickAttack, _trickSand, _trickShove;
        private InputAction _chat, _voicePushToTalk;
        private InputAction _trickFeint, _trickBait, _trickSneak, _cycleStance;
        private InputAction _startWoodcut, _startFishing, _lockTarget;
        private readonly InputAction[] _weaveNodes = new InputAction[10];
        private readonly InputAction[] _journeyChoices = new InputAction[9];
        private readonly InputEventDebugState _inputDebugState = new InputEventDebugState();
        private readonly InputDeviceEventDebugState _deviceEventDebugState = new InputDeviceEventDebugState();
        private bool _inputDiagnosticsEnabled;
        private bool _inputEventListenerRegistered;
        private InputActionRebindingExtensions.RebindingOperation _rebindOperation;
        private const string BindingOverridesPrefsKey = "Pogranichye.InputBindingOverrides";

        public Vector2 Move => _move.ReadValue<Vector2>();
        public Vector2 LookDelta => _look.ReadValue<Vector2>();
        public float Zoom => _zoom.ReadValue<float>();
        public Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public bool JumpPressed => _jump.WasPressedThisFrame();
        public bool SprintHeld => _sprint.IsPressed();
        public bool JogHeld => _jog.IsPressed();
        public bool InteractPressed => _interact.WasPressedThisFrame();
        public bool ToggleHudPressed => _toggleHud.WasPressedThisFrame();
        public bool ToggleDiagnosticsPressed => _toggleDiagnostics.WasPressedThisFrame();
        public bool ToggleFullscreenPressed => _toggleFullscreen.WasPressedThisFrame();
        public bool AutoWalkTogglePressed => _toggleAutoWalk != null && _toggleAutoWalk.WasPressedThisFrame();
        public bool MenuPressed => _menu.WasPressedThisFrame();
        public bool InventoryMenuPressed => _openInventory.WasPressedThisFrame();
        public bool JournalMenuPressed => _openJournal.WasPressedThisFrame();
        public bool MapMenuPressed => _openMap.WasPressedThisFrame();
        public bool CancelPressed => _cancel.WasPressedThisFrame();
        public bool ChatPressed => _chat.WasPressedThisFrame();
        public bool VoicePushToTalkHeld => _voicePushToTalk != null && _voicePushToTalk.IsPressed();
        public bool WeavePressed => _weaveHold.WasPressedThisFrame();
        public bool WeaveReleased => _weaveHold.WasReleasedThisFrame();
        public bool WeaveHeld => _weaveHold.IsPressed();
        public bool WeaveRemovePressed => _weaveRemove.WasPressedThisFrame();
        public bool PrimaryPressed => _primary.WasPressedThisFrame();
        public bool PrimaryReleased => _primary.WasReleasedThisFrame();
        public bool SecondaryPressed => _secondary.WasPressedThisFrame();
        public bool SecondaryReleased => _secondary.WasReleasedThisFrame();
        public bool ToggleHintsPressed => _toggleHints.WasPressedThisFrame();
        public bool DodgePressed => _dodge.WasPressedThisFrame();
        public bool QuickAttackPressed => _quickAttack.WasPressedThisFrame();
        public bool TrickSandPressed => _trickSand.WasPressedThisFrame();
        public bool TrickShovePressed => _trickShove.WasPressedThisFrame();
        public bool TrickFeintPressed => _trickFeint.WasPressedThisFrame();
        public bool TrickBaitPressed => _trickBait.WasPressedThisFrame();
        public bool TrickSneakPressed => _trickSneak.WasPressedThisFrame();
        public bool CycleStancePressed => _cycleStance.WasPressedThisFrame();
        public bool StartWoodcutPressed => _startWoodcut.WasPressedThisFrame();
        public bool StartFishingPressed => _startFishing.WasPressedThisFrame();
        public bool LockTargetPressed => _lockTarget.WasPressedThisFrame();

        private void Awake()
        {
            // InputSettings is shared by the Input System. Never lower a project's existing
            // setting; only raise the budget enough to absorb high-frequency device bursts.
            if (InputSystem.settings != null && InputSystem.settings.maxEventBytesPerUpdate < maxInputEventBytes)
                InputSystem.settings.maxEventBytesPerUpdate = maxInputEventBytes;

            _actions = new InputActionMap("PogranichyeGameplay");
            _move = _actions.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _look = _actions.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            _zoom = _actions.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            _jump = Button("Jump", "<Keyboard>/space");
            _sprint = Button("Sprint", "<Keyboard>/leftShift");
            _sprint.AddBinding("<Keyboard>/rightShift");
            _jog = Button("Jog", "<Keyboard>/leftAlt");
            _jog.AddBinding("<Keyboard>/rightAlt");
            _interact = Button("Interact", "<Keyboard>/e");
            _toggleHud = Button("ToggleHud", "<Keyboard>/f1");
            _toggleDiagnostics = Button("ToggleDiagnostics", "<Keyboard>/f3");
            _toggleFullscreen = Button("ToggleFullscreen", "<Keyboard>/f11");
            // '=' is intentionally a dedicated travel toggle; it does not alter mouse-look or combat bindings.
            _toggleAutoWalk = Button("ToggleAutoWalk", "<Keyboard>/equals");
            _menu = Button("Menu", "<Keyboard>/tab");
            _openInventory = Button("OpenInventory", CharacterMenuHotkeyPolicy.InventoryBinding);
            _openJournal = Button("OpenJournal", CharacterMenuHotkeyPolicy.JournalBinding);
            _openMap = Button("OpenMap", CharacterMenuHotkeyPolicy.MapBinding);
            _cancel = Button("Cancel", "<Keyboard>/escape");
            _chat = Button("Chat", "<Keyboard>/t");
            _voicePushToTalk = Button("VoicePushToTalk", "<Keyboard>/p");
            _weaveHold = Button("WeaveHold", "<Keyboard>/r");
            _weaveRemove = Button("WeaveRemove", "<Keyboard>/backspace");
            _primary = Button("Primary", "<Mouse>/leftButton");
            _secondary = Button("Secondary", "<Mouse>/rightButton");
            _toggleHints = Button("ToggleHints", "<Keyboard>/h");
            _dodge = Button("Dodge", "<Keyboard>/q");
            _quickAttack = Button("QuickAttack", "<Keyboard>/f");
            _trickSand = Button("TrickSand", "<Keyboard>/z");
            _trickShove = Button("TrickShove", "<Keyboard>/x");
            _trickFeint = Button("TrickFeint", "<Keyboard>/v");
            _trickBait = Button("TrickBait", "<Keyboard>/b");
            _trickSneak = Button("TrickSneak", "<Keyboard>/c");
            _cycleStance = Button("CycleStance", "<Keyboard>/n");
            _startWoodcut = Button("StartWoodcut", "<Keyboard>/g");
            _startFishing = Button("StartFishing", CharacterMenuHotkeyPolicy.FishingBinding);
            _lockTarget = _actions.AddAction("LockTarget", InputActionType.Button);
            _lockTarget.AddBinding("<Keyboard>/capsLock");
            _lockTarget.AddBinding("<Mouse>/middleButton");

            for (var i = 0; i < _weaveNodes.Length; i++)
            {
                // 1–9, then 0 for the tenth server node. Mouse tracing remains available too.
                var key = i == 9 ? "0" : (i + 1).ToString();
                _weaveNodes[i] = Button("WeaveNode" + (i + 1), "<Keyboard>/" + key);
            }
            for (var i = 0; i < _journeyChoices.Length; i++)
                _journeyChoices[i] = Button("JourneyChoice" + (i + 1), "<Keyboard>/" + (i + 1));

            _actions.actionTriggered += OnActionTriggered;
            var savedOverrides = PlayerPrefs.GetString(BindingOverridesPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(savedOverrides))
            {
                try { _actions.LoadBindingOverridesFromJson(savedOverrides); }
                catch (Exception exception)
                {
                    Debug.LogWarning("Не удалось загрузить переназначение клавиш; используются стандартные: " + exception.Message);
                    PlayerPrefs.DeleteKey(BindingOverridesPrefsKey);
                }
            }
            _actions.Enable();
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            var action = context.action;
            var control = context.control;
            _inputDebugState.Record(
                action != null ? action.name : null,
                control != null ? control.displayName : null,
                PhaseName(context.phase),
                Time.unscaledTime);
        }

        private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (!_inputDiagnosticsEnabled || device == null) return;
            _deviceEventDebugState.Record(
                device.deviceId,
                device.displayName,
                eventPtr.sizeInBytes,
                Time.frameCount);
        }

        public void SetDiagnosticsEnabled(bool enabled)
        {
            if (_inputDiagnosticsEnabled == enabled) return;
            _inputDiagnosticsEnabled = enabled;
            _deviceEventDebugState.Reset();
            if (enabled && !_inputEventListenerRegistered)
            {
                InputSystem.onEvent += OnInputEvent;
                _inputEventListenerRegistered = true;
            }
            else if (!enabled && _inputEventListenerRegistered)
            {
                InputSystem.onEvent -= OnInputEvent;
                _inputEventListenerRegistered = false;
            }
        }

        public string BuildDeviceEventDebug()
        {
            var budget = InputSystem.settings != null
                ? InputSystem.settings.maxEventBytesPerUpdate : 0;
            return _deviceEventDebugState.Describe(Time.frameCount, budget);
        }

        private static string PhaseName(InputActionPhase phase)
        {
            switch (phase)
            {
                case InputActionPhase.Started: return "Started";
                case InputActionPhase.Performed: return "Performed";
                case InputActionPhase.Canceled: return "Canceled";
                case InputActionPhase.Waiting: return "Waiting";
                default: return "Disabled";
            }
        }

        private InputAction Button(string name, string path)
        {
            var action = _actions.AddAction(name, InputActionType.Button);
            action.AddBinding(path);
            return action;
        }

        public bool WeaveNodePressed(int index)
        {
            return index >= 0 && index < _weaveNodes.Length && _weaveNodes[index].WasPressedThisFrame();
        }

        public bool JourneyChoicePressed(int index)
        {
            return index >= 0 && index < _journeyChoices.Length && _journeyChoices[index].WasPressedThisFrame();
        }

        /// <summary>Returns the number of bindings for a semantic gameplay action.</summary>
        public int GetBindingCount(string actionName)
        {
            var action = _actions != null ? _actions.FindAction(actionName, false) : null;
            return action != null ? action.bindings.Count : 0;
        }

        public string GetBindingDisplayString(string actionName, int bindingIndex = 0)
        {
            var action = _actions != null ? _actions.FindAction(actionName, false) : null;
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return "—";
            return action.GetBindingDisplayString(bindingIndex);
        }

        /// <summary>
        /// Starts an interactive rebind for one exact binding. Overrides are stored locally and
        /// survive relaunches; gameplay/network code continues consuming semantic actions.
        /// </summary>
        public bool StartRebind(string actionName, int bindingIndex, Action<string> completed, Action cancelled)
        {
            var action = _actions != null ? _actions.FindAction(actionName, false) : null;
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return false;

            _rebindOperation?.Cancel();
            _rebindOperation?.Dispose();
            _rebindOperation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(operation =>
                {
                    operation.Dispose();
                    _rebindOperation = null;
                    SaveBindingOverrides();
                    completed?.Invoke(GetBindingDisplayString(actionName, bindingIndex));
                })
                .OnCancel(operation =>
                {
                    operation.Dispose();
                    _rebindOperation = null;
                    cancelled?.Invoke();
                });
            _rebindOperation.Start();
            return true;
        }

        public string GetBindingConflict(string actionName, int bindingIndex)
        {
            var action = _actions != null ? _actions.FindAction(actionName, false) : null;
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return null;

            var path = action.bindings[bindingIndex].effectivePath;
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var other in _actions.actions)
            {
                if (other == action) continue; // Same action may intentionally have alternate bindings.
                for (var i = 0; i < other.bindings.Count; i++)
                {
                    var otherPath = other.bindings[i].effectivePath;
                    if (!string.IsNullOrEmpty(otherPath) &&
                        string.Equals(path, otherPath, StringComparison.OrdinalIgnoreCase))
                        return other.name;
                }
            }
            return null;
        }

        /// <summary>Stops an in-progress interactive rebind when leaving the controls page.</summary>
        public void CancelRebind()
        {
            // Cancel may synchronously invoke OnCancel and clear _rebindOperation.
            _rebindOperation?.Cancel();
            _rebindOperation?.Dispose();
            _rebindOperation = null;
        }

        public void ResetBindingOverrides()
        {
            _rebindOperation?.Cancel();
            _rebindOperation?.Dispose();
            _rebindOperation = null;
            if (_actions == null) return;
            foreach (var action in _actions.actions)
                action.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(BindingOverridesPrefsKey);
            PlayerPrefs.Save();
        }

        private void SaveBindingOverrides()
        {
            if (_actions == null) return;
            PlayerPrefs.SetString(BindingOverridesPrefsKey, _actions.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public string BuildPressedKeysDebug()
        {
            var keys = new List<string>();
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // Report physical keys, not the generic "WASD" label. This makes it possible
                // to distinguish a genuinely held S/Down key from a bad movement transform.
                if (keyboard.wKey.isPressed) keys.Add("W");
                if (keyboard.aKey.isPressed) keys.Add("A");
                if (keyboard.sKey.isPressed) keys.Add("S");
                if (keyboard.dKey.isPressed) keys.Add("D");
                if (keyboard.upArrowKey.isPressed) keys.Add("↑");
                if (keyboard.leftArrowKey.isPressed) keys.Add("←");
                if (keyboard.downArrowKey.isPressed) keys.Add("↓");
                if (keyboard.rightArrowKey.isPressed) keys.Add("→");
                if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) keys.Add("Shift");
                if (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed) keys.Add("Alt");
                if (keyboard.spaceKey.isPressed) keys.Add("Space");
                if (keyboard.eKey.isPressed) keys.Add("E");
                if (keyboard.tabKey.isPressed) keys.Add("Tab");
                if (keyboard.iKey.isPressed) keys.Add("I (инвентарь)");
                if (keyboard.jKey.isPressed) keys.Add("J (задания)");
                if (keyboard.mKey.isPressed) keys.Add("M (карта)");
                if (keyboard.kKey.isPressed) keys.Add("K (рыбалка)");
                if (keyboard.escapeKey.isPressed) keys.Add("Esc");
                if (keyboard.equalsKey.isPressed) keys.Add("= (автоходьба)");
                if (keyboard.rKey.isPressed) keys.Add("R");
                if (keyboard.backspaceKey.isPressed) keys.Add("Backspace");
                if (keyboard.qKey.isPressed) keys.Add("Q");
                if (keyboard.fKey.isPressed) keys.Add("F");
                if (keyboard.gKey.isPressed) keys.Add("G");
                if (keyboard.capsLockKey.isPressed) keys.Add("Caps");
                if (keyboard.tKey.isPressed) keys.Add("T");
                if (keyboard.pKey.isPressed) keys.Add("P (голос)");
                if (keyboard.nKey.isPressed) keys.Add("N");
                if (keyboard.hKey.isPressed) keys.Add("H");
                if (keyboard.zKey.isPressed) keys.Add("Z");
                if (keyboard.xKey.isPressed) keys.Add("X");
                if (keyboard.vKey.isPressed) keys.Add("V");
                if (keyboard.bKey.isPressed) keys.Add("B");
                if (keyboard.cKey.isPressed) keys.Add("C");
                for (var i = 0; i < _weaveNodes.Length; i++)
                    if (_weaveNodes[i].IsPressed()) keys.Add((i + 1).ToString());
            }
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.isPressed) keys.Add("ЛКМ");
                if (Mouse.current.rightButton.isPressed) keys.Add("ПКМ");
                if (Mouse.current.middleButton.isPressed) keys.Add("СКМ");
            }
            var held = keys.Count == 0 ? "нет" : string.Join(", ", keys);
            return held + " · " + _inputDebugState.Describe(Time.unscaledTime);
        }

        private void OnDestroy()
        {
            if (_inputEventListenerRegistered)
            {
                InputSystem.onEvent -= OnInputEvent;
                _inputEventListenerRegistered = false;
            }
            _rebindOperation?.Cancel();
            _rebindOperation?.Dispose();
            _rebindOperation = null;
            if (_actions != null)
            {
                _actions.actionTriggered -= OnActionTriggered;
                _actions.Dispose();
            }
        }
    }
}
