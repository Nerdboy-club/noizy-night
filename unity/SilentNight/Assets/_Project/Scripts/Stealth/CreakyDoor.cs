using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Physical creaky door ("Скрипучие двери с физическим открытием"): a door leaf (Rigidbody, ~6 kg, angular damping 2)
    /// on a vertical HingeJoint limited to ±100°, pushed open by elves walking into it (ElfController pushes
    /// non-kinematic bodies). Noise depends on the hinge angular speed:
    ///   slow (&lt; silentBelowSpeed, ~40°/s)  -> silent (crouch-push);
    ///   medium                              -> creak ~24..36 (NoiseSource.CreakyFloor = AudioManager "creak" clip);
    ///   fast (&gt; slamAboveSpeed, ~150°/s) or a hard stop at the limit/wall -> "БАХ!" 65 (NoiseSource.Object = "knock")
    ///   plus a HUD toast (with cooldown).
    ///
    /// Setup: <see cref="Create"/> builds everything (edit mode or play mode). Added by hand to an object it either uses a
    /// child HingeJoint's Rigidbody as the leaf, hinges its own BoxCollider on the local -X edge, or builds a default leaf
    /// from width/height with the hinge at this transform (floor level) and the leaf extending along local +X.
    /// Everything is put on the Interactable layer so the NavMesh bake ignores the door.
    /// </summary>
    [DisallowMultipleComponent]
    public class CreakyDoor : MonoBehaviour
    {
        /// <summary>Gap between the leaf and the jambs (> half the leaf thickness so the hinge-side corners never scrape).</summary>
        public const float JambClearance = 0.03f;
        public const float FloorClearance = 0.015f;
        public const float TopClearance = 0.02f;

        [Header("Leaf (built at runtime when empty)")]
        [SerializeField] private Rigidbody leaf;
        [Tooltip("Doorway width (m). The leaf is this minus 2 x JambClearance.")]
        [SerializeField] private float width = 1.4f;
        [Tooltip("Doorway height (m). The leaf is this minus floor/top clearance.")]
        [SerializeField] private float height = 2.2f;
        [SerializeField] private float thickness = 0.04f;
        [SerializeField] private Material material;

        [Header("Physics")]
        [SerializeField] private float leafMass = 6f;
        [SerializeField] private float leafAngularDamping = 2f;
        [SerializeField] private float limitDegrees = 100f;
        [Tooltip("Weak spring pulling the door back to closed (off by default).")]
        [SerializeField] private bool springToClosed = false;
        [SerializeField] private float closeSpring = 1.5f;
        [SerializeField] private float closeSpringDamper = 0.3f;

        [Header("Noise by hinge angular speed (deg/s)")]
        [SerializeField] private float silentBelowSpeed = 40f;
        [SerializeField] private float slamAboveSpeed = 150f;
        [SerializeField, Range(0f, 100f)] private float creakLoudnessMin = 24f;
        [SerializeField, Range(0f, 100f)] private float creakLoudnessMax = 36f;
        [SerializeField] private float creakInterval = 0.5f;
        [Tooltip("CreakyFloor makes AudioManager play the 'creak' clip.")]
        [SerializeField] private NoiseSource creakSource = NoiseSource.CreakyFloor;
        [Tooltip("Hard stop (joint limit, wall, furniture): speed before the stop must be at least this...")]
        [SerializeField] private float impactMinSpeed = 90f;
        [Tooltip("...and at least this fraction of it must be lost in one physics step.")]
        [SerializeField, Range(0.1f, 1f)] private float impactSpeedDrop = 0.6f;
        [SerializeField, Range(0f, 100f)] private float slamLoudness = 65f;
        [Tooltip("Object makes AudioManager play the 'knock' clip (БАХ!).")]
        [SerializeField] private NoiseSource slamSource = NoiseSource.Object;
        [SerializeField] private float slamCooldown = 1.5f;
        [SerializeField] private string slamToast = "Дверь хлопнула на весь дом!";
        [SerializeField] private float slamToastCooldown = 5f;

        private HingeJoint _hinge;
        private bool _ready;
        private float _prevSpeed;
        private float _creakTimer;
        private float _slamTimer;
        private float _toastTimer;

        private static bool s_creating;

        // ---------------- public read API (HUD / tests) ----------------
        /// <summary>Loudness (0..100) of the last noise this door raised; 0 if none yet.</summary>
        public float LastNoise { get; private set; }
        public NoiseSource LastNoiseSource { get; private set; }
        /// <summary>Time.time of the last noise, or -1.</summary>
        public float LastNoiseTime { get; private set; } = -1f;
        public int NoiseCount { get; private set; }
        public int SlamCount { get; private set; }
        /// <summary>|hinge angular speed| in deg/s, measured in the last FixedUpdate.</summary>
        public float AngularSpeed => _prevSpeed;
        /// <summary>Hinge angle in degrees (0 = closed).</summary>
        public float Angle => _hinge != null ? _hinge.angle : 0f;
        public Rigidbody Leaf => leaf;
        public HingeJoint Hinge => _hinge;
        public float SilentBelowSpeed => silentBelowSpeed;
        public float SlamAboveSpeed => slamAboveSpeed;

        /// <summary>
        /// Build a door in a doorway (works in edit mode for the scene builder and at runtime).
        /// <paramref name="hingeWorldPos"/> = the hinge-side jamb at floor height; the closed leaf extends from it along
        /// Quaternion.Euler(0, yawDeg, 0) * Vector3.right for <paramref name="width"/> metres (yaw -90 = +Z, 90 = -Z,
        /// 0 = +X, 180 = -X). Width/height are the doorway size; clearances are subtracted internally.
        /// </summary>
        public static CreakyDoor Create(Transform parent, Vector3 hingeWorldPos, float width, float height, float yawDeg, Material mat)
        {
            var go = new GameObject("CreakyDoor");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(hingeWorldPos, Quaternion.Euler(0f, yawDeg, 0f));

            CreakyDoor door;
            s_creating = true;
            try { door = go.AddComponent<CreakyDoor>(); }
            finally { s_creating = false; }

            door.width = Mathf.Max(0.2f, width);
            door.height = Mathf.Max(0.3f, height);
            door.material = mat;
            door.Setup();
            return door;
        }

        private void Awake()
        {
            if (s_creating) return; // Create() fills the fields first, then calls Setup()
            Setup();
        }

        /// <summary>Find / build the leaf and (re)apply the physics settings. Idempotent.</summary>
        private void Setup()
        {
            if (leaf == null)
            {
                HingeJoint existing = GetComponentInChildren<HingeJoint>(true);
                if (existing != null) leaf = existing.GetComponent<Rigidbody>();
            }

            bool newHinge = false;
            if (leaf == null)
            {
                BoxCollider ownBox = GetComponent<BoxCollider>();
                if (ownBox != null && !ownBox.isTrigger)
                {
                    leaf = GetComponent<Rigidbody>();
                    if (leaf == null) leaf = gameObject.AddComponent<Rigidbody>();
                    _hinge = gameObject.AddComponent<HingeJoint>();
                    _hinge.anchor = ownBox.center + new Vector3(-ownBox.size.x * 0.5f, 0f, 0f);
                    newHinge = true;
                }
                else
                {
                    BuildLeaf();
                    newHinge = true;
                }
            }

            if (_hinge == null) _hinge = leaf.GetComponent<HingeJoint>();
            if (_hinge == null)
            {
                _hinge = leaf.gameObject.AddComponent<HingeJoint>();
                newHinge = true;
            }
            if (newHinge)
            {
                _hinge.axis = Vector3.up;
                _hinge.connectedBody = null; // hinged to the world
                _hinge.autoConfigureConnectedAnchor = true;
                _hinge.enableCollision = false;
            }

            ApplyPhysics();
            int layer = LayerMask.NameToLayer(GameConstants.LayerInteractable);
            if (layer >= 0)
            {
                StealthUtil.SetLayerRecursive(gameObject, layer);
                if (!leaf.transform.IsChildOf(transform)) StealthUtil.SetLayerRecursive(leaf.gameObject, layer);
            }
            _ready = true;
        }

        private void BuildLeaf()
        {
            float leafW = Mathf.Max(0.1f, width - 2f * JambClearance);
            float leafH = Mathf.Max(0.1f, height - FloorClearance - TopClearance);
            float t = Mathf.Max(0.01f, thickness);

            var go = new GameObject("DoorLeaf");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(JambClearance + leafW * 0.5f, FloorClearance + leafH * 0.5f, 0f);
            go.transform.localRotation = Quaternion.identity;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(leafW, leafH, t);

            AddVisual(go.transform, "Mesh", PrimitiveType.Cube, Vector3.zero, new Vector3(leafW, leafH, t));
            // Knobs on both faces near the free edge (human height: elves can't reach them, they have to push).
            float knobX = leafW * 0.5f - 0.09f;
            float knobY = 1.0f - (FloorClearance + leafH * 0.5f);
            AddVisual(go.transform, "Knob_A", PrimitiveType.Sphere, new Vector3(knobX, knobY, t * 0.5f + 0.03f), Vector3.one * 0.07f);
            AddVisual(go.transform, "Knob_B", PrimitiveType.Sphere, new Vector3(knobX, knobY, -t * 0.5f - 0.03f), Vector3.one * 0.07f);

            leaf = go.AddComponent<Rigidbody>();
            _hinge = go.AddComponent<HingeJoint>();
            _hinge.anchor = new Vector3(-leafW * 0.5f, 0f, 0f); // hinge-side edge of the leaf
        }

        private void AddVisual(Transform parent, string name, PrimitiveType prim, Vector3 localPos, Vector3 scale)
        {
            GameObject v = GameObject.CreatePrimitive(prim);
            v.name = name;
            Collider c = v.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c); // must be gone before the leaf Rigidbody is added (compound collider)
            v.transform.SetParent(parent, false);
            v.transform.localPosition = localPos;
            v.transform.localRotation = Quaternion.identity;
            v.transform.localScale = scale;
            Renderer r = v.GetComponent<Renderer>();
            if (r != null && material != null) r.sharedMaterial = material;
        }

        private void ApplyPhysics()
        {
            leaf.isKinematic = false;
            leaf.useGravity = true;
            leaf.mass = Mathf.Max(0.1f, leafMass);
            leaf.linearDamping = 0.05f;
            leaf.angularDamping = Mathf.Max(0f, leafAngularDamping);
            leaf.interpolation = RigidbodyInterpolation.Interpolate;
            leaf.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            leaf.solverIterations = Mathf.Max(leaf.solverIterations, 12);

            float lim = Mathf.Clamp(limitDegrees, 1f, 177f);
            _hinge.useLimits = true;
            _hinge.limits = new JointLimits
            {
                min = -lim,
                max = lim,
                bounciness = 0f,
                bounceMinVelocity = 0.2f,
                contactDistance = 0f
            };
            _hinge.useSpring = springToClosed;
            _hinge.spring = new JointSpring
            {
                spring = Mathf.Max(0f, closeSpring),
                damper = Mathf.Max(0f, closeSpringDamper),
                targetPosition = 0f
            };
            _hinge.useMotor = false;
        }

        // ---------------- noise ----------------

        private void FixedUpdate()
        {
            if (!_ready || leaf == null || _hinge == null) return;
            float dt = Time.fixedDeltaTime;
            if (_creakTimer > 0f) _creakTimer -= dt;
            if (_slamTimer > 0f) _slamTimer -= dt;
            if (_toastTimer > 0f) _toastTimer -= dt;

            float speed = Mathf.Abs(SignedAngularSpeed());
            float prev = _prevSpeed;
            _prevSpeed = speed;

            bool hardStop = prev >= impactMinSpeed && speed <= prev * (1f - impactSpeedDrop);
            if (speed >= slamAboveSpeed || hardStop)
            {
                if (_slamTimer <= 0f) Slam();
                return;
            }

            if (speed >= silentBelowSpeed && _creakTimer <= 0f && _slamTimer <= 0f)
            {
                _creakTimer = creakInterval;
                float t = Mathf.InverseLerp(silentBelowSpeed, slamAboveSpeed, speed);
                Emit(Mathf.Lerp(creakLoudnessMin, creakLoudnessMax, t), creakSource);
            }
        }

        /// <summary>Angular speed about the hinge axis in deg/s (sign = opening direction).</summary>
        private float SignedAngularSpeed()
        {
            Vector3 axis = leaf.transform.TransformDirection(_hinge.axis);
            if (axis.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Dot(leaf.angularVelocity, axis.normalized) * Mathf.Rad2Deg;
        }

        private void Slam()
        {
            _slamTimer = slamCooldown;
            _creakTimer = creakInterval;
            SlamCount++;
            Emit(slamLoudness, slamSource);
            if (_toastTimer <= 0f && !string.IsNullOrEmpty(slamToast))
            {
                _toastTimer = slamToastCooldown;
                GameEvents.RaiseToast(slamToast);
            }
        }

        private void Emit(float loudness, NoiseSource source)
        {
            loudness = Mathf.Clamp(loudness, 0f, 100f);
            LastNoise = loudness;
            LastNoiseSource = source;
            LastNoiseTime = Time.time;
            NoiseCount++;
            GameEvents.RaiseNoise(new NoiseEvent(leaf.worldCenterOfMass, loudness, source, gameObject));
        }

        // ---------------- test / scripting helpers ----------------

        /// <summary>Spin the leaf about its hinge at this angular speed (deg/s, sign = direction). For tests and scripted events.</summary>
        public void SetAngularSpeed(float degPerSecond)
        {
            if (leaf == null || _hinge == null) return;
            Vector3 axis = leaf.transform.TransformDirection(_hinge.axis).normalized;
            Vector3 omega = axis * (degPerSecond * Mathf.Deg2Rad);
            Vector3 pivot = leaf.transform.TransformPoint(_hinge.anchor);
            leaf.WakeUp();
            leaf.angularVelocity = omega;
            leaf.linearVelocity = Vector3.Cross(omega, leaf.worldCenterOfMass - pivot); // rigid rotation about the hinge
            _prevSpeed = Mathf.Abs(degPerSecond); // not a "hard stop" on the next step
        }

        /// <summary>Forget the last noise and all cooldowns.</summary>
        public void ResetNoise()
        {
            LastNoise = 0f;
            LastNoiseTime = -1f;
            NoiseCount = 0;
            SlamCount = 0;
            _creakTimer = 0f;
            _slamTimer = 0f;
            _toastTimer = 0f;
        }
    }
}
