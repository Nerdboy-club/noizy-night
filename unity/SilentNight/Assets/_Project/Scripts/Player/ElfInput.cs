using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace SilentNight.Player
{
    /// <summary>
    /// Player input built entirely in code (no .inputactions asset).
    /// Values are sampled once per frame in Update (runs early via DefaultExecutionOrder) so every
    /// other player component reads consistent values. Disabling the component disables all actions.
    ///
    /// Local co-op device filter (all actions live in one InputActionMap, filtered via InputActionMap.devices):
    /// - Player 1 (PlayerIndex 0): keyboard + mouse + every gamepad NOT claimed by another player. While nobody
    ///   else claims a gamepad the map is left unrestricted (devices == null), i.e. exactly the old single-player behaviour.
    /// - Player 2+ (PlayerIndex 1..3): only its own gamepad (AssignedGamepad). Without one its actions stay disabled.
    /// The static test hooks DebugMoveOverride / DebugJumpOverride apply to player 1 only.
    /// TODO (later): keyboard-split / second keyboard player, rebinding UI, per-player RagdollDebug action
    /// (ElfRagdoll still reads Keyboard.current), InputUser pairing if we ever need per-user control schemes.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class ElfInput : MonoBehaviour
    {
        [Tooltip("Right-stick look is converted to mouse-pixel equivalents per second so the camera can use one sensitivity.")]
        [SerializeField] private float stickLookPixelsPerSecond = 1200f;
        [Tooltip("Player 1 only: add LocalCoopJoin at runtime if the object has none (scenes baked before co-op).")]
        [SerializeField] private bool autoAddCoopJoin = true;

        private InputActionMap _map;
        private InputAction _move, _lookMouse, _lookStick, _jump, _sprint, _crouch, _interact,
                            _sack, _jetpack, _cameraToggle, _pause, _zoom, _catnip;
        private InputAction[] _claimScan; // actions whose active control tells player 1 which device it is using
        private bool _created;
        private bool _filterApplied;
        private int _suppressPressesUntilFrame = -1;

        private static readonly List<ElfInput> s_All = new List<ElfInput>(4);

        // ---------- co-op identity ----------
        /// <summary>0 = player 1 (keyboard/mouse + free gamepads), 1..3 = extra local players (own gamepad only).</summary>
        public int PlayerIndex { get; private set; }
        /// <summary>Player 2+: the gamepad this elf listens to. Player 1: always null.</summary>
        public Gamepad AssignedGamepad { get; private set; }
        /// <summary>Player 1: the last unclaimed gamepad that actually drove this elf (null = none yet).</summary>
        public Gamepad LastUsedGamepad { get; private set; }
        /// <summary>Player 1: true once keyboard or mouse input reached this elf.</summary>
        public bool HasUsedKeyboardMouse { get; private set; }
        /// <summary>Every live ElfInput (all local players), in creation order.</summary>
        public static IReadOnlyList<ElfInput> All => s_All;

        // ---------- continuous values ----------
        /// <summary>WASD / left stick, magnitude 0..1.</summary>
        public Vector2 Move { get; private set; }
        /// <summary>Test/automation hook: when set, replaces the WASD/stick value (player 1 only).</summary>
        public static Vector2? DebugMoveOverride;
        /// <summary>Test/automation hook (one-shot, player 1 only): when set true, JumpPressed reads true for the next
        /// sampled frame and the flag resets itself.</summary>
        public static bool DebugJumpOverride;
        /// <summary>Combined look this frame in mouse-pixel units (mouse delta + scaled right stick).</summary>
        public Vector2 Look { get; private set; }
        /// <summary>Raw mouse delta this frame (pixels).</summary>
        public Vector2 LookMouse { get; private set; }
        /// <summary>Raw right stick (-1..1).</summary>
        public Vector2 LookStick { get; private set; }
        /// <summary>Mouse scroll Y this frame (raw; sign is what matters, magnitude is platform dependent).</summary>
        public float Zoom { get; private set; }

        public bool JumpHeld { get; private set; }
        public bool SprintHeld { get; private set; }
        public bool JetpackHeld { get; private set; }
        public bool InteractHeld { get; private set; }

        // ---------- pressed this frame ----------
        public bool JumpPressed { get; private set; }
        public bool CrouchPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool SackPressed { get; private set; }
        public bool JetpackPressed { get; private set; }
        public bool CameraTogglePressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool SprintPressed { get; private set; }
        /// <summary>[F] / gamepad left shoulder: throw a catnip pouch.</summary>
        public bool CatnipPressed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_All.Clear();
            DebugMoveOverride = null;
            DebugJumpOverride = false;
        }

        private void Awake()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
            CreateActions();
        }

        private void Start()
        {
            // Scenes baked before local co-op have no LocalCoopJoin on the elf; player 1 brings it along.
            if (autoAddCoopJoin && PlayerIndex == 0 && GetComponent<LocalCoopJoin>() == null)
                gameObject.AddComponent<LocalCoopJoin>();
        }

        private void CreateActions()
        {
            if (_created) return;
            _created = true;

            _map = new InputActionMap("Elf");

            _move = _map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _lookMouse = _map.AddAction("LookMouse", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");
            _lookStick = _map.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");

            _jump = _map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _sprint = _map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _crouch = _map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/buttonEast");

            _interact = _map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");

            _sack = _map.AddAction("Sack", InputActionType.Button, "<Keyboard>/r");
            _sack.AddBinding("<Gamepad>/buttonNorth");

            _jetpack = _map.AddAction("Jetpack", InputActionType.Button, "<Keyboard>/q");
            _jetpack.AddBinding("<Gamepad>/rightTrigger");

            _cameraToggle = _map.AddAction("CameraToggle", InputActionType.Button, "<Keyboard>/v");
            _cameraToggle.AddBinding("<Gamepad>/rightStickPress");

            _pause = _map.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/start");

            _zoom = _map.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y", expectedControlLayout: "Axis");

            _catnip = _map.AddAction("Catnip", InputActionType.Button, "<Keyboard>/f");
            _catnip.AddBinding("<Gamepad>/leftShoulder");

            // Pause is deliberately not here: a Start press on an idle pad is the co-op join gesture, not a "claim".
            _claimScan = new[] { _move, _lookMouse, _lookStick, _jump, _sprint, _crouch, _interact, _sack, _jetpack, _cameraToggle, _catnip };
        }

        private void OnEnable()
        {
            CreateActions();
            SetActionsEnabled(true);
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            SetActionsEnabled(false);
            ClearFrame();
            Move = Vector2.zero;
            JumpHeld = SprintHeld = JetpackHeld = InteractHeld = false;
        }

        private void OnDestroy()
        {
            s_All.Remove(this);
            bool hadPad = AssignedGamepad != null;
            AssignedGamepad = null;
            if (hadPad) RefreshPrimaryFilters(); // player 1 gets this pad back

            if (!_created) return;
            _map.Dispose();
            _created = false;
        }

        private bool CanEnableActions => PlayerIndex == 0 || AssignedGamepad != null;

        private void SetActionsEnabled(bool on)
        {
            if (!_created) return;
            if (on && CanEnableActions) _map.Enable();
            else _map.Disable();
        }

        // ---------------- co-op API ----------------

        /// <summary>
        /// Sets this elf's player slot. index 0 = player 1 (gamepad ignored); index 1..3 = only <paramref name="gamepad"/>.
        /// Call right after AddComponent (PlayerSetup.CreatePlayer does). Re-filters player 1 so it drops the pad.
        /// </summary>
        public void AssignPlayer(int index, Gamepad gamepad)
        {
            CreateActions();
            PlayerIndex = Mathf.Max(0, index);
            AssignedGamepad = PlayerIndex == 0 ? null : gamepad;
            // The button press that made this player join must not also jump / pause on the first frames.
            SuppressPresses(1);
            ApplyDeviceFilter();
            SetActionsEnabled(isActiveAndEnabled);
            RefreshPrimaryFilters();
        }

        /// <summary>Ignore "pressed this frame" values for the current frame and the next <paramref name="extraFrames"/>.</summary>
        public void SuppressPresses(int extraFrames = 0)
        {
            _suppressPressesUntilFrame = Mathf.Max(_suppressPressesUntilFrame, Time.frameCount + Mathf.Max(0, extraFrames));
        }

        /// <summary>True if a player 2+ owns this device (player 1 then ignores it).</summary>
        public static bool IsGamepadClaimed(InputDevice device)
        {
            if (device == null) return false;
            for (int i = 0; i < s_All.Count; i++)
            {
                ElfInput e = s_All[i];
                if (e != null && e.PlayerIndex > 0 && e.AssignedGamepad == device) return true;
            }
            return false;
        }

        /// <summary>First player-1 ElfInput, or null.</summary>
        public static ElfInput Primary
        {
            get
            {
                for (int i = 0; i < s_All.Count; i++)
                    if (s_All[i] != null && s_All[i].PlayerIndex == 0) return s_All[i];
                return null;
            }
        }

        private static bool AnyGamepadClaimed()
        {
            for (int i = 0; i < s_All.Count; i++)
            {
                ElfInput e = s_All[i];
                if (e != null && e.PlayerIndex > 0 && e.AssignedGamepad != null) return true;
            }
            return false;
        }

        private static void RefreshPrimaryFilters()
        {
            for (int i = 0; i < s_All.Count; i++)
            {
                ElfInput e = s_All[i];
                if (e != null && e._created && e.PlayerIndex == 0) e.ApplyDeviceFilter();
            }
        }

        private void ApplyDeviceFilter()
        {
            if (!_created) return;

            if (PlayerIndex > 0)
            {
                if (AssignedGamepad != null)
                {
                    _map.devices = new ReadOnlyArray<InputDevice>(new InputDevice[] { AssignedGamepad });
                    _filterApplied = true;
                }
                return;
            }

            if (LastUsedGamepad != null && IsGamepadClaimed(LastUsedGamepad)) LastUsedGamepad = null;

            if (!AnyGamepadClaimed())
            {
                // Alone: unrestricted, exactly the pre-co-op behaviour. Only touch the map if we filtered it before.
                if (_filterApplied)
                {
                    _map.devices = null;
                    _filterApplied = false;
                }
                return;
            }

            var list = new List<InputDevice>();
            ReadOnlyArray<InputDevice> all = InputSystem.devices;
            for (int i = 0; i < all.Count; i++)
            {
                InputDevice d = all[i];
                if (d is Gamepad && IsGamepadClaimed(d)) continue;
                list.Add(d);
            }
            _map.devices = new ReadOnlyArray<InputDevice>(list.ToArray());
            _filterApplied = true;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // Player 1's explicit device list must pick up keyboards/pads plugged in later.
            if (PlayerIndex != 0 || !_filterApplied) return;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected ||
                change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                ApplyDeviceFilter();
        }

        // ---------------- per frame ----------------

        private void ClearFrame()
        {
            Look = LookMouse = LookStick = Vector2.zero;
            Zoom = 0f;
            ClearPressed();
        }

        private void ClearPressed()
        {
            JumpPressed = CrouchPressed = InteractPressed = SackPressed = JetpackPressed =
                CameraTogglePressed = PausePressed = SprintPressed = CatnipPressed = false;
        }

        private void Update()
        {
            if (!_created) return;
            bool primary = PlayerIndex == 0;

            Vector2? moveOverride = primary ? DebugMoveOverride : null;
            Move = moveOverride ?? Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            LookMouse = _lookMouse.ReadValue<Vector2>();
            LookStick = _lookStick.ReadValue<Vector2>();
            Look = LookMouse + LookStick * (stickLookPixelsPerSecond * Time.unscaledDeltaTime);
            Zoom = _zoom.ReadValue<float>();

            JumpHeld = _jump.IsPressed();
            SprintHeld = _sprint.IsPressed();
            JetpackHeld = _jetpack.IsPressed();
            InteractHeld = _interact.IsPressed();

            JumpPressed = _jump.WasPressedThisFrame();
            if (primary && DebugJumpOverride)
            {
                JumpPressed = true;
                DebugJumpOverride = false;
            }
            CrouchPressed = _crouch.WasPressedThisFrame();
            InteractPressed = _interact.WasPressedThisFrame();
            SackPressed = _sack.WasPressedThisFrame();
            JetpackPressed = _jetpack.WasPressedThisFrame();
            CameraTogglePressed = _cameraToggle.WasPressedThisFrame();
            PausePressed = _pause.WasPressedThisFrame();
            SprintPressed = _sprint.WasPressedThisFrame();
            CatnipPressed = _catnip.WasPressedThisFrame();

            if (Time.frameCount <= _suppressPressesUntilFrame) ClearPressed();

            if (primary) TrackPrimaryDevices();
        }

        /// <summary>Player 1: remember which gamepad (if any) and whether keyboard/mouse are really in use.</summary>
        private void TrackPrimaryDevices()
        {
            for (int i = 0; i < _claimScan.Length; i++)
            {
                InputControl c = _claimScan[i].activeControl;
                if (c == null) continue;
                InputDevice d = c.device;
                if (d is Gamepad g)
                {
                    if (!IsGamepadClaimed(g)) LastUsedGamepad = g;
                }
                else if (d is Keyboard || d is Mouse)
                {
                    HasUsedKeyboardMouse = true;
                }
            }
        }
    }
}
