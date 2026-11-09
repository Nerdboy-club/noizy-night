using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Hub sleigh. [E] loads everything the elf carries (hands + sack) into the sleigh.
    /// Once every gift of the active order is loaded, [E] starts the delivery flight.
    /// </summary>
    public class SleighLoadZone : InteractableBase
    {
        [SerializeField] private string loadPrompt = "Погрузить подарки в сани";
        [SerializeField] private string departPrompt = "Отправиться в рейс!";
        [SerializeField] private string noOrderPrompt = "Выбрать заказ на доске";

        private readonly int[] _need = new int[8];
        private string _cachedPrompt;
        private int _cachedLoaded = -1, _cachedTotal = -1;
        private Order _cachedOrder;

        private static int Total => GameServices.ActiveOrder != null ? GameServices.ActiveOrder.Gifts.Count : 0;
        private static int Loaded => GameServices.LoadedGifts.Count;
        public bool AllLoaded => GameServices.ActiveOrder != null && Total > 0 && Loaded >= Total;

        protected override void Awake()
        {
            prompt = loadPrompt;
            base.Awake();
        }

        public override string Prompt
        {
            get
            {
                var order = GameServices.ActiveOrder;
                int loaded = Loaded, total = Total;
                if (_cachedPrompt == null || order != _cachedOrder || loaded != _cachedLoaded || total != _cachedTotal)
                {
                    _cachedOrder = order; _cachedLoaded = loaded; _cachedTotal = total;
                    if (order == null) _cachedPrompt = noOrderPrompt;
                    else if (AllLoaded) _cachedPrompt = departPrompt;
                    else _cachedPrompt = $"{loadPrompt} ({loaded}/{total})";
                }
                return _cachedPrompt;
            }
        }

        public override bool CanInteract(IInteractor interactor) => isActiveAndEnabled;

        public override void Interact(IInteractor interactor)
        {
            var order = GameServices.ActiveOrder;
            if (order == null)
            {
                GameEvents.RaiseRequestHubPanel("orders");
                return;
            }

            var carrier = CarrierOf(interactor);
            if (HasAnything(carrier))
            {
                LoadFrom(carrier, order);
                return;
            }

            if (AllLoaded)
            {
                SilentNight.Multiplayer.CoopNetworkManager.RequestSynchronizedDeparture(order);
                return;
            }

            GameEvents.RaiseToast("Принесите подарки с конвейера");
        }

        private void LoadFrom(ICarrier carrier, Order order)
        {
            // Remaining count per kind for this order.
            System.Array.Clear(_need, 0, _need.Length);
            for (int i = 0; i < order.Gifts.Count; i++) _need[(int)order.Gifts[i]]++;
            for (int i = 0; i < GameServices.LoadedGifts.Count; i++) _need[(int)GameServices.LoadedGifts[i]]--;

            var items = carrier.TakeAll();
            if (items == null) return;

            int added = 0, rejected = 0;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null) continue;
                int k = (int)item.Kind;
                if (_need[k] > 0)
                {
                    _need[k]--;
                    GameServices.LoadedGifts.Add(item.Kind);
                    added++;
                    SilentNight.Multiplayer.CoopNetworkManager.Instance?.NotifySleighLoaded(item.Kind);
                }
                else rejected++;

                if (item.GameObject != null) Destroy(item.GameObject);
            }

            int total = order.Gifts.Count;
            if (added > 0) GameEvents.RaiseGiftLoaded(GameServices.LoadedGifts.Count, total);
            if (rejected > 0) GameEvents.RaiseToast("Лишний подарок не из этого заказа — вернули на склад");

            if (GameServices.LoadedGifts.Count >= total)
            {
                GameEvents.RaiseObjective("Все подарки в санях! Нажмите [E] у саней, чтобы отправиться в рейс");
            }
            else if (added > 0)
            {
                GameEvents.RaiseObjective($"Погружено подарков: {GameServices.LoadedGifts.Count}/{total}");
            }
        }
    }
}
