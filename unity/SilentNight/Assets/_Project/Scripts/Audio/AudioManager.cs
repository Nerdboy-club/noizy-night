using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Audio
{
    /// <summary>Clip names loaded from Resources/Audio/&lt;name&gt; (optional variants &lt;name&gt;_1 … &lt;name&gt;_8).</summary>
    public static class AudioClips
    {
        public const string FootstepWood = "footstep_wood";
        public const string FootstepCarpet = "footstep_carpet";
        public const string Creak = "creak";
        public const string GiftThud = "gift_thud";
        public const string ToyTick = "toy_tick";
        public const string Jetpack = "jetpack";
        public const string Meow = "meow";
        public const string Knock = "knock";

        public const string MusicMenu = "music_menu";
        public const string MusicHub = "music_hub";
        public const string MusicHouseCozy = "music_house_cozy";
        public const string MusicHouseSpooky = "music_house_spooky";

        public const string StingerCaught = "stinger_caught";
        public const string StingerAlert = "stinger_alert";
        public const string JingleDelivered = "jingle_delivered";

        public const string GiftLoaded = "gift_loaded";
        public const string GiftDelivered = "gift_delivered";
        public const string UiOpen = "ui_open";

        // Finer-grained names. If a file for one of these is missing but its parent (see Parent) exists,
        // the parent's files are used, so real assets stay consistent; otherwise ProceduralSfx fills in.
        public const string FootstepCreaky = "footstep_creaky";
        public const string FootstepTile = "footstep_tile";
        public const string FootstepIce = "footstep_ice";
        public const string Landing = "landing";
        public const string GiftSmash = "gift_smash";
        public const string ToyAlarm = "toy_alarm";
        public const string Yowl = "yowl";
        public const string UiClick = "ui_click";
        public const string Snore = "snore";

        /// <summary>Fallback clip name for the finer-grained names above (null if none).</summary>
        public static string Parent(string name)
        {
            switch (name)
            {
                case FootstepCreaky:
                case FootstepTile:
                case FootstepIce:
                case Landing: return FootstepWood;
                case GiftSmash: return GiftThud;
                case ToyAlarm: return ToyTick;
                case Yowl: return Meow;
                case UiClick: return UiOpen;
                default: return null;
            }
        }
    }

    /// <summary>
    /// Persistent audio singleton (Boot scene, DontDestroyOnLoad). Reacts only to GameEvents:
    /// 3D one-shots for every Noise, per-state music with crossfade, stingers and jingles.
    /// Missing clips fall back to ProceduralSfx placeholders (or silence for unknown names; logged once).
    /// Just AddComponent&lt;AudioManager&gt;().
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Volumes")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float stingerVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float pausedMusicDuck = 0.4f;

        [Header("Music")]
        [SerializeField, Min(0.05f)] private float musicCrossfadeSeconds = 1.5f;

        [Header("3D one-shots")]
        [SerializeField, Range(4, 48)] private int voiceCount = 16;
        [SerializeField] private float minDistance = 1.5f;
        [Tooltip("Audible max distance = max(minMaxDistance, loudness * maxDistancePerLoudness).")]
        [SerializeField] private float maxDistancePerLoudness = 0.35f;
        [SerializeField] private float minMaxDistance = 8f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.06f;
        [Tooltip("Loudness (0..100) that maps to full clip volume.")]
        [SerializeField] private float fullVolumeLoudness = 70f;

        [Header("Resources")]
        [SerializeField] private string resourcesFolder = "Audio";
        [SerializeField, Range(0, 8)] private int maxVariants = 8;
        [Tooltip("Synthesize soft placeholder clips (ProceduralSfx) for names with no file in Resources.")]
        [SerializeField] private bool useProceduralFallback = true;
        [Tooltip("Also synthesize placeholder music loops when no music file exists.")]
        [SerializeField] private bool proceduralMusic = true;

        // ---- runtime
        private readonly Dictionary<string, AudioClip[]> _cache = new Dictionary<string, AudioClip[]>();
        private readonly HashSet<string> _missingLogged = new HashSet<string>();
        private AudioSource[] _voices;
        private int _nextVoice;
        private AudioSource _ui;
        private readonly AudioSource[] _music = new AudioSource[2];
        private readonly float[] _musicWeight = new float[2];
        private readonly float[] _musicTarget = new float[2];
        private int _activeMusic;
        private string _currentMusic;
        private bool _paused;
        private AlertLevel _lastAlert = AlertLevel.Asleep;
        private readonly float[] _nextAllowed = new float[System.Enum.GetValues(typeof(NoiseSource)).Length];

        private static readonly float[] Cooldown =
        {
            0.07f, // Footstep
            0.10f, // Landing
            0.30f, // CreakyFloor
            0.08f, // GiftImpact
            0.25f, // NoisyGift
            0.22f, // Jetpack
            1.00f, // Cat
            0.10f  // Object
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent != null) transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            BuildSources();
        }

        private void OnEnable()
        {
            if (Instance != this) return;
            GameEvents.Noise += OnNoise;
            GameEvents.StateChanged += OnStateChanged;
            GameEvents.PlayerCaught += OnPlayerCaught;
            GameEvents.GrandpaAlertChanged += OnAlert;
            GameEvents.DeliveryCompleted += OnDeliveryCompleted;
            GameEvents.GiftLoaded += OnGiftLoaded;
            GameEvents.GiftDelivered += OnGiftDelivered;
            GameEvents.RequestHubPanel += OnHubPanel;
        }

        private void OnDisable()
        {
            GameEvents.Noise -= OnNoise;
            GameEvents.StateChanged -= OnStateChanged;
            GameEvents.PlayerCaught -= OnPlayerCaught;
            GameEvents.GrandpaAlertChanged -= OnAlert;
            GameEvents.DeliveryCompleted -= OnDeliveryCompleted;
            GameEvents.GiftLoaded -= OnGiftLoaded;
            GameEvents.GiftDelivered -= OnGiftDelivered;
            GameEvents.RequestHubPanel -= OnHubPanel;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            if (Instance != this) return;
            var flow = GameServices.Flow;
            if (flow != null) ApplyMusicForState(flow.State);
        }

        private void BuildSources()
        {
            _voices = new AudioSource[voiceCount];
            for (int i = 0; i < voiceCount; i++)
            {
                var go = new GameObject("Voice3D_" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = minDistance;
                s.maxDistance = minMaxDistance;
                s.dopplerLevel = 0f;
                _voices[i] = s;
            }

            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;

            for (int i = 0; i < 2; i++)
            {
                var m = gameObject.AddComponent<AudioSource>();
                m.playOnAwake = false;
                m.loop = true;
                m.spatialBlend = 0f;
                m.volume = 0f;
                m.priority = 0;
                _music[i] = m;
            }
        }

        private void Update()
        {
            // Music crossfade (unscaled: keeps working while paused with timeScale 0).
            float step = Time.unscaledDeltaTime / musicCrossfadeSeconds;
            float duck = _paused ? pausedMusicDuck : 1f;
            for (int i = 0; i < 2; i++)
            {
                var src = _music != null ? _music[i] : null;
                if (src == null) continue; // sources not created yet (or destroyed on scene change)
                _musicWeight[i] = Mathf.MoveTowards(_musicWeight[i], _musicTarget[i], step);
                src.volume = _musicWeight[i] * musicVolume * masterVolume * duck;
                if (_musicWeight[i] <= 0f && _musicTarget[i] <= 0f && src.isPlaying) src.Stop();
            }
        }

        // ------------------------------------------------------------------ public API

        /// <summary>2D one-shot (UI, stingers). Missing clip = silent.</summary>
        public void Play2D(string clipName, float volume01 = 1f)
        {
            var clip = PickClip(clipName);
            if (clip == null || _ui == null) return;
            _ui.PlayOneShot(clip, Mathf.Clamp01(volume01) * masterVolume);
        }

        /// <summary>3D positional one-shot from the voice pool.</summary>
        public void PlayAt(string clipName, Vector3 position, float volume01, float maxDistance)
        {
            var clip = PickClip(clipName);
            if (clip == null || _voices == null || _voices.Length == 0) return;
            var src = NextVoice();
            src.transform.position = position;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume01) * sfxVolume * masterVolume;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.minDistance = minDistance;
            src.maxDistance = Mathf.Max(minDistance + 0.5f, maxDistance);
            src.Play();
        }

        public void PlayMusic(string clipName)
        {
            if (clipName == _currentMusic) return;
            _currentMusic = clipName;
            AudioClip clip = string.IsNullOrEmpty(clipName) ? null : PickClip(clipName, firstOnly: true);

            int next = 1 - _activeMusic;
            _musicTarget[_activeMusic] = 0f;
            var src = _music[next];
            if (clip != null)
            {
                src.clip = clip;
                src.time = 0f;
                src.Play();
                _musicTarget[next] = 1f;
            }
            else
            {
                _musicTarget[next] = 0f;
            }
            _activeMusic = next;
        }

        // ------------------------------------------------------------------ event handlers

        private void OnNoise(NoiseEvent e)
        {
            int si = (int)e.Source;
            if (si < 0 || si >= _nextAllowed.Length) return;
            float now = Time.unscaledTime;
            if (now < _nextAllowed[si]) return;
            _nextAllowed[si] = now + (si < Cooldown.Length ? Cooldown[si] : 0.1f);

            string clip = ClipFor(e);
            if (clip == null) return;
            float vol = Mathf.Clamp(e.Loudness / Mathf.Max(1f, fullVolumeLoudness), 0.12f, 1f);
            float maxDist = Mathf.Max(minMaxDistance, e.Loudness * maxDistancePerLoudness);
            PlayAt(clip, e.Position, vol, maxDist);
        }

        // Loudness thresholds picking the "big" variant (Gift shatter = 82, alarm ring = 62, cat tail yowl = 65).
        private const float GiftSmashLoudness = 80f;
        private const float ToyAlarmLoudness = 50f;
        private const float YowlLoudness = 55f;

        private static string ClipFor(in NoiseEvent e)
        {
            switch (e.Source)
            {
                case NoiseSource.Footstep:
                    switch (LocalSurface(e.Emitter))
                    {
                        case SurfaceType.Carpet: return AudioClips.FootstepCarpet;
                        case SurfaceType.CreakyWood: return AudioClips.FootstepCreaky;
                        case SurfaceType.Tile: return AudioClips.FootstepTile;
                        case SurfaceType.Ice: return AudioClips.FootstepIce;
                        default: return AudioClips.FootstepWood;
                    }
                case NoiseSource.Landing: return AudioClips.Landing;
                case NoiseSource.CreakyFloor: return AudioClips.Creak;
                case NoiseSource.GiftImpact:
                    return e.Loudness >= GiftSmashLoudness ? AudioClips.GiftSmash : AudioClips.GiftThud;
                case NoiseSource.NoisyGift:
                    return e.Loudness >= ToyAlarmLoudness ? AudioClips.ToyAlarm : AudioClips.ToyTick;
                case NoiseSource.Jetpack: return AudioClips.Jetpack;
                case NoiseSource.Cat:
                    return e.Loudness >= YowlLoudness ? AudioClips.Yowl : AudioClips.Meow;
                case NoiseSource.Object: return AudioClips.Knock;
                default: return null;
            }
        }

        /// <summary>Surface under the local player if the noise came from them; otherwise Default (wood).</summary>
        private static SurfaceType LocalSurface(GameObject emitter)
        {
            var p = GameServices.LocalPlayer;
            if (p == null || (p is Object po && po == null)) return SurfaceType.Default;
            var t = p.Transform;
            if (t == null) return SurfaceType.Default;
            if (emitter != null && emitter.transform.root != t.root) return SurfaceType.Default;
            return p.CurrentSurface;
        }

        private void OnStateChanged(GameState prev, GameState next)
        {
            _paused = next == GameState.Paused;
            if (next == GameState.Delivery || next == GameState.Hub) _lastAlert = AlertLevel.Asleep;
            ApplyMusicForState(next);
        }

        private void ApplyMusicForState(GameState s)
        {
            switch (s)
            {
                case GameState.MainMenu: PlayMusic(AudioClips.MusicMenu); break;
                case GameState.Hub: PlayMusic(AudioClips.MusicHub); break;
                case GameState.Delivery:
                    var o = GameServices.ActiveOrder;
                    PlayMusic(o != null && o.Mood == HouseMood.Spooky ? AudioClips.MusicHouseSpooky : AudioClips.MusicHouseCozy);
                    break;
                case GameState.Results:
                case GameState.GameOver:
                    PlayMusic(null); // let the jingle / stinger breathe
                    break;
                // Paused: keep the current track, ducked in Update. Boot: nothing.
            }
        }

        private void OnPlayerCaught(string reason) => Play2D(AudioClips.StingerCaught, stingerVolume);

        private void OnAlert(AlertLevel level)
        {
            if (level == AlertLevel.Chasing && _lastAlert != AlertLevel.Chasing)
                Play2D(AudioClips.StingerAlert, stingerVolume);
            _lastAlert = level;
        }

        private void OnDeliveryCompleted(Order order, float integrity, int earned) =>
            Play2D(AudioClips.JingleDelivered, stingerVolume);

        private void OnGiftLoaded(int loaded, int total) => Play2D(AudioClips.GiftLoaded, 0.7f);

        private void OnGiftDelivered(ICarryable gift, int delivered, int total) => Play2D(AudioClips.GiftDelivered, 0.7f);

        private void OnHubPanel(string panel)
        {
            if (!string.IsNullOrEmpty(panel)) Play2D(AudioClips.UiOpen, 0.6f);
        }

        // ------------------------------------------------------------------ clips

        private AudioSource NextVoice()
        {
            // Prefer a free voice, otherwise steal round-robin.
            for (int i = 0; i < _voices.Length; i++)
            {
                int idx = (_nextVoice + i) % _voices.Length;
                if (!_voices[idx].isPlaying)
                {
                    _nextVoice = (idx + 1) % _voices.Length;
                    return _voices[idx];
                }
            }
            var v = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            v.Stop();
            return v;
        }

        private AudioClip PickClip(string clipName, bool firstOnly = false)
        {
            if (string.IsNullOrEmpty(clipName)) return null;
            if (!_cache.TryGetValue(clipName, out var clips))
            {
                clips = LoadClips(clipName);
                _cache[clipName] = clips;
            }
            if (clips.Length == 0) return null;
            return firstOnly || clips.Length == 1 ? clips[0] : clips[Random.Range(0, clips.Length)];
        }

        /// <summary>
        /// Lookup order: Resources/&lt;folder&gt;/&lt;name&gt; (+ _1.._N variants) → the parent name's files
        /// (AudioClips.Parent) → ProceduralSfx synthesized fallback → empty (silent).
        /// </summary>
        private AudioClip[] LoadClips(string clipName)
        {
            var list = new List<AudioClip>(4);
            LoadFromResources(clipName, list);

            string parent = list.Count == 0 ? AudioClips.Parent(clipName) : null;
            if (parent != null) LoadFromResources(parent, list);
            if (list.Count > 0) return list.ToArray();

            bool allowProcedural = useProceduralFallback && (proceduralMusic || !clipName.StartsWith("music_", System.StringComparison.Ordinal));
            var procedural = allowProcedural ? ProceduralSfx.GetVariants(clipName) : null;
            if (procedural != null && procedural.Length > 0)
            {
                if (_missingLogged.Add(clipName))
                    Debug.Log($"[AudioManager] No clip 'Resources/{ResourcePath(clipName)}' — using procedural fallback ({procedural.Length} variants).");
                return procedural;
            }

            if (_missingLogged.Add(clipName))
                Debug.Log($"[AudioManager] No clip 'Resources/{ResourcePath(clipName)}' (or _1.._{maxVariants}) — skipping. See Assets/_Project/Audio/README.md");
            return list.ToArray();
        }

        private string ResourcePath(string clipName) =>
            string.IsNullOrEmpty(resourcesFolder) ? clipName : resourcesFolder + "/" + clipName;

        private void LoadFromResources(string clipName, List<AudioClip> list)
        {
            string basePath = ResourcePath(clipName);
            var main = Resources.Load<AudioClip>(basePath);
            if (main != null) list.Add(main);
            for (int i = 1; i <= maxVariants; i++)
            {
                var v = Resources.Load<AudioClip>(basePath + "_" + i);
                if (v != null) list.Add(v);
            }
        }
    }
}
