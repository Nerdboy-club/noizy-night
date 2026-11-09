using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Stealth
{
    /// <summary>
    /// TV remote lying on the edge of the stand (Default layer, loose Rigidbody so elves push it).
    /// When it hits something with relative speed above <see cref="TriggerSpeed"/> (it fell / was knocked)
    /// it switches its TV on. One trigger per fall: cooldown <see cref="Cooldown"/> s.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class TvRemote : MonoBehaviour
    {
        public static readonly Vector3 Size = new Vector3(0.18f, 0.04f, 0.06f);

        [SerializeField] private NoisyTV tv;
        [SerializeField] private float triggerSpeed = 1.2f;
        [SerializeField] private float cooldown = 2f;
        [Tooltip("Ignore impacts right after spawn while the body settles.")]
        [SerializeField] private float armDelay = 0.75f;
        [SerializeField] private float mass = 0.2f;

        private Rigidbody _rb;
        private float _armTime;
        private float _nextAllowedTime;

        public NoisyTV Tv => tv;
        public Rigidbody Body => _rb != null ? _rb : (_rb = GetComponent<Rigidbody>());
        public float TriggerSpeed => triggerSpeed;
        public float Cooldown => cooldown;
        public int TriggerCount { get; private set; }

        public void SetTv(NoisyTV target) => tv = target;

        /// <summary>Create the box collider, visual and Rigidbody (called by NoisyTV.Create).</summary>
        internal void Build(Material mat)
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.size = Size;
            box.center = Vector3.zero;

            GameObject v = GameObject.CreatePrimitive(PrimitiveType.Cube);
            v.name = "Mesh";
            Collider c = v.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
            v.transform.SetParent(transform, false);
            v.transform.localScale = Size;
            Renderer r = v.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            v.layer = gameObject.layer;

            // Red power button.
            GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            btn.name = "Button";
            Collider bc = btn.GetComponent<Collider>();
            if (bc != null) DestroyImmediate(bc);
            btn.transform.SetParent(transform, false);
            btn.transform.localPosition = new Vector3(Size.x * 0.32f, Size.y * 0.5f + 0.004f, 0f);
            btn.transform.localScale = new Vector3(0.025f, 0.008f, 0.025f);
            Renderer br = btn.GetComponent<Renderer>();
            if (br != null && mat != null) br.sharedMaterial = mat;
            btn.layer = gameObject.layer;

            ConfigureBody();
        }

        private void ConfigureBody()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.mass = Mathf.Max(0.01f, mass);
            _rb.linearDamping = 0.05f;
            _rb.angularDamping = 0.2f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void Awake()
        {
            ConfigureBody();
        }

        private void OnEnable()
        {
            _armTime = Time.time + armDelay;
        }

        private void Start()
        {
            if (tv == null) tv = GetComponentInParent<NoisyTV>();
            if (tv == null) tv = FindNearestTv();
        }

        private NoisyTV FindNearestTv()
        {
            NoisyTV[] all = FindObjectsByType<NoisyTV>();
            NoisyTV best = null;
            float bestD = float.PositiveInfinity;
            Vector3 p = transform.position;
            for (int i = 0; i < all.Length; i++)
            {
                float d = (all[i].transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = all[i]; }
            }
            return best;
        }

        private void OnCollisionEnter(Collision collision)
        {
            NotifyImpact(collision.relativeVelocity.magnitude);
        }

        /// <summary>Report an impact of this relative speed (m/s). Public for tests / scripted events.</summary>
        public bool NotifyImpact(float relativeSpeed)
        {
            if (Time.time < _armTime) return false;
            if (relativeSpeed < triggerSpeed) return false;
            if (Time.time < _nextAllowedTime) return false;
            _nextAllowedTime = Time.time + cooldown;
            TriggerCount++;
            if (tv != null && !tv.IsOn) tv.TurnOn();
            return true;
        }

        /// <summary>Clear the spawn arm delay and cooldown (tests).</summary>
        public void Arm()
        {
            _armTime = 0f;
            _nextAllowedTime = 0f;
        }
    }
}
