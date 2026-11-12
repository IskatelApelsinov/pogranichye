using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private InputAction _toggleHud, _toggleDiagnostics, _toggleFullscreen, _menu, _cancel;
        private InputAction _weaveHold, _weaveRemove, _primary, _secondary;
        private InputAction _toggleHints, _dodge, _quickAttack, _trickSand, _trickShove;
        private InputAction _trickFeint, _trickBait, _trickSneak, _cycleStance;
        private readonly InputAction[] _weaveNodes = new InputAction[6];
        private readonly InputAction[] _journeyChoices = new InputAction[9];
        private readonly InputEventDebugState _inputDebugState = new InputEventDebugState();

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
        public bool MenuPressed => _menu.WasPressedThisFrame();
        public bool CancelPressed => _cancel.WasPressedThisFrame();
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
            _menu = Button("Menu", "<Keyboard>/tab");
            _cancel = Button("Cancel", "<Keyboard>/escape");
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
            _cycleStance = Button("CycleStance", "<Keyboard>/t");

            for (var i = 0; i < _weaveNodes.Length; i++)
                _weaveNodes[i] = Button("WeaveNode" + (i + 1), "<Keyboard>/" + (i + 1));
            for (var i = 0; i < _journeyChoices.Length; i++)
                _journeyChoices[i] = Button("JourneyChoice" + (i + 1), "<Keyboard>/" + (i + 1));

            _actions.actionTriggered += OnActionTriggered;
            _actions.Enable();
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            var action = context.action;
            var control = context.control;
            _inputDebugState.Record(
                action != null ? action.name : null,
                control != null ? control.displayName : null,
                context.phase.ToString(),
                Time.unscaledTime);
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
                if (keyboard.escapeKey.isPressed) keys.Add("Esc");
                if (keyboard.rKey.isPressed) keys.Add("R");
                if (keyboard.backspaceKey.isPressed) keys.Add("Backspace");
                if (keyboard.qKey.isPressed) keys.Add("Q");
                if (keyboard.fKey.isPressed) keys.Add("F");
                if (keyboard.tKey.isPressed) keys.Add("T");
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
            if (_actions != null)
            {
                _actions.actionTriggered -= OnActionTriggered;
                _actions.Dispose();
            }
        }
    }
}
