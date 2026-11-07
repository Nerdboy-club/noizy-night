using System.Collections.Generic;
using UnityEngine;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>
    /// A hiding place (under a table / sofa / bed). Put it on a GameObject with a trigger BoxCollider that covers
    /// the free space under the furniture. A player whose feet are inside the box (and who is crouching, unless
    /// requiresCrouch is off) is ignored by VisionCone beyond its reveal distance.
    /// Uses an oriented-box test (works for rotated furniture), no physics callbacks, no per-frame allocations.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class HidingSpot : MonoBehaviour
    {
        [SerializeField] private bool requiresCrouch = true;
        [Tooltip("Shown in the toast: \"Спрятались {label}.\"")]
        [SerializeField] private string label = "под столом";
        [Tooltip("Minimum seconds between two entry toasts from this spot.")]
        [SerializeField] private float toastCooldown = 3f;

        /// <summary>Feet position is lifted by this much before the inside test (feet sit exactly on the floor).</summary>
        public const float FeetUpOffset = 0.1f;

        private static readonly List<HidingSpot> _spots = new List<HidingSpot>(16);

        private BoxCollider _box;
        private string _toastText;
        private bool _localHiddenHere;
        private float _nextToastTime;

        public static IReadOnlyList<HidingSpot> All => _spots;
        public bool RequiresCrouch { get => requiresCrouch; set => requiresCrouch = value; }
        public string Label => label;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _spots.Clear();

        /// <summary>Used by the scene builder.</summary>
        public void Configure(string spotLabel, bool needsCrouch)
        {
            label = spotLabel;
            requiresCrouch = needsCrouch;
            _toastText = BuildToast(label);
        }

        private void Awake()
        {
            _box = GetComponent<BoxCollider>();
            if (_box != null) _box.isTrigger = true;
            _toastText = BuildToast(label);
        }

        private void OnEnable()
        {
            if (_box == null) _box = GetComponent<BoxCollider>();
            if (!_spots.Contains(this)) _spots.Add(this);
            _localHiddenHere = false;
        }

        private void OnDisable()
        {
            _spots.Remove(this);
            _localHiddenHere = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            toastCooldown = Mathf.Max(0f, toastCooldown);
            _toastText = BuildToast(label);
        }
#endif

        private static string BuildToast(string l) => "Спрятались " + (string.IsNullOrEmpty(l) ? "" : l) + ".";

        /// <summary>True if this spot is active and its box contains the world point.</summary>
        public bool ContainsPoint(Vector3 worldPoint)
        {
            if (!isActiveAndEnabled || _box == null || !_box.enabled) return false;
            Vector3 local = transform.InverseTransformPoint(worldPoint) - _box.center;
            Vector3 h = _box.size * 0.5f;
            return Mathf.Abs(local.x) <= h.x && Mathf.Abs(local.y) <= h.y && Mathf.Abs(local.z) <= h.z;
        }

        /// <summary>True if the player (feet transform) is in this spot and satisfies its crouch rule.</summary>
        public bool Hides(Transform player, bool crouching)
        {
            if (player == null) return false;
            if (requiresCrouch && !crouching) return false;
            return ContainsPoint(player.position + Vector3.up * FeetUpOffset);
        }

        /// <summary>True if the player is hidden by any enabled spot.</summary>
        public static bool IsHidden(Transform player, bool crouching) => IsHidden(player, crouching, out _);

        public static bool IsHidden(Transform player, bool crouching, out HidingSpot spot)
        {
            spot = null;
            if (player == null) return false;
            for (int i = 0; i < _spots.Count; i++)
            {
                HidingSpot s = _spots[i];
                if (s == null) continue;
                if (s.Hides(player, crouching))
                {
                    spot = s;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Convenience overload for an IPlayerStatus.</summary>
        public static bool IsHidden(IPlayerStatus player)
        {
            if (!StealthUtil.IsValidPlayer(player, out Transform t)) return false;
            return IsHidden(t, player.IsCrouching, out _);
        }

        private void Update()
        {
            IPlayerStatus local = GameServices.LocalPlayer;
            bool hidden = StealthUtil.IsValidPlayer(local, out Transform t) && Hides(t, local.IsCrouching);
            if (hidden && !_localHiddenHere && Time.time >= _nextToastTime)
            {
                _nextToastTime = Time.time + toastCooldown;
                GameEvents.RaiseToast(_toastText);
            }
            _localHiddenHere = hidden;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            BoxCollider b = _box != null ? _box : GetComponent<BoxCollider>();
            if (b == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = _localHiddenHere ? new Color(0.3f, 1f, 0.4f, 0.35f) : new Color(0.3f, 0.6f, 1f, 0.2f);
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
