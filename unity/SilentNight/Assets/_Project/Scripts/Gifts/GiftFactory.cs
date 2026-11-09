using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>Creates gift objects at runtime (conveyor in the hub, spawner in the house).</summary>
    public static class GiftFactory
    {
        private static readonly GameObject[] ModelPrefabs = new GameObject[System.Enum.GetValues(typeof(GiftKind)).Length];

        /// <summary>Optional: register a real model for a kind (null = placeholder). Call once, e.g. from a bootstrap.</summary>
        public static void RegisterModel(GiftKind kind, GameObject modelPrefab)
        {
            int i = (int)kind;
            if (i >= 0 && i < ModelPrefabs.Length) ModelPrefabs[i] = modelPrefab;
        }

        public static Gift Create(GiftKind kind, Vector3 pos, Quaternion rot, int netId = -1)
        {
            var go = new GameObject($"Gift_{kind}");
            // Build inactive so Awake runs only after the kind/model are configured.
            go.SetActive(false);
            go.transform.SetPositionAndRotation(pos, rot);

            var gift = go.AddComponent<Gift>(); // RequireComponent adds Rigidbody + GiftVisual
            var visual = go.GetComponent<GiftVisual>();
            if (visual == null) visual = go.AddComponent<GiftVisual>();
            int i = (int)kind;
            if (i >= 0 && i < ModelPrefabs.Length && ModelPrefabs[i] != null) visual.SetModelPrefab(ModelPrefabs[i]);

            gift.Configure(kind);
            if (netId >= 0) Gift.RegisterNetId(gift, netId);
            go.SetActive(true);
            return gift;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int i = 0; i < ModelPrefabs.Length; i++) ModelPrefabs[i] = null;
        }
    }
}
