using SilentNight.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>Delivery summary: stars by packaging integrity, address, earned tips, back to hub.</summary>
    public class ResultsView : UIView
    {
        private VisualElement _dialog;
        private readonly IconElement[] _stars = new IconElement[3];
        private Label _address;
        private Label _integrity;
        private Label _earned;
        private Label _bonus;
        private Label _rank;
        private readonly Label[] _recapLines = new Label[5];

        protected override VisualElement PopTarget => _dialog;

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.El("sn-backdrop sn-backdrop--dark");
            _dialog = UIFactory.El("sn-dialog sn-dialog--gold", root);
            UIFactory.Text("ЗАКАЗ ДОСТАВЛЕН!", "sn-dialog__ribbon", _dialog);

            var stars = UIFactory.Ignored("sn-stars", _dialog);
            for (int i = 0; i < 3; i++)
                _stars[i] = UIFactory.Icon(IconKind.Star, i == 1 ? "sn-star sn-star--mid" : "sn-star", stars);

            _address = UIFactory.Text("", "sn-dialog__address", _dialog);

            var stats = UIFactory.Ignored("sn-stats", _dialog);
            var r1 = UIFactory.Ignored("sn-stat", stats);
            UIFactory.Text("Упаковка", "sn-stat__name", r1);
            _integrity = UIFactory.Text("100%", "sn-stat__value", r1);
            var r2 = UIFactory.Ignored("sn-stat sn-stat--money", stats);
            UIFactory.Text("Чаевые", "sn-stat__name", r2);
            UIFactory.Icon(IconKind.Coin, "sn-stat__coin", r2);
            _earned = UIFactory.Text("+0", "sn-stat__value sn-stat__value--big", r2);
            _bonus = UIFactory.Text("ЖУТКИЙ ДОМ: чаевые x2", "sn-dialog__bonus", _dialog);

            // Comedic run recap (RunStats).
            var recap = UIFactory.Ignored("sn-recap", _dialog);
            var rankRow = UIFactory.Ignored("sn-recap__rank-row", recap);
            UIFactory.Text("Звание:", "sn-recap__rank-label", rankRow);
            _rank = UIFactory.Text("", "sn-recap__rank", rankRow);
            for (int i = 0; i < _recapLines.Length; i++)
                _recapLines[i] = UIFactory.Text("", "sn-recap__line", recap);

            UIFactory.Btn("На базу", () => SilentNight.Multiplayer.CoopNetworkManager.RequestSynchronizedReturnToHub(false), "sn-btn--green sn-btn--big", _dialog);
            return root;
        }

        public override void Bind()
        {
            GameEvents.DeliveryCompleted += OnCompleted;
            RunStats.Subscribe(); // idempotent; re-hooks after GameEvents.ResetAll()
        }

        public override void Unbind() => GameEvents.DeliveryCompleted -= OnCompleted;

        protected override void OnShow()
        {
            // DeliveryCompleted is raised right after the state switch; show the active order meanwhile.
            Fill(GameServices.ActiveOrder, 100f, 0);
        }

        private void OnCompleted(Order order, float integrity, int earned) => Fill(order, integrity, earned);

        private void Fill(Order order, float integrity, int earned)
        {
            _address.text = order != null ? order.Address : "";
            int pct = Mathf.RoundToInt(Mathf.Clamp(integrity, 0f, 100f));
            _integrity.text = pct + "%";
            _earned.text = "+" + earned;
            int stars = pct >= 90 ? 3 : pct >= 60 ? 2 : 1;
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                bool lit = idx < stars;
                _stars[idx].SetColors(lit ? Palette.Gold : Palette.Unlit, Palette.GoldDark);
                if (lit)
                {
                    _stars[idx].AddToClassList("sn-bump");
                    _stars[idx].schedule.Execute(() => _stars[idx].RemoveFromClassList("sn-bump")).StartingIn(120 + idx * 100);
                }
            }
            _bonus.style.display = order != null && order.Mood == HouseMood.Spooky ? DisplayStyle.Flex : DisplayStyle.None;
            FillRecap();
        }

        private void FillRecap()
        {
            var rank = RunStats.Rank;
            _rank.text = RunStats.TitleFor(rank);
            _rank.EnableInClassList("sn-recap__rank--mouse", rank == StealthRank.QuietAsMouse);
            _rank.EnableInClassList("sn-recap__rank--ninja", rank == StealthRank.AlmostNinja);
            _rank.EnableInClassList("sn-recap__rank--elephant", rank == StealthRank.BullInChinaShop);
            _rank.AddToClassList("sn-bump");
            _rank.schedule.Execute(() => _rank.RemoveFromClassList("sn-bump")).StartingIn(220);

            var lines = RunStats.BuildRecapLines(_recapLines.Length);
            for (int i = 0; i < _recapLines.Length; i++)
            {
                bool has = i < lines.Count;
                _recapLines[i].text = has ? "• " + lines[i] : "";
                _recapLines[i].style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
