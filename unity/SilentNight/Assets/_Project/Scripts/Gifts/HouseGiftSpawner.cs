using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// House arrival point for the gifts. On Start spawns GameServices.LoadedGifts
    /// (fallback: ActiveOrder.Gifts, fallback: Small, Noisy, Huge for testing the House scene directly),
    /// spread in a row along transform.right. Stores the total for TreeDropZone / ChimneyExit.
    /// </summary>
    public class HouseGiftSpawner : MonoBehaviour
    {
        [SerializeField] private float spacing = 0.35f;
        [SerializeField] private float spawnHeight = 0.3f;
        [Tooltip("Raycast down to put gifts on the floor.")]
        [SerializeField] private bool snapToFloor = true;
        [SerializeField] private GiftKind[] testGifts = { GiftKind.Small, GiftKind.Noisy, GiftKind.Huge };

        /// <summary>Total gifts spawned by the most recent spawner (static fallback for TreeDropZone).</summary>
        public static int LastSpawnCount { get; private set; }

        public int TotalCount { get; private set; }
        public bool HasSpawned { get; private set; }
        public readonly List<Gift> SpawnedGifts = new List<Gift>(8);

        private readonly List<GiftKind> _kinds = new List<GiftKind>(8);

        // Forget the previous house's count before anything reads it this scene.
        private void Awake() => LastSpawnCount = 0;

        private void Start() => Spawn();

        public void Spawn()
        {
            if (HasSpawned) return;
            HasSpawned = true;

            _kinds.Clear();
            if (GameServices.LoadedGifts.Count > 0) _kinds.AddRange(GameServices.LoadedGifts);
            else if (GameServices.ActiveOrder != null && GameServices.ActiveOrder.Gifts.Count > 0) _kinds.AddRange(GameServices.ActiveOrder.Gifts);
            else if (testGifts != null) _kinds.AddRange(testGifts);

            // Total width of the row to centre it on the spawner.
            float width = 0f;
            for (int i = 0; i < _kinds.Count; i++) width += Footprint(_kinds[i]) + (i > 0 ? spacing : 0f);

            Vector3 right = transform.right;
            float x = -width * 0.5f;
            for (int i = 0; i < _kinds.Count; i++)
            {
                var kind = _kinds[i];
                float w = Footprint(kind);
                Vector3 pos = transform.position + right * (x + w * 0.5f);
                x += w + spacing;

                float halfH = kind == GiftKind.Huge ? 0.52f : 0.23f;
                if (snapToFloor && Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    pos.y = hit.point.y + halfH + 0.02f;
                else
                    pos.y += spawnHeight + halfH;

                var gift = GiftFactory.Create(kind, pos, transform.rotation);
                SpawnedGifts.Add(gift);
            }

            TotalCount = _kinds.Count;
            LastSpawnCount = TotalCount;

            if (TotalCount > 0)
                GameEvents.RaiseObjective($"Тихо положите подарки под ёлку (0/{TotalCount})");
        }

        // Width along transform.right: bike is 1.64 m long (its local X is aligned with spawner's right).
        private static float Footprint(GiftKind k) => k == GiftKind.Huge ? 1.7f : 0.5f;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.3f, new Vector3(3f, 0.6f, 0.8f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LastSpawnCount = 0;
    }
}
