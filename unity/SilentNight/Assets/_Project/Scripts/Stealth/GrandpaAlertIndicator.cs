using UnityEngine;
using SilentNight.Core;

namespace SilentNight.Stealth
{
    /// <summary>
    /// World-space readability marker above Grandpa's head, built at runtime from TextMeshes:
    /// "Zzz" (bobbing) asleep, "…" stirring, "?" suspicious, "??" searching, "!" chasing,
    /// plus a short Russian word for ~2 s saying WHAT he just heard. Billboards toward Camera.main (yaw only).
    /// Text is only reassigned on change; no per-frame allocations.
    /// </summary>
    [DisallowMultipleComponent]
    public class GrandpaAlertIndicator : MonoBehaviour
    {
        private const float HeadClearance = 0.55f;
        private const float MarkHeight = 0.55f;   // metres per text line, reads from ~6 m
        private const float WordHeight = 0.24f;
        private const int FontSize = 96;
        private const float DefaultWordTime = 2f;
        private const float WordFadeTime = 0.4f;

        private static readonly string[] MarkTexts = { "Zzz", "…", "?", "??", "!" };
        private static readonly Color[] MarkColors =
        {
            new Color(0.62f, 0.80f, 1.00f), // Asleep: calm blue
            new Color(0.85f, 0.95f, 0.70f), // Stirring: pale green-yellow
            new Color(1.00f, 0.90f, 0.30f), // Suspicious: yellow
            new Color(1.00f, 0.58f, 0.15f), // Searching: orange
            new Color(1.00f, 0.18f, 0.12f)  // Chasing: red
        };
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.65f);

        private static Font _font;

        private Grandpa _owner;
        private Transform _billboard;
        private TextMesh _mark, _markShadow, _word, _wordShadow;
        private AlertLevel _shownLevel;
        private bool _levelShown;
        private Color _markColor;
        private Color _markTarget;
        private float _pop;
        private float _time;

        private string _wordText;
        private float _wordTimer;
        private float _wordLoudness;
        private Color _wordColor;

        /// <summary>Creates the indicator for a grandpa (as a separate root object so his scale/pose never distorts it).</summary>
        public static GrandpaAlertIndicator Create(Grandpa owner)
        {
            var go = new GameObject("GrandpaAlertIndicator");
            var ind = go.AddComponent<GrandpaAlertIndicator>();
            ind.Build(owner);
            return ind;
        }

        /// <summary>Short Russian word for what Grandpa heard.</summary>
        public static string HeardWord(NoiseSource source)
        {
            switch (source)
            {
                case NoiseSource.Footstep: return "топ-топ?";
                case NoiseSource.Landing: return "бум?";
                case NoiseSource.CreakyFloor: return "скрип?";
                case NoiseSource.GiftImpact: return "бах?";
                case NoiseSource.NoisyGift: return "дзынь?";
                case NoiseSource.Jetpack: return "что за гул?";
                case NoiseSource.Cat: return "кот?!";
                case NoiseSource.Object: return "что-то упало?";
                default: return "а?";
            }
        }

        /// <summary>Noise reached Grandpa. Louder (or same-type) sounds refresh the word; quiet ones don't override a loud one.</summary>
        public void ShowHeard(NoiseSource source, float heardLoudness)
        {
            string w = HeardWord(source);
            bool showing = _wordTimer > 0f;
            if (showing && !ReferenceEquals(w, _wordText) && heardLoudness < _wordLoudness * 0.75f && _wordTimer > WordFadeTime)
                return;
            ShowWord(w, DefaultWordTime);
            _wordLoudness = Mathf.Max(heardLoudness, showing ? _wordLoudness * 0.5f : 0f);
        }

        /// <summary>Shows an arbitrary short line next to the mark.</summary>
        public void ShowWord(string text, float seconds)
        {
            if (_word == null || string.IsNullOrEmpty(text)) return;
            if (!ReferenceEquals(text, _wordText))
            {
                _wordText = text;
                _word.text = text;
                _wordShadow.text = text;
                _pop = Mathf.Max(_pop, 0.5f);
            }
            _wordTimer = Mathf.Max(0.1f, seconds);
            _wordLoudness = 0f;
        }

