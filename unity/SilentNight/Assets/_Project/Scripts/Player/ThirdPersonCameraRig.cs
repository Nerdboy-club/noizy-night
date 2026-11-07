using SilentNight.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SilentNight.Player
{
    /// <summary>
    /// Third-person orbit camera on Cinemachine 3. A follow-target pivot (a separate root object, so the
    /// elf's own turning never rotates the camera) gets yaw/pitch from ElfInput.Look; a CinemachineCamera with
    /// CinemachineThirdPersonFollow tracks it. V toggles close/far, mouse wheel zooms.
    /// Runs after ElfController (execution order) and in Update, so the pivot is placed before the brain's LateUpdate.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        [Header("References (created at runtime if empty)")]
        [SerializeField] private CinemachineCamera cinemachineCamera;
        [SerializeField] private Transform followTarget;

        [Header("Look")]
        [Tooltip("Degrees per mouse pixel (stick input is pre-scaled to pixels by ElfInput).")]
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private bool invertY;
        [SerializeField] private float minPitch = -30f;
        [SerializeField] private float maxPitch = 65f;
        [SerializeField] private float startPitch = 18f;

        [Header("Framing")]
        [SerializeField] private float pivotHeight = 0.52f;
        [SerializeField] private float crouchPivotHeight = 0.36f;
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.25f, 0.12f, 0f);
        [SerializeField] private float verticalArmLength = 0.15f;
        [Range(0f, 1f)] [SerializeField] private float cameraSide = 1f;
        [SerializeField] private float fieldOfView = 60f;
        [SerializeField] private float cameraRadius = 0.12f;

        [Header("Distance")]
        [SerializeField] private float startDistance = 2.6f;
        [SerializeField] private float closeDistance = 1.8f;
        [SerializeField] private float farDistance = 3.6f;
        [SerializeField] private float minZoom = 1.2f;
        [SerializeField] private float maxZoom = 5f;
        [SerializeField] private float zoomStep = 0.3f;
        [SerializeField] private float distanceSmoothing = 10f;

        [Header("Local co-op (shared screen)")]
        [Tooltip("With more than one registered elf, follow their midpoint and pull back so everyone stays in view (clamped to maxZoom).")]
        [SerializeField] private bool groupFraming = true;
        [Tooltip("Extra distance (m) on top of the distance needed to fit the group's radius in the vertical FOV.")]
        [SerializeField] private float groupPadding = 0.8f;
        [Tooltip("Group radius never counts as smaller than this (m), so two elves side by side do not trigger a zoom.")]
        [SerializeField] private float groupMinRadius = 0.6f;

        private ElfInput _input;
        private ElfController _controller;
        private float _yaw;
        private float _pitch;
        private float _targetDistance;
        private float _distance;
        private bool _createdCamera;
        private bool _createdPivot;
        private CinemachineThirdPersonFollow _follow;
        private bool _grouped;
        private Vector3 _groupCenter;
        private float _groupRadius;

        /// <summary>True while the rig frames several local elves (shared-screen co-op).</summary>
        public bool IsFramingGroup => _grouped;

        /// <summary>Camera yaw in degrees (world). ElfController uses it for camera-relative movement.</summary>
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public Transform FollowTarget => followTarget;
        public CinemachineCamera CinemachineCamera => cinemachineCamera;

        private void Awake()
        {
            _input = GetComponent<ElfInput>();
            _yaw = transform.eulerAngles.y;
            _pitch = startPitch;
            _targetDistance = _distance = Mathf.Clamp(startDistance, minZoom, maxZoom);
        }

        private void OnEnable()
        {
            _controller = GetComponent<ElfController>();
            if (_controller != null) _controller.Teleported += OnTeleported;
        }

        private void OnDisable()
        {
            if (_controller != null) _controller.Teleported -= OnTeleported;
        }

        private void Start()
        {
            if (_controller == null)
            {
                _controller = GetComponent<ElfController>();
                if (_controller != null) _controller.Teleported += OnTeleported;
            }
            EnsurePivot();
            EnsureCamera();
            EnsureBrain();
            UpdatePivot();
        }

        private void OnDestroy()
        {
            if (_createdCamera && cinemachineCamera != null) Destroy(cinemachineCamera.gameObject);
            if (_createdPivot && followTarget != null) Destroy(followTarget.gameObject);
        }

        // ---------------- setup ----------------

        private void EnsurePivot()
        {
            if (followTarget != null) return;
            var go = new GameObject("ElfCameraPivot");
            followTarget = go.transform;
            _createdPivot = true;
        }

        private void EnsureCamera()
        {
            if (cinemachineCamera == null)
            {
                var go = new GameObject("ElfCinemachineCamera");
                go.SetActive(false); // configure fully before CinemachineCamera.OnEnable builds its pipeline
                cinemachineCamera = go.AddComponent<CinemachineCamera>();
                _follow = go.AddComponent<CinemachineThirdPersonFollow>();
                _createdCamera = true;
                ConfigureCamera();
                go.SetActive(true);
            }
            else
            {
                _follow = cinemachineCamera.GetComponent<CinemachineThirdPersonFollow>();
                if (_follow == null) _follow = cinemachineCamera.gameObject.AddComponent<CinemachineThirdPersonFollow>();
                ConfigureCamera();
            }
        }

        private void ConfigureCamera()
        {
            cinemachineCamera.Follow = followTarget;
            ApplyLens(cinemachineCamera, fieldOfView);
            if (_follow != null)
            {
                _follow.ShoulderOffset = shoulderOffset;
                _follow.VerticalArmLength = verticalArmLength;
                _follow.CameraSide = cameraSide;
                _follow.CameraDistance = _distance;
                _follow.Damping = new Vector3(0.1f, 0.25f, 0.2f);
                ApplyObstacleAvoidance(_follow, BuildCollisionFilter(), cameraRadius);
            }
        }

        // Cinemachine 3.1: CinemachineCamera.Lens is a public LensSettings struct field (FieldOfView, NearClipPlane).
        private static void ApplyLens(CinemachineCamera cam, float fov)
        {
            LensSettings lens = cam.Lens;
            lens.FieldOfView = fov;
            lens.NearClipPlane = 0.05f;
            cam.Lens = lens;
        }

        // CinemachineThirdPersonFollow.AvoidObstacles is an ObstacleSettings struct (Enabled, CollisionFilter, IgnoreTag,
        // CameraRadius, DampingIntoCollision, DampingFromCollision). Only exists when CINEMACHINE_PHYSICS is defined
        // (com.unity.modules.physics is in the manifest).
        private static void ApplyObstacleAvoidance(CinemachineThirdPersonFollow follow, LayerMask filter, float radius)
        {
            var ao = follow.AvoidObstacles;
            ao.Enabled = true;
            ao.CollisionFilter = filter;
            ao.IgnoreTag = GameConstants.TagPlayer;
            ao.CameraRadius = radius;
            ao.DampingIntoCollision = 0.05f;
            ao.DampingFromCollision = 0.4f;
            follow.AvoidObstacles = ao;
        }

        /// <summary>Everything except Player / Gift / CameraIgnore (missing layers are skipped).</summary>
        private static LayerMask BuildCollisionFilter()
        {
            int mask = ~0;
            mask = ExcludeLayer(mask, GameConstants.LayerPlayer);
            mask = ExcludeLayer(mask, GameConstants.LayerGift);
            mask = ExcludeLayer(mask, GameConstants.LayerCameraIgnore);
            mask = ExcludeLayer(mask, "Ignore Raycast");
            return mask;
        }

        private static int ExcludeLayer(int mask, string layerName)
        {
            int l = LayerMask.NameToLayer(layerName);
            return l >= 0 ? mask & ~(1 << l) : mask;
        }

        private static void EnsureBrain()
        {
            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
                if (Object.FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
            }
            if (cam.GetComponent<CinemachineBrain>() == null) cam.gameObject.AddComponent<CinemachineBrain>();
        }

        // ---------------- per frame ----------------

        private void Update()
        {
            if (_input == null) _input = GetComponent<ElfInput>();
            if (_input != null) HandleInput();

            UpdatePivot(); // also refreshes the group frame used below

            float desired = _targetDistance;
            if (_grouped) desired = Mathf.Clamp(Mathf.Max(desired, GroupFitDistance()), minZoom, maxZoom);

            float k = 1f - Mathf.Exp(-distanceSmoothing * Time.unscaledDeltaTime);
            _distance = Mathf.Lerp(_distance, desired, k);
            if (_follow != null) _follow.CameraDistance = _distance;
        }

        // ---------------- local co-op group framing ----------------
        // Shared screen only: extra local elves have no rig; this (player 1's) rig follows the midpoint of every
        // registered player and pulls back to keep them in view. Mouse/stick look stays with player 1.
        // TODO (step 2): optional split-screen (one Camera + CinemachineBrain + channel per player, Camera.rect),
        // CinemachineTargetGroup/GroupFraming for a nicer fit, per-player HUD, Billboard facing the nearest camera.

        /// <summary>Recomputes _grouped/_groupCenter/_groupRadius from GameServices.Players (allocation free).</summary>
        private void RefreshGroupFrame()
        {
            _grouped = false;
            if (!groupFraming) return;
            var players = GameServices.Players;
            if (players == null || players.Count < 2) return;

            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayerStatus p = players[i];
                if (!PlayerGate.IsAlive(p)) continue;
                Transform t = p.Transform;
                if (t == null) continue;
                sum += t.position;
                n++;
            }
            if (n < 2) return;

            Vector3 center = sum / n;
            float r = 0f;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayerStatus p = players[i];
                if (!PlayerGate.IsAlive(p)) continue;
                Transform t = p.Transform;
                if (t == null) continue;
                r = Mathf.Max(r, Vector3.Distance(center, t.position));
            }

            _grouped = true;
            _groupCenter = center;
            _groupRadius = Mathf.Max(r, groupMinRadius);
        }

        /// <summary>Camera distance at which a sphere of the group radius fits the vertical FOV, plus padding.</summary>
        private float GroupFitDistance()
        {
            float halfFov = Mathf.Clamp(fieldOfView, 10f, 170f) * 0.5f * Mathf.Deg2Rad;
            return _groupRadius / Mathf.Tan(halfFov) + groupPadding;
        }

        private void HandleInput()
        {
            // Standalone scene (no GameFlow): click to lock the cursor so mouse look works.
            if (GameServices.Flow == null && Cursor.lockState != CursorLockMode.Locked)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            if (!PlayerGate.CanControl) return;

            Vector2 look = PlayerGate.CanMouseLook ? _input.Look : StickOnly();
            _yaw += look.x * mouseSensitivity;
            _pitch += (invertY ? look.y : -look.y) * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            _yaw = Mathf.Repeat(_yaw, 360f);

            if (_input.CameraTogglePressed)
            {
                // Toggle between close and far; from an in-between zoom go to whichever is farther away.
                float midpoint = (closeDistance + farDistance) * 0.5f;
                _targetDistance = _targetDistance > midpoint ? closeDistance : farDistance;
            }

            float scroll = _input.Zoom;
            if (Mathf.Abs(scroll) > 0.01f)
                _targetDistance = Mathf.Clamp(_targetDistance - Mathf.Sign(scroll) * zoomStep, minZoom, maxZoom);
        }

        /// <summary>Right-stick contribution only (when the cursor is unlocked, the mouse must not turn the camera).</summary>
        private Vector2 StickOnly() => _input.Look - _input.LookMouse;

        private void UpdatePivot()
        {
            if (followTarget == null) return;
            RefreshGroupFrame();
            bool crouch = _controller != null && _controller.IsCrouching;
            float h = crouch ? crouchPivotHeight : pivotHeight;
            Vector3 basePos = _grouped ? _groupCenter : transform.position;
            followTarget.SetPositionAndRotation(basePos + Vector3.up * h, Quaternion.Euler(_pitch, _yaw, 0f));
        }

        private void OnTeleported()
        {
            _yaw = transform.eulerAngles.y;
            _pitch = startPitch;
            UpdatePivot();
            SnapCamera();
        }

        // CinemachineVirtualCameraBase.PreviousStateIsValid = false makes the next update skip damping (snap).
        private void SnapCamera()
        {
            if (cinemachineCamera != null) cinemachineCamera.PreviousStateIsValid = false;
        }
    }
}
