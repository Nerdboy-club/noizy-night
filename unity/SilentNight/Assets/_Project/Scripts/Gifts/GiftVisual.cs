using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Builds the look of a gift: either an instance of <see cref="modelPrefab"/> or a placeholder made of primitives.
    /// Decor parts never have colliders; the gift root gets ONE BoxCollider sized to the gift.
    /// Built on demand by <see cref="Gift"/> (not in its own Awake) so the kind is known first.
    /// </summary>
    [DisallowMultipleComponent]
    public class GiftVisual : MonoBehaviour
    {
        [Tooltip("Optional real model. If null, a primitive placeholder is built.")]
        [SerializeField] private GameObject modelPrefab;
        [Tooltip("If > 0, overrides the main collider size (otherwise per-kind default / model bounds).")]
        [SerializeField] private Vector3 colliderSizeOverride = Vector3.zero;

        [Header("Placeholder colours")]
        [SerializeField] private Color smallPaper = new Color32(0xc6, 0x28, 0x28, 0xff);
        [SerializeField] private Color smallRibbon = new Color32(0x1f, 0x9d, 0x55, 0xff);
        [SerializeField] private Color noisyPaper = new Color32(0xc2, 0x74, 0x2b, 0xff);
        [SerializeField] private Color noisyRibbon = new Color32(0xdc, 0x26, 0x26, 0xff);
        [SerializeField] private Color hugePaper = new Color32(0x25, 0x63, 0xeb, 0xff);
        [SerializeField] private Color hugeRibbon = new Color32(0xf5, 0xb0, 0x21, 0xff);
        [SerializeField] private Color tire = new Color32(0x1f, 0x1f, 0x24, 0xff);
        [SerializeField] private Color gold = new Color32(0xf5, 0xb0, 0x21, 0xff);
        [SerializeField] private Color silver = new Color32(0xe2, 0xe8, 0xf0, 0xff);
        [SerializeField] private Color rcPaper = new Color32(0x7c, 0x3a, 0xed, 0xff);
        [SerializeField] private Color rcRibbon = new Color32(0xfa, 0xcc, 0x15, 0xff);
        [SerializeField] private Color rcBall = new Color32(0xef, 0x44, 0x44, 0xff);

        public Transform WindupKey { get; private set; }
        /// <summary>RcCar only: antenna pivot (at its base), wobbled by RcCarDriver while driving.</summary>
        public Transform RcAntenna { get; private set; }
        public Renderer[] Renderers { get; private set; } = System.Array.Empty<Renderer>();
        public BoxCollider MainCollider { get; private set; }
        public Vector3 ColliderSize => MainCollider != null ? MainCollider.size : Vector3.one * 0.4f;

        private Transform _root;
        private GiftKind _builtKind;
        private bool _built;

        private static readonly Dictionary<(Color, float, float), Material> MaterialCache = new Dictionary<(Color, float, float), Material>();
        private static Shader _litShader;

        /// <summary>Allows GiftFactory to inject a per-kind model before the gift is activated.</summary>
        public void SetModelPrefab(GameObject prefab) => modelPrefab = prefab;

        public void Build(GiftKind kind)
        {
            if (_built && _builtKind == kind && _root != null) return;
            if (_root != null)
            {
                // Detach first so GetComponentsInChildren no longer sees the old parts this frame.
                var old = _root.gameObject;
                old.SetActive(false);
                _root.SetParent(null, false);
                DestroySafe(old);
            }
            WindupKey = null;
            RcAntenna = null;

            _root = new GameObject("Visual").transform;
            _root.SetParent(transform, false);

            Vector3 size;
            if (modelPrefab != null)
            {
                var model = Instantiate(modelPrefab, _root, false);
                StripColliders(model);
                size = ComputeLocalBounds(out Vector3 center);
                SetupCollider(size, center);
            }
            else
            {
                switch (kind)
                {
                    case GiftKind.Noisy: size = BuildNoisy(); break;
                    case GiftKind.Huge: size = BuildHuge(); break;
                    case GiftKind.Fragile: size = BuildFragile(); break;
                    case GiftKind.RcCar: size = BuildRcCar(); break;
                    default: size = BuildSmall(); break;
                }
                SetupCollider(size, Vector3.zero);
            }

            Renderers = _root.GetComponentsInChildren<Renderer>(true);
            _builtKind = kind;
            _built = true;
        }

        // ---------------- placeholders ----------------

        private Vector3 BuildSmall()
        {
            Material paper = Mat(smallPaper), ribbon = Mat(smallRibbon), bell = Mat(gold, 0.8f, 0.75f);
            Part(PrimitiveType.Cube, paper, new Vector3(0f, -0.02f, 0f), new Vector3(0.38f, 0.34f, 0.38f));
            Part(PrimitiveType.Cube, paper, new Vector3(0f, 0.17f, 0f), new Vector3(0.42f, 0.1f, 0.42f)); // lid
            // crossed ribbons
            Part(PrimitiveType.Cube, ribbon, new Vector3(0f, -0.01f, 0f), new Vector3(0.1f, 0.36f, 0.395f));
            Part(PrimitiveType.Cube, ribbon, new Vector3(0f, -0.01f, 0f), new Vector3(0.395f, 0.36f, 0.1f));
            Part(PrimitiveType.Cube, ribbon, new Vector3(0f, 0.17f, 0f), new Vector3(0.1f, 0.105f, 0.435f));
            Part(PrimitiveType.Cube, ribbon, new Vector3(0f, 0.17f, 0f), new Vector3(0.435f, 0.105f, 0.1f));
            Bow(ribbon, new Vector3(0f, 0.25f, 0f), 1f);
            Part(PrimitiveType.Sphere, bell, new Vector3(0f, 0.215f, 0.07f), Vector3.one * 0.08f);
            return new Vector3(0.42f, 0.42f, 0.42f);
        }

        private Vector3 BuildNoisy()
        {
            Material paper = Mat(noisyPaper), ribbon = Mat(noisyRibbon), metal = Mat(silver, 0.85f, 0.8f), g = Mat(gold, 0.8f, 0.75f);
            Part(PrimitiveType.Cube, paper, Vector3.zero, new Vector3(0.44f, 0.44f, 0.44f));
            Part(PrimitiveType.Cube, ribbon, Vector3.zero, new Vector3(0.465f, 0.12f, 0.465f));
            Part(PrimitiveType.Cube, ribbon, Vector3.zero, new Vector3(0.12f, 0.465f, 0.465f));
            Bow(ribbon, new Vector3(0f, 0.25f, 0f), 0.9f);
            // alarm-clock bells on top
            Part(PrimitiveType.Sphere, metal, new Vector3(-0.15f, 0.24f, 0f), new Vector3(0.14f, 0.1f, 0.14f));
            Part(PrimitiveType.Sphere, metal, new Vector3(0.15f, 0.24f, 0f), new Vector3(0.14f, 0.1f, 0.14f));
            Part(PrimitiveType.Sphere, g, new Vector3(0f, 0f, 0.235f), Vector3.one * 0.07f);

            // wind-up key on the back (-Z), spins around its local Z axis
            var key = new GameObject("WindupKey").transform;
            key.SetParent(_root, false);
            key.localPosition = new Vector3(0f, 0.02f, -0.24f);
            var shaft = Part(PrimitiveType.Cylinder, g, Vector3.zero, new Vector3(0.04f, 0.05f, 0.04f), key);
            shaft.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.localPosition = new Vector3(0f, 0f, -0.05f);
            Part(PrimitiveType.Sphere, g, new Vector3(-0.06f, 0f, -0.1f), new Vector3(0.1f, 0.07f, 0.025f), key);
            Part(PrimitiveType.Sphere, g, new Vector3(0.06f, 0f, -0.1f), new Vector3(0.1f, 0.07f, 0.025f), key);
            WindupKey = key;

            return new Vector3(0.46f, 0.46f, 0.46f);
        }

        private Renderer _globeGlass;

        /// <summary>Glass snow globe on a wooden base with a tiny tree inside.</summary>
        private Vector3 BuildFragile()
        {
            Material wood = Mat(new Color(0.45f, 0.27f, 0.14f)), glass = Mat(new Color(0.75f, 0.9f, 1f), 0f, 0.95f);
            Material tree = Mat(new Color(0.15f, 0.55f, 0.28f)), snow = Mat(Color.white), ribbon = Mat(smallRibbon);
            Part(PrimitiveType.Cylinder, wood, new Vector3(0f, -0.12f, 0f), new Vector3(0.3f, 0.05f, 0.3f));
            Part(PrimitiveType.Cylinder, ribbon, new Vector3(0f, -0.075f, 0f), new Vector3(0.31f, 0.012f, 0.31f));
            Part(PrimitiveType.Cylinder, snow, new Vector3(0f, -0.05f, 0f), new Vector3(0.22f, 0.02f, 0.22f));
            Part(PrimitiveType.Capsule, tree, new Vector3(0f, 0.02f, 0f), new Vector3(0.08f, 0.07f, 0.08f));
            Transform g = Part(PrimitiveType.Sphere, glass, new Vector3(0f, 0.03f, 0f), Vector3.one * 0.28f);
            _globeGlass = g.GetComponent<Renderer>();
            return new Vector3(0.3f, 0.32f, 0.3f);
        }

        /// <summary>Snow globe is cracked: cloudy grey glass.</summary>
        public void ShowBroken()
        {
            if (_globeGlass != null) _globeGlass.sharedMaterial = Mat(new Color(0.45f, 0.47f, 0.5f), 0f, 0.3f);
        }

        /// <summary>
        /// Radio-controlled toy car in gift wrap: low box on 4 little wheels with an antenna and a red ball.
        /// Long axis = local Z (forward = drive direction). Collider 0.22 x 0.16 x 0.32.
        /// </summary>
        private Vector3 BuildRcCar()
        {
            Material paper = Mat(rcPaper), ribbon = Mat(rcRibbon), rubber = Mat(tire, 0f, 0.2f);
            Material hub = Mat(silver, 0.8f, 0.7f), ball = Mat(rcBall, 0f, 0.6f), metal = Mat(silver, 0.9f, 0.8f);

            // wheels (axis along local X), bottoms touch the collider floor at y = -0.08
            const float wheelD = 0.1f;
            const float wy = -0.08f + wheelD * 0.5f;
            foreach (float x in new[] { -0.09f, 0.09f })
            foreach (float z in new[] { -0.1f, 0.1f })
            {
                var w = Part(PrimitiveType.Cylinder, rubber, new Vector3(x, wy, z), new Vector3(wheelD, 0.02f, wheelD));
                w.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var h = Part(PrimitiveType.Cylinder, hub, new Vector3(x * 1.12f, wy, z), new Vector3(0.04f, 0.006f, 0.04f));
                h.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // wrapped body + small cabin
            Part(PrimitiveType.Cube, paper, new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.09f, 0.3f));
            Part(PrimitiveType.Cube, paper, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.13f, 0.04f, 0.14f));
            // ribbons
            Part(PrimitiveType.Cube, ribbon, new Vector3(0f, 0.005f, 0f), new Vector3(0.165f, 0.095f, 0.04f));
            Part(PrimitiveType.Cube, ribbon, new Vector3(0f, 0.005f, 0f), new Vector3(0.04f, 0.095f, 0.305f));
            Part(PrimitiveType.Cube, ribbon, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.04f, 0.045f, 0.145f));
            Bow(ribbon, new Vector3(0f, 0.085f, -0.02f), 0.45f);
            // headlights on the nose (+Z)
            Part(PrimitiveType.Sphere, Mat(gold, 0.5f, 0.9f), new Vector3(-0.05f, 0.01f, 0.15f), Vector3.one * 0.03f);
            Part(PrimitiveType.Sphere, Mat(gold, 0.5f, 0.9f), new Vector3(0.05f, 0.01f, 0.15f), Vector3.one * 0.03f);

            // antenna on the back-right corner, pivot at its base
            var ant = new GameObject("RcAntenna").transform;
            ant.SetParent(_root, false);
            ant.localPosition = new Vector3(0.055f, 0.045f, -0.12f);
            Part(PrimitiveType.Cylinder, metal, new Vector3(0f, 0.07f, 0f), new Vector3(0.008f, 0.07f, 0.008f), ant);
            Part(PrimitiveType.Sphere, ball, new Vector3(0f, 0.145f, 0f), Vector3.one * 0.035f, ant);
            RcAntenna = ant;

            return new Vector3(0.22f, 0.16f, 0.32f);
        }

        private Vector3 BuildHuge()
        {
            // Bike wrapped in paper, side view along local X, wheels on the XY plane. Box ~1.64 x 1.04 x 0.68.
            Material paper = Mat(hugePaper), ribbon = Mat(hugeRibbon), rubber = Mat(tire, 0f, 0.2f), hub = Mat(silver, 0.8f, 0.7f);
            const float wheelR = 0.45f;
            const float groundY = -0.52f;
            float wy = groundY + wheelR;
            Vector3 rear = new Vector3(-0.55f, wy, 0f), front = new Vector3(0.55f, wy, 0f);

            foreach (var c in new[] { rear, front })
            {
                // torus-like wheel: flattened cylinders (tire, paper disc, hub)
                Wheel(rubber, c, wheelR * 2f, 0.1f);
                Wheel(paper, c, wheelR * 1.55f, 0.12f);
                Wheel(hub, c, 0.12f, 0.16f);
            }

            Vector3 seat = new Vector3(-0.2f, 0.36f, 0f);
            Vector3 bracket = new Vector3(0f, wy, 0f);
            Vector3 head = new Vector3(0.38f, 0.34f, 0f);
            const float t = 0.07f;
            Bar(paper, rear, seat, t);
            Bar(paper, seat, bracket, t);
            Bar(paper, bracket, head, t);
            Bar(paper, seat, head, t);
            Bar(paper, rear, bracket, t);
            Bar(paper, head, front, t);
            Bar(paper, head, head + new Vector3(0.03f, 0.13f, 0f), t);
            // handlebar across Z
            Bar(rubber, head + new Vector3(0.03f, 0.13f, -0.26f), head + new Vector3(0.03f, 0.13f, 0.26f), 0.05f);
            // saddle
            Part(PrimitiveType.Cube, rubber, seat + new Vector3(0f, 0.07f, 0f), new Vector3(0.26f, 0.06f, 0.14f));
            // ribbon wrapped around the frame + bow on the top tube
            Part(PrimitiveType.Cube, ribbon, new Vector3(0.09f, 0.33f, 0f), new Vector3(0.08f, 0.12f, 0.12f));
            Bow(ribbon, new Vector3(0.09f, 0.43f, 0f), 1.6f);

            return new Vector3(1.64f, 1.04f, 0.68f);
        }

        private void Bow(Material m, Vector3 pos, float scale)
        {
            var l = Part(PrimitiveType.Sphere, m, pos + new Vector3(-0.075f, 0.02f, 0f) * scale, new Vector3(0.15f, 0.105f, 0.08f) * scale);
            l.localRotation = Quaternion.Euler(0f, 0f, 28f);
            var r = Part(PrimitiveType.Sphere, m, pos + new Vector3(0.075f, 0.02f, 0f) * scale, new Vector3(0.15f, 0.105f, 0.08f) * scale);
            r.localRotation = Quaternion.Euler(0f, 0f, -28f);
            Part(PrimitiveType.Sphere, m, pos, Vector3.one * 0.09f * scale);
        }

        private void Wheel(Material m, Vector3 center, float diameter, float thickness)
        {
            // Unity cylinder: height 2 along Y, diameter 1. Rotate so its axis is local Z.
            var w = Part(PrimitiveType.Cylinder, m, center, new Vector3(diameter, thickness * 0.5f, diameter));
            w.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private void Bar(Material m, Vector3 from, Vector3 to, float thickness)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.0001f) return;
            var b = Part(PrimitiveType.Cube, m, (from + to) * 0.5f, new Vector3(thickness, len, thickness));
            b.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
        }

        private Transform Part(PrimitiveType type, Material m, Vector3 localPos, Vector3 localScale, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; DestroySafe(col); }
            go.name = type.ToString();
            var t = go.transform;
            t.SetParent(parent != null ? parent : _root, false);
            t.localPosition = localPos;
            t.localScale = localScale;
            var r = go.GetComponent<MeshRenderer>();
            if (r != null && m != null) r.sharedMaterial = m;
            return t;
        }

        // ---------------- collider ----------------

        private void SetupCollider(Vector3 size, Vector3 center)
        {
            if (colliderSizeOverride.x > 0f && colliderSizeOverride.y > 0f && colliderSizeOverride.z > 0f)
                size = colliderSizeOverride;

            MainCollider = GetComponent<BoxCollider>();
            if (MainCollider == null) MainCollider = gameObject.AddComponent<BoxCollider>();
            MainCollider.isTrigger = false;
            MainCollider.size = size;
            MainCollider.center = center;
        }

        private Vector3 ComputeLocalBounds(out Vector3 center)
        {
            var renderers = _root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { center = Vector3.zero; return Vector3.one * 0.4f; }
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            // Convert world AABB to local (assumes no rotation of the gift at build time; good enough for a box).
            center = transform.InverseTransformPoint(b.center);
            Vector3 ls = transform.lossyScale;
            return new Vector3(b.size.x / Mathf.Max(0.0001f, ls.x), b.size.y / Mathf.Max(0.0001f, ls.y), b.size.z / Mathf.Max(0.0001f, ls.z));
        }

        private static void StripColliders(GameObject go)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) { cols[i].enabled = false; DestroySafe(cols[i]); }
            var rbs = go.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++) DestroySafe(rbs[i]);
        }

        // ---------------- materials ----------------

        public static Material Mat(Color c, float metallic = 0f, float smoothness = 0.55f)
        {
            var key = (c, metallic, smoothness);
            if (MaterialCache.TryGetValue(key, out var cached) && cached != null) return cached;

            if (_litShader == null)
            {
                // VERIFY: URP Lit must be included in builds (it is when any material in the project uses it).
                _litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (_litShader == null) _litShader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (_litShader == null) _litShader = Shader.Find("Standard");
            }
            // No shader found (stripped from the build): callers keep the primitive's default material.
            if (_litShader == null) return null;

            var m = new Material(_litShader) { name = "GiftPlaceholder" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);

            MaterialCache[key] = m;
            return m;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            MaterialCache.Clear();
            _litShader = null;
        }

        private static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
