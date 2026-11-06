using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>
    /// Cork/wood order board with three pinned paper orders. Spooky orders are dark purple with an "x2" stamp.
    /// Opened via GameEvents.RequestHubPanel("orders") or Tab in the Hub.
    /// </summary>
    public class OrderBoardView : UIView
    {
        private VisualElement _board;
        private VisualElement _cards;
        private Label _wallet;
        private Button _go;
        private readonly List<Order> _shown = new List<Order>();
        private readonly List<VisualElement> _cardEls = new List<VisualElement>();

        protected override VisualElement PopTarget => _board;

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.El("sn-backdrop");
            _board = UIFactory.El("sn-panel sn-board", root);

            var header = UIFactory.El("sn-panel__header", _board);
            UIFactory.Text("ДОСКА ЗАКАЗОВ", "sn-panel__title", header);
            UIFactory.Ignored("sn-grow", header);
            var wallet = UIFactory.Ignored("sn-chip sn-chip--gold", header);
            UIFactory.Icon(IconKind.Coin, "sn-chip__icon", wallet);
            _wallet = UIFactory.Text("0", "sn-chip__value", wallet);
            var close = UIFactory.Btn("", () => Owner.RequestClosePanel(), "sn-btn--close", header);
            UIFactory.Icon(IconKind.Close, "sn-btn__icon", close);

            _cards = UIFactory.El("sn-cards", _board);

            var footer = UIFactory.El("sn-panel__footer", _board);
            UIFactory.Btn("Новые адреса", OnReroll, "sn-btn--green", footer);
            UIFactory.Btn("Верстак", () => Owner.RequestOpenPanel(UIRoot.PanelWorkbench), "sn-btn--wood", footer);
            UIFactory.Ignored("sn-grow", footer);
            _go = UIFactory.Btn("В рейс!", OnGo, "sn-btn--red sn-btn--big", footer);
            return root;
        }

        protected override void OnShow() => Rebuild();

        private void Rebuild()
        {
            var p = GameServices.Profile;
            _wallet.text = p != null ? p.Tips.ToString() : "0";

            _cards.Clear();
            _cardEls.Clear();
            _shown.Clear();
            _shown.AddRange(GameServices.AvailableOrders);
            if (GameServices.ActiveOrder == null && _shown.Count > 0) GameServices.ActiveOrder = _shown[0];

            for (int i = 0; i < _shown.Count; i++)
                _cardEls.Add(BuildCard(_shown[i], i));
            if (_shown.Count == 0)
                UIFactory.Text("Заказов пока нет", "sn-cards__empty", _cards);

            RefreshSelection();
        }

        private VisualElement BuildCard(Order o, int index)
        {
            bool spooky = o.Mood == HouseMood.Spooky;
            var card = UIFactory.Btn("", () => Select(index), "sn-card sn-card--tilt" + (index % 3), _cards);
            card.RemoveFromClassList("sn-btn"); // a card is not a chunky button
            if (spooky) card.AddToClassList("sn-card--spooky");

            UIFactory.Ignored("sn-card__pin", card);
            UIFactory.Text(spooky ? "ЖУТКИЙ ДОМ" : "УЮТНЫЙ ДОМ", "sn-card__mood", card);
            UIFactory.Text(o.Address ?? "", "sn-card__address", card);
            UIFactory.Text(o.Description ?? "", "sn-card__desc", card);

            var gifts = UIFactory.Ignored("sn-card__gifts", card);
            foreach (var g in o.Gifts)
                UIFactory.Icon(Palette.GiftIcon(g), "sn-card__gift" + (g == GiftKind.Huge ? " sn-card__gift--huge" : ""), gifts);

            var reward = UIFactory.Ignored("sn-card__reward", card);
            UIFactory.Icon(IconKind.Coin, "sn-card__coin", reward);
            UIFactory.Text(o.BaseReward.ToString(), "sn-card__money", reward);
            if (!spooky && o.TipMultiplier > 1.001f)
                UIFactory.Text("×" + o.TipMultiplier.ToString("0.##"), "sn-card__mult", reward);

            if (spooky) UIFactory.Text("x2", "sn-card__stamp", card);
            UIFactory.Ignored("sn-card__check", card);
            return card;
        }

        private void Select(int index)
        {
            if (index < 0 || index >= _shown.Count) return;
            GameServices.ActiveOrder = _shown[index];
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            var active = GameServices.ActiveOrder;
            for (int i = 0; i < _cardEls.Count; i++)
                _cardEls[i].EnableInClassList("sn-card--selected", _shown[i] == active);
            _go.SetEnabled(active != null);
        }

        private void OnReroll()
        {
            GameServices.RegenerateOrders();
            Rebuild();
        }

        private void OnGo()
        {
            var order = GameServices.ActiveOrder;
            if (order == null) return;
            Owner.RequestClosePanel();
            GameEvents.RaiseRequestStartDelivery(order);
        }

        public override void Tick(float dt)
        {
            // keep the wallet in sync if tips change while the board is open (cheap int compare)
            var p = GameServices.Profile;
            if (p != null && p.Tips != _lastTips)
            {
                _lastTips = p.Tips;
                _wallet.text = p.Tips.ToString();
            }
        }

        private int _lastTips = int.MinValue;
    }
}
