using UnityEngine;

namespace SilentNight.FX
{
    /// <summary>
    /// One-shot chunky cloud of dark soot puffs plus a few bright ember sparks
    /// (elf popping out of a fireplace / chimney). Built entirely in code.
    /// Use <see cref="Spawn(Vector3, float)"/> for fire-and-forget, or add the component and call <see cref="Play"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SootPuff : MonoBehaviour
    {
        [Tooltip("Play automatically in Start.")]
        public bool playOnStart = true;

        [Tooltip("Destroy this GameObject N seconds after Play. 0 = keep (re-playable).")]
        [Min(0f)] public float destroyAfter = 0f;

        [Tooltip("Uniform size / speed / radius multiplier.")]
        [Min(0.05f)] public float scale = 1f;

        [Range(4, 40)] public int puffCount = 18;
        [Range(0, 16)] public int emberCount = 6;

        private const int MaxSoot = 40;
        private const int MaxEmbers = 16;

        private ParticleSystem _soot;
        private ParticleSystem _embers;
        private bool _played;

        /// <summary>Spawns a temporary self-destroying soot puff at a world position.</summary>
        public static SootPuff Spawn(Vector3 position)
        {
            return Spawn(position, 1f);
        }

        /// <summary>Spawns a temporary self-destroying soot puff at a world position with a size multiplier.</summary>
        public static SootPuff Spawn(Vector3 position, float scale)
        {
            var go = new GameObject("SootPuff (temp)");
            go.transform.position = position;
            var puff = go.AddComponent<SootPuff>();
            puff.playOnStart = false;
            puff.scale = Mathf.Max(0.05f, scale);
            puff.destroyAfter = 2.5f;
            puff.Play();
            return puff;
        }

        private void Awake()
        {
            Build();
        }

        private void Start()
        {
            if (playOnStart && !_played) Play();
        }

        /// <summary>Emits the burst (can be called repeatedly; existing puffs keep fading out).</summary>
        public void Play()
        {
            Build();
            _played = true;
            float s = scale;

            // --- soot ---
            var main = _soot.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f * s, 0.4f * s);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * s, 1.4f * s);
            var shape = _soot.shape;
            shape.radius = 0.2f * s;
            var limit = _soot.limitVelocityOverLifetime;
            limit.limit = 0.3f * s;
            var vel = _soot.velocityOverLifetime;
            vel.x = 0f;
            vel.y = 0.35f * s;
            vel.z = 0f;
            var emission = _soot.emission;
            emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.Clamp(puffCount, 0, MaxSoot)));

            // --- embers ---
            var eMain = _embers.main;
            eMain.startSize = new ParticleSystem.MinMaxCurve(0.025f * s, 0.06f * s);
            eMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * s, 3.5f * s);
            var eShape = _embers.shape;
            eShape.radius = 0.1f * s;
            var eEmission = _embers.emission;
            eEmission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.Clamp(emberCount, 0, MaxEmbers)));

            _soot.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _embers.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _soot.Play(false);
            if (emberCount > 0) _embers.Play(false);

            if (destroyAfter > 0f) Destroy(gameObject, destroyAfter);
        }

        private void Build()
        {
            if (_soot != null) return;

            // ---------------- Soot puffs ----------------
            _soot = FxUtil.CreateSystem("Soot", transform, FxUtil.AlphaMaterial, MaxSoot);
            var main = _soot.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.06f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.07f, 0.07f, 0.08f, 1f),
                new Color(0.24f, 0.23f, 0.24f, 1f));

            var emission = _soot.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)18) });

            var shape = _soot.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.2f;
            shape.radiusThickness = 1f;

            // All three axes in Constant mode (mixing modes logs an error).
            var vel = _soot.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.35f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);

            var limit = _soot.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(0.3f);
            limit.dampen = 0.12f;

            var size = _soot.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, FxUtil.Curve(0f, 0.5f, 0.15f, 1f, 1f, 1.35f));

            var col = _soot.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FxUtil.AlphaFade(0f, 1f, 0.6f, 0.85f, 1f, 0f));

            // ---------------- Embers ----------------
            _embers = FxUtil.CreateSystem("Embers", transform, FxUtil.AdditiveMaterial, MaxEmbers);
            var eMain = _embers.main;
            eMain.duration = 0.1f;
            eMain.loop = false;
            eMain.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            eMain.gravityModifier = 0.8f;
            eMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.78f, 0.32f, 1f),
                new Color(1f, 0.42f, 0.08f, 1f));

            var eEmission = _embers.emission;
            eEmission.enabled = true;
            eEmission.rateOverTime = 0f;
            eEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)6) });

            var eShape = _embers.shape;
            eShape.enabled = true;
            eShape.shapeType = ParticleSystemShapeType.Hemisphere;
            eShape.radius = 0.1f;
            eShape.rotation = new Vector3(-90f, 0f, 0f); // hemisphere opens toward +Y

            var eSize = _embers.sizeOverLifetime;
            eSize.enabled = true;
            eSize.size = new ParticleSystem.MinMaxCurve(1f, FxUtil.Curve(0f, 1f, 1f, 0.3f));

            var eCol = _embers.colorOverLifetime;
            eCol.enabled = true;
            eCol.color = new ParticleSystem.MinMaxGradient(FxUtil.AlphaFade(0f, 1f, 0.7f, 0.9f, 1f, 0f));

            var eRenderer = _embers.GetComponent<ParticleSystemRenderer>();
            eRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            eRenderer.velocityScale = 0.05f;
            eRenderer.lengthScale = 1.5f;
        }
    }
}
