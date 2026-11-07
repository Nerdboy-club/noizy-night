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

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.Ignored("sn-hud");

            // ---- objective, top-left
            var obj = UIFactory.Ignored("sn-objective", root);
            UIFactory.Ignored("sn-objective__mark", obj);
            _objective = UIFactory.Text("", "sn-objective__text", obj);

            // ---- top-right: wallet + counter
            var tr = UIFactory.Ignored("sn-topright", root);
            var wallet = UIFactory.Ignored("sn-chip sn-chip--gold", tr);
            UIFactory.Icon(IconKind.Coin, "sn-chip__icon", wallet);
            _tips = UIFactory.Text("0", "sn-chip__value", wallet);
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
            if (_objective != null) _objective.text = text ?? string.Empty;
        }

        private void OnAlert(AlertLevel level) => SetAlert(level);

        private void SetAlert(AlertLevel level)
        {
            _alert = level;
            _eye.Level = level;
            _eyeLabel.text = level switch
            {
                AlertLevel.Asleep => "Zzz",
                AlertLevel.Stirring => "Ворочается",
                AlertLevel.Suspicious => "Что за шум?",
                AlertLevel.Searching => "Ищет!",
                _ => "ПОГОНЯ!"
            };
            _eyeWrap.EnableInClassList("sn-eye-wrap--danger", level >= AlertLevel.Searching);
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
                _tipsShown = tips;
                _tips.text = tips.ToString();
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

            // noise (smoothed toward target so the needle wobbles nicely)
            float target = Mathf.Clamp01(player.CurrentNoise / 100f);
            _noiseShown = Mathf.Lerp(_noiseShown, target, 1f - Mathf.Exp(-12f * dt));
            _gauge.Value01 = _noiseShown;
            float thr = GameConstants.GrandpaWakeThreshold / 100f;
            int word = _noiseShown >= thr ? 2 : _noiseShown >= thr * 0.6f ? 1 : 0;
            if (word != _noiseWordState)
            {
                _noiseWordState = word;
                _noiseWord.text = word == 2 ? "ГРОМКО!" : word == 1 ? "ОСТОРОЖНО" : "ТИХО";
                _noiseWord.EnableInClassList("sn-gauge__word--warn", word == 1);
                _noiseWord.EnableInClassList("sn-gauge__word--loud", word == 2);
            }

            // stamina
            float st = Mathf.Clamp01(player.Stamina01);
            if (Mathf.Abs(st - _staminaShown) > 0.004f)
            {
                _staminaShown = st;
                _staminaFill.style.width = Length.Percent(st * 100f);
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
                _sackCap = cap; _sackCount = cnt;
                for (int i = 0; i < MaxSackSlots; i++)
                {
                    _slots[i].style.display = i < cap ? DisplayStyle.Flex : DisplayStyle.None;
                    _slots[i].EnableInClassList("sn-slot--full", i < cnt);
                }
            }

            RefreshHands(carrier);
            RefreshCatnip(profile);

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
                _handsTitle.text = gift != null ? gift.Label : SilentNight.Gifts.Gift.LabelFor(held.Kind);

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
                    _handsHint.text = "несите боком через двери";
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
        }
    }
}
