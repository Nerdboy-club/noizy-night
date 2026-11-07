using UnityEngine;
using UnityEngine.AI;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Grumpy grandpa. Sleeps in bed (initial transform), hears GameEvents.Noise into an awareness meter,
    /// sits up when suspicious, gets up and searches with a flashlight, chases when he sees the elf,
    /// and raises GameEvents.PlayerCaught on contact. Broadcasts GameEvents.GrandpaAlertChanged.
    /// Only thinks while GameServices.Flow == null or Flow.State == Delivery.
    /// </summary>
    [DisallowMultipleComponent]
    public class Grandpa : MonoBehaviour
    {
        private enum Phase { InBed, GettingUp, Investigating, LookingAround, Patrolling, Returning, LyingDown, Chasing, Caught }
        private enum AfterGetUp { Investigate, Patrol, Chase }

        [Header("References (auto-found if empty)")]
        [SerializeField] private NPCVisual visual;
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private VisionCone vision;
        [Tooltip("Vision origin. Follows the visual's head every frame; created if missing.")]
        [SerializeField] private Transform eyes;
        [SerializeField] private Light flashlight;
        [SerializeField] private Collider bodyCollider;

        [Header("Hearing (awareness 0..100)")]
        [SerializeField] private float hearingFloor = 4f;
        [Tooltip("Awareness per second per unit of heard loudness above the floor.")]
        [SerializeField] private float awarenessGain = 0.6f;
        [Tooltip("How fast the 'recently heard' envelope fades (loudness/s).")]
        [SerializeField] private float heardEnvelopeDecay = 25f;
        [SerializeField] private float stirThreshold = 15f;
        [SerializeField] private float suspiciousThreshold = 40f;
        [SerializeField] private float searchThreshold = 70f;
        [SerializeField] private float hysteresis = 6f;
        [SerializeField] private float decayAsleep = 4f;
        [SerializeField] private float decayStirring = 2.5f;
        [SerializeField] private float decaySuspicious = 2f;
        [Tooltip("Hearing multiplier while he is out of bed and alert.")]
        [SerializeField] private float awakeHearingMultiplier = 1.5f;
        [Tooltip("Heard loudness that makes a searching grandpa walk to the new sound.")]
        [SerializeField] private float reinvestigateLoudness = 14f;

        [Header("Spooky house")]
        [SerializeField] private float spookyHearingMultiplier = 1.35f;
        [SerializeField] private float spookyDecayMultiplier = 0.7f;
        [SerializeField, Range(0f, 1f)] private float spookyDarkness = 0.65f;

        [Header("Vision (detection 0..1)")]
        [SerializeField] private float detectRateSuspicious = 0.6f;
        [SerializeField] private float detectRateSearching = 1.4f;
        [SerializeField] private float detectDecay = 0.5f;
        [SerializeField] private float loseSightTime = 3.5f;
        [SerializeField] private float eyePitch = 8f;

        [Header("Movement")]
        [SerializeField] private float searchSpeed = 1.2f;
        [SerializeField] private float patrolSpeed = 1.0f;
        [SerializeField] private float chaseSpeed = 3.2f;
        [SerializeField] private float turnSpeed = 240f;
        [SerializeField] private float arriveDistance = 0.6f;
        [SerializeField] private float chaseRepathInterval = 0.2f;
        [Tooltip("Where he steps out of bed: bed position + bed forward * this.")]
        [SerializeField] private float standOffset = 1.0f;
        [SerializeField] private float getUpTime = 1.6f;
        [SerializeField] private float quickGetUpTime = 0.6f;
        [SerializeField] private float lieDownTime = 1.2f;

        [Header("Searching")]
        [Tooltip("Seconds of the visible look-around at the noise spot before he gives up.")]
        [SerializeField] private float lookAroundDuration = 3f;
        [Tooltip("Yaw swing (deg) to each side during the look-around.")]
        [SerializeField] private float lookAroundAngle = 70f;
        [Tooltip("Left+right sweeps during the look-around.")]
        [SerializeField, Min(1)] private int lookAroundSweeps = 2;
        [SerializeField] private float searchTimeout = 20f;
        [SerializeField] private float returnTimeout = 25f;

        [Header("Catching")]
        [SerializeField] private float catchDistance = 1.0f;
        [Tooltip("A hidden (crouching under furniture) elf can only be caught closer than this.")]
        [SerializeField] private float hiddenCatchDistance = 0.6f;
        [SerializeField] private float catchHeight = 1.6f;

        [Header("Patrol / restless sleep (optional)")]
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private float patrolPause = 2f;
        [SerializeField] private float patrolTimeout = 45f;
        [Tooltip("Random seconds between night trips (only with patrol points).")]
        [SerializeField] private Vector2 restlessWakeInterval = new Vector2(45f, 80f);
        [SerializeField] private float spookyRestlessMultiplier = 0.6f;

        [Header("Flashlight")]
        [SerializeField] private float flashlightIntensity = 6f;   // URP non-physical intensity; tune in scene
        [SerializeField] private float flashlightChaseIntensity = 10f;

        [Header("Readability")]
        [Tooltip("World-space Zzz / ? / ! marker above his head plus a short word for what he heard.")]
        [SerializeField] private bool showAlertIndicator = true;
        [Tooltip("Looping 3D snore while he sleeps (procedural clip).")]
        [SerializeField] private bool snoreAudio = true;
        [SerializeField, Range(0f, 1f)] private float snoreVolume = 0.6f;
        [SerializeField] private float snoreMaxDistance = 10f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        private static readonly string[] CatchLines =
        {
            "Ах вы, мелкие воришки! Брысь отсюда!",
            "Опять эльфы?! Где мой тапок!",
            "Попался, ушастый! Сейчас как дам валенком!",
            "Кто тут шуршит в моём доме?! А ну стоять!",
            "Я же говорил бабке — эльфы существуют!",
            "Ну всё, звоню Санте! Пусть забирает своих бандитов!"
        };

        private static readonly string[] ThrowOutGrumbles =
        {
            "бу-бу-бу… спать!",
            "и не возвращайся!",
            "развелось ушастых…"
        };

        private const string GiveUpToast = "Показалось… Спать, спать.";
        private const string GiveUpWord = "показалось…";

        // ---------------- public read-only ----------------
        public AlertLevel Level => _level;
        public float Awareness => _awareness;
        public float Detection01 => Mathf.Clamp01(_detect);
        /// <summary>World position of his head (follows the lying / sitting / standing pose). Used by the alert indicator.</summary>
        public Vector3 IndicatorAnchor => HeadPosition;
        /// <summary>Co-op: how many times an elf was caught and thrown back into the fireplace instead of a game over.</summary>
        public int ElvesThrownOut { get; private set; }
        private bool _suppressLevelToast;

        private GrandpaAlertIndicator _indicator;
        private GrandpaSnore _snore;
        private bool _grumbledThisTrip;

        // ---------------- state ----------------
        private NavMover _mover;
        private Phase _phase = Phase.InBed;
        private AfterGetUp _afterGetUp;
        private AlertLevel _level = AlertLevel.Asleep;
        private AlertLevel _bedLevel = AlertLevel.Asleep;
        private bool _levelBroadcast;
        private bool _initialized;
        private bool _spooky;
        private bool _quietWake;
        private bool _caughtRaised;

        private float _awareness;
        private float _heard;
        private float _detect;
        private Vector3 _lastNoisePos;
        private bool _hasNoisePos;
        private bool _pendingInvestigate;
        private Vector3 _pendingNoisePos;
        private Vector3 _lastSeenPos;
        private float _timeSinceSeen = 999f;

        private Vector3 _bedPos;
        private Quaternion _bedRot;
        private Vector3 _standPoint;
        private bool _standOnNavMesh;

        private float _phaseTimer;
        private float _phaseDuration;
        private float _searchTimer;
        private float _restlessTimer;
        private float _repathTimer;
        private float _lookBaseYaw;
        private int _patrolIndex;
        private float _patrolPauseTimer;
        private Vector3 _transitionFrom;
        private Quaternion _transitionFromRot;
        private float _flashCurrent;

        private bool OutOfBed => _phase != Phase.InBed && _phase != Phase.LyingDown;
        private bool HasPatrol => patrolPoints != null && patrolPoints.Length > 0;

        private Vector3 HeadPosition
        {
            get
            {
                if (visual != null && visual.Head != null) return visual.Head.position;
                return transform.position + Vector3.up * 1.6f;
            }
        }

        // ---------------- configuration (factory / scene builder) ----------------

        public void Configure(NPCVisual npcVisual, NavMeshAgent navAgent, VisionCone visionCone, Transform eyeTransform, Light flashLight, Collider body)
        {
            visual = npcVisual;
            agent = navAgent;
            vision = visionCone;
            eyes = eyeTransform;
            flashlight = flashLight;
            bodyCollider = body;
        }

        public void SetPatrolPoints(Transform[] points) => patrolPoints = points;

        /// <summary>External scare (e.g. spooky event / alarm clock): treat as a loud noise at pos.</summary>
        public void Alert(Vector3 position, float awarenessBoost = 100f)
        {
            if (!_initialized || _phase == Phase.Caught) return;
            _lastNoisePos = position;
            _hasNoisePos = true;
            _awareness = Mathf.Clamp(_awareness + awarenessBoost, 0f, 100f);
            if (OutOfBed && _phase != Phase.Chasing) { _pendingInvestigate = true; _pendingNoisePos = position; }
            if (_indicator != null && _phase != Phase.Chasing) _indicator.ShowWord("что это?!", 2f);
        }

        // ---------------- lifecycle ----------------

        private void OnEnable() => GameEvents.Noise += OnNoise;
        private void OnDisable() => GameEvents.Noise -= OnNoise;

        private void OnDestroy()
        {
            if (_indicator != null) Destroy(_indicator.gameObject);
        }

        private void Start() => Init();

        private void Init()
        {
            if (_initialized) return;

            if (visual == null) visual = GetComponent<NPCVisual>();
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (vision == null) vision = GetComponent<VisionCone>();
            if (bodyCollider == null) bodyCollider = GetComponent<Collider>();
            if (visual != null) visual.EnsureBuilt();
            if (eyes == null)
            {
                var e = new GameObject("Eyes").transform;
                e.SetParent(transform, false);
                e.localPosition = new Vector3(0f, 1.6f, 0.15f);
                eyes = e;
            }
            if (vision != null && vision.Eye == vision.transform) vision.Configure(eyes, vision.FovAngle, vision.ViewRange);
            if (flashlight == null) flashlight = GetComponentInChildren<Light>(true);

            _bedPos = transform.position;
            _bedRot = transform.rotation;
            _spooky = StealthUtil.IsSpookyHouse;
            if (_spooky && vision != null) vision.Darkness = Mathf.Max(vision.Darkness, spookyDarkness);

            _mover = new NavMover(transform, agent)
            {
                AllowManualFallback = true,
                TurnSpeed = turnSpeed,
                ArriveDistance = arriveDistance
            };

            Vector3 probe = _bedPos + _bedRot * Vector3.forward * standOffset;
            _standOnNavMesh = NavMover.Sample(probe, 2.5f, out _standPoint) || NavMover.Sample(_bedPos, 3f, out _standPoint);
            if (!_standOnNavMesh) _standPoint = probe;

            _mover.Disable(); // in bed: agent off (bed is usually carved out of the NavMesh)
            if (bodyCollider != null) bodyCollider.enabled = false;
            if (flashlight != null) { flashlight.intensity = 0f; flashlight.enabled = false; }

            ResetRestless();
            _initialized = true;
            UpdateEyes();
            UpdateVisual();
            if (visual != null) visual.SnapPose();
            SetLevel(AlertLevel.Asleep);
            if (showAlertIndicator && _indicator == null) _indicator = GrandpaAlertIndicator.Create(this);
            if (snoreAudio && _snore == null) _snore = GrandpaSnore.Create(transform, snoreVolume, snoreMaxDistance);
        }

        // ---------------- hearing ----------------

        private void OnNoise(NoiseEvent e)
        {
            if (!_initialized || _phase == Phase.Caught || !StealthUtil.IsGameplayActive) return;
            if (e.Emitter != null && e.Emitter == gameObject) return;

            float mul = (_spooky ? spookyHearingMultiplier : 1f) * (OutOfBed ? awakeHearingMultiplier : 1f);
            float heard = e.LoudnessAt(HeadPosition) * mul;
            if (heard < hearingFloor) return;

            if (heard >= _heard) { _lastNoisePos = e.Position; _hasNoisePos = true; }
            _heard = Mathf.Max(_heard, heard);

            // Readability: tell the player WHAT he heard (he already knows where the elf is while chasing).
            if (_indicator != null && _phase != Phase.Chasing) _indicator.ShowHeard(e.Source, heard);

            // A single loud bang wakes him straight up.
            if (heard >= GameConstants.GrandpaWakeThreshold)
                _awareness = Mathf.Max(_awareness, searchThreshold);

            if (OutOfBed && _phase != Phase.Chasing && heard >= reinvestigateLoudness)
            {
                _pendingInvestigate = true;
                _pendingNoisePos = e.Position;
            }
        }

        // ---------------- main loop ----------------

        private void Update()
        {
            if (!_initialized) return;
            if (!StealthUtil.IsGameplayActive)
            {
                _mover.Halt();
                if (_snore != null) _snore.Tick(0f, false, false, true, HeadPosition);
                return;
            }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateEyes();

            _heard = Mathf.MoveTowards(_heard, 0f, heardEnvelopeDecay * dt);
            if (_heard > hearingFloor) _awareness += (_heard - hearingFloor) * awarenessGain * dt;

            UpdateVision(dt);

            if (_pendingInvestigate)
            {
                _pendingInvestigate = false;
                if (_phase == Phase.Investigating || _phase == Phase.LookingAround || _phase == Phase.Patrolling || _phase == Phase.Returning)
                {
                    _searchTimer = Mathf.Min(_searchTimer, searchTimeout * 0.5f);
                    if (_phase != Phase.Investigating) _quietWake = false;
                    StartInvestigate(_pendingNoisePos);
                }
            }

            _phaseTimer += dt;
            switch (_phase)
            {
                case Phase.InBed: TickInBed(dt); break;
                case Phase.GettingUp: TickGettingUp(dt); break;
                case Phase.Investigating: TickInvestigating(dt); break;
                case Phase.LookingAround: TickLookingAround(dt); break;
                case Phase.Patrolling: TickPatrolling(dt); break;
                case Phase.Returning: TickReturning(dt); break;
                case Phase.LyingDown: TickLyingDown(); break;
                case Phase.Chasing: TickChasing(dt); break;
                case Phase.Caught: break;
            }

            _awareness = Mathf.Clamp(_awareness, 0f, 100f);
            UpdateFlashlight(dt);
            UpdateVisual();
            SetLevel(ComputeLevel());

            if (_snore != null)
            {
                bool snoring = _phase == Phase.InBed && _bedLevel <= AlertLevel.Stirring;
                _snore.Tick(dt, snoring, _bedLevel == AlertLevel.Stirring, false, HeadPosition);
            }
        }

        private void UpdateEyes()
        {
            if (eyes == null) return;
            eyes.SetPositionAndRotation(HeadPosition, transform.rotation * Quaternion.Euler(eyePitch, 0f, 0f));
        }

        private void UpdateVision(float dt)
        {
            float rate;
            switch (_phase)
            {
                case Phase.InBed: rate = _bedLevel == AlertLevel.Suspicious ? detectRateSuspicious : 0f; break;
                case Phase.GettingUp: rate = detectRateSuspicious; break;
                case Phase.LyingDown:
                case Phase.Caught: rate = 0f; break;
                default: rate = detectRateSearching; break;
            }

            if (rate <= 0f || vision == null)
            {
                _detect = Mathf.MoveTowards(_detect, 0f, detectDecay * dt);
                _timeSinceSeen += dt;
                return;
            }

            vision.FlashlightOn = _flashCurrent > 0.1f;
            float vis = vision.Evaluate();
            if (vis > 0f)
            {
                _detect = Mathf.Min(1f, _detect + vis * rate * dt);
                _lastSeenPos = vision.LastTargetPoint;
                _timeSinceSeen = 0f;
                _awareness = Mathf.Max(_awareness, suspiciousThreshold);
            }
            else
            {
                _detect = Mathf.MoveTowards(_detect, 0f, detectDecay * dt);
                _timeSinceSeen += dt;
            }

            if (_detect >= 1f && _phase != Phase.Chasing && _phase != Phase.Caught) StartChase();
        }

        // ---------------- phases ----------------

        private void TickInBed(float dt)
        {
            float decay = _bedLevel == AlertLevel.Asleep ? decayAsleep : _bedLevel == AlertLevel.Stirring ? decayStirring : decaySuspicious;
            if (_spooky) decay *= spookyDecayMultiplier;
            _awareness -= decay * dt;

            // Level with hysteresis (rise immediately, drop one step at a time).
            AlertLevel target = _awareness >= suspiciousThreshold ? AlertLevel.Suspicious
                              : _awareness >= stirThreshold ? AlertLevel.Stirring : AlertLevel.Asleep;
            if (target > _bedLevel) _bedLevel = target;
            else if (target < _bedLevel)
            {
                float thr = _bedLevel == AlertLevel.Suspicious ? suspiciousThreshold : stirThreshold;
                if (_awareness < thr - hysteresis) _bedLevel = (AlertLevel)((int)_bedLevel - 1);
            }

            if (_bedLevel == AlertLevel.Suspicious && _hasNoisePos)
                _mover.FaceTowards(_lastNoisePos, dt, 0.4f);
            else if (_bedLevel == AlertLevel.Asleep)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, _bedRot, turnSpeed * 0.3f * dt);

            if (_awareness >= searchThreshold)
            {
                BeginGetUp(AfterGetUp.Investigate, getUpTime, false);
                return;
            }

            if (_bedLevel == AlertLevel.Asleep && HasPatrol)
            {
                _restlessTimer -= dt;
                if (_restlessTimer <= 0f) BeginGetUp(AfterGetUp.Patrol, getUpTime, true);
            }
        }

        private void BeginGetUp(AfterGetUp after, float duration, bool quiet)
        {
            _afterGetUp = after;
            _quietWake = quiet;
            _phase = Phase.GettingUp;
            _phaseTimer = 0f;
            _phaseDuration = Mathf.Max(0.05f, duration);
            _transitionFrom = transform.position;
            _transitionFromRot = transform.rotation;
            _searchTimer = 0f;
            _grumbledThisTrip = false;
            if (bodyCollider != null) bodyCollider.enabled = true;
        }

        private void TickGettingUp(float dt)
        {
            float t = _phaseTimer / _phaseDuration;
            transform.position = Vector3.Lerp(_transitionFrom, _standPoint, StealthUtil.SmoothStep01(t));

            if (_afterGetUp == AfterGetUp.Chase && StealthUtil.TryGetPlayer(out Transform pt, out _))
                _mover.FaceTowards(pt.position, dt, 1f);
            else if (_afterGetUp == AfterGetUp.Investigate && _hasNoisePos)
                _mover.FaceTowards(_lastNoisePos, dt, 0.6f);

            if (t < 1f) return;

            if (_standOnNavMesh) _mover.EnableAt(_standPoint);
            else transform.position = _standPoint;

            switch (_afterGetUp)
            {
                case AfterGetUp.Chase: EnterChase(); break;
                case AfterGetUp.Patrol: StartPatrol(); break;
                default:
                    StartInvestigate(_hasNoisePos ? _lastNoisePos : _standPoint + transform.forward * 2f);
                    break;
            }
        }

        private void StartInvestigate(Vector3 noisePos)
        {
            _phase = Phase.Investigating;
            _phaseTimer = 0f;
            NavMover.Sample(noisePos, 2.5f, out Vector3 target);
            _mover.SetTarget(target, searchSpeed);
        }

        private void TickInvestigating(float dt)
        {
            _searchTimer += dt;
            if (_mover.Tick(dt)) { StartLookAround(); return; }
            if (_searchTimer > searchTimeout) GiveUpSearch();
        }

        private void StartLookAround()
        {
            _phase = Phase.LookingAround;
            _phaseTimer = 0f;
            _mover.Stop();
            _lookBaseYaw = transform.eulerAngles.y;
        }

        /// <summary>
        /// Readable look-around at the noise spot: swings left then right (lookAroundSweeps times, ±lookAroundAngle)
        /// over lookAroundDuration and ends facing the original direction. The vision cone follows (eyes = body yaw),
        /// so it visibly sweeps the room. Always plays to the end unless a new noise or a sighting interrupts it.
        /// </summary>
        private void TickLookingAround(float dt)
        {
            _searchTimer += dt;
            float dur = Mathf.Max(0.5f, lookAroundDuration);
            float u = Mathf.Clamp01(_phaseTimer / dur);
            // Negative yaw = turn left first; whole sine cycles so he ends where he started.
            float offset = -Mathf.Sin(u * Mathf.PI * 2f * Mathf.Max(1, lookAroundSweeps)) * lookAroundAngle;
            Quaternion look = Quaternion.Euler(0f, _lookBaseYaw + offset, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * 2f * dt);
            if (_phaseTimer >= dur) GiveUpSearch();
        }

        /// <summary>Nothing found: grumble once per trip out of bed, then use the normal return-to-bed path.</summary>
        private void GiveUpSearch()
        {
            if (!_grumbledThisTrip)
            {
                _grumbledThisTrip = true;
                GameEvents.RaiseToast(GiveUpToast);
                if (_indicator != null) _indicator.ShowWord(GiveUpWord, 2.5f);
            }
            StartReturn();
        }

        private void StartPatrol()
        {
            _phase = Phase.Patrolling;
            _phaseTimer = 0f;
            _patrolIndex = 0;
            _patrolPauseTimer = 0f;
            GoToPatrolPoint();
        }

        private void GoToPatrolPoint()
        {
            while (HasPatrol && _patrolIndex < patrolPoints.Length && patrolPoints[_patrolIndex] == null) _patrolIndex++;
            if (!HasPatrol || _patrolIndex >= patrolPoints.Length) { StartReturn(); return; }
            NavMover.Sample(patrolPoints[_patrolIndex].position, 2f, out Vector3 target);
            _mover.SetTarget(target, patrolSpeed);
        }

        private void TickPatrolling(float dt)
        {
            _searchTimer += dt;
            if (_searchTimer > patrolTimeout) { StartReturn(); return; }

            if (_patrolPauseTimer > 0f)
            {
                _patrolPauseTimer -= dt;
                if (_patrolPauseTimer <= 0f)
                {
                    _patrolIndex++;
                    GoToPatrolPoint();
                }
                return;
            }

            if (_mover.Tick(dt))
            {
                _mover.Stop();
                _patrolPauseTimer = patrolPause;
            }
        }

        private void StartReturn()
        {
            _phase = Phase.Returning;
            _phaseTimer = 0f;
            _mover.SetTarget(_standPoint, searchSpeed);
        }

        private void TickReturning(float dt)
        {
            if (_mover.Tick(dt) || _phaseTimer > returnTimeout) BeginLyingDown();
        }

        private void BeginLyingDown()
        {
            _mover.Disable();
            _phase = Phase.LyingDown;
            _phaseTimer = 0f;
            _transitionFrom = transform.position;
            _transitionFromRot = transform.rotation;
            if (bodyCollider != null) bodyCollider.enabled = false;
        }

        private void TickLyingDown()
        {
            float t = StealthUtil.SmoothStep01(_phaseTimer / Mathf.Max(0.05f, lieDownTime));
            transform.SetPositionAndRotation(Vector3.Lerp(_transitionFrom, _bedPos, t), Quaternion.Slerp(_transitionFromRot, _bedRot, t));
            if (t >= 1f) EnterBed();
        }

        private void EnterBed()
        {
            _phase = Phase.InBed;
            _phaseTimer = 0f;
            transform.SetPositionAndRotation(_bedPos, _bedRot);
            _awareness = _quietWake ? 0f : stirThreshold + 5f; // grumbles for a while after a search
            _bedLevel = _awareness >= stirThreshold ? AlertLevel.Stirring : AlertLevel.Asleep;
            _detect = 0f;
            _heard = 0f;
            _pendingInvestigate = false;
            _quietWake = false;
            ResetRestless();
        }

        private void StartChase()
        {
            switch (_phase)
            {
                case Phase.InBed: BeginGetUp(AfterGetUp.Chase, quickGetUpTime, false); break;
                case Phase.GettingUp: _afterGetUp = AfterGetUp.Chase; _quietWake = false; break;
                case Phase.LyingDown:
                case Phase.Caught:
                case Phase.Chasing: break;
                default: EnterChase(); break;
            }
        }

        private void EnterChase()
        {
            _phase = Phase.Chasing;
            _phaseTimer = 0f;
            _timeSinceSeen = 0f;
            _quietWake = false;
            _repathTimer = 0f;
            Vector3 target = StealthUtil.TryGetPlayer(out Transform pt, out _) ? pt.position : _lastSeenPos;
            _mover.SetTarget(target, chaseSpeed);
        }

        private void TickChasing(float dt)
        {
            if (!StealthUtil.TryGetPlayer(out Transform pt, out bool crouching))
            {
                StartInvestigate(_lastSeenPos);
                return;
            }

            Vector3 pp = pt.position;
            Vector3 me = transform.position;
            float hd = StealthUtil.HorizontalDistance(me, pp);
            // A crouching elf under furniture is only grabbed when Grandpa is right on top of it.
            bool hiddenSafe = HidingSpot.IsHidden(pt, crouching) && hd > hiddenCatchDistance;
            if (!hiddenSafe && hd <= catchDistance && Mathf.Abs(pp.y - me.y) < catchHeight)
            {
                Catch(pp);
                return;
            }

            if (_timeSinceSeen > loseSightTime)
            {
                _detect = 0.5f;
                _awareness = 100f;
                _searchTimer = 0f;
                StartInvestigate(_lastSeenPos);
                return;
            }

            // He "knows" where the elf went for a moment after losing sight, then heads to the last seen spot.
            Vector3 target = _timeSinceSeen < loseSightTime * 0.5f ? pp : _lastSeenPos;
            _repathTimer -= dt;
            if (_repathTimer <= 0f)
            {
                _repathTimer = chaseRepathInterval;
                _mover.UpdateTarget(target, chaseSpeed);
            }
            _mover.Tick(dt);
            if (!_mover.AgentReady) _mover.FaceTowards(target, dt, 1.5f);
        }

        private void Catch(Vector3 playerPos)
        {
            // Co-op: a caught elf is thrown back into the fireplace instead of ending the run (solo unchanged).
            if (TryThrowOut(playerPos)) return;

            _phase = Phase.Caught;
            _mover.Stop();
            _mover.FaceTowards(playerPos, 1f, 100f); // snap to face the elf
            if (_caughtRaised) return;
            _caughtRaised = true;

            // Comic beat before the game-over flow.
            GameEvents.RaiseToast(CatchLines[Random.Range(0, CatchLines.Length)]);
            if (_indicator != null) _indicator.ShowWord("Попался!", 5f);

            SetLevel(AlertLevel.Chasing);
            GameEvents.RaisePlayerCaught("Дед тебя поймал!");
        }

        /// <summary>
        /// Co-op only (more than one valid registered elf): grabs the elf nearest to the catch point, throws it back
        /// to the PlayerSpawn (fireplace) limp in a soot puff, then grumbles and walks back to bed. Returns false
        /// (caller keeps the normal PlayerCaught / game-over path) when solo, when this is the last elf, or when the
        /// caught elf cannot be resolved.
        /// </summary>
        private bool TryThrowOut(Vector3 catchPos)
        {
            if (_phase == Phase.Caught || _caughtRaised) return false;
            var players = GameServices.Players;
            if (players.Count <= 1 || CountValidPlayers() <= 1) return false;
            if (!StealthUtil.TryGetNearestPlayer(catchPos, out IPlayerStatus caught, out Transform elf, out _)) return false;

            int index = -1;
            for (int i = 0; i < players.Count; i++)
            {
                if (ReferenceEquals(players[i], caught)) { index = i; break; }
            }
            if (index < 0) return false; // not a registered player (legacy fallback): treat like solo

            // Fireplace spawn (same lookup as ElfController / FallRespawn).
            GameObject spawn = null;
            try { spawn = GameObject.FindWithTag(GameConstants.TagSpawn); } catch (UnityException) { }
            Vector3 spawnPos = spawn != null ? spawn.transform.position : Vector3.up;
            Quaternion spawnRot = spawn != null ? spawn.transform.rotation : Quaternion.identity;
            Vector2 jitter = Random.insideUnitCircle * 0.4f;
            Vector3 landPos = spawnPos + new Vector3(jitter.x, 0f, jitter.y);

            var controller = elf.GetComponent<SilentNight.Player.ElfController>();
            if (controller != null) controller.Teleport(landPos, spawnRot, true);
            else elf.SetPositionAndRotation(landPos, spawnRot);

            var ragdoll = elf.GetComponent<SilentNight.Player.ElfRagdoll>();
            if (ragdoll != null) ragdoll.GoLimp();

            SilentNight.FX.SootPuff.Spawn(spawnPos + Vector3.up * 0.3f, 0.8f);
            ElvesThrownOut++;

            // Grandpa: stop the chase, grumble, go back to bed through the normal return path.
            _mover.Stop();
            if (OutOfBed) _mover.FaceTowards(catchPos, 1f, 100f);
            _detect = 0f;
            _timeSinceSeen = 999f;
            _pendingInvestigate = false;
            _quietWake = false;
            _grumbledThisTrip = true; // no extra "Показалось…" toast on the way back
            _awareness = Mathf.Min(_awareness, searchThreshold - 1f);

            switch (_phase)
            {
                case Phase.InBed:
                case Phase.LyingDown:
                    break; // already (going) to bed
                case Phase.GettingUp:
                    BeginLyingDown();
                    break;
                default:
                    StartReturn();
                    break;
            }

            // Update the alert level silently so the throw-out toast isn't replaced by "Дед потерял тебя из виду...".
            _suppressLevelToast = true;
            SetLevel(ComputeLevel());
            _suppressLevelToast = false;

            GameEvents.RaiseToast("Дед схватил эльфа " + (index + 1) + " за шиворот и выкинул обратно в камин!");
            if (_indicator != null) _indicator.ShowWord(ThrowOutGrumbles[Random.Range(0, ThrowOutGrumbles.Length)], 3f);
            return true;
        }

        private static int CountValidPlayers()
        {
            int n = 0;
            for (int i = 0; i < StealthUtil.PlayerCount; i++)
                if (StealthUtil.TryGetPlayerAt(i, out _, out _, out _)) n++;
            return n;
        }

        // ---------------- helpers ----------------

        private AlertLevel ComputeLevel()
        {
            switch (_phase)
            {
                case Phase.InBed: return _bedLevel;
                case Phase.GettingUp: return _afterGetUp == AfterGetUp.Chase ? AlertLevel.Chasing : AlertLevel.Searching;
                case Phase.LyingDown: return AlertLevel.Stirring;
                case Phase.Chasing:
                case Phase.Caught: return AlertLevel.Chasing;
                default: return AlertLevel.Searching;
            }
        }

        private void SetLevel(AlertLevel next)
        {
            if (_levelBroadcast && next == _level) return;
            AlertLevel prev = _level;
            bool first = !_levelBroadcast;
            _level = next;
            _levelBroadcast = true;
            GameEvents.RaiseGrandpaAlertChanged(next);
            if (first || prev == next || _suppressLevelToast) return;

            switch (next)
            {
                case AlertLevel.Stirring:
                    if (prev == AlertLevel.Asleep) GameEvents.RaiseToast("Дед заворочался...");
                    break;
                case AlertLevel.Suspicious:
                    if (prev < AlertLevel.Suspicious) GameEvents.RaiseToast("Дед приподнялся и прислушивается...");
                    break;
                case AlertLevel.Searching:
                    if (prev == AlertLevel.Chasing) GameEvents.RaiseToast("Дед потерял тебя из виду...");
                    else if (prev < AlertLevel.Searching)
                        GameEvents.RaiseToast(_quietWake && _afterGetUp == AfterGetUp.Patrol ? "Дед встал попить воды..." : "Дед проснулся!");
                    break;
                case AlertLevel.Chasing:
                    GameEvents.RaiseToast("Дед тебя заметил! Беги!");
                    break;
                case AlertLevel.Asleep:
                    GameEvents.RaiseToast("Дед снова захрапел...");
                    break;
            }
        }

        private void UpdateFlashlight(float dt)
        {
            if (flashlight == null) return;
            float target = 0f;
            if (_phase == Phase.Chasing || _phase == Phase.Caught) target = flashlightChaseIntensity;
            else if (OutOfBed) target = flashlightIntensity;
            _flashCurrent = Mathf.MoveTowards(_flashCurrent, target, Mathf.Max(1f, flashlightChaseIntensity) * 2f * dt);
            flashlight.intensity = _flashCurrent;
            bool on = _flashCurrent > 0.01f;
            if (flashlight.enabled != on) flashlight.enabled = on;
        }

        private void UpdateVisual()
        {
            if (visual == null) return;
            NPCPose pose;
            switch (_phase)
            {
                case Phase.InBed: pose = _bedLevel == AlertLevel.Suspicious ? NPCPose.Sitting : NPCPose.Lying; break;
                case Phase.LyingDown: pose = NPCPose.Lying; break;
                default: pose = NPCPose.Standing; break;
            }
            visual.SetPose(pose);
            visual.SetSleeping(_phase == Phase.InBed && _bedLevel != AlertLevel.Suspicious);
            visual.SetAlert((int)_level);
            visual.SetLocomotion(_mover != null ? _mover.CurrentSpeed : 0f);
        }

        private void ResetRestless()
        {
            float a = Mathf.Min(restlessWakeInterval.x, restlessWakeInterval.y);
            float b = Mathf.Max(restlessWakeInterval.x, restlessWakeInterval.y);
            _restlessTimer = Random.Range(a, b) * (_spooky ? spookyRestlessMultiplier : 1f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Vector3 bed = Application.isPlaying ? _bedPos : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(bed + Vector3.up * 0.3f, new Vector3(1f, 0.6f, 2f));
            if (Application.isPlaying)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(_standPoint, 0.25f);
                if (_hasNoisePos)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(_lastNoisePos, 0.35f);
                }
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position, catchDistance);
            }
            if (patrolPoints != null)
            {
                Gizmos.color = new Color(0.4f, 0.7f, 1f);
                for (int i = 0; i < patrolPoints.Length; i++)
                    if (patrolPoints[i] != null) Gizmos.DrawWireSphere(patrolPoints[i].position, 0.2f);
            }
        }
#endif
    }
}
