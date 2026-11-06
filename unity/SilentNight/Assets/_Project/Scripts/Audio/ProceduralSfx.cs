using System.Collections.Generic;
using UnityEngine;

namespace SilentNight.Audio
{
    /// <summary>
    /// Procedural fallback clips for when no file exists under Resources/Audio.
    /// Everything is mono, 44.1 kHz PCM floats, synthesized lazily on first request and cached per name
    /// (2–3 variants for SFX, 1 seamless loop for music). Soft, short and toy-like on purpose: plasticine
    /// thumps, wooden knocks, little bells. After generation there are no allocations: callers get cached clips.
    /// </summary>
    public static class ProceduralSfx
    {
        public const int SampleRate = 44100;
        private const float TwoPi = Mathf.PI * 2f;
        private const float MaxPartialHz = 9000f;

        private static readonly Dictionary<string, AudioClip[]> Cache = new Dictionary<string, AudioClip[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        // ------------------------------------------------------------------ public API

        /// <summary>A random variant of the procedural clip for <paramref name="name"/>, or null if the name is unknown.</summary>
        public static AudioClip Get(string name)
        {
            var v = GetVariants(name);
            if (v == null || v.Length == 0) return null;
            return v.Length == 1 ? v[0] : v[Random.Range(0, v.Length)];
        }

        /// <summary>True if a procedural recipe exists for this name.</summary>
        public static bool Has(string name) => VariantCount(name) > 0;

        /// <summary>All cached variants for <paramref name="name"/> (generated on first call), or null if unknown.</summary>
        public static AudioClip[] GetVariants(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out var cached) && AllAlive(cached)) return cached;

