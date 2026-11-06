using UnityEngine;

namespace SilentNight.FX
{
    /// <summary>
    /// Small outward ring of snow / dust puffs for comedic landings and faceplants.
    /// One shared world-space ParticleSystem is created lazily (and re-created after a scene change);
    /// every call just emits into it, so spawning is allocation-free.
    /// </summary>
    public static class LandingDust
    {
        private const int MaxParticles = 96;

        private static readonly Color32 SnowA = new Color32(250, 250, 255, 255);
        private static readonly Color32 SnowB = new Color32(222, 230, 242, 255);

        private static ParticleSystem s_ps;

        /// <summary>Snow-white landing puff. strength01: 0 = soft hop, 1 = full faceplant.</summary>
        public static void Spawn(Vector3 pos, float strength01)
        {
            Emit(pos, strength01, SnowA, SnowB);
        }

        /// <summary>Landing puff with a custom tint (e.g. warm dust indoors, soot-grey on a hearth).</summary>
        public static void Spawn(Vector3 pos, float strength01, Color tint)
        {
            Color32 a = tint;
            Color32 b = Color.Lerp(tint, Color.white, 0.25f);
            Emit(pos, strength01, a, b);
        }

        private static void Emit(Vector3 pos, float strength01, Color32 colA, Color32 colB)
        {
            ParticleSystem ps = GetSystem();
            if (ps == null) return;

            float s = Mathf.Clamp01(strength01);
            int count = Mathf.RoundToInt(Mathf.Lerp(6f, 16f, s));
            float speed = Mathf.Lerp(0.7f, 2.2f, s);
            float ringRadius = Mathf.Lerp(0.06f, 0.16f, s);
            float sizeMul = Mathf.Lerp(0.7f, 1.3f, s);
            float phase = Random.value * Mathf.PI * 2f;
            float step = Mathf.PI * 2f / count;

            var ep = new ParticleSystem.EmitParams();
            ep.applyShapeToPosition = false;

            for (int i = 0; i < count; i++)
            {
                float a = phase + i * step + Random.Range(-0.25f, 0.25f) * step;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                ep.position = pos + dir * ringRadius + new Vector3(0f, 0.03f, 0f);
                ep.velocity = dir * (speed * Random.Range(0.7f, 1.2f))
                              + new Vector3(0f, Random.Range(0.25f, 0.7f) * (0.5f + s), 0f);
                ep.startSize = Random.Range(0.1f, 0.18f) * sizeMul;
                ep.startLifetime = Random.Range(0.3f, 0.6f);
                ep.rotation = Random.Range(0f, 360f);
                ep.startColor = Color32.Lerp(colA, colB, Random.value);
                ps.Emit(ep, 1);
            }
        }

        private static ParticleSystem GetSystem()
        {
            if (s_ps != null) return s_ps;

            s_ps = FxUtil.CreateSystem("[FX] LandingDust", null, FxUtil.AlphaMaterial, MaxParticles);

            var main = s_ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = 0.45f;
            main.startSize = 0.14f;
            main.gravityModifier = 0.35f;

            var emission = s_ps.emission;
            emission.enabled = false;

            var limit = s_ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(0.25f);
            limit.dampen = 0.25f;

            var size = s_ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, FxUtil.Curve(0f, 0.6f, 0.3f, 1f, 1f, 1.25f));

            var col = s_ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FxUtil.AlphaFade(0f, 0.95f, 0.5f, 0.7f, 1f, 0f));

            // Keep it simulating so Emit() particles update; emission is off so it costs ~nothing when idle.
            s_ps.Play(false);
            return s_ps;
        }
    }
}
