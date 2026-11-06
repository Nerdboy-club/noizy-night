using SilentNight.Core;

namespace SilentNight.Gifts
{
    /// <summary>Hub workbench: [E] opens the upgrades panel (UI listens to RequestHubPanel).</summary>
    public class WorkbenchStation : InteractableBase
    {
        protected override void Awake()
        {
            if (string.IsNullOrEmpty(prompt) || prompt == "Взаимодействовать") prompt = "Верстак улучшений";
            base.Awake();
        }

        public override void Interact(IInteractor interactor) => GameEvents.RaiseRequestHubPanel("workbench");
    }
}