        private void Build(Grandpa owner)
        {
            _owner = owner;
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _billboard = new GameObject("Billboard").transform;
            _billboard.SetParent(transform, false);

            _markShadow = MakeText("MarkShadow", TextAnchor.LowerCenter, MarkHeight, new Vector3(0.03f, -0.03f, 0.02f));
            _mark = MakeText("Mark", TextAnchor.LowerCenter, MarkHeight, Vector3.zero);
            _wordShadow = MakeText("WordShadow", TextAnchor.LowerLeft, WordHeight, new Vector3(0.32f, 0.05f, 0.02f));
            _word = MakeText("Word", TextAnchor.LowerLeft, WordHeight, new Vector3(0.30f, 0.07f, 0f));

            _markShadow.color = ShadowColor;
            _word.text = string.Empty;
            _wordShadow.text = string.Empty;
            SetWordVisible(false);

            _markColor = MarkColors[0];
            _markTarget = _markColor;
            ApplyLevel(owner != null ? owner.Level : AlertLevel.Asleep, true);
            Place();
        }

        private TextMesh MakeText(string name, TextAnchor anchor, float lineHeight, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_billboard, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.fontSize = FontSize;
            // TextMesh line height in world units ~= fontSize * characterSize / 10.
            tm.characterSize = lineHeight * 10f / FontSize;
            tm.anchor = anchor;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.richText = false;
            tm.text = string.Empty;
            var mr = go.GetComponent<MeshRenderer>();
            if (_font != null) mr.sharedMaterial = _font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return tm;
        }

        private void ApplyLevel(AlertLevel level, bool instant)
        {
            int i = Mathf.Clamp((int)level, 0, MarkTexts.Length - 1);
            _shownLevel = level;
            _levelShown = true;
            _mark.text = MarkTexts[i];
            _markShadow.text = MarkTexts[i];
            _markTarget = MarkColors[i];
            if (instant) { _markColor = _markTarget; _mark.color = _markColor; }
            else _pop = 1f;
        }

        private void SetWordVisible(bool on)
        {
            if (_word == null) return;
            var a = _word.GetComponent<MeshRenderer>();
            var b = _wordShadow.GetComponent<MeshRenderer>();
            if (a.enabled != on) a.enabled = on;
            if (b.enabled != on) b.enabled = on;
        }

        private void LateUpdate()
        {
            if (_owner == null) { Destroy(gameObject); return; }

            bool active = _owner.isActiveAndEnabled;
            if (_billboard.gameObject.activeSelf != active) _billboard.gameObject.SetActive(active);
            if (!active) return;

            float dt = Time.deltaTime;
            _time += dt;

            AlertLevel lvl = _owner.Level;
            if (!_levelShown || lvl != _shownLevel) ApplyLevel(lvl, false);

            if (_markColor != _markTarget)
            {
                _markColor = Color.Lerp(_markColor, _markTarget, Mathf.Clamp01(dt * 6f));
                if (Mathf.Abs(_markColor.r - _markTarget.r) + Mathf.Abs(_markColor.g - _markTarget.g) + Mathf.Abs(_markColor.b - _markTarget.b) < 0.01f)
                    _markColor = _markTarget;
                _mark.color = _markColor;
            }

            if (_wordTimer > 0f)
            {
                _wordTimer -= dt;
                _wordLoudness = Mathf.MoveTowards(_wordLoudness, 0f, 10f * dt);
                if (_wordTimer <= 0f)
                {
                    _wordTimer = 0f;
                    SetWordVisible(false);
                }
                else
                {
                    SetWordVisible(true);
                    float a = Mathf.Clamp01(_wordTimer / WordFadeTime);
                    Color c = Color.Lerp(Color.white, _markColor, 0.35f);
                    c.a = a;
                    if (c != _wordColor) { _wordColor = c; _word.color = c; }
                    Color s = ShadowColor;
                    s.a *= a;
                    _wordShadow.color = s;
                }
            }

            _pop = Mathf.MoveTowards(_pop, 0f, dt * 3f);
            Place();
        }

        private void Place()
        {
            if (_owner == null) return;
            Vector3 pos = _owner.IndicatorAnchor + Vector3.up * HeadClearance;
            float scale = 1f + 0.35f * _pop;

            switch (_shownLevel)
            {
                case AlertLevel.Asleep:
                    // Slow dreamy bob and sway.
                    pos.y += Mathf.Sin(_time * 1.6f) * 0.08f;
                    pos.x += Mathf.Sin(_time * 0.8f) * 0.04f;
                    scale *= 0.85f;
                    break;
                case AlertLevel.Stirring:
                    pos.y += Mathf.Sin(_time * 2.5f) * 0.03f;
                    break;
                case AlertLevel.Chasing:
                    scale *= 1.1f + Mathf.Abs(Mathf.Sin(_time * 9f)) * 0.12f;
                    break;
            }
            transform.position = pos;
            _billboard.localScale = new Vector3(scale, scale, scale);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 dir = pos - cam.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }
    }
}
