using SilentNight.Core;
using SilentNight.Gifts;
using UnityEngine;

namespace SilentNight.Rooftop
{
    /// <summary>
    /// A chimney on the rooftops. [E] dives in: the right house (number from the order address) loads the house scene,
    /// a wrong one only shows a hint. Numbers are assigned at runtime by RooftopLevelController.
    /// </summary>
    public class RooftopChimney : InteractableBase
    {
        [SerializeField] private TextMesh numberLabel;

        public int HouseNumber { get; private set; }
        public bool IsTarget { get; private set; }
        public static int TargetNumber { get; set; }

        public void Setup(int houseNumber, bool isTarget)
        {
            HouseNumber = houseNumber;
            IsTarget = isTarget;
            if (numberLabel != null) numberLabel.text = "№ " + houseNumber;
        }

        public override string Prompt => IsTarget ? "Нырнуть в дымоход" : "Заглянуть в дымоход (дом № " + HouseNumber + ")";

        public override void Interact(IInteractor interactor)
        {
            if (IsTarget)
            {
                GameEvents.RaiseToast("Плюх! Прямо в камин.");
                GameEvents.RaiseRequestEnterHouse();
            }
            else
            {
                GameEvents.RaiseToast("Дом № " + HouseNumber + " — не тот адрес. Ищите № " + TargetNumber + ".");
            }
        }
    }
}
