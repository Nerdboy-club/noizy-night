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

        /// <summary>True if the player is non-null, not a destroyed MonoBehaviour and has a live Transform.</summary>
        public static bool IsValidPlayer(IPlayerStatus p, out Transform playerTransform)
        {
            playerTransform = null;
            if (p == null) return false;
            if (p is UnityEngine.Object uo && uo == null) return false; // destroyed component
            playerTransform = p.Transform;
            return playerTransform != null;
        }

        /// <summary>Number of registered players (some entries may be invalid; use TryGetPlayerAt).</summary>
        public static int PlayerCount => GameServices.Players.Count;

        /// <summary>Resolves registered player #index safely. Use with PlayerCount to iterate all players without allocating.</summary>
        public static bool TryGetPlayerAt(int index, out IPlayerStatus player, out Transform playerTransform, out bool crouching)
        {
            player = null;
            playerTransform = null;
            crouching = false;
            var players = GameServices.Players;
            if (index < 0 || index >= players.Count) return false;
            IPlayerStatus p = players[index];
            if (!IsValidPlayer(p, out playerTransform)) return false;
            player = p;
            crouching = p.IsCrouching;
            return true;
        }

        /// <summary>
        /// Resolves "the player" without a reference point: the local player if valid, otherwise the first valid
        /// registered player. With one player this is exactly GameServices.LocalPlayer.
        /// </summary>
        public static bool TryGetPlayer(out Transform playerTransform, out bool crouching)
        {
            crouching = false;
            IPlayerStatus local = GameServices.LocalPlayer;
            if (IsValidPlayer(local, out playerTransform))
            {
                crouching = local.IsCrouching;
                return true;
            }
            var players = GameServices.Players;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayerStatus p = players[i];
                if (p == local) continue;
                if (IsValidPlayer(p, out playerTransform))
                {
                    crouching = p.IsCrouching;
                    return true;
                }
            }
            playerTransform = null;
            return false;
        }

        /// <summary>Nearest valid registered player to <paramref name="from"/> (3D distance). No allocations.</summary>
        public static bool TryGetNearestPlayer(Vector3 from, out IPlayerStatus player, out Transform playerTransform, out bool crouching)
        {
            player = null;
            playerTransform = null;
            crouching = false;
            float best = float.PositiveInfinity;
            var players = GameServices.Players;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayerStatus p = players[i];
                if (!IsValidPlayer(p, out Transform t)) continue;
                float d = (t.position - from).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    player = p;
                    playerTransform = t;
                }
            }
            if (player == null)
            {
                // Not in the list (e.g. registered only via a legacy path): fall back to the old behaviour.
                if (!TryGetPlayer(out playerTransform, out crouching)) return false;
                player = GameServices.LocalPlayer;
                return true;
            }
            crouching = player.IsCrouching;
            return true;
        }

        /// <summary>Nearest player to <paramref name="from"/>; same out-parameters as TryGetPlayer.</summary>
        public static bool TryGetPlayer(Vector3 from, out Transform playerTransform, out bool crouching)
            => TryGetNearestPlayer(from, out _, out playerTransform, out crouching);

        /// <summary>The registered player whose hierarchy contains the collider, or false.</summary>
        public static bool TryGetPlayerForCollider(Collider c, out IPlayerStatus player)
        {
            player = null;
            if (c == null) return false;
            Transform ct = c.transform;
            var players = GameServices.Players;
            for (int i = 0; i < players.Count; i++)
            {
                IPlayerStatus p = players[i];
                if (!IsValidPlayer(p, out Transform pt)) continue;
                if (ct == pt || ct.IsChildOf(pt))
                {
                    player = p;
                    return true;
                }
            }
            // Legacy fallback: LocalPlayer not present in the list.
            IPlayerStatus local = GameServices.LocalPlayer;
            if (IsValidPlayer(local, out Transform lt) && (ct == lt || ct.IsChildOf(lt)))
            {
                player = local;
                return true;
            }
            return false;
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
            // Fallback: collider belongs to any registered player's hierarchy.
            return TryGetPlayerForCollider(c, out _);
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
