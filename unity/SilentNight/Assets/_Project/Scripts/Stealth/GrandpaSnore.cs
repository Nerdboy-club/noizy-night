using UnityEngine;
using SilentNight.Audio;

namespace SilentNight.Stealth
{
    /// <summary>
    /// Grandpa's audible snore: a looping 3D AudioSource (child of Grandpa) that the elf can hear from ~10 m.
    /// Owned and ticked by Grandpa (plain C# object, no allocations after creation).
    /// - Snoring: loops the "snore" clip, fades in when he (re)falls asleep.
    /// - Asleep -> Stirring: the snore breaks with a short comic pitch wobble.
    /// - Waking (sits up / gets out of bed / caught): a quick pitched-up "snort", then silence.
    /// - Gameplay inactive (pause, results): the source is paused, not restarted.
    /// </summary>
    internal sealed class GrandpaSnore
    {
        private const float FadeInTime = 0.8f;
        private const float StirVolumeMul = 0.8f;

        private const float WobbleTime = 0.7f;
        private const float WobbleDepth = 0.28f;   // ± pitch
        private const float WobbleRate = 22f;      // rad/s

        private const float SnortTime = 0.35f;
        private const float SnortPitchRise = 0.8f;
        private const float SnortSeekTime = 0.55f; // into the rattly inhale, so the snort is always audible

        private readonly AudioSource _src;
        private readonly float _volume;

        private bool _snoring;
        private bool _stirring;
        private bool _paused;
        private float _fade;
        private float _wobbleLeft;
        private float _wobbleT;
        private float _snortLeft;

        private GrandpaSnore(AudioSource src, float volume)
        {
            _src = src;
            _volume = volume;
        }

        /// <summary>Creates the snore source under <paramref name="owner"/>. Returns null if no snore clip is available.</summary>
        public static GrandpaSnore Create(Transform owner, float volume, float maxDistance)
        {
            if (owner == null) return null;
            AudioClip clip = ProceduralSfx.Get(AudioClips.Snore);
            if (clip == null) return null;

            var go = new GameObject("SnoreAudio");
            go.transform.SetParent(owner, false);
            go.transform.localPosition = new Vector3(0f, 0.6f, 0f);

            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.clip = clip;
            s.loop = true;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1f;
            s.maxDistance = Mathf.Max(1.5f, maxDistance);
            s.dopplerLevel = 0f;
            s.volume = 0f;
            s.pitch = 1f;
            s.priority = 64;
            return new GrandpaSnore(s, Mathf.Clamp01(volume));
        }

        /// <param name="wantSnore">He is lying in bed asleep or stirring.</param>
        /// <param name="stirring">He is stirring (snore breaks / quieter).</param>
        /// <param name="paused">Gameplay inactive: freeze the audio.</param>
        /// <param name="headPos">World position of his head (the snore follows the pose).</param>
        public void Tick(float dt, bool wantSnore, bool stirring, bool paused, Vector3 headPos)
        {
            if (_src == null) return;

            if (paused)
            {
                if (!_paused)
                {
                    _paused = true;
                    if (_src.isPlaying) _src.Pause();
                }
                return;
            }
            if (_paused)
            {
                _paused = false;
                if (_snoring || _snortLeft > 0f) _src.UnPause();
            }

            _src.transform.position = headPos;

            if (wantSnore && !_snoring) StartSnoring(stirring);
            else if (!wantSnore && _snoring) StartSnort();

            if (_snoring)
            {
                if (stirring && !_stirring) { _wobbleLeft = WobbleTime; _wobbleT = 0f; }
                _stirring = stirring;
                _fade = Mathf.MoveTowards(_fade, 1f, dt / FadeInTime);

                float pitch = 1f;
                if (_wobbleLeft > 0f)
                {
                    _wobbleLeft -= dt;
                    _wobbleT += dt;
                    float k = Mathf.Clamp01(_wobbleLeft / WobbleTime);
                    pitch += Mathf.Sin(_wobbleT * WobbleRate) * WobbleDepth * k;
                }
                _src.pitch = pitch;
                _src.volume = _volume * _fade * (_stirring ? StirVolumeMul : 1f);
            }
            else if (_snortLeft > 0f)
            {
                _snortLeft -= dt;
                float k = Mathf.Clamp01(_snortLeft / SnortTime); // 1 -> 0
                _src.pitch = 1f + (1f - k) * SnortPitchRise;
                _src.volume = _volume * k;
                if (_snortLeft <= 0f)
                {
                    _src.Stop();
                    _src.pitch = 1f;
                    _src.volume = 0f;
                }
            }
        }

        private void StartSnoring(bool stirring)
        {
            _snoring = true;
            _stirring = stirring;
            _snortLeft = 0f;
            _wobbleLeft = 0f;
            _fade = 0f;
            _src.pitch = 1f;
            _src.volume = 0f;
            if (!_src.isPlaying)
            {
                _src.time = 0f;
                _src.Play();
            }
        }

        private void StartSnort()
        {
            _snoring = false;
            _wobbleLeft = 0f;
            _snortLeft = SnortTime;
            if (!_src.isPlaying) _src.Play();
            AudioClip clip = _src.clip;
            if (clip != null && clip.length > SnortSeekTime + SnortTime) _src.time = SnortSeekTime;
        }
    }
}
