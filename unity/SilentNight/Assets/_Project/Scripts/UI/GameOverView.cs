using SilentNight.Core;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>Caught by grandpa: red plaque, wide-open eye, reason text, back to hub.</summary>
    public class GameOverView : UIView
    {
        private const string DefaultReason = "Ворчливый Дед заметил вас и прогнал обратно в камин!";

        private VisualElement _dialog;
        private Label _reason;
        private string _lastReason = DefaultReason;

        protected override VisualElement PopTarget => _dialog;

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.El("sn-backdrop sn-backdrop--red");
            _dialog = UIFactory.El("sn-dialog sn-dialog--red", root);
            UIFactory.Text("РЕЙС СОРВАН!", "sn-dialog__ribbon sn-dialog__ribbon--red", _dialog);
            var eye = new AlertEyeElement { Level = AlertLevel.Chasing };
            eye.AddToClassList("sn-eye--big");
            _dialog.Add(eye);
            _reason = UIFactory.Text(DefaultReason, "sn-dialog__text", _dialog);
            UIFactory.Btn("На базу", () => GameEvents.RaiseRequestReturnToHub(), "sn-btn--red sn-btn--big", _dialog);
            return root;
        }

        public override void Bind() => GameEvents.PlayerCaught += OnCaught;
        public override void Unbind() => GameEvents.PlayerCaught -= OnCaught;

        // GameFlow switches to GameOver inside its own PlayerCaught handler (before ours),
        // so the label is refreshed whenever the reason arrives.
        private void OnCaught(string reason)
        {
            _lastReason = string.IsNullOrEmpty(reason) ? DefaultReason : reason;
            if (_reason != null) _reason.text = _lastReason;
        }

        protected override void OnShow() => _reason.text = _lastReason;

        protected override void OnHide() => _lastReason = DefaultReason;
    }
}
