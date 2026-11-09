using System.Collections.Generic;
using UnityEngine;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    public enum NPCKind { Grandpa, Cat }
    public enum NPCPose { Standing, Sitting, Lying }

    /// <summary>
    /// Visual for an NPC. Instantiates modelPrefab, or builds a primitive placeholder (grandpa / cat).
    /// Poses (lying in bed / sitting up / standing) are applied to the "Model" child; Animator is optional
    /// and only receives parameters that exist (Speed float, Sleeping bool, Alert int).
    /// The model is built in Start (or on EnsureBuilt), so Configure() can be called right after AddComponent.
    /// </summary>
    [DisallowMultipleComponent]
    public class NPCVisual : MonoBehaviour
    {
        [SerializeField] private NPCKind kind = NPCKind.Grandpa;

        [Header("Model (optional)")]
        [SerializeField] private GameObject modelPrefab;
        [Tooltip("Controller for the model's Animator (FBX models import without one). Set by the scene builder.")]
        [SerializeField] private RuntimeAnimatorController animatorController;

        public RuntimeAnimatorController AnimatorController { get => animatorController; set => animatorController = value; }
        [SerializeField] private Vector3 modelLocalOffset = Vector3.zero;
        [SerializeField] private Vector3 modelLocalEuler = Vector3.zero;
        [SerializeField] private float modelScale = 1f;
        [SerializeField] private string headTransformName = "Head";
        [SerializeField] private string tailTransformName = "Tail";
        [SerializeField] private Animator animator;

        [Header("Pose")]
        [SerializeField] private bool applyPoseTransforms = true;
        [SerializeField] private float poseLerpSpeed = 3.5f;
        [Tooltip("Grandpa lying in bed: model root offset/rotation. Pivot is at the feet; head ends up towards -Z (pillow).")]
        [SerializeField] private Vector3 lyingOffset = new Vector3(0f, 0.55f, 0.9f);
        [SerializeField] private Vector3 lyingEuler = new Vector3(-90f, 0f, 0f);
        [Tooltip("Added to lyingOffset.y: drops the standing figure so the hips rest on the mattress.")]
        [SerializeField] private Vector3 sittingOffset = new Vector3(0f, -0.8f, -0.55f);
        [SerializeField] private Vector3 sittingEuler = new Vector3(-8f, 0f, 0f);
        [SerializeField] private float walkBobAmplitude = 0.04f;
        [SerializeField] private float walkBobFrequency = 9f;

        [Header("Cat")]
        [SerializeField, Range(0.5f, 1f)] private float catSleepSquash = 0.85f;
        [SerializeField] private float tailWagSpeed = 3f;
        [SerializeField] private float tailWagAngle = 25f;

        public NPCKind Kind => kind;
        public Transform ModelRoot { get; private set; }
        public Transform Head { get; private set; }
        public Transform Tail { get; private set; }
        public Animator NpcAnimator => animator;
        public bool IsBuilt => _built;
        public bool IsPlaceholder => _placeholder;

        private bool _built;
        private bool _placeholder;
        private NPCPose _pose = NPCPose.Lying;
        private float _speed;
        private int _alert;
        private bool _sleeping = true;
        private float _time;
        private Quaternion _tailBaseRot = Quaternion.identity;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int SleepingHash = Animator.StringToHash("Sleeping");
        private static readonly int AlertHash = Animator.StringToHash("Alert");
        private bool _hasSpeed, _hasSleeping, _hasAlert;

        private static readonly Dictionary<int, Material> MaterialCache = new Dictionary<int, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => MaterialCache.Clear();

        // ---------------- public API ----------------

        public void Configure(NPCKind npcKind, GameObject prefab = null)
        {
            kind = npcKind;
            if (prefab != null) modelPrefab = prefab;
            if (_built) Rebuild();
        }

        public void EnsureBuilt()
        {
            if (!_built) Build();
        }

        public void Rebuild()
        {
            if (ModelRoot != null) Destroy(ModelRoot.gameObject);
            ModelRoot = null; Head = null; Tail = null;
            _built = false;
            Build();
        }

        public void SetPose(NPCPose pose) => _pose = pose;
        public void SetLocomotion(float speed) => _speed = speed;
        public void SetAlert(int level) => _alert = level;
        public void SetSleeping(bool sleeping) => _sleeping = sleeping;

        /// <summary>Jump to the current pose without blending (e.g. after a teleport).</summary>
        public void SnapPose()
        {
            if (ModelRoot == null || !applyPoseTransforms) return;
            GetPoseTarget(out Vector3 p, out Quaternion r, out Vector3 s);
            ModelRoot.localPosition = p;
            ModelRoot.localRotation = r;
            ModelRoot.localScale = s;
        }

        // ---------------- lifecycle ----------------

        private void Start()
        {
            EnsureBuilt();
        }

        private void LateUpdate()
        {
            if (!_built || ModelRoot == null) return;
            float dt = Time.deltaTime;
            _time += dt;

            if (applyPoseTransforms)
            {
                GetPoseTarget(out Vector3 p, out Quaternion r, out Vector3 s);

                if (_speed > 0.1f && _pose == NPCPose.Standing)
                    p.y += Mathf.Abs(Mathf.Sin(_time * walkBobFrequency * Mathf.Clamp(_speed, 0.5f, 3f) * 0.5f)) * walkBobAmplitude;

                if (kind == NPCKind.Grandpa && _pose == NPCPose.Lying && _alert == (int)AlertLevel.Stirring)
                    r *= Quaternion.Euler(0f, Mathf.Sin(_time * 3f) * 6f, 0f); // tossing and turning

                float k = 1f - Mathf.Exp(-poseLerpSpeed * dt);
                ModelRoot.localPosition = Vector3.Lerp(ModelRoot.localPosition, p, k);
                ModelRoot.localRotation = Quaternion.Slerp(ModelRoot.localRotation, r, k);

                if (kind == NPCKind.Cat && _sleeping)
                    s.y *= 1f + Mathf.Sin(_time * 2f) * 0.04f; // breathing
                ModelRoot.localScale = s;
            }

            if (_placeholder && Tail != null)
            {
                float speed = _sleeping ? 1.2f : tailWagSpeed;
                float angle = _sleeping ? tailWagAngle * 0.3f : tailWagAngle;
                Tail.localRotation = _tailBaseRot * Quaternion.Euler(0f, 0f, Mathf.Sin(_time * speed) * angle);
            }

            UpdateAnimator();
        }

        private void GetPoseTarget(out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            scale = Vector3.one;
            if (kind == NPCKind.Grandpa)
            {
                switch (_pose)
                {
                    case NPCPose.Lying: pos = lyingOffset; rot = Quaternion.Euler(lyingEuler); break;
                    case NPCPose.Sitting: pos = lyingOffset.y * Vector3.up + sittingOffset; rot = Quaternion.Euler(sittingEuler); break;
                }
            }
            else
            {
                if (_pose != NPCPose.Standing)
                {
                    pos = new Vector3(0f, -0.03f, 0f);
                    scale = new Vector3(1.08f, catSleepSquash, 1f);
                }
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (_hasSpeed) animator.SetFloat(SpeedHash, _speed);
            if (_hasSleeping) animator.SetBool(SleepingHash, _sleeping);
            if (_hasAlert) animator.SetInteger(AlertHash, _alert);
        }

        private void CacheAnimatorParams()
        {
            _hasSpeed = _hasSleeping = _hasAlert = false;
            if (animator == null || animator.runtimeAnimatorController == null) return;
            AnimatorControllerParameter[] ps = animator.parameters; // one-time allocation
            for (int i = 0; i < ps.Length; i++)
            {
                AnimatorControllerParameter p = ps[i];
                if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float) _hasSpeed = true;
                else if (p.nameHash == SleepingHash && p.type == AnimatorControllerParameterType.Bool) _hasSleeping = true;
                else if (p.nameHash == AlertHash && p.type == AnimatorControllerParameterType.Int) _hasAlert = true;
            }
        }

        // ---------------- building ----------------

        private void Build()
        {
#if UNITY_EDITOR
            if (modelPrefab == null && kind == NPCKind.Cat)
                modelPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/Cat/Cat.fbx");
#endif
            Transform old = transform.Find("Model");
            if (old != null && old != ModelRoot) Destroy(old.gameObject);

            var rootGo = new GameObject("Model");
            rootGo.layer = gameObject.layer;
            ModelRoot = rootGo.transform;
            ModelRoot.SetParent(transform, false);

            if (modelPrefab != null)
            {
                _placeholder = false;
                GameObject inst = Instantiate(modelPrefab, ModelRoot);
                inst.transform.localPosition = modelLocalOffset;
                inst.transform.localRotation = Quaternion.Euler(modelLocalEuler);
                inst.transform.localScale = Vector3.one * modelScale;
                StealthUtil.SetLayerRecursive(inst, gameObject.layer);
                Head = FindDeep(inst.transform, headTransformName);
                Tail = FindDeep(inst.transform, tailTransformName);
                if (animator == null) animator = inst.GetComponentInChildren<Animator>();
                if (animator != null && animator.runtimeAnimatorController == null && animatorController != null)
                    animator.runtimeAnimatorController = animatorController;
#if UNITY_EDITOR
                if (animator != null && animator.runtimeAnimatorController == null && kind == NPCKind.Cat)
                    animator.runtimeAnimatorController = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Characters/Cat/Cat.controller");
#endif
            }
            else
            {
                _placeholder = true;
                if (kind == NPCKind.Grandpa) BuildGrandpa(ModelRoot);
                else BuildCat(ModelRoot);
            }

            if (Head == null)
            {
                var h = new GameObject("Head").transform;
                h.SetParent(ModelRoot, false);
                h.localPosition = kind == NPCKind.Grandpa ? new Vector3(0f, 1.75f, 0.05f) : new Vector3(0f, 0.36f, 0.36f);
                Head = h;
            }
            if (Tail != null) _tailBaseRot = Tail.localRotation;

            if (animator == null) animator = GetComponentInChildren<Animator>();
            CacheAnimatorParams();

            _built = true;
            SnapPose();
        }

        private void BuildGrandpa(Transform root)
        {
            const int pajama = 0x2f4f86, pants = 0x3b5f9c, robe = 0x7c1d26, robeTrim = 0xd4a038,
                      skin = 0xf0b998, beard = 0xf5f2eb, capRed = 0xc13a3a, capWhite = 0xfdfaf2,
                      dark = 0x1b1b22, slipper = 0x9c6236, brass = 0xe8b931;

            // Legs + warm reindeer slippers
            Part(PrimitiveType.Capsule, "LegL", root, new Vector3(-0.13f, 0.42f, 0f), new Vector3(0.18f, 0.42f, 0.18f), pants);
            Part(PrimitiveType.Capsule, "LegR", root, new Vector3(0.13f, 0.42f, 0f), new Vector3(0.18f, 0.42f, 0.18f), pants);
            Part(PrimitiveType.Sphere, "SlipperL", root, new Vector3(-0.13f, 0.06f, 0.06f), new Vector3(0.21f, 0.13f, 0.32f), slipper);
            Part(PrimitiveType.Sphere, "SlipperR", root, new Vector3(0.13f, 0.06f, 0.06f), new Vector3(0.21f, 0.13f, 0.32f), slipper);
            Part(PrimitiveType.Sphere, "SlipperNoseL", root, new Vector3(-0.13f, 0.08f, 0.22f), new Vector3(0.06f, 0.06f, 0.06f), capRed);
            Part(PrimitiveType.Sphere, "SlipperNoseR", root, new Vector3(0.13f, 0.08f, 0.22f), new Vector3(0.06f, 0.06f, 0.06f), capRed);

            // Cozy burgundy velvet dressing gown over flannel pajamas + golden belt + shawl collar
            Part(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 1.15f, 0f), new Vector3(0.64f, 0.52f, 0.56f), robe);
            Part(PrimitiveType.Sphere, "Belly", root, new Vector3(0f, 1.05f, 0.08f), new Vector3(0.64f, 0.60f, 0.56f), robe);
            Part(PrimitiveType.Cube, "PajamaFront", root, new Vector3(0f, 1.18f, 0.24f), new Vector3(0.18f, 0.58f, 0.12f), pajama);
            Part(PrimitiveType.Cylinder, "RobeBelt", root, new Vector3(0f, 0.96f, 0.04f), new Vector3(0.66f, 0.035f, 0.60f), robeTrim);
            Part(PrimitiveType.Capsule, "ShawlCollarL", root, new Vector3(-0.11f, 1.42f, 0.18f), new Vector3(0.10f, 0.18f, 0.10f), robeTrim, new Vector3(0f, 0f, -22f));
            Part(PrimitiveType.Capsule, "ShawlCollarR", root, new Vector3(0.11f, 1.42f, 0.18f), new Vector3(0.10f, 0.18f, 0.10f), robeTrim, new Vector3(0f, 0f, 22f));
            Part(PrimitiveType.Capsule, "ArmL", root, new Vector3(-0.37f, 1.2f, 0f), new Vector3(0.15f, 0.35f, 0.15f), robe, new Vector3(0f, 0f, -10f));
            Part(PrimitiveType.Capsule, "ArmR", root, new Vector3(0.37f, 1.2f, 0f), new Vector3(0.15f, 0.35f, 0.15f), robe, new Vector3(0f, 0f, 10f));
            Part(PrimitiveType.Sphere, "HandL", root, new Vector3(-0.42f, 0.86f, 0.02f), new Vector3(0.12f, 0.12f, 0.12f), skin);
            Part(PrimitiveType.Sphere, "HandR", root, new Vector3(0.42f, 0.86f, 0.02f), new Vector3(0.12f, 0.12f, 0.12f), skin);

            // Head pivot
            var head = new GameObject("Head").transform;
            head.gameObject.layer = gameObject.layer;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 1.78f, 0.02f);
            Head = head;

            Part(PrimitiveType.Sphere, "Skull", head, Vector3.zero, new Vector3(0.42f, 0.41f, 0.4f), skin);
            Part(PrimitiveType.Sphere, "Nose", head, new Vector3(0f, -0.02f, 0.21f), new Vector3(0.11f, 0.1f, 0.11f), skin);
            Part(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.08f, 0.06f, 0.17f), new Vector3(0.05f, 0.04f, 0.03f), dark);
            Part(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.08f, 0.06f, 0.17f), new Vector3(0.05f, 0.04f, 0.03f), dark);
            // Brass reading spectacles
            Part(PrimitiveType.Cylinder, "GlassesL", head, new Vector3(-0.08f, 0.05f, 0.19f), new Vector3(0.09f, 0.008f, 0.09f), brass, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, "GlassesR", head, new Vector3(0.08f, 0.05f, 0.19f), new Vector3(0.09f, 0.008f, 0.09f), brass, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, "GlassesBridge", head, new Vector3(0f, 0.05f, 0.195f), new Vector3(0.06f, 0.015f, 0.015f), brass);
            Part(PrimitiveType.Capsule, "BrowL", head, new Vector3(-0.09f, 0.12f, 0.17f), new Vector3(0.035f, 0.055f, 0.035f), beard, new Vector3(0f, 0f, 70f));
            Part(PrimitiveType.Capsule, "BrowR", head, new Vector3(0.09f, 0.12f, 0.17f), new Vector3(0.035f, 0.055f, 0.035f), beard, new Vector3(0f, 0f, -70f));
            Part(PrimitiveType.Capsule, "MustacheL", head, new Vector3(-0.06f, -0.07f, 0.19f), new Vector3(0.05f, 0.08f, 0.05f), beard, new Vector3(0f, 0f, 78f));
            Part(PrimitiveType.Capsule, "MustacheR", head, new Vector3(0.06f, -0.07f, 0.19f), new Vector3(0.05f, 0.08f, 0.05f), beard, new Vector3(0f, 0f, -78f));
            Part(PrimitiveType.Sphere, "Beard", head, new Vector3(0f, -0.17f, 0.1f), new Vector3(0.35f, 0.27f, 0.27f), beard);

            // Nightcap: white brim, red bent cone (capsules), white pompom
            Part(PrimitiveType.Cylinder, "CapBrim", head, new Vector3(0f, 0.16f, -0.02f), new Vector3(0.44f, 0.04f, 0.44f), capWhite, new Vector3(-6f, 0f, 0f));
            Part(PrimitiveType.Capsule, "CapLow", head, new Vector3(0f, 0.3f, -0.07f), new Vector3(0.3f, 0.18f, 0.3f), capRed, new Vector3(-20f, 0f, 0f));
            Part(PrimitiveType.Capsule, "CapTip", head, new Vector3(0f, 0.46f, -0.2f), new Vector3(0.14f, 0.14f, 0.14f), capRed, new Vector3(-50f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Pompom", head, new Vector3(0f, 0.53f, -0.32f), new Vector3(0.13f, 0.13f, 0.13f), capWhite);
        }

        private void BuildCat(Transform root)
        {
            const int orange = 0xf08a3c, stripe = 0xc4581c, cream = 0xfff1dc, pink = 0xf4a0a8,
                      eye = 0x9be15d, red = 0xc23030, gold = 0xfacc15;

            // Squashed fat body, belly, back stripes, paws
            Part(PrimitiveType.Sphere, "Body", root, new Vector3(0f, 0.22f, 0f), new Vector3(0.5f, 0.4f, 0.7f), orange);
            Part(PrimitiveType.Sphere, "Belly", root, new Vector3(0f, 0.15f, 0.06f), new Vector3(0.4f, 0.28f, 0.5f), cream);
            for (int i = -1; i <= 1; i++)
                Part(PrimitiveType.Capsule, "Stripe", root, new Vector3(0f, 0.415f, i * 0.12f), new Vector3(0.035f, 0.1f, 0.035f), stripe, new Vector3(0f, 0f, 90f));
            Part(PrimitiveType.Sphere, "PawFL", root, new Vector3(-0.13f, 0.04f, 0.2f), new Vector3(0.12f, 0.08f, 0.14f), cream);
            Part(PrimitiveType.Sphere, "PawFR", root, new Vector3(0.13f, 0.04f, 0.2f), new Vector3(0.12f, 0.08f, 0.14f), cream);
            Part(PrimitiveType.Sphere, "PawBL", root, new Vector3(-0.15f, 0.04f, -0.2f), new Vector3(0.12f, 0.08f, 0.14f), cream);
            Part(PrimitiveType.Sphere, "PawBR", root, new Vector3(0.15f, 0.04f, -0.2f), new Vector3(0.12f, 0.08f, 0.14f), cream);

            // Collar + bell
            Part(PrimitiveType.Cylinder, "Collar", root, new Vector3(0f, 0.28f, 0.26f), new Vector3(0.3f, 0.015f, 0.3f), red, new Vector3(70f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Bell", root, new Vector3(0f, 0.17f, 0.32f), new Vector3(0.05f, 0.05f, 0.05f), gold);

            // Head pivot
            var head = new GameObject("Head").transform;
            head.gameObject.layer = gameObject.layer;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 0.36f, 0.36f);
            Head = head;

            Part(PrimitiveType.Sphere, "Skull", head, Vector3.zero, new Vector3(0.36f, 0.32f, 0.32f), orange);
            Part(PrimitiveType.Sphere, "Muzzle", head, new Vector3(0f, -0.05f, 0.12f), new Vector3(0.18f, 0.12f, 0.12f), cream);
            Part(PrimitiveType.Cube, "EarL", head, new Vector3(-0.1f, 0.14f, 0f), new Vector3(0.09f, 0.09f, 0.03f), orange, new Vector3(0f, 0f, 45f));
            Part(PrimitiveType.Cube, "EarR", head, new Vector3(0.1f, 0.14f, 0f), new Vector3(0.09f, 0.09f, 0.03f), orange, new Vector3(0f, 0f, 45f));
            Part(PrimitiveType.Cube, "EarInL", head, new Vector3(-0.1f, 0.135f, 0.012f), new Vector3(0.05f, 0.05f, 0.02f), pink, new Vector3(0f, 0f, 45f));
            Part(PrimitiveType.Cube, "EarInR", head, new Vector3(0.1f, 0.135f, 0.012f), new Vector3(0.05f, 0.05f, 0.02f), pink, new Vector3(0f, 0f, 45f));
            Part(PrimitiveType.Sphere, "EyeL", head, new Vector3(-0.07f, 0.03f, 0.14f), new Vector3(0.06f, 0.06f, 0.04f), eye);
            Part(PrimitiveType.Sphere, "EyeR", head, new Vector3(0.07f, 0.03f, 0.14f), new Vector3(0.06f, 0.06f, 0.04f), eye);
            Part(PrimitiveType.Sphere, "Nose", head, new Vector3(0f, -0.02f, 0.18f), new Vector3(0.035f, 0.025f, 0.03f), pink);

            // Tail pivot at the back; capsule tilted backwards, wagged in LateUpdate
            var tail = new GameObject("Tail").transform;
            tail.gameObject.layer = gameObject.layer;
            tail.SetParent(root, false);
            tail.localPosition = new Vector3(0f, 0.25f, -0.33f);
            tail.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            Tail = tail;
            Part(PrimitiveType.Capsule, "TailMesh", tail, new Vector3(0f, 0.18f, 0f), new Vector3(0.08f, 0.2f, 0.08f), orange);
            Part(PrimitiveType.Sphere, "TailTip", tail, new Vector3(0f, 0.37f, 0f), new Vector3(0.09f, 0.09f, 0.09f), cream);
        }

        private GameObject Part(PrimitiveType type, string partName, Transform parent, Vector3 localPos, Vector3 localScale, int hexColor, Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            go.layer = gameObject.layer;
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col); // immediate (allowed at runtime) so it never blocks vision/physics for a frame
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = localScale;
            MeshRenderer r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                Material m = GetMaterial(hexColor, r.sharedMaterial);
                if (m != null) r.sharedMaterial = m;
            }
            return go;
        }

        private static Material GetMaterial(int hex, Material fallbackTemplate)
        {
            if (MaterialCache.TryGetValue(hex, out Material cached) && cached != null) return cached;

            Color c = new Color(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f, 1f);
            // VERIFY: in player builds Shader.Find only works if the shader is included (URP Lit usually is via
            // other materials; otherwise add it to Graphics settings ▸ Always Included Shaders).
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) sh = Shader.Find("Standard");

            Material m;
            if (sh != null) m = new Material(sh);
            else if (fallbackTemplate != null) m = new Material(fallbackTemplate);
            else return null;

            m.name = "NPC_Placeholder_" + hex.ToString("X6");
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            MaterialCache[hex] = m;
            return m;
        }

        private static Transform FindDeep(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;
            if (root.name == childName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform r = FindDeep(root.GetChild(i), childName);
                if (r != null) return r;
            }
            return null;
        }
    }
}
