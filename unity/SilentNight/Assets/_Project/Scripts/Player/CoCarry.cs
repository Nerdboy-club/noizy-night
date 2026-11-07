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
    /// The holder's solo wall clearance (ElfController huge-box test) is off while co-carrying; instead this class
    /// tests the bike box for the new pose each frame and, if it would enter a wall / door frame, cancels both
    /// elves' planar walk for the frame (bike keeps its previous pose) and skips any rope drag that would.
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

        [Header("Wall clearance (bike box vs static geometry)")]
        [Tooltip("The bike box is shrunk by this much on every side before the overlap test.")]
        [SerializeField] private float clearanceSkin = 0.02f;
        [Tooltip("Ignore the bottom of the bike box below the higher elf's feet + max(this, stepOffset).")]
        [SerializeField] private float floorClearance = 0.2f;
        [Tooltip("A planar jump bigger than this in one frame is a teleport, not a walk: never cancelled.")]
        [SerializeField] private float teleportDistance = 1.5f;
        [SerializeField] private float scrapeNoise = 35f;
        [SerializeField] private float scrapeCooldown = 0.6f;
        [SerializeField] private float stuckToastCooldown = 4f;

        public const string StuckToast = "Велосипед застрял! Разверните его вдвоём.";

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
        /// <summary>True on frames where the elves' movement (or the rope drag) was cancelled because the bike
        /// would have gone into a wall / door frame.</summary>
        public bool Blocked { get; private set; }

        private Transform _item;
        private Gift _gift;
        private BoxCollider _bikeBox;
        private CharacterController _holderCc, _helperCc;
        private ElfController _holderCtrl, _helperCtrl;
        private ElfRagdoll _holderBody, _helperBody;
        private bool _destroying;

        private readonly Collider[] _overlap = new Collider[16];
        private int _mask = ~0;
        private Vector3 _prevHolderPos, _prevHelperPos;
        private bool _prevWedged; // the accepted pose already overlapped (e.g. started in a doorway): never freeze
        private float _scrapeTimer, _toastTimer;

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
            _gift = gift;
            _bikeBox = gift != null ? gift.GetComponent<BoxCollider>() : null;
            float len = gift != null ? gift.HalfExtents.x * 2f : 0f;
            BikeLength = len > 0.2f ? len : DefaultBikeLength;

            // Same exclusions as ElfController's huge-box test: Ignore Raycast, players, gifts, NPCs.
            _mask = ~0 & ~Physics.IgnoreRaycastLayer;
            ExcludeLayer(ref _mask, GameConstants.LayerPlayer);
            ExcludeLayer(ref _mask, GameConstants.LayerGift);
            ExcludeLayer(ref _mask, GameConstants.LayerNPC);

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
            _prevHolderPos = holder.transform.position;
            _prevHelperPos = helper.transform.position;
            _prevWedged = PoseOverlaps(_prevHolderPos, _prevHelperPos);
        }

        private static void ExcludeLayer(ref int mask, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask &= ~(1 << layer);
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
            Blocked = false;
            if (_scrapeTimer > 0f) _scrapeTimer -= dt;
            if (_toastTimer > 0f) _toastTimer -= dt;

            Vector3 holderNow = Holder.transform.position, helperNow = Helper.transform.position;
            Vector3 holderStep = Planar(holderNow - _prevHolderPos);
            Vector3 helperStep = Planar(helperNow - _prevHelperPos);
            float tp2 = teleportDistance * teleportDistance;
            bool teleported = holderStep.sqrMagnitude > tp2 || helperStep.sqrMagnitude > tp2;
            bool moved = holderStep.sqrMagnitude > 1e-8f || helperStep.sqrMagnitude > 1e-8f;

            bool wedged = _prevWedged;
            bool walkCancelled = false;
            if (teleported)
            {
                wedged = PoseOverlaps(holderNow, helperNow); // new place: re-baseline, never cancel a teleport
            }
            else if (moved)
            {
                bool overlaps = PoseOverlaps(holderNow, helperNow);
                if (overlaps && !_prevWedged)
                {
                    // Cancel this frame's planar walk of both elves; the bike keeps last frame's pose.
                    MoveElf(_holderCc, -holderStep);
                    MoveElf(_helperCc, -helperStep);
                    Blocked = true;
                    walkCancelled = true;
                }
                else wedged = overlaps;
            }

            if (!Blocked && dt > 0f && ComputeRope(dt, out Vector3 holderDrag, out Vector3 helperDrag))
            {
                holderNow = Holder.transform.position;
                helperNow = Helper.transform.position;
                bool dragOverlaps = PoseOverlaps(holderNow + holderDrag, helperNow + helperDrag);
                if (!dragOverlaps || wedged)
                {
                    MoveElf(_holderCc, holderDrag);
                    MoveElf(_helperCc, helperDrag);
                    wedged = dragOverlaps;
                }
                else Blocked = true; // the lagging elf is not dragged; the (clear) walked pose still applies below
            }

            if (Blocked) OnScrape();
            if (walkCancelled)
            {
                FaceEachOther();
                RememberPositions(wedged);
                return; // keep the previous bike pose
            }

            Vector3 a = Holder.transform.position, b = Helper.transform.position;
            if ((a - b).sqrMagnitude > breakDistance * breakDistance)
            {
                Slip();
                return;
            }

            FaceEachOther();
            Place();
            RememberPositions(wedged);
        }

        private void RememberPositions(bool wedged)
        {
            _prevHolderPos = Holder.transform.position;
            _prevHelperPos = Helper.transform.position;
            _prevWedged = wedged;
        }

        private void OnScrape()
        {
            if (_scrapeTimer <= 0f && scrapeNoise > 0f)
            {
                _scrapeTimer = scrapeCooldown;
                GameEvents.RaiseNoise(new NoiseEvent(_item.position, scrapeNoise, NoiseSource.GiftImpact, Holder.gameObject));
            }
            if (_toastTimer <= 0f)
            {
                _toastTimer = stuckToastCooldown;
                GameEvents.RaiseToast(StuckToast);
            }
        }

        private static Vector3 Planar(Vector3 v)
        {
            v.y = 0f;
            return v;
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
        /// Only computes the planar drag of each elf; the caller applies it if the resulting bike pose is clear.
        /// </summary>
        private bool ComputeRope(float dt, out Vector3 holderDrag, out Vector3 helperDrag)
        {
            holderDrag = Vector3.zero;
            helperDrag = Vector3.zero;
            Vector3 d = Holder.transform.position - Helper.transform.position;
            d.y = 0f;
            float dist = d.magnitude;
            Distance = dist;
            if (dist < 0.0001f) return false;
            Vector3 n = d / dist; // helper -> holder

            float slack = SlackDistance;
            if (dist > slack)
            {
                float excess = dist - slack;
                float holderPull = Mathf.Max(0f, Vector3.Dot(PlanarVelocity(_holderCtrl), n));
                float helperPull = Mathf.Max(0f, Vector3.Dot(PlanarVelocity(_helperCtrl), -n));
                float sum = holderPull + helperPull;
                float holderShare = sum > 0.01f ? helperPull / sum : 0.5f; // fraction of the excess the holder is dragged
                holderDrag = -n * (excess * holderShare);
                helperDrag = n * (excess * (1f - holderShare));
            }
            else
            {
                float min = BikeLength * minSpacingFactor;
                if (dist < min)
                {
                    float push = (min - dist) * Mathf.Clamp01(spacingPushRate * dt) * 0.5f;
                    holderDrag = n * push;
                    helperDrag = -n * push;
                }
            }
            return holderDrag.sqrMagnitude > 1e-10f || helperDrag.sqrMagnitude > 1e-10f;
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
            ComputePose(Holder.transform.position, Helper.transform.position, out Vector3 mid, out Quaternion rot, out Vector3 axis);
            _item.SetPositionAndRotation(mid, rot);
            float half = BikeLength * 0.5f;
            HolderGrip = mid + axis * half;
            HelperGrip = mid - axis * half;
        }

        /// <summary>
        /// Bike pose for elves standing at <paramref name="holderPos"/> / <paramref name="helperPos"/> and facing each
        /// other (as FaceEachOther would turn them). Matches Place() for the current positions.
        /// </summary>
        private void ComputePose(Vector3 holderPos, Vector3 helperPos, out Vector3 mid, out Quaternion rot, out Vector3 axis)
        {
            Transform ht = Holder.transform, kt = Helper.transform;
            Quaternion holderRot = ht.rotation, helperRot = kt.rotation;
            Vector3 d = Planar(holderPos - helperPos);
            if (d.sqrMagnitude >= 1e-6f)
            {
                d.Normalize();
                holderRot = Quaternion.LookRotation(-d, Vector3.up);
                helperRot = Quaternion.LookRotation(d, Vector3.up);
            }

            Vector3 holdLocal = Holder.HugeHoldLocalPosition;
            Vector3 holderHold = holderPos + holderRot * Vector3.Scale(holdLocal, ht.lossyScale);
            Vector3 helperHold = helperPos + helperRot * Vector3.Scale(holdLocal, kt.lossyScale);
            mid = (holderHold + helperHold) * 0.5f;

            axis = holderPos - helperPos; // includes height difference
            if (axis.sqrMagnitude < 1e-6f) axis = ht.right;
            axis.Normalize();
            Vector3 fwd = Vector3.Cross(axis, Vector3.up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = ht.forward;
            fwd.Normalize();
            Vector3 up = Vector3.Cross(fwd, axis); // right = Cross(up, fwd) = axis
            rot = Quaternion.LookRotation(fwd, up);
        }

        /// <summary>
        /// Would the bike box overlap solid static geometry with the elves at these positions? Same rules as
        /// ElfController's solo huge-box test: shrunk by a skin, floor part cut off, Ignore Raycast / Player / Gift /
        /// NPC layers, triggers, both elves, the bike itself and loose dynamic bodies never count.
        /// </summary>
        private bool PoseOverlaps(Vector3 holderPos, Vector3 helperPos)
        {
            if (_gift == null || _item == null) return false;
            Vector3 he = _gift.HalfExtents;
            if (he.x <= 0f || he.y <= 0f || he.z <= 0f) return false;

            ComputePose(holderPos, helperPos, out Vector3 mid, out Quaternion boxRot, out _);
            Vector3 localCenter = _bikeBox != null ? Vector3.Scale(_bikeBox.center, _item.lossyScale) : Vector3.zero;
            Vector3 center = mid + boxRot * localCenter;

            float skin = Mathf.Max(0f, clearanceSkin);
            Vector3 half = new Vector3(
                Mathf.Max(0.01f, he.x - skin),
                Mathf.Max(0.01f, he.y - skin),
                Mathf.Max(0.01f, he.z - skin));

            // Cut the bottom of the box off at the (higher) feet + clearance so floors and low steps never count.
            Vector3 up = boxRot * Vector3.up;
            if (up.y < 0f) up = -up;
            if (up.y > 0.5f)
            {
                float step = 0f;
                if (_holderCc != null) step = Mathf.Max(step, _holderCc.stepOffset);
                if (_helperCc != null) step = Mathf.Max(step, _helperCc.stepOffset);
                float minBottom = Mathf.Max(holderPos.y, helperPos.y) + Mathf.Max(floorClearance, step);
                float bottom = center.y - half.y * up.y;
                if (bottom < minBottom)
                {
                    float cut = (minBottom - bottom) / up.y;
                    if (cut >= half.y * 2f - 0.02f) return false; // whole box is below the clearance line
                    half.y -= cut * 0.5f;
                    center += up * (cut * 0.5f);
                }
            }

            Transform ht = Holder.transform, kt = Helper.transform;
            int n = Physics.OverlapBoxNonAlloc(center, half, _overlap, boxRot, _mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _overlap[i];
                if (c == null || c.isTrigger) continue;
                Transform t = c.transform;
                if (t == ht || t.IsChildOf(ht) || t == kt || t.IsChildOf(kt)) continue; // the elves themselves
                if (t == _item || t.IsChildOf(_item)) continue; // the bike
                Rigidbody body = c.attachedRigidbody;
                if (body != null && !body.isKinematic) continue; // loose props are not door frames
                return true;
            }
            return false;
        }
    }
}
