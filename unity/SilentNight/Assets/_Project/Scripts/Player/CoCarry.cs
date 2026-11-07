using System.Collections.Generic;
using SilentNight.Core;
using SilentNight.Gifts;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Two elves carrying one Huge gift (the wrapped bike) together - the signature co-op gag.
    /// Created at runtime by ElfCoCarryHandle (CoCarry.Begin) on its own GameObject and destroyed when it ends.
    ///
    /// While active it owns the bike: the bike is unparented (still IsHeld / kinematic / colliders off, as set by
    /// Gift.OnPickedUp) and every LateUpdate placed at the midpoint between the two elves' hold points, long axis
    /// (local X) along the line between them. Both elves are turned to face the bike. A rope-like constraint keeps
    /// them together: past SlackDistance the elf that lags behind the pull is dragged along with
    /// CharacterController.Move; past BreakDistance the grip breaks and the front elf keeps the bike solo.
    ///
    /// Holder = front elf (ElfCarry.Held is the bike). Helper = rear elf (its hands report empty).
    /// The holder's solo wall clearance (ElfController huge-box test) is off while co-carrying.
    /// </summary>
    [DefaultExecutionOrder(80)] // LateUpdate after the elves have moved this frame
    [DisallowMultipleComponent]
    public class CoCarry : MonoBehaviour
    {
        public const string SlipToast = "Велосипед выскользнул!";
        private const float DefaultBikeLength = 1.64f;

        [Tooltip("Rope slack: elves may be up to bikeLength * slackFactor + slackExtra apart before one is dragged.")]
        [SerializeField] private float slackFactor = 0.9f;
        [SerializeField] private float slackExtra = 0.35f;
        [Tooltip("Above this distance between the elves (m) the helper loses the grip.")]
        [SerializeField] private float breakDistance = 2.6f;
        [Tooltip("Closer than bikeLength * this, the elves are gently pushed apart (the bike is between them).")]
        [SerializeField] private float minSpacingFactor = 0.85f;
        [SerializeField] private float spacingPushRate = 6f;
        [SerializeField] private float slipNoise = 30f;

        private static readonly List<CoCarry> s_All = new List<CoCarry>(2);

        /// <summary>All active co-carries (one per bike).</summary>
        public static IReadOnlyList<CoCarry> All => s_All;
        /// <summary>The first active co-carry, or null. Convenience for tests / HUD.</summary>
        public static CoCarry Active => s_All.Count > 0 ? s_All[0] : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_All.Clear();

        /// <summary>The active co-carry this elf is part of, or null.</summary>
        public static CoCarry FindFor(ElfCarry elf) => elf != null ? elf.ActiveCoCarry : null;

        public ElfCarry Holder { get; private set; }
        public ElfCarry Helper { get; private set; }
        public ICarryable Item { get; private set; }
        public bool IsActive { get; private set; }
        /// <summary>Length of the bike along its long axis (local X), metres.</summary>
        public float BikeLength { get; private set; } = DefaultBikeLength;
        public float SlackDistance => BikeLength * slackFactor + slackExtra;
        public float BreakDistance => breakDistance;
        /// <summary>World position of the bike end the helper holds (the release prompt sits here).</summary>
        public Vector3 HelperGrip { get; private set; }
        /// <summary>World position of the bike end the holder holds.</summary>
        public Vector3 HolderGrip { get; private set; }
        /// <summary>Planar distance between the two elves this frame (m).</summary>
        public float Distance { get; private set; }

        private Transform _item;
        private CharacterController _holderCc, _helperCc;
        private ElfController _holderCtrl, _helperCtrl;
        private ElfRagdoll _holderBody, _helperBody;
        private bool _destroying;

        // ---------------------------------------------------------------- start / end

        /// <summary>True if <paramref name="helper"/> may grab the other end of <paramref name="holder"/>'s bike now.</summary>
        public static bool CanBegin(ElfCarry holder, ElfCarry helper)
        {
            if (holder == null || helper == null || holder == helper) return false;
            if (!holder.isActiveAndEnabled || !helper.isActiveAndEnabled) return false;
            if (!holder.IsCarryingHuge || holder.IsCoCarrying || helper.IsCoCarrying || helper.IsHoldingSomething) return false;
            ElfRagdoll a = holder.GetComponent<ElfRagdoll>();
            ElfRagdoll b = helper.GetComponent<ElfRagdoll>();
            if ((a != null && a.IsLimp) || (b != null && b.IsLimp)) return false;
            return true;
        }

        /// <summary>Start carrying <paramref name="holder"/>'s bike together with <paramref name="helper"/>. Null if not allowed.</summary>
        public static CoCarry Begin(ElfCarry holder, ElfCarry helper)
        {
            if (!CanBegin(holder, helper)) return null;
            var go = new GameObject("CoCarry");
            var c = go.AddComponent<CoCarry>();
            c.Init(holder, helper);
            return c;
        }

        private void Init(ElfCarry holder, ElfCarry helper)
        {
            Holder = holder;
            Helper = helper;
            Item = holder.Held;
            _item = Item.GameObject.transform;

            Gift gift = Item as Gift;
            if (gift == null) gift = Item.GameObject.GetComponent<Gift>();
            float len = gift != null ? gift.HalfExtents.x * 2f : 0f;
            BikeLength = len > 0.2f ? len : DefaultBikeLength;

            _holderCc = holder.GetComponent<CharacterController>();
            _helperCc = helper.GetComponent<CharacterController>();
            _holderCtrl = holder.GetComponent<ElfController>();
            _helperCtrl = helper.GetComponent<ElfController>();
            _holderBody = holder.GetComponent<ElfRagdoll>();
            _helperBody = helper.GetComponent<ElfRagdoll>();

            IsActive = true;
            s_All.Add(this);
            holder.SetCoCarry(this);
            helper.SetCoCarry(this);

            _item.SetParent(null, true); // still held (kinematic, colliders off); we place it ourselves
            FaceEachOther();
            Place();
        }

        /// <summary>The helper lets go; the front elf keeps the bike as a normal solo carry.</summary>
        public void Release() => End(true);

        /// <summary>
        /// End the co-carry. keepWithHolder: re-attach the bike to the holder's hold point (solo carry).
        /// Pass false when the holder is about to drop / hand over the bike itself.
        /// </summary>
        public void End(bool keepWithHolder)
        {
            if (!IsActive) return;
            ElfCarry holder = Holder;
            Shutdown();
            if (keepWithHolder && holder != null && PlayerGate.IsAlive(Item) && ReferenceEquals(holder.Held, Item))
                holder.ReattachHeld();
            if (!_destroying) Destroy(gameObject);
        }

        /// <summary>End without touching the hierarchy (called from OnDisable / OnDestroy). The holder re-attaches
        /// the bike on its next Update if it still holds it.</summary>
        public void Abort()
        {
            if (!IsActive) return;
            Shutdown();
            if (!_destroying) Destroy(gameObject);
        }

        private void Shutdown()
        {
            IsActive = false;
            s_All.Remove(this);
            if (Helper != null) Helper.ClearCoCarry(this);
            if (Holder != null) Holder.ClearCoCarry(this);
        }

        private void OnDestroy()
        {
            _destroying = true;
            Abort();
        }

        // ---------------------------------------------------------------- per frame

        private void LateUpdate()
        {
            if (!IsActive || !StillValid()) return;

            float dt = Time.deltaTime;
            if (dt > 0f) ApplyRope(dt);

            Vector3 a = Holder.transform.position, b = Helper.transform.position;
            if ((a - b).sqrMagnitude > breakDistance * breakDistance)
            {
                Slip();
                return;
            }

            FaceEachOther();
            Place();
        }

        private bool StillValid()
        {
            if (Holder == null || !PlayerGate.IsAlive(Item) || !ReferenceEquals(Holder.Held, Item))
            {
                End(false); // the bike is no longer the holder's (delivered, destroyed, taken)
                return false;
            }
            if (!Holder.isActiveAndEnabled) { Abort(); return false; }
            if (Helper == null || !Helper.isActiveAndEnabled) { End(true); return false; }
            if (_helperBody != null && _helperBody.IsLimp) { End(true); return false; } // helper fell: front keeps it
            if (_holderBody != null && _holderBody.IsLimp) { End(true); return false; } // (ElfRagdoll normally drops it first)
            return true;
        }

        private void Slip()
        {
            GameEvents.RaiseToast(SlipToast);
            if (slipNoise > 0f && Holder != null)
                GameEvents.RaiseNoise(new NoiseEvent(_item.position, slipNoise, NoiseSource.GiftImpact, Holder.gameObject));
            End(true);
        }

        /// <summary>
        /// Rope: past the slack, whoever pulls away harder drags the other one along (both pulling = tug of war,
        /// split by effort; nobody pulling = meet halfway). Too close: nudge apart so the bike fits between them.
        /// </summary>
        private void ApplyRope(float dt)
        {
            Vector3 d = Holder.transform.position - Helper.transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            Distance = dist;
            if (dist < 0.0001f) return;
            Vector3 n = d / dist; // helper -> holder

            float slack = SlackDistance;
            if (dist > slack)
            {
                float excess = dist - slack;
                float holderPull = Mathf.Max(0f, Vector3.Dot(PlanarVelocity(_holderCtrl), n));
                float helperPull = Mathf.Max(0f, Vector3.Dot(PlanarVelocity(_helperCtrl), -n));
                float sum = holderPull + helperPull;
                float holderShare = sum > 0.01f ? helperPull / sum : 0.5f; // fraction of the excess the holder is dragged
                MoveElf(_holderCc, -n * (excess * holderShare));
                MoveElf(_helperCc, n * (excess * (1f - holderShare)));
            }
            else
            {
                float min = BikeLength * minSpacingFactor;
                if (dist < min)
                {
                    float push = (min - dist) * Mathf.Clamp01(spacingPushRate * dt) * 0.5f;
                    MoveElf(_holderCc, n * push);
                    MoveElf(_helperCc, -n * push);
                }
            }
        }

        private static Vector3 PlanarVelocity(ElfController c)
        {
            if (c == null) return Vector3.zero;
            Vector3 v = c.Velocity;
            v.y = 0f;
            return v;
        }

        private static void MoveElf(CharacterController cc, Vector3 delta)
        {
            if (cc == null || !cc.enabled || delta.sqrMagnitude < 1e-10f) return;
            cc.Move(delta);
        }

        private void FaceEachOther()
        {
            Vector3 d = Holder.transform.position - Helper.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            d.Normalize();
            Holder.transform.rotation = Quaternion.LookRotation(-d, Vector3.up);
            Helper.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        /// <summary>Bike at the midpoint of both hold points, long axis (local X) pointing from the helper to the holder.</summary>
        private void Place()
        {
            Vector3 holdLocal = Holder.HugeHoldLocalPosition;
            Vector3 holderHold = Holder.transform.TransformPoint(holdLocal);
            Vector3 helperHold = Helper.transform.TransformPoint(holdLocal);
            Vector3 mid = (holderHold + helperHold) * 0.5f;

            Vector3 up0 = Vector3.up * holdLocal.y;
            Vector3 axis = (Holder.transform.position + up0) - (Helper.transform.position + up0); // includes height difference
            if (axis.sqrMagnitude < 1e-6f) axis = Holder.transform.right;
            axis.Normalize();
            Vector3 fwd = Vector3.Cross(axis, Vector3.up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Holder.transform.forward;
            fwd.Normalize();
            Vector3 up = Vector3.Cross(fwd, axis); // right = Cross(up, fwd) = axis
            Quaternion rot = Quaternion.LookRotation(fwd, up);

            _item.SetPositionAndRotation(mid, rot);
            float half = BikeLength * 0.5f;
            HolderGrip = mid + axis * half;
            HelperGrip = mid - axis * half;
        }
    }
}
