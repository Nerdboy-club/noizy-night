using UnityEngine;

namespace SilentNight.FX
{
    /// <summary>
    /// Looping, cheap (max 40 particles) grey-white smoke rising from a chimney top with a gentle wind sway.
    /// Put it on (or under) a transform at the chimney mouth; smoke always rises along world +Y.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChimneySmoke : MonoBehaviour
    {
        private const int MaxParticles = 40;

        [Tooltip("Puffs per second (clamped so rate * lifetime stays under the particle cap).")]
        [Range(0.5f, 10f)] public float rate = 6f;

        [Min(0.05f)] public float scale = 1f;
        [Min(0f)] public float riseSpeed = 0.45f;

        [Tooltip("World-space wind direction (Y is ignored).")]
        public Vector3 windDirection = new Vector3(1f, 0f, 0.3f);
        [Min(0f)] public float windStrength = 0.35f;
        [Min(0f)] public float swayAmplitude = 0.2f;
        [Min(0.01f)] public float swayFrequency = 0.35f;

        public Color colorA = new Color(0.76f, 0.76f, 0.79f, 1f);
        public Color colorB = new Color(0.96f, 0.96f, 0.98f, 1f);

        [Tooltip("Fill the column immediately on scene start.")]
        public bool prewarm = true;

        private ParticleSystem _ps;
        private float _phase;
        private Vector3 _windDir;
        private Vector3 _swayDir;

        public ParticleSystem Particles => _ps;

        private void Awake()
        {
            _phase = Random.value * 10f;
            Build();
        }

        private void OnEnable()
        {
            if (_ps != null) _ps.Play(false);
        }

        private void OnDisable()
        {
            if (_ps != null) _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>Turns emission on/off (live puffs fade out naturally).</summary>
        public void SetEmitting(bool on)
        {
            if (_ps == null) return;
            if (on) { if (!_ps.isEmitting) _ps.Play(false); }
            else _ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>Changes puffs per second at runtime (e.g. thicker smoke after an elf dives in).</summary>
        public void SetRate(float puffsPerSecond)
        {
            rate = puffsPerSecond;
            if (_ps == null) return;
            var emission = _ps.emission;
            emission.rateOverTime = ClampedRate();
        }

        private float ClampedRate()
        {
            const float maxLifetime = 5.5f;
            return Mathf.Clamp(rate, 0f, (MaxParticles - 2) / maxLifetime);
        }

        private void Update()
        {
            if (_ps == null) return;
            float t = Time.time * swayFrequency * (Mathf.PI * 2f) + _phase;
            float sway = Mathf.Sin(t) * swayAmplitude + Mathf.Sin(t * 0.37f + 1.3f) * swayAmplitude * 0.5f;
            Vector3 w = _windDir * windStrength + _swayDir * sway;

            var vel = _ps.velocityOverLifetime;
            vel.xMultiplier = w.x * scale;
            vel.zMultiplier = w.z * scale;
        }

        private void Build()
        {
            if (_ps != null) return;

            Vector3 flat = new Vector3(windDirection.x, 0f, windDirection.z);
            _windDir = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.right;
            _swayDir = new Vector3(-_windDir.z, 0f, _windDir.x);

            float s = scale;
            _ps = FxUtil.CreateSystem("ChimneySmoke", transform, FxUtil.AlphaMaterial, MaxParticles);

            var main = _ps.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = prewarm;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f * s, 0.3f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f * s, 0.55f * s);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);

            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = ClampedRate();

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.12f * s;
            shape.rotation = new Vector3(-90f, 0f, 0f); // cone axis +Z -> local +Y

            // All three axes in Curve mode (mixing modes logs an error).
            // X/Z ramp up with age so smoke bends more with the wind as it rises; Y is a flat lift.
            AnimationCurve ramp = FxUtil.Curve(0f, 0.1f, 1f, 1f);
            AnimationCurve flatCurve = FxUtil.Curve(0f, 1f, 1f, 1f);
            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(_windDir.x * windStrength * s, ramp);
            vel.y = new ParticleSystem.MinMaxCurve(riseSpeed * s, flatCurve);
            vel.z = new ParticleSystem.MinMaxCurve(_windDir.z * windStrength * s, ramp);

            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, FxUtil.Curve(0f, 0.5f, 1f, 2.2f));

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FxUtil.AlphaFade(0f, 0f, 0.12f, 0.55f, 0.7f, 0.35f, 1f, 0f));

            var r = _ps.GetComponent<ParticleSystemRenderer>();
            r.sortingFudge = 10f; // draw behind nearer transparent FX
            r.maxParticleSize = 1.5f;
        }
    }
}
