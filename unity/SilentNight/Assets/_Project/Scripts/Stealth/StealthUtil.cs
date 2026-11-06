using UnityEngine;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>Small shared helpers for the stealth module (no allocations).</summary>
    internal static class StealthUtil
    {
        /// <summary>NPCs only think during a delivery (or when testing a scene without GameFlow).</summary>
        public static bool IsGameplayActive
        {
            get
            {
                var flow = GameServices.Flow;
                return flow == null || flow.State == GameState.Delivery;
            }
        }

        public static bool IsSpookyHouse
        {
            get
            {
                var order = GameServices.ActiveOrder;
                return order != null && order.Mood == HouseMood.Spooky;
            }
        }

        /// <summary>Resolves the local player safely (handles a destroyed MonoBehaviour behind the interface).</summary>
        public static bool TryGetPlayer(out Transform playerTransform, out bool crouching)
        {
            playerTransform = null;
            crouching = false;
            IPlayerStatus p = GameServices.LocalPlayer;
            if (p == null) return false;
            if (p is UnityEngine.Object uo && uo == null) return false; // destroyed component
            playerTransform = p.Transform;
            if (playerTransform == null) return false;
            crouching = p.IsCrouching;
            return true;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool IsPlayerCollider(Collider c)
        {
            if (c == null) return false;
            if (c.CompareTag(GameConstants.TagPlayer)) return true;
            Rigidbody rb = c.attachedRigidbody;
            if (rb != null && rb.CompareTag(GameConstants.TagPlayer)) return true;
            // Fallback: collider belongs to the registered local player hierarchy.
            if (TryGetPlayer(out Transform pt, out _))
                return c.transform == pt || c.transform.IsChildOf(pt);
            return false;
        }

        /// <summary>Removes a named layer from a mask (no-op if the layer does not exist).</summary>
        public static int WithoutLayer(int mask, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) return mask;
            return mask & ~(1 << layer);
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null || layer < 0) return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        public static float SmoothStep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
