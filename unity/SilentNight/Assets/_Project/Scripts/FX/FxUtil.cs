using UnityEngine;
using UnityEngine.Rendering;

namespace SilentNight.FX
{
    /// <summary>
    /// Shared runtime helpers for code-built particle effects: cached materials,
    /// a procedural soft round texture and a ParticleSystem factory.
    /// </summary>
    internal static class FxUtil
    {
        private static Material s_alphaMat;
        private static Material s_additiveMat;
        private static Texture2D s_softTex;
        private static bool s_warnedNoShader;

        /// <summary>Alpha-blended soft round particle material (white, tinted by particle color).</summary>
        internal static Material AlphaMaterial
        {
            get
            {
                if (s_alphaMat == null) s_alphaMat = CreateMaterial(false);
                return s_alphaMat;
            }
        }

        /// <summary>Additive soft round particle material for embers / sparks.</summary>
        internal static Material AdditiveMaterial
        {
            get
            {
                if (s_additiveMat == null) s_additiveMat = CreateMaterial(true);
                return s_additiveMat;
            }
        }

        internal static Texture2D SoftTexture
        {
            get
            {
                if (s_softTex == null) s_softTex = CreateSoftTexture(64);
                return s_softTex;
            }
        }

        private static Material CreateMaterial(bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                if (!s_warnedNoShader)
                {
                    s_warnedNoShader = true;
                    Debug.LogWarning("[SilentNight.FX] No particle shader found (URP Particles/Unlit, URP Unlit, Sprites/Default).");
                }
                return null;
            }

            var m = new Material(shader)
            {
                name = additive ? "FX_Additive (runtime)" : "FX_Alpha (runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };

            Texture2D tex = SoftTexture;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

            // URP transparent surface setup (ignored by Sprites/Default, which is always alpha-blended).
            SetFloat(m, "_Surface", 1f);
            SetFloat(m, "_Blend", additive ? 2f : 0f); // 0 Alpha, 2 Additive
            SetFloat(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(m, "_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(m, "_SrcBlendAlpha", (float)BlendMode.One);
            SetFloat(m, "_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(m, "_ZWrite", 0f);
            SetFloat(m, "_AlphaClip", 0f);
            SetFloat(m, "_SoftParticlesEnabled", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        private static void SetFloat(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        /// <summary>Chunky soft blob: solid core with a short smooth falloff (felt / clay puff look).</summary>
        private static Texture2D CreateSoftTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = "FX_SoftDot (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var px = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// Creates a stopped, cleared ParticleSystem on a new child GameObject with a sane cheap baseline:
        /// no play-on-awake, world simulation, no emission rate, shape disabled, no shadows/probes.
        /// </summary>
        internal static ParticleSystem CreateSystem(string name, Transform parent, Material mat, int maxParticles)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = false;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }

        internal static AnimationCurve Curve(float t0, float v0, float t1, float v1)
        {
            return new AnimationCurve(new Keyframe(t0, v0), new Keyframe(t1, v1));
        }

        internal static AnimationCurve Curve(float t0, float v0, float t1, float v1, float t2, float v2)
        {
            return new AnimationCurve(new Keyframe(t0, v0), new Keyframe(t1, v1), new Keyframe(t2, v2));
        }

        /// <summary>White color gradient with the given alpha keys (time, alpha pairs).</summary>
        internal static Gradient AlphaFade(params float[] timeAlphaPairs)
        {
            int n = timeAlphaPairs.Length / 2;
            var alphas = new GradientAlphaKey[n];
            for (int i = 0; i < n; i++)
                alphas[i] = new GradientAlphaKey(timeAlphaPairs[i * 2 + 1], timeAlphaPairs[i * 2]);

            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphas);
            return g;
        }
    }
}
