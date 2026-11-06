using System;

namespace SilentNight.Core
{
    /// <summary>
    /// Global event bus. Modules talk to each other ONLY through these events and GameServices,
    /// never by finding each other's concrete classes.
    /// Always unsubscribe in OnDisable/OnDestroy.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>Any audible sound. Raised by player, gifts, cat, surfaces. Listened by Grandpa, HUD, audio.</summary>
        public static event Action<NoiseEvent> Noise;
        public static void RaiseNoise(NoiseEvent e) => Noise?.Invoke(e);

        /// <summary>(previous, next)</summary>
        public static event Action<GameState, GameState> StateChanged;
        public static void RaiseStateChanged(GameState prev, GameState next) => StateChanged?.Invoke(prev, next);

        public static event Action<AlertLevel> GrandpaAlertChanged;
        public static void RaiseGrandpaAlertChanged(AlertLevel level) => GrandpaAlertChanged?.Invoke(level);

        /// <summary>Gift put under the tree. Args: gift, delivered count, total required.</summary>
        public static event Action<ICarryable, int, int> GiftDelivered;
        public static void RaiseGiftDelivered(ICarryable gift, int delivered, int total) => GiftDelivered?.Invoke(gift, delivered, total);

        /// <summary>Gift loaded into the sleigh in the hub. Args: loaded count, total required.</summary>
        public static event Action<int, int> GiftLoaded;
        public static void RaiseGiftLoaded(int loaded, int total) => GiftLoaded?.Invoke(loaded, total);

        /// <summary>Gift took damage. Args: gift, new integrity 0..100.</summary>
        public static event Action<ICarryable, float> GiftDamaged;
        public static void RaiseGiftDamaged(ICarryable gift, float integrity) => GiftDamaged?.Invoke(gift, integrity);

        /// <summary>Short message for the HUD objective/toast line.</summary>
        public static event Action<string> Objective;
        public static void RaiseObjective(string text) => Objective?.Invoke(text);

        public static event Action<string> Toast;
        public static void RaiseToast(string text) => Toast?.Invoke(text);

        /// <summary>Player was caught. Arg: reason text.</summary>
        public static event Action<string> PlayerCaught;
        public static void RaisePlayerCaught(string reason) => PlayerCaught?.Invoke(reason);

        /// <summary>Delivery finished successfully. Args: order, avg integrity 0..100, earned tips.</summary>
        public static event Action<Order, float, int> DeliveryCompleted;
        public static void RaiseDeliveryCompleted(Order order, float avgIntegrity, int earned) => DeliveryCompleted?.Invoke(order, avgIntegrity, earned);

        /// <summary>UI requests (buttons) — GameFlow listens.</summary>
        public static event Action<Order> RequestStartDelivery;
        public static void RaiseRequestStartDelivery(Order o) => RequestStartDelivery?.Invoke(o);

        /// <summary>Elf dives into the correct chimney on the rooftops: load the house.</summary>
        public static event Action RequestEnterHouse;
        public static void RaiseRequestEnterHouse() => RequestEnterHouse?.Invoke();

        public static event Action RequestReturnToHub;
        public static void RaiseRequestReturnToHub() => RequestReturnToHub?.Invoke();

        /// <summary>Open/close hub menus. Arg: which panel ("orders", "workbench") or null to close.</summary>
        public static event Action<string> RequestHubPanel;
        public static void RaiseRequestHubPanel(string panel) => RequestHubPanel?.Invoke(panel);

        /// <summary>Clear all subscribers (call on domain reload / app quit to avoid leaks in editor).</summary>
        public static void ResetAll()
        {
            Noise = null; StateChanged = null; GrandpaAlertChanged = null; GiftDelivered = null;
            GiftLoaded = null; GiftDamaged = null; Objective = null; Toast = null; PlayerCaught = null;
            DeliveryCompleted = null; RequestStartDelivery = null; RequestEnterHouse = null; RequestReturnToHub = null; RequestHubPanel = null;
        }
    }
}
