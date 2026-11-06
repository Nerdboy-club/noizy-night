using System;
using SilentNight.Core;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace SilentNight.UI
{
    /// <summary>
    /// Persistent UI host (lives in the Boot scene, DontDestroyOnLoad).
    /// Creates a UIDocument + runtime PanelSettings, loads Resources/UI/Game.uss, builds every view in C#
    /// and switches them on GameEvents.StateChanged. Hub panels (orders/workbench) are modal:
    /// they unlock the cursor and set <see cref="IsModalOpen"/> which the player reads to ignore input.
    /// Just AddComponent&lt;UIRoot&gt;() — every field is optional.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public class UIRoot : MonoBehaviour
    {
        public const string PanelOrders = "orders";
        public const string PanelWorkbench = "workbench";

        public static UIRoot Instance { get; private set; }
        /// <summary>True while a hub panel is open. Player input should ignore movement/look/interact.</summary>
        public static bool IsModalOpen { get => GameServices.UIModalOpen; private set => GameServices.UIModalOpen = value; }
        /// <summary>"orders", "workbench" or null.</summary>
        public static string OpenPanelId { get; private set; }

        [Header("Assets (optional — created/loaded at runtime when empty)")]
        [SerializeField] private PanelSettings panelSettings;
        [Tooltip("Optional. Default runtime theme (UnityDefaultRuntimeTheme.tss). Not required: all controls are styled by Game.uss.")]
        [SerializeField] private ThemeStyleSheet themeStyleSheet;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private string styleSheetResourcePath = "UI/Game";
        [Tooltip("Optional font. Defaults to Unity's built-in LegacyRuntime.ttf (has Cyrillic).")]
        [SerializeField] private Font font;

        [Header("Behaviour")]
        [Tooltip("Escape toggles pause in Hub/Delivery. OFF by default: the Player module (ElfController) already calls Flow.TogglePause() on Escape/Start. Escape always closes open hub panels.")]
        [SerializeField] private bool handlePauseKey = false;
        [Tooltip("Tab opens/closes the order board in the Hub.")]
        [SerializeField] private bool handleOrdersKey = true;
        [SerializeField] private bool createEventSystemIfMissing = true;
        [SerializeField] private int sortingOrder = 100;

        private UIDocument _document;
        private VisualElement _layer;

        private MainMenuView _menu;
        private HudView _hud;
        private OrderBoardView _orders;
        private WorkbenchView _workbench;
        private ResultsView _results;
        private GameOverView _gameOver;
        private PauseView _pause;
        private UIView[] _all;

        private UIView _openModal;
        private GameState _state = GameState.Boot;
        private GameState _pausedFrom = GameState.Hub;
        private bool _bound;
        private int _escCloseFrame = -100;
        private bool _undoPause;

        public GameState CurrentState => _state;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            IsModalOpen = false;
            OpenPanelId = null;
        }

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

            if (createEventSystemIfMissing) EnsureEventSystem();
            BuildDocument();
        }

        private void OnEnable()
        {
            if (Instance != this || _all == null || _bound) return;
            _bound = true;
            GameEvents.StateChanged += OnStateChanged;
            GameEvents.RequestHubPanel += OnRequestHubPanel;
            foreach (var v in _all) v.Bind();
        }

        private void OnDisable()
        {
            if (!_bound) return;
            _bound = false;
            GameEvents.StateChanged -= OnStateChanged;
            GameEvents.RequestHubPanel -= OnRequestHubPanel;
            foreach (var v in _all) v.Unbind();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                IsModalOpen = false;
                OpenPanelId = null;
            }
        }

        private void Start()
        {
            if (Instance != this) return;
            var flow = GameServices.Flow;
            if (flow != null && flow.State != _state) ApplyState(GameState.Boot, flow.State);
        }

        private void LateUpdate()
        {
            // Escape closed a hub panel, but the Player module also toggled pause on the same key press: undo it.
            if (!_undoPause) return;
            _undoPause = false;
            var flow = GameServices.Flow;
            if (flow != null && flow.State == GameState.Paused) flow.TogglePause();
        }

        private void Update()
        {
            if (_all == null) return;
            HandleKeys();
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].IsVisible) _all[i].Tick(dt);
        }

        // ------------------------------------------------------------------ setup

        private void BuildDocument()
        {
            var ps = panelSettings != null ? panelSettings : CreatePanelSettings();

            // Create the UIDocument on an inactive child so panelSettings is set before its OnEnable runs.
            var docGo = new GameObject("UIDocument");
            docGo.SetActive(false);
            docGo.transform.SetParent(transform, false);
            _document = docGo.AddComponent<UIDocument>();
            _document.panelSettings = ps;
            docGo.SetActive(true);

            var root = _document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[UIRoot] UIDocument.rootVisualElement is null — UI disabled.");
                return;
            }

            var sheet = styleSheet != null ? styleSheet : Resources.Load<StyleSheet>(styleSheetResourcePath);
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogWarning($"[UIRoot] StyleSheet Resources/{styleSheetResourcePath}.uss not found — UI will be unstyled.");

            _layer = new VisualElement { name = "sn-root", pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("sn-root");
            // Fill the screen even if the USS failed to import.
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0; _layer.style.top = 0; _layer.style.right = 0; _layer.style.bottom = 0;
            root.Add(_layer);

            ApplyFont(_layer);

            _hud = new HudView();
            _menu = new MainMenuView();
            _orders = new OrderBoardView();
            _workbench = new WorkbenchView();
            _results = new ResultsView();
            _gameOver = new GameOverView();
            _pause = new PauseView();
            // Order = draw order (later is on top).
            _all = new UIView[] { _hud, _menu, _orders, _workbench, _results, _gameOver, _pause };
            foreach (var v in _all) v.Build(this, _layer);
        }

        private PanelSettings CreatePanelSettings()
        {
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "SilentNight_RuntimePanelSettings";
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.sortingOrder = sortingOrder;
            ps.clearColor = false;
            if (themeStyleSheet != null) ps.themeStyleSheet = themeStyleSheet;
            return ps;
        }

        private void ApplyFont(VisualElement target)
        {
            Font f = font;
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (Exception) { f = null; }
            }
            if (f != null)
                target.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(f));
            else
                Debug.LogWarning("[UIRoot] No font found; relying on PanelSettings theme/text settings.");
        }

        private void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null ||
                FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var go = new GameObject("EventSystem (UIRoot)");
            go.transform.SetParent(transform, false);
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        // ------------------------------------------------------------------ state

        private void OnStateChanged(GameState prev, GameState next) => ApplyState(prev, next);

        private void ApplyState(GameState prev, GameState next)
        {
            if (_all == null) return; // document failed to build
            _state = next;
            if (next == GameState.Paused &&
                (_openModal != null || Time.frameCount - _escCloseFrame <= 1))
            {
                // Pause came from the same Escape press that should only close the panel
                // (Player module handles Escape itself). Close the panel and resume in LateUpdate.
                _undoPause = true;
            }
            CloseModalInternal(false); // GameFlow already set the cursor for the new state

            bool gameplay = next == GameState.Hub || next == GameState.Delivery || next == GameState.Rooftop;
            if (next == GameState.Paused) _pausedFrom = prev;
            // Re-enter HUD mode unless this is a plain resume (Pause -> "give up" goes Paused -> Hub from a Delivery).
            if (gameplay && (prev != GameState.Paused || next != _pausedFrom)) _hud.EnterMode(next);

            _hud.SetVisible(gameplay || next == GameState.Paused);
            _menu.SetVisible(next == GameState.MainMenu);
            _results.SetVisible(next == GameState.Results);
            _gameOver.SetVisible(next == GameState.GameOver);
            if (next == GameState.Paused) _pause.SetContext(prev);
            _pause.SetVisible(next == GameState.Paused);
        }

        // ------------------------------------------------------------------ modal hub panels

        /// <summary>Ask to open a hub panel (goes through GameEvents so stations stay in sync).</summary>
        public void RequestOpenPanel(string id) => GameEvents.RaiseRequestHubPanel(id);

        /// <summary>Ask to close the open hub panel.</summary>
        public void RequestClosePanel()
        {
            if (_openModal != null) GameEvents.RaiseRequestHubPanel(null);
        }

        private void OnRequestHubPanel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                CloseModalInternal(true);
                return;
            }
            if (_state != GameState.Hub) return;

            UIView view = null;
            string norm = null;
            if (string.Equals(id, PanelOrders, StringComparison.OrdinalIgnoreCase)) { view = _orders; norm = PanelOrders; }
            else if (string.Equals(id, PanelWorkbench, StringComparison.OrdinalIgnoreCase)) { view = _workbench; norm = PanelWorkbench; }
            if (view == null)
            {
                Debug.LogWarning($"[UIRoot] Unknown hub panel '{id}'.");
                return;
            }

            if (_openModal != null && _openModal != view) _openModal.Hide();
            _openModal = view;
            OpenPanelId = norm;
            IsModalOpen = true;
            view.Show();
            _hud.SetPromptSuppressed(true);
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        private void CloseModalInternal(bool relockCursor)
        {
            if (_openModal == null) return;
            _openModal.Hide();
            _openModal = null;
            IsModalOpen = false;
            OpenPanelId = null;
            _hud.SetPromptSuppressed(false);
            var fe = _document != null && _document.rootVisualElement != null
                ? _document.rootVisualElement.focusController?.focusedElement : null;
            fe?.Blur();
            if (relockCursor && (_state == GameState.Hub || _state == GameState.Delivery || _state == GameState.Rooftop))
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }

        // ------------------------------------------------------------------ keys

        private void HandleKeys()
        {
            bool tab, esc;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            tab = kb.tabKey.wasPressedThisFrame;
            esc = kb.escapeKey.wasPressedThisFrame;
#else
            tab = Input.GetKeyDown(KeyCode.Tab);
            esc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (esc)
            {
                if (_openModal != null)
                {
                    _escCloseFrame = Time.frameCount;
                    RequestClosePanel();
                }
                else if (handlePauseKey && GameServices.Flow != null &&
                         (_state == GameState.Hub || _state == GameState.Delivery || _state == GameState.Rooftop || _state == GameState.Paused))
                    GameServices.Flow.TogglePause();
            }
            else if (tab && handleOrdersKey && _state == GameState.Hub)
            {
                if (_openModal != null) RequestClosePanel();
                else RequestOpenPanel(PanelOrders);
            }
        }
    }
}
