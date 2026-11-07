using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.UI
{
    /// <summary>Stealth title for the end-of-run recap.</summary>
    public enum StealthRank { QuietAsMouse, AlmostNinja, BullInChinaShop }

    /// <summary>
    /// Static per-run statistics collector for the comedic Results recap.
    /// Listens to GameEvents only (no scene references). A run starts when the state goes
    /// Hub/MainMenu -> Rooftop/Delivery; events are ignored while in Hub, MainMenu, Results or GameOver.
    /// </summary>
    public static class RunStats
    {
        public const float LoudThreshold = 45f;
        public const float CatYowlThreshold = 55f;
        public const float FallThreshold = 40f;

        private static readonly int SourceCount = System.Enum.GetValues(typeof(NoiseSource)).Length;
        private static readonly int[] _loudBySource = new int[SourceCount];
        private static readonly HashSet<ICarryable> _damaged = new HashSet<ICarryable>();
        private static readonly HashSet<ICarryable> _broken = new HashSet<ICarryable>();

        private static GameState _state = GameState.Boot;
        private static AlertLevel _lastAlert = AlertLevel.Asleep;
        private static bool _inHouse;
        private static float _houseEnterTime;
        private static float _houseSeconds;

        // ------------------------------------------------------------------ public read-only stats

        public static int LoudNoises { get; private set; }
        public static float LoudestNoise { get; private set; }
        public static NoiseSource LoudestSource { get; private set; }
        public static int GiftsDamaged => _damaged.Count;
        public static int GiftsBroken => _broken.Count;
        public static int SnowGlobesBroken { get; private set; }
        public static AlertLevel MaxAlert { get; private set; }
        public static int GrandpaWakes { get; private set; }
        public static int CatYowls { get; private set; }
        public static int ElfFalls { get; private set; }
        public static bool WasCaught { get; private set; }

        /// <summary>Seconds spent inside the house (Delivery, pauses excluded since Time.time stops at timeScale 0).</summary>
        public static float SecondsInHouse => _houseSeconds + (_inHouse ? Mathf.Max(0f, Time.time - _houseEnterTime) : 0f);

        /// <summary>Loud noises (>= LoudThreshold) made by one source.</summary>
        public static int LoudNoisesFrom(NoiseSource source)
        {
            int i = (int)source;
            return i >= 0 && i < _loudBySource.Length ? _loudBySource[i] : 0;
        }

        public static StealthRank Rank
        {
            get
            {
                if (GrandpaWakes == 0 && LoudNoises == 0 && !WasCaught) return StealthRank.QuietAsMouse;
                if (LoudNoises >= 6 || GrandpaWakes >= 3 || MaxAlert >= AlertLevel.Chasing || GiftsBroken >= 2 || WasCaught)
                    return StealthRank.BullInChinaShop;
                return StealthRank.AlmostNinja;
            }
        }

        public static string RankTitle => TitleFor(Rank);

        public static string TitleFor(StealthRank rank)
        {
            switch (rank)
            {
                case StealthRank.QuietAsMouse: return "Тише мыши";
                case StealthRank.AlmostNinja: return "Почти ниндзя";
                default: return "Слон в посудной лавке";
            }
        }

        // ------------------------------------------------------------------ lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Init()
        {
            _state = GameState.Boot;
            Reset();
            Subscribe();
        }

        /// <summary>(Re)subscribes to GameEvents. Idempotent: safe to call after GameEvents.ResetAll().</summary>
        public static void Subscribe()
        {
            Unsubscribe();
            GameEvents.Noise += OnNoise;
            GameEvents.GiftDamaged += OnGiftDamaged;
            GameEvents.GrandpaAlertChanged += OnAlert;
            GameEvents.PlayerCaught += OnCaught;
            GameEvents.StateChanged += OnStateChanged;
        }

        public static void Unsubscribe()
        {
            GameEvents.Noise -= OnNoise;
            GameEvents.GiftDamaged -= OnGiftDamaged;
            GameEvents.GrandpaAlertChanged -= OnAlert;
            GameEvents.PlayerCaught -= OnCaught;
            GameEvents.StateChanged -= OnStateChanged;
        }

        /// <summary>Clears all counters (called automatically at the start of a run).</summary>
        public static void Reset()
        {
            for (int i = 0; i < _loudBySource.Length; i++) _loudBySource[i] = 0;
            _damaged.Clear();
            _broken.Clear();
            LoudNoises = 0;
            LoudestNoise = 0f;
            LoudestSource = NoiseSource.Footstep;
            SnowGlobesBroken = 0;
            MaxAlert = AlertLevel.Asleep;
            _lastAlert = AlertLevel.Asleep;
            GrandpaWakes = 0;
            CatYowls = 0;
            ElfFalls = 0;
            WasCaught = false;
            _inHouse = false;
            _houseEnterTime = 0f;
            _houseSeconds = 0f;
        }

        // ------------------------------------------------------------------ handlers

        private static bool Counting =>
            _state != GameState.Hub && _state != GameState.MainMenu &&
            _state != GameState.Results && _state != GameState.GameOver;

        private static void OnStateChanged(GameState prev, GameState next)
        {
            bool runStart = (next == GameState.Rooftop || next == GameState.Delivery) &&
                            (prev == GameState.Hub || prev == GameState.MainMenu);
            if (runStart) Reset();

            if (next == GameState.Delivery && prev != GameState.Paused && !_inHouse)
            {
                _inHouse = true;
                _houseEnterTime = Time.time;
            }
            else if (next != GameState.Delivery && next != GameState.Paused && _inHouse)
            {
                _houseSeconds += Mathf.Max(0f, Time.time - _houseEnterTime);
                _inHouse = false;
            }
            _state = next;
        }

        private static void OnNoise(NoiseEvent e)
        {
            if (!Counting) return;
            float l = e.Loudness;
            if (l > LoudestNoise)
            {
                LoudestNoise = l;
                LoudestSource = e.Source;
            }
            if (l >= LoudThreshold)
            {
                LoudNoises++;
                int i = (int)e.Source;
                if (i >= 0 && i < _loudBySource.Length) _loudBySource[i]++;
            }
            if (e.Source == NoiseSource.Cat && l >= CatYowlThreshold) CatYowls++;
            if (e.Source == NoiseSource.Landing && l >= FallThreshold) ElfFalls++;
        }

        private static void OnGiftDamaged(ICarryable gift, float integrity)
        {
            if (!Counting || gift == null) return;
            _damaged.Add(gift);
            if (integrity <= 0f && _broken.Add(gift) && gift.Kind == GiftKind.Fragile) SnowGlobesBroken++;
        }

        private static void OnAlert(AlertLevel level)
        {
            if (!Counting) return;
            if (_lastAlert == AlertLevel.Asleep && level != AlertLevel.Asleep) GrandpaWakes++;
            if (level > MaxAlert) MaxAlert = level;
            _lastAlert = level;
        }

        private static void OnCaught(string reason)
        {
            if (!Counting) return;
            WasCaught = true;
        }

        // ------------------------------------------------------------------ recap text

        /// <summary>3..maxLines comedic Russian recap lines picked from the current stats.</summary>
        public static List<string> BuildRecapLines(int maxLines = 5)
        {
            var lines = new List<string>(8);

            if (LoudestNoise >= LoudThreshold)
                lines.Add($"Самый громкий звук: {SourceName(LoudestSource)} ({Mathf.RoundToInt(LoudestNoise)})");
            else
                lines.Add("Громких звуков: ноль. Мыши завидуют");

            if (GrandpaWakes == 0)
                lines.Add("Дед так и не проснулся. Храпит до сих пор");
            else if (MaxAlert >= AlertLevel.Chasing)
                lines.Add($"Дед просыпался {Times(GrandpaWakes)} и даже гонялся с тапком");
            else
                lines.Add($"Дед просыпался {Times(GrandpaWakes)}");

            if (SnowGlobesBroken > 0) lines.Add($"Разбито снежных шаров: {SnowGlobesBroken}");
            else if (GiftsBroken > 0) lines.Add($"Разбито подарков: {GiftsBroken}");
            else if (GiftsDamaged > 0) lines.Add($"Помято подарков: {GiftsDamaged}. Скажем, что так задумано");

            if (CatYowls > 0) lines.Add($"Кот орал {Times(CatYowls)}");
            if (ElfFalls > 0) lines.Add($"Эльфы падали {Times(ElfFalls)}");
            if (LoudNoises >= 2) lines.Add($"Громких звуков всего: {LoudNoises}");

            int secs = Mathf.RoundToInt(SecondsInHouse);
            lines.Add(secs > 0 ? $"Времени в доме: {secs / 60}:{secs % 60:00}" : "Времени в доме: быстрее, чем дед моргнул");

            if (maxLines < 3) maxLines = 3;
            if (lines.Count > maxLines)
            {
                // Keep the time line as the closing beat.
                string last = lines[lines.Count - 1];
                lines.RemoveRange(maxLines - 1, lines.Count - (maxLines - 1));
                lines.Add(last);
            }
            return lines;
        }

        public static string SourceName(NoiseSource s)
        {
            switch (s)
            {
                case NoiseSource.Footstep: return "топот сапожек";
                case NoiseSource.Landing: return "бух эльфа об пол";
                case NoiseSource.CreakyFloor: return "скрип половицы";
                case NoiseSource.GiftImpact: return "бац подарка";
                case NoiseSource.NoisyGift: return "дзынь шкатулки";
                case NoiseSource.Jetpack: return "рёв джетпака";
                case NoiseSource.Cat: return "вопль кота";
                case NoiseSource.Object: return "грохот чего-то ценного";
                default: return "что-то непонятное";
            }
        }

        /// <summary>"1 раз", "2 раза", "5 раз".</summary>
        public static string Times(int n)
        {
            int m100 = n % 100, m10 = n % 10;
            bool few = m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14);
            return n + (few ? " раза" : " раз");
        }
    }
}
