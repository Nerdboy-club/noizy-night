using UnityEngine;
using UnityEngine.AI;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Fat house cat. Sleeps at its spot; wakes when the elf comes close without crouching, steps on its tail,
    /// or something loud happens nearby. On waking it meows loudly (NoiseSource.Cat → Grandpa hears it),
    /// wanders around a bit on the NavMesh (stays put if there is none), then returns and sleeps again.
    /// </summary>
    [DisallowMultipleComponent]
    public class FatCat : MonoBehaviour
    {
        private enum CatState { Sleeping, Startled, Wandering, Returning }

        [Header("References (auto-found if empty)")]
        [SerializeField] private NPCVisual visual;
        [SerializeField] private NavMeshAgent agent;

        [Header("Waking")]
        [SerializeField] private float wakeRadius = 1.5f;
        [Tooltip("A crouching elf can sneak past (still wakes on the tail).")]
        [SerializeField] private bool crouchSneaksPast = true;
        [Tooltip("Tail zone centre: this far behind the cat.")]
        [SerializeField] private float tailOffset = 0.5f;
        [SerializeField] private float tailRadius = 0.35f;
        [Tooltip("Heard loudness (after falloff) that wakes the cat. Other cats' meows are ignored.")]
        [SerializeField] private float hearingWakeLoudness = 30f;
        [Tooltip("After falling asleep again the cat can't be woken by proximity/noise for this long.")]
        [SerializeField] private float wakeCooldown = 8f;

        [Header("Meow")]
        [SerializeField] private float meowLoudness = 65f;
        [SerializeField] private float tailMeowLoudness = 80f;
        [SerializeField] private float meowCooldown = 4f;
        [Tooltip("While awake, the cat meows again if a standing elf gets this close.")]
        [SerializeField] private float awakeMeowRadius = 0.9f;
        [SerializeField] private bool showToasts = true;

        [Header("Wander")]
        [SerializeField] private float startledTime = 0.8f;
        [SerializeField] private float wanderRadius = 3f;
        [SerializeField] private float wanderSpeed = 0.9f;
        [SerializeField] private float wanderDuration = 7f;
        [SerializeField] private float wanderPause = 1.5f;
        [SerializeField] private float returnTimeout = 15f;
        [SerializeField] private float turnSpeed = 300f;
        [SerializeField] private float arriveDistance = 0.35f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        public bool IsSleeping => _state == CatState.Sleeping;

        private NavMover _mover;
        private CatState _state = CatState.Sleeping;
        private Vector3 _sleepPos;
        private Quaternion _sleepRot;
        private Vector3 _homeOnNav;
        private bool _homeOnNavValid;
        private float _timer;
        private float _wanderTimer;
        private float _pauseTimer;
        private float _cooldownTimer;
        private float _meowTimer;
        private bool _initialized;
        private bool _pendingNoiseWake;

        public void Configure(NPCVisual npcVisual, NavMeshAgent navAgent)
        {
            visual = npcVisual;
            agent = navAgent;
        }

        private void OnEnable() => GameEvents.Noise += OnNoise;
        private void OnDisable() => GameEvents.Noise -= OnNoise;

        private void Start()
        {
            if (visual == null) visual = GetComponent<NPCVisual>();
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (visual != null) visual.EnsureBuilt();

            _sleepPos = transform.position;
            _sleepRot = transform.rotation;
            _homeOnNavValid = NavMover.Sample(_sleepPos, 1.5f, out _homeOnNav);

            _mover = new NavMover(transform, agent)
            {
                AllowManualFallback = false, // without NavMesh the cat would walk through walls
                TurnSpeed = turnSpeed,
                ArriveDistance = arriveDistance
            };
            _mover.Disable();
            _initialized = true;
            UpdateVisual();
            if (visual != null) visual.SnapPose();
        }

        private void OnNoise(NoiseEvent e)
        {
            if (!_initialized || _state != CatState.Sleeping || _cooldownTimer > 0f) return;
            if (e.Source == NoiseSource.Cat || (e.Emitter != null && e.Emitter == gameObject)) return;
            if (!StealthUtil.IsGameplayActive) return;
            if (e.LoudnessAt(transform.position + Vector3.up * 0.3f) >= hearingWakeLoudness)
                _pendingNoiseWake = true; // handled in Update (avoid re-entrant RaiseNoise inside the event)
        }

        private void Update()
        {
            if (!_initialized) return;
            if (!StealthUtil.IsGameplayActive) { _mover.Halt(); return; }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            _timer += dt;
            if (_meowTimer > 0f) _meowTimer -= dt;
            if (_cooldownTimer > 0f) _cooldownTimer -= dt;

            switch (_state)
            {
                case CatState.Sleeping: TickSleeping(); break;
                case CatState.Startled: TickStartled(dt); break;
                case CatState.Wandering: TickWandering(dt); break;
                case CatState.Returning: TickReturning(dt); break;
            }
            UpdateVisual();
        }

        private void TickSleeping()
        {
            bool havePlayer = StealthUtil.TryGetPlayer(out Transform pt, out bool crouching);
            if (havePlayer)
            {
                Vector3 pp = pt.position;
                Vector3 tail = transform.position - transform.forward * tailOffset;
                if (StealthUtil.HorizontalDistance(pp, tail) <= tailRadius && Mathf.Abs(pp.y - transform.position.y) < 0.6f)
                {
                    Wake(true);
                    return;
                }
            }

            if (_cooldownTimer > 0f) { _pendingNoiseWake = false; return; }

            if (_pendingNoiseWake) { _pendingNoiseWake = false; Wake(false); return; }

            if (havePlayer && !(crouching && crouchSneaksPast))
            {
                Vector3 pp = pt.position;
                if (StealthUtil.HorizontalDistance(pp, transform.position) <= wakeRadius && Mathf.Abs(pp.y - transform.position.y) < 1.2f)
                    Wake(false);
            }
        }

        private void Wake(bool tail)
        {
            _state = CatState.Startled;
            _timer = 0f;
            _pendingNoiseWake = false;
            Meow(tail ? tailMeowLoudness : meowLoudness, tail);
            if (showToasts)
                GameEvents.RaiseToast(tail ? "МЯУ!!! Ты наступил коту на хвост!" : "Кот проснулся и громко мяукнул!");
        }

        private void Meow(float loudness, bool force)
        {
            if (!force && _meowTimer > 0f) return;
            _meowTimer = meowCooldown;
            GameEvents.RaiseNoise(new NoiseEvent(transform.position + Vector3.up * 0.3f, loudness, NoiseSource.Cat, gameObject));
        }

        private void TickStartled(float dt)
        {
            // Look at the elf (or just puff up) for a moment.
            if (StealthUtil.TryGetPlayer(out Transform pt, out _))
                _mover.FaceTowards(pt.position, dt, 1f);
            if (_timer < startledTime) return;

            _state = CatState.Wandering;
            _timer = 0f;
            _wanderTimer = 0f;
            _pauseTimer = 0f;
            if (_homeOnNavValid) _mover.EnableAt(_homeOnNav);
            PickWanderPoint();
        }

        private void TickWandering(float dt)
        {
            _wanderTimer += dt;

            // Annoyed cat meows again if a standing elf bumps into it.
            if (StealthUtil.TryGetPlayer(out Transform pt, out bool crouching) && !crouching &&
                StealthUtil.HorizontalDistance(pt.position, transform.position) <= awakeMeowRadius)
                Meow(meowLoudness, false);

            if (_wanderTimer >= wanderDuration)
            {
                StartReturn();
                return;
            }

            if (!_mover.AgentReady)
            {
                // No NavMesh: slowly turn around in place.
                transform.Rotate(0f, 40f * dt, 0f, Space.World);
                return;
            }

            if (_pauseTimer > 0f)
            {
                _pauseTimer -= dt;
                if (_pauseTimer <= 0f) PickWanderPoint();
                return;
            }

            if (_mover.Tick(dt))
            {
                _mover.Stop();
                _pauseTimer = wanderPause;
            }
        }

        private void PickWanderPoint()
        {
            if (!_mover.AgentReady) return;
            Vector3 centre = _homeOnNavValid ? _homeOnNav : _sleepPos;
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                if (NavMover.Sample(centre + new Vector3(r.x, 0f, r.y), 1f, out Vector3 p))
                {
                    _mover.SetTarget(p, wanderSpeed);
                    return;
                }
            }
            _pauseTimer = wanderPause;
        }

        private void StartReturn()
        {
            _state = CatState.Returning;
            _timer = 0f;
            if (_mover.AgentReady) _mover.SetTarget(_homeOnNavValid ? _homeOnNav : _sleepPos, wanderSpeed);
        }

        private void TickReturning(float dt)
        {
            bool arrived = !_mover.AgentReady || _mover.Tick(dt);
            if (!arrived && _timer < returnTimeout) return;

            _mover.Disable();
            transform.SetPositionAndRotation(_sleepPos, _sleepRot); // hops back onto its spot
            _state = CatState.Sleeping;
            _timer = 0f;
            _cooldownTimer = wakeCooldown;
            _pendingNoiseWake = false;
        }

        private void UpdateVisual()
        {
            if (visual == null) return;
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
            Gizmos.DrawWireSphere(transform.position, wakeRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position - transform.forward * tailOffset, tailRadius);
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(Application.isPlaying ? _sleepPos : transform.position, wanderRadius);
        }
#endif
    }
}
