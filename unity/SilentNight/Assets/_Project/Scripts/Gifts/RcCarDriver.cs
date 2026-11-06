using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Self-driving behaviour of the RcCar gift (a wrapped radio-controlled toy car used as a distraction).
    /// Added and driven by <see cref="Gift"/>: OnDropped arms it, ~1 s after it settles on the ground it drives
    /// forward (local +Z) for a few seconds, buzzing loudly to lure Grandpa and the cat, turning on walls.
    /// Picking it up / storing it in the sack stops it. Limited number of runs (battery).
    /// </summary>
    [DisallowMultipleComponent]
    public class RcCarDriver : MonoBehaviour
    {
        [Header("Run")]
        [SerializeField] private float startDelay = 1f;          // seconds settled on the ground before it switches on
        [SerializeField] private float settleSpeed = 0.35f;      // m/s: below this it counts as settled
        [SerializeField] private float runDuration = 7f;
        [SerializeField] private float driveSpeed = 1.6f;        // m/s along forward on XZ
        [SerializeField] private int maxRuns = 2;

        [Header("Turning")]
        [SerializeField] private float minTurn = 90f;
        [SerializeField] private float maxTurn = 140f;
        [SerializeField] private float turnSpeed = 360f;         // deg/s
        [SerializeField] private float turnCooldown = 0.35f;
        [SerializeField] private float wallProbe = 0.12f;        // extra ray length beyond the nose
        [SerializeField] private float stuckSpeed = 0.3f;        // actual speed below this while driving = stuck
        [SerializeField] private float stuckTime = 0.5f;

        [Header("Noise")]
        [SerializeField] private float buzzInterval = 0.8f;
        [SerializeField] private float buzzLoudness = 42f;

        [Header("Ground")]
        [SerializeField] private float groundProbeExtra = 0.1f;

        public bool IsDriving { get; private set; }
        public bool IsArmed { get; private set; }
        public int RunsLeft => Mathf.Max(0, maxRuns - _runsUsed);

        private Gift _gift;
        private Rigidbody _rb;
        private GiftVisual _visual;
        private float _settleTimer;
        private float _runLeft;
        private float _buzzTimer;
        private float _turnCooldownTimer;
        private float _stuckTimer;
        private float _yaw;
        private float _targetYaw;
        private int _runsUsed;
        private bool _deadToastShown;
        private RigidbodyConstraints _savedConstraints;
        private PhysicsMaterial _savedMaterial;

        private static PhysicsMaterial _slick;

        public void Init(Gift gift, Rigidbody rb, GiftVisual visual)
        {
            _gift = gift;
            _rb = rb;
            _visual = visual;
        }

        /// <summary>Called by Gift.OnDropped.</summary>
        public void Arm()
        {
            if (IsDriving) StopRun(false);
            if (RunsLeft <= 0)
            {
                IsArmed = false;
                if (!_deadToastShown)
                {
                    _deadToastShown = true;
                    GameEvents.RaiseToast("Батарейка села.");
                }
                return;
            }
            IsArmed = true;
            _settleTimer = 0f;
        }

        /// <summary>Called on pick up / sack / delivery / kind change.</summary>
        public void Disarm()
        {
            if (IsDriving) StopRun(false);
            IsArmed = false;
            _settleTimer = 0f;
        }

        private void OnDisable() => Disarm();

        private bool CanAct => _gift != null && _rb != null && !_rb.isKinematic
                               && !_gift.IsHeld && !_gift.IsInSack && !_gift.IsDelivered;

        private void Update()
        {
            if (!IsDriving) return;
            if (!CanAct) { StopRun(false); IsArmed = false; return; }

            float dt = Time.deltaTime;
            _buzzTimer -= dt;
            if (_buzzTimer <= 0f)
            {
                _buzzTimer = buzzInterval;
                Buzz();
            }

            if (_visual != null && _visual.RcAntenna != null)
                _visual.RcAntenna.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 28f) * 9f, 0f, Mathf.Sin(Time.time * 19f) * 6f);

            _runLeft -= dt;
            if (_runLeft <= 0f) StopRun(true);
        }

        private void FixedUpdate()
        {
            if (_gift == null || _rb == null) return;
            if (!CanAct)
            {
                if (IsDriving) StopRun(false);
                return;
            }

            float dt = Time.fixedDeltaTime;
            bool grounded = IsGrounded();

            if (!IsDriving)
            {
                if (!IsArmed) return;
                if (grounded && _rb.linearVelocity.magnitude < settleSpeed)
                {
                    _settleTimer += dt;
                    if (_settleTimer >= startDelay) StartRun();
                }
                else _settleTimer = 0f;
                return;
            }

            // --- driving ---
            if (_turnCooldownTimer > 0f) _turnCooldownTimer -= dt;

            // Yaw via angular velocity (X/Z rotation frozen while driving, so it stays on its wheels).
            _yaw = _rb.rotation.eulerAngles.y;
            float delta = Mathf.DeltaAngle(_yaw, _targetYaw);
            bool turning = Mathf.Abs(delta) > 3f;
            float yawRate = turning ? Mathf.Clamp(delta / dt, -turnSpeed, turnSpeed) : 0f; // deg/s
            _rb.angularVelocity = new Vector3(0f, yawRate * Mathf.Deg2Rad, 0f);

            if (!grounded) { _stuckTimer = 0f; return; } // airborne: let gravity have it

            Vector3 fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

            // Look ahead: wall right in front of the nose -> turn before bonking.
            if (!turning && _turnCooldownTimer <= 0f)
            {
                float nose = _visual != null ? _visual.ColliderSize.z * 0.5f * transform.lossyScale.z : 0.16f;
                if (Physics.Raycast(_rb.position, fwd, out RaycastHit hit, nose + wallProbe, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.rigidbody != _rb && Mathf.Abs(hit.normal.y) < 0.5f)
                    Turn();
            }

            float speed = turning ? driveSpeed * 0.4f : driveSpeed;
            Vector3 v = _rb.linearVelocity;
            Vector3 drive = fwd * speed;
            _rb.linearVelocity = new Vector3(drive.x, v.y, drive.z);

            // Stuck detection: pushing but not moving (stuck against something the probe missed).
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            if (!turning && flat.magnitude < stuckSpeed)
            {
                _stuckTimer += dt;
                if (_stuckTimer >= stuckTime) { _stuckTimer = 0f; Turn(); }
            }
            else _stuckTimer = 0f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsDriving || _turnCooldownTimer > 0f || collision.contactCount == 0) return;
            Vector3 n = collision.GetContact(0).normal;
            if (Mathf.Abs(n.y) > 0.5f) return; // floor/ceiling
            Vector3 fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Vector3 nFlat = new Vector3(n.x, 0f, n.z).normalized;
            if (Mathf.Abs(Vector3.Dot(nFlat, fwd)) < 0.3f) return; // side scrape
            Turn();
        }

        private void StartRun()
        {
            IsArmed = false;
            IsDriving = true;
            _runsUsed++;
            _runLeft = runDuration;
            _buzzTimer = buzzInterval;
            _turnCooldownTimer = 0f;
            _stuckTimer = 0f;

            // Stand upright facing its flattened forward (cartoon "hop" back on its wheels).
            Vector3 f = transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) { f = transform.up; f.y = 0f; }
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            _yaw = Quaternion.LookRotation(f.normalized, Vector3.up).eulerAngles.y;
            _targetYaw = _yaw;

            _savedConstraints = _rb.constraints;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.angularVelocity = Vector3.zero;
            _rb.rotation = Quaternion.Euler(0f, _yaw, 0f);

            var col = _visual != null ? _visual.MainCollider : null;
            if (col != null)
            {
                _savedMaterial = col.sharedMaterial;
                col.sharedMaterial = Slick();
            }

            Buzz();
            GameEvents.RaiseToast("Вжжж! Машинка поехала шуметь в другую сторону.");
        }

        private void StopRun(bool natural)
        {
            if (!IsDriving) return;
            IsDriving = false;
            IsArmed = false;
            if (_rb != null) _rb.constraints = _savedConstraints;
            var col = _visual != null ? _visual.MainCollider : null;
            if (col != null) col.sharedMaterial = _savedMaterial;
            _savedMaterial = null;
            if (_visual != null && _visual.RcAntenna != null) _visual.RcAntenna.localRotation = Quaternion.identity;

            if (natural && RunsLeft <= 0 && !_deadToastShown)
            {
                _deadToastShown = true;
                GameEvents.RaiseToast("Батарейка села.");
            }
        }

        private void Turn()
        {
            float a = Random.Range(minTurn, maxTurn) * (Random.value < 0.5f ? -1f : 1f);
            _targetYaw = _yaw + a;
            _turnCooldownTimer = turnCooldown;
            _stuckTimer = 0f;
        }

        private void Buzz()
        {
            GameEvents.RaiseNoise(new NoiseEvent(transform.position, buzzLoudness, NoiseSource.Object, gameObject));
        }

        private bool IsGrounded()
        {
            float halfH = _visual != null ? _visual.ColliderSize.y * 0.5f * transform.lossyScale.y : 0.08f;
            // Ray starts inside our own collider, so it never hits ourselves.
            return Physics.Raycast(_rb.position, Vector3.down, halfH + groundProbeExtra, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private static PhysicsMaterial Slick()
        {
            if (_slick != null) return _slick;
            _slick = new PhysicsMaterial("RcCarSlick")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            return _slick;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _slick = null;
    }
}
