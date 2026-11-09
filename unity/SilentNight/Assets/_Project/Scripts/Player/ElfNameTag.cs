using SilentNight.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilentNight.Player
{
    /// <summary>
    /// Co-op readability label above an elf: "Эльф N" in that elf's sweater colour, "Помогите! [E]" (pulsing red)
    /// while downed, and a small "<-> несём вместе" line while co-carrying. Only shown with more than one registered
    /// player (hidden in solo). Built at runtime from TextMeshes (same approach as GrandpaAlertIndicator) on a separate
    /// root object so the elf's layer passes / crouch scale / ragdoll never touch it. Follows the ragdoll pelvis when
    /// present, else the CharacterController root. Billboards to Camera.main. Text is only reassigned on state change.
    /// Self-ensuring: every ElfInput in a loaded scene gets one (scene-baked player 1 included).
    /// </summary>
    [DisallowMultipleComponent]
    public class ElfNameTag : MonoBehaviour
    {
        private const float StandClearance = 0.22f;  // above the controller top
        private const float LimpClearance = 0.45f;   // above the lying pelvis
        private const float HugeGiftExtra = 0.35f;   // a huge gift is held above the head
        private const float MainHeight = 0.22f;      // metres per line, reads from ~8 m
        private const float SubHeight = 0.13f;
        private const int FontSize = 96;
        private const float FollowSharpness = 12f;

        // Same table as ElfVisual.TunicColors (private there), brightened a little for night readability.
        private static readonly Color[] SweaterColors =
        {
            new Color(0.15f, 0.62f, 0.25f), // green
            new Color(0.80f, 0.16f, 0.16f), // red
            new Color(0.22f, 0.42f, 0.86f), // blue
            new Color(0.56f, 0.26f, 0.76f)  // purple
        };
        private static readonly string[] NameTexts = { "Эльф 1", "Эльф 2", "Эльф 3", "Эльф 4" };
        private const string HelpText = "Помогите! [E]";
        private const string CoCarryText = "<-> несём вместе";
        private static readonly Color HelpColorA = new Color(1.00f, 0.15f, 0.10f);
        private static readonly Color HelpColorB = new Color(1.00f, 0.55f, 0.45f);
        private static readonly Color CoCarryColor = new Color(1.00f, 0.95f, 0.80f);
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.65f);

        private static Font _font;
        private static int s_knownElfCount = -1;
        private static int s_lastEnsureFrame = -1;

        private enum State { Hidden, Name, CoCarry, Downed }

        private ElfInput _input;
        private ElfVisual _visual;
        private ElfRagdoll _ragdoll;
        private ElfCarry _carry;

        private Transform _root;
        private Transform _billboard;
        private TextMesh _main, _mainShadow, _sub, _subShadow;
        private MeshRenderer _subRenderer, _subShadowRenderer;

        private State _state = State.Hidden;
        private bool _stateShown;
        private int _shownPlayerIndex = -1;
        private int _shownTheme = -1;
        private string _nameText;
        private Color _nameColor;
        private float _time;
        private float _followY;
        private bool _hasFollowY;

        // ---------------- self-ensure ----------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            s_knownElfCount = -1;
            s_lastEnsureFrame = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAutoEnsure()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureAll();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureAll();

        /// <summary>Adds a name tag to every live ElfInput that has none.</summary>
        public static void EnsureAll()
        {
            var all = ElfInput.All;
            for (int i = 0; i < all.Count; i++)
            {
                ElfInput e = all[i];
                if (e != null) Ensure(e.gameObject);
            }
            s_knownElfCount = all.Count;
        }

        /// <summary>Returns the elf's name tag, adding one if missing.</summary>
        public static ElfNameTag Ensure(GameObject elf)
        {
            if (elf == null) return null;
            ElfNameTag tag = elf.GetComponent<ElfNameTag>();
            if (tag == null) tag = elf.AddComponent<ElfNameTag>();
            return tag;
        }

        // ---------------- lifecycle ----------------

        private void Awake()
        {
            CacheComponents();
        }

        private void Start()
        {
            CacheComponents();
            if (_root == null) Build();
        }

        private void OnDisable()
        {
            // LateUpdate stops while disabled, so hide the (separate-root) label here.
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            _state = State.Hidden;
            _hasFollowY = false;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        private void CacheComponents()
        {
            if (_input == null) _input = GetComponent<ElfInput>();
            if (_visual == null) _visual = GetComponent<ElfVisual>();
            if (_ragdoll == null) _ragdoll = GetComponent<ElfRagdoll>();
            if (_carry == null) _carry = GetComponent<ElfCarry>();
        }

        private void Build()
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _root = new GameObject("ElfNameTag_" + gameObject.name).transform;
            _billboard = new GameObject("Billboard").transform;
            _billboard.SetParent(_root, false);

            _mainShadow = MakeText("MainShadow", MainHeight, new Vector3(0.018f, -0.018f, 0.02f));
            _main = MakeText("Main", MainHeight, Vector3.zero);
            _subShadow = MakeText("SubShadow", SubHeight, new Vector3(0.012f, MainHeight * 1.05f - 0.012f, 0.02f));
            _sub = MakeText("Sub", SubHeight, new Vector3(0f, MainHeight * 1.05f, 0f));
            _mainShadow.color = ShadowColor;
            _subShadow.color = ShadowColor;
            _subRenderer = _sub.GetComponent<MeshRenderer>();
            _subShadowRenderer = _subShadow.GetComponent<MeshRenderer>();

            _stateShown = false;
            _hasFollowY = false;
            _root.gameObject.SetActive(false);
        }

        private TextMesh MakeText(string objName, float lineHeight, Vector3 localPos)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(_billboard, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.fontSize = FontSize;
            tm.characterSize = lineHeight * 10f / FontSize; // line height ~= fontSize * characterSize / 10
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.richText = false;
            tm.text = string.Empty;
            var mr = go.GetComponent<MeshRenderer>();
            if (_font != null) mr.sharedMaterial = _font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return tm;
        }

        // ---------------- per frame ----------------

        private void LateUpdate()
        {
            // Elves that appeared without PlayerSetup (prefab instances etc.) get a tag too.
            int frame = Time.frameCount;
            if (s_lastEnsureFrame != frame)
            {
                s_lastEnsureFrame = frame;
                if (ElfInput.All.Count != s_knownElfCount) EnsureAll();
            }

            if (_root == null) Build(); // e.g. destroyed by a scene change while the elf persisted

            State target = EvaluateState();
            bool visible = target != State.Hidden;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible)
            {
                _state = State.Hidden;
                _hasFollowY = false;
                return;
            }

            RefreshIdentity();
            if (!_stateShown || target != _state) ApplyState(target);

            float dt = Time.deltaTime;
            _time += dt;
            if (_state == State.Downed)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(_time * 7f);
                _main.color = Color.Lerp(HelpColorA, HelpColorB, p);
                float s = 1f + 0.12f * p;
                _billboard.localScale = new Vector3(s, s, s);
            }

            Place(dt);
        }

        private State EvaluateState()
        {
            if (!isActiveAndEnabled) return State.Hidden;
            bool isCoop = GameServices.Players.Count > 1 ||
                          (SilentNight.Multiplayer.CoopNetworkManager.Instance != null &&
                           SilentNight.Multiplayer.CoopNetworkManager.Instance.Role != SilentNight.Multiplayer.CoopNetworkManager.NetRole.Offline);
            if (!isCoop) return State.Hidden;
            if (_ragdoll == null || _carry == null || _input == null || _visual == null) CacheComponents();
            if (_ragdoll != null && _ragdoll.IsDowned) return State.Downed;
            if (_carry != null && _carry.IsCoCarrying) return State.CoCarry;
            return State.Name;
        }

        /// <summary>Player slot / sweater colour can change after creation; rebuild the cached name only then.</summary>
        private void RefreshIdentity()
        {
            int idx = _input != null ? _input.PlayerIndex : 0;
            int theme = _visual != null ? _visual.ThemeIndex : 0;
            if (idx == 0 && theme > 0 && _input != null && !_input.IsNetworkRemote) idx = theme;
            if (idx == _shownPlayerIndex && theme == _shownTheme) return;
            _shownPlayerIndex = idx;
            _shownTheme = theme;
            _nameText = idx >= 0 && idx < NameTexts.Length ? NameTexts[idx] : "Эльф " + (idx + 1);
            Color c = SweaterColors[Mathf.Clamp(theme, 0, SweaterColors.Length - 1)];
            _nameColor = Color.Lerp(c, Color.white, 0.25f);
            _stateShown = false; // re-apply texts/colours
        }

        private void ApplyState(State s)
        {
            _state = s;
            _stateShown = true;
            switch (s)
            {
                case State.Downed:
                    SetMain(HelpText, HelpColorA);
                    SetSub(_nameText, _nameColor);
                    break;
                case State.CoCarry:
                    SetMain(_nameText, _nameColor);
                    SetSub(CoCarryText, CoCarryColor);
                    break;
                default:
                    SetMain(_nameText, _nameColor);
                    SetSub(null, CoCarryColor);
                    break;
            }
            if (s != State.Downed) _billboard.localScale = Vector3.one;
        }

        private void SetMain(string text, Color color)
        {
            if (!ReferenceEquals(_main.text, text)) { _main.text = text; _mainShadow.text = text; }
            _main.color = color;
        }

        private void SetSub(string text, Color color)
        {
            bool on = !string.IsNullOrEmpty(text);
            if (_subRenderer.enabled != on) _subRenderer.enabled = on;
            if (_subShadowRenderer.enabled != on) _subShadowRenderer.enabled = on;
            if (!on) return;
            if (!ReferenceEquals(_sub.text, text)) { _sub.text = text; _subShadow.text = text; }
            _sub.color = color;
        }

        private void Place(float dt)
        {
            Vector3 rootPos = transform.position;
            Transform pelvis = _ragdoll != null ? _ragdoll.PelvisTransform : null;
            Vector3 pos;
            float targetY;
            if (pelvis != null)
            {
                Vector3 p = pelvis.position;
                pos = new Vector3(p.x, 0f, p.z);
                targetY = _ragdoll.IsLimp
                    ? p.y + LimpClearance
                    : Mathf.Max(rootPos.y + PlayerSetup.ControllerHeight + StandClearance, p.y + LimpClearance);
            }
            else
            {
                pos = new Vector3(rootPos.x, 0f, rootPos.z);
                targetY = rootPos.y + PlayerSetup.ControllerHeight + StandClearance;
            }
            if (_carry != null && _carry.IsCarryingHuge) targetY += HugeGiftExtra;

            if (!_hasFollowY) { _followY = targetY; _hasFollowY = true; }
            else _followY = Mathf.Lerp(_followY, targetY, 1f - Mathf.Exp(-FollowSharpness * dt));
            pos.y = _followY;
            _root.position = pos;

            Camera cam = Camera.main;
            if (cam != null) _root.rotation = cam.transform.rotation; // screen-aligned: readable from the high co-op camera
        }
    }
}
