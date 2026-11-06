using System.Collections.Generic;
using UnityEngine;

namespace SilentNight.Core
{
    /// <summary>Tiny service registry so modules don't need FindObjectOfType or hard references.</summary>
    public static class GameServices
    {
        public static PlayerProfile Profile { get; private set; }
        public static IPlayerStatus LocalPlayer { get; private set; }
        public static GameFlow Flow { get; private set; }

        /// <summary>True while a UI panel (order board, workbench) is open over gameplay. Set by UI, read by Player.</summary>
        public static bool UIModalOpen { get; set; }

        /// <summary>Order chosen for the current/next delivery. Survives scene loads.</summary>
        public static Order ActiveOrder { get; set; }
        /// <summary>Order board contents (3 orders).</summary>
        public static readonly List<Order> AvailableOrders = new List<Order>();
        /// <summary>Gift kinds loaded into the sleigh in the hub; spawned in the house on arrival.</summary>
        public static readonly List<GiftKind> LoadedGifts = new List<GiftKind>();

        public static void RegisterProfile(PlayerProfile p) => Profile = p;
        public static void RegisterLocalPlayer(IPlayerStatus p) => LocalPlayer = p;
        public static void UnregisterLocalPlayer(IPlayerStatus p) { if (LocalPlayer == p) LocalPlayer = null; }
        public static void RegisterFlow(GameFlow f) => Flow = f;

        public static void RegenerateOrders()
        {
            AvailableOrders.Clear();
            var rng = new System.Random();
            for (int i = 0; i < 3; i++) AvailableOrders.Add(Order.Generate(rng));
            if (AvailableOrders.Count > 0 && AvailableOrders.TrueForAll(o => o.Mood == HouseMood.Cozy) && rng.NextDouble() < 0.3)
            {
                AvailableOrders[2] = Order.Generate(rng);
            }
            ActiveOrder = AvailableOrders[0];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Profile = null; LocalPlayer = null; Flow = null; ActiveOrder = null; UIModalOpen = false;
            AvailableOrders.Clear(); LoadedGifts.Clear();
            GameEvents.ResetAll();
        }
    }
}
