using System;
using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Local co-op slapstick: two elves running into each other bonk heads and both flop over (Gang Beasts style);
    /// a slower bump just shoves the other elf along (elves can push each other around).
    ///
    /// One instance per elf (PlayerSetup adds it), but only the first enabled instance (the "leader") runs the pair
    /// pass each FixedUpdate, so every pair is handled exactly once. The pass iterates over GameServices.Players
    /// (every registered ElfController), so elves baked into scenes without this component still take part as long
    /// as any elf has it (joined co-op elves always do). Solo = one player = nothing happens.
    ///
    /// Pair rules: horizontal distance &lt; radiusA + radiusB + contactMargin, roughly the same height.
    /// Skipped: co-carrying the same bike, limp / getting up, InputLocked, mantling, not grounded, CC disabled.
    /// Closing speed (relative planar velocity projected on the line between them) &gt;= bonkMinClosingSpeed
    /// → both go limp, pushed apart (ElfRagdoll.GoLimp), dust puff, noise, toast; per-pair cooldown.
    /// Below that → the slower elf is shoved along the line (CharacterController.Move, a few cm per step).
    ///
    /// CharacterControllers may block each other (and ElfController clamps its velocity to the blocked move), so the
    /// closing speed also considers each elf's velocity from the previous physics step.
    /// </summary>
    [DisallowMultipleComponent]
    public class ElfBonk : MonoBehaviour
    {
        public const string BonkToast = "Бдыщ! Эльфы столкнулись лбами.";

        [Tooltip("Extra gap (m) beyond both capsule radii that already counts as touching.")]
        [SerializeField] private float contactMargin = 0.08f;
        [Tooltip("Closing speed (m/s) needed for a head bonk. Two walking elves ≈ 5, walking into a standing one ≈ 2.5.")]
        [SerializeField] private float bonkMinClosingSpeed = 3f;
        [SerializeField] private float bonkBackSpeed = 2f;
        [SerializeField] private float bonkUpSpeed = 1.5f;
        [SerializeField] private float bonkNoise = 40f;
        [SerializeField] private float pairCooldown = 3f;
        [Tooltip("Elves whose feet differ by more than this (m) never interact (one is on a ledge / crate).")]
        [SerializeField] private float maxHeightDifference = 0.35f;

        [Header("Shove (below the bonk threshold)")]
        [Tooltip("Fraction of the closing speed handed to the slower elf.")]
        [SerializeField] private float shoveTransfer = 0.9f;
        [SerializeField] private float maxShoveSpeed = 2.5f;
        [Tooltip("Max shove per physics step (m).")]
        [SerializeField] private float maxShoveStep = 0.05f;
        [Tooltip("Closing speeds below this (m/s) do not shove; overlapping capsules are just separated.")]
        [SerializeField] private float minShoveClosingSpeed = 0.05f;

        private sealed class ElfState
        {
            public int Id;
            public ElfController Ctrl;
            public CharacterController Cc;
            public ElfRagdoll Ragdoll;
            public ElfCarry Carry;
            public Vector3 PrevVelocity;
            public bool HasPrev;
            public bool Seen;
        }

        private static readonly List<ElfBonk> s_Instances = new List<ElfBonk>(4);
        private static readonly Dictionary<ElfController, ElfState> s_States = new Dictionary<ElfController, ElfState>(4);
        private static readonly List<ElfState> s_Active = new List<ElfState>(4);
        private static readonly List<ElfController> s_Stale = new List<ElfController>(4);
        private static readonly Dictionary<long, float> s_PairNextTime = new Dictionary<long, float>(8);
        private static int s_NextId;

        /// <summary>Number of head bonks since the last reset (tests). Set to 0 to reset.</summary>
        public static int BonkCount { get; set; }

        /// <summary>Raised on every head bonk (both elves).</summary>
        public static event Action<ElfController, ElfController> Bonked;

        public static void ResetBonkCount() => BonkCount = 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Instances.Clear();
            s_States.Clear();
            s_Active.Clear();
            s_Stale.Clear();
            s_PairNextTime.Clear();
            s_NextId = 0;
            BonkCount = 0;
            Bonked = null;
        }

        private void OnEnable()
        {
            if (!s_Instances.Contains(this)) s_Instances.Add(this);
        }

        private void OnDisable() => s_Instances.Remove(this);

        private void FixedUpdate()
        {
            // Leader election: only the first enabled instance runs the (global) pair pass.
            for (int i = s_Instances.Count - 1; i >= 0; i--)
                if (s_Instances[i] == null) s_Instances.RemoveAt(i);
            if (s_Instances.Count == 0 || s_Instances[0] != this) return;

            CollectElves();
            if (s_Active.Count >= 2)
            {
                float dt = Time.fixedDeltaTime;
                for (int i = 0; i < s_Active.Count; i++)
                    for (int j = i + 1; j < s_Active.Count; j++)
                        EvaluatePair(s_Active[i], s_Active[j], dt);
            }

            // Remember this step's velocities (CC-vs-CC blocking clamps them on the next frame).
            for (int i = 0; i < s_Active.Count; i++)
            {
                ElfState s = s_Active[i];
                if (s.Ctrl == null) continue;
                s.PrevVelocity = Planar(s.Ctrl.Velocity);
                s.HasPrev = true;
            }
        }

        // ---------------------------------------------------------------- registry

        private static void CollectElves()
        {
            s_Active.Clear();
            foreach (KeyValuePair<ElfController, ElfState> kv in s_States) kv.Value.Seen = false;

            IReadOnlyList<IPlayerStatus> players = GameServices.Players;
            for (int i = 0; i < players.Count; i++)
            {
                ElfController c = players[i] as ElfController;
                if (c == null || !c.isActiveAndEnabled) continue;
                if (!s_States.TryGetValue(c, out ElfState s))
                {
                    s = new ElfState
                    {
                        Id = ++s_NextId,
                        Ctrl = c,
                        Cc = c.GetComponent<CharacterController>(),
                        Ragdoll = c.GetComponent<ElfRagdoll>(),
                        Carry = c.GetComponent<ElfCarry>(),
                    };
                    s_States[c] = s;
                }
                if (s.Ragdoll == null) s.Ragdoll = c.GetComponent<ElfRagdoll>(); // added later at runtime
                if (s.Carry == null) s.Carry = c.GetComponent<ElfCarry>();
                if (s.Seen) continue; // duplicate registration
                s.Seen = true;
                s_Active.Add(s);
            }

            // Forget elves that left (destroyed / disabled) so their state does not leak.
            if (s_States.Count > s_Active.Count)
            {
                s_Stale.Clear();
                foreach (KeyValuePair<ElfController, ElfState> kv in s_States)
                    if (!kv.Value.Seen) s_Stale.Add(kv.Key);
                for (int i = 0; i < s_Stale.Count; i++) s_States.Remove(s_Stale[i]);
                s_Stale.Clear();
            }
        }

        // ---------------------------------------------------------------- pair logic

        private static bool CanInteract(ElfState s)
        {
            if (s.Ctrl == null || s.Cc == null || !s.Cc.enabled) return false;
            if (!s.Ctrl.IsGrounded || s.Ctrl.InputLocked || s.Ctrl.IsMantling) return false;
            if (s.Ragdoll != null && s.Ragdoll.IsLimp) return false;
            return true;
        }

        private static bool CoCarryingTogether(ElfState a, ElfState b)
        {
            if (a.Carry == null || b.Carry == null) return false;
            CoCarry ca = a.Carry.ActiveCoCarry;
            return ca != null && ReferenceEquals(ca, b.Carry.ActiveCoCarry);
        }

        private void EvaluatePair(ElfState a, ElfState b, float dt)
        {
            if (!CanInteract(a) || !CanInteract(b)) return;
            if (CoCarryingTogether(a, b)) return; // the bike's rope logic owns their spacing

            Vector3 pa = a.Ctrl.transform.position, pb = b.Ctrl.transform.position;
            if (Mathf.Abs(pa.y - pb.y) > maxHeightDifference) return;

            Vector3 d = Planar(pb - pa);
            float dist = d.magnitude;
            float radii = Radius(a) + Radius(b);
            if (dist >= radii + contactMargin) return;

            Vector3 n = dist > 1e-4f ? d / dist : Planar(a.Ctrl.transform.forward).normalized; // A -> B
            if (n.sqrMagnitude < 0.5f) n = Vector3.forward;

            Vector3 va = Planar(a.Ctrl.Velocity), vb = Planar(b.Ctrl.Velocity);
            float closingNow = Vector3.Dot(va - vb, n);
            float closing = closingNow;
            if (a.HasPrev && b.HasPrev)
                closing = Mathf.Max(closing, Vector3.Dot(a.PrevVelocity - b.PrevVelocity, n));

            if (closing >= bonkMinClosingSpeed && PairReady(a, b))
            {
                Bonk(a, b, n, pa, pb);
                return;
            }

            Shove(a, b, n, va, vb, closing, dist, radii, dt);
        }

        private static bool PairReady(ElfState a, ElfState b)
        {
            long key = PairKey(a, b);
            return !s_PairNextTime.TryGetValue(key, out float next) || Time.time >= next;
        }

        private void Bonk(ElfState a, ElfState b, Vector3 n, Vector3 pa, Vector3 pb)
        {
            s_PairNextTime[PairKey(a, b)] = Time.time + pairCooldown;
            BonkCount++;

            Vector3 up = Vector3.up * bonkUpSpeed;
            if (a.Ragdoll != null) a.Ragdoll.GoLimp(-n * bonkBackSpeed + up);
            if (b.Ragdoll != null) b.Ragdoll.GoLimp(n * bonkBackSpeed + up);

            Vector3 mid = (pa + pb) * 0.5f;
            SilentNight.FX.LandingDust.Spawn(mid + Vector3.up * (PlayerSetup.ControllerHeight * 0.6f), 1f);
            SilentNight.FX.LandingDust.Spawn(mid, 0.6f);
            if (bonkNoise > 0f)
                GameEvents.RaiseNoise(new NoiseEvent(mid, Mathf.Clamp(bonkNoise, 0f, 100f), NoiseSource.Landing, a.Ctrl.gameObject));
            GameEvents.RaiseToast(BonkToast);
            Bonked?.Invoke(a.Ctrl, b.Ctrl);
        }

        /// <summary>The faster elf pushes the slower one along the line between them; overlapping capsules are separated.</summary>
        private void Shove(ElfState a, ElfState b, Vector3 n, Vector3 va, Vector3 vb, float closing, float dist,
            float radii, float dt)
        {
            if (closing >= minShoveClosingSpeed)
            {
                bool aFaster = va.sqrMagnitude >= vb.sqrMagnitude;
                ElfState slow = aFaster ? b : a;
                Vector3 dir = aFaster ? n : -n; // from the faster elf toward the slower one
                float speed = Mathf.Min(closing * shoveTransfer, maxShoveSpeed);
                float step = Mathf.Min(speed * dt, maxShoveStep);
                Move(slow, dir * step);
                return;
            }

            float overlap = radii - dist;
            if (overlap > 0.001f)
            {
                float half = Mathf.Min(overlap * 0.5f, maxShoveStep * 0.5f);
                Move(a, -n * half);
                Move(b, n * half);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static void Move(ElfState s, Vector3 delta)
        {
            if (s.Cc == null || !s.Cc.enabled || delta.sqrMagnitude < 1e-10f) return;
            s.Cc.Move(delta);
        }

        private static float Radius(ElfState s)
        {
            if (s.Cc == null) return PlayerSetup.ControllerRadius;
            Vector3 sc = s.Ctrl.transform.lossyScale;
            return s.Cc.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z));
        }

        private static long PairKey(ElfState a, ElfState b)
        {
            int lo = Mathf.Min(a.Id, b.Id), hi = Mathf.Max(a.Id, b.Id);
            return ((long)lo << 32) | (uint)hi;
        }

        private static Vector3 Planar(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
