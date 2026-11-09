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

        private static readonly List<IPlayerStatus> _players = new List<IPlayerStatus>(4);
        private static readonly List<IPlayerStatus> _localPlayers = new List<IPlayerStatus>(4);

        /// <summary>All registered players (local and, later, remote), in registration order. Index with a for-loop to avoid allocations.</summary>
        public static IReadOnlyList<IPlayerStatus> Players => _players;
        /// <summary>Players controlled locally on this machine (used for local camera group framing).</summary>
        public static IReadOnlyList<IPlayerStatus> LocalPlayers => _localPlayers;

        public static void RegisterProfile(PlayerProfile p) => Profile = p;

        /// <summary>
        /// Adds a player (no duplicates). LocalPlayer stays the first registered local player;
        /// it is only assigned here when there is none yet.
        /// </summary>
        public static void RegisterPlayer(IPlayerStatus p, bool isLocal = true)
        {
            if (p == null) return;
            if (!_players.Contains(p)) _players.Add(p);
            if (isLocal && !_localPlayers.Contains(p)) _localPlayers.Add(p);
            if (LocalPlayer == null && _localPlayers.Count > 0) LocalPlayer = _localPlayers[0];
        }

        /// <summary>Removes a player. If it was LocalPlayer, the next registered local player (or null) takes over.</summary>
        public static void UnregisterPlayer(IPlayerStatus p)
        {
            if (p == null) return;
            _players.Remove(p);
            _localPlayers.Remove(p);
            if (LocalPlayer == p) LocalPlayer = _localPlayers.Count > 0 ? _localPlayers[0] : null;
        }

        /// <summary>Legacy single-player API; same as RegisterPlayer(p, isLocal: true).</summary>
        public static void RegisterLocalPlayer(IPlayerStatus p) => RegisterPlayer(p, true);
        /// <summary>Legacy single-player API; same as UnregisterPlayer(p).</summary>
        public static void UnregisterLocalPlayer(IPlayerStatus p) => UnregisterPlayer(p);

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
            _players.Clear(); _localPlayers.Clear();
            GameEvents.ResetAll();
        }
    }
}
