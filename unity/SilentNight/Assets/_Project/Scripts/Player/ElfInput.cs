using UnityEngine;
using UnityEngine.InputSystem;

namespace SilentNight.Player
{
    /// <summary>
    /// Player input built entirely in code (no .inputactions asset).
    /// Values are sampled once per frame in Update (runs early via DefaultExecutionOrder) so every
    /// other player component reads consistent values. Disabling the component disables all actions.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class ElfInput : MonoBehaviour
    {
        [Tooltip("Right-stick look is converted to mouse-pixel equivalents per second so the camera can use one sensitivity.")]
        [SerializeField] private float stickLookPixelsPerSecond = 1200f;

        private InputAction _move, _lookMouse, _lookStick, _jump, _sprint, _crouch, _interact,
                            _sack, _jetpack, _cameraToggle, _pause, _zoom;
        private bool _created;

        // ---------- continuous values ----------
        /// <summary>WASD / left stick, magnitude 0..1.</summary>
        public Vector2 Move { get; private set; }
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

        private void Awake() => CreateActions();

        private void CreateActions()
        {
            if (_created) return;
            _created = true;

            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
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

            _lookMouse = new InputAction("LookMouse", InputActionType.Value, "<Mouse>/delta", expectedControlType: "Vector2");
            _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlType: "Vector2");

            _jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/buttonEast");

            _interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonWest");

            _sack = new InputAction("Sack", InputActionType.Button, "<Keyboard>/r");
            _sack.AddBinding("<Gamepad>/buttonNorth");

            _jetpack = new InputAction("Jetpack", InputActionType.Button, "<Keyboard>/q");
            _jetpack.AddBinding("<Gamepad>/rightTrigger");

            _cameraToggle = new InputAction("CameraToggle", InputActionType.Button, "<Keyboard>/v");
            _cameraToggle.AddBinding("<Gamepad>/rightStickPress");

            _pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/start");

            _zoom = new InputAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y", expectedControlType: "Axis");
        }

        private void OnEnable()
        {
            CreateActions();
            SetActionsEnabled(true);
        }

        private void OnDisable()
        {
            SetActionsEnabled(false);
            ClearFrame();
            Move = Vector2.zero;
            JumpHeld = SprintHeld = JetpackHeld = InteractHeld = false;
        }

        private void OnDestroy()
        {
            if (!_created) return;
            _move.Dispose(); _lookMouse.Dispose(); _lookStick.Dispose(); _jump.Dispose(); _sprint.Dispose();
            _crouch.Dispose(); _interact.Dispose(); _sack.Dispose(); _jetpack.Dispose(); _cameraToggle.Dispose();
            _pause.Dispose(); _zoom.Dispose();
            _created = false;
        }

        private void SetActionsEnabled(bool on)
        {
            if (!_created) return;
            if (on)
            {
                _move.Enable(); _lookMouse.Enable(); _lookStick.Enable(); _jump.Enable(); _sprint.Enable();
                _crouch.Enable(); _interact.Enable(); _sack.Enable(); _jetpack.Enable(); _cameraToggle.Enable();
                _pause.Enable(); _zoom.Enable();
            }
            else
            {
                _move.Disable(); _lookMouse.Disable(); _lookStick.Disable(); _jump.Disable(); _sprint.Disable();
                _crouch.Disable(); _interact.Disable(); _sack.Disable(); _jetpack.Disable(); _cameraToggle.Disable();
                _pause.Disable(); _zoom.Disable();
            }
        }

        private void ClearFrame()
        {
            Look = LookMouse = LookStick = Vector2.zero;
            Zoom = 0f;
            JumpPressed = CrouchPressed = InteractPressed = SackPressed = JetpackPressed =
                CameraTogglePressed = PausePressed = SprintPressed = false;
        }

        private void Update()
        {
            if (!_created) return;

            Move = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
            LookMouse = _lookMouse.ReadValue<Vector2>();
            LookStick = _lookStick.ReadValue<Vector2>();
            Look = LookMouse + LookStick * (stickLookPixelsPerSecond * Time.unscaledDeltaTime);
            Zoom = _zoom.ReadValue<float>();

            JumpHeld = _jump.IsPressed();
            SprintHeld = _sprint.IsPressed();
            JetpackHeld = _jetpack.IsPressed();
            InteractHeld = _interact.IsPressed();

            JumpPressed = _jump.WasPressedThisFrame();
            CrouchPressed = _crouch.WasPressedThisFrame();
            InteractPressed = _interact.WasPressedThisFrame();
            SackPressed = _sack.WasPressedThisFrame();
            JetpackPressed = _jetpack.WasPressedThisFrame();
            CameraTogglePressed = _cameraToggle.WasPressedThisFrame();
            PausePressed = _pause.WasPressedThisFrame();
            SprintPressed = _sprint.WasPressedThisFrame();
        }
    }
}
