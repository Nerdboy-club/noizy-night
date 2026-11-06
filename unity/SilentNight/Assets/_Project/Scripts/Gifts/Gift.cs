using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// A physical gift. Purely ICarryable: the player's ElfInteractor/ElfCarry detect and drive it.
    /// Small/Noisy fit in the sack, Huge (wrapped bike, ~12 kg) must be carried.
    /// Integrity drops on hard impacts; impacts and the wind-up toy inside Noisy gifts make noise.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(GiftVisual))]
    public class Gift : MonoBehaviour, ICarryable
    {
        [Header("Kind")]
        [SerializeField] private GiftKind kind = GiftKind.Small;
        [SerializeField, Range(0f, 100f)] private float integrity = 100f;

        [Header("Mass (kg)")]
        [SerializeField] private float massSmall = 1.0f;
        [SerializeField] private float massNoisy = 1.4f;
        [SerializeField] private float massHuge = 12f;
        [SerializeField] private float massFragile = 0.8f;
        [SerializeField] private float massRcCar = 0.9f;
        [SerializeField] private float linearDamping = 0.4f;
        [SerializeField] private float angularDamping = 0.6f;

        [Header("Damage (prototype: speed > 4.2, dmg = (speed-3.5)*4, min 25%)")]
        [SerializeField] private float damageSpeedThreshold = 4.2f;
        [SerializeField] private float damageSpeedOffset = 3.5f;
        [SerializeField] private float damagePerSpeed = 4f;
        [SerializeField] private float minIntegrity = 25f;
        [SerializeField] private float damageCooldown = 0.8f;
        [Tooltip("Each Grip upgrade level reduces damage by this fraction (capped at 0.75).")]
        [SerializeField] private float gripDamageReductionPerLevel = 0.15f;

        [Header("Impact noise")]
        [Tooltip("Impacts slower than this are silent.")]
        [SerializeField] private float noiseSpeedThreshold = 1.5f;
        [SerializeField] private float noisePerSpeed = 10f;
        [SerializeField] private float maxImpactNoise = 75f;
        [Tooltip("Loudness multiplier for the Huge gift (heavy bike clanks louder).")]
        [SerializeField] private float hugeNoiseMultiplier = 1.3f;
        [SerializeField] private float noiseCooldown = 0.25f;

        [Header("Noisy gift (wind-up toy)")]
        [SerializeField] private float tickInterval = 4.2f;
        [SerializeField] private float tickLoudness = 38f;
        [SerializeField] private float windupKeySpinSpeed = 230f; // deg/s
        [Tooltip("Seconds of soft ticking after a wind-up; then the box rings loudly. Picking it up re-winds it.")]
        [SerializeField] private float windupDuration = 35f;
        [SerializeField] private float ringLoudness = 62f;
        [SerializeField] private int ringBursts = 4;
        [SerializeField] private float ringInterval = 0.45f;

        [Header("Fragile gift (glass snow globe)")]
        [Tooltip("Impact speed that shatters the globe (a careful drop survives, a fall from a tumbling elf does not).")]
        [SerializeField] private float fragileBreakSpeed = 3.4f;
        [SerializeField] private float shatterLoudness = 82f;

        [Header("Drop")]
        [Tooltip("Seconds the gift ignores the holder's colliders after being dropped/thrown.")]
        [SerializeField] private float ignoreHolderAfterDrop = 0.5f;
        [Tooltip("Gifts that fall below this Y are considered lost and destroyed.")]
        [SerializeField] private float killY = -30f;

        // ---------------- ICarryable ----------------
        public GameObject GameObject => gameObject;
        public GiftKind Kind => kind;
        public float Mass => _rb != null ? _rb.mass : MassFor(kind);
        public bool FitsInSack => kind != GiftKind.Huge;
        public float Integrity => integrity;

        // ---------------- state ----------------
        public bool IsHeld { get; private set; }
        public bool IsInSack { get; private set; }
        public bool IsDelivered { get; private set; }
        public bool IsBroken { get; private set; }
        /// <summary>Seconds until the music box rings (Noisy only).</summary>
        public float WindLeft => _windLeft;
        /// <summary>Player-facing name (Russian).</summary>
        public string Label => LabelFor(kind);
        /// <summary>RcCar only: currently driving on its own (distraction run).</summary>
        public bool IsSelfDriving => _rcCar != null && _rcCar.enabled && _rcCar.IsDriving;

        private Rigidbody _rb;
        private GiftVisual _visual;
        private RcCarDriver _rcCar;
        private Collider[] _colliders;
        private Renderer[] _renderers;
        private readonly List<Collider> _holderColliders = new List<Collider>(8);
        private float _ignoreHolderTimer;
        private float _damageTimer;
        private float _noiseTimer;
        private float _tickTimer;
        private float _windLeft;
        private int _ringLeft;
        private float _ringTimer;

        public static string LabelFor(GiftKind k) => k switch
        {
            GiftKind.Small => "Маленький подарок",
            GiftKind.Noisy => "Заводная шумная шкатулка",
            GiftKind.Huge => "Огромный велосипед в обёртке",
            GiftKind.Fragile => "Стеклянный снежный шар",
            GiftKind.RcCar => "Радиоуправляемая машинка",
            _ => "Подарок"
        };

        public float MassFor(GiftKind k) => k switch
        {
            GiftKind.Small => massSmall,
            GiftKind.Noisy => massNoisy,
            GiftKind.Huge => massHuge,
            GiftKind.Fragile => massFragile,
            GiftKind.RcCar => massRcCar,
            _ => massSmall
        };

        /// <summary>Set the kind BEFORE the object is activated (GiftFactory does this on an inactive object).</summary>
        public void Configure(GiftKind newKind, float startIntegrity = 100f)
        {
            kind = newKind;
            integrity = Mathf.Clamp(startIntegrity, 0f, 100f);
            if (_rb != null) Rebuild();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _visual = GetComponent<GiftVisual>();
            Rebuild();
            _tickTimer = Random.Range(0f, tickInterval);
            _windLeft = windupDuration * Random.Range(0.6f, 1f);
        }

        private void Rebuild()
        {
            _visual.Build(kind);
            _colliders = new Collider[] { _visual.MainCollider };
            _renderers = _visual.Renderers;

            int giftLayer = LayerMask.NameToLayer(GameConstants.LayerGift);
            if (giftLayer >= 0) SetLayerRecursive(transform, giftLayer);

            _rb.mass = MassFor(kind);
            _rb.linearDamping = linearDamping;   // Unity 6 name (was .drag)
            _rb.angularDamping = angularDamping; // Unity 6 name (was .angularDrag)
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Speculative works for both kinematic and dynamic bodies (no warnings when toggling isKinematic).
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.isKinematic = false;
            _rb.useGravity = true;

            // RcCar: self-driving distraction helper. Other kinds keep it disabled (if the kind changed).
            if (kind == GiftKind.RcCar)
            {
                if (_rcCar == null) _rcCar = GetComponent<RcCarDriver>();
                if (_rcCar == null) _rcCar = gameObject.AddComponent<RcCarDriver>();
                _rcCar.Init(this, _rb, _visual);
                _rcCar.enabled = true;
            }
            else if (_rcCar != null)
            {
                _rcCar.Disarm();
                _rcCar.enabled = false;
            }
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_damageTimer > 0f) _damageTimer -= dt;
            if (_noiseTimer > 0f) _noiseTimer -= dt;

            if (_ignoreHolderTimer > 0f)
            {
                _ignoreHolderTimer -= dt;
                if (_ignoreHolderTimer <= 0f) SetIgnoreHolder(false);
            }

            // Wind-up toy ticking: audible whenever the gift is out of the sack and not yet delivered.
            if (kind == GiftKind.Noisy && !IsInSack && !IsDelivered)
            {
                if (_visual.WindupKey != null)
                    _visual.WindupKey.Rotate(0f, 0f, windupKeySpinSpeed * dt, Space.Self);

                _tickTimer += dt;
                if (_tickTimer >= tickInterval)
                {
                    _tickTimer = 0f;
                    GameEvents.RaiseNoise(new NoiseEvent(transform.position, tickLoudness * 0.6f, NoiseSource.NoisyGift, gameObject));
                }

                // The spring runs down: then it rings like an alarm clock until re-wound (pick it up) or put in the sack.
                if (_ringLeft <= 0)
                {
                    _windLeft -= dt;
                    if (_windLeft <= 0f)
                    {
                        _ringLeft = ringBursts;
                        _ringTimer = 0f;
                        GameEvents.RaiseToast("ДЗЗЗЫНЬ! Заводная шкатулка затрезвонила. Подкрутите ключ: возьмите её в руки.");
                    }
                }
                else
                {
                    _ringTimer -= dt;
                    if (_ringTimer <= 0f)
                    {
                        _ringTimer = ringInterval;
                        _ringLeft--;
                        GameEvents.RaiseNoise(new NoiseEvent(transform.position, ringLoudness, NoiseSource.NoisyGift, gameObject));
                        if (_ringLeft <= 0) _windLeft = windupDuration;
                    }
                }
            }

            if (!IsHeld && !IsInSack && transform.position.y < killY)
                Destroy(gameObject);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsHeld || IsInSack || IsDelivered) return;

            float speed = collision.relativeVelocity.magnitude;
            if (kind == GiftKind.Fragile && !IsBroken && speed >= fragileBreakSpeed)
            {
                Shatter(collision.contactCount > 0 ? collision.GetContact(0).point : transform.position);
                return;
            }
            if (speed < noiseSpeedThreshold) return;

            // Noise scaled by impact speed.
            if (_noiseTimer <= 0f)
            {
                _noiseTimer = noiseCooldown;
                float loud = speed * noisePerSpeed * (kind == GiftKind.Huge ? hugeNoiseMultiplier : 1f);
                loud = Mathf.Min(loud, maxImpactNoise);
                Vector3 p = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
                GameEvents.RaiseNoise(new NoiseEvent(p, loud, NoiseSource.GiftImpact, gameObject));
            }

            // Packaging damage (skipped while the RC car drives itself: its own bumps don't wreck it).
            if (speed > damageSpeedThreshold && _damageTimer <= 0f && !IsSelfDriving)
            {
                _damageTimer = damageCooldown;
                int grip = GameServices.Profile != null ? GameServices.Profile.GripLevel : 0;
                float reduction = Mathf.Min(0.75f, grip * gripDamageReductionPerLevel);
                float dmg = Mathf.Round((speed - damageSpeedOffset) * damagePerSpeed * (1f - reduction));
                if (dmg > 0f && integrity > minIntegrity)
                {
                    integrity = Mathf.Max(minIntegrity, integrity - dmg);
                    GameEvents.RaiseGiftDamaged(this, integrity);
                }
            }
        }

        private void Shatter(Vector3 at)
        {
            IsBroken = true;
            integrity = 0f;
            _noiseTimer = noiseCooldown;
            GameEvents.RaiseNoise(new NoiseEvent(at, shatterLoudness, NoiseSource.GiftImpact, gameObject));
            GameEvents.RaiseGiftDamaged(this, integrity);
            GameEvents.RaiseToast("Дзынь! Снежный шар разбился. Доставить можно, но чаевых за него не будет.");
            _visual.ShowBroken();
        }

        // ---------------- ICarryable actions ----------------

        public void OnPickedUp(Transform holdPoint)
        {
            SetIgnoreHolder(false);
            IsHeld = true;
            IsInSack = false;
            if (kind == GiftKind.Noisy)
            {
                // Picking the music box up re-winds the key and stops any ringing.
                _windLeft = windupDuration;
                _ringLeft = 0;
            }
            if (_rcCar != null) _rcCar.Disarm(); // grabbing the RC car switches it off

            _rb.interpolation = RigidbodyInterpolation.None; // child of a moving rig: no interpolation fight
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;

            // Colliders off while held: no collisions with the player, no self-detection by the interactor.
            SetCollidersEnabled(false);
            SetRenderersEnabled(true);

            // Remember the holder's colliders so we can ignore them briefly after the drop.
            _holderColliders.Clear();
            if (holdPoint != null)
            {
                holdPoint.root.GetComponentsInChildren(true, _holderColliders);
                transform.SetParent(holdPoint, true);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }

        public void OnDropped(Vector3 velocity)
        {
            IsHeld = false;
            IsInSack = false;
            transform.SetParent(null, true);

            SetRenderersEnabled(true);
            SetCollidersEnabled(true);
            SetIgnoreHolder(true);
            _ignoreHolderTimer = ignoreHolderAfterDrop;

            _rb.isKinematic = false; // must be dynamic before setting velocity
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Vector3.zero;
            _rb.WakeUp();
            _damageTimer = 0.15f; // don't count the release itself as an impact

            // RcCar: arm it; it switches on ~1 s after it settles on the floor.
            if (kind == GiftKind.RcCar && _rcCar != null && _rcCar.enabled) _rcCar.Arm();
        }

        public void OnStoredInSack()
        {
            IsHeld = false;
            IsInSack = true;
            if (_rcCar != null) _rcCar.Disarm();
            SetIgnoreHolder(false);
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
            SetCollidersEnabled(false);
            SetRenderersEnabled(false);
        }

        /// <summary>Freeze in place under the tree (or in the sleigh). Not pickable anymore.</summary>
        public void MarkDelivered(Vector3 position, Quaternion rotation)
        {
            IsDelivered = true;
            IsHeld = false;
            IsInSack = false;
            if (_rcCar != null) _rcCar.Disarm();
            SetIgnoreHolder(false);
            transform.SetParent(null, true);
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.None;
            transform.SetPositionAndRotation(position, rotation);
            SetCollidersEnabled(false);
            SetRenderersEnabled(true);
        }

        /// <summary>Half extents of the main collider in world units (for placement).</summary>
        public Vector3 HalfExtents
        {
            get
            {
                Vector3 s = _visual != null ? _visual.ColliderSize : Vector3.one * 0.4f;
                Vector3 l = transform.lossyScale;
                return new Vector3(s.x * l.x, s.y * l.y, s.z * l.z) * 0.5f;
            }
        }

        // ---------------- helpers ----------------

        private void SetCollidersEnabled(bool on)
        {
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = on;
        }

        private void SetRenderersEnabled(bool on)
        {
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = on;
        }

        private void SetIgnoreHolder(bool ignore)
        {
            if (_holderColliders.Count == 0) return;
            for (int h = 0; h < _holderColliders.Count; h++)
            {
                var hc = _holderColliders[h];
                if (hc == null || !hc.enabled || !hc.gameObject.activeInHierarchy) continue;
                for (int i = 0; i < _colliders.Length; i++)
                {
                    var c = _colliders[i];
                    // IgnoreCollision logs an error if either collider is disabled/inactive.
                    if (c == null || c == hc || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    Physics.IgnoreCollision(c, hc, ignore);
                }
            }
            if (!ignore) _ignoreHolderTimer = 0f;
        }
    }
}
