using System;
using System.Collections.Generic;
using SilentNight.Core;
using SilentNight.Gifts;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Hands + sack of the elf (ICarrier). One item in hands; Small/Noisy gifts can be stored in the sack
    /// (capacity from GameServices.Profile.SackCapacity). Huge gifts only go in hands and slow the elf down.
    /// </summary>
    [DisallowMultipleComponent]
    public class ElfCarry : MonoBehaviour, ICarrier
    {
        [Header("Anchors (auto-filled from ElfVisual / created if missing)")]
        [SerializeField] private Transform holdPoint;
        [SerializeField] private Transform sackAnchor;

        [Header("Hold positions (local to the elf root)")]
        [SerializeField] private Vector3 smallHoldLocalPosition = new Vector3(0f, 0.3f, 0.26f);
        [SerializeField] private Vector3 hugeHoldLocalPosition = new Vector3(0f, 0.4f, 0.5f);

        [Header("Drop")]
        [SerializeField] private float dropForwardSpeed = 1.6f;
        [SerializeField] private float dropUpSpeed = 1.2f;
        [SerializeField] private float inheritVelocity = 0.5f;

        [Header("Speed penalties (from the prototype)")]
        [SerializeField] private float sackPenaltyPerItem = 0.04f;
        [SerializeField] private float sackPenaltyMin = 0.78f;
        [SerializeField] private float hugeBaseMultiplier = 0.42f;
        [SerializeField] private float hugeGripBonusPerLevel = 0.12f;
        [SerializeField] private float hugeMaxMultiplier = 0.85f;
        [SerializeField] private float noisyBaseMultiplier = 0.92f;
        [SerializeField] private float noisyGripBonusPerLevel = 0.06f;

        [Tooltip("Used when there is no PlayerProfile (scene started without Boot).")]
        [SerializeField] private int fallbackSackCapacity = 1;

        private readonly List<ICarryable> _sack = new List<ICarryable>(8);
        private ElfController _controller;

        /// <summary>Raised whenever hands/sack contents change.</summary>
        public event Action Changed;

        public Transform HoldPoint => holdPoint;
        public Transform SackAnchor => sackAnchor;

        // ---------------- ICarrier ----------------
        public ICarryable Held { get; private set; }
        public bool IsHoldingSomething => PlayerGate.IsAlive(Held);
        public int SackCount => _sack.Count;

        public int SackCapacity
        {
            get
            {
                PlayerProfile p = GameServices.Profile;
                return p != null ? Mathf.Max(1, p.SackCapacity) : Mathf.Max(1, fallbackSackCapacity);
            }
        }

        public bool IsSackFull => _sack.Count >= SackCapacity;
        public bool IsCarryingHuge => IsHoldingSomething && Held.Kind == GiftKind.Huge;

        private int GripLevel => GameServices.Profile != null ? GameServices.Profile.GripLevel : 0;

        /// <summary>Movement speed multiplier (held gift weight × sack penalty). Read by ElfController.</summary>
        public float SpeedMultiplier
        {
            get
            {
                float sack = Mathf.Max(sackPenaltyMin, 1f - _sack.Count * sackPenaltyPerItem);
                float hands = 1f;
                if (IsHoldingSomething)
                {
                    int grip = GripLevel;
                    switch (Held.Kind)
                    {
                        case GiftKind.Huge:
                            hands = Mathf.Min(hugeMaxMultiplier, hugeBaseMultiplier + grip * hugeGripBonusPerLevel);
                            break;
                        case GiftKind.Noisy:
                            hands = Mathf.Min(1f, noisyBaseMultiplier + grip * noisyGripBonusPerLevel);
                            break;
                        default:
                            hands = 1f;
                            break;
                    }
                }
                return hands * sack;
            }
        }

        // Cached lookup for the held Huge gift's box (refreshed when Held changes).
        private ICarryable _boxSource;
        private Gift _boxGift;
        private BoxCollider _boxCollider;

        /// <summary>
        /// World-space box of the held Huge gift (the bike) as it sits at the hold point right now.
        /// The gift's colliders are disabled while held, so ElfController uses this to test clearance itself.
        /// Returns false when not carrying a Huge gift. No allocations.
        /// </summary>
        public bool TryGetHugeBox(out Vector3 worldCenter, out Quaternion worldRotation, out Vector3 halfExtents,
            out Transform giftRoot)
        {
            worldCenter = Vector3.zero;
            worldRotation = Quaternion.identity;
            halfExtents = Vector3.zero;
            giftRoot = null;
            if (!IsCarryingHuge) return false;

            if (!ReferenceEquals(_boxSource, Held))
            {
                _boxSource = Held;
                _boxGift = Held as Gift;
                if (_boxGift == null && Held.GameObject != null) _boxGift = Held.GameObject.GetComponent<Gift>();
                _boxCollider = _boxGift != null ? _boxGift.GetComponent<BoxCollider>() : null;
            }
            if (_boxGift == null || !_boxGift.IsHeld) return false;

            Transform t = _boxGift.transform;
            Vector3 localCenter = _boxCollider != null ? _boxCollider.center : Vector3.zero;
            worldCenter = t.TransformPoint(localCenter);
            worldRotation = t.rotation;
            halfExtents = _boxGift.HalfExtents;
            giftRoot = t;
            return halfExtents.x > 0f && halfExtents.y > 0f && halfExtents.z > 0f;
        }

        public bool SackContains(ICarryable item)
        {
            for (int i = 0; i < _sack.Count; i++)
                if (ReferenceEquals(_sack[i], item)) return true;
            return false;
        }

        private void Awake()
        {
            _controller = GetComponent<ElfController>();
            EnsureAnchors();
        }

        private void EnsureAnchors()
        {
            ElfVisual visual = GetComponent<ElfVisual>();
            if (visual != null) visual.EnsureAnchors();

            if (holdPoint == null)
                holdPoint = visual != null ? visual.HoldPoint : CreateChild("HoldPoint", smallHoldLocalPosition);
            if (sackAnchor == null)
                sackAnchor = visual != null ? visual.SackAnchor : CreateChild("SackAnchor", new Vector3(0f, 0.5f, -0.3f));
        }

        private Transform CreateChild(string childName, Vector3 localPos)
        {
            Transform existing = transform.Find(childName);
            if (existing != null) return existing;
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private void Update()
        {
            // Forget items that were destroyed by someone else (e.g. delivered and cleaned up).
            if (Held != null && !PlayerGate.IsAlive(Held))
            {
                Held = null;
                Changed?.Invoke();
            }
            for (int i = _sack.Count - 1; i >= 0; i--)
            {
                if (!PlayerGate.IsAlive(_sack[i]))
                {
                    _sack.RemoveAt(i);
                    Changed?.Invoke();
                }
            }
        }

        private void PlaceHoldPointFor(ICarryable item)
        {
            if (holdPoint == null) EnsureAnchors();
            if (holdPoint == null) return;
            Vector3 local = item.Kind == GiftKind.Huge ? hugeHoldLocalPosition : smallHoldLocalPosition;
            // Hold point may be nested under a visual root; convert from elf-root space.
            Vector3 world = transform.TransformPoint(local);
            holdPoint.position = world;
            holdPoint.rotation = transform.rotation;
        }

        public bool TryPickUp(ICarryable item)
        {
            if (!PlayerGate.IsAlive(item)) return false;
            if (IsHoldingSomething) return false;
            if (SackContains(item)) return false;

            PlaceHoldPointFor(item);
            Held = item;
            item.OnPickedUp(holdPoint);
            Changed?.Invoke();
            return true;
        }

        public void Drop()
        {
            if (!IsHoldingSomething) { Held = null; return; }
            ICarryable item = Held;
            Held = null;

            Vector3 v = transform.forward * dropForwardSpeed + Vector3.up * dropUpSpeed;
            if (_controller == null) _controller = GetComponent<ElfController>();
            if (_controller != null) v += _controller.Velocity * inheritVelocity;
            item.OnDropped(v);
            Changed?.Invoke();
        }

        public bool TryStoreHeldInSack()
        {
            if (!IsHoldingSomething) return false;
            if (!Held.FitsInSack) return false;
            if (IsSackFull) return false;

            ICarryable item = Held;
            Held = null;
            item.OnStoredInSack();
            _sack.Add(item);
            Changed?.Invoke();
            return true;
        }

        public bool TryTakeFromSack()
        {
            if (IsHoldingSomething) return false;
            if (_sack.Count == 0) return false;

            int last = _sack.Count - 1;
            ICarryable item = _sack[last];
            _sack.RemoveAt(last);
            if (!PlayerGate.IsAlive(item)) { Changed?.Invoke(); return false; }

            PlaceHoldPointFor(item);
            Held = item;
            item.OnPickedUp(holdPoint);
            Changed?.Invoke();
            return true;
        }

        public ICarryable[] TakeAll()
        {
            int count = (IsHoldingSomething ? 1 : 0);
            for (int i = 0; i < _sack.Count; i++) if (PlayerGate.IsAlive(_sack[i])) count++;

            var result = new ICarryable[count];
            int n = 0;
            if (IsHoldingSomething) result[n++] = Held;
            for (int i = 0; i < _sack.Count; i++)
                if (PlayerGate.IsAlive(_sack[i])) result[n++] = _sack[i];

            Held = null;
            _sack.Clear();
            Changed?.Invoke();
            return result;
        }
    }
}
