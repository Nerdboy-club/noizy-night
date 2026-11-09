using SilentNight.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SilentNight.UI
{
    /// <summary>
    /// In-game HUD for Hub and Delivery: objective (top-left), wallet + gift counter (top-right),
    /// grandpa eye + toasts (top-centre), noise gauge + stamina/fuel (bottom-left),
    /// sack slots (bottom-right), interaction prompt (centre-bottom).
    /// </summary>
    public class HudView : UIView
    {
        private const int MaxSackSlots = 1 + PlayerProfile.MaxUpgradeLevel;
        private const int ToastPool = 4;
        private const float ToastTime = 2.8f;
        private const float ToastFade = 0.4f;

        private GameState _mode = GameState.Hub;

        // widgets
        private Label _objective;
        private Label _tips;
        private VisualElement _walletChip;
        private VisualElement _counterChip;
        private IconElement _counterIcon;
        private Label _counterLabel;
        private VisualElement _eyeWrap;
        private AlertEyeElement _eye;
        private Label _eyeLabel;
        private VisualElement _toasts;
        private readonly Label[] _toastLabels = new Label[ToastPool];
        private readonly float[] _toastLeft = new float[ToastPool];
        private int _toastNext;
        private VisualElement _gaugeWrap;
        private NoiseGaugeElement _gauge;
        private Label _noiseWord;
        private VisualElement _staminaFill;
        private VisualElement _fuelRow;
        private VisualElement _fuelFill;
        private VisualElement _sackWrap;
        private readonly VisualElement[] _slots = new VisualElement[MaxSackSlots];
        private VisualElement _prompt;
        private Label _promptText;
        private VisualElement _hint;
        private VisualElement _hands;
        private Label _handsTitle;
        private Label _handsHint;
        private VisualElement _catnip;
        private Label _catnipText;
        private VisualElement _reticle;
        private VisualElement _countdownBanner;
        private Label _countdownTitle;
        private Label _countdownSub;

        // co-op HUD widgets (per-player status cards + split-screen dividers)
        private const int MaxCoopCards = 4;
        private VisualElement _coopWrap;
        private Label _coopHeader;
        private readonly VisualElement[] _coopCards = new VisualElement[MaxCoopCards];
        private readonly Label[] _coopNames = new Label[MaxCoopCards];
        private readonly Label[] _coopStatus = new Label[MaxCoopCards];
        private readonly Label[] _coopPrompts = new Label[MaxCoopCards];
        private readonly VisualElement[] _coopStaminaFills = new VisualElement[MaxCoopCards];
        private VisualElement _splitLineV;
        private VisualElement _splitLineH;
        private static readonly Color[] CoopColors =
        {
            new Color(0.22f, 0.78f, 0.34f),
            new Color(0.92f, 0.26f, 0.26f),
            new Color(0.32f, 0.56f, 0.96f),
            new Color(0.68f, 0.38f, 0.90f),
        };

        // cached state (to avoid per-frame allocations / restyles)
        private float _noiseShown;
        private float _staminaShown = -1f;
        private float _fuelShown = -1f;
        private int _fuelVisible = -1;
        private int _tipsShown = int.MinValue;
        private int _sackCount = -1, _sackCap = -1;
        private string _promptShown;
        private bool _promptSuppressed;
        private int _noiseWordState = -1;
        private int _counterDone, _counterTotal;
        private AlertLevel _alert = AlertLevel.Asleep;
        private float _pulseTimer;
        private bool _pulseOn;
        private float _damageToastCooldown;
        private ICarryable _heldShown;
        private int _heldStateShown = int.MinValue;
        private int _catnipShown = -1;
        private int _lastCountdownSec = -1;

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.Ignored("sn-hud");

            // ---- objective, top-left
            var obj = UIFactory.Ignored("sn-objective", root);
            UIFactory.Ignored("sn-objective__mark", obj);
            _objective = UIFactory.Text("", "sn-objective__text", obj);

            // ---- co-op party panel (below objective, top-left) + split-screen dividers
            BuildCoopHud(root);

            // ---- top-right: wallet + counter
            var tr = UIFactory.Ignored("sn-topright", root);
            _walletChip = UIFactory.Ignored("sn-chip sn-chip--gold", tr);
            UIFactory.Icon(IconKind.Coin, "sn-chip__icon", _walletChip);
            _tips = UIFactory.Text("0", "sn-chip__value", _walletChip);
            _counterChip = UIFactory.Ignored("sn-chip", tr);
            _counterIcon = UIFactory.Icon(IconKind.GiftSmall, "sn-chip__icon", _counterChip);
            _counterLabel = UIFactory.Text("0/0", "sn-chip__value", _counterChip);

            // ---- top-centre: eye + toasts
            var tc = UIFactory.Ignored("sn-topcenter", root);
            _eyeWrap = UIFactory.Ignored("sn-eye-wrap", tc);
            _eye = new AlertEyeElement();
            _eyeWrap.Add(_eye);
            _eyeLabel = UIFactory.Text("Zzz", "sn-eye__label", _eyeWrap);
            _toasts = UIFactory.Ignored("sn-toasts", tc);
            for (int i = 0; i < ToastPool; i++)
            {
                var t = UIFactory.Text("", "sn-toast", _toasts);
                t.style.display = DisplayStyle.None;
                _toastLabels[i] = t;
            }

            // ---- bottom-left: noise gauge + bars
            _gaugeWrap = UIFactory.Ignored("sn-gauge-wrap", root);
            var plate = UIFactory.Ignored("sn-gauge-plate", _gaugeWrap);
            _gauge = new NoiseGaugeElement();
            plate.Add(_gauge);
            _noiseWord = UIFactory.Text("ТИХО", "sn-gauge__word", plate);
            var bars = UIFactory.Ignored("sn-bars", _gaugeWrap);
            _staminaFill = MakeBar(bars, "sn-bar__fill--stamina", "СИЛЫ", out _);
            _fuelFill = MakeBar(bars, "sn-bar__fill--fuel", "РАНЕЦ", out _fuelRow);

            // ---- above the gauge: catnip counter + "in hands" status (column grows upward)
            var status = UIFactory.Ignored("sn-hands-col");
            _gaugeWrap.Insert(0, status);
            _catnip = UIFactory.Ignored("sn-catnip", status);
            UIFactory.Key("F", _catnip);
            _catnipText = UIFactory.Text("Мята: 0", "sn-catnip__text", _catnip);
            _catnip.style.display = DisplayStyle.None;
            _hands = UIFactory.Ignored("sn-hands", status);
            _handsTitle = UIFactory.Text("", "sn-hands__title", _hands);
            _handsHint = UIFactory.Text("", "sn-hands__hint", _hands);
            _hands.style.display = DisplayStyle.None;

            // ---- bottom-right: sack slots
            _sackWrap = UIFactory.Ignored("sn-sack", root);
            var slotsRow = UIFactory.Ignored("sn-sack__slots", _sackWrap);
            for (int i = 0; i < MaxSackSlots; i++)
            {
                var slot = UIFactory.Ignored("sn-slot", slotsRow);
                UIFactory.Icon(IconKind.GiftSmall, "sn-slot__icon", slot);
                _slots[i] = slot;
            }
            var sackLabel = UIFactory.Ignored("sn-sack__label", _sackWrap);
            UIFactory.Key("R", sackLabel);
            UIFactory.Text("МЕШОК", "sn-sack__text", sackLabel);

            // ---- prompt, centre-bottom
            _prompt = UIFactory.Ignored("sn-prompt", root);
            var plaque = UIFactory.Ignored("sn-prompt__plaque", _prompt);
            UIFactory.Key("E", plaque).AddToClassList("sn-key--big");
            _promptText = UIFactory.Text("", "sn-prompt__text", plaque);
            _prompt.style.display = DisplayStyle.None;

            // ---- center reticle for 3D interaction focus
            _reticle = UIFactory.Ignored("sn-reticle", root);

            // ---- center countdown banner (group transitions)
            _countdownBanner = UIFactory.Ignored("sn-countdown-banner", root);
            _countdownTitle = UIFactory.Text("", "sn-countdown-banner__title", _countdownBanner);
            _countdownSub = UIFactory.Text("", "sn-countdown-banner__sub", _countdownBanner);
            _countdownBanner.style.display = DisplayStyle.None;

            // ---- hub hint, bottom-centre
            _hint = UIFactory.Ignored("sn-hint", root);
            UIFactory.Key("Tab", _hint);
            UIFactory.Text("Доска заказов", "sn-hint__text", _hint);

            return root;
        }

        private static VisualElement MakeBar(VisualElement parent, string fillClass, string label, out VisualElement row)
        {
            row = UIFactory.Ignored("sn-bar", parent);
            UIFactory.Text(label, "sn-bar__label", row);
            var track = UIFactory.Ignored("sn-bar__track", row);
            var fill = UIFactory.Ignored("sn-bar__fill " + fillClass, track);
            UIFactory.Ignored("sn-bar__shine", track);
            return fill;
        }

        private void BuildCoopHud(VisualElement root)
        {
            // Split-screen border lines
            _splitLineV = UIFactory.Ignored("sn-split-v", root);
            _splitLineV.style.position = Position.Absolute;
            _splitLineV.style.left = Length.Percent(50f);
            _splitLineV.style.top = 0;
            _splitLineV.style.bottom = 0;
            _splitLineV.style.width = 3;
            _splitLineV.style.backgroundColor = new Color(0.95f, 0.78f, 0.32f, 0.65f);
            _splitLineV.style.display = DisplayStyle.None;

            _splitLineH = UIFactory.Ignored("sn-split-h", root);
            _splitLineH.style.position = Position.Absolute;
            _splitLineH.style.left = 0;
            _splitLineH.style.right = 0;
            _splitLineH.style.top = Length.Percent(50f);
            _splitLineH.style.height = 3;
            _splitLineH.style.backgroundColor = new Color(0.95f, 0.78f, 0.32f, 0.65f);
            _splitLineH.style.display = DisplayStyle.None;

            // Co-op panel under top-left objective
            _coopWrap = UIFactory.Ignored("sn-coop-wrap", root);
            _coopWrap.style.position = Position.Absolute;
            _coopWrap.style.left = 28;
            _coopWrap.style.top = 104;
            _coopWrap.style.width = 360;
            _coopWrap.style.flexDirection = FlexDirection.Column;

            var headerBox = UIFactory.Ignored("sn-coop-header", _coopWrap);
            headerBox.style.backgroundColor = new Color(0.12f, 0.08f, 0.05f, 0.88f);
            headerBox.style.borderTopLeftRadius = headerBox.style.borderTopRightRadius =
                headerBox.style.borderBottomLeftRadius = headerBox.style.borderBottomRightRadius = 10;
            headerBox.style.borderTopWidth = headerBox.style.borderRightWidth = 2;
            headerBox.style.borderBottomWidth = 4;
            Color hBorder = new Color(0.16f, 0.08f, 0.04f, 0.95f);
            headerBox.style.borderTopColor = headerBox.style.borderRightColor = headerBox.style.borderBottomColor = hBorder;
            headerBox.style.paddingLeft = headerBox.style.paddingRight = 12;
            headerBox.style.paddingTop = headerBox.style.paddingBottom = 6;
            headerBox.style.marginBottom = 6;
            headerBox.style.borderLeftWidth = 4;
            headerBox.style.borderLeftColor = new Color(0.95f, 0.78f, 0.32f, 0.95f);
            _coopHeader = UIFactory.Text("[F4] Сетевой кооп  •  [F2] +Эльф 2", "sn-coop-header__text", headerBox);
            _coopHeader.style.fontSize = 13;
            _coopHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            _coopHeader.style.color = new Color(0.96f, 0.90f, 0.76f);

            for (int i = 0; i < MaxCoopCards; i++)
            {
                var card = UIFactory.Ignored("sn-coop-card", _coopWrap);
                card.style.backgroundColor = new Color(0.14f, 0.09f, 0.06f, 0.90f);
                card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                    card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 10;
                card.style.borderTopWidth = card.style.borderRightWidth = 2;
                card.style.borderBottomWidth = 4;
                card.style.borderTopColor = card.style.borderRightColor = card.style.borderBottomColor = hBorder;
                card.style.paddingLeft = card.style.paddingRight = 12;
                card.style.paddingTop = card.style.paddingBottom = 6;
                card.style.marginBottom = 5;
                card.style.borderLeftWidth = 5;
                card.style.borderLeftColor = CoopColors[i];
                card.style.display = DisplayStyle.None;

                var topRow = UIFactory.Ignored("sn-coop-card__top", card);
                topRow.style.flexDirection = FlexDirection.Row;
                topRow.style.justifyContent = Justify.SpaceBetween;
                topRow.style.alignItems = Align.Center;

                var nameLbl = UIFactory.Text($"Эльф {i + 1}", "sn-coop-card__name", topRow);
                nameLbl.style.fontSize = 14;
                nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLbl.style.color = Color.Lerp(CoopColors[i], Color.white, 0.3f);

                var statusLbl = UIFactory.Text("", "sn-coop-card__status", topRow);
                statusLbl.style.fontSize = 12;
                statusLbl.style.color = new Color(0.92f, 0.88f, 0.80f);

                var barTrack = UIFactory.Ignored("sn-coop-card__track", card);
                barTrack.style.height = 5;
                barTrack.style.backgroundColor = new Color(0.05f, 0.04f, 0.03f, 0.85f);
                barTrack.style.borderTopLeftRadius = barTrack.style.borderTopRightRadius =
                    barTrack.style.borderBottomLeftRadius = barTrack.style.borderBottomRightRadius = 3;
                barTrack.style.marginTop = 4;
                barTrack.style.overflow = Overflow.Hidden;

                var barFill = UIFactory.Ignored("sn-coop-card__fill", barTrack);
                barFill.style.height = Length.Percent(100f);
                barFill.style.width = Length.Percent(100f);
                barFill.style.backgroundColor = CoopColors[i];

                var promptLbl = UIFactory.Text("", "sn-coop-card__prompt", card);
                promptLbl.style.fontSize = 12;
                promptLbl.style.color = new Color(1.0f, 0.86f, 0.42f);
                promptLbl.style.marginTop = 3;
                promptLbl.style.display = DisplayStyle.None;

                _coopCards[i] = card;
                _coopNames[i] = nameLbl;
                _coopStatus[i] = statusLbl;
                _coopStaminaFills[i] = barFill;
                _coopPrompts[i] = promptLbl;
            }
        }

        // ------------------------------------------------------------------ events

        public override void Bind()
        {
            GameEvents.Objective += OnObjective;
            GameEvents.Toast += ShowToast;
            GameEvents.GrandpaAlertChanged += OnAlert;
            GameEvents.GiftDelivered += OnGiftDelivered;
            GameEvents.GiftLoaded += OnGiftLoaded;
            GameEvents.GiftDamaged += OnGiftDamaged;
        }

        public override void Unbind()
        {
            GameEvents.Objective -= OnObjective;
            GameEvents.Toast -= ShowToast;
            GameEvents.GrandpaAlertChanged -= OnAlert;
            GameEvents.GiftDelivered -= OnGiftDelivered;
            GameEvents.GiftLoaded -= OnGiftLoaded;
            GameEvents.GiftDamaged -= OnGiftDamaged;
        }

        /// <summary>Called by UIRoot when entering Hub or Delivery (not when resuming from pause).</summary>
        public void EnterMode(GameState mode)
        {
            _mode = mode;
            bool delivery = mode == GameState.Delivery;
            var order = GameServices.ActiveOrder;

            _eyeWrap.style.display = delivery ? DisplayStyle.Flex : DisplayStyle.None;
            _hint.style.display = delivery ? DisplayStyle.None : DisplayStyle.Flex;
            SetAlert(AlertLevel.Asleep);

            int total = order != null ? order.Gifts.Count : 0;
            if (delivery)
            {
                _counterDone = 0;
                _counterIcon.Set(IconKind.GiftHuge, Palette.Green, Palette.Gold);
                _objective.text = "Отнесите подарки под ёлку. Не разбудите деда!";
            }
            else
            {
                _counterDone = Mathf.Min(GameServices.LoadedGifts.Count, total);
                _counterIcon.Set(IconKind.GiftSmall, Palette.Red, Palette.Gold);
                _objective.text = "Погрузите подарки с конвейера в сани и выберите адрес";
            }
            _counterTotal = total;
            RefreshCounter();

            Root.EnableInClassList("sn-hud--spooky", delivery && order != null && order.Mood == HouseMood.Spooky);
            for (int i = 0; i < ToastPool; i++) { _toastLeft[i] = 0f; _toastLabels[i].style.display = DisplayStyle.None; }
            _promptShown = null;
            _prompt.style.display = DisplayStyle.None;

            _heldShown = null;
            _heldStateShown = int.MinValue;
            _hands.style.display = DisplayStyle.None;
            _catnipShown = -1;
            _catnip.style.display = DisplayStyle.None;
        }

        public void SetPromptSuppressed(bool suppressed)
        {
            _promptSuppressed = suppressed;
            if (suppressed && _prompt != null) { _prompt.style.display = DisplayStyle.None; _promptShown = null; }
        }

        private void OnObjective(string text)
        {
            if (_objective != null)
            {
                _objective.text = text ?? string.Empty;
                var parent = _objective.parent;
                if (parent != null)
                {
                    parent.AddToClassList("sn-bump");
                    parent.schedule.Execute(() => parent.RemoveFromClassList("sn-bump")).StartingIn(160);
                }
            }
        }

        private void OnAlert(AlertLevel level) => SetAlert(level);

        private void SetAlert(AlertLevel level)
        {
            bool changed = level != _alert;
            _alert = level;
            _eye.Level = level;
            _eyeLabel.text = level switch
            {
                AlertLevel.Asleep => "Zzz... спит",
                AlertLevel.Stirring => "Ворочается...",
                AlertLevel.Suspicious => "Что за шум?!",
                AlertLevel.Searching => "Ищет воришек!",
                _ => "ПОГОНЯ С ВЕНИКОМ!"
            };
            _eyeWrap.EnableInClassList("sn-eye-wrap--danger", level >= AlertLevel.Searching);
            if (changed && _eyeWrap != null)
            {
                _eyeWrap.AddToClassList("sn-bump");
                _eyeWrap.schedule.Execute(() => _eyeWrap.RemoveFromClassList("sn-bump")).StartingIn(180);
            }
            if (level != AlertLevel.Chasing)
            {
                _pulseOn = false;
                _eyeWrap.RemoveFromClassList("sn-eye-wrap--pulse");
            }
        }

        private void OnGiftDelivered(ICarryable gift, int delivered, int total)
        {
            if (_mode != GameState.Delivery) return;
            _counterDone = delivered; _counterTotal = total;
            RefreshCounter();
            BumpCounter();
        }

        private void OnGiftLoaded(int loaded, int total)
        {
            if (_mode != GameState.Hub) return;
            _counterDone = loaded; _counterTotal = total;
            RefreshCounter();
            BumpCounter();
        }

        private void OnGiftDamaged(ICarryable gift, float integrity)
        {
            if (!IsVisible || _damageToastCooldown > 0f) return;
            _damageToastCooldown = 1.5f;
            ShowToast($"Упаковка помялась: {Mathf.RoundToInt(integrity)}%");
        }

        private void RefreshCounter()
        {
            _counterLabel.text = _counterTotal > 0 ? $"{_counterDone}/{_counterTotal}" : "—";
            _counterChip.EnableInClassList("sn-chip--done", _counterTotal > 0 && _counterDone >= _counterTotal);
        }

        private void BumpCounter()
        {
            _counterChip.AddToClassList("sn-bump");
            _counterChip.schedule.Execute(() => _counterChip.RemoveFromClassList("sn-bump")).StartingIn(180);
        }

        public void ShowToast(string text)
        {
            if (string.IsNullOrEmpty(text) || _toasts == null) return;
            int i = _toastNext;
            _toastNext = (_toastNext + 1) % ToastPool;
            var l = _toastLabels[i];
            l.text = text;
            l.style.opacity = 1f;
            l.style.display = DisplayStyle.Flex;
            l.BringToFront(); // newest at the bottom of the column
            _toastLeft[i] = ToastTime;
            l.AddToClassList("sn-bump");
            l.schedule.Execute(() => l.RemoveFromClassList("sn-bump")).StartingIn(160);

            l.RemoveFromClassList("sn-toast--red");
            l.RemoveFromClassList("sn-toast--green");
            l.RemoveFromClassList("sn-toast--gold");
            string lower = text.ToLowerInvariant();
            if (lower.Contains("плюх") || lower.Contains("доставл") || lower.Contains("поднял") || lower.Contains("все подарки") || lower.Contains("успешно"))
            {
                l.AddToClassList("sn-toast--green");
            }
            else if (lower.Contains("урон") || lower.Contains("сбежал") || lower.Contains("потер") || lower.Contains("ошибк") || lower.Contains("не тот адрес"))
            {
                l.AddToClassList("sn-toast--red");
            }
            else if (lower.Contains("кооп") || lower.Contains("рейс") || lower.Contains("чаев") || lower.Contains("куплен") || lower.Contains("сани"))
            {
                l.AddToClassList("sn-toast--gold");
            }
        }

        // ------------------------------------------------------------------ tick

        public override void Tick(float dt)
        {
            if (_damageToastCooldown > 0f) _damageToastCooldown -= dt;

            for (int i = 0; i < ToastPool; i++)
            {
                if (_toastLeft[i] <= 0f) continue;
                _toastLeft[i] -= dt;
                var l = _toastLabels[i];
                if (_toastLeft[i] <= 0f) l.style.display = DisplayStyle.None;
                else if (_toastLeft[i] < ToastFade) l.style.opacity = _toastLeft[i] / ToastFade;
            }

            if (_alert == AlertLevel.Chasing)
            {
                _pulseTimer -= dt;
                if (_pulseTimer <= 0f)
                {
                    _pulseTimer = 0.35f;
                    _pulseOn = !_pulseOn;
                    _eyeWrap.EnableInClassList("sn-eye-wrap--pulse", _pulseOn);
                }
            }

            var profile = GameServices.Profile;
            int tips = profile != null ? profile.Tips : 0;
            if (tips != _tipsShown)
            {
                bool gained = tips > _tipsShown && _tipsShown >= 0;
                _tipsShown = tips;
                _tips.text = tips.ToString();
                if (gained && _walletChip != null)
                {
                    _walletChip.AddToClassList("sn-bump");
                    _walletChip.schedule.Execute(() => _walletChip.RemoveFromClassList("sn-bump")).StartingIn(180);
                }
            }

            var player = GameServices.LocalPlayer;
            if (player is Object po && po == null) player = null;
            bool hasPlayer = player != null;
            _gaugeWrap.style.display = hasPlayer ? DisplayStyle.Flex : DisplayStyle.None;
            _sackWrap.style.display = hasPlayer ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasPlayer)
            {
                if (_promptShown != null) { _promptShown = null; _prompt.style.display = DisplayStyle.None; }
                return;
            }

            // noise (loudest across all active players in co-op, smoothed toward target)
            float peakNoise = player.CurrentNoise;
            var allPlayers = GameServices.Players;
            if (allPlayers != null)
            {
                for (int i = 0; i < allPlayers.Count; i++)
                {
                    var p = allPlayers[i];
                    if (p != null && !(p is Object uo && uo == null) && p.CurrentNoise > peakNoise)
                        peakNoise = p.CurrentNoise;
                }
            }
            float target = Mathf.Clamp01(peakNoise / 100f);
            _noiseShown = Mathf.Lerp(_noiseShown, target, 1f - Mathf.Exp(-12f * dt));
            _gauge.Value01 = _noiseShown;
            float thr = GameConstants.GrandpaWakeThreshold / 100f;
            int word = _noiseShown >= thr ? 2 : _noiseShown >= thr * 0.6f ? 1 : 0;
            if (word != _noiseWordState)
            {
                _noiseWordState = word;
                _noiseWord.text = word == 2 ? "ГРОХОТ!" : word == 1 ? "ШОРОХ..." : "ТИХО";
                _noiseWord.EnableInClassList("sn-gauge__word--warn", word == 1);
                _noiseWord.EnableInClassList("sn-gauge__word--loud", word == 2);
            }

            // stamina
            float st = Mathf.Clamp01(player.Stamina01);
            if (Mathf.Abs(st - _staminaShown) > 0.004f)
            {
                _staminaShown = st;
                _staminaFill.style.width = Length.Percent(st * 100f);
                _staminaFill.EnableInClassList("sn-bar__fill--stamina-low", st < 0.22f);
            }

            // jetpack fuel
            int fv = player.HasJetpack ? 1 : 0;
            if (fv != _fuelVisible)
            {
                _fuelVisible = fv;
                _fuelRow.style.display = fv == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (fv == 1)
            {
                float fu = Mathf.Clamp01(player.JetpackFuel01);
                if (Mathf.Abs(fu - _fuelShown) > 0.004f)
                {
                    _fuelShown = fu;
                    _fuelFill.style.width = Length.Percent(fu * 100f);
                }
            }

            // sack
            var carrier = player.Carrier;
            if (carrier is Object co && co == null) carrier = null;
            int cap = carrier != null ? Mathf.Clamp(carrier.SackCapacity, 0, MaxSackSlots) : 0;
            int cnt = carrier != null ? Mathf.Clamp(carrier.SackCount, 0, cap) : 0;
            if (cap != _sackCap || cnt != _sackCount)
            {
                bool itemAdded = cnt > _sackCount && _sackCount >= 0;
                _sackCap = cap; _sackCount = cnt;
                for (int i = 0; i < MaxSackSlots; i++)
                {
                    _slots[i].style.display = i < cap ? DisplayStyle.Flex : DisplayStyle.None;
                    _slots[i].EnableInClassList("sn-slot--full", i < cnt);
                }
                if (itemAdded && cnt > 0 && cnt <= MaxSackSlots)
                {
                    var bumpedSlot = _slots[cnt - 1];
                    bumpedSlot.AddToClassList("sn-bump");
                    bumpedSlot.schedule.Execute(() => bumpedSlot.RemoveFromClassList("sn-bump")).StartingIn(180);
                }
            }

            RefreshHands(carrier);
            RefreshCatnip(profile);
            RefreshCoopHud();

            // reticle focus feedback
            if (_reticle != null)
            {
                bool hasFocus = !_promptSuppressed && player.FocusedInteractable != null;
                _reticle.EnableInClassList("sn-reticle--focus", hasFocus);
            }

            // group transition countdown banner
            var netMgr = SilentNight.Multiplayer.CoopNetworkManager.Instance;
            if (_countdownBanner != null)
            {
                if (netMgr != null && netMgr.TransitionCountdownRemaining > 0f)
                {
                    _countdownBanner.style.display = DisplayStyle.Flex;
                    _countdownTitle.text = $"{netMgr.TransitionCountdownRemaining:F1} с";
                    _countdownSub.text = netMgr.TransitionCountdownLabel ?? "ПОСАДКА ВСЕЙ КОМАНДЫ";
                    int sec = Mathf.CeilToInt(netMgr.TransitionCountdownRemaining);
                    if (sec != _lastCountdownSec)
                    {
                        _lastCountdownSec = sec;
                        _countdownBanner.AddToClassList("sn-bump");
                        _countdownBanner.schedule.Execute(() => _countdownBanner.RemoveFromClassList("sn-bump")).StartingIn(150);
                    }
                }
                else
                {
                    _countdownBanner.style.display = DisplayStyle.None;
                    _lastCountdownSec = -1;
                }
            }

            // interaction prompt
            string prompt = null;
            if (!_promptSuppressed)
            {
                var focused = player.FocusedInteractable;
                // Unity "fake null": a destroyed MonoBehaviour behind the interface.
                if (focused is Object uo && uo == null) focused = null;
                if (focused != null) prompt = focused.Prompt;
                if (string.IsNullOrEmpty(prompt)) prompt = null;
            }
            if (!string.Equals(prompt, _promptShown))
            {
                _promptShown = prompt;
                if (prompt == null) _prompt.style.display = DisplayStyle.None;
                else
                {
                    _promptText.text = prompt;
                    _prompt.style.display = DisplayStyle.Flex;
                }
            }
        }

        /// <summary>"In hands" line: gift name + kind-specific hint. Strings rebuilt only when the shown state changes.</summary>
        private void RefreshHands(ICarrier carrier)
        {
            ICarryable held = carrier != null ? carrier.Held : null;
            if (held is Object ho && ho == null) held = null;

            var gift = held as SilentNight.Gifts.Gift;
            int state = 0;
            if (held != null)
            {
                int sub = 0;
                if (held.Kind == GiftKind.Noisy && gift != null)
                    sub = Mathf.Clamp(Mathf.CeilToInt(gift.WindLeft), 0, 999);
                else if (held.Kind == GiftKind.Fragile && gift != null && gift.IsBroken)
                    sub = 1;
                state = (int)held.Kind * 1000 + sub;
            }

            if (ReferenceEquals(held, _heldShown) && state == _heldStateShown) return;
            bool sameItem = ReferenceEquals(held, _heldShown);
            _heldShown = held;
            _heldStateShown = state;

            if (held == null)
            {
                _hands.style.display = DisplayStyle.None;
                return;
            }

            if (!sameItem)
            {
                _handsTitle.text = gift != null ? gift.Label : SilentNight.Gifts.Gift.LabelFor(held.Kind);
                _hands.AddToClassList("sn-bump");
                _hands.schedule.Execute(() => _hands.RemoveFromClassList("sn-bump")).StartingIn(160);
            }

            bool danger = false;
            switch (held.Kind)
            {
                case GiftKind.Small:
                    _handsHint.text = "можно убрать в мешок [R]";
                    break;
                case GiftKind.Noisy:
                    int sec = state % 1000;
                    danger = sec < 8;
                    _handsHint.text = $"звон через {sec} с — возьмите в руки, чтобы подкрутить";
                    break;
                case GiftKind.Fragile:
                    bool broken = state % 1000 == 1;
                    danger = broken;
                    _handsHint.text = broken ? "разбит" : "хрупкое! не бегите и не падайте";
                    break;
                case GiftKind.RcCar:
                    _handsHint.text = "положите на пол [E] — уедет шуметь";
                    break;
                case GiftKind.Huge:
                    _handsHint.text = "тяжёлый! несите боком • [E] положить • вдвоём легче!";
                    break;
                default:
                    _handsHint.text = string.Empty;
                    break;
            }
            _handsHint.EnableInClassList("sn-hands__hint--danger", danger);
            _hands.style.display = DisplayStyle.Flex;
        }

        /// <summary>Catnip counter: Delivery only, shown when the player owns at least one pouch.</summary>
        private void RefreshCatnip(PlayerProfile profile)
        {
            int n = _mode == GameState.Delivery && profile != null ? profile.CatnipPouches : 0;
            if (n == _catnipShown) return;
            _catnipShown = n;
            if (n <= 0)
            {
                _catnip.style.display = DisplayStyle.None;
                return;
            }
            _catnipText.text = $"Мята: {n}";
            _catnip.style.display = DisplayStyle.Flex;
            _catnip.AddToClassList("sn-bump");
            _catnip.schedule.Execute(() => _catnip.RemoveFromClassList("sn-bump")).StartingIn(160);
        }

        private void RefreshCoopHud()
        {
            if (_coopWrap == null) return;
            var players = GameServices.Players;
            int count = players != null ? players.Count : 0;

            bool split = SilentNight.Player.ThirdPersonCameraRig.SplitScreenEnabled && count >= 2;
            if (_splitLineV != null) _splitLineV.style.display = split ? DisplayStyle.Flex : DisplayStyle.None;
            if (_splitLineH != null) _splitLineH.style.display = (split && count >= 3) ? DisplayStyle.Flex : DisplayStyle.None;

            var net = SilentNight.Multiplayer.CoopNetworkManager.Instance;
            bool isNet = net != null && net.Role != SilentNight.Multiplayer.CoopNetworkManager.NetRole.Offline;

            if (isNet)
            {
                string roleName = net.Role == SilentNight.Multiplayer.CoopNetworkManager.NetRole.Host ? "ХОСТ" : $"ЭЛЬФ {net.LocalSlot + 1}";
                _coopHeader.text = $"СЕТЕВОЙ КООП [{roleName}]  •  [F4] Меню лобби  •  Игроков: {count}";
            }
            else if (count <= 1)
            {
                _coopHeader.text = "[F4] Сетевой кооп  •  [F2] +Эльф 2 (Стрелки)  •  Геймпад [Start]";
                for (int i = 0; i < MaxCoopCards; i++)
                    _coopCards[i].style.display = DisplayStyle.None;
                return;
            }
            else
            {
                string camMode = split ? "Сплит [F3]" : "Общая [F3]";
                _coopHeader.text = $"КООП ({count}/4)  •  Камера: {camMode}  •  [F4] Сеть";
            }

            for (int i = 0; i < MaxCoopCards; i++)
            {
                if (i >= count)
                {
                    _coopCards[i].style.display = DisplayStyle.None;
                    continue;
                }
                IPlayerStatus p = players[i];
                if (p == null || (p is Object uo && uo == null) || p.Transform == null)
                {
                    _coopCards[i].style.display = DisplayStyle.None;
                    continue;
                }

                _coopCards[i].style.display = DisplayStyle.Flex;
                GameObject go = p.Transform.gameObject;
                var inp = go.GetComponent<SilentNight.Player.ElfInput>();
                var rag = go.GetComponent<SilentNight.Player.ElfRagdoll>();
                var carry = p.Carrier as SilentNight.Player.ElfCarry;

                int slotIdx = inp != null ? inp.PlayerIndex : i;
                string ctrlTag = slotIdx == 0 ? "WASD" : (inp != null && inp.IsKeyboardSecondary ? "Стрелки" : "Геймпад");
                _coopNames[i].text = $"Эльф {slotIdx + 1} ({ctrlTag})";

                _coopStaminaFills[i].style.width = Length.Percent(Mathf.Clamp01(p.Stamina01) * 100f);

                bool downed = rag != null && rag.IsDowned;
                Color statusBorder = downed
                    ? new Color(0.95f, 0.25f, 0.20f, 1f)
                    : new Color(0.16f, 0.08f, 0.04f, 0.95f);
                _coopCards[i].style.borderTopColor = _coopCards[i].style.borderRightColor = _coopCards[i].style.borderBottomColor = statusBorder;

                if (downed)
                {
                    _coopStatus[i].text = "УПАЛ! Помогите [E]/[Enter]";
                    _coopStatus[i].style.color = new Color(1f, 0.35f, 0.30f);
                    _coopCards[i].style.backgroundColor = new Color(0.38f, 0.08f, 0.08f, 0.95f);
                }
                else if (carry != null && carry.IsCoCarrying)
                {
                    _coopStatus[i].text = "<-> Несём велосипед вдвоём";
                    _coopStatus[i].style.color = new Color(1f, 0.92f, 0.55f);
                    _coopCards[i].style.backgroundColor = new Color(0.14f, 0.09f, 0.06f, 0.90f);
                }
                else
                {
                    var held = p.Carrier != null ? p.Carrier.Held : null;
                    if (held is Object ho && ho == null) held = null;
                    int sackCnt = p.Carrier != null ? p.Carrier.SackCount : 0;
                    int sackCap = p.Carrier != null ? p.Carrier.SackCapacity : 0;
                    if (held != null)
                        _coopStatus[i].text = $"В руках: {SilentNight.Gifts.Gift.LabelFor(held.Kind)}  |  Мешок {sackCnt}/{sackCap}";
                    else
                        _coopStatus[i].text = $"Пусто  |  Мешок {sackCnt}/{sackCap}";
                    _coopStatus[i].style.color = new Color(0.92f, 0.88f, 0.80f);
                    _coopCards[i].style.backgroundColor = new Color(0.14f, 0.09f, 0.06f, 0.90f);
                }

                // Show interaction prompt for extra players right on their card
                string pPrompt = null;
                if (slotIdx > 0 && !_promptSuppressed)
                {
                    var foc = p.FocusedInteractable;
                    if (foc is Object fo && fo == null) foc = null;
                    if (foc != null && !string.IsNullOrEmpty(foc.Prompt))
                    {
                        string keyHint = (inp != null && inp.IsKeyboardSecondary) ? "[Enter]" : "(X)";
                        pPrompt = $"{keyHint} {foc.Prompt}";
                    }
                }
                if (string.IsNullOrEmpty(pPrompt))
                {
                    _coopPrompts[i].style.display = DisplayStyle.None;
                }
                else
                {
                    _coopPrompts[i].text = pPrompt;
                    _coopPrompts[i].style.display = DisplayStyle.Flex;
                }
            }
        }
    }
}
