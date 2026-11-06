using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>Shared "may the player act right now?" check for all player components.</summary>
    public static class PlayerGate
    {
        /// <summary>True when there is no GameFlow (scene started standalone) or the game is in Hub/Delivery.</summary>
        public static bool CanControl
        {
            get
            {
                GameFlow f = GameServices.Flow;
                if (GameServices.UIModalOpen) return false;
                if (f == null) return true;
                return f.State == GameState.Hub || f.State == GameState.Delivery;
            }
        }

        /// <summary>Mouse look is only allowed while the cursor is locked (UI panels unlock it).</summary>
        public static bool CanMouseLook => CanControl && Cursor.lockState == CursorLockMode.Locked;

        /// <summary>True if the UnityEngine.Object behind an interface reference is still alive.</summary>
        public static bool IsAlive(object o)
        {
            if (o == null) return false;
            if (o is Object uo) return uo != null; // Unity's overloaded null check (destroyed objects)
            return true;
        }
    }
}
