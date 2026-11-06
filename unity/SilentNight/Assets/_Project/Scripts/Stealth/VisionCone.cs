using UnityEngine;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Field-of-view check from an eye transform to the local player's chest.
    /// Returns a gradual visibility 0..1 (distance, angle, light) so the owner can fill a detection meter.
    /// </summary>
    [DisallowMultipleComponent]
    public class VisionCone : MonoBehaviour
    {
        [Header("Eye")]
        [SerializeField] private Transform eye;
        [SerializeField, Range(1f, 179f)] private float fovAngle = 80f;
        [SerializeField] private float range = 7.5f;
        [Tooltip("Within this radius the player is noticed regardless of angle/light (if line of sight).")]
        [SerializeField] private float nearSenseRadius = 1.1f;

        [Header("Target")]
        [SerializeField] private float chestHeight = 0.6f;
        [SerializeField] private float crouchChestHeight = 0.35f;
        [SerializeField, Range(0f, 1f)] private float crouchRangeMultiplier = 0.6f;

        [Header("Light")]
        [Tooltip("0 = fully lit room, 1 = pitch dark.")]
        [SerializeField, Range(0f, 1f)] private float darkness = 0.3f;
        [Tooltip("How much full darkness shortens the effective range (0.5 = half range).")]
        [SerializeField, Range(0f, 1f)] private float darknessRangePenalty = 0.5f;
        [Tooltip("Inside this cone a lit flashlight ignores darkness.")]
        [SerializeField, Range(1f, 179f)] private float flashlightAngle = 45f;

        [Header("Line of sight")]
        [SerializeField] private LayerMask obstacleMask = Physics.DefaultRaycastLayers;
        [Tooltip("Strip NPC / Player / Gift / CameraIgnore layers from the obstacle mask at runtime.")]
        [SerializeField] private bool autoExcludeActorLayers = true;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        private int _mask;

        public Transform Eye => eye != null ? eye : transform;
        public bool FlashlightOn { get; set; }
        public float Darkness { get => darkness; set => darkness = Mathf.Clamp01(value); }
        public float ViewRange { get => range; set => range = Mathf.Max(0.1f, value); }
        public float FovAngle { get => fovAngle; set => fovAngle = Mathf.Clamp(value, 1f, 179f); }
        public float LastVisibility { get; private set; }
        public Vector3 LastTargetPoint { get; private set; }

        /// <summary>Used by NPCFactory / scene builder.</summary>
        public void Configure(Transform eyeTransform, float fov, float viewRange)
        {
            eye = eyeTransform;
            fovAngle = Mathf.Clamp(fov, 1f, 179f);
            range = Mathf.Max(0.1f, viewRange);
        }

        private void Awake()
        {
            RebuildMask();
        }

        public void RebuildMask()
        {
            int m = obstacleMask.value;
            if (autoExcludeActorLayers)
            {
                m = StealthUtil.WithoutLayer(m, GameConstants.LayerNPC);
                m = StealthUtil.WithoutLayer(m, GameConstants.LayerPlayer);
                m = StealthUtil.WithoutLayer(m, GameConstants.LayerGift);
                m = StealthUtil.WithoutLayer(m, GameConstants.LayerCameraIgnore);
            }
            _mask = m;
        }

        /// <summary>Visibility of the local player 0..1 (0 if no player registered).</summary>
        public float Evaluate()
        {
            if (!StealthUtil.TryGetPlayer(out Transform pt, out bool crouching))
            {
                LastVisibility = 0f;
                return 0f;
            }
            LastVisibility = EvaluateTarget(pt, crouching);
            return LastVisibility;
        }

        public float EvaluateTarget(Transform target, bool crouching)
        {
            if (target == null) return 0f;
            Transform e = Eye;
            Vector3 origin = e.position;
            Vector3 point = target.position + Vector3.up * (crouching ? crouchChestHeight : chestHeight);
            LastTargetPoint = point;

            Vector3 to = point - origin;
            float dist = to.magnitude;
            if (dist < 0.001f) return 1f;
            Vector3 dir = to / dist;

            bool near = dist <= nearSenseRadius;
            float half = fovAngle * 0.5f;
            float angle = Vector3.Angle(e.forward, dir);
            float effRange = range * (crouching ? crouchRangeMultiplier : 1f);

            float light = 1f - darkness * darknessRangePenalty;
            if (FlashlightOn && angle <= flashlightAngle * 0.5f) light = 1f;
            float litRange = effRange * light;

            if (!near)
            {
                if (angle > half) return 0f;
                if (dist > litRange) return 0f;
            }

            // Line of sight: anything that is not the player blocks.
            if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, _mask, QueryTriggerInteraction.Ignore))
            {
                Transform h = hit.transform;
                bool isPlayer = h == target || h.IsChildOf(target) || hit.collider.CompareTag(GameConstants.TagPlayer);
                if (!isPlayer) return 0f;
            }

            if (near) return 1f;

            float distF = 1f - Mathf.InverseLerp(litRange * 0.4f, litRange, dist);
            float angF = 1f - Mathf.InverseLerp(half * 0.5f, half, angle);
            return Mathf.Clamp01((0.25f + 0.75f * distF) * (0.3f + 0.7f * angF));
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Transform e = Eye;
            Vector3 o = e.position;
            Vector3 f = e.forward;
            float half = fovAngle * 0.5f;

            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.8f);
            Vector3 left = Quaternion.AngleAxis(-half, e.up) * f;
            Vector3 right = Quaternion.AngleAxis(half, e.up) * f;
            Vector3 up = Quaternion.AngleAxis(-half, e.right) * f;
            Vector3 down = Quaternion.AngleAxis(half, e.right) * f;
            Gizmos.DrawLine(o, o + left * range);
            Gizmos.DrawLine(o, o + right * range);
            Gizmos.DrawLine(o, o + up * range);
            Gizmos.DrawLine(o, o + down * range);
            Gizmos.DrawLine(o, o + f * range);

            const int segments = 16;
            Vector3 prev = o + left * range;
            for (int i = 1; i <= segments; i++)
            {
                float a = Mathf.Lerp(-half, half, i / (float)segments);
                Vector3 p = o + (Quaternion.AngleAxis(a, e.up) * f) * range;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }

            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(o, nearSenseRadius);

            if (Application.isPlaying && LastVisibility > 0f)
            {
                Gizmos.color = Color.Lerp(Color.yellow, Color.red, LastVisibility);
                Gizmos.DrawLine(o, LastTargetPoint);
            }
        }
#endif
    }
}
