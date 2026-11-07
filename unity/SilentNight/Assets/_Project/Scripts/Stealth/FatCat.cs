using UnityEngine;
using UnityEngine.AI;
using SilentNight.Core;
using SilentNight.Player;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Fat, lazy, unpredictable house cat.
    /// - Sleeps most of the time; every 20–45 s it wakes up, strolls to a random nearby spot, its bowl or its bed,
    ///   lingers there and curls up again (sometimes right where it is, sometimes back on the bed).
    /// - A loud enough noise wakes it: it turns, walks over to the source, sniffs around for a few seconds, then goes back.
    /// - A standing elf stepping near its rear = tail stepped on: loud yowl (NoiseSource.Cat → wakes Grandpa), toast, bolt.
    /// - While awake and near an elf it meows now and then (quiet, comedic tension).
    /// - Catnip (Player/CatnipThrower): a free pouch within catnipNoticeRadius makes it walk over and roll/purr there for
    ///   catnipDuration s. Meanwhile it ignores all noises, never meows/yowls and its tail is safe. Then the pouch is consumed.
    /// Without a NavMesh it never walks (would clip through walls): it only turns in place and snaps back to its bed.
    /// </summary>
    [DisallowMultipleComponent]
    public class FatCat : MonoBehaviour
    {
        private enum CatState { Sleeping, Waking, Walking, Lingering, Investigating, Sniffing, Fleeing, Glaring, Returning, CatnipApproach, CatnipPurr }
        private enum WakeReason { Idle, Elf, Noise }
        private enum TripKind { Random, Bowl, Bed }

        private const int MaxElves = 8;
        private const float TaggedRefreshInterval = 1.5f;
        private static readonly string[] MeowToasts = { "Кот: «Мяу?»", "Кот: «Мрррау...»", "Кот подозрительно на вас смотрит. «Мяу.»" };

        [Header("References (auto-found if empty)")]
        [SerializeField] private NPCVisual visual;
        [SerializeField] private NavMeshAgent agent;
        [Tooltip("Where the cat sleeps. Empty = its spawn position/rotation.")]
        [SerializeField] private Transform bed;
        [Tooltip("Food bowl. Empty = a NavMesh point ~bowlFallbackDistance from the bed.")]
        [SerializeField] private Transform bowl;
        [SerializeField] private float bowlFallbackDistance = 1.5f;

        [Header("Idle life")]
        [Tooltip("Random nap length (s) before the cat gets up on its own.")]
        [SerializeField] private float napMin = 20f;
        [SerializeField] private float napMax = 45f;
        [Tooltip("Stretch / look around after waking up on its own.")]
        [SerializeField] private float stretchTime = 1.5f;
        [SerializeField] private float wanderRadius = 3f;
        [SerializeField] private float wanderSpeed = 0.9f;
        [Tooltip("How long it sits at a stop (bowl stops last 1.5x longer).")]
        [SerializeField] private float lingerMin = 2f;
        [SerializeField] private float lingerMax = 5f;
        [Tooltip("Stops per outing (random 1..max).")]
        [SerializeField] private int maxStops = 3;
        [Range(0f, 1f)] [SerializeField] private float bowlChance = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float bedChance = 0.25f;
        [Tooltip("At the end of an outing: chance to curl up right where it is instead of going back to the bed.")]
        [Range(0f, 1f)] [SerializeField] private float sleepAnywhereChance = 0.35f;

        [Header("Waking by elves / noise")]
        [Tooltip("A standing elf this close wakes the sleeping cat (quiet meow, no yowl).")]
        [SerializeField] private float proximityWakeRadius = 1.0f;
        [Tooltip("A crouching elf can sneak past without waking it.")]
        [SerializeField] private bool crouchSneaksPast = true;
        [Tooltip("Heard loudness (after falloff) that wakes / distracts the cat. Cat noises are ignored.")]
        [SerializeField] private float hearingWakeLoudness = 30f;
        [Tooltip("After falling asleep the cat ignores proximity/noise for this long.")]
        [SerializeField] private float rewakeCooldown = 4f;
        [SerializeField] private float noiseWakeTime = 0.6f;

        [Header("Investigate noise")]
        [SerializeField] private float investigateSpeed = 0.85f;
        [Tooltip("Stops this far short of the noise source.")]
        [SerializeField] private float investigateStopDistance = 0.45f;
        [SerializeField] private float investigateTimeout = 12f;
        [SerializeField] private float sniffMin = 2.5f;
        [SerializeField] private float sniffMax = 4.5f;

        [Header("Tail")]
        [Tooltip("Tail zone centre: this far behind the cat.")]
        [SerializeField] private float tailOffset = 0.5f;
        [SerializeField] private float tailRadius = 0.35f;
        [SerializeField] private float tailCooldown = 6f;
        [SerializeField] private float tailYowlLoudness = 65f;
        [SerializeField] private float boltDistance = 3f;
        [SerializeField] private float boltSpeed = 2.6f;
        [SerializeField] private float boltAcceleration = 20f;
        [SerializeField] private float boltTimeout = 4f;
        [SerializeField] private float glareTime = 1.5f;

        [Header("Meow (awake)")]
        [SerializeField] private float awakeMeowLoudness = 35f;
        [SerializeField] private float awakeMeowRadius = 1.3f;
        [SerializeField] private float meowCooldownMin = 5f;
        [SerializeField] private float meowCooldownMax = 10f;
        [Range(0f, 1f)] [SerializeField] private float meowToastChance = 0.3f;
        [SerializeField] private bool showToasts = true;

        [Header("Catnip")]
        [Tooltip("A free catnip pouch this close (horizontal) attracts the cat, even out of a nap.")]
        [SerializeField] private float catnipNoticeRadius = 6f;
        [SerializeField] private float catnipScanInterval = 0.4f;
        [SerializeField] private float catnipApproachSpeed = 1.0f;
        [Tooltip("Stops this far from the pouch.")]
        [SerializeField] private float catnipStopDistance = 0.25f;
        [SerializeField] private float catnipApproachTimeout = 10f;
        [Tooltip("Purring blissfully next to the pouch: ignores noise, never yowls.")]
        [SerializeField] private float catnipDuration = 20f;
        [Tooltip("Quiet purr noise (0 = silent).")]
        [SerializeField] private float purrLoudness = 15f;
        [SerializeField] private float purrInterval = 4f;

        [Header("Movement")]
        [SerializeField] private float returnTimeout = 15f;
        [SerializeField] private float turnSpeed = 300f;
        [SerializeField] private float arriveDistance = 0.35f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        public bool IsSleeping => _state == CatState.Sleeping;
        /// <summary>True while the cat is going for / purring at catnip (deaf to noise, tail safe).</summary>
        public bool IsOnCatnip => IsCatnipState;
        private bool IsCatnipState => _state == CatState.CatnipApproach || _state == CatState.CatnipPurr;

        private CatnipPouch _catnip;
        private Vector3 _catnipTargetPos;
        private float _catnipScanTimer;
        private float _catnipRetarget;
        private float _purrNoiseTimer;

        private NavMover _mover;
        private CatState _state = CatState.Sleeping;
        private WakeReason _wakeReason;
        private TripKind _tripKind;
        private Vector3 _bedPos;
        private Quaternion _bedRot;
        private Vector3 _homeOnNav;
        private bool _homeOnNavValid;
        private Vector3 _bowlPos;
        private bool _bowlValid;
        private float _baseAcceleration = 6f;

        private float _timer;
        private float _napTimer;
        private float _lingerTime;
        private float _sniffTime;
        private float _cooldownTimer;
        private float _meowTimer;
        private float _tailTimer;
        private int _stopsLeft;
        private bool _initialized;

        private bool _pendingNoise;
        private Vector3 _pendingNoisePos;
        private float _pendingNoiseHeard;
        private Vector3 _noisePos;
        private Vector3 _lookTarget;
        private bool _hasLookTarget;

        // Elves (local player + anything tagged Player), refreshed without per-frame allocations.
        private readonly Transform[] _elfT = new Transform[MaxElves];
        private readonly bool[] _elfCrouch = new bool[MaxElves];
        private int _elfCount;
        private GameObject[] _tagged = new GameObject[0];
        private IPlayerStatus[] _taggedStatus = new IPlayerStatus[0];
        private float _taggedRefresh;

        public void Configure(NPCVisual npcVisual, NavMeshAgent navAgent)
        {
            visual = npcVisual;
            agent = navAgent;
        }

        private void OnEnable() => GameEvents.Noise += OnNoise;
        private void OnDisable()
        {
            GameEvents.Noise -= OnNoise;
            ReleaseCatnip(); // let another cat have it; state machine bails out of catnip on re-enable
        }

        private void Start()
        {
            if (visual == null) visual = GetComponent<NPCVisual>();
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (visual != null) visual.EnsureBuilt();
            if (agent != null) _baseAcceleration = agent.acceleration;

            if (bed != null)
            {
                _bedPos = bed.position;
                _bedRot = Quaternion.Euler(0f, bed.eulerAngles.y, 0f);
                transform.SetPositionAndRotation(_bedPos, _bedRot);
            }
            else
            {
                _bedPos = transform.position;
                _bedRot = transform.rotation;
            }
            _homeOnNavValid = NavMover.Sample(_bedPos, 1.5f, out _homeOnNav);
            ResolveBowl();

            _mover = new NavMover(transform, agent)
            {
                AllowManualFallback = false, // without NavMesh the cat would walk through walls
                TurnSpeed = turnSpeed,
                ArriveDistance = arriveDistance
            };
            _mover.Disable();

            _napTimer = Random.Range(napMin * 0.4f, napMax); // desync the first wake-up
            _meowTimer = meowCooldownMin;
            _initialized = true;
            UpdateVisual();
            if (visual != null) visual.SnapPose();
        }

        private void ResolveBowl()
        {
            if (bowl != null)
            {
                _bowlValid = true;
                if (!NavMover.Sample(bowl.position, 1f, out _bowlPos)) _bowlPos = bowl.position;
                return;
            }

            Vector3 centre = _homeOnNavValid ? _homeOnNav : _bedPos;
            Vector3 fwd = _bedRot * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, 90f + i * 45f, 0f) * fwd;
                Vector3 candidate = centre + dir * bowlFallbackDistance;
                if (i == 0) _bowlPos = candidate;
                if (NavMover.Sample(candidate, 0.6f, out Vector3 p))
                {
                    _bowlPos = p;
                    _bowlValid = true;
                    return;
                }
            }
            _bowlValid = false;
        }

        // ---------------- events ----------------

        private void OnNoise(NoiseEvent e)
        {
            if (!_initialized) return;
            if (IsCatnipState) return; // blissed out: hears nothing
            if (e.Source == NoiseSource.Cat || (e.Emitter != null && e.Emitter == gameObject)) return;
            if (!StealthUtil.IsGameplayActive) return;
            float heard = e.LoudnessAt(transform.position + Vector3.up * 0.3f);
            if (heard < hearingWakeLoudness) return;
            if (!_pendingNoise || heard > _pendingNoiseHeard)
            {
                // Handled in Update (avoid re-entrant RaiseNoise inside the event).
                _pendingNoise = true;
                _pendingNoisePos = e.Position;
                _pendingNoiseHeard = heard;
            }
        }

        // ---------------- update ----------------

        private void Update()
        {
            if (!_initialized) return;
            if (!StealthUtil.IsGameplayActive) { _mover.Halt(); _pendingNoise = false; return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            _timer += dt;
            if (_meowTimer > 0f) _meowTimer -= dt;
            if (_cooldownTimer > 0f) _cooldownTimer -= dt;
            if (_tailTimer > 0f) _tailTimer -= dt;

            GatherElves(dt);

            // Catnip overrides everything (checked before the tail: stepping on it is safe while purring).
            if (IsCatnipState)
            {
                _pendingNoise = false;
                TickCatnip(dt);
                UpdateVisual();
                return;
            }
            if (TryNoticeCatnip(dt)) { UpdateVisual(); return; }

            if (CheckTail()) { UpdateVisual(); return; }
            HandlePendingNoise();

            switch (_state)
            {
                case CatState.Sleeping: TickSleeping(dt); break;
                case CatState.Waking: TickWaking(dt); break;
                case CatState.Walking: TickWalking(dt); break;
                case CatState.Lingering: TickLingering(dt); break;
                case CatState.Investigating: TickInvestigating(dt); break;
                case CatState.Sniffing: TickSniffing(dt); break;
                case CatState.Fleeing: TickFleeing(dt); break;
                case CatState.Glaring: TickGlaring(dt); break;
                case CatState.Returning: TickReturning(dt); break;
            }

            if (_state != CatState.Sleeping && _state != CatState.Fleeing) TryAwakeMeow();
            UpdateVisual();
        }

        // ---------------- elves ----------------

        private void GatherElves(float dt)
        {
            _elfCount = 0;
            Transform local = null;
            if (StealthUtil.TryGetPlayer(out Transform pt, out bool crouching))
            {
                local = pt;
                _elfT[0] = pt;
                _elfCrouch[0] = crouching;
                _elfCount = 1;
            }

            _taggedRefresh -= dt;
            if (_taggedRefresh <= 0f)
            {
                _taggedRefresh = TaggedRefreshInterval;
                _tagged = GameObject.FindGameObjectsWithTag(GameConstants.TagPlayer);
                if (_taggedStatus.Length != _tagged.Length) _taggedStatus = new IPlayerStatus[_tagged.Length];
                for (int i = 0; i < _tagged.Length; i++)
                    _taggedStatus[i] = _tagged[i] != null ? _tagged[i].GetComponentInParent<IPlayerStatus>() : null;
            }

            for (int i = 0; i < _tagged.Length && _elfCount < MaxElves; i++)
            {
                GameObject go = _tagged[i];
                if (go == null) continue;
                Transform t = go.transform;
                if (local != null && (t == local || t.IsChildOf(local))) continue;
                IPlayerStatus s = _taggedStatus[i];
                bool c = false;
                if (s != null && !(s is Object uo && uo == null)) c = s.IsCrouching;
                _elfT[_elfCount] = t;
                _elfCrouch[_elfCount] = c;
                _elfCount++;
            }
        }

        /// <summary>Nearest elf (horizontal) within maxDist and similar height. Optionally ignores crouching elves.</summary>
        private bool TryNearestElf(Vector3 point, float maxDist, float maxDy, bool ignoreCrouching, out Vector3 elfPos)
        {
            elfPos = Vector3.zero;
            float best = maxDist;
            bool found = false;
            for (int i = 0; i < _elfCount; i++)
            {
                Transform t = _elfT[i];
                if (t == null) continue;
                if (ignoreCrouching && _elfCrouch[i]) continue;
                Vector3 p = t.position;
                if (Mathf.Abs(p.y - point.y) > maxDy) continue;
                float d = StealthUtil.HorizontalDistance(p, point);
                if (d <= best)
                {
                    best = d;
                    elfPos = p;
                    found = true;
                }
            }
            return found;
        }

        // ---------------- tail ----------------

        private bool CheckTail()
        {
            if (_tailTimer > 0f || _state == CatState.Fleeing) return false;
            Vector3 tail = transform.position - transform.forward * tailOffset;
            if (!TryNearestElf(tail, tailRadius, 0.6f, true, out Vector3 elfPos)) return false;

            _tailTimer = tailCooldown;
            _meowTimer = Random.Range(meowCooldownMin, meowCooldownMax);
            _pendingNoise = false;
            GameEvents.RaiseNoise(new NoiseEvent(transform.position + Vector3.up * 0.3f, tailYowlLoudness, NoiseSource.Cat, gameObject));
            if (showToasts) GameEvents.RaiseToast("Мяяяу! Вы наступили коту на хвост.");
            StartFlee(elfPos);
            return true;
        }

        private void StartFlee(Vector3 from)
        {
            EnsureAgent();
            SetState(CatState.Fleeing);
            if (!_mover.AgentReady) return; // no NavMesh: TickFleeing just hisses in place briefly

            if (agent != null) agent.acceleration = boltAcceleration;
            Vector3 away = transform.position - from;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;
            away.Normalize();

            for (int i = 0; i < 8; i++)
            {
                float angle = (i % 2 == 0 ? 1f : -1f) * (i / 2) * 30f + Random.Range(-15f, 15f);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * away;
                float dist = boltDistance * Random.Range(0.8f, 1.2f);
                if (NavMover.Sample(transform.position + dir * dist, 1f, out Vector3 p) &&
                    StealthUtil.HorizontalDistance(p, from) > StealthUtil.HorizontalDistance(transform.position, from) + 0.5f)
                {
                    _mover.SetTarget(p, boltSpeed);
                    return;
                }
            }
            // Nowhere to run: at least turn away.
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void TickFleeing(float dt)
        {
            bool arrived = _mover.Tick(dt);
            if (!arrived && _timer < boltTimeout) return;
            _mover.Stop();
            RestoreAcceleration();
            SetState(CatState.Glaring);
            _hasLookTarget = TryNearestElf(transform.position, 6f, 2f, false, out _lookTarget);
        }

        private void TickGlaring(float dt)
        {
            if (_hasLookTarget) _mover.FaceTowards(_lookTarget, dt, 0.8f);
            if (_timer >= glareTime) StartReturn();
        }

        // ---------------- noise ----------------

        private void HandlePendingNoise()
        {
            if (!_pendingNoise) return;
            _pendingNoise = false;
            Vector3 pos = _pendingNoisePos;
            _pendingNoiseHeard = 0f;

            switch (_state)
            {
                case CatState.Fleeing:
                case CatState.Glaring:
                    return; // too offended to care
                case CatState.Sleeping:
                    if (_cooldownTimer > 0f) return;
                    _noisePos = pos;
                    Wake(WakeReason.Noise);
                    if (showToasts) GameEvents.RaiseToast("Кот проснулся и навострил уши...");
                    return;
                case CatState.Waking:
                    _noisePos = pos;
                    _wakeReason = WakeReason.Noise;
                    return;
                default:
                    StartInvestigate(pos);
                    return;
            }
        }

        private void StartInvestigate(Vector3 source)
        {
            _noisePos = source;
            EnsureAgent();
            SetState(CatState.Investigating);
            if (!_mover.AgentReady) return;

            Vector3 toCat = transform.position - source;
            toCat.y = 0f;
            Vector3 goal = source;
            if (toCat.sqrMagnitude > investigateStopDistance * investigateStopDistance)
                goal = source + toCat.normalized * investigateStopDistance;
            if (NavMover.Sample(goal, 1.5f, out Vector3 p)) _mover.SetTarget(p, investigateSpeed);
            else _mover.Stop();
        }

        private void TickInvestigating(float dt)
        {
            bool arrived = _mover.Tick(dt);
            if (!_mover.AgentReady) _mover.FaceTowards(_noisePos, dt, 0.6f);
            if (!arrived && _timer < investigateTimeout) return;
            _mover.Stop();
            SetState(CatState.Sniffing);
            _sniffTime = Random.Range(sniffMin, sniffMax);
        }

        private void TickSniffing(float dt)
        {
            // Nose towards the source, head swinging left/right.
            Vector3 dir = _noisePos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir = Quaternion.Euler(0f, Mathf.Sin(_timer * 2.5f) * 35f, 0f) * dir;
            _mover.FaceDirection(dir, dt, 0.4f);
            if (_timer < _sniffTime) return;
            _stopsLeft = 0;
            StartReturn();
        }

        // ---------------- catnip ----------------

        private bool TryNoticeCatnip(float dt)
        {
            _catnipScanTimer -= dt;
            if (_catnipScanTimer > 0f) return false;
            _catnipScanTimer = catnipScanInterval;
            if (_state == CatState.Fleeing) return false; // too busy running
            if (!CatnipThrower.TryFindFree(transform.position, catnipNoticeRadius, out CatnipPouch pouch)) return false;

            pouch.Claimer = this;
            _catnip = pouch;
            _pendingNoise = false;
            _hasLookTarget = false;
            if (showToasts) GameEvents.RaiseToast("Кот учуял кошачью мяту...");
            StartCatnipApproach();
            return true;
        }

        private void StartCatnipApproach()
        {
            RestoreAcceleration();
            EnsureAgent();
            SetState(CatState.CatnipApproach);
            _catnipRetarget = 0.5f;
            _catnipTargetPos = _catnip.Position;
            if (_mover.AgentReady && TryCatnipGoal(_catnipTargetPos, out Vector3 goal)) _mover.SetTarget(goal, catnipApproachSpeed);
            else _mover.Stop();
        }

        /// <summary>NavMesh point right next to the pouch, on the cat's side.</summary>
        private bool TryCatnipGoal(Vector3 pouchPos, out Vector3 goal)
        {
            Vector3 toCat = transform.position - pouchPos;
            toCat.y = 0f;
            Vector3 p = pouchPos;
            if (toCat.sqrMagnitude > catnipStopDistance * catnipStopDistance) p = pouchPos + toCat.normalized * catnipStopDistance;
            return NavMover.Sample(p, 1f, out goal);
        }

        private void TickCatnip(float dt)
        {
            if (_catnip == null || !_catnip.Alive) { EndCatnip(false); return; }
            Vector3 pouchPos = _catnip.Position;

            if (_state == CatState.CatnipApproach)
            {
                bool arrived;
                if (_mover.AgentReady)
                {
                    // The pouch may still be rolling: follow it.
                    _catnipRetarget -= dt;
                    if (_catnipRetarget <= 0f)
                    {
                        _catnipRetarget = 0.5f;
                        if (StealthUtil.HorizontalDistance(pouchPos, _catnipTargetPos) > 0.3f && TryCatnipGoal(pouchPos, out Vector3 goal))
                        {
                            _catnipTargetPos = pouchPos;
                            _mover.UpdateTarget(goal, catnipApproachSpeed);
                        }
                    }
                    arrived = _mover.Tick(dt) ||
                              StealthUtil.HorizontalDistance(transform.position, pouchPos) <= catnipStopDistance + 0.15f;
                }
                else
                {
                    // No NavMesh: cannot walk, so it just turns towards the smell and melts on the spot.
                    _mover.FaceTowards(pouchPos, dt, 0.8f);
                    arrived = _timer > 0.8f;
                }
                if (arrived || _timer >= catnipApproachTimeout) StartPurr();
                return;
            }

            // CatnipPurr: lazily rolling around next to the pouch.
            Vector3 dir = pouchPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir = Quaternion.Euler(0f, Mathf.Sin(_timer * 1.2f) * 40f, 0f) * dir;
            _mover.FaceDirection(dir, dt, 0.25f);

            if (purrLoudness > 0f)
            {
                _purrNoiseTimer -= dt;
                if (_purrNoiseTimer <= 0f)
                {
                    _purrNoiseTimer = purrInterval;
                    GameEvents.RaiseNoise(new NoiseEvent(transform.position + Vector3.up * 0.3f, purrLoudness, NoiseSource.Cat, gameObject));
                }
            }

            if (_timer >= catnipDuration) EndCatnip(true);
        }

        private void StartPurr()
        {
            _mover.Stop();
            SetState(CatState.CatnipPurr);
            _purrNoiseTimer = 1f;
            if (showToasts) GameEvents.RaiseToast("Кот нашёл мяту и блаженно мурлычет. Мррр...");
        }

        private void EndCatnip(bool finished)
        {
            CatnipPouch pouch = _catnip;
            _catnip = null;
            if (pouch != null)
            {
                if (finished) CatnipThrower.Consume(pouch);
                else if (pouch.Claimer == this) pouch.Claimer = null;
            }
            if (finished && showToasts) GameEvents.RaiseToast("Мята выветрилась. Кот вернулся к своим делам.");
            _pendingNoise = false;
            _meowTimer = Random.Range(meowCooldownMin, meowCooldownMax);
            _catnipScanTimer = catnipScanInterval;
            StartReturn();
        }

        private void ReleaseCatnip()
        {
            if (_catnip != null && _catnip.Claimer == this) _catnip.Claimer = null;
            _catnip = null;
        }

        // ---------------- sleeping / waking ----------------

        private void TickSleeping(float dt)
        {
            if (_cooldownTimer <= 0f &&
                TryNearestElf(transform.position, proximityWakeRadius, 1.2f, crouchSneaksPast, out Vector3 elfPos))
            {
                _lookTarget = elfPos;
                _hasLookTarget = true;
                Wake(WakeReason.Elf);
                Meow(true);
                return;
            }

            _napTimer -= dt;
            if (_napTimer <= 0f) Wake(WakeReason.Idle);
        }

        private void Wake(WakeReason reason)
        {
            _wakeReason = reason;
            _stopsLeft = Random.Range(1, Mathf.Max(1, maxStops) + 1);
            if (reason != WakeReason.Elf) _hasLookTarget = false;
            SetState(CatState.Waking);
        }

        private void TickWaking(float dt)
        {
            float duration;
            switch (_wakeReason)
            {
                case WakeReason.Noise:
                    _mover.FaceTowards(_noisePos, dt, 0.8f);
                    duration = noiseWakeTime;
                    break;
                case WakeReason.Elf:
                    if (_hasLookTarget) _mover.FaceTowards(_lookTarget, dt, 1f);
                    duration = noiseWakeTime * 1.5f;
                    break;
                default:
                    duration = stretchTime;
                    break;
            }
            if (_timer < duration) return;

            if (_wakeReason == WakeReason.Noise) StartInvestigate(_noisePos);
            else StartTrip(_wakeReason == WakeReason.Elf ? TripKind.Random : ChooseTripKind());
        }

        // ---------------- idle trips ----------------

        private TripKind ChooseTripKind()
        {
            float r = Random.value;
            if (r < bowlChance && _bowlValid) return TripKind.Bowl;
            if (r < bowlChance + bedChance && !IsAtBed()) return TripKind.Bed;
            return TripKind.Random;
        }

        private bool IsAtBed() => StealthUtil.HorizontalDistance(transform.position, _bedPos) < 0.6f;

        private void StartTrip(TripKind kind)
        {
            if (kind == TripKind.Bed) { StartReturn(); return; }
            if (kind == TripKind.Bowl && !_bowlValid) kind = TripKind.Random;
            _tripKind = kind;

            EnsureAgent();
            SetState(CatState.Walking);
            if (!_mover.AgentReady) return; // TickWalking: turns in place

            if (kind == TripKind.Bowl) { _mover.SetTarget(_bowlPos, wanderSpeed); return; }

            Vector3 centre = _homeOnNavValid ? _homeOnNav : _bedPos;
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                if (NavMover.Sample(centre + new Vector3(r.x, 0f, r.y), 1f, out Vector3 p))
                {
                    _mover.SetTarget(p, wanderSpeed);
                    return;
                }
            }
            _mover.Stop(); // nowhere to go: just linger
        }

        private void TickWalking(float dt)
        {
            bool arrived;
            if (_mover.AgentReady) arrived = _mover.Tick(dt);
            else
            {
                transform.Rotate(0f, 40f * dt, 0f, Space.World); // no NavMesh: lazily turn around in place
                arrived = _timer > 3f;
            }
            if (!arrived && _timer < returnTimeout) return;

            _mover.Stop();
            SetState(CatState.Lingering);
            _lingerTime = Random.Range(lingerMin, lingerMax) * (_tripKind == TripKind.Bowl ? 1.5f : 1f);
            _hasLookTarget = _tripKind == TripKind.Bowl && bowl != null; // face the actual bowl while "eating"
            if (_hasLookTarget) _lookTarget = bowl.position;
        }

        private void TickLingering(float dt)
        {
            if (_hasLookTarget) _mover.FaceTowards(_lookTarget, dt, 0.5f);
            if (_timer < _lingerTime) return;

            _stopsLeft--;
            if (_stopsLeft > 0) { StartTrip(ChooseTripKind()); return; }
            if (_mover.AgentReady && Random.value < sleepAnywhereChance) GoToSleep(false);
            else StartReturn();
        }

        private void StartReturn()
        {
            EnsureAgent();
            SetState(CatState.Returning);
            if (_mover.AgentReady) _mover.SetTarget(_homeOnNavValid ? _homeOnNav : _bedPos, wanderSpeed);
        }

        private void TickReturning(float dt)
        {
            bool arrived = !_mover.AgentReady || _mover.Tick(dt);
            if (!arrived && _timer < returnTimeout) return;
            GoToSleep(true);
        }

        private void GoToSleep(bool atBed)
        {
            _mover.Stop();
            _mover.Disable();
            RestoreAcceleration();
            if (atBed) transform.SetPositionAndRotation(_bedPos, _bedRot); // hops back onto its spot
            SetState(CatState.Sleeping);
            _cooldownTimer = rewakeCooldown;
            _napTimer = Random.Range(napMin, napMax);
            _pendingNoise = false;
            _hasLookTarget = false;
        }

        // ---------------- helpers ----------------

        private void SetState(CatState s)
        {
            _state = s;
            _timer = 0f;
        }

        /// <summary>Enables the NavMeshAgent on the nearest NavMesh point (the factory leaves it disabled).</summary>
        private bool EnsureAgent()
        {
            if (_mover.AgentReady) return true;
            if (NavMover.Sample(transform.position, 1.5f, out Vector3 p)) return _mover.EnableAt(p);
            if (_homeOnNavValid) return _mover.EnableAt(_homeOnNav);
            return false;
        }

        private void RestoreAcceleration()
        {
            if (agent != null) agent.acceleration = _baseAcceleration;
        }

        private void TryAwakeMeow()
        {
            if (_meowTimer > 0f) return;
            if (!TryNearestElf(transform.position, awakeMeowRadius, 1.2f, false, out _)) return;
            Meow(false);
        }

        private void Meow(bool forceToast)
        {
            _meowTimer = Random.Range(meowCooldownMin, meowCooldownMax);
            GameEvents.RaiseNoise(new NoiseEvent(transform.position + Vector3.up * 0.3f, awakeMeowLoudness, NoiseSource.Cat, gameObject));
            if (showToasts && (forceToast || Random.value < meowToastChance))
                GameEvents.RaiseToast(MeowToasts[Random.Range(0, MeowToasts.Length)]);
        }

        private void UpdateVisual()
        {
            if (visual == null) return;
            if (_state == CatState.CatnipPurr)
            {
                // Rolls around: lying, briefly sitting up, lying again. Eyes open, perfectly content.
                int phase = (int)(_timer / 2.5f) % 3;
                visual.SetPose(phase == 1 ? NPCPose.Sitting : NPCPose.Lying);
                visual.SetSleeping(false);
                visual.SetAlert(0);
                visual.SetLocomotion(0f);
                return;
            }
            bool sleeping = _state == CatState.Sleeping;
            visual.SetPose(sleeping ? NPCPose.Lying : NPCPose.Standing);
            visual.SetSleeping(sleeping);
            visual.SetAlert(sleeping ? 0 : 1);
            visual.SetLocomotion(_mover != null ? _mover.CurrentSpeed : 0f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, proximityWakeRadius);
            Gizmos.color = new Color(1f, 1f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, awakeMeowRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position - transform.forward * tailOffset, tailRadius);
            Gizmos.color = new Color(0.4f, 0.9f, 0.3f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, catnipNoticeRadius);
            Vector3 home = Application.isPlaying ? _bedPos : (bed != null ? bed.position : transform.position);
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(home, wanderRadius);
            if (Application.isPlaying && _bowlValid)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
                Gizmos.DrawWireCube(_bowlPos + Vector3.up * 0.05f, new Vector3(0.25f, 0.1f, 0.25f));
            }
            else if (bowl != null)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
                Gizmos.DrawWireCube(bowl.position + Vector3.up * 0.05f, new Vector3(0.25f, 0.1f, 0.25f));
            }
        }
#endif
    }
}
