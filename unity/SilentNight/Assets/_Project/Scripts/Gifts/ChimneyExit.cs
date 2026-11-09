using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Gifts
{
    /// <summary>
    /// Fireplace in the house. [E] always returns to the hub; before all gifts are delivered
    /// it is an escape without payment.
    /// </summary>
    public class ChimneyExit : InteractableBase
    {
        [SerializeField] private string returnPrompt = "Вернуться на базу";
        [SerializeField] private string escapePrompt = "Сбежать через камин (без оплаты)";

        private int _delivered;
        private int _total = -1;

        protected override void Awake()
        {
            prompt = returnPrompt;
            base.Awake();
        }

        private void OnEnable() => GameEvents.GiftDelivered += OnGiftDelivered;
        private void OnDisable() => GameEvents.GiftDelivered -= OnGiftDelivered;

        private void OnGiftDelivered(ICarryable gift, int delivered, int total)
        {
            _delivered = delivered;
            _total = total;
        }

        private int Total
        {
            get
            {
                if (_total >= 0) return _total;
                if (HouseGiftSpawner.LastSpawnCount > 0) return HouseGiftSpawner.LastSpawnCount;
                var o = GameServices.ActiveOrder;
                return o != null ? o.Gifts.Count : 0;
            }
        }

        public bool AllDelivered => _delivered >= Total;

        public override string Prompt => AllDelivered ? returnPrompt : escapePrompt;

        public override bool CanInteract(IInteractor interactor) => isActiveAndEnabled;

        public override void Interact(IInteractor interactor)
        {
            if (!AllDelivered) GameEvents.RaiseToast("Эльф сбежал через камин. Заказ не оплачен.");
            SilentNight.Multiplayer.CoopNetworkManager.RequestSynchronizedReturnToHub(!AllDelivered);
        }
    }
}
