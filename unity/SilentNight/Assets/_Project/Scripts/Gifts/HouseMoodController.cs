using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Applies the order's HouseMood on Start.
    /// Cozy: warm lights on, warm trilight ambient, light fog.
    /// Spooky: warm lights off, a few flickering cold lights, cold dim ambient, dense fog.
    /// </summary>
    public class HouseMoodController : MonoBehaviour
    {
        [Header("Lights (assigned by the scene builder)")]
        [SerializeField] private Light[] warmLights;
        [Tooltip("Candidates that stay on (dim, cold, flickering) in Spooky houses.")]
        [SerializeField] private Light[] spookyFlickerLights;
        [SerializeField] private int spookyFlickerCount = 3;
        [Tooltip("Shown only in cozy houses (garlands, fireplace fire...).")]
        [SerializeField] private GameObject[] cozyOnlyObjects;
        [Tooltip("Shown only in spooky houses (cobwebs, candles...).")]
        [SerializeField] private GameObject[] spookyOnlyObjects;

        [Header("Testing")]
        [SerializeField] private bool overrideMood;
        [SerializeField] private HouseMood testMood = HouseMood.Cozy;

        [Header("Cozy")]
        [SerializeField] private Color cozySky = new Color(0.55f, 0.42f, 0.32f);
        [SerializeField] private Color cozyEquator = new Color(0.42f, 0.30f, 0.22f);
        [SerializeField] private Color cozyGround = new Color(0.20f, 0.14f, 0.10f);
        [SerializeField] private Color cozyFogColor = new Color(0.25f, 0.18f, 0.14f);
        [SerializeField] private float cozyFogDensity = 0.012f;

        [Header("Spooky")]
        [SerializeField] private Color spookySky = new Color(0.10f, 0.11f, 0.20f);
        [SerializeField] private Color spookyEquator = new Color(0.06f, 0.07f, 0.12f);
        [SerializeField] private Color spookyGround = new Color(0.02f, 0.02f, 0.04f);
        [SerializeField] private Color spookyFogColor = new Color(0.04f, 0.05f, 0.09f);
        [SerializeField] private float spookyFogDensity = 0.07f;
        [SerializeField] private Color spookyLightTint = new Color32(0x81, 0x8c, 0xf8, 0xff);
        [Tooltip("Spooky lights intensity relative to their original intensity (prototype 0.65 / 2.8).")]
        [SerializeField] private float spookyIntensityScale = 0.25f;

        [Header("Flicker")]
        [SerializeField] private Vector2 flickerSteadyTime = new Vector2(0.4f, 3.5f);
        [SerializeField] private Vector2 flickerBurstTime = new Vector2(0.03f, 0.18f);
        [SerializeField] private int maxBurstFlashes = 6;

        public HouseMood Mood { get; private set; }

        private float[] _warmBase;
        private float[] _flickerBase;
        private Color[] _flickerBaseColor;
        private bool[] _flickerActive;
        private float[] _flickerTimer;
        private int[] _flickerBurstLeft;

        private void Awake()
        {
            _warmBase = CaptureIntensities(warmLights);
            _flickerBase = CaptureIntensities(spookyFlickerLights);
            int n = spookyFlickerLights != null ? spookyFlickerLights.Length : 0;
            _flickerBaseColor = new Color[n];
            for (int i = 0; i < n; i++) _flickerBaseColor[i] = spookyFlickerLights[i] != null ? spookyFlickerLights[i].color : Color.white;
            _flickerActive = new bool[n];
            _flickerTimer = new float[n];
            _flickerBurstLeft = new int[n];
        }

        private void Start()
        {
            HouseMood mood = overrideMood
                ? testMood
                : (GameServices.ActiveOrder != null ? GameServices.ActiveOrder.Mood : HouseMood.Cozy);
            Apply(mood);
        }

        public void Apply(HouseMood mood)
        {
            Mood = mood;
            bool spooky = mood == HouseMood.Spooky;

            // Warm lights.
            if (warmLights != null)
            {
                for (int i = 0; i < warmLights.Length; i++)
                {
                    var l = warmLights[i];
                    if (l == null || IsFlickerCandidate(l)) continue;
                    l.enabled = !spooky;
                    if (!spooky) { l.intensity = _warmBase[i]; }
                }
            }

            // Flicker lights: in spooky houses pick a few random ones, rest off.
            int n = spookyFlickerLights != null ? spookyFlickerLights.Length : 0;
            for (int i = 0; i < n; i++) _flickerActive[i] = false;
            if (spooky && n > 0)
            {
                int want = Mathf.Clamp(spookyFlickerCount, 0, n);
                int picked = 0, guard = 0;
                while (picked < want && guard++ < 100)
                {
                    int idx = Random.Range(0, n);
                    if (_flickerActive[idx] || spookyFlickerLights[idx] == null) continue;
                    _flickerActive[idx] = true;
                    _flickerTimer[idx] = Random.Range(flickerSteadyTime.x, flickerSteadyTime.y);
                    _flickerBurstLeft[idx] = 0;
                    picked++;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var l = spookyFlickerLights[i];
                if (l == null) continue;
                if (spooky)
                {
                    l.enabled = _flickerActive[i];
                    l.color = spookyLightTint;
                    l.intensity = _flickerBase[i] * spookyIntensityScale;
                }
                else
                {
                    l.enabled = true;
                    l.color = _flickerBaseColor[i];
                    l.intensity = _flickerBase[i];
                }
            }

            SetActiveAll(cozyOnlyObjects, !spooky);
            SetActiveAll(spookyOnlyObjects, spooky);

            // Ambient + fog.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = spooky ? spookySky : cozySky;
            RenderSettings.ambientEquatorColor = spooky ? spookyEquator : cozyEquator;
            RenderSettings.ambientGroundColor = spooky ? spookyGround : cozyGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = spooky ? spookyFogColor : cozyFogColor;
            RenderSettings.fogDensity = spooky ? spookyFogDensity : cozyFogDensity;
            // Refresh the ambient probe (SH) with the new trilight colours (harmless if Unity already did it).
            DynamicGI.UpdateEnvironment();
        }

        private void Update()
        {
            if (Mood != HouseMood.Spooky || spookyFlickerLights == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < spookyFlickerLights.Length; i++)
            {
                if (!_flickerActive[i]) continue;
                var l = spookyFlickerLights[i];
                if (l == null) continue;

                _flickerTimer[i] -= dt;
                if (_flickerTimer[i] > 0f) continue;

                float baseI = _flickerBase[i] * spookyIntensityScale;
                if (_flickerBurstLeft[i] <= 0)
                {
                    // Start a burst of quick flashes.
                    _flickerBurstLeft[i] = Random.Range(2, Mathf.Max(3, maxBurstFlashes + 1));
                }

                _flickerBurstLeft[i]--;
                if (_flickerBurstLeft[i] > 0)
                {
                    bool off = l.enabled && Random.value < 0.6f;
                    l.enabled = !off;
                    l.intensity = baseI * Random.Range(0.3f, 1.2f);
                    _flickerTimer[i] = Random.Range(flickerBurstTime.x, flickerBurstTime.y);
                }
                else
                {
                    // Burst over: steady again for a while.
                    l.enabled = true;
                    l.intensity = baseI;
                    _flickerTimer[i] = Random.Range(flickerSteadyTime.x, flickerSteadyTime.y);
                }
            }
        }

        private bool IsFlickerCandidate(Light l)
        {
            if (spookyFlickerLights == null) return false;
            for (int i = 0; i < spookyFlickerLights.Length; i++)
                if (spookyFlickerLights[i] == l) return true;
            return false;
        }

        private static float[] CaptureIntensities(Light[] lights)
        {
            int n = lights != null ? lights.Length : 0;
            var arr = new float[n];
            for (int i = 0; i < n; i++) arr[i] = lights[i] != null ? lights[i].intensity : 1f;
            return arr;
        }

        private static void SetActiveAll(GameObject[] objs, bool active)
        {
            if (objs == null) return;
            for (int i = 0; i < objs.Length; i++)
                if (objs[i] != null) objs[i].SetActive(active);
        }
    }
}
