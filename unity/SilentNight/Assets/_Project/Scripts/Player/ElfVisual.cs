using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>
    /// Elf look. If modelPrefab is null, builds a cute placeholder elf from primitives at runtime
    /// (capsule body, sphere head, stacked-cylinder hat with pompom). Provides HoldPoint / SackAnchor
    /// transforms (direct children of the elf root, so crouch scaling never moves held gifts).
    /// Also shows a sack blob (when the sack is not empty) and a jetpack (when the upgrade is owned).
    /// </summary>
    [DisallowMultipleComponent]
    public class ElfVisual : MonoBehaviour
    {
        [SerializeField] private GameObject modelPrefab;
        [Range(0, 3)] [SerializeField] private int themeIndex;
        [SerializeField] private Vector3 holdPointLocal = new Vector3(0f, 0.55f, 0.45f);
        [SerializeField] private Vector3 sackAnchorLocal = new Vector3(0f, 0.5f, -0.3f);
        [SerializeField] private float crouchVisualScaleY = 0.7f;
        [SerializeField] private float crouchLerpSpeed = 10f;

        [SerializeField] private Transform holdPoint;
        [SerializeField] private Transform sackAnchor;

        // tunic, hat, trim
        private static readonly Color[] TunicColors =
        {
            new Color(0.15f, 0.62f, 0.25f), // green
            new Color(0.80f, 0.16f, 0.16f), // red
            new Color(0.22f, 0.42f, 0.86f), // blue
            new Color(0.56f, 0.26f, 0.76f)  // purple
        };
        private static readonly Color[] HatColors =
        {
            new Color(0.85f, 0.12f, 0.14f),
            new Color(0.12f, 0.60f, 0.24f),
            new Color(0.95f, 0.80f, 0.20f),
            new Color(0.15f, 0.70f, 0.68f)
        };
        private static readonly Color SkinColor = new Color(1.0f, 0.84f, 0.72f);
        private static readonly Color TrimColor = new Color(0.96f, 0.96f, 0.96f);
        private static readonly Color DarkColor = new Color(0.12f, 0.08f, 0.06f);
        private static readonly Color BeltColor = new Color(0.25f, 0.15f, 0.08f);
        private static readonly Color BuckleColor = new Color(0.95f, 0.80f, 0.25f);
        private static readonly Color SackColor = new Color(0.55f, 0.36f, 0.20f);
        private static readonly Color MetalColor = new Color(0.55f, 0.58f, 0.62f);
        private static readonly Color FlameColor = new Color(1.0f, 0.55f, 0.10f);

        private Transform _visualRoot;
        private Transform _sackVisual;
        private Transform _jetpackVisual;
        private Transform _flameVisual;
        private Renderer[] _tunicRenderers = new Renderer[0];
        private Renderer[] _hatRenderers = new Renderer[0];
        private readonly List<Material> _createdMaterials = new List<Material>();
        private Material _tunicMat, _hatMat;
        private bool _built;

        private ElfController _controller;
        private ElfCarry _carry;

        public Transform HoldPoint { get { EnsureAnchors(); return holdPoint; } }
        public Transform SackAnchor { get { EnsureAnchors(); return sackAnchor; } }
        public Transform VisualRoot => _visualRoot;
        public Animator ModelAnimator { get; private set; }

        public int ThemeIndex
        {
            get => themeIndex;
            set
            {
                themeIndex = Mathf.Clamp(value, 0, TunicColors.Length - 1);
                if (_built) ApplyTheme();
            }
        }

        public GameObject ModelPrefab
        {
            get => modelPrefab;
            set => modelPrefab = value; // takes effect on next Awake/Build
        }

        // ---------------- anchors ----------------

        /// <summary>Creates HoldPoint and SackAnchor children if missing. Safe in edit mode (no materials created).</summary>
        public void EnsureAnchors()
        {
            if (holdPoint == null) holdPoint = FindOrCreateChild("HoldPoint", holdPointLocal);
            if (sackAnchor == null) sackAnchor = FindOrCreateChild("SackAnchor", sackAnchorLocal);
        }

        private Transform FindOrCreateChild(string childName, Vector3 localPos)
        {
            Transform t = transform.Find(childName);
            if (t != null) return t;
            var go = new GameObject(childName);
            go.layer = gameObject.layer;
            t = go.transform;
            t.SetParent(transform, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            return t;
        }

        // ---------------- lifecycle ----------------

        private void Awake()
        {
            EnsureAnchors();
            Build();
        }

        private void Start()
        {
            _controller = GetComponent<ElfController>();
            _carry = GetComponent<ElfCarry>();
            HookAnimator();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _createdMaterials.Count; i++)
                if (_createdMaterials[i] != null) Destroy(_createdMaterials[i]);
            _createdMaterials.Clear();
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            var root = new GameObject("Visual");
            _visualRoot = root.transform;
            _visualRoot.SetParent(transform, false);

            if (modelPrefab != null)
            {
                GameObject model = Instantiate(modelPrefab, _visualRoot);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                // Child colliders would fight the CharacterController.
                foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                ModelAnimator = model.GetComponentInChildren<Animator>(true);
            }
            else
            {
                BuildPlaceholder();
            }

            BuildGadgets();
            SetLayerRecursive(_visualRoot, gameObject.layer);
            HookAnimator();
        }

        private void HookAnimator()
        {
            if (ModelAnimator == null) return;
            ElfAnimator ea = GetComponent<ElfAnimator>();
            if (ea != null && ea.Animator == null) ea.SetAnimator(ModelAnimator);
        }

        private void BuildPlaceholder()
        {
            Color tunic = TunicColors[Mathf.Clamp(themeIndex, 0, TunicColors.Length - 1)];
            Color hat = HatColors[Mathf.Clamp(themeIndex, 0, HatColors.Length - 1)];
            _tunicMat = CreateMaterial(tunic);
            _hatMat = CreateMaterial(hat);
            Material skin = CreateMaterial(SkinColor);
            Material trim = CreateMaterial(TrimColor);
            Material dark = CreateMaterial(DarkColor);
            Material belt = CreateMaterial(BeltColor);
            Material buckle = CreateMaterial(BuckleColor);

            var tunicList = new List<Renderer>();
            var hatList = new List<Renderer>();

            // Legs + boots
            Part(PrimitiveType.Capsule, "LegL", new Vector3(-0.1f, 0.14f, 0f), new Vector3(0.14f, 0.14f, 0.14f), Vector3.zero, dark);
            Part(PrimitiveType.Capsule, "LegR", new Vector3(0.1f, 0.14f, 0f), new Vector3(0.14f, 0.14f, 0.14f), Vector3.zero, dark);

            // Body (tunic)
            tunicList.Add(Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.44f, 0f), new Vector3(0.46f, 0.26f, 0.40f), Vector3.zero, _tunicMat));
            Part(PrimitiveType.Cylinder, "Belt", new Vector3(0f, 0.38f, 0f), new Vector3(0.47f, 0.03f, 0.41f), Vector3.zero, belt);
            Part(PrimitiveType.Cube, "Buckle", new Vector3(0f, 0.38f, 0.2f), new Vector3(0.09f, 0.07f, 0.02f), Vector3.zero, buckle);

            // Arms
            tunicList.Add(Part(PrimitiveType.Capsule, "ArmL", new Vector3(-0.27f, 0.47f, 0f), new Vector3(0.11f, 0.15f, 0.11f), new Vector3(0f, 0f, -12f), _tunicMat));
            tunicList.Add(Part(PrimitiveType.Capsule, "ArmR", new Vector3(0.27f, 0.47f, 0f), new Vector3(0.11f, 0.15f, 0.11f), new Vector3(0f, 0f, 12f), _tunicMat));

            // Collar
            Part(PrimitiveType.Cylinder, "Collar", new Vector3(0f, 0.66f, 0f), new Vector3(0.34f, 0.025f, 0.32f), Vector3.zero, trim);

            // Head
            Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 0.84f, 0f), new Vector3(0.38f, 0.36f, 0.36f), Vector3.zero, skin);
            Part(PrimitiveType.Sphere, "EarL", new Vector3(-0.2f, 0.86f, 0f), new Vector3(0.07f, 0.15f, 0.05f), new Vector3(0f, 0f, 35f), skin);
            Part(PrimitiveType.Sphere, "EarR", new Vector3(0.2f, 0.86f, 0f), new Vector3(0.07f, 0.15f, 0.05f), new Vector3(0f, 0f, -35f), skin);
            Part(PrimitiveType.Sphere, "EyeL", new Vector3(-0.07f, 0.87f, 0.165f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, dark);
            Part(PrimitiveType.Sphere, "EyeR", new Vector3(0.07f, 0.87f, 0.165f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, dark);
            Part(PrimitiveType.Sphere, "Nose", new Vector3(0f, 0.82f, 0.18f), new Vector3(0.05f, 0.05f, 0.05f), Vector3.zero, CreateMaterial(new Color(1f, 0.6f, 0.55f)));

            // Hat: brim + tapering cylinders + pompom (tilted back a little)
            Part(PrimitiveType.Cylinder, "HatBrim", new Vector3(0f, 0.98f, 0f), new Vector3(0.37f, 0.03f, 0.37f), Vector3.zero, trim);
            hatList.Add(Part(PrimitiveType.Cylinder, "Hat1", new Vector3(0f, 1.05f, -0.01f), new Vector3(0.31f, 0.05f, 0.31f), new Vector3(-6f, 0f, 0f), _hatMat));
            hatList.Add(Part(PrimitiveType.Cylinder, "Hat2", new Vector3(0f, 1.14f, -0.03f), new Vector3(0.23f, 0.05f, 0.23f), new Vector3(-12f, 0f, 0f), _hatMat));
            hatList.Add(Part(PrimitiveType.Cylinder, "Hat3", new Vector3(0f, 1.22f, -0.06f), new Vector3(0.15f, 0.045f, 0.15f), new Vector3(-20f, 0f, 0f), _hatMat));
            hatList.Add(Part(PrimitiveType.Cylinder, "Hat4", new Vector3(0f, 1.28f, -0.10f), new Vector3(0.08f, 0.04f, 0.08f), new Vector3(-28f, 0f, 0f), _hatMat));
            Part(PrimitiveType.Sphere, "PomPom", new Vector3(0f, 1.32f, -0.14f), new Vector3(0.09f, 0.09f, 0.09f), Vector3.zero, trim);

            _tunicRenderers = tunicList.ToArray();
            _hatRenderers = hatList.ToArray();
        }

        private void BuildGadgets()
        {
            // Sack: shown when the sack has items; grows with count.
            Material sackMat = CreateMaterial(SackColor);
            Renderer sack = Part(PrimitiveType.Sphere, "SackVisual", Vector3.zero, Vector3.one * 0.32f, Vector3.zero, sackMat);
            _sackVisual = sack.transform;
            _sackVisual.SetParent(SackAnchor, false);
            _sackVisual.localPosition = new Vector3(0f, 0f, -0.05f);
            _sackVisual.gameObject.SetActive(false);

            // Jetpack: two tanks + flame, parented to the visual root (scales with crouch).
            var jp = new GameObject("JetpackVisual");
            _jetpackVisual = jp.transform;
            _jetpackVisual.SetParent(_visualRoot, false);
            _jetpackVisual.localPosition = new Vector3(0f, 0.5f, -0.24f);
            Material metal = CreateMaterial(MetalColor);
            Material flame = CreateMaterial(FlameColor);
            Renderer t1 = Part(PrimitiveType.Cylinder, "TankL", Vector3.zero, new Vector3(0.1f, 0.14f, 0.1f), Vector3.zero, metal);
            Renderer t2 = Part(PrimitiveType.Cylinder, "TankR", Vector3.zero, new Vector3(0.1f, 0.14f, 0.1f), Vector3.zero, metal);
            t1.transform.SetParent(_jetpackVisual, false); t1.transform.localPosition = new Vector3(-0.07f, 0f, 0f);
            t2.transform.SetParent(_jetpackVisual, false); t2.transform.localPosition = new Vector3(0.07f, 0f, 0f);
            Renderer f = Part(PrimitiveType.Sphere, "Flame", Vector3.zero, new Vector3(0.12f, 0.2f, 0.12f), Vector3.zero, flame);
            _flameVisual = f.transform;
            _flameVisual.SetParent(_jetpackVisual, false);
            _flameVisual.localPosition = new Vector3(0f, -0.22f, 0f);
            _flameVisual.gameObject.SetActive(false);
            _jetpackVisual.gameObject.SetActive(false);
        }

        private Renderer Part(PrimitiveType type, string partName, Vector3 localPos, Vector3 localScale, Vector3 localEuler, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col); // placeholder parts must never collide
            Transform t = go.transform;
            t.SetParent(_visualRoot, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(localEuler);
            t.localScale = localScale;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return r;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null; // keep the primitive's default material
            var m = new Material(shader) { name = "ElfPlaceholder" };
            SetColor(m, color);
            _createdMaterials.Add(m);
            return m;
        }

        private static void SetColor(Material m, Color color)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
        }

        private void ApplyTheme()
        {
            int i = Mathf.Clamp(themeIndex, 0, TunicColors.Length - 1);
            SetColor(_tunicMat, TunicColors[i]);
            SetColor(_hatMat, HatColors[i]);
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        // ---------------- per-frame cosmetics ----------------

        private void Update()
        {
            if (_visualRoot == null) return;
            if (_controller == null) _controller = GetComponent<ElfController>();
            if (_carry == null) _carry = GetComponent<ElfCarry>();

            float dt = Time.deltaTime;

            // Crouch squash
            float targetY = _controller != null && _controller.IsCrouching ? crouchVisualScaleY : 1f;
            Vector3 s = _visualRoot.localScale;
            float k = 1f - Mathf.Exp(-crouchLerpSpeed * dt);
            s.y = Mathf.Lerp(s.y, targetY, k);
            _visualRoot.localScale = s;

            // Sack blob
            if (_sackVisual != null)
            {
                int count = _carry != null ? _carry.SackCount : 0;
                bool show = count > 0;
                if (_sackVisual.gameObject.activeSelf != show) _sackVisual.gameObject.SetActive(show);
                if (show) _sackVisual.localScale = Vector3.one * (0.26f + 0.05f * Mathf.Min(count, 6));
            }

            // Jetpack
            if (_jetpackVisual != null)
            {
                bool has = _controller != null && _controller.HasJetpack;
                if (_jetpackVisual.gameObject.activeSelf != has) _jetpackVisual.gameObject.SetActive(has);
                if (has && _flameVisual != null)
                {
                    bool firing = _controller.IsJetpacking;
                    if (_flameVisual.gameObject.activeSelf != firing) _flameVisual.gameObject.SetActive(firing);
                    if (firing)
                        _flameVisual.localScale = new Vector3(0.12f, 0.16f + Random.value * 0.1f, 0.12f);
                }
            }
        }
    }
}
