using System;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Elf locomotion on a CharacterController: camera-relative walk/sprint/crouch, gravity, jump with coyote
    /// time and buffering, stamina, jetpack (upgrade), slippery surfaces, footstep/landing/jetpack noise.
    /// Implements IPlayerStatus and registers itself in GameServices.LocalPlayer while enabled.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(ElfInput))]
    [DisallowMultipleComponent]
    public class ElfController : MonoBehaviour, IPlayerStatus
    {
        [Header("Speeds (m/s)")]
        [SerializeField] private float walkSpeed = 1.85f;
        [SerializeField] private float sprintSpeed = 2.85f;
        [SerializeField] private float crouchSpeed = 1.10f;
        [SerializeField] private float groundAcceleration = 20f;
        [SerializeField] private float airAcceleration = 8f;
        [Tooltip("Acceleration factor on a fully slippery surface (Slipperiness = 1).")]
        [SerializeField] private float slipperyAccelerationFactor = 0.08f;
        [Tooltip("Each SilentBoots level reduces slipperiness by this fraction.")]
        [SerializeField] private float bootsSlipReductionPerLevel = 0.15f;
        [SerializeField] private float turnSpeedDegrees = 640f;
        [SerializeField] private float heavyTurnFactor = 0.5f;

        [Header("Jump & gravity")]
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float maxFallSpeed = 30f;
        [SerializeField] private float jumpHeight = 0.7f;
        [Tooltip("Jump height factor when carrying something heavy (SpeedMultiplier < 0.5). 4.8/7.6 in the prototype.")]
        [SerializeField] private float heavyJumpFactor = 0.63f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBufferTime = 0.12f;
        [SerializeField] private float groundedStickVelocity = -2f;

        [Header("Crouch")]
        [SerializeField] private float standHeight = 1.1f * 0.55f;
        [SerializeField] private float crouchHeight = 0.75f * 0.55f;
        [SerializeField] private float crouchTransitionSpeed = 8f;

        [Header("Stamina (0..100)")]
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float sprintDrainPerSecond = 22f;
        [SerializeField] private float heavyCarryDrainPerSecond = 8f;
        [SerializeField] private float staminaRegenPerSecond = 28f;
        [SerializeField] private float staminaRegenDelay = 0.6f;
        [Tooltip("After running dry, sprint is blocked until stamina recovers to this value.")]
        [SerializeField] private float exhaustedRecoverThreshold = 25f;

        [Header("Jetpack (percent of tank per second)")]
        [SerializeField] private float jetpackBaseBurn = 22f;
        [SerializeField] private float jetpackBurnReductionPerLevel = 3f;
        [SerializeField] private float jetpackMinBurn = 8f;
        [SerializeField] private float jetpackBaseRefuel = 6f;
        [SerializeField] private float jetpackRefuelPerLevel = 2f;
        [SerializeField] private float jetpackBaseThrustVelocity = 3.4f;
        [SerializeField] private float jetpackThrustPerLevel = 0.4f;
        [SerializeField] private float jetpackResponsiveness = 8f;
        [SerializeField] private float jetpackNoiseInterval = 0.3f;

        [Header("Ground probe")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundProbeDistance = 0.17f;

        [Header("Noise")]
        [SerializeField] private float stepIntervalWalk = 0.46f;
        [SerializeField] private float stepIntervalSprint = 0.33f;
        [SerializeField] private float stepIntervalCrouch = 0.58f;
        [SerializeField] private float minStepSpeed = 0.5f;
        [Tooltip("Each SilentBoots level reduces footstep noise by this fraction.")]
        [SerializeField] private float bootsNoiseReductionPerLevel = 0.15f;
        [SerializeField] private float landingMinFallSpeed = 4f;
        [SerializeField] private float landingMaxFallSpeed = 14f;
        [SerializeField] private float noiseSmoothing = 3f;

        [Header("Stumble (sprinting into a wall)")]
        [Tooltip("Speed INTO the wall (m/s) needed to faceplant. Walk 1.85, sprint 2.85.")]
        [SerializeField] private float bumpMinSpeed = 2.55f;
        [Tooltip("Hits with |normal.y| above this are floors/ceilings/slopes, not walls.")]
        [SerializeField] private float bumpMaxWallNormalY = 0.5f;
        [Tooltip("A bump also needs the move to lose at least this fraction of the planar speed (filters glancing slides and steps).")]
        [SerializeField] private float bumpMinSpeedLoss = 0.4f;
        [Tooltip("Loose dynamic bodies lighter than this (gifts, toys) never cause a faceplant.")]
        [SerializeField] private float bumpIgnoreRigidbodyMass = 5f;
        [SerializeField] private float bumpCooldown = 2.5f;
        [SerializeField] private float bumpNoise = 45f;
        [SerializeField] private string bumpToast = "Бум! Эльф врезался в стену.";

        [Header("Pushing loose bodies (doors, toys, loose gifts)")]
        [SerializeField] private bool pushBodies = true;
        [Tooltip("Continuous push while walking into a body: impulse per second per m/s of speed into it (N per m/s). " +
                 "Heavier bodies accelerate less. Tuned so a crouch-push opens a CreakyDoor silently and a walk-push creaks.")]
        [SerializeField] private float pushStrength = 4f;
        [Tooltip("One-off impulse on first contact per m/s of approach speed above pushImpactFreeSpeed (N·s per m/s). " +
                 "Sprinting into a door slams it.")]
        [SerializeField] private float pushImpactStrength = 7f;
        [Tooltip("Approach speed (m/s) below which first contact adds no extra impulse (crouch speed is 1.1).")]
        [SerializeField] private float pushImpactFreeSpeed = 1.2f;
        [Tooltip("Hard cap of a single push impulse (N·s).")]
        [SerializeField] private float pushMaxImpulse = 20f;
        [Tooltip("A push never makes the contact point faster than the elf's speed times this.")]
        [SerializeField] private float pushMaxSpeedFactor = 1.2f;
        [Tooltip("Contact with the same body within this time (s) counts as continuous (no new impact impulse).")]
        [SerializeField] private float pushContactMemory = 0.25f;

        [Header("Huge gift (wrapped bike) clearance")]
        [Tooltip("The held bike's box is shrunk by this much on every side before the overlap test (lets it graze walls).")]
        [SerializeField] private float hugeClearanceSkin = 0.02f;
        [Tooltip("Ignore the bottom of the bike box below feet + max(this, stepOffset): floor, rugs and low steps never block it.")]
        [SerializeField] private float hugeFloorClearance = 0.2f;
        [SerializeField] private float hugeScrapeNoise = 35f;
        [SerializeField] private float hugeScrapeCooldown = 0.6f;
        [SerializeField] private float hugeStuckToastCooldown = 4f;
        [SerializeField] private string hugeStuckToast = "Велосипед застрял в проёме! Разверните его боком.";

        [Header("Ledge grab & mantle")]
        [SerializeField] private bool ledgeGrabEnabled = true;
        [Tooltip("Ledge top height above the feet (m) accepted for a mantle. Below: stepOffset / jump. Above: out of reach.")]
        [SerializeField] private float ledgeMinHeight = 0.35f;
        [SerializeField] private float ledgeMaxHeight = 1.0f;
        [Tooltip("Forward capsule cast distance beyond the controller radius (m).")]
        [SerializeField] private float ledgeReach = 0.3f;
        [Tooltip("Wall faces with |normal.y| above this are not climbable faces.")]
        [SerializeField] private float ledgeMaxWallNormalY = 0.35f;
        [Tooltip("The ledge top must be walkable: normal.y above this.")]
        [SerializeField] private float ledgeMinTopNormalY = 0.7f;
        [Tooltip("Wish direction must face into the wall at least this much (dot).")]
        [SerializeField] private float ledgeMinFacingDot = 0.5f;
        [Tooltip("How far past the wall face (in addition to the radius) the elf ends up standing (m).")]
        [SerializeField] private float ledgeInset = 0.06f;
        [Tooltip("No grab when falling faster than this (m/s).")]
        [SerializeField] private float ledgeMaxFallSpeed = 6f;
        [SerializeField] private float mantleDuration = 0.45f;
        [Tooltip("Fraction of the mantle spent rising before the forward part starts to dominate.")]
        [SerializeField] private float mantleUpFraction = 0.6f;
        [Tooltip("Feet end this far above the ledge top (must exceed the controller skin width).")]
        [SerializeField] private float mantleClearance = 0.04f;
        [SerializeField] private float mantleExitSpeed = 0.6f;
        [SerializeField] private float mantleNoise = 20f;
        [SerializeField] private float mantleCooldown = 0.3f;

        [Header("Misc")]
        [Tooltip("Pause key toggles GameServices.Flow.TogglePause().")]
        [SerializeField] private bool handlePauseInput = true;
        [SerializeField] private bool moveToSpawnOnStart = true;

        // ---------------- components ----------------
        private CharacterController _cc;
        private ElfInput _input;
        private ElfCarry _carry;
        private ElfInteractor _interactor;
        private ThirdPersonCameraRig _rig;

        // ---------------- state ----------------
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private bool _grounded;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private float _peakFallSpeed;
        private bool _crouching;
        private bool _sprinting;
        private bool _exhausted;
        private float _stamina;
        private float _staminaRegenTimer;
        private float _fuel01 = 1f;
        private bool _jetpacking;
        private float _jetpackNoiseTimer;
        private float _stepTimer;
        private float _currentNoise;
        private SurfaceType _surface = SurfaceType.Default;
        private SurfaceTag _surfaceTag;
        private Collider _lastGroundCollider;
        private SurfaceTag _lastGroundTag;

        // stumble detection (filled by OnControllerColliderHit during _cc.Move)
        private bool _collectHits;
        private Vector3 _preMovePlanar;
        private float _bestBumpSpeed;
        private float _bumpCooldownTimer;

        // pushing loose bodies (filled by OnControllerColliderHit during _cc.Move)
        private Vector3 _pushWishVelocity;
        private Rigidbody _pushedThisMove;
        private Rigidbody _lastPushBody;
        private float _lastPushTime = -10f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];

        // huge gift clearance (box pose relative to the elf root, refreshed every frame while carrying the bike)
        private readonly Collider[] _hugeOverlap = new Collider[16];
        private int _hugeMask = ~0;
        private Vector3 _hugeLocalCenter;
        private Quaternion _hugeLocalRotation = Quaternion.identity;
        private Vector3 _hugeHalfExtents;
        private Transform _hugeGiftRoot;
        private bool _hugeGiftBlocked;
        private float _hugeScrapeTimer;
        private float _hugeToastTimer;

        // ledge grab / mantle
        private int _ledgeMask = ~0;
        private readonly RaycastHit[] _ledgeHits = new RaycastHit[16];      // forward wall cast
        private readonly RaycastHit[] _ledgeProbeHits = new RaycastHit[16]; // top / rise probes (used inside the wall loop)
        private readonly Collider[] _ledgeOverlap = new Collider[16];
        private bool _mantling;
        private float _mantleTimer;
        private float _mantleCooldownTimer;
        private Vector3 _mantleStart;
        private Vector3 _mantleEnd;
        private Vector3 _mantleDir;

        // ---------------- events (for animator / visuals / audio inside the Player module) ----------------
        public event Action Jumped;
        /// <summary>Arg: fall speed at impact (m/s).</summary>
        public event Action<float> Landed;
        public event Action Teleported;
        /// <summary>Arg: loudness of the footstep noise raised.</summary>
        public event Action<float> Footstep;
        /// <summary>Elf sprinted face-first into a wall/obstacle. Arg: speed into the wall (m/s). ElfRagdoll goes limp.</summary>
        public event Action<float> Bumped;
        /// <summary>The elf grabbed a ledge and started clambering up (normal movement is suspended).</summary>
        public event Action MantleStarted;
        /// <summary>The elf finished clambering onto a ledge.</summary>
        public event Action Mantled;

        // ---------------- public read API ----------------
        /// <summary>True while the elf is clambering up onto a ledge (~mantleDuration seconds).</summary>
        public bool IsMantling => _mantling;
        /// <summary>Set by ElfRagdoll while the elf is limp: ignores movement input.</summary>
        public bool InputLocked { get; set; }
        public Vector3 Velocity => _planarVelocity + Vector3.up * _verticalVelocity;
        public float PlanarSpeed => _planarVelocity.magnitude;
        public float VerticalSpeed => _verticalVelocity;
        public bool IsSprinting => _sprinting;
        public bool IsJetpacking => _jetpacking;
        public bool IsExhausted => _exhausted;
        public float WalkSpeed => walkSpeed;
        public float SprintSpeed => sprintSpeed;
        /// <summary>True when, this frame, the held Huge gift (bike) vetoed part of the elf's move or turn
        /// because its box would have entered static geometry (door frames, walls, furniture).</summary>
        public bool HugeGiftBlocked => _hugeGiftBlocked;
        public bool IsNetworkRemote => _input != null && _input.IsNetworkRemote;

        private Vector3 _netTargetPos;
        private float _netTargetYaw;
        private bool _netHasState;

        public void ApplyNetworkState(Vector3 pos, float yaw, Vector3 planarVel, float verticalVel,
            bool grounded, bool crouching, bool sprinting, bool mantling, bool jetpacking, float stamina01)
        {
            if (!_netHasState || Vector3.Distance(transform.position, pos) > 3.2f)
            {
                Teleport(pos, Quaternion.Euler(0f, yaw, 0f), notify: false);
            }
            _netTargetPos = pos;
            _netTargetYaw = yaw;
            _netHasState = true;
            _planarVelocity = planarVel;
            _verticalVelocity = verticalVel;
            _grounded = grounded;
            _crouching = crouching;
            _sprinting = sprinting;
            _mantling = mantling;
            _jetpacking = jetpacking;
            _stamina = Mathf.Clamp01(stamina01) * maxStamina;
        }

        // ---------------- IPlayerStatus ----------------
        public float Stamina01 => maxStamina > 0f ? Mathf.Clamp01(_stamina / maxStamina) : 0f;
        public float JetpackFuel01 => HasJetpack ? _fuel01 : 0f;
        public bool HasJetpack => JetpackLevel > 0;
        public bool IsCrouching => _crouching;
        public bool IsGrounded => _grounded;
        public float CurrentNoise => _currentNoise;
        public SurfaceType CurrentSurface => _surface;
        public ICarrier Carrier => _carry;
        public Transform Transform => transform;
        public IInteractable FocusedInteractable => _interactor != null ? _interactor.FocusedInteractable : null;

        private int JetpackLevel => GameServices.Profile != null ? GameServices.Profile.JetpackLevel : 0;
        private int BootsLevel => GameServices.Profile != null ? GameServices.Profile.SilentBootsLevel : 0;
        private float BootsNoiseFactor => Mathf.Max(0.1f, 1f - bootsNoiseReductionPerLevel * BootsLevel);

        // ======================================================================

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _input = GetComponent<ElfInput>();
            CacheSiblings();
            _stamina = maxStamina;
            _currentNoise = GameConstants.NoiseIdle;

            int playerLayer = LayerMask.NameToLayer(GameConstants.LayerPlayer);
            if (playerLayer >= 0) groundMask = groundMask.value & ~(1 << playerLayer);

            _hugeMask = ~0 & ~Physics.IgnoreRaycastLayer;
            ExcludeLayer(ref _hugeMask, GameConstants.LayerPlayer);
            ExcludeLayer(ref _hugeMask, GameConstants.LayerGift);
            ExcludeLayer(ref _hugeMask, GameConstants.LayerNPC);
            _ledgeMask = _hugeMask; // same exclusions: player, gifts, NPCs, Ignore Raycast
        }

        private static void ExcludeLayer(ref int mask, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask &= ~(1 << layer);
        }

        /// <summary>Siblings can be added after this component (PlayerSetup at runtime), so re-query lazily.</summary>
        private void CacheSiblings()
        {
            if (_carry == null) _carry = GetComponent<ElfCarry>();
            if (_interactor == null) _interactor = GetComponent<ElfInteractor>();
            if (_rig == null) _rig = GetComponent<ThirdPersonCameraRig>();
        }

        private void OnEnable()
        {
            if (_input == null) _input = GetComponent<ElfInput>();
            GameServices.RegisterPlayer(this, isLocal: !IsNetworkRemote);
        }

        private void OnDisable() => GameServices.UnregisterPlayer(this);

        private void Start()
        {
            CacheSiblings();
            if (moveToSpawnOnStart)
            {
                GameObject spawn = FindSpawn();
                if (spawn != null) Teleport(spawn.transform.position, spawn.transform.rotation);
            }
        }

        private static GameObject FindSpawn()
        {
            try
            {
                return GameObject.FindWithTag(GameConstants.TagSpawn);
            }
            catch (UnityException)
            {
                // Tag not defined in the Tag Manager yet.
                return null;
            }
        }

        /// <summary>Move the elf instantly (CharacterController must be disabled while setting the transform).</summary>
        public void Teleport(Vector3 position, Quaternion rotation, bool notify = true)
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            bool wasEnabled = _cc.enabled;
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
            _cc.enabled = wasEnabled;
            Physics.SyncTransforms();

            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _peakFallSpeed = 0f;
            _coyoteTimer = 0f;
            _jumpBufferTimer = 0f;
            _grounded = false;
            _mantling = false;
            if (notify) Teleported?.Invoke();
        }

        // ======================================================================

        private void Update()
        {
            if (handlePauseInput && _input.PausePressed)
            {
                GameFlow flow = GameServices.Flow;
                if (flow != null) flow.TogglePause();
            }

            float dt = Time.deltaTime;
            if (dt <= 0f || !_cc.enabled) return; // paused (timeScale 0) or controller disabled
            CacheSiblings();

            if (IsNetworkRemote)
            {
                if (_netHasState)
                {
                    Vector3 cur = transform.position;
                    Vector3 next = Vector3.Lerp(cur, _netTargetPos, 1f - Mathf.Exp(-18f * dt));
                    Quaternion nextRot = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _netTargetYaw, 0f), 1f - Mathf.Exp(-18f * dt));
                    bool wasOn = _cc.enabled;
                    _cc.enabled = false;
                    transform.SetPositionAndRotation(next, nextRot);
                    _cc.enabled = wasOn;
                }
                UpdateCapsuleHeight(dt);
                UpdateFootsteps(dt);
                UpdateNoiseLevel(dt);
                return;
            }

            if (_mantleCooldownTimer > 0f) _mantleCooldownTimer -= dt;
            if (_mantling)
            {
                UpdateMantle(dt);
                UpdateNoiseLevel(dt);
                return;
            }

            bool canControl = PlayerGate.CanControl && !InputLocked;
            Vector2 moveInput = canControl ? _input.Move : Vector2.zero;
            bool wantsMove = moveInput.sqrMagnitude > 0.0001f;
            float carryMul = _carry != null ? _carry.SpeedMultiplier : 1f;
            bool heavy = carryMul < 0.5f;

            // ---- crouch ----
            if (canControl && _input.CrouchPressed)
            {
                if (_crouching) { if (HasHeadroom()) _crouching = false; }
                else _crouching = true;
            }
            if (canControl && _crouching && _input.SprintPressed && HasHeadroom()) _crouching = false;
            UpdateCapsuleHeight(dt);

            // ---- sprint & stamina ----
            bool coCarrying = _carry != null && _carry.IsCoCarrying;
            _sprinting = canControl && wantsMove && _input.SprintHeld && !_crouching && !_exhausted && _stamina > 0f
                         && !coCarrying; // no sprinting with a shared bike
            UpdateStamina(dt);

            // ---- desired planar velocity ----
            Vector3 wishDir = Vector3.zero;
            if (wantsMove)
            {
                Quaternion yaw = Quaternion.Euler(0f, GetCameraYaw(), 0f);
                wishDir = yaw * new Vector3(moveInput.x, 0f, moveInput.y); // magnitude 0..1
            }
            float speed = _crouching ? crouchSpeed : (_sprinting ? sprintSpeed : walkSpeed);
            Vector3 targetVelocity = wishDir * (speed * carryMul);

            float slip = _grounded ? CurrentSlipperiness() : 0f;
            float accel = _grounded
                ? groundAcceleration * Mathf.Lerp(1f, slipperyAccelerationFactor, slip)
                : airAcceleration;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, accel * dt);

            // ---- jump (buffer + coyote) ----
            if (canControl && _input.JumpPressed) _jumpBufferTimer = jumpBufferTime;
            else _jumpBufferTimer -= dt;
            _coyoteTimer = _grounded ? coyoteTime : _coyoteTimer - dt;

            bool jumpedThisFrame = false;
            if (_jumpBufferTimer > 0f && _coyoteTimer > 0f && !_crouching)
            {
                float h = jumpHeight * (heavy ? heavyJumpFactor : 1f);
                _verticalVelocity = Mathf.Sqrt(2f * -gravity * h);
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
                _grounded = false;
                jumpedThisFrame = true;
                RaiseNoise(GameConstants.NoiseWalk * SurfaceNoiseMultiplier() * BootsNoiseFactor, NoiseSource.Footstep);
                Jumped?.Invoke();
            }

            // ---- ledge grab (airborne / just jumped, pushing toward a wall with a reachable top) ----
            if (ledgeGrabEnabled && canControl && wantsMove && !_grounded && !_crouching
                && _mantleCooldownTimer <= 0f && _verticalVelocity > -ledgeMaxFallSpeed
                && !(_carry != null && (_carry.IsCarryingHuge || coCarrying))
                && TryFindLedge(new Vector3(wishDir.x, 0f, wishDir.z).normalized, out Vector3 ledgeStand, out Vector3 ledgeDir))
            {
                BeginMantle(ledgeStand, ledgeDir);
                UpdateMantle(dt);
                UpdateNoiseLevel(dt);
                return;
            }

            // ---- jetpack ----
            UpdateJetpack(dt, canControl);

            // ---- gravity ----
            if (!_jetpacking)
            {
                if (_grounded && !jumpedThisFrame && _verticalVelocity < 0f) _verticalVelocity = groundedStickVelocity;
                else _verticalVelocity += gravity * dt;
            }
            if (_verticalVelocity < -maxFallSpeed) _verticalVelocity = -maxFallSpeed;

            // ---- move ----
            bool wasGrounded = _grounded;
            if (!_grounded) _peakFallSpeed = Mathf.Max(_peakFallSpeed, -_verticalVelocity);

            // Facing is computed before the move so the huge-gift clearance check can veto it.
            Quaternion currentRot = transform.rotation;
            Quaternion facingRot = currentRot;
            bool wantsTurn = wishDir.sqrMagnitude > 0.0001f;
            if (wantsTurn)
            {
                Quaternion targetRot = Quaternion.LookRotation(new Vector3(wishDir.x, 0f, wishDir.z), Vector3.up);
                float turn = turnSpeedDegrees * (heavy ? heavyTurnFactor : 1f) * dt;
                facingRot = Quaternion.RotateTowards(currentRot, targetRot, turn);
            }

            // Huge gift (bike): its colliders are off while held, so test its box against the level ourselves.
            _hugeGiftBlocked = false;
            bool hugeMoveBlocked = false;
            bool hugeActive = false;
            bool hugeWedged = false;
            Vector3 preMovePos = transform.position;
            if (_hugeScrapeTimer > 0f) _hugeScrapeTimer -= dt;
            if (_hugeToastTimer > 0f) _hugeToastTimer -= dt;
            // Not while two elves share the bike: CoCarry owns its pose then (TODO: clearance for the shared bike).
            if (_carry != null && _carry.IsCarryingHuge && !coCarrying && SnapshotHugeBox())
            {
                hugeActive = true;
                hugeWedged = HugeBoxOverlaps(preMovePos, currentRot);
                // Already wedged (e.g. picked up inside a frame): don't veto anything so the player can wriggle free.
                if (!hugeWedged) ResolveHugeGiftMotion(preMovePos, currentRot, ref facingRot, dt, ref hugeMoveBlocked);
            }

            _preMovePlanar = _planarVelocity;
            _bestBumpSpeed = 0f;
            _pushWishVelocity = targetVelocity;
            _pushedThisMove = null;
            _collectHits = true;
            CollisionFlags flags = _cc.Move((_planarVelocity + Vector3.up * _verticalVelocity) * dt);
            _collectHits = false;

            // The CharacterController may slide somewhere the prediction didn't test: verify, and undo the planar part.
            if (hugeActive && !hugeWedged)
            {
                Vector3 moved = transform.position - preMovePos;
                moved.y = 0f;
                if (moved.sqrMagnitude > 1e-8f && HugeBoxOverlaps(transform.position, facingRot))
                {
                    _cc.Move(-moved);
                    facingRot = currentRot;
                    _planarVelocity = Vector3.zero;
                    _hugeGiftBlocked = true;
                    hugeMoveBlocked = true;
                }
            }
            if (hugeMoveBlocked && wantsMove) OnHugeGiftScrape();

            if (_bumpCooldownTimer > 0f) _bumpCooldownTimer -= dt;
            if ((flags & CollisionFlags.Sides) != 0)
            {
                Vector3 actual = _cc.velocity;
                actual.y = 0f;

                // Faceplant: sprinting straight into a wall.
                float preSpeed = _preMovePlanar.magnitude;
                if (_bestBumpSpeed >= bumpMinSpeed && !_crouching && !InputLocked && _bumpCooldownTimer <= 0f
                    && actual.magnitude <= preSpeed * (1f - bumpMinSpeedLoss))
                    TriggerBump(_bestBumpSpeed);

                // Kill momentum into walls (important on ice).
                if (actual.sqrMagnitude < _planarVelocity.sqrMagnitude) _planarVelocity = actual;
            }
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            // ---- ground / surface ----
            bool probeHit = ProbeGround();
            _grounded = (_cc.isGrounded || probeHit) && _verticalVelocity <= 0.5f;

            if (_grounded && !wasGrounded) OnLanded();
            if (_grounded) _peakFallSpeed = 0f;

            // ---- facing ----
            if (wantsTurn) transform.rotation = facingRot;

            // ---- noise ----
            if (canControl)
            {
                UpdateFootsteps(dt);
                UpdateNoiseLevel(dt);
            }
        }

        private float GetCameraYaw()
        {
            if (_rig != null && _rig.isActiveAndEnabled) return _rig.Yaw;
            Camera cam = Camera.main;
            if (cam != null) return cam.transform.eulerAngles.y;
            return transform.eulerAngles.y;
        }

        // ---------------- huge gift clearance ----------------

        /// <summary>Store the held bike's box relative to the elf root, so it can be re-posed for candidate moves/turns.</summary>
        private bool SnapshotHugeBox()
        {
            if (!_carry.TryGetHugeBox(out Vector3 center, out Quaternion rot, out Vector3 half, out Transform giftRoot))
                return false;
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            _hugeLocalCenter = inv * (center - transform.position);
            _hugeLocalRotation = inv * rot;
            _hugeHalfExtents = half;
            _hugeGiftRoot = giftRoot;
            return true;
        }

        /// <summary>Would the bike box overlap solid static geometry if the elf root stood at pos/rot?</summary>
        private bool HugeBoxOverlaps(Vector3 pos, Quaternion rot)
        {
            Vector3 center = pos + rot * _hugeLocalCenter;
            Quaternion boxRot = rot * _hugeLocalRotation;
            float skin = Mathf.Max(0f, hugeClearanceSkin);
            Vector3 half = new Vector3(
                Mathf.Max(0.01f, _hugeHalfExtents.x - skin),
                Mathf.Max(0.01f, _hugeHalfExtents.y - skin),
                Mathf.Max(0.01f, _hugeHalfExtents.z - skin));

            // Cut the bottom of the box off at feet + clearance so the floor and low steps never count.
            Vector3 up = boxRot * Vector3.up;
            if (up.y < 0f) up = -up;
            if (up.y > 0.5f)
            {
                float minBottom = pos.y + Mathf.Max(hugeFloorClearance, _cc.stepOffset);
                float bottom = center.y - half.y * up.y;
                if (bottom < minBottom)
                {
                    float cut = (minBottom - bottom) / up.y;
                    if (cut >= half.y * 2f - 0.02f) return false; // whole box is below the clearance line
                    half.y -= cut * 0.5f;
                    center += up * (cut * 0.5f);
                }
            }

            int n = Physics.OverlapBoxNonAlloc(center, half, _hugeOverlap, boxRot, _hugeMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _hugeOverlap[i];
                if (c == null || c.isTrigger || IsSelf(c)) continue;
                if (_hugeGiftRoot != null && (c.transform == _hugeGiftRoot || c.transform.IsChildOf(_hugeGiftRoot))) continue;
                Rigidbody body = c.attachedRigidbody;
                if (body != null && !body.isKinematic) continue; // loose props are not door frames
                return true;
            }
            return false;
        }

        /// <summary>
        /// Veto the parts of this frame's move/turn that would push the bike into geometry:
        /// full -> move without turning -> turn without moving -> neither. Modifies _planarVelocity and facingRot.
        /// </summary>
        private void ResolveHugeGiftMotion(Vector3 pos, Quaternion currentRot, ref Quaternion facingRot, float dt,
            ref bool moveBlocked)
        {
            Vector3 planarDelta = _planarVelocity * dt;
            bool moving = planarDelta.sqrMagnitude > 1e-8f;
            bool turning = Quaternion.Angle(currentRot, facingRot) > 0.01f;
            if (!moving && !turning) return;

            Vector3 target = pos + planarDelta;
            if (!HugeBoxOverlaps(moving ? target : pos, facingRot)) return;

            _hugeGiftBlocked = true;
            if (moving && turning)
            {
                if (!HugeBoxOverlaps(target, currentRot)) { facingRot = currentRot; return; }
                if (!HugeBoxOverlaps(pos, facingRot)) { _planarVelocity = Vector3.zero; moveBlocked = true; return; }
            }

            facingRot = currentRot;
            if (moving)
            {
                _planarVelocity = Vector3.zero;
                moveBlocked = true;
            }
        }

        private void OnHugeGiftScrape()
        {
            if (_hugeScrapeTimer <= 0f)
            {
                _hugeScrapeTimer = hugeScrapeCooldown;
                RaiseNoise(hugeScrapeNoise, NoiseSource.GiftImpact);
            }
            if (_hugeToastTimer <= 0f && !string.IsNullOrEmpty(hugeStuckToast))
            {
                _hugeToastTimer = hugeStuckToastCooldown;
                GameEvents.RaiseToast(hugeStuckToast);
            }
        }

        // ---------------- ledge grab & mantle ----------------

        /// <summary>Loose light dynamic bodies (toys, ragdoll limbs) are never walls, ledges or ceilings.</summary>
        private bool IsLooseLight(Collider c)
        {
            Rigidbody body = c.attachedRigidbody;
            return body != null && !body.isKinematic && body.mass < bumpIgnoreRigidbodyMass;
        }

        private bool IsLedgeBlocker(Collider c) => c != null && !c.isTrigger && !IsSelf(c) && !IsLooseLight(c);

        /// <summary>
        /// Forward capsule cast (knee to chest) along dir finds a near-vertical face we are pushing into; then a
        /// downward ray just past that face must find a walkable top between ledgeMinHeight and ledgeMaxHeight above
        /// the feet, with room for the standing capsule there and room to rise straight up from here.
        /// </summary>
        private bool TryFindLedge(Vector3 dir, out Vector3 standPos, out Vector3 mantleDir)
        {
            standPos = Vector3.zero;
            mantleDir = Vector3.zero;
            if (dir.sqrMagnitude < 0.5f) return false;

            Vector3 feet = transform.position;
            float r = _cc.radius;
            float castR = r * 0.85f;
            float bottom = castR + 0.1f;
            float top = Mathf.Max(bottom, _cc.height - castR);
            Vector3 p1 = feet + Vector3.up * bottom;
            Vector3 p2 = feet + Vector3.up * top;
            float dist = ledgeReach + (r - castR) + _cc.skinWidth;

            int n = Physics.CapsuleCastNonAlloc(p1, p2, castR, dir, _ledgeHits, dist, _ledgeMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = _ledgeHits[i];
                if (!IsLedgeBlocker(hit.collider)) continue;
                if (hit.distance <= 0f || hit.distance >= best) continue; // initial overlaps report distance 0
                if (Mathf.Abs(hit.normal.y) > ledgeMaxWallNormalY) continue;
                Vector3 wn = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (wn.sqrMagnitude < 0.0001f) continue;
                Vector3 into = -wn.normalized;
                if (Vector3.Dot(into, dir) < ledgeMinFacingDot) continue;
                if (!ProbeLedgeTop(hit.point, into, feet, out Vector3 stand)) continue;

                best = hit.distance;
                standPos = stand;
                mantleDir = into;
                found = true;
            }
            return found;
        }

        private bool ProbeLedgeTop(Vector3 facePoint, Vector3 into, Vector3 feet, out Vector3 stand)
        {
            stand = Vector3.zero;
            float r = _cc.radius;
            Vector3 probe = new Vector3(facePoint.x, feet.y, facePoint.z) + into * (r + ledgeInset);
            float startY = feet.y + ledgeMaxHeight + 0.1f;
            Vector3 origin = new Vector3(probe.x, startY, probe.z);

            // Ray starting inside the obstacle = it is taller than ledgeMaxHeight.
            if (Physics.CheckSphere(origin, 0.02f, _ledgeMask, QueryTriggerInteraction.Ignore)) return false;

            float len = startY - (feet.y + ledgeMinHeight) + 0.02f;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, _ledgeProbeHits, len, _ledgeMask, QueryTriggerInteraction.Ignore);
            bool hitTop = false;
            RaycastHit topHit = default;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _ledgeProbeHits[i];
                if (!IsLedgeBlocker(h.collider)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; topHit = h; hitTop = true; }
            }
            if (!hitTop) return false;
            if (topHit.normal.y < ledgeMinTopNormalY) return false;

            float height = topHit.point.y - feet.y;
            if (height < ledgeMinHeight || height > ledgeMaxHeight) return false;
            if (topHit.point.y < facePoint.y - 0.05f) return false; // found something lower beyond the face, not its top

            stand = new Vector3(probe.x, topHit.point.y + mantleClearance, probe.z);
            if (StandingCapsuleBlocked(stand)) return false;

            // Room to rise straight up from where we are now.
            float rise = stand.y - feet.y;
            Vector3 head = feet + Vector3.up * (_cc.height - r);
            int m = Physics.SphereCastNonAlloc(head, r * 0.85f, Vector3.up, _ledgeProbeHits, rise + 0.02f, _ledgeMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
                if (IsLedgeBlocker(_ledgeProbeHits[i].collider)) return false;
            return true;
        }

        private bool StandingCapsuleBlocked(Vector3 feet)
        {
            float r = _cc.radius * 0.95f;
            Vector3 p1 = feet + Vector3.up * (r + 0.01f);
            Vector3 p2 = feet + Vector3.up * Mathf.Max(r + 0.01f, standHeight - r);
            int n = Physics.OverlapCapsuleNonAlloc(p1, p2, r, _ledgeOverlap, _ledgeMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (IsLedgeBlocker(_ledgeOverlap[i])) return true;
            return false;
        }

        private void BeginMantle(Vector3 standPos, Vector3 dir)
        {
            _mantling = true;
            _mantleTimer = 0f;
            _mantleStart = transform.position;
            _mantleEnd = standPos;
            _mantleDir = dir;
            _jetpacking = false;
            _sprinting = false;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _peakFallSpeed = 0f;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _grounded = false;
            RaiseNoise(mantleNoise, NoiseSource.Landing);
            MantleStarted?.Invoke();
        }

        /// <summary>Scripted clamber: rise first, then slide forward onto the ledge (CharacterController.Move each frame
        /// toward the point on the arc, so geometry still blocks it).</summary>
        private void UpdateMantle(float dt)
        {
            if (InputLocked) { EndMantle(false); return; } // knocked limp mid-climb

            _mantleTimer += dt;
            float t = Mathf.Clamp01(_mantleTimer / Mathf.Max(0.05f, mantleDuration));
            float upFrac = Mathf.Clamp(mantleUpFraction, 0.1f, 0.95f);
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / upFrac));
            float fStart = upFrac * 0.7f;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - fStart) / (1f - fStart)));

            Vector3 desired = new Vector3(
                Mathf.Lerp(_mantleStart.x, _mantleEnd.x, f),
                Mathf.Lerp(_mantleStart.y, _mantleEnd.y, u),
                Mathf.Lerp(_mantleStart.z, _mantleEnd.z, f));
            Vector3 delta = desired - transform.position;
            if (delta.sqrMagnitude > 1e-8f) _cc.Move(delta);

            if (_mantleDir.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(_mantleDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeedDegrees * dt);
            }

            if (t >= 1f) EndMantle(true);
        }

        private void EndMantle(bool completed)
        {
            _mantling = false;
            _mantleCooldownTimer = mantleCooldown;
            _verticalVelocity = 0f;
            _peakFallSpeed = 0f;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _planarVelocity = completed ? _mantleDir * mantleExitSpeed : Vector3.zero;
            _grounded = ProbeGround() || _cc.isGrounded;
            if (completed) Mantled?.Invoke();
        }

        // ---------------- stamina ----------------

        private void UpdateStamina(float dt)
        {
            bool draining = false;
            if (_sprinting)
            {
                _stamina -= sprintDrainPerSecond * dt;
                draining = true;
            }
            bool heavyStrain = _carry != null && _carry.IsCarryingHuge && !_carry.IsCoCarrying // shared weight: no strain
                               && (GameServices.Profile == null || GameServices.Profile.GripLevel < 2);
            if (heavyStrain)
            {
                _stamina -= heavyCarryDrainPerSecond * dt;
                draining = true;
            }

            if (draining) _staminaRegenTimer = staminaRegenDelay;
            else if (_staminaRegenTimer > 0f) _staminaRegenTimer -= dt;
            else _stamina += staminaRegenPerSecond * dt;

            _stamina = Mathf.Clamp(_stamina, 0f, maxStamina);
            if (_stamina <= 0f) { _exhausted = true; _sprinting = false; }
            else if (_exhausted && _stamina >= exhaustedRecoverThreshold) _exhausted = false;
        }

        // ---------------- jetpack ----------------

        private void UpdateJetpack(float dt, bool canControl)
        {
            int level = JetpackLevel;
            if (level <= 0)
            {
                _jetpacking = false;
                _fuel01 = 1f;
                return;
            }

            _jetpacking = canControl && _input.JetpackHeld && _fuel01 > 0.01f;
            if (_jetpacking)
            {
                // Higher level = slower burn = effectively a bigger tank.
                float burn = Mathf.Max(jetpackMinBurn, jetpackBaseBurn - level * jetpackBurnReductionPerLevel) / 100f;
                _fuel01 = Mathf.Max(0f, _fuel01 - burn * dt);

                float targetUp = jetpackBaseThrustVelocity + level * jetpackThrustPerLevel;
                float k = 1f - Mathf.Exp(-jetpackResponsiveness * dt);
                _verticalVelocity = Mathf.Lerp(_verticalVelocity, targetUp, k);
                _grounded = false;

                _jetpackNoiseTimer -= dt;
                if (_jetpackNoiseTimer <= 0f)
                {
                    _jetpackNoiseTimer = jetpackNoiseInterval;
                    RaiseNoise(GameConstants.NoiseJetpack, NoiseSource.Jetpack);
                }
            }
            else
            {
                _jetpackNoiseTimer = 0f;
                if (_grounded)
                {
                    float refuel = (jetpackBaseRefuel + level * jetpackRefuelPerLevel) / 100f;
                    _fuel01 = Mathf.Min(1f, _fuel01 + refuel * dt);
                }
            }
        }

        // ---------------- crouch ----------------

        private void UpdateCapsuleHeight(float dt)
        {
            float target = _crouching ? crouchHeight : standHeight;
            float h = Mathf.MoveTowards(_cc.height, target, crouchTransitionSpeed * dt);
            if (!Mathf.Approximately(h, _cc.height))
            {
                _cc.height = h;
                _cc.center = new Vector3(_cc.center.x, h * 0.5f, _cc.center.z);
            }
        }

        private bool HasHeadroom()
        {
            float radius = _cc.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (_cc.height - _cc.radius);
            float dist = Mathf.Max(0.01f, standHeight - _cc.height + 0.05f);
            int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.up, _hits, dist, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null || IsSelf(c)) continue;
                return false;
            }
            return true;
        }

        // ---------------- ground probe & surface ----------------

        private bool IsSelf(Collider c) => c.transform == transform || c.transform.IsChildOf(transform);

        /// <summary>SphereCast down from the bottom of the capsule. Returns true if solid ground is close.
        /// Also updates the current surface (trigger volumes with a SurfaceTag override the floor's own tag).</summary>
        private bool ProbeGround()
        {
            float radius = _cc.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (_cc.radius + 0.05f);
            float dist = 0.05f + _cc.skinWidth + groundProbeDistance;
            int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _hits, dist, groundMask, QueryTriggerInteraction.Collide);

            Collider ground = null;
            float bestDist = float.MaxValue;
            SurfaceTag triggerTag = null;

            for (int i = 0; i < n; i++)
            {
                Collider c = _hits[i].collider;
                if (c == null || IsSelf(c)) continue;
                if (c.isTrigger)
                {
                    if (triggerTag == null)
                    {
                        SurfaceTag t = c.GetComponentInParent<SurfaceTag>();
                        if (t != null) triggerTag = t;
                    }
                    continue;
                }
                if (_hits[i].distance < bestDist)
                {
                    bestDist = _hits[i].distance;
                    ground = c;
                }
            }

            SurfaceTag tag = triggerTag;
            if (tag == null && ground != null)
            {
                if (ground != _lastGroundCollider)
                {
                    _lastGroundCollider = ground;
                    _lastGroundTag = ground.GetComponentInParent<SurfaceTag>();
                }
                tag = _lastGroundTag;
            }

            if (ground != null || triggerTag != null)
            {
                _surfaceTag = tag;
                _surface = tag != null ? tag.Surface : SurfaceType.Default;
            }
            // When airborne keep the last surface (HUD stays stable mid-jump).
            return ground != null;
        }

        private float SurfaceNoiseMultiplier() => _surfaceTag != null ? _surfaceTag.NoiseMultiplier : 1f;

        private float CurrentSlipperiness()
        {
            if (_surfaceTag == null) return 0f;
            float slip = _surfaceTag.Slipperiness;
            if (slip <= 0f) return 0f;
            return Mathf.Clamp01(slip * Mathf.Max(0f, 1f - bootsSlipReductionPerLevel * BootsLevel));
        }

        // ---------------- noise ----------------

        private float BaseStepNoise() =>
            _crouching ? GameConstants.NoiseCrouch : (_sprinting ? GameConstants.NoiseRun : GameConstants.NoiseWalk);

        private float StepLoudness(out NoiseSource source)
        {
            float boots = BootsNoiseFactor;
            float loud = BaseStepNoise() * SurfaceNoiseMultiplier() * boots;
            source = NoiseSource.Footstep;
            if (_surface == SurfaceType.CreakyWood)
            {
                loud += GameConstants.NoiseCreakyFloorBonus * (_crouching ? 0.5f : 1f) * boots;
                source = NoiseSource.CreakyFloor;
            }
            return loud;
        }

        private void UpdateFootsteps(float dt)
        {
            float planarSpeed = _planarVelocity.magnitude;
            if (!_grounded || planarSpeed < minStepSpeed)
            {
                _stepTimer = 0f;
                return;
            }

            float interval = _crouching ? stepIntervalCrouch : (_sprinting ? stepIntervalSprint : stepIntervalWalk);
            _stepTimer += dt;
            if (_stepTimer >= interval)
            {
                _stepTimer = 0f;
                float loud = StepLoudness(out NoiseSource src);
                RaiseNoise(loud, src);
                Footstep?.Invoke(loud);
            }
        }

        private void OnLanded()
        {
            float fall = _peakFallSpeed;
            _peakFallSpeed = 0f;
            Landed?.Invoke(fall);
            if (fall >= 3f) SilentNight.FX.LandingDust.Spawn(transform.position, Mathf.InverseLerp(3f, 12f, fall));
            if (fall < landingMinFallSpeed) return;

            float t = Mathf.InverseLerp(landingMinFallSpeed, landingMaxFallSpeed, fall);
            float loud = GameConstants.NoiseLanding * Mathf.Lerp(0.35f, 1f, t) * SurfaceNoiseMultiplier() * BootsNoiseFactor;
            if (_surface == SurfaceType.CreakyWood) loud += GameConstants.NoiseCreakyFloorBonus * BootsNoiseFactor;
            RaiseNoise(loud, NoiseSource.Landing);
        }

        // ---------------- stumble ----------------

        /// <summary>Called by the CharacterController during Move: push loose bodies we walk into and remember the
        /// hardest wall impact of this move.</summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!_collectHits || hit.collider == null || IsSelf(hit.collider)) return;
            Rigidbody body = hit.rigidbody;
            if (pushBodies && body != null && !body.isKinematic) TryPushBody(hit, body);

            if (Mathf.Abs(hit.normal.y) > bumpMaxWallNormalY) return;               // floor / ceiling / slope
            if (body != null && !body.isKinematic
                && (body.mass < bumpIgnoreRigidbodyMass || body.GetComponent<HingeJoint>() != null))
                return; // loose light prop, or a hinged door that swings away (no faceplant)

            Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (n.sqrMagnitude < 0.0001f) return;
            float into = Vector3.Dot(_preMovePlanar, -n.normalized);
            if (into > _bestBumpSpeed) _bestBumpSpeed = into;
        }

        /// <summary>
        /// Shove a non-kinematic body (door leaf, toy, loose gift) at the contact point along the planar move direction:
        /// a continuous push from the wished speed plus a one-off impact impulse on first contact. Impulses (not velocity
        /// changes) so heavier bodies move less; capped so the contact point never outruns the elf.
        /// </summary>
        private void TryPushBody(ControllerColliderHit hit, Rigidbody body)
        {
            if (body == _pushedThisMove) return;                      // several contacts with one body in one Move
            if (hit.moveDirection.y < -0.3f) return;                  // standing on it, not walking into it
            if (IsPuppetBody(body) || IsHeldBody(body)) return;

            Vector3 dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            // Only the part of the move that goes into the surface pushes (sliding along a door leaf doesn't).
            Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z);
            float facing = n.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(dir, -n.normalized)) : 1f;
            if (facing <= 0.01f) return;

            float approach = Mathf.Max(0f, Vector3.Dot(_preMovePlanar, dir)) * facing;
            float wish = Mathf.Max(approach, Mathf.Max(0f, Vector3.Dot(_pushWishVelocity, dir)) * facing);
            if (wish <= 0.01f) return;

            float pointSpeed = Vector3.Dot(body.GetPointVelocity(hit.point), dir);
            float maxSpeed = wish * Mathf.Max(0.1f, pushMaxSpeedFactor);
            if (pointSpeed >= maxSpeed) return;                        // already moving away faster than we push

            float now = Time.time;
            bool newContact = body != _lastPushBody || now - _lastPushTime > pushContactMemory;
            _lastPushBody = body;
            _lastPushTime = now;
            _pushedThisMove = body;

            float impulse = pushStrength * wish * Time.deltaTime;
            if (newContact) impulse += pushImpactStrength * Mathf.Max(0f, approach - pushImpactFreeSpeed);
            impulse = Mathf.Min(Mathf.Min(impulse, pushMaxImpulse), (maxSpeed - pointSpeed) * body.mass);
            if (impulse <= 0f) return;

            body.WakeUp();
            body.AddForceAtPosition(dir * impulse, hit.point, ForceMode.Impulse);
        }

        /// <summary>Active-ragdoll limbs live on a separate root object named "ElfRagdoll" (see ElfRagdoll.Build).</summary>
        private static bool IsPuppetBody(Rigidbody body)
        {
            Transform root = body.transform.root;
            return root != null && root.name.StartsWith("ElfRagdoll", StringComparison.Ordinal);
        }

        private bool IsHeldBody(Rigidbody body)
        {
            if (_carry != null && _carry.IsHoldingSomething)
            {
                GameObject held = _carry.Held.GameObject;
                if (held != null && (body.transform == held.transform || body.transform.IsChildOf(held.transform)))
                    return true;
            }
            SilentNight.Gifts.Gift gift = body.GetComponentInParent<SilentNight.Gifts.Gift>();
            return gift != null && gift.IsHeld;
        }

        private void TriggerBump(float speedIntoWall)
        {
            _bumpCooldownTimer = bumpCooldown;
            _sprinting = false;
            RaiseNoise(bumpNoise, NoiseSource.Landing);
            if (!string.IsNullOrEmpty(bumpToast)) GameEvents.RaiseToast(bumpToast);
            Bumped?.Invoke(speedIntoWall);
        }

        private void UpdateNoiseLevel(float dt)
        {
            float target;
            if (_jetpacking)
                target = GameConstants.NoiseJetpack;
            else if (_grounded && _planarVelocity.magnitude >= minStepSpeed)
                target = StepLoudness(out _) * 0.85f;
            else
                target = GameConstants.NoiseIdle * (_crouching ? 0.6f : 1f);

            float k = 1f - Mathf.Exp(-noiseSmoothing * dt);
            _currentNoise = Mathf.Clamp(Mathf.Lerp(_currentNoise, target, k), 0f, 100f);
        }

        private void RaiseNoise(float loudness, NoiseSource source)
        {
            loudness = Mathf.Clamp(loudness, 0f, 100f);
            if (loudness > _currentNoise) _currentNoise = loudness; // spike, then decays smoothly
            GameEvents.RaiseNoise(new NoiseEvent(transform.position, loudness, source, gameObject));
        }
    }
}
