using System.Collections.Generic;
using SilentNight.Core;
using SilentNight.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>
    /// Dedicated Standalone Main Menu screen inspired by R.E.P.O. / Lethal Company.
    /// Completely opaque, separate screen with Left Navigation Column and Right Showcase & Staging Board.
    /// </summary>
    public class MainMenuView : UIView
    {
        private enum MenuTab
        {
            CharacterShowcase,
            Lobby,
            Join,
            Settings
        }

        private VisualElement _root;
        private VisualElement _navCol;
        private VisualElement _rightArea;

        // Showcase View (Default & Customize)
        private VisualElement _showcaseBox;
        private ElfPreviewStudio _previewStudio;
        private Image _previewImg;
        private Label _charTitle;
        private Label _charRole;
        private Label _charDesc;
        private readonly Button[] _charSwatches = new Button[PlayerProfile.SkinNames.Length];

        // Lobby View
        private VisualElement _lobbyBox;
        private Label _lobbyStatusLbl;
        private Label _lobbyIpLbl;
        private Button _startCoopBtn;
        private readonly VisualElement[] _lobbySlots = new VisualElement[4];
        private readonly Label[] _lobbySlotNames = new Label[4];
        private readonly Label[] _lobbySlotStatus = new Label[4];

        // Join View
        private VisualElement _joinBox;
        private TextField _joinIpField;
        private TextField _joinPortField;

        // Settings View
        private VisualElement _settingsBox;
        private Slider _volumeSlider;
        private Button _fullscreenBtn;

        private MenuTab _currentTab = MenuTab.CharacterShowcase;
        private Texture2D _bgTex;

        protected override VisualElement PopTarget => _navCol;

        protected override VisualElement CreateRoot()
        {
            _root = UIFactory.El("sn-screen sn-screen--repo");
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.top = 0;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.width = Length.Percent(100);
            _root.style.height = Length.Percent(100);
            _root.style.backgroundColor = new Color(0.04f, 0.03f, 0.05f, 1f);

            if (_bgTex == null) _bgTex = CreateAtmosphericBackground();
            _root.style.backgroundImage = new StyleBackground(_bgTex);
            _root.style.unityBackgroundImageTintColor = Color.white;

            // =================================================================
            // LEFT NAVIGATION COLUMN
            // =================================================================
            _navCol = UIFactory.El("sn-repo-col", _root);

            // Title block
            var titleBox = UIFactory.Ignored("sn-repo-title", _navCol);
            UIFactory.Text("OPERATION:", "sn-repo-title__kicker", titleBox);
            UIFactory.Text("SILENT NIGHT", "sn-repo-title__big", titleBox);
            UIFactory.Text("v0.6.4.2 [NIGHT-OPS]  •  ЭЛЬФИЙСКИЙ КООП-РЕЙПО", "sn-repo-title__tag", titleBox);

            // Navigation Buttons List
            var navList = UIFactory.El("sn-repo-nav", _navCol);
            AddNavBtn(navList, "ОДИНОЧНАЯ СМЕНА", OnSingleplayer);
            AddNavBtn(navList, "СОЗДАТЬ ЛОББИ", OnCreateLobby);
            AddNavBtn(navList, "ПОДКЛЮЧИТЬСЯ", OnOpenJoin);
            AddNavBtn(navList, "ВЫБОР ЭЛЬФА", OnOpenCustomize);
            AddNavBtn(navList, "НАСТРОЙКИ", OnOpenSettings);
            AddNavBtn(navList, "ВЫХОД", OnQuit);

            // Bottom quick hint bar
            var bottomBar = UIFactory.Ignored("sn-repo-bottom", _navCol);
            UIFactory.Text("Поддержка геймпадов и клавиатуры • LAN & Online", "sn-repo-bottom__text", bottomBar);

            // =================================================================
            // RIGHT STAGING & SHOWCASE AREA
            // =================================================================
            _rightArea = UIFactory.El("sn-repo-right", _root);

            BuildShowcaseBox();
            BuildLobbyBox();
            BuildJoinBox();
            BuildSettingsBox();

            SwitchTab(MenuTab.CharacterShowcase);
            return _root;
        }

        private void AddNavBtn(VisualElement parent, string title, System.Action onClick)
        {
            var btn = UIFactory.Btn("", onClick, "sn-repo-btn", parent);
            var arrow = UIFactory.Text("▶", "sn-repo-btn__arrow", btn);
            var text = UIFactory.Text(title, "sn-repo-btn__text", btn);
            arrow.style.opacity = 0f;
            arrow.style.color = new Color(1.0f, 0.65f, 0.1f, 1f); // R.E.P.O. glowing orange
            btn.RegisterCallback<MouseEnterEvent>(evt =>
            {
                arrow.style.opacity = 1f;
                text.style.color = new Color(1.0f, 0.88f, 0.45f, 1f);
                SilentNight.Audio.AudioManager.Instance?.Play2D(SilentNight.Audio.AudioClips.UiClick, 0.25f);
            });
            btn.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                arrow.style.opacity = 0f;
                text.style.color = new Color(0.92f, 0.88f, 0.82f, 1f);
            });
        }

        private Texture2D CreateAtmosphericBackground()
        {
            int w = 512;
            int h = 320;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / w;

                    // Base atmospheric night gradient: midnight top, deep road bottom
                    Color topCol = new Color(0.03f, 0.03f, 0.05f, 1f);
                    Color horizonCol = new Color(0.07f, 0.07f, 0.10f, 1f);
                    Color groundCol = new Color(0.02f, 0.02f, 0.03f, 1f);

                    Color c;
                    if (ny > 0.45f)
                    {
                        float t = (ny - 0.45f) / 0.55f;
                        c = Color.Lerp(horizonCol, topCol, t);
                    }
                    else
                    {
                        float t = ny / 0.45f;
                        c = Color.Lerp(groundCol, horizonCol, t);
                    }

                    // Headlight glow cone (like R.E.P.O. van headlights on the road/fog)
                    float dx = (nx - 0.65f) * 1.5f;
                    float dy = (ny - 0.25f) * 2.2f;
                    float distHeadlight = Mathf.Sqrt(dx * dx + dy * dy);
                    float headlight = Mathf.Clamp01(1f - distHeadlight * 1.8f);
                    if (headlight > 0f)
                    {
                        c += new Color(0.18f, 0.14f, 0.08f, 0f) * (headlight * headlight);
                    }

                    // Secondary headlight cone
                    float dx2 = (nx - 0.52f) * 2.0f;
                    float dy2 = (ny - 0.22f) * 2.5f;
                    float distHeadlight2 = Mathf.Sqrt(dx2 * dx2 + dy2 * dy2);
                    float headlight2 = Mathf.Clamp01(1f - distHeadlight2 * 2.0f);
                    if (headlight2 > 0f)
                    {
                        c += new Color(0.12f, 0.10f, 0.06f, 0f) * (headlight2 * headlight2);
                    }

                    // Heavy corner vignette
                    float vx = (nx - 0.5f) * 2f;
                    float vy = (ny - 0.5f) * 2f;
                    float v = Mathf.Clamp01(Mathf.Sqrt(vx * vx + vy * vy) * 0.75f);
                    c = Color.Lerp(c, new Color(0.015f, 0.012f, 0.02f, 1f), v * v);

                    // Subtle film grain
                    float grain = (Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f);
                    grain = (grain - Mathf.Floor(grain)) * 0.035f - 0.0175f;
                    c.r = Mathf.Clamp01(c.r + grain);
                    c.g = Mathf.Clamp01(c.g + grain);
                    c.b = Mathf.Clamp01(c.b + grain);

                    pixels[y * w + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // =========================================================================
        // RIGHT PANELS
        // =========================================================================

        private void BuildShowcaseBox()
        {
            _showcaseBox = UIFactory.El("sn-repo-card", _rightArea);
            UIFactory.Text("КАРТОЧКА ПЕРСОНАЖА", "sn-repo-card__header", _showcaseBox);

            var row = UIFactory.Ignored("sn-repo-showcase-row", _showcaseBox);

            // Left: 3D Live Rotating Preview
            var previewFrame = UIFactory.Ignored("sn-repo-preview-frame", row);
            if (_previewStudio == null)
            {
                _previewStudio = ElfPreviewStudio.Create();
            }
            _previewImg = new Image
            {
                image = _previewStudio.PreviewTexture
            };
            _previewImg.AddToClassList("sn-repo-preview-img");
            previewFrame.Add(_previewImg);

            // Right: Information & Customization controls
            var details = UIFactory.Ignored("sn-repo-details", row);
            var info = UIFactory.Ignored("sn-repo-card__info", details);
            _charTitle = UIFactory.Text("Классический Эльф", "sn-repo-card__title", info);
            _charRole = UIFactory.Text("Роль: Мастер упаковки подарков", "sn-repo-card__role", info);
            _charDesc = UIFactory.Text("Традиционный зелёный костюм мастера мастерской Санты.", "sn-repo-card__desc", info);

            // Carousel selector
            var navRow = UIFactory.Ignored("sn-repo-card__nav", details);
            UIFactory.Btn("◀  Предыдущий", () => CycleElf(-1), "sn-btn--wood sn-repo-card__arrow-btn", navRow);
            UIFactory.Btn("Следующий  ▶", () => CycleElf(1), "sn-btn--wood sn-repo-card__arrow-btn", navRow);

            // Quick swatches
            var swatchWrap = UIFactory.Ignored("sn-repo-swatches", details);
            for (int i = 0; i < PlayerProfile.SkinNames.Length; i++)
            {
                int idx = i;
                var b = UIFactory.Btn(PlayerProfile.SkinNames[i], () => SelectElf(idx), "sn-repo-swatch", swatchWrap);
                _charSwatches[i] = b;
            }

            var footer = UIFactory.Ignored("sn-repo-card__footer", _showcaseBox);
            UIFactory.Btn("ВЫБРАТЬ И НАЧАТЬ СМЕНУ", OnSingleplayer, "sn-btn--green sn-btn--big sn-repo-card__start", footer);
        }

        private void BuildLobbyBox()
        {
            _lobbyBox = UIFactory.El("sn-repo-card sn-repo-card--lobby", _rightArea);
            UIFactory.Text("СЕТЕВАЯ КОМНАТА (ЛОББИ КООПА)", "sn-repo-card__header", _lobbyBox);

            _lobbyStatusLbl = UIFactory.Text("Ожидание игроков...", "sn-repo-card__status", _lobbyBox);

            var ipRow = UIFactory.Ignored("sn-repo-card__ip-row", _lobbyBox);
            _lobbyIpLbl = UIFactory.Text("IP адрес: ...", "sn-repo-card__ip-val", ipRow);
            UIFactory.Btn("Скопировать IP", OnCopyHostIp, "sn-btn--wood sn-repo-btn--small", ipRow);

            var roster = UIFactory.Ignored("sn-repo-roster", _lobbyBox);
            for (int i = 0; i < 4; i++)
            {
                var slot = UIFactory.Ignored("sn-repo-roster__slot", roster);
                _lobbySlots[i] = slot;
                _lobbySlotNames[i] = UIFactory.Text($"Слот {i + 1}", "sn-repo-roster__name", slot);
                _lobbySlotStatus[i] = UIFactory.Text("Пусто", "sn-repo-roster__state", slot);
            }

            var footer = UIFactory.Ignored("sn-repo-card__footer", _lobbyBox);
            _startCoopBtn = UIFactory.Btn("НАЧАТЬ СМЕНУ ВСЕЙ КОМАНДОЙ", OnStartCoopShift, "sn-btn--red sn-btn--big", footer);
            UIFactory.Btn("Закрыть лобби", () => SwitchTab(MenuTab.CharacterShowcase), "sn-btn--wood", footer);
        }

        private void BuildJoinBox()
        {
            _joinBox = UIFactory.El("sn-repo-card", _rightArea);
            UIFactory.Text("ПОДКЛЮЧЕНИЕ К ХОСТУ", "sn-repo-card__header", _joinBox);

            var form = UIFactory.Ignored("sn-repo-form", _joinBox);
            UIFactory.Text("IP адрес друга (LAN / VPN / Интернет):", "sn-repo-form__label", form);
            _joinIpField = new TextField { value = "127.0.0.1" };
            _joinIpField.AddToClassList("sn-repo-form__field");
            form.Add(_joinIpField);

            UIFactory.Text("Порт:", "sn-repo-form__label", form);
            _joinPortField = new TextField { value = "7777" };
            _joinPortField.AddToClassList("sn-repo-form__field");
            form.Add(_joinPortField);

            var footer = UIFactory.Ignored("sn-repo-card__footer", _joinBox);
            UIFactory.Btn("ПОДКЛЮЧИТЬСЯ", OnConfirmJoin, "sn-btn--green sn-btn--big", footer);
            UIFactory.Btn("Назад", () => SwitchTab(MenuTab.CharacterShowcase), "sn-btn--wood", footer);
        }

        private void BuildSettingsBox()
        {
            _settingsBox = UIFactory.El("sn-repo-card", _rightArea);
            UIFactory.Text("НАСТРОЙКИ", "sn-repo-card__header", _settingsBox);

            var form = UIFactory.Ignored("sn-repo-form", _settingsBox);
            UIFactory.Text("Общая громкость:", "sn-repo-form__label", form);
            _volumeSlider = new Slider(0f, 100f) { value = AudioListener.volume * 100f };
            _volumeSlider.AddToClassList("sn-repo-form__slider");
            _volumeSlider.RegisterValueChangedCallback(evt => AudioListener.volume = evt.newValue / 100f);
            form.Add(_volumeSlider);

            UIFactory.Text("Экранный режим:", "sn-repo-form__label", form);
            _fullscreenBtn = UIFactory.Btn(Screen.fullScreen ? "Полноэкранный режим (ВКЛ)" : "Оконный режим", OnToggleFullscreen, "sn-btn--wood", form);

            var footer = UIFactory.Ignored("sn-repo-card__footer", _settingsBox);
            UIFactory.Btn("ГОТОВО", () => SwitchTab(MenuTab.CharacterShowcase), "sn-btn--green sn-btn--big", footer);
        }

        // =========================================================================
        // NAVIGATION & ACTIONS
        // =========================================================================

        private void SwitchTab(MenuTab tab)
        {
            _currentTab = tab;
            _showcaseBox.style.display = tab == MenuTab.CharacterShowcase ? DisplayStyle.Flex : DisplayStyle.None;
            _lobbyBox.style.display = tab == MenuTab.Lobby ? DisplayStyle.Flex : DisplayStyle.None;
            _joinBox.style.display = tab == MenuTab.Join ? DisplayStyle.Flex : DisplayStyle.None;
            _settingsBox.style.display = tab == MenuTab.Settings ? DisplayStyle.Flex : DisplayStyle.None;

            if (tab == MenuTab.CharacterShowcase) RefreshCharacterDisplay();
        }

        protected override void OnShow()
        {
            SwitchTab(MenuTab.CharacterShowcase);
            RefreshCharacterDisplay();
        }

        private void OnSingleplayer()
        {
            // Leave main menu and spawn into the Hub shift
            GameServices.Flow?.StartShift();
        }

        private void OnCreateLobby()
        {
            var net = CoopNetworkManager.Instance;
            if (net != null && net.Role != CoopNetworkManager.NetRole.Host)
            {
                net.StartHost();
            }
            SwitchTab(MenuTab.Lobby);
        }

        private void OnOpenJoin()
        {
            SwitchTab(MenuTab.Join);
        }

        private void OnOpenCustomize()
        {
            SwitchTab(MenuTab.CharacterShowcase);
        }

        private void OnOpenSettings()
        {
            SwitchTab(MenuTab.Settings);
        }

        private void OnQuit()
        {
            if (GameServices.Flow != null) GameServices.Flow.QuitGame();
            else Application.Quit();
        }

        private void OnCopyHostIp()
        {
            var net = CoopNetworkManager.Instance;
            string ip = "127.0.0.1";
            if (net != null && net.LocalIps != null && net.LocalIps.Count > 0)
            {
                ip = net.LocalIps[0].Split(' ')[0];
            }
            GUIUtility.systemCopyBuffer = ip;
            GameEvents.RaiseToast($"IP скопирован в буфер: {ip}");
        }

        private void OnConfirmJoin()
        {
            string hostIp = _joinIpField.value?.Trim();
            if (string.IsNullOrEmpty(hostIp)) hostIp = "127.0.0.1";
            if (!int.TryParse(_joinPortField.value, out int port)) port = 7777;

            var net = CoopNetworkManager.Instance;
            if (net != null)
            {
                net.StartClient(hostIp, port);
                SwitchTab(MenuTab.Lobby);
            }
        }

        private void OnStartCoopShift()
        {
            GameServices.Flow?.StartShift();
        }

        private void OnToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            if (_fullscreenBtn != null)
                _fullscreenBtn.text = Screen.fullScreen ? "Полноэкранный режим (ВКЛ)" : "Оконный режим";
        }

        // =========================================================================
        // CHARACTER LOGIC
        // =========================================================================

        private void CycleElf(int delta)
        {
            var profile = GameServices.Profile;
            if (profile == null) return;
            profile.CycleSkin(delta);
            ApplyElfToAvatar(profile.SelectedSkin);
            RefreshCharacterDisplay();
        }

        private void SelectElf(int index)
        {
            var profile = GameServices.Profile;
            if (profile == null) return;
            profile.SetSkin(index);
            ApplyElfToAvatar(profile.SelectedSkin);
            RefreshCharacterDisplay();
        }

        private void ApplyElfToAvatar(int skinIndex)
        {
            var net = CoopNetworkManager.Instance;
            if (net != null)
            {
                net.ApplyLocalPlayerTheme(skinIndex);
            }

            // Also directly update any local elf in the scene immediately
            var primary = SilentNight.Player.ElfInput.Primary;
            if (primary != null)
            {
                var vis = primary.GetComponent<SilentNight.Player.ElfVisual>();
                if (vis != null) vis.ThemeIndex = skinIndex;
                var rag = primary.GetComponent<SilentNight.Player.ElfRagdoll>();
                if (rag != null) rag.RefreshThemeModel();
            }
            else
            {
                var allVis = Object.FindObjectsByType<SilentNight.Player.ElfVisual>();
                for (int i = 0; i < allVis.Length; i++)
                {
                    var inp = allVis[i].GetComponent<SilentNight.Player.ElfInput>();
                    if (inp == null || (!inp.IsNetworkRemote && inp.PlayerIndex == 0))
                    {
                        allVis[i].ThemeIndex = skinIndex;
                        var rag = allVis[i].GetComponent<SilentNight.Player.ElfRagdoll>();
                        if (rag != null) rag.RefreshThemeModel();
                    }
                }
            }
        }

        private void RefreshCharacterDisplay()
        {
            var profile = GameServices.Profile;
            int skin = profile != null ? profile.SelectedSkin : 0;
            if (skin < 0 || skin >= PlayerProfile.SkinNames.Length) skin = 0;

            if (_previewStudio != null)
            {
                _previewStudio.SetTheme(skin);
            }

            if (_charTitle != null) _charTitle.text = PlayerProfile.SkinNames[skin];
            if (_charDesc != null) _charDesc.text = PlayerProfile.SkinDescriptions[skin];
            if (_charRole != null)
            {
                _charRole.text = skin switch
                {
                    1 => "Роль: Быстрый скаут и разведчик крыш",
                    2 => "Роль: Механик и настройщик саней",
                    3 => "Роль: Силовой траппер в шапке-ушанке",
                    4 => "Роль: Спецагент со стильной сумкой",
                    _ => "Роль: Мастер упаковки рождественских подарков"
                };
            }

            for (int i = 0; i < _charSwatches.Length; i++)
            {
                if (_charSwatches[i] != null)
                    _charSwatches[i].EnableInClassList("sn-repo-swatch--active", i == skin);
            }
        }

        // =========================================================================
        // TICK (LOBBY ROSTER)
        // =========================================================================

        public override void Tick(float dt)
        {
            if (_currentTab == MenuTab.Lobby)
            {
                UpdateLobbyStatus();
            }
        }

        private void UpdateLobbyStatus()
        {
            var net = CoopNetworkManager.Instance;
            if (net == null) return;

            bool isHost = net.Role == CoopNetworkManager.NetRole.Host;
            bool isClient = net.Role == CoopNetworkManager.NetRole.Client;

            _lobbyStatusLbl.text = isHost ? "👑 ВЫ — ХОСТ КОМНАТЫ" : (isClient ? $"🎮 ЭЛЬФ ПОДКЛЮЧЁН (Слот {net.LocalSlot + 1})" : "ОФФЛАЙН");

            string ipSummary = net.LocalIps.Count > 0 ? net.LocalIps[0] : "127.0.0.1";
            _lobbyIpLbl.text = $"IP для друзей: {ipSummary}";

            int totalPlayers = net.ConnectedPlayerCount;
            for (int i = 0; i < 4; i++)
            {
                bool active = i < totalPlayers;
                _lobbySlots[i].EnableInClassList("sn-repo-roster__slot--active", active);
                if (i == 0)
                {
                    var p = GameServices.Profile;
                    string skinName = p != null ? PlayerProfile.SkinNames[p.SelectedSkin] : "Классик";
                    _lobbySlotNames[i].text = $"👑 Хост (Вы)  •  {skinName}";
                    _lobbySlotStatus[i].text = "ГОТОВ";
                }
                else if (active)
                {
                    _lobbySlotNames[i].text = $"Эльф {i + 1} (В сети)";
                    _lobbySlotStatus[i].text = "ГОТОВ";
                }
                else
                {
                    _lobbySlotNames[i].text = $"Слот {i + 1}";
                    _lobbySlotStatus[i].text = "[Ожидание игрока...]";
                }
            }

            if (_startCoopBtn != null)
            {
                _startCoopBtn.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
                _startCoopBtn.text = $"НАЧАТЬ СМЕНУ ВСЕЙ КОМАНДОЙ ({totalPlayers}/4)";
            }
        }

        public override void Unbind()
        {
            base.Unbind();
            if (_bgTex != null)
            {
                Object.Destroy(_bgTex);
                _bgTex = null;
            }
            if (_previewStudio != null)
            {
                Object.Destroy(_previewStudio.gameObject);
                _previewStudio = null;
            }
        }
    }
}
