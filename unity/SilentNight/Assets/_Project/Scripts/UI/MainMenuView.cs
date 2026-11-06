using SilentNight.Core;
using UnityEngine.UIElements;

namespace SilentNight.UI
{
    /// <summary>Title screen over the Hub scene: big wooden sign, two chunky buttons, key caps strip.</summary>
    public class MainMenuView : UIView
    {
        private VisualElement _sign;
        protected override VisualElement PopTarget => _sign;

        protected override VisualElement CreateRoot()
        {
            var root = UIFactory.El("sn-screen sn-screen--menu");

            _sign = UIFactory.El("sn-sign", root);
            UIFactory.Ignored("sn-sign__rope sn-sign__rope--l", _sign);
            UIFactory.Ignored("sn-sign__rope sn-sign__rope--r", _sign);
            var stars = UIFactory.Ignored("sn-sign__stars", _sign);
            UIFactory.Icon(IconKind.Star, "sn-sign__star sn-sign__star--small", stars);
            UIFactory.Icon(IconKind.Star, "sn-sign__star", stars);
            UIFactory.Icon(IconKind.Star, "sn-sign__star sn-sign__star--small", stars);
            UIFactory.Text("OPERATION:", "sn-title-kicker", _sign);
            UIFactory.Text("SILENT NIGHT", "sn-title-big", _sign);
            UIFactory.Text("Эльфийский стелс", "sn-title-sub", _sign);

            var buttons = UIFactory.El("sn-menu-buttons", root);
            UIFactory.Btn("Начать смену", OnStart, "sn-btn--red sn-btn--huge", buttons);
            UIFactory.Btn("Выход", OnQuit, "sn-btn--wood", buttons);

            var keys = UIFactory.Ignored("sn-keys", root);
            AddKey(keys, "WASD", "идти");
            AddKey(keys, "Пробел", "прыжок");
            AddKey(keys, "C", "красться");
            AddKey(keys, "E", "взять");
            AddKey(keys, "R", "мешок");
            AddKey(keys, "Q", "ранец");
            AddKey(keys, "Tab", "заказы");
            return root;
        }

        private static void AddKey(VisualElement parent, string key, string what)
        {
            var g = UIFactory.Ignored("sn-keys__item", parent);
            UIFactory.Key(key, g);
            UIFactory.Text(what, "sn-keys__text", g);
        }

        private static void OnStart() => GameServices.Flow?.StartShift();

        private static void OnQuit()
        {
            if (GameServices.Flow != null) GameServices.Flow.QuitGame();
            else UnityEngine.Application.Quit();
        }
    }
}
