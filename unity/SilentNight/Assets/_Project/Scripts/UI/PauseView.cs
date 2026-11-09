using SilentNight.Core;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>Pause plaque: continue, give up the run (from a delivery), quit.</summary>
    public class PauseView : UIView
    {
        private VisualElement _dialog;
        private Button _giveUp;
        private GameState _from = GameState.Hub;

        protected override VisualElement PopTarget => _dialog;

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.El("sn-backdrop sn-backdrop--dark");
            _dialog = UIFactory.El("sn-dialog", root);
            UIFactory.Text("ПАУЗА", "sn-dialog__ribbon", _dialog);
            var col = UIFactory.El("sn-dialog__buttons", _dialog);
            UIFactory.Btn("Продолжить", () => GameServices.Flow?.TogglePause(), "sn-btn--green sn-btn--big", col);
            UIFactory.Btn("Сетевой кооп [F4]", () => SilentNight.Multiplayer.CoopNetworkManager.ToggleWindow(), "sn-btn--wood", col);
            _giveUp = UIFactory.Btn("Сдаться — на базу", () => SilentNight.Multiplayer.CoopNetworkManager.RequestSynchronizedReturnToHub(true), "sn-btn--wood", col);
            UIFactory.Btn("Выход", OnQuit, "sn-btn--red", col);
            UIFactory.Text("Esc — продолжить", "sn-dialog__hint", _dialog);
            return root;
        }

        /// <summary>State the game was in before pausing.</summary>
        public void SetContext(GameState from) => _from = from;

        protected override void OnShow()
        {
            _giveUp.style.display = _from == GameState.Delivery ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void OnQuit()
        {
            if (GameServices.Flow != null) GameServices.Flow.QuitGame();
            else UnityEngine.Application.Quit();
        }
    }
}
