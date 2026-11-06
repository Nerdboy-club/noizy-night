using UnityEngine;

namespace SilentNight.Core
{
    /// <summary>A sound made in the world. Loudness is 0..100 at the source (dB-like scale from the original game).</summary>
    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;
        public readonly float Loudness;
        public readonly NoiseSource Source;
        public readonly GameObject Emitter;

        public NoiseEvent(Vector3 position, float loudness, NoiseSource source, GameObject emitter = null)
        {
            Position = position;
            Loudness = Mathf.Clamp(loudness, 0f, 100f);
            Source = source;
            Emitter = emitter;
        }

        /// <summary>Loudness heard at a listener position (linear falloff, 0 beyond ~loudness/6 metres).</summary>
        public float LoudnessAt(Vector3 listener)
        {
            float dist = Vector3.Distance(Position, listener);
            float range = Mathf.Max(1f, Loudness / 6f);
            return Loudness * Mathf.Clamp01(1f - dist / range);
        }
    }
}
