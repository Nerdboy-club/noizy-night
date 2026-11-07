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
        [SerializeField] private float walkSpeed = 2.5f;
        [SerializeField] private float sprintSpeed = 3.8f;
        [SerializeField] private float crouchSpeed = 1.4f;
        [SerializeField] private float groundAcceleration = 22f;
        [SerializeField] private float airAcceleration = 9f;
        [Tooltip("Acceleration factor on a fully slippery surface (Slipperiness = 1).")]
        [SerializeField] private float slipperyAccelerationFactor = 0.08f;
        [Tooltip("Each SilentBoots level reduces slipperiness by this fraction.")]
        [SerializeField] private float bootsSlipReductionPerLevel = 0.15f;
        [SerializeField] private float turnSpeedDegrees = 720f;
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
        [SerializeField] private float stepIntervalWalk = 0.42f;
        [SerializeField] private float stepIntervalSprint = 0.3f;
        [SerializeField] private float stepIntervalCrouch = 0.55f;
        [SerializeField] private float minStepSpeed = 0.6f;
        [Tooltip("Each SilentBoots level reduces footstep noise by this fraction.")]
        [SerializeField] private float bootsNoiseReductionPerLevel = 0.15f;
        [SerializeField] private float landingMinFallSpeed = 4f;
        [SerializeField] private float landingMaxFallSpeed = 14f;
        [SerializeField] private float noiseSmoothing = 3f;

        [Header("Stumble (sprinting into a wall)")]
        [Tooltip("Speed INTO the wall (m/s) needed to faceplant. Walk 2.5, sprint 3.8.")]
        [SerializeField] private float bumpMinSpeed = 3.3f;
        [Tooltip("Hits with |normal.y| above this are floors/ceilings/slopes, not walls.")]
        [SerializeField] private float bumpMaxWallNormalY = 0.5f;
        [Tooltip("A bump also needs the move to lose at least this fraction of the planar speed (filters glancing slides and steps).")]
        [SerializeField] private float bumpMinSpeedLoss = 0.4f;
        [Tooltip("Loose dynamic bodies lighter than this (gifts, toys) never cause a faceplant.")]
        [SerializeField] private float bumpIgnoreRigidbodyMass = 5f;
        [SerializeField] private float bumpCooldown = 2.5f;
        [SerializeField] private float bumpNoise = 45f;
        [SerializeField] private string bumpToast = "Бум! Эльф врезался в стену.";

        [Header("Huge gift (wrapped bike) clearance")]
        [Tooltip("The held bike's box is shrunk by this much on every side before the overlap test (lets it graze walls).")]
        [SerializeField] private float hugeClearanceSkin = 0.02f;
        [Tooltip("Ignore the bottom of the bike box below feet + max(this, stepOffset): floor, rugs and low steps never block it.")]
        [SerializeField] private float hugeFloorClearance = 0.2f;
        [SerializeField] private float hugeScrapeNoise = 35f;
        [SerializeField] private float hugeScrapeCooldown = 0.6f;
        [SerializeField] private float hugeStuckToastCooldown = 4f;
        [SerializeField] private string hugeStuckToast = "Велосипед застрял в проёме! Разверните его боком.";

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

        // ---------------- events (for animator / visuals / audio inside the Player module) ----------------
        public event Action Jumped;
        /// <summary>Arg: fall speed at impact (m/s).</summary>
        public event Action<float> Landed;
        public event Action Teleported;
        /// <summary>Arg: loudness of the footstep noise raised.</summary>
        public event Action<float> Footstep;
        /// <summary>Elf sprinted face-first into a wall/obstacle. Arg: speed into the wall (m/s). ElfRagdoll goes limp.</summary>
        public event Action<float> Bumped;

        // ---------------- public read API ----------------
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

        private void OnEnable() => GameServices.RegisterPlayer(this, isLocal: true);

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
            _sprinting = canControl && wantsMove && _input.SprintHeld && !_crouching && !_exhausted && _stamina > 0f;
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
            if (_carry != null && _carry.IsCarryingHuge && SnapshotHugeBox())
            {
                hugeActive = true;
                hugeWedged = HugeBoxOverlaps(preMovePos, currentRot);
                // Already wedged (e.g. picked up inside a frame): don't veto anything so the player can wriggle free.
                if (!hugeWedged) ResolveHugeGiftMotion(preMovePos, currentRot, ref facingRot, dt, ref hugeMoveBlocked);
            }

            _preMovePlanar = _planarVelocity;
            _bestBumpSpeed = 0f;
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
            UpdateFootsteps(dt);
            UpdateNoiseLevel(dt);
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

        // ---------------- stamina ----------------

        private void UpdateStamina(float dt)
        {
            bool draining = false;
            if (_sprinting)
            {
                _stamina -= sprintDrainPerSecond * dt;
                draining = true;
            }
            bool heavyStrain = _carry != null && _carry.IsCarryingHuge
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

        /// <summary>Called by the CharacterController during Move: remember the hardest wall impact of this move.</summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!_collectHits || hit.collider == null || IsSelf(hit.collider)) return;
            if (Mathf.Abs(hit.normal.y) > bumpMaxWallNormalY) return;               // floor / ceiling / slope
            Rigidbody body = hit.rigidbody;
            if (body != null && !body.isKinematic && body.mass < bumpIgnoreRigidbodyMass) return; // loose light prop

            Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z);
            if (n.sqrMagnitude < 0.0001f) return;
            float into = Vector3.Dot(_preMovePlanar, -n.normalized);
            if (into > _bestBumpSpeed) _bestBumpSpeed = into;
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
