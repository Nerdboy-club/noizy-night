using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Trigger volume around the Christmas tree. [E] puts everything the elf carries under the tree;
    /// gifts dropped or thrown into the trigger (not held) are delivered too.
    /// Delivered gifts are frozen in a ring around <see cref="placementCenter"/>.
    /// Raises GameEvents.GiftDelivered(gift, delivered, total) for each gift (GameFlow completes the run).
    /// </summary>
    public class TreeDropZone : InteractableBase
    {
        [Header("Tree drop zone")]
        [Tooltip("Optional. If null, HouseGiftSpawner.LastSpawnCount (then ActiveOrder) is used for the total.")]
        [SerializeField] private HouseGiftSpawner spawner;
        [Tooltip("Centre of the ring of presents (tree trunk). Defaults to this transform.")]
        [SerializeField] private Transform placementCenter;
        [SerializeField] private float ringRadius = 1.1f;
        [SerializeField] private float hugeExtraRadius = 0.45f;
        [SerializeField] private float ringAngleOffset = 200f;
        [Tooltip("Size of the trigger created when this object has no collider.")]
        [SerializeField] private Vector3 triggerSize = new Vector3(3.2f, 2.5f, 3.2f);

        public int Delivered { get; private set; }

        public int Total
        {
            get
            {
                if (spawner != null && spawner.HasSpawned) return spawner.TotalCount;
                if (HouseGiftSpawner.LastSpawnCount > 0) return HouseGiftSpawner.LastSpawnCount;
                var o = GameServices.ActiveOrder;
                return o != null ? o.Gifts.Count : 0;
            }
        }

        protected override void Awake()
        {
            if (string.IsNullOrEmpty(prompt) || prompt == "Взаимодействовать") prompt = "Положить подарки под ёлку";
            defaultColliderSize = triggerSize;
            defaultColliderCenter = new Vector3(0f, triggerSize.y * 0.5f, 0f);
            base.Awake();

            // Our own colliders must be triggers so gifts can fall in.
            var cols = GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++) cols[i].isTrigger = true;
        }

        public override bool CanInteract(IInteractor interactor) => isActiveAndEnabled && HasAnything(CarrierOf(interactor));

        public override void Interact(IInteractor interactor)
        {
            var carrier = CarrierOf(interactor);
            if (!HasAnything(carrier)) return;
            var items = carrier.TakeAll();
            if (items == null) return;
            for (int i = 0; i < items.Length; i++) Deliver(items[i]);
        }

        private void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            var gift = rb.GetComponent<Gift>();
            if (gift == null || gift.IsHeld || gift.IsInSack || gift.IsDelivered) return;
            Deliver(gift);
        }

        private void Deliver(ICarryable item)
        {
            if (item == null) return;
            var gift = item as Gift;
            if (gift != null && gift.IsDelivered) return;

            int total = Mathf.Max(Total, Delivered + 1);
            int slot = Delivered;
            Delivered++;

            if (gift != null)
            {
                PlaceUnderTree(gift, slot, total);
            }
            else if (item.GameObject != null)
            {
                // Unknown carryable: just tuck it away.
                item.GameObject.SetActive(false);
            }

            GameEvents.RaiseGiftDelivered(item, Delivered, total);
            if (Delivered < total)
                GameEvents.RaiseObjective($"Подарки под ёлкой: {Delivered}/{total}");
        }

        private void PlaceUnderTree(Gift gift, int slot, int total)
        {
            Transform c = placementCenter != null ? placementCenter : transform;
            float step = 360f / Mathf.Max(1, total);
            float angle = (ringAngleOffset + slot * step) * Mathf.Deg2Rad;
            float r = ringRadius + (gift.Kind == GiftKind.Huge ? hugeExtraRadius : 0f);
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 pos = c.position + dir * r;

            Vector3 half = gift.HalfExtents;
            float floorY = c.position.y;
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 4f, ~GiftLayerMask(), QueryTriggerInteraction.Ignore))
                floorY = hit.point.y;
            pos.y = floorY + half.y + 0.01f;

            // Face the tree; the bike lies tangentially (its long X axis along the ring).
            Quaternion rot = Quaternion.LookRotation(-dir, Vector3.up);
            gift.MarkDelivered(pos, rot);
        }

        private static int GiftLayerMask()
        {
            int layer = LayerMask.NameToLayer(GameConstants.LayerGift);
            int player = LayerMask.NameToLayer(GameConstants.LayerPlayer);
            int mask = 0;
            if (layer >= 0) mask |= 1 << layer;
            if (player >= 0) mask |= 1 << player;
            return mask;
        }

        private void OnDrawGizmos()
        {
            Transform c = placementCenter != null ? placementCenter : transform;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            const int seg = 24;
            Vector3 prev = c.position + new Vector3(ringRadius, 0.02f, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 p = c.position + new Vector3(Mathf.Cos(a) * ringRadius, 0.02f, Mathf.Sin(a) * ringRadius);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
