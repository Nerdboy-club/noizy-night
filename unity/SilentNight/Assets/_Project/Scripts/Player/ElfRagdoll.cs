using SilentNight.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SilentNight.Player
{
    /// <summary>
    /// Active-ragdoll "puppet" of the elf (port of /playground ActiveRagdollSystem).
    /// The CharacterController root (ElfController) stays the gameplay authority (stamina, noise, surfaces, jetpack,
    /// carrying). This component builds an 11-part physical body from primitives (Rigidbody + ConfigurableJoint with
    /// slerp drives), holds the pelvis at ride height with a spring (like PEAK / Human Fall Flat), animates limbs by
    /// driving joint targets (procedural gait), and goes limp after a hard landing or on [G], then gets back up.
    /// The old placeholder body of ElfVisual is hidden; gadgets (sack, jetpack) stay on the root.
    /// </summary>
    [DefaultExecutionOrder(60)]
    [DisallowMultipleComponent]
    public class ElfRagdoll : MonoBehaviour
    {
        [Header("Visual")]
        [Tooltip("Skinned elf model (FBX with Mixamo-named bones). Null = primitive body; in the editor the default Tripo elf is auto-loaded.")]
        [SerializeField] private GameObject modelPrefab;

        [Header("Pelvis spring (ride height)")]
        [SerializeField] private float rideHeight = 0.40f;
        [SerializeField] private float crouchDrop = 0.14f;
        [SerializeField] private float positionSpring = 90f;
        [SerializeField] private float positionDamper = 14f;
        [SerializeField] private float uprightSpring = 150f;
        [SerializeField] private float uprightDamper = 24f;
        [SerializeField] private float leanAtSpeedDegrees = 12f;

        [Header("Joints")]
        [SerializeField] private float jointSpring = 700f;
        [SerializeField] private float jointDamper = 45f;
        [SerializeField] private float jointMaxForce = 1000000f;
        [SerializeField] private float massScale = 1f;

        [Header("Gait")]
        private float strideRadPerMeter = 8f; // not serialized: scenes built earlier must not keep old values
        private float maxSwingDegrees = 55f;
        private float mantleScrambleRate = 24f; // rad/s of the leg/arm scramble while clambering onto a ledge

        [Header("Falling")]
        [Tooltip("Landing speed (m/s) above which the elf goes limp.")]
        [SerializeField] private float limpLandingSpeed = 9f;
        [SerializeField] private float limpDuration = 1.4f;
        [SerializeField] private float getUpDuration = 0.9f;
        [SerializeField] private Key ragdollDebugKey = Key.G;

        [Header("Co-op: downed")]
        [Tooltip("Co-op only (more than one player): how long a fallen elf stays down so friends can help it up.")]
        [SerializeField] private float downedDuration = 4f;
        [Tooltip("Upward velocity (m/s) of the little hop when a friend helps the elf up.")]
        [SerializeField] private float helpHopSpeed = 2.2f;

        private enum Mode { Active, Limp, GettingUp }

        private sealed class Limb
        {
            public string Name;
            public Transform T;
            public Rigidbody Rb;
            public ConfigurableJoint Joint;
            public Limb Parent;
            public float Target; // desired swing about the X axis (deg), set by Drive()
            public float Visual; // smoothed value used for the skinned model
            public Vector3 StartLocalPos;
        }

        private ElfController _controller;
        private ElfCarry _carry;
        private ElfVisual _visual;
        private CharacterController _cc;

        private Transform _puppetRoot;
        private Limb _pelvis, _torso, _head, _uArmL, _lArmL, _uArmR, _lArmR, _uLegL, _lLegL, _uLegR, _lLegR;
        private readonly System.Collections.Generic.List<Limb> _limbs = new System.Collections.Generic.List<Limb>();
        private readonly System.Collections.Generic.List<Material> _mats = new System.Collections.Generic.List<Material>();
        private float _totalMass;

        private Mode _mode = Mode.Active;
        private float _modeTimer;
        private float _phase;
        private float _strength = 1f; // 0..1 drive/spring multiplier
        private bool _built;
        private bool _wantLimp;

        public GameObject ModelPrefab { get => modelPrefab; set => modelPrefab = value; }
        public const string DefaultModelPath = "Assets/_Project/Art/Characters/Elf/Elf_Tripo.fbx";

        private bool _hasPendingPush;
        private Vector3 _pendingPush;

        /// <summary>Knock the elf over (debug key, tests, hazards). Safe to call repeatedly: ignored while already limp,
        /// several calls in one frame collapse into one fall. Works during get-up too (knocked down again).</summary>
        public void GoLimp() => RequestLimp(Vector3.zero, false);

        /// <summary>Same as GoLimp() but throws the pelvis with this velocity change (m/s) instead of the default
        /// "along the movement" push.</summary>
        public void GoLimp(Vector3 push) => RequestLimp(push, true);

        private void RequestLimp(Vector3 push, bool custom)
        {
            if (_mode == Mode.Limp) return;
            _wantLimp = true;
            if (custom || !_hasPendingPush)
            {
                _pendingPush = push;
                _hasPendingPush = custom;
            }
        }

        public bool IsLimp => _mode != Mode.Active;
        /// <summary>Lying on the ground (limp phase only, not while getting up).</summary>
        public bool IsKnockedDown => _mode == Mode.Limp;
        /// <summary>Limp in co-op mode: stays down for downedDuration and can be helped up by a friend (ElfHelpUp).</summary>
        public bool IsDowned => _mode == Mode.Limp && _downed;
        public Transform PelvisTransform => _pelvis != null ? _pelvis.T : null;

        private bool _downed;
        private readonly System.Collections.Generic.List<Collider> _ignoreWithBody = new System.Collections.Generic.List<Collider>();

        /// <summary>
        /// A friend helps the elf up: skips the rest of the limp/downed time and jumps straight into the get-up phase
        /// with a little upward hop. Returns false (and does nothing) unless the elf is lying limp.
        /// </summary>
        public bool HelpUp()
        {
            if (!_built || _mode != Mode.Limp) return false;
            EnterGetUp();
            _downed = false;
            _wantLimp = false;
            _hasPendingPush = false;
            Vector3 hop = Vector3.up * helpHopSpeed;
            foreach (Limb l in _limbs) l.Rb.linearVelocity = hop;
            if (_pelvis != null) SilentNight.FX.LandingDust.Spawn(_pelvis.T.position, 0.5f);
            return true;
        }

        /// <summary>Make the puppet's body never collide with <paramref name="c"/> (e.g. the help-up trigger).
        /// Call again after re-enabling the collider: Unity resets IgnoreCollision when a collider is disabled.</summary>
        public void IgnoreOwnBody(Collider c)
        {
            if (c == null) return;
            if (!_ignoreWithBody.Contains(c)) _ignoreWithBody.Add(c);
            if (!_built) return; // applied at the end of Build()
            for (int i = 0; i < _limbs.Count; i++)
            {
                Collider a = _limbs[i].T.GetComponent<Collider>();
                if (a != null) Physics.IgnoreCollision(a, c, true);
            }
            if (_cc != null && _cc != c) Physics.IgnoreCollision(_cc, c, true);
        }

        // ---------------------------------------------------------------- lifecycle

        private void Start()
        {
            _controller = GetComponent<ElfController>();
            _carry = GetComponent<ElfCarry>();
            _visual = GetComponent<ElfVisual>();
            _cc = GetComponent<CharacterController>();
            Build();
            // Co-op "help a fallen friend" target (PlayerSetup adds it; scenes baked earlier get it here).
            if (GetComponent<ElfHelpUp>() == null) gameObject.AddComponent<ElfHelpUp>();
            if (_controller != null)
            {
                _controller.Landed += OnLanded;
                _controller.Teleported += SnapToRoot;
                _controller.Bumped += OnBumped;
            }
        }

        private void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.Landed -= OnLanded;
                _controller.Teleported -= SnapToRoot;
                _controller.Bumped -= OnBumped;
            }
            if (_puppetRoot != null) Destroy(_puppetRoot.gameObject);
            for (int i = 0; i < _mats.Count; i++) if (_mats[i] != null) Destroy(_mats[i]);
        }

        private void OnEnable()
        {
            if (_puppetRoot != null) _puppetRoot.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (_puppetRoot != null) _puppetRoot.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- build

        private static readonly Color[] Tunic =
        {
            new Color(0.15f, 0.62f, 0.25f), new Color(0.80f, 0.16f, 0.16f),
            new Color(0.22f, 0.42f, 0.86f), new Color(0.56f, 0.26f, 0.76f)
        };
        private static readonly Color[] Hat =
        {
            new Color(0.85f, 0.12f, 0.14f), new Color(0.12f, 0.60f, 0.24f),
            new Color(0.95f, 0.80f, 0.20f), new Color(0.15f, 0.70f, 0.68f)
        };

        private Material Mat(Color c)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (s == null) s = Shader.Find("Standard");
            if (s == null) return null;
            var m = new Material(s) { name = "ElfRagdoll" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            _mats.Add(m);
            return m;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            int theme = _visual != null ? Mathf.Clamp(_visual.ThemeIndex, 0, 3) : 0;
            Material tunic = Mat(Tunic[theme]);
            Material hat = Mat(Hat[theme]);
            Material skin = Mat(new Color(1f, 0.84f, 0.72f));
            Material dark = Mat(new Color(0.14f, 0.1f, 0.1f));
            Material trim = Mat(new Color(0.96f, 0.96f, 0.96f));

            var go = new GameObject("ElfRagdoll");
            _puppetRoot = go.transform;
            Vector3 basePos = transform.position;
            Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            _puppetRoot.SetPositionAndRotation(basePos, yaw);

            // Local layout (feet at y = 0, facing +Z, character's left = -X).
            _pelvis = MakeLimb("Pelvis", null, new Vector3(0f, 0.40f, 0f), Vector3.zero, 0.15f, 0.2f, 5f, tunic, PrimitiveType.Sphere, new Vector3(0.30f, 0.24f, 0.26f));
            _torso = MakeLimb("Torso", _pelvis, new Vector3(0f, 0.60f, 0f), new Vector3(0f, 0.48f, 0f), 0.15f, 0.30f, 7f, tunic, PrimitiveType.Capsule, new Vector3(0.34f, 0.17f, 0.28f));
            _head = MakeLimb("Head", _torso, new Vector3(0f, 0.88f, 0f), new Vector3(0f, 0.76f, 0f), 0.18f, 0.0f, 3f, skin, PrimitiveType.Sphere, new Vector3(0.38f, 0.36f, 0.36f));

            _uArmL = MakeLimb("UArmL", _torso, new Vector3(-0.22f, 0.62f, 0f), new Vector3(-0.22f, 0.72f, 0f), 0.055f, 0.20f, 1.2f, tunic, PrimitiveType.Capsule, new Vector3(0.11f, 0.11f, 0.11f));
            _lArmL = MakeLimb("LArmL", _uArmL, new Vector3(-0.22f, 0.44f, 0f), new Vector3(-0.22f, 0.53f, 0f), 0.05f, 0.18f, 0.8f, skin, PrimitiveType.Capsule, new Vector3(0.10f, 0.09f, 0.10f));
            _uArmR = MakeLimb("UArmR", _torso, new Vector3(0.22f, 0.62f, 0f), new Vector3(0.22f, 0.72f, 0f), 0.055f, 0.20f, 1.2f, tunic, PrimitiveType.Capsule, new Vector3(0.11f, 0.11f, 0.11f));
            _lArmR = MakeLimb("LArmR", _uArmR, new Vector3(0.22f, 0.44f, 0f), new Vector3(0.22f, 0.53f, 0f), 0.05f, 0.18f, 0.8f, skin, PrimitiveType.Capsule, new Vector3(0.10f, 0.09f, 0.10f));

            _uLegL = MakeLimb("ULegL", _pelvis, new Vector3(-0.09f, 0.27f, 0f), new Vector3(-0.09f, 0.36f, 0f), 0.065f, 0.18f, 2.5f, dark, PrimitiveType.Capsule, new Vector3(0.13f, 0.1f, 0.13f));
            _lLegL = MakeLimb("LLegL", _uLegL, new Vector3(-0.09f, 0.1f, 0f), new Vector3(-0.09f, 0.18f, 0f), 0.06f, 0.18f, 1.8f, dark, PrimitiveType.Capsule, new Vector3(0.12f, 0.1f, 0.12f));
            _uLegR = MakeLimb("ULegR", _pelvis, new Vector3(0.09f, 0.27f, 0f), new Vector3(0.09f, 0.36f, 0f), 0.065f, 0.18f, 2.5f, dark, PrimitiveType.Capsule, new Vector3(0.13f, 0.1f, 0.13f));
            _lLegR = MakeLimb("LLegR", _uLegR, new Vector3(0.09f, 0.1f, 0f), new Vector3(0.09f, 0.18f, 0f), 0.06f, 0.18f, 1.8f, dark, PrimitiveType.Capsule, new Vector3(0.12f, 0.1f, 0.12f));

            // Cosmetic extras on the head / feet / hands (no colliders).
            AddCosmetic(_head.T, PrimitiveType.Sphere, new Vector3(-0.19f, 0.01f, 0f), new Vector3(0.07f, 0.15f, 0.05f), new Vector3(0, 0, 35), skin);
            AddCosmetic(_head.T, PrimitiveType.Sphere, new Vector3(0.19f, 0.01f, 0f), new Vector3(0.07f, 0.15f, 0.05f), new Vector3(0, 0, -35), skin);
            AddCosmetic(_head.T, PrimitiveType.Sphere, new Vector3(-0.07f, 0.03f, 0.165f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, dark);
            AddCosmetic(_head.T, PrimitiveType.Sphere, new Vector3(0.07f, 0.03f, 0.165f), new Vector3(0.05f, 0.06f, 0.03f), Vector3.zero, dark);
            AddCosmetic(_head.T, PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3(0.37f, 0.03f, 0.37f), Vector3.zero, trim);
            AddCosmetic(_head.T, PrimitiveType.Cylinder, new Vector3(0f, 0.19f, -0.01f), new Vector3(0.30f, 0.05f, 0.30f), new Vector3(-6, 0, 0), hat);
            AddCosmetic(_head.T, PrimitiveType.Cylinder, new Vector3(0f, 0.28f, -0.03f), new Vector3(0.22f, 0.05f, 0.22f), new Vector3(-12, 0, 0), hat);
            AddCosmetic(_head.T, PrimitiveType.Cylinder, new Vector3(0f, 0.36f, -0.06f), new Vector3(0.14f, 0.045f, 0.14f), new Vector3(-20, 0, 0), hat);
            AddCosmetic(_head.T, PrimitiveType.Sphere, new Vector3(0f, 0.44f, -0.11f), new Vector3(0.09f, 0.09f, 0.09f), Vector3.zero, trim);
            AddCosmetic(_torso.T, PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3(0.28f, 0.025f, 0.26f), Vector3.zero, trim);
            AddCosmetic(_lLegL.T, PrimitiveType.Sphere, new Vector3(0f, -0.06f, 0.04f), new Vector3(0.14f, 0.1f, 0.2f), Vector3.zero, Mat(new Color(0.38f, 0.22f, 0.12f)));
            AddCosmetic(_lLegR.T, PrimitiveType.Sphere, new Vector3(0f, -0.06f, 0.04f), new Vector3(0.14f, 0.1f, 0.2f), Vector3.zero, Mat(new Color(0.38f, 0.22f, 0.12f)));
            AddCosmetic(_lArmL.T, PrimitiveType.Sphere, new Vector3(0f, -0.1f, 0f), new Vector3(0.11f, 0.11f, 0.11f), Vector3.zero, Mat(new Color(0.8f, 0.1f, 0.12f)));
            AddCosmetic(_lArmR.T, PrimitiveType.Sphere, new Vector3(0f, -0.1f, 0f), new Vector3(0.11f, 0.11f, 0.11f), Vector3.zero, Mat(new Color(0.8f, 0.1f, 0.12f)));

            // Joints (limits in degrees; for hanging limbs +X swings BACK, -X FORWARD; for spine/head +X leans forward).
            AddJoint(_torso, new Vector3(0f, 0.48f, 0f), -25f, 40f, 30f, 20f);
            AddJoint(_head, new Vector3(0f, 0.76f, 0f), -40f, 40f, 40f, 30f);
            AddJoint(_uArmL, new Vector3(-0.22f, 0.72f, 0f), -170f, 60f, 60f, 85f);
            AddJoint(_lArmL, new Vector3(-0.22f, 0.53f, 0f), -140f, 5f, 8f, 8f);
            AddJoint(_uArmR, new Vector3(0.22f, 0.72f, 0f), -170f, 60f, 60f, 85f);
            AddJoint(_lArmR, new Vector3(0.22f, 0.53f, 0f), -140f, 5f, 8f, 8f);
            AddJoint(_uLegL, new Vector3(-0.09f, 0.36f, 0f), -100f, 40f, 15f, 20f);
            AddJoint(_lLegL, new Vector3(-0.09f, 0.18f, 0f), -5f, 130f, 5f, 5f);
            AddJoint(_uLegR, new Vector3(0.09f, 0.36f, 0f), -100f, 40f, 15f, 20f);
            AddJoint(_lLegR, new Vector3(0.09f, 0.18f, 0f), -5f, 130f, 5f, 5f);

            // No self collisions; never collide with the CharacterController capsule.
            for (int i = 0; i < _limbs.Count; i++)
            {
                Collider a = _limbs[i].T.GetComponent<Collider>();
                for (int j = i + 1; j < _limbs.Count; j++)
                    Physics.IgnoreCollision(a, _limbs[j].T.GetComponent<Collider>(), true);
                if (_cc != null) Physics.IgnoreCollision(a, _cc, true);
                for (int k = 0; k < _ignoreWithBody.Count; k++)
                    if (_ignoreWithBody[k] != null) Physics.IgnoreCollision(a, _ignoreWithBody[k], true);
            }

            _totalMass = 0f;
            foreach (Limb l in _limbs) _totalMass += l.Rb.mass;

            if (_visual != null) _visual.SetBodyVisible(false);
            BuildModel();
        }

        // ---------------------------------------------------------------- skinned model follows the ragdoll

        private enum Kind { Physical, UpLegL, LegL, UpLegR, LegR, ArmL, ForeArmL, ArmR, ForeArmR }

        private sealed class BoneLink
        {
            public Transform Bone;
            public Limb Limb;
            public Quaternion RestBone;   // bone world rotation at rest
            public Quaternion RestLimb;   // limb world rotation at rest
            public Kind Kind;
        }

        private readonly System.Collections.Generic.List<BoneLink> _links = new System.Collections.Generic.List<BoneLink>();
        private Transform _hipsBone;
        private Vector3 _hipsRestOffsetLocal; // hips position relative to the pelvis, in pelvis rest frame
        private Quaternion _pelvisRest;
        private Quaternion _yaw0;
        private GameObject _model;

        private void BuildModel()
        {
            GameObject prefab = modelPrefab;
#if UNITY_EDITOR
            if (prefab == null) prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DefaultModelPath);
#endif
            if (prefab == null) return;

            _model = Instantiate(prefab, _puppetRoot.position, _puppetRoot.rotation, _puppetRoot);
            _model.name = "ElfModel";
            _model.transform.localScale = Vector3.one * PlayerSetup.ElfScale;
            foreach (Animator a in _model.GetComponentsInChildren<Animator>(true)) Destroy(a);
            foreach (Collider c in _model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            SetLayerRecursive(_model.transform, gameObject.layer);
            foreach (SkinnedMeshRenderer smr in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                FixMaterials(smr);
            }

            var bones = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (Transform t in _model.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                int i = n.LastIndexOf(':');
                if (i >= 0) n = n.Substring(i + 1);
                if (!bones.ContainsKey(n)) bones[n] = t;
            }

            if (!bones.TryGetValue("Hips", out _hipsBone))
            {
                Debug.LogWarning("[ElfRagdoll] Model has no Hips bone, keeping the primitive body.");
                Destroy(_model);
                _model = null;
                return;
            }

            _pelvisRest = _pelvis.T.rotation;
            _yaw0 = _puppetRoot.rotation;
            _hipsRestOffsetLocal = Quaternion.Inverse(_pelvisRest) * (_hipsBone.position - _pelvis.T.position);

            Link(bones, "Hips", _pelvis);
            foreach (string n in new[] { "Spine", "Spine1", "Spine2", "Neck", "LeftShoulder", "RightShoulder" }) Link(bones, n, _torso);
            Link(bones, "Head", _head);
            Link(bones, "LeftArm", _uArmL, Kind.ArmL); Link(bones, "LeftForeArm", _lArmL, Kind.ForeArmL); Link(bones, "LeftHand", _lArmL, Kind.ForeArmL);
            Link(bones, "RightArm", _uArmR, Kind.ArmR); Link(bones, "RightForeArm", _lArmR, Kind.ForeArmR); Link(bones, "RightHand", _lArmR, Kind.ForeArmR);
            Link(bones, "LeftUpLeg", _uLegL, Kind.UpLegL); Link(bones, "LeftLeg", _lLegL, Kind.LegL); Link(bones, "LeftFoot", _lLegL, Kind.LegL); Link(bones, "LeftToeBase", _lLegL, Kind.LegL);
            Link(bones, "RightUpLeg", _uLegR, Kind.UpLegR); Link(bones, "RightLeg", _lLegR, Kind.LegR); Link(bones, "RightFoot", _lLegR, Kind.LegR); Link(bones, "RightToeBase", _lLegR, Kind.LegR);

            // Hide the primitive body (colliders stay).
            foreach (Limb l in _limbs)
                foreach (Renderer r in l.T.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        private void Link(System.Collections.Generic.Dictionary<string, Transform> bones, string boneName, Limb limb, Kind kind = Kind.Physical)
        {
            if (!bones.TryGetValue(boneName, out Transform b)) return;
            _links.Add(new BoneLink { Bone = b, Limb = limb, RestBone = b.rotation, RestLimb = limb.T.rotation, Kind = kind });
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }

        /// <summary>FBX materials import as Standard (pink in URP): rebuild them as URP/Lit keeping the textures.</summary>
        private void FixMaterials(Renderer r)
        {
            Material[] mats = r.sharedMaterials;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;
            for (int i = 0; i < mats.Length; i++)
            {
                Material old = mats[i];
                if (old == null) continue;
                if (old.shader != null && old.shader.name.StartsWith("Universal Render Pipeline")) continue;
                var m = new Material(lit) { name = old.name + "_URP" };
                Texture main = old.HasProperty("_MainTex") ? old.GetTexture("_MainTex") : null;
                if (main != null) m.SetTexture("_BaseMap", main);
                Color c = old.HasProperty("_Color") ? old.GetColor("_Color") : Color.white;
                m.SetColor("_BaseColor", c);
                if (old.HasProperty("_BumpMap") && old.GetTexture("_BumpMap") != null)
                {
                    m.SetTexture("_BumpMap", old.GetTexture("_BumpMap"));
                    m.EnableKeyword("_NORMALMAP");
                }
                m.SetFloat("_Smoothness", 0.15f);
                m.SetFloat("_Metallic", 0f);
                _mats.Add(m);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }

        /// <summary>Rotation by deg about the character's left-right axis (puppet frame).</summary>
        private Quaternion Swing(float deg) => _yaw0 * Quaternion.Euler(deg, 0f, 0f) * Quaternion.Inverse(_yaw0);

        private void LateUpdate()
        {
            if (_model == null || _hipsBone == null) return;
            float dt = Time.deltaTime;
            bool procedural = _mode != Mode.Limp; // limp: pure physics

            // Smooth the animation targets (keeps the walk readable even when the physics jitters).
            float k = 1f - Mathf.Exp(-22f * dt);
            foreach (Limb l in _limbs) l.Visual = Mathf.Lerp(l.Visual, l.Target, k);

            Quaternion pelvisDelta = _pelvis.T.rotation * Quaternion.Inverse(_pelvisRest);
            Quaternion torsoDelta = _torso.T.rotation * Quaternion.Inverse(_pelvisRest);
            _hipsBone.position = _pelvis.T.position + pelvisDelta * (_pelvisRest * _hipsRestOffsetLocal);

            for (int i = 0; i < _links.Count; i++)
            {
                BoneLink l = _links[i];
                Quaternion q;
                if (!procedural || l.Kind == Kind.Physical)
                    q = l.Limb.T.rotation * Quaternion.Inverse(l.RestLimb) * l.RestBone;
                else
                    switch (l.Kind)
                    {
                        case Kind.UpLegL: q = pelvisDelta * Swing(_uLegL.Visual) * l.RestBone; break;
                        case Kind.LegL: q = pelvisDelta * Swing(_uLegL.Visual) * Swing(_lLegL.Visual) * l.RestBone; break;
                        case Kind.UpLegR: q = pelvisDelta * Swing(_uLegR.Visual) * l.RestBone; break;
                        case Kind.LegR: q = pelvisDelta * Swing(_uLegR.Visual) * Swing(_lLegR.Visual) * l.RestBone; break;
                        case Kind.ArmL: q = torsoDelta * Swing(_uArmL.Visual) * l.RestBone; break;
                        case Kind.ForeArmL: q = torsoDelta * Swing(_uArmL.Visual) * Swing(_lArmL.Visual) * l.RestBone; break;
                        case Kind.ArmR: q = torsoDelta * Swing(_uArmR.Visual) * l.RestBone; break;
                        case Kind.ForeArmR: q = torsoDelta * Swing(_uArmR.Visual) * Swing(_lArmR.Visual) * l.RestBone; break;
                        default: q = l.Limb.T.rotation * Quaternion.Inverse(l.RestLimb) * l.RestBone; break;
                    }
                l.Bone.rotation = q;
            }
        }

        private Limb MakeLimb(string name, Limb parent, Vector3 center, Vector3 jointPoint, float radius, float length,
                              float mass, Material mat, PrimitiveType prim, Vector3 visualScale)
        {
            float S = PlayerSetup.ElfScale;
            center *= S; jointPoint *= S; radius *= S; length *= S; visualScale *= S; mass *= S;
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            Transform t = go.transform;
            t.SetParent(_puppetRoot, false);
            t.localPosition = center;
            t.localRotation = Quaternion.identity;

            // Collider
            if (prim == PrimitiveType.Sphere)
            {
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = radius;
            }
            else
            {
                var cc = go.AddComponent<CapsuleCollider>();
                cc.direction = 1;
                cc.radius = radius;
                cc.height = Mathf.Max(radius * 2f, length + radius * 2f);
            }

            // Visual child (scaled primitive, no collider)
            GameObject v = GameObject.CreatePrimitive(prim);
            v.name = "Mesh";
            Collider vc = v.GetComponent<Collider>();
            if (vc != null) Destroy(vc);
            v.layer = go.layer;
            v.transform.SetParent(t, false);
            Vector3 s = visualScale;
            if (prim == PrimitiveType.Capsule) s = new Vector3(radius * 2f, Mathf.Max(radius, (length + radius * 2f) * 0.5f), radius * 2f);
            v.transform.localScale = s;
            Renderer r = v.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass * massScale;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxAngularVelocity = 20f;

            var limb = new Limb { Name = name, T = t, Rb = rb, Parent = parent, StartLocalPos = center };
            _limbs.Add(limb);
            return limb;
        }

        private void AddCosmetic(Transform parent, PrimitiveType prim, Vector3 localPos, Vector3 scale, Vector3 euler, Material mat)
        {
            localPos *= PlayerSetup.ElfScale; scale *= PlayerSetup.ElfScale;
            GameObject v = GameObject.CreatePrimitive(prim);
            Collider c = v.GetComponent<Collider>();
            if (c != null) Destroy(c);
            v.name = "Cosmetic";
            v.layer = parent.gameObject.layer;
            v.transform.SetParent(parent, false);
            v.transform.localPosition = localPos;
            v.transform.localRotation = Quaternion.Euler(euler);
            v.transform.localScale = scale;
            Renderer r = v.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }

        private void AddJoint(Limb limb, Vector3 jointLocalInPuppet, float xLow, float xHigh, float yLim, float zLim)
        {
            jointLocalInPuppet *= PlayerSetup.ElfScale;
            Limb parent = limb.Parent;
            var j = limb.T.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = parent.Rb;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = limb.T.InverseTransformPoint(_puppetRoot.TransformPoint(jointLocalInPuppet));
            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Limited;
            j.angularYMotion = ConfigurableJointMotion.Limited;
            j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = xLow };
            j.highAngularXLimit = new SoftJointLimit { limit = xHigh };
            j.angularYLimit = new SoftJointLimit { limit = yLim };
            j.angularZLimit = new SoftJointLimit { limit = zLim };
            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.enableCollision = false;
            j.enablePreprocessing = false;
            limb.Joint = j;
            SetDrive(limb, 1f);
            // Target rotation is relative to the starting local rotation (identity here).
            j.targetRotation = Quaternion.identity;
        }

        private void SetDrive(Limb limb, float strength)
        {
            if (limb.Joint == null) return;
            limb.Joint.slerpDrive = new JointDrive
            {
                positionSpring = jointSpring * strength,
                positionDamper = jointDamper * Mathf.Lerp(0.15f, 1f, strength),
                maximumForce = jointMaxForce,
                useAcceleration = true // mass independent: stable for light limbs
            };
        }

        // ---------------------------------------------------------------- events

        private void OnLanded(float fallSpeed)
        {
            if (fallSpeed >= limpLandingSpeed) GoLimp();
        }

        /// <summary>Sprinted into a wall: bounce back off it and flop over.</summary>
        private void OnBumped(float speedIntoWall)
        {
            Vector3 back = -transform.forward * Mathf.Clamp(speedIntoWall * 0.45f, 1f, 2.5f);
            GoLimp(back + Vector3.up * 1.6f);
        }

        /// <summary>Move the whole puppet to the root (after teleports / scene start).</summary>
        private void SnapToRoot()
        {
            if (!_built || _puppetRoot == null) return;
            Vector3 delta = transform.position - _puppetRoot.position;
            foreach (Limb l in _limbs)
            {
                l.Rb.position += delta;
                l.T.position += delta;
                l.Rb.linearVelocity = Vector3.zero;
                l.Rb.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }

        // ---------------------------------------------------------------- update

        private void Update()
        {
            if (!_built) return;

            if (Keyboard.current != null && Keyboard.current[ragdollDebugKey].wasPressedThisFrame && _mode == Mode.Active && PlayerGate.CanControl)
                GoLimp();

            // Safety net: root was teleported (spawn, scene change) before we subscribed → don't let the spring fling the puppet.
            if ((_pelvis.T.position - transform.position).sqrMagnitude > 1f) SnapToRoot();

            switch (_mode)
            {
                case Mode.Active:
                    if (_wantLimp) EnterLimp();
                    break;
                case Mode.Limp:
                    _modeTimer += Time.deltaTime;
                    FollowPelvisWithRoot();
                    float hold = _downed ? downedDuration : limpDuration; // co-op: stay down so friends can help
                    if (_modeTimer >= hold && _pelvis.Rb.linearVelocity.magnitude < 1.2f) EnterGetUp();
                    break;
                case Mode.GettingUp:
                    if (_wantLimp) { EnterLimp(); break; } // knocked down again while getting up
                    _modeTimer += Time.deltaTime;
                    _strength = Mathf.Clamp01(_modeTimer / getUpDuration);
                    if (_strength >= 1f) ExitToActive();
                    break;
            }
            _wantLimp = false;
            _hasPendingPush = false;

            // Gait / pose targets (drive joints only when not limp).
            AnimateJoints(Time.deltaTime);
        }

        private void EnterLimp()
        {
            if (!_built || _mode == Mode.Limp) return;

            // Butterfingers: a gift in hands flies off with the elf's momentum (fragile ones may break on impact).
            DropHeldGift();

            _pelvis.Rb.constraints = RigidbodyConstraints.None;
            _mode = Mode.Limp;
            _modeTimer = 0f;
            _strength = 0f;
            _downed = GameServices.Players.Count > 1; // single-player keeps the short limpDuration
            if (_controller != null) _controller.InputLocked = true;
            if (_pelvis != null) SilentNight.FX.LandingDust.Spawn(_pelvis.T.position, 0.8f);

            Vector3 push;
            if (_hasPendingPush) push = _pendingPush;
            else if (_controller != null) push = _controller.Velocity.normalized * 3f + Vector3.up * 1.5f; // along the movement
            else push = Vector3.up * 1.5f;
            _pelvis.Rb.AddForce(push, ForceMode.VelocityChange);
            _torso.Rb.AddForce(push * 0.6f, ForceMode.VelocityChange);
            _hasPendingPush = false;
            _wantLimp = false;
        }

        private void DropHeldGift()
        {
            if (_carry == null) _carry = GetComponent<ElfCarry>();
            if (_carry != null && _carry.IsHoldingSomething) _carry.Drop();
        }

        private void FollowPelvisWithRoot()
        {
            if (_controller == null || _pelvis == null) return;
            Vector3 p = _pelvis.T.position;
            // Keep the root under the pelvis at ground height so camera & interactions follow the body.
            float y = transform.position.y;
            if (Physics.Raycast(p + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 3f, ~(1 << gameObject.layer), QueryTriggerInteraction.Ignore))
                y = hit.point.y;
            Vector3 target = new Vector3(p.x, y, p.z);
            if ((target - transform.position).sqrMagnitude > 0.0004f)
                _controller.Teleport(target, transform.rotation, false);
        }

        /// <summary>Put every part back into the standing layout at the root (yaw = facing). Used to get up reliably.</summary>
        private void ResetStanding(Quaternion facing)
        {
            Quaternion yaw = Quaternion.Euler(0f, facing.eulerAngles.y, 0f);
            foreach (Limb l in _limbs)
            {
                Vector3 pos = transform.position + yaw * l.StartLocalPos;
                l.Rb.position = pos;
                l.Rb.rotation = yaw;
                l.T.SetPositionAndRotation(pos, yaw);
                l.Rb.linearVelocity = Vector3.zero;
                l.Rb.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }

        private void EnterGetUp()
        {
            _mode = Mode.GettingUp;
            _modeTimer = 0f;
            _downed = false;
            FollowPelvisWithRoot();
            // Face the direction the head points to (keeps the get-up natural).
            Vector3 fwd = Vector3.ProjectOnPlane(_pelvis.T.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(_head.T.position - _pelvis.T.position, Vector3.up);
            if (fwd.sqrMagnitude > 0.01f && _controller != null)
                _controller.Teleport(transform.position, Quaternion.LookRotation(fwd.normalized, Vector3.up), false);
            ResetStanding(transform.rotation);
        }

        private void ExitToActive()
        {
            _mode = Mode.Active;
            _strength = 1f;
            if (_controller != null) _controller.InputLocked = false;
        }

        // ---------------------------------------------------------------- pose

        private static float SwingDeg(float rad, float maxDeg) => Mathf.Clamp(rad * Mathf.Rad2Deg, -maxDeg, maxDeg);

        private void Drive(Limb l, float xDeg)
        {
            l.Target = xDeg;
            l.Joint.targetRotation = Quaternion.Inverse(Quaternion.Euler(xDeg, 0f, 0f));
        }

        private void AnimateJoints(float dt)
        {
            if (_controller == null) return;
            float strength = _mode == Mode.Limp ? 0.04f : _strength;
            foreach (Limb l in _limbs) if (l.Joint != null) SetDrive(l, strength);
            if (_mode == Mode.Limp) return;

            float speed = _controller.PlanarSpeed;
            bool grounded = _controller.IsGrounded;
            bool crouch = _controller.IsCrouching;
            bool carrying = _carry != null && (_carry.IsHoldingSomething);

            _phase += speed * strideRadPerMeter * dt;
            float amp = Mathf.Lerp(0.08f, 0.95f, Mathf.Clamp01(speed / _controller.SprintSpeed));
            if (crouch) amp *= 0.55f;
            float s = Mathf.Sin(_phase);
            float c = Mathf.Cos(_phase);
            float lean = Mathf.Clamp01(speed / _controller.SprintSpeed) * leanAtSpeedDegrees + (crouch ? 22f : 0f);

            float hipL, hipR, kneeL, kneeR;
            if (!grounded)
            {
                hipL = -22f; hipR = -10f; kneeL = 45f; kneeR = 30f; // tucked while airborne
            }
            else
            {
                hipL = -SwingDeg(amp * s, maxSwingDegrees);
                hipR = SwingDeg(amp * s, maxSwingDegrees);
                kneeL = Mathf.Max(0f, c) * amp * 60f + 4f;
                kneeR = Mathf.Max(0f, -c) * amp * 60f + 4f;
            }
            if (crouch)
            {
                hipL -= 50f; hipR -= 50f; kneeL += 85f; kneeR += 85f;
            }

            // Ledge mantle: knees up and kicking (alternating scramble), torso hunched over the edge.
            bool mantling = _controller.IsMantling;
            float scramble = mantling ? Mathf.Sin(Time.time * mantleScrambleRate) : 0f;
            if (mantling)
            {
                hipL = -65f + 22f * scramble; hipR = -65f - 22f * scramble;
                kneeL = 95f - 15f * scramble; kneeR = 95f + 15f * scramble;
                lean = 35f;
            }

            Drive(_uLegL, hipL);
            Drive(_uLegR, hipR);
            Drive(_lLegL, kneeL);
            Drive(_lLegR, kneeR);
            Drive(_torso, lean * 0.6f);
            Drive(_head, -lean * 0.5f);

            if (mantling && !carrying)
            {
                // Both arms reaching up/forward onto the ledge, pawing a little out of phase.
                Drive(_uArmL, -150f + 12f * scramble); Drive(_uArmR, -150f - 12f * scramble);
                Drive(_lArmL, -35f); Drive(_lArmR, -35f);
            }
            else if (carrying)
            {
                Drive(_uArmL, -75f); Drive(_uArmR, -75f);
                Drive(_lArmL, -50f); Drive(_lArmR, -50f);
            }
            else if (!grounded)
            {
                Drive(_uArmL, -140f); Drive(_uArmR, -140f);
                Drive(_lArmL, -20f); Drive(_lArmR, -20f);
            }
            else
            {
                Drive(_uArmL, SwingDeg(0.5f * amp * s, 35f) + 4f);
                Drive(_uArmR, -SwingDeg(0.5f * amp * s, 35f) + 4f);
                Drive(_lArmL, -18f); Drive(_lArmR, -18f);
            }
        }

        private void OnGUI()
        {
            if (!_built) return;
            GUI.Label(new Rect(10, 10, 600, 24), "Ragdoll: " + _mode + "  strength " + _strength.ToString("0.00") + "  pelvisY " + _pelvis.T.position.y.ToString("0.00") + "  rootY " + transform.position.y.ToString("0.00"));
        }

        // ---------------------------------------------------------------- physics

        private void FixedUpdate()
        {
            if (!_built || _controller == null || _mode == Mode.Limp) return;

            Rigidbody rb = _pelvis.Rb;
            float k = _strength;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            bool crouch = _controller.IsCrouching;

            // Position spring toward (root x/z, ride height). During get-up the target rises from the ground.
            Vector3 root = transform.position;
            float h = (rideHeight - (crouch ? crouchDrop : 0f)) * PlayerSetup.ElfScale;
            Vector3 target = new Vector3(root.x, root.y + h, root.z);
            Vector3 err = target - rb.position;
            Vector3 accel = (err * positionSpring - rb.linearVelocity * positionDamper) * Mathf.Max(0.15f, k);
            // Support the weight of the whole body (limbs hang off the pelvis).
            Vector3 force = accel * _totalMass + Vector3.up * (-Physics.gravity.y * _totalMass * k);
            // Horizontal tracking is stiffer so the puppet never lags behind the root.
            Vector3 horiz = new Vector3(err.x, 0f, err.z);
            force += horiz * (positionSpring * 1.5f * _totalMass * k);
            rb.AddForce(force, ForceMode.Force);

            // Upright + facing torque (acceleration mode: independent of the inertia tensor).
            float speed01 = Mathf.Clamp01(_controller.PlanarSpeed / _controller.SprintSpeed);
            Quaternion want = Quaternion.Euler(speed01 * leanAtSpeedDegrees * 0.3f, transform.eulerAngles.y, 0f);
            Quaternion delta = want * Quaternion.Inverse(rb.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (!float.IsNaN(axis.x) && axis.sqrMagnitude > 0.0001f)
            {
                Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad * uprightSpring) - rb.angularVelocity * uprightDamper;
                rb.AddTorque(torque * Mathf.Max(0.2f, k), ForceMode.Acceleration);
            }
        }
    }
}
