using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>A thrown catnip pouch in the world. Plain C# handle kept in <see cref="CatnipThrower.Active"/>.</summary>
    public sealed class CatnipPouch
    {
        public GameObject Go;
        public float SpawnTime;
        /// <summary>The cat currently using this pouch (null = free). Only one cat per pouch.</summary>
        public Object Claimer;

        public bool Alive => Go != null;
        public bool IsClaimed => Claimer != null; // Unity null check: a destroyed claimer frees the pouch
        public Vector3 Position => Go != null ? Go.transform.position : Vector3.zero;
    }

    /// <summary>
    /// "Кошачья мята": [F] / LB throws a green felt pouch ~2 m forward (consumes one PlayerProfile.CatnipPouches).
    /// Cats (Stealth/FatCat) look up nearby pouches through the static registry below, claim one,
    /// purr next to it and then call <see cref="Consume"/>. Unclaimed pouches vanish after <see cref="UnclaimedLifetime"/> s.
    /// </summary>
    [DisallowMultipleComponent]
    public class CatnipThrower : MonoBehaviour
    {
        public const float UnclaimedLifetime = 45f;

        private static readonly List<CatnipPouch> _active = new List<CatnipPouch>();
        private static Material _pouchMat;

        /// <summary>Pouches currently lying in the world (may contain destroyed entries until the next prune).</summary>
        public static IReadOnlyList<CatnipPouch> Active => _active;

        [SerializeField] private float throwForwardSpeed = 3.2f;
        [SerializeField] private float throwUpSpeed = 2.2f;
        [SerializeField] private Vector3 pouchSize = new Vector3(0.12f, 0.09f, 0.12f);
        [SerializeField] private float throwCooldown = 0.5f;

        private ElfInput _input;
        private ElfRagdoll _ragdoll;
        private CharacterController _cc;
        private float _cooldown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active.Clear();
            _pouchMat = null;
        }

        private void Awake()
        {
            _input = GetComponent<ElfInput>();
            _ragdoll = GetComponent<ElfRagdoll>();
            _cc = GetComponent<CharacterController>();
        }

        private void Update()
        {
            PruneAndExpire();
            if (_cooldown > 0f) _cooldown -= Time.deltaTime;

            if (_input == null) _input = GetComponent<ElfInput>();
            if (_input == null || !_input.CatnipPressed) return;
            if (!PlayerGate.CanControl || _cooldown > 0f) return;
            if (_ragdoll != null && _ragdoll.IsLimp) return;

            PlayerProfile profile = GameServices.Profile;
            if (profile == null || !profile.TryUseCatnip())
            {
                GameEvents.RaiseToast("Мяты нет — купите на верстаке.");
                _cooldown = throwCooldown;
                return;
            }

            Throw();
            _cooldown = throwCooldown;
            GameEvents.RaiseToast("Пакетик мяты брошен.");
        }

        private void Throw()
        {
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            Vector3 spawn = transform.position + Vector3.up * (PlayerSetup.ControllerHeight * 0.75f)
                            + fwd * (PlayerSetup.ControllerRadius + 0.15f);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CatnipPouch";
            go.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(fwd, Vector3.up));
            go.transform.localScale = pouchSize;

            var col = go.GetComponent<Collider>();
            if (col != null && _cc != null) Physics.IgnoreCollision(col, _cc);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Material m = GetPouchMaterial(mr.sharedMaterial);
                if (m != null) mr.sharedMaterial = m;
            }

            // A little knot of string on top so it reads as a pouch, not a box.
            GameObject knot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knot.name = "Knot";
            Object.DestroyImmediate(knot.GetComponent<Collider>()); // before parenting, so it never joins the compound collider
            knot.transform.SetParent(go.transform, false);
            knot.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            knot.transform.localScale = new Vector3(0.45f, 0.5f, 0.45f);
            var kr = knot.GetComponent<MeshRenderer>();
            if (kr != null && mr != null) kr.sharedMaterial = mr.sharedMaterial;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = fwd * throwForwardSpeed + Vector3.up * throwUpSpeed;
            rb.angularVelocity = new Vector3(Random.Range(-6f, 6f), Random.Range(-3f, 3f), Random.Range(-6f, 6f));

            _active.Add(new CatnipPouch { Go = go, SpawnTime = Time.time });
        }

        private static Material GetPouchMaterial(Material template)
        {
            if (_pouchMat != null) return _pouchMat;
            if (template == null) return null;
            _pouchMat = new Material(template) { name = "CatnipFelt" };
            Color felt = new Color(0.30f, 0.62f, 0.25f);
            if (_pouchMat.HasProperty("_BaseColor")) _pouchMat.SetColor("_BaseColor", felt);
            if (_pouchMat.HasProperty("_Color")) _pouchMat.SetColor("_Color", felt);
            if (_pouchMat.HasProperty("_Smoothness")) _pouchMat.SetFloat("_Smoothness", 0.05f);
            if (_pouchMat.HasProperty("_Glossiness")) _pouchMat.SetFloat("_Glossiness", 0.05f);
            return _pouchMat;
        }

        private static void PruneAndExpire()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                CatnipPouch p = _active[i];
                if (p == null || !p.Alive) { _active.RemoveAt(i); continue; }
                if (!p.IsClaimed && now - p.SpawnTime > UnclaimedLifetime)
                {
                    Object.Destroy(p.Go);
                    _active.RemoveAt(i);
                }
            }
        }

        // ---------------- registry API (used by cats) ----------------

        /// <summary>Nearest free (unclaimed, alive) pouch within radius of a point. Horizontal distance, max 2 m height difference.</summary>
        public static bool TryFindFree(Vector3 from, float radius, out CatnipPouch pouch)
        {
            pouch = null;
            float best = radius * radius;
            for (int i = 0; i < _active.Count; i++)
            {
                CatnipPouch p = _active[i];
                if (p == null || !p.Alive || p.IsClaimed) continue;
                Vector3 d = p.Position - from;
                if (Mathf.Abs(d.y) > 2f) continue;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= best)
                {
                    best = sq;
                    pouch = p;
                }
            }
            return pouch != null;
        }

        /// <summary>Removes the pouch from the world (cat is done with it).</summary>
        public static void Consume(CatnipPouch pouch)
        {
            if (pouch == null) return;
            _active.Remove(pouch);
            if (pouch.Go != null) Object.Destroy(pouch.Go);
            pouch.Go = null;
            pouch.Claimer = null;
        }
    }
}
