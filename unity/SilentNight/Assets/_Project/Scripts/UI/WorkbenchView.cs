using SilentNight.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>Workbench: four upgrades with 5 level pips each plus catnip pouches (3 pips = pouches carried), buy with tips (PlayerProfile.TryBuy).</summary>
    public class WorkbenchView : UIView
    {
        private struct Row
        {
            public UpgradeType Type;
            public VisualElement El;
            public VisualElement[] Pips;
            public Button Buy;
            public Label Cost;
            public IconElement CostCoin;
        }

        private static readonly UpgradeType[] Types =
            { UpgradeType.Jetpack, UpgradeType.SilentBoots, UpgradeType.Sack, UpgradeType.Grip, UpgradeType.Catnip };

        private VisualElement _panel;
        private Label _wallet;
        private Row[] _rows;
        private PlayerProfile _subscribed;

        protected override VisualElement PopTarget => _panel;

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.El("sn-backdrop");
            _panel = UIFactory.El("sn-panel sn-bench", root);

            var header = UIFactory.El("sn-panel__header", _panel);
            UIFactory.Text("ВЕРСТАК", "sn-panel__title", header);
            UIFactory.Ignored("sn-grow", header);
            var wallet = UIFactory.Ignored("sn-chip sn-chip--gold", header);
            UIFactory.Icon(IconKind.Coin, "sn-chip__icon", wallet);
            _wallet = UIFactory.Text("0", "sn-chip__value", wallet);
            var close = UIFactory.Btn("", () => Owner.RequestClosePanel(), "sn-btn--close", header);
            UIFactory.Icon(IconKind.Close, "sn-btn__icon", close);

            var list = UIFactory.El("sn-upgrades", _panel);
            _rows = new Row[Types.Length];
            for (int i = 0; i < Types.Length; i++) _rows[i] = BuildRow(list, Types[i]);

            var footer = UIFactory.El("sn-panel__footer", _panel);
            UIFactory.Btn("Доска заказов", () => Owner.RequestOpenPanel(UIRoot.PanelOrders), "sn-btn--wood", footer);
            UIFactory.Ignored("sn-grow", footer);
            UIFactory.Btn("Готово", () => Owner.RequestClosePanel(), "sn-btn--green", footer);
            return root;
        }

        private Row BuildRow(VisualElement parent, UpgradeType t)
        {
            var row = new Row { Type = t, Pips = new VisualElement[PlayerProfile.MaxLevelFor(t)] };
            row.El = UIFactory.Ignored("sn-upg", parent);

            var iconPlate = UIFactory.Ignored("sn-upg__plate", row.El);
            var icon = UIFactory.Icon(IconFor(t), "sn-upg__icon", iconPlate);
            if (t == UpgradeType.Catnip) icon.SetColors(Palette.Green, Palette.GreenDark); // green felt pouch

            var info = UIFactory.Ignored("sn-upg__info", row.El);
            UIFactory.Text(NameFor(t), "sn-upg__name", info);
            UIFactory.Text(DescFor(t), "sn-upg__desc", info);
            var pips = UIFactory.Ignored("sn-pips", info);
            for (int i = 0; i < row.Pips.Length; i++) row.Pips[i] = UIFactory.Ignored("sn-pip", pips);

            var type = t;
            row.Buy = UIFactory.Btn("", () => OnBuy(type), "sn-btn--gold sn-upg__buy", row.El);
            row.CostCoin = UIFactory.Icon(IconKind.Coin, "sn-btn__coin", row.Buy);
            row.Cost = UIFactory.Text("", "sn-btn__label", row.Buy);
            return row;
        }

        private static IconKind IconFor(UpgradeType t) => t switch
        {
            UpgradeType.Jetpack => IconKind.Jetpack,
            UpgradeType.SilentBoots => IconKind.Boot,
            UpgradeType.Sack => IconKind.Sack,
            UpgradeType.Catnip => IconKind.Sack,
            _ => IconKind.Mitten
        };

        private static string NameFor(UpgradeType t) => t switch
        {
            UpgradeType.Jetpack => "Реактивный ранец",
            UpgradeType.SilentBoots => "Бесшумные ботинки",
            UpgradeType.Sack => "Большой мешок",
            UpgradeType.Catnip => "Кошачья мята",
            _ => "Крепкий хват"
        };

        private static string DescFor(UpgradeType t) => t switch
        {
            UpgradeType.Jetpack => "Полёт на [Q] над столами и льдом",
            UpgradeType.SilentBoots => "Тише шаги, меньше скольжения",
            UpgradeType.Sack => "+1 подарок в мешке [R]",
            UpgradeType.Catnip => "Бросьте [F] рядом с котом: он забудет обо всём и будет мурлыкать 20 секунд.",
            _ => "Легче нести, меньше вмятин"
        };

        protected override void OnShow()
        {
            var p = GameServices.Profile;
            if (p != _subscribed)
            {
                if (_subscribed != null) _subscribed.Changed -= Refresh;
                _subscribed = p;
                if (p != null) p.Changed += Refresh;
            }
            Refresh();
        }

        protected override void OnHide()
        {
            if (_subscribed != null) _subscribed.Changed -= Refresh;
            _subscribed = null;
        }

        public override void Unbind() => OnHide();

        private void Refresh()
        {
            var p = GameServices.Profile;
            _wallet.text = p != null ? p.Tips.ToString() : "0";
            foreach (var r in _rows)
            {
                int lvl = p != null ? p.GetLevel(r.Type) : 0;
                for (int i = 0; i < r.Pips.Length; i++) r.Pips[i].EnableInClassList("sn-pip--on", i < lvl);
                bool maxed = lvl >= PlayerProfile.MaxLevelFor(r.Type);
                r.CostCoin.style.display = maxed ? DisplayStyle.None : DisplayStyle.Flex;
                r.Cost.text = maxed ? (r.Type == UpgradeType.Catnip ? "ПОЛНО" : "МАКС.") : (p != null ? p.GetCost(r.Type).ToString() : "—");
                bool can = p != null && p.CanBuy(r.Type);
                r.Buy.SetEnabled(can);
                r.Buy.EnableInClassList("sn-upg__buy--max", maxed);
                r.El.EnableInClassList("sn-upg--maxed", maxed);
            }
        }

        private void OnBuy(UpgradeType t)
        {
            var p = GameServices.Profile;
            if (p == null) return;
            if (p.TryBuy(t))
            {
                for (int i = 0; i < _rows.Length; i++)
                {
                    if (_rows[i].Type != t) continue;
                    var el = _rows[i].El;
                    el.AddToClassList("sn-bump");
                    el.schedule.Execute(() => el.RemoveFromClassList("sn-bump")).StartingIn(180);
                }
                if (t == UpgradeType.Catnip)
                    GameEvents.RaiseToast($"Куплено: {NameFor(t)} ({p.CatnipPouches}/{PlayerProfile.MaxCatnip})");
                else
                    GameEvents.RaiseToast("Улучшено: " + NameFor(t));
            }
            Refresh(); // also called via Profile.Changed; harmless
        }
    }
}
