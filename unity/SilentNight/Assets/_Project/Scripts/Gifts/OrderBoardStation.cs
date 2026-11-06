using SilentNight.Core;

namespace SilentNight.Gifts
{
    /// <summary>Hub order board: [E] opens the orders panel (UI listens to RequestHubPanel).</summary>
    public class OrderBoardStation : InteractableBase
    {
        protected override void Awake()
        {
            if (string.IsNullOrEmpty(prompt) || prompt == "Взаимодействовать") prompt = "Доска заказов";
            base.Awake();
        }

        public override void Interact(IInteractor interactor) => GameEvents.RaiseRequestHubPanel("orders");
    }
}
