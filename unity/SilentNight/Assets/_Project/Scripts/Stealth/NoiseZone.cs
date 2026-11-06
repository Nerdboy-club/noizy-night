using UnityEngine;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Trigger volume that makes extra noise when the elf moves through it
    /// (creaky floorboard, squeaky toy, wind chime / clock area).
    /// Needs a trigger Collider; the player is detected by tag GameConstants.TagPlayer
    /// (on the collider or its attached Rigidbody).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class NoiseZone : MonoBehaviour
    {
        [SerializeField] private NoiseSource source = NoiseSource.CreakyFloor;
        [SerializeField, Range(0f, 100f)] private float loudness = 45f;
        [Tooltip("Minimum seconds between two noises from this zone.")]
        [SerializeField] private float cooldown = 1.2f;
        [Tooltip("Player must move this far inside the zone to trigger again (standing still is silent).")]
        [SerializeField] private float minMoveDistance = 0.25f;
        [SerializeField, Range(0f, 1f)] private float crouchMultiplier = 0.6f;
        [SerializeField] private bool emitOnEnter = true;
        [SerializeField, Range(0f, 1f)] private float chance = 1f;
        [Tooltip("Optional HUD toast when triggered (empty = none).")]
        [SerializeField] private string toastText = "";
        [Tooltip("Adds a kinematic Rigidbody so triggers fire even against a CharacterController/static setup.")]
        [SerializeField] private bool ensureKinematicRigidbody = true;

        private float _cooldownTimer;
        private Vector3 _lastPlayerPos;
        private bool _tracking;
        private int _insideCount;

        public void Configure(NoiseSource noiseSource, float noiseLoudness, float noiseCooldown)
        {
            source = noiseSource;
            loudness = Mathf.Clamp(noiseLoudness, 0f, 100f);
            cooldown = Mathf.Max(0f, noiseCooldown);
        }

        private void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        private void Awake()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
            if (ensureKinematicRigidbody && GetComponent<Rigidbody>() == null)
            {
                Rigidbody rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private void Update()
        {
            if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!StealthUtil.IsPlayerCollider(other)) return;
            _insideCount++;
            Vector3 p = PlayerPosition(other);
            if (!_tracking)
            {
                _tracking = true;
                _lastPlayerPos = p;
                if (emitOnEnter) TryEmit(p);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!_tracking || !StealthUtil.IsPlayerCollider(other)) return;
            Vector3 p = PlayerPosition(other);
            if ((p - _lastPlayerPos).sqrMagnitude < minMoveDistance * minMoveDistance) return;
            _lastPlayerPos = p;
            TryEmit(p);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!StealthUtil.IsPlayerCollider(other)) return;
            _insideCount = Mathf.Max(0, _insideCount - 1);
            if (_insideCount == 0) _tracking = false;
        }

        private void OnDisable()
        {
            _insideCount = 0;
            _tracking = false;
        }

        private static Vector3 PlayerPosition(Collider c)
        {
            Rigidbody rb = c.attachedRigidbody;
            return rb != null ? rb.position : c.transform.position;
        }

        private void TryEmit(Vector3 at)
        {
            if (_cooldownTimer > 0f || !StealthUtil.IsGameplayActive) return;
            if (chance < 1f && Random.value > chance) return;
            _cooldownTimer = cooldown;

            float l = loudness;
            if (StealthUtil.TryGetPlayer(out _, out bool crouching) && crouching) l *= crouchMultiplier;
            GameEvents.RaiseNoise(new NoiseEvent(at, l, source, gameObject));
            if (!string.IsNullOrEmpty(toastText)) GameEvents.RaiseToast(toastText);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Collider c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
            Bounds b = c.bounds;
            Gizmos.DrawCube(b.center, b.size);
        }
#endif
    }
}