            int count = VariantCount(name);
            if (count <= 0) return null;

            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++)
            {
                var rng = new System.Random(Seed(name, i));
                float[] data = Synthesize(name, rng, i);
                if (data == null || data.Length == 0) return null;
                var clip = AudioClip.Create("proc_" + name + "_" + i, data.Length, 1, SampleRate, false);
                clip.SetData(data, 0);
                clips[i] = clip;
            }
            Cache[name] = clips;
            return clips;
        }

        // ------------------------------------------------------------------ recipes

        private static int VariantCount(string name)
        {
            switch (name)
            {
                case AudioClips.MusicMenu:
                case AudioClips.MusicHub:
                case AudioClips.MusicHouseCozy:
                case AudioClips.MusicHouseSpooky:
                case AudioClips.Snore:
                    return 1;

                case AudioClips.StingerCaught:
                case AudioClips.StingerAlert:
                case AudioClips.JingleDelivered:
                    return 2;

                case AudioClips.FootstepWood:
                case AudioClips.FootstepCarpet:
                case AudioClips.FootstepCreaky:
                case AudioClips.FootstepTile:
                case AudioClips.FootstepIce:
                case AudioClips.Landing:
                case AudioClips.Creak:
                case AudioClips.GiftThud:
                case AudioClips.GiftSmash:
                case AudioClips.ToyTick:
                case AudioClips.ToyAlarm:
                case AudioClips.Jetpack:
                case AudioClips.Meow:
                case AudioClips.Yowl:
                case AudioClips.Knock:
                case AudioClips.UiOpen:
                case AudioClips.UiClick:
                case AudioClips.GiftLoaded:
                case AudioClips.GiftDelivered:
                    return 3;

                default:
                    return 0;
            }
        }

        private static float[] Synthesize(string name, System.Random rng, int variant)
        {
            switch (name)
            {
                case AudioClips.FootstepCarpet: return FootCarpet(rng);
                case AudioClips.FootstepWood: return FootWood(rng);
                case AudioClips.FootstepCreaky: return FootCreaky(rng);
                case AudioClips.FootstepTile: return FootTile(rng);
                case AudioClips.FootstepIce: return FootIce(rng);
                case AudioClips.Landing: return LandingThud(rng);
                case AudioClips.Creak: return CreakSfx(rng);
                case AudioClips.GiftThud: return GiftThudSfx(rng);
                case AudioClips.GiftSmash: return GiftSmashSfx(rng);
                case AudioClips.ToyTick: return ToyTickSfx(rng);
                case AudioClips.ToyAlarm: return ToyAlarmSfx(rng);
                case AudioClips.Jetpack: return JetpackSfx(rng);
                case AudioClips.Meow: return MeowSfx(rng, false);
                case AudioClips.Yowl: return MeowSfx(rng, true);
                case AudioClips.Knock: return KnockSfx(rng);
                case AudioClips.UiOpen:
                case AudioClips.UiClick: return UiClickSfx(rng);
                case AudioClips.GiftLoaded: return GiftLoadedSfx(rng, variant);
                case AudioClips.GiftDelivered: return GiftDeliveredSfx(rng, variant);
                case AudioClips.JingleDelivered: return JingleSfx(rng, variant);
                case AudioClips.StingerAlert: return AlertSfx(rng, variant);
                case AudioClips.StingerCaught: return CaughtSfx(rng, variant);
                case AudioClips.Snore: return SnoreLoop(rng);
                case AudioClips.MusicMenu: return MusicLoop(rng, 0);
                case AudioClips.MusicHub: return MusicLoop(rng, 1);
                case AudioClips.MusicHouseCozy: return MusicLoop(rng, 2);
                case AudioClips.MusicHouseSpooky: return MusicLoop(rng, 3);
                default: return null;
            }
        }

        // ---- footsteps / impacts

        private static float[] FootCarpet(System.Random rng)
        {
            var b = Buf(0.14f);
            AddPing(b, 0f, R(rng, 70f, 95f), 1f, 0.035f, 0.004f, 0.6f, 0.02f);
            AddNoise(b, rng, 0f, 0.003f, 0.03f, Filt.Low, R(rng, 250f, 400f), 0.7f, 2.5f);
            return Finish(b, 0.42f, 0.002f, 0.02f);
        }

        private static float[] FootWood(System.Random rng)
        {
            var b = Buf(0.16f);
            WoodTap(b, rng, 0f, 1f);
            return Finish(b, 0.55f, 0.001f, 0.02f);
        }

        private static void WoodTap(float[] b, System.Random rng, float start, float amp)
        {
            float f = R(rng, 170f, 215f);
            AddPing(b, start, f, 0.8f * amp, 0.03f, 0.001f, 0.3f, 0.01f);
            AddPing(b, start, f * R(rng, 2.2f, 2.5f), 0.3f * amp, 0.015f, 0.001f);
            AddPing(b, start, R(rng, 85f, 100f), 0.4f * amp, 0.025f, 0.002f, 0.4f, 0.015f);
            AddNoise(b, rng, start, 0.0005f, 0.006f, Filt.Band, R(rng, 1500f, 2100f), 0.8f, 0.6f * amp);
        }

        private static float[] FootCreaky(System.Random rng)
        {
            var b = Buf(0.36f);
            WoodTap(b, rng, 0f, 0.7f);
            Creak(b, rng, 0.02f, R(rng, 0.22f, 0.3f), 0.8f, R(rng, 150f, 230f));
            return Finish(b, 0.55f, 0.001f, 0.03f);
        }

        private static float[] FootTile(System.Random rng)
        {
            var b = Buf(0.09f);
            AddNoise(b, rng, 0f, 0.0004f, 0.004f, Filt.Band, R(rng, 2500f, 3500f), 0.5f, 0.6f);
            AddPing(b, 0f, R(rng, 1100f, 1400f), 0.5f, 0.012f, 0.0008f);
            AddPing(b, 0f, R(rng, 2600f, 3000f), 0.2f, 0.006f, 0.0008f);
            AddPing(b, 0f, R(rng, 150f, 180f), 0.25f, 0.015f, 0.002f);
            return Finish(b, 0.5f, 0.0008f, 0.015f);
        }

        private static float[] FootIce(System.Random rng)
        {
            var b = Buf(0.32f);
            AddNoise(b, rng, 0f, 0.03f, 0.07f, Filt.Band, R(rng, 1500f, 2200f), 0.25f, 0.5f, R(rng, 2800f, 3600f));
            AddTone(b, 0.01f, R(rng, 0.14f, 0.2f), R(rng, 850f, 1000f), R(rng, 1200f, 1450f), 0.15f, 2, 0.03f, 0.08f, 3000f);
            AddPing(b, 0f, R(rng, 120f, 150f), 0.3f, 0.02f, 0.002f);
            return Finish(b, 0.45f, 0.002f, 0.04f);
        }

        private static float[] LandingThud(System.Random rng)
        {
            var b = Buf(0.36f);
            AddPing(b, 0f, R(rng, 55f, 70f), 1f, 0.07f, 0.004f, 1f, 0.03f);
            AddNoise(b, rng, 0f, 0.002f, 0.05f, Filt.Low, R(rng, 420f, 560f), 0.7f, 2.5f);
            AddPing(b, 0f, R(rng, 130f, 155f), 0.3f, 0.04f, 0.002f);
            // tiny plasticine "squish" bounce
            AddPing(b, R(rng, 0.07f, 0.09f), R(rng, 75f, 90f), 0.25f, 0.03f, 0.004f, 0.5f, 0.02f);
            return Finish(b, 0.6f, 0.002f, 0.04f);
        }

        private static float[] CreakSfx(System.Random rng)
        {
            float len = R(rng, 0.45f, 0.7f);
            var b = Buf(len + 0.03f);
            Creak(b, rng, 0f, len, 1f, R(rng, 120f, 220f));
            return Finish(b, 0.5f, 0.005f, 0.03f);
        }

        /// <summary>Stick-slip friction: an irregular impulse train exciting two wooden resonances.</summary>
        private static void Creak(float[] b, System.Random rng, float start, float dur, float amp, float fBase)
        {
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int n = Mathf.RoundToInt(dur * SampleRate);
            float wobbleRate = R(rng, 1f, 2.4f);
            float wobblePh = R(rng, 0f, TwoPi);
            float rise = R(rng, -0.15f, 0.3f);
            float r1 = R(rng, 600f, 820f), r2 = R(rng, 1250f, 1700f);
            var f1 = new Svf();
            var f2 = new Svf();
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = s0 + i;
                if (idx >= b.Length) break;
                if (idx < 0) continue;
                float u = i / (float)n;
                float t = i / (float)SampleRate;
                float f = fBase * (1f + 0.35f * Mathf.Sin(TwoPi * wobbleRate * t + wobblePh) + rise * u);
                f *= 1f + 0.08f * Noise(rng);
                phase += Mathf.Max(30f, f) / SampleRate;
                float x = 0.03f * Noise(rng);
                if (phase >= 1.0)
                {
                    phase -= 1.0;
                    x += 1f + 0.4f * Noise(rng);
                }
                f1.Process(x, r1, 0.12f);
                f2.Process(x, r2, 0.15f);
                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.7f);
                b[idx] += amp * env * (f1.Band + 0.6f * f2.Band);
            }
        }

        private static float[] GiftThudSfx(System.Random rng)
        {
            var b = Buf(0.36f);
            float f = R(rng, 95f, 125f);
            AddPing(b, 0f, f, 1f, 0.06f, 0.003f, 0.5f, 0.02f);
            AddNoise(b, rng, 0f, 0.002f, 0.05f, Filt.Low, R(rng, 500f, 750f), 0.7f, 2.2f);
            AddNoise(b, rng, 0f, 0.001f, 0.02f, Filt.Band, R(rng, 1100f, 1500f), 0.7f, 0.35f); // cardboard rustle
            float bounce = R(rng, 0.09f, 0.12f);
            AddPing(b, bounce, f * 1.1f, 0.35f, 0.04f, 0.003f, 0.4f, 0.02f);
            AddNoise(b, rng, bounce, 0.002f, 0.03f, Filt.Low, 600f, 0.7f, 0.8f);
            return Finish(b, 0.58f, 0.002f, 0.04f);
        }

        private static float[] GiftSmashSfx(System.Random rng)
        {
            var b = Buf(0.85f);
            AddPing(b, 0f, R(rng, 90f, 115f), 0.6f, 0.05f, 0.003f, 0.5f, 0.02f);
            AddNoise(b, rng, 0f, 0.001f, 0.05f, Filt.High, R(rng, 3000f, 4000f), 0.6f, 0.5f);
            int shards = 14 + rng.Next(8);
            for (int i = 0; i < shards; i++)
            {
                float u = (float)rng.NextDouble();
                AddPing(b, u * u * 0.4f, R(rng, 2200f, 6500f), R(rng, 0.08f, 0.25f), R(rng, 0.02f, 0.09f), 0.0008f);
            }
            return Finish(b, 0.55f, 0.001f, 0.06f);
        }

        private static float[] KnockSfx(System.Random rng)
        {
            var b = Buf(0.45f);
            WoodTap(b, rng, 0f, 1f);
            float t2 = R(rng, 0.1f, 0.14f);
            WoodTap(b, rng, t2, 0.45f);
            WoodTap(b, rng, t2 + R(rng, 0.07f, 0.09f), 0.2f);
            return Finish(b, 0.55f, 0.001f, 0.04f);
        }

        // ---- toys, cat, jetpack

        private static readonly float[] Pentatonic6 = { 1046.5f, 1174.7f, 1318.5f, 1568f, 1760f };

        private static float[] ToyTickSfx(System.Random rng)
        {
            var b = Buf(0.34f);
            AddNoise(b, rng, 0f, 0.0004f, 0.002f, Filt.Band, R(rng, 3500f, 4500f), 0.4f, 0.5f);
            AddPing(b, 0f, R(rng, 3000f, 3400f), 0.35f, 0.004f, 0.0005f);
            float tock = R(rng, 0.1f, 0.12f);
            AddPing(b, tock, R(rng, 1900f, 2300f), 0.25f, 0.005f, 0.0005f);
            AddBell(b, 0f, Pentatonic6[rng.Next(Pentatonic6.Length)], 0.35f, 0.12f);
            return Finish(b, 0.5f, 0.0005f, 0.03f);
        }

        private static float[] ToyAlarmSfx(System.Random rng)
        {
            var b = Buf(0.85f);
            float f1 = R(rng, 1900f, 2150f), f2 = f1 * 1.19f;
            float rate = R(rng, 18f, 24f), ringLen = R(rng, 0.5f, 0.65f);
            int strikes = Mathf.FloorToInt(ringLen * rate);
            for (int i = 0; i < strikes; i++)
            {
                float t = i / rate;
                float fade = 1f - t / (ringLen + 0.1f);
                AddBell(b, t, (i & 1) == 0 ? f1 : f2, 0.4f * fade, 0.05f);
            }
            return Finish(b, 0.55f, 0.001f, 0.06f);
        }

        private static float[] JetpackSfx(System.Random rng)
        {
            float len = R(rng, 0.38f, 0.46f);
            var b = Buf(len);
            int n = b.Length;
            var bp = new Svf();
            float rumble = 0f, ra = OnePole(140f);
            float lo = R(rng, 380f, 520f), hi = R(rng, 1200f, 1600f);
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)n;
                float x = Noise(rng);
                bp.Process(x, lo + (hi - lo) * Mathf.Sin(Mathf.PI * u), 0.5f);
                rumble += ra * (x - rumble);
                float env = u < 0.15f ? u / 0.15f : Mathf.Exp(-(u - 0.15f) * 4f);
                b[i] = env * (bp.Band * 0.8f + rumble * 2.5f);
            }
            return Finish(b, 0.5f, 0.01f, 0.06f);
        }

        /// <summary>Harmonic source with a pitch sweep through two moving formants: "m-ee-ow".</summary>
        private static float[] MeowSfx(System.Random rng, bool yowl)
        {
            float dur = yowl ? R(rng, 0.8f, 1.0f) : R(rng, 0.45f, 0.6f);
            var b = Buf(dur + 0.02f);
            int n = Mathf.RoundToInt(dur * SampleRate);
            float fa = yowl ? R(rng, 360f, 420f) : R(rng, 480f, 560f);
            float fb = yowl ? R(rng, 600f, 680f) : R(rng, 700f, 820f);
            float fc = yowl ? R(rng, 300f, 360f) : R(rng, 420f, 500f);
            float vibDepth = yowl ? 0.05f : 0.015f, vibRate = yowl ? R(rng, 6f, 8f) : 5.5f;
            var form1 = new Svf();
            var form2 = new Svf();
            double phase = 0;
            for (int i = 0; i < n && i < b.Length; i++)
            {
                float u = i / (float)n;
                float t = i / (float)SampleRate;
                float f0 = u < 0.35f ? Mathf.Lerp(fa, fb, Smooth(u / 0.35f)) : Mathf.Lerp(fb, fc, Smooth((u - 0.35f) / 0.65f));
                f0 *= 1f + vibDepth * Mathf.Sin(TwoPi * vibRate * t) * Mathf.Clamp01(u * 3f);
                phase += f0 / SampleRate;
                if (phase >= 1.0) phase -= 1.0;
                float ph = (float)phase * TwoPi;
                float src = 0f;
                for (int k = 1; k <= 8; k++)
                {
                    if (k * f0 > MaxPartialHz) break;
                    src += Mathf.Sin(ph * k) / k;
                }
                // formants: "ee" -> "ah" -> "oo"
                float F1, F2;
                if (u < 0.2f) { F1 = Mathf.Lerp(300f, 450f, u / 0.2f); F2 = 2200f; }
                else if (u < 0.5f) { float k2 = (u - 0.2f) / 0.3f; F1 = Mathf.Lerp(450f, 850f, k2); F2 = Mathf.Lerp(2200f, 1300f, k2); }
                else { float k3 = (u - 0.5f) / 0.5f; F1 = Mathf.Lerp(850f, 450f, k3); F2 = Mathf.Lerp(1300f, 800f, k3); }
                form1.Process(src, F1, 0.18f);
                form2.Process(src, F2, 0.12f);
                float env = Mathf.Clamp01(t / 0.05f) * (u > 0.7f ? Mathf.Clamp01((1f - u) / 0.3f) : 1f);
                b[i] = env * (form1.Band + 0.6f * form2.Band);
            }
            return Finish(b, 0.5f, 0.005f, 0.04f);
        }

        // ---- UI / jingles / stingers

        private static float[] UiClickSfx(System.Random rng)
        {
            var b = Buf(0.07f);
            AddPing(b, 0f, R(rng, 850f, 1000f), 0.6f, 0.008f, 0.0008f, 0.4f, 0.004f);
            AddNoise(b, rng, 0f, 0.0003f, 0.002f, Filt.Band, R(rng, 2700f, 3300f), 0.5f, 0.4f);
            return Finish(b, 0.45f, 0.0005f, 0.015f);
        }

        private static float[] GiftLoadedSfx(System.Random rng, int variant)
        {
            var b = Buf(0.9f);
            float f = variant == 0 ? 1046.5f : variant == 1 ? 1174.7f : 1318.5f;
            AddBell(b, 0f, f, 0.6f, 0.18f);
            AddBell(b, R(rng, 0.06f, 0.08f), f * 1.498f, 0.3f, 0.15f);
            return Finish(b, 0.5f, 0.001f, 0.08f);
        }

        private static float[] GiftDeliveredSfx(System.Random rng, int variant)
        {
            var b = Buf(1.0f);
            float root = variant == 0 ? 1046.5f : variant == 1 ? 1174.7f : 1396.9f;
            float step = R(rng, 0.05f, 0.065f);
            float[] ratios = { 1f, 1.26f, 1.498f, 2f };
            for (int i = 0; i < ratios.Length; i++)
                AddBell(b, i * step, root * ratios[i], 0.5f - i * 0.07f, 0.18f);
            for (int i = 0; i < 5; i++)
                AddPing(b, R(rng, 0.05f, 0.35f), R(rng, 4500f, 7000f), 0.04f, 0.03f, 0.001f);
            return Finish(b, 0.5f, 0.001f, 0.08f);
        }

        private static float[] JingleSfx(System.Random rng, int variant)
        {
            var b = Buf(2.2f);
            const float c5 = 523.25f;
            int[] melody = variant == 0
                ? new[] { 0, 4, 7, 12, 7, 12, 16, 19 }
                : new[] { 0, 4, 7, 9, 12, 16, 14, 19 };
            float beat = 0.13f;
            for (int i = 0; i < melody.Length; i++)
            {
                float f = c5 * Semi(melody[i]);
                bool last = i == melody.Length - 1;
                AddBell(b, i * beat, f, last ? 0.45f : 0.5f, last ? 0.4f : 0.3f);
                AddPing(b, i * beat, f * 0.5f, 0.18f, 0.22f, 0.004f); // soft body
                SleighShake(b, rng, i * beat + beat * 0.5f, 0.6f);
            }
            float end = melody.Length * beat + 0.04f;
            AddBell(b, end, c5 * Semi(12), 0.35f, 0.55f);
            AddBell(b, end, c5 * Semi(16), 0.3f, 0.55f);
            AddBell(b, end, c5 * Semi(19), 0.28f, 0.55f);
            AddPing(b, end, c5 * 0.5f, 0.2f, 0.4f, 0.006f);
            SleighShake(b, rng, end, 1f);
            SleighShake(b, rng, end + beat, 0.6f);
            return Finish(b, 0.6f, 0.001f, 0.15f);
        }

        private static void SleighShake(float[] b, System.Random rng, float start, float amp)
        {
            for (int k = 0; k < 3; k++)
                AddPing(b, start + k * 0.012f, R(rng, 5000f, 7000f), 0.05f * amp, 0.01f, 0.0005f);
            AddNoise(b, rng, start, 0.001f, 0.015f, Filt.Band, R(rng, 6000f, 7000f), 0.5f, 0.12f * amp);
        }

        /// <summary>"Uh-oh!" two-note toy horn plus a little alarm-bell trill.</summary>
        private static float[] AlertSfx(System.Random rng, int variant)
        {
            var b = Buf(1.0f);
            float k = variant == 0 ? 1f : Semi(-2);
            AddTone(b, 0f, 0.16f, 392f * k, 392f * k, 0.5f, 5, 0.01f, 0.04f, 2500f);
            AddTone(b, 0.18f, 0.42f, 523.25f * k, 494f * k, 0.55f, 5, 0.012f, 0.12f, 2500f, 6f, 0.02f);
            float bell = R(rng, 1700f, 1900f);
            for (int i = 0; i < 8; i++)
            {
                float t = 0.18f + i / 18f;
                AddBell(b, t, (i & 1) == 0 ? bell : bell * 1.19f, 0.15f * (1f - i / 9f), 0.04f);
            }
            return Finish(b, 0.6f, 0.002f, 0.08f);
        }

        /// <summary>Sad-trombone "wah wah wah waaah", muffled like a felt toy horn.</summary>
        private static float[] CaughtSfx(System.Random rng, int variant)
        {
            var b = Buf(2.0f);
            float k = variant == 0 ? 1f : Semi(-2);
            float[] notes = { 392f, 370f, 349.2f };
            for (int i = 0; i < notes.Length; i++)
            {
                float f = notes[i] * k;
                AddTone(b, i * 0.32f, 0.3f, f * 0.97f, f, 0.5f, 6, 0.04f, 0.08f, 1500f);
            }
            float last = 329.6f * k;
            AddTone(b, 0.96f, 0.9f, last * 0.97f, last * 0.985f, 0.55f, 6, 0.04f, 0.3f, 1400f, R(rng, 5f, 6f), 0.03f);
            return Finish(b, 0.55f, 0.002f, 0.1f);
        }

        // ---- loops

        /// <summary>Grandpa snore: rattly low inhale, soft whistly exhale. Seamless (starts/ends at silence).</summary>
        private static float[] SnoreLoop(System.Random rng)
        {
            var b = Buf(3.0f);
            int n = b.Length;
            float lp1 = 0f, a1 = OnePole(350f);
            float lp2 = 0f, a2 = OnePole(600f);
            float rattle = R(rng, 24f, 32f);
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float x = Noise(rng);
                lp1 += a1 * (x - lp1);
                lp2 += a2 * (x - lp2);
                float y = 0f;
                if (t < 1.4f)
                {
                    float r = 0.5f + 0.5f * Mathf.Sin(TwoPi * rattle * t);
                    y += Mathf.Sin(Mathf.PI * t / 1.4f) * r * r * lp1 * 3f;
                }
                else if (t > 1.6f && t < 2.8f)
                {
                    float e = Mathf.Sin(Mathf.PI * (t - 1.6f) / 1.2f);
                    ph += 900.0 / SampleRate;
                    if (ph >= 1.0) ph -= 1.0;
                    y += e * (lp2 * 1.2f + 0.05f * Mathf.Sin((float)ph * TwoPi));
                }
                b[i] = y;
            }
            Normalize(b, 0.5f);
            return b;
        }

        /// <summary>
        /// Soft bell-pad music loop. All events are written with wrap-around so the loop is seamless.
        /// style: 0 menu, 1 hub, 2 cozy house, 3 spooky house.
        /// </summary>
        private static float[] MusicLoop(System.Random rng, int style)
        {
            int[][] chords;
            float bar;
            int bellsPerBar;
            float bellAmp, padAmp;
            switch (style)
            {
                case 0: // C  Am  F  G
                    chords = new[] { new[] { 60, 64, 67 }, new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 59, 62 } };
                    bar = 3f; bellsPerBar = 4; bellAmp = 0.22f; padAmp = 0.25f;
                    break;
                case 1: // C  F  G  C (cheerful, busier)
                    chords = new[] { new[] { 60, 64, 67 }, new[] { 53, 57, 60 }, new[] { 55, 59, 62 }, new[] { 60, 64, 67 } };
                    bar = 2.4f; bellsPerBar = 8; bellAmp = 0.18f; padAmp = 0.2f;
                    break;
                case 2: // Fmaj7 Em7 Dm7 Cmaj7 (sparse, quiet)
                    chords = new[] { new[] { 53, 57, 60, 64 }, new[] { 52, 55, 59, 62 }, new[] { 50, 53, 57, 60 }, new[] { 48, 52, 55, 59 } };
                    bar = 3.5f; bellsPerBar = 2; bellAmp = 0.14f; padAmp = 0.22f;
                    break;
                default: // spooky: Am  Fm  Am  E (minor drone)
                    chords = new[] { new[] { 45, 48, 52 }, new[] { 41, 44, 48 }, new[] { 45, 48, 52 }, new[] { 40, 44, 47 } };
                    bar = 4f; bellsPerBar = 1; bellAmp = 0.1f; padAmp = 0.3f;
                    break;
            }

            float total = bar * chords.Length;
            var b = Buf(total);
            float ov = bar * 0.25f;

            for (int c = 0; c < chords.Length; c++)
            {
                float start = c * bar - ov;
                float win = bar + 2f * ov;
                int[] chord = chords[c];
                for (int k = 0; k < chord.Length; k++)
                {
                    float f = Midi(chord[k]);
                    AddPadNote(b, start, win, f, padAmp * 0.5f, 1.003f);
                    AddPadNote(b, start, win, f, padAmp * 0.5f, 0.997f);
                }
                AddPadNote(b, start, win, Midi(chord[0] - 12), padAmp * (style == 3 ? 0.9f : 0.6f), 1f);

                for (int i = 0; i < bellsPerBar; i++)
                {
                    float t = c * bar + i * (bar / bellsPerBar);
                    int note = chord[(i + rng.Next(2)) % chord.Length] + 12;
                    if (style == 3) note = chord[rng.Next(chord.Length)] + (rng.Next(3) == 0 ? 13 : 12); // slight dissonance
                    float amp = bellAmp * (i == 0 ? 1f : 0.75f);
                    AddBell(b, t, Midi(note), amp, style == 3 ? 0.9f : 0.5f, true);
                }
            }
            Normalize(b, style == 3 ? 0.4f : 0.45f);
            return b;
        }

        /// <summary>Soft sine pad note with sin² window (wraps around the buffer).</summary>
        private static void AddPadNote(float[] b, float start, float win, float freq, float amp, float detune)
        {
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int n = Mathf.Min(b.Length, Mathf.RoundToInt(win * SampleRate));
            if (n <= 0) return;
            // Rotating phasors (double precision) instead of per-sample Sin calls: music loops are long.
            double w = 2.0 * System.Math.PI * freq * detune / SampleRate;
            double ci = System.Math.Cos(w), si = System.Math.Sin(w);
            double s = 0.0, c = 1.0;                       // oscillator sin/cos
            double wv = System.Math.PI / n;
            double cwi = System.Math.Cos(wv), swi = System.Math.Sin(wv);
            double ws = 0.0, wc = 1.0;                     // window sin/cos
            for (int i = 0; i < n; i++)
            {
                int idx = (s0 + i) % b.Length;
                if (idx < 0) idx += b.Length;
                double osc = s + 0.3 * s * c;              // sin x + 0.15 * sin 2x
                b[idx] += (float)(amp * ws * ws * osc);
                double ns = s * ci + c * si;
                c = c * ci - s * si;
                s = ns;
                double nws = ws * cwi + wc * swi;
                wc = wc * cwi - ws * swi;
                ws = nws;
            }
        }

        // ------------------------------------------------------------------ DSP helpers

        private enum Filt { Low, Band, High }

        /// <summary>Chamberlin state-variable filter. q = damping (1/Q).</summary>
        private struct Svf
        {
            private float _low, _band;
            public float Low, Band, High;

            public void Process(float x, float fc, float q)
            {
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(fc, SampleRate * 0.16f) / SampleRate);
                _low += f * _band;
                float high = x - _low - q * _band;
                _band += f * high;
                Low = _low;
                Band = _band;
                High = high;
            }
        }

        private static float[] Buf(float seconds) => new float[Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate))];

        private static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        private static float Noise(System.Random rng) => (float)rng.NextDouble() * 2f - 1f;

        private static float OnePole(float fc) => 1f - Mathf.Exp(-TwoPi * fc / SampleRate);

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float Semi(int semitones) => Mathf.Pow(2f, semitones / 12f);

        private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        /// <summary>Linear attack, exponential decay (tau = decay seconds).</summary>
        private static float AD(float t, float attack, float decay)
        {
            if (t < 0f) return 0f;
            if (t < attack) return t / attack;
            return Mathf.Exp(-(t - attack) / decay);
        }

        /// <summary>Damped sine with optional downward pitch drop (thumps) and optional wrap-around (loops).</summary>
        private static void AddPing(float[] b, float start, float freq, float amp, float decay,
            float attack = 0.002f, float drop = 0f, float dropTime = 0.03f, bool wrap = false)
        {
            if (freq > MaxPartialHz) return;
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int n = Mathf.CeilToInt((attack + decay * 7f) * SampleRate);
            if (wrap) n = Mathf.Min(n, b.Length);
            attack = Mathf.Max(attack, 1e-4f);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                int idx = s0 + i;
                if (wrap)
                {
                    idx %= b.Length;
                    if (idx < 0) idx += b.Length;
                }
                else if (idx < 0) continue;
                else if (idx >= b.Length) break;

                float t = i / (float)SampleRate;
                float f = drop != 0f ? freq * (1f + drop * Mathf.Exp(-t / dropTime)) : freq;
                phase += f / SampleRate;
                if (phase >= 1.0) phase -= 1.0;
                b[idx] += amp * AD(t, attack, decay) * Mathf.Sin((float)phase * TwoPi);
            }
        }

        /// <summary>Soft glockenspiel-ish bell (bar partials 1, 2, 2.76, 5.4).</summary>
        private static void AddBell(float[] b, float start, float freq, float amp, float decay, bool wrap = false)
        {
            AddPing(b, start, freq, amp, decay, 0.001f, 0f, 0.03f, wrap);
            AddPing(b, start, freq * 2f, amp * 0.2f, decay * 0.5f, 0.001f, 0f, 0.03f, wrap);
            AddPing(b, start, freq * 2.76f, amp * 0.12f, decay * 0.3f, 0.001f, 0f, 0.03f, wrap);
            AddPing(b, start, freq * 5.4f, amp * 0.05f, decay * 0.15f, 0.001f, 0f, 0.03f, wrap);
        }

        /// <summary>Filtered noise burst with AD envelope; optional linear cutoff sweep to fcEnd.</summary>
        private static void AddNoise(float[] b, System.Random rng, float start, float attack, float decay,
            Filt mode, float fc, float q, float amp, float fcEnd = -1f)
        {
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int n = Mathf.CeilToInt((attack + decay * 7f) * SampleRate);
            attack = Mathf.Max(attack, 1e-4f);
            var svf = new Svf();
            for (int i = 0; i < n; i++)
            {
                int idx = s0 + i;
                if (idx >= b.Length) break;
                if (idx < 0) continue;
                float t = i / (float)SampleRate;
                float c = fcEnd > 0f ? Mathf.Lerp(fc, fcEnd, i / (float)n) : fc;
                svf.Process(Noise(rng), c, q);
                float y = mode == Filt.Low ? svf.Low : mode == Filt.Band ? svf.Band : svf.High;
                b[idx] += amp * AD(t, attack, decay) * y;
            }
        }

        /// <summary>Soft harmonic tone (1/k partials, one-pole lowpassed) with exponential glide and optional vibrato.</summary>
        private static void AddTone(float[] b, float start, float dur, float f0, float f1, float amp, int harmonics,
            float attack, float release, float lpHz, float vibRate = 0f, float vibDepth = 0f)
        {
            int s0 = Mathf.RoundToInt(start * SampleRate);
            int n = Mathf.RoundToInt(dur * SampleRate);
            double phase = 0;
            float lp = 0f, a = OnePole(lpHz);
            float ratio = f1 / f0;
            attack = Mathf.Max(attack, 1e-4f);
            release = Mathf.Max(release, 1e-4f);
            for (int i = 0; i < n; i++)
            {
                int idx = s0 + i;
                if (idx >= b.Length) break;
                if (idx < 0) continue;
                float t = i / (float)SampleRate;
                float u = t / dur;
                float f = f0 * Mathf.Pow(ratio, u);
                if (vibDepth > 0f) f *= 1f + vibDepth * Mathf.Sin(TwoPi * vibRate * t) * Mathf.Clamp01(u * 2f);
                phase += f / SampleRate;
                if (phase >= 1.0) phase -= 1.0;
                float ph = (float)phase * TwoPi;
                float x = 0f;
                for (int k = 1; k <= harmonics; k++)
                {
                    if (k * f > MaxPartialHz) break;
                    x += Mathf.Sin(ph * k) / k;
                }
                lp += a * (x - lp);
                float env = Mathf.Min(1f, t / attack) * Mathf.Clamp01((dur - t) / release);
                b[idx] += amp * env * lp;
            }
        }

        private static float[] Finish(float[] b, float peak, float fadeIn, float fadeOut)
        {
            int len = b.Length;
            int fi = Mathf.Min(len, Mathf.CeilToInt(fadeIn * SampleRate));
            int fo = Mathf.Min(len, Mathf.CeilToInt(fadeOut * SampleRate));
            for (int i = 0; i < fi; i++) b[i] *= i / (float)fi;
            for (int i = 0; i < fo; i++) b[len - 1 - i] *= i / (float)fo;
            Normalize(b, peak);
            return b;
        }

        private static void Normalize(float[] b, float peak)
        {
            float m = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float a = b[i] < 0f ? -b[i] : b[i];
                if (a > m) m = a;
            }
            if (m < 1e-6f) return;
            float s = peak / m;
            for (int i = 0; i < b.Length; i++) b[i] *= s;
        }

        private static bool AllAlive(AudioClip[] clips)
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] == null) return false;
            return true;
        }

        private static int Seed(string name, int variant)
        {
            unchecked
            {
                int h = -2128831035; // FNV-1a offset basis
                for (int i = 0; i < name.Length; i++) h = (h ^ name[i]) * 16777619;
                return h ^ (variant * 7919 + 17);
            }
        }
    }
}
