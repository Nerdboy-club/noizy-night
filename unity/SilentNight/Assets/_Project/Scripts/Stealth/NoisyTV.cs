using System;
using SilentNight.Core;
using SilentNight.Gifts;
using UnityEngine;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Old TV with white noise ("Телевизор с белым шумом, который может внезапно включиться при падении пульта").
    /// When ON: the screen flickers grey-white (unlit colour via MaterialPropertyBlock + a small flickering point light),
    /// a 3D AudioSource loops quiet procedural white noise and every <see cref="NoiseInterval"/> seconds a
    /// NoiseEvent (55, NoiseSource.Object) is raised at the screen, so Grandpa comes to check.
    /// [E] "Выключить телевизор" turns it off. It is switched on by its <see cref="TvRemote"/> falling / being knocked.
    ///
    /// Setup: <see cref="Create"/> builds stand + body + screen + remote (edit mode or play mode). Everything is on the
    /// Interactable layer except the remote (Default) so elves (ElfController) can push it off the stand.
    /// </summary>
    [DisallowMultipleComponent]
    public class NoisyTV : InteractableBase
    {
        // Layout of the built TV (local space, front of the screen faces local +Z).
        public const float StandWidth = 1.2f;
        public const float StandDepth = 0.45f;
        public const float StandHeight = 0.55f;
        public const float BodyWidth = 0.9f;
        public const float BodyHeight = 0.6f;
        public const float BodyDepth = 0.35f;
        public const float BodyOffsetZ = -0.03f;

        [Header("Parts (built by Create)")]
        [SerializeField] private Renderer screen;
        [SerializeField] private TvRemote remote;
        [SerializeField] private AudioSource staticSource;
        [SerializeField] private Light screenLight;

        [Header("Texts")]
        [SerializeField] private string promptOff = "Телевизор";
        [SerializeField] private string promptOn = "Выключить телевизор";
        [SerializeField] private string toastOn = "Пульт упал — телевизор зашипел белым шумом!";
        [SerializeField] private string toastOff = "Фух, выключили.";

        [Header("Noise")]
        [SerializeField, Range(0f, 100f)] private float noiseLoudness = 55f;
        [SerializeField] private float noiseInterval = 2f;
        [SerializeField] private NoiseSource noiseSource = NoiseSource.Object;

        [Header("Audio")]
        [SerializeField, Range(0f, 1f)] private float staticVolume = 0.12f;
        [SerializeField] private float audioMinDistance = 1f;
        [SerializeField] private float audioMaxDistance = 12f;

        [Header("Screen flicker")]
        [SerializeField] private Color offColor = new Color(0.04f, 0.05f, 0.055f, 1f);
        [SerializeField] private float flickerMin = 0.55f;
        [SerializeField] private float flickerMax = 1f;
        [SerializeField] private float flickerSpeed = 22f;
        [SerializeField] private float lightIntensity = 0.8f;

        private bool _isOn;
        private float _noiseTimer;
        private float _flickerSeed;
        private bool _ready;
        private MaterialPropertyBlock _mpb;

        private static bool s_creating;
        private static AudioClip s_whiteNoise;
        private static Material s_screenMaterial;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_whiteNoise = null;
            s_screenMaterial = null;
            s_creating = false;
        }

        // ---------------- public API ----------------
        public bool IsOn => _isOn;
        public TvRemote Remote => remote;
        public Renderer Screen => screen;
        public AudioSource StaticSource => staticSource;
        public float NoiseLoudness => noiseLoudness;
        public float NoiseInterval => noiseInterval;
        /// <summary>Number of NoiseEvents raised since start (or <see cref="ResetNoise"/>).</summary>
        public int NoiseCount { get; private set; }
        /// <summary>Time.time of the last noise, or -1.</summary>
        public float LastNoiseTime { get; private set; } = -1f;
        public int TurnOnCount { get; private set; }
        /// <summary>Raised with the new power state.</summary>
        public event Action<bool> PowerChanged;

        public override string Prompt => _isOn ? promptOn : promptOff;

        /// <summary>
        /// Build a TV on a stand (works in edit mode for the scene builder and at runtime).
        /// <paramref name="floorPos"/> = centre of the stand's footprint at floor height. The screen faces
        /// Quaternion.Euler(0, yawDeg, 0) * Vector3.forward (yaw -90 = faces -X, 90 = +X, 0 = +Z, 180 = -Z);
        /// the stand's back is StandDepth/2 behind floorPos. The remote lies on the front edge of the stand.
        /// </summary>
        public static NoisyTV Create(Transform parent, Vector3 floorPos, float yawDeg, Material bodyMat)
        {
            var root = new GameObject("NoisyTV");
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floorPos, Quaternion.Euler(0f, yawDeg, 0f));

            // Interaction trigger (added before the component so InteractableBase doesn't add its default one).
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(StandWidth + 0.2f, StandHeight + BodyHeight + 0.1f, StandDepth + 0.5f);
            trigger.center = new Vector3(0f, (StandHeight + BodyHeight + 0.1f) * 0.5f, 0.2f);

            NoisyTV tv;
            s_creating = true;
            try { tv = root.AddComponent<NoisyTV>(); }
            finally { s_creating = false; }

            tv.Build(bodyMat);
            tv.Setup();
            return tv;
        }

        protected override void Awake()
        {
            base.Awake();
            if (s_creating) return; // Create() builds the parts first, then calls Setup()
            Setup();
        }

        private void Build(Material bodyMat)
        {
            Transform t = transform;

            // Stand (solid, static).
            GameObject stand = Part(t, "TvStand", PrimitiveType.Cube, new Vector3(0f, StandHeight * 0.5f, 0f),
                new Vector3(StandWidth, StandHeight, StandDepth), bodyMat, keepCollider: true);
            // Small legs / plinth look.
            Part(stand.transform, "Plinth", PrimitiveType.Cube, new Vector3(0f, -0.5f + 0.02f / StandHeight, 0.02f / StandDepth),
                new Vector3(1.02f, 0.04f / StandHeight, 1f), bodyMat, keepCollider: false);

            // TV body (old CRT box) with a solid collider.
            float bodyY = StandHeight + BodyHeight * 0.5f;
            Part(t, "TvBody", PrimitiveType.Cube, new Vector3(0f, bodyY, BodyOffsetZ),
                new Vector3(BodyWidth, BodyHeight, BodyDepth), bodyMat, keepCollider: true);
            // CRT back bulge.
            Part(t, "TvBack", PrimitiveType.Cube, new Vector3(0f, bodyY, BodyOffsetZ - BodyDepth * 0.5f - 0.07f),
                new Vector3(BodyWidth * 0.7f, BodyHeight * 0.75f, 0.14f), bodyMat, keepCollider: false);
            // Antenna.
            GameObject ant = Part(t, "Antenna", PrimitiveType.Cylinder, new Vector3(0.12f, StandHeight + BodyHeight + 0.14f, BodyOffsetZ),
                new Vector3(0.012f, 0.15f, 0.012f), bodyMat, keepCollider: false);
            ant.transform.localRotation = Quaternion.Euler(0f, 0f, -25f);
            GameObject ant2 = Part(t, "Antenna2", PrimitiveType.Cylinder, new Vector3(-0.12f, StandHeight + BodyHeight + 0.14f, BodyOffsetZ),
                new Vector3(0.012f, 0.15f, 0.012f), bodyMat, keepCollider: false);
            ant2.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);

            // Screen (visual only).
            float screenZ = BodyOffsetZ + BodyDepth * 0.5f + 0.006f;
            GameObject scr = Part(t, "Screen", PrimitiveType.Cube, new Vector3(0f, bodyY + 0.02f, screenZ),
                new Vector3(BodyWidth * 0.78f, BodyHeight * 0.74f, 0.01f), bodyMat, keepCollider: false);
            screen = scr.GetComponent<Renderer>();
            interactionPoint = scr.transform;

            // Glow light (off until the TV turns on).
            var lightGo = new GameObject("ScreenLight");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.localPosition = new Vector3(0f, bodyY, screenZ + 0.25f);
            screenLight = lightGo.AddComponent<Light>();
            screenLight.type = LightType.Point;
            screenLight.range = 3f;
            screenLight.intensity = 0f;
            screenLight.color = new Color(0.82f, 0.86f, 0.95f);
            screenLight.shadows = LightShadows.None;
            screenLight.enabled = false;

            // Static audio on the body.
            staticSource = scr.AddComponent<AudioSource>();

            // Everything on Interactable...
            int layer = LayerMask.NameToLayer(GameConstants.LayerInteractable);
            if (layer >= 0) StealthUtil.SetLayerRecursive(gameObject, layer);

            // ...except the remote (Default) so elves push it.
            var remoteGo = new GameObject("TvRemote");
            remoteGo.transform.SetParent(t, false);
            remoteGo.transform.localPosition = new Vector3(0.3f, StandHeight + TvRemote.Size.y * 0.5f + 0.002f,
                StandDepth * 0.5f - TvRemote.Size.z * 0.5f - 0.005f);
            remoteGo.transform.localRotation = Quaternion.Euler(0f, 8f, 0f);
            remoteGo.layer = 0;
            remote = remoteGo.AddComponent<TvRemote>();
            remote.Build(bodyMat);
            remote.SetTv(this);
        }

        private static GameObject Part(Transform parent, string name, PrimitiveType prim, Vector3 localPos, Vector3 scale,
            Material mat, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(prim);
            go.name = name;
            if (!keepCollider)
            {
                Collider c = go.GetComponent<Collider>();
                if (c != null) DestroyImmediate(c);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return go;
        }

        /// <summary>Find parts, configure audio/light/screen. Idempotent.</summary>
        private void Setup()
        {
            if (screen == null)
            {
                Transform s = transform.Find("Screen");
                if (s != null) screen = s.GetComponent<Renderer>();
            }
            if (remote == null) remote = GetComponentInChildren<TvRemote>(true);
            if (remote != null) remote.SetTv(this);
            if (screenLight == null) screenLight = GetComponentInChildren<Light>(true);

            if (staticSource == null)
            {
                GameObject host = screen != null ? screen.gameObject : gameObject;
                staticSource = host.GetComponent<AudioSource>();
                if (staticSource == null) staticSource = host.AddComponent<AudioSource>();
            }
            staticSource.playOnAwake = false;
            staticSource.loop = true;
            staticSource.spatialBlend = 1f;
            staticSource.rolloffMode = AudioRolloffMode.Linear;
            staticSource.minDistance = Mathf.Max(0.1f, audioMinDistance);
            staticSource.maxDistance = Mathf.Max(staticSource.minDistance + 0.5f, audioMaxDistance);
            staticSource.volume = staticVolume;
            staticSource.dopplerLevel = 0f;

            if (Application.isPlaying)
            {
                if (staticSource.clip == null) staticSource.clip = WhiteNoiseClip;
                if (screen != null)
                {
                    Material m = ScreenMaterial;
                    if (m != null) screen.sharedMaterial = m;
                }
            }

            _flickerSeed = UnityEngine.Random.value * 100f;
            _ready = true;
            ApplyScreen(_isOn ? flickerMax : 0f);
        }

        // ---------------- power ----------------

        public void TurnOn()
        {
            if (_isOn) return;
            _isOn = true;
            TurnOnCount++;
            if (staticSource != null && Application.isPlaying)
            {
                if (staticSource.clip == null) staticSource.clip = WhiteNoiseClip;
                staticSource.volume = staticVolume;
                staticSource.Play();
            }
            if (screenLight != null) screenLight.enabled = true;
            ApplyScreen(flickerMax);
            if (!string.IsNullOrEmpty(toastOn)) GameEvents.RaiseToast(toastOn);
            EmitNoise();
            _noiseTimer = noiseInterval;
            PowerChanged?.Invoke(true);
        }

        public void TurnOff()
        {
            if (!_isOn) return;
            _isOn = false;
            if (staticSource != null) staticSource.Stop();
            if (screenLight != null)
            {
                screenLight.intensity = 0f;
                screenLight.enabled = false;
            }
            ApplyScreen(0f);
            if (!string.IsNullOrEmpty(toastOff)) GameEvents.RaiseToast(toastOff);
            PowerChanged?.Invoke(false);
        }

        public override void Interact(IInteractor interactor)
        {
            if (_isOn) TurnOff();
        }

        private void OnDisable()
        {
            if (staticSource != null) staticSource.Stop();
        }

        private void OnEnable()
        {
            if (_isOn && staticSource != null && Application.isPlaying && staticSource.clip != null) staticSource.Play();
        }

        private void Update()
        {
            if (!_ready || !_isOn) return;

            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer <= 0f)
            {
                _noiseTimer += Mathf.Max(0.1f, noiseInterval);
                if (_noiseTimer <= 0f) _noiseTimer = Mathf.Max(0.1f, noiseInterval);
                EmitNoise();
            }

            float n = Mathf.PerlinNoise(Time.time * flickerSpeed, _flickerSeed);
            float jitter = UnityEngine.Random.Range(-0.08f, 0.08f);
            ApplyScreen(Mathf.Clamp01(Mathf.Lerp(flickerMin, flickerMax, n) + jitter));
        }

        private void EmitNoise()
        {
            Vector3 pos = screen != null ? screen.transform.position : transform.position;
            NoiseCount++;
            LastNoiseTime = Time.time;
            GameEvents.RaiseNoise(new NoiseEvent(pos, noiseLoudness, noiseSource, gameObject));
        }

        /// <summary>brightness 0 = off colour, otherwise grey-white at that brightness.</summary>
        private void ApplyScreen(float brightness)
        {
            if (screen == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            Color c;
            if (!_isOn || brightness <= 0f)
            {
                c = offColor;
            }
            else
            {
                float b = Mathf.Clamp01(brightness);
                c = new Color(b * 0.92f, b * 0.95f, b, 1f);
            }
            screen.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            _mpb.SetColor(EmissionColorId, _isOn ? c * 1.5f : Color.black);
            screen.SetPropertyBlock(_mpb);

            if (screenLight != null && _isOn) screenLight.intensity = lightIntensity * Mathf.Clamp01(brightness);
        }

        /// <summary>Forget noise counters (tests).</summary>
        public void ResetNoise()
        {
            NoiseCount = 0;
            LastNoiseTime = -1f;
            _noiseTimer = noiseInterval;
        }

        // ---------------- shared resources ----------------

        /// <summary>1 s mono looping white-noise clip (gently low-passed TV hiss), synthesized once.</summary>
        public static AudioClip WhiteNoiseClip
        {
            get
            {
                if (s_whiteNoise != null) return s_whiteNoise;
                const int rate = 22050;
                const int samples = rate;
                var data = new float[samples];
                var rng = new System.Random(1225);
                float lp = 0f;
                for (int i = 0; i < samples; i++)
                {
                    float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (w - lp) * 0.6f; // soften the harshest highs a little
                    data[i] = lp * 0.5f;
                }
                // Crossfade the tail into the head so the loop has no click.
                const int fade = 256;
                for (int i = 0; i < fade; i++)
                {
                    float t = i / (float)fade;
                    int tail = samples - fade + i;
                    data[tail] = Mathf.Lerp(data[tail], data[i], t);
                }
                s_whiteNoise = AudioClip.Create("TvWhiteNoise", samples, 1, rate, false);
                s_whiteNoise.SetData(data, 0);
                return s_whiteNoise;
            }
        }

        private static Material ScreenMaterial
        {
            get
            {
                if (s_screenMaterial != null) return s_screenMaterial;
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                bool lit = false;
                if (sh == null) { sh = Shader.Find("Universal Render Pipeline/Lit"); lit = sh != null; }
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh == null) return null;
                s_screenMaterial = new Material(sh) { name = "TvScreen (runtime)" };
                if (lit) s_screenMaterial.EnableKeyword("_EMISSION");
                return s_screenMaterial;
            }
        }
    }
}
