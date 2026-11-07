using System;
using System.IO;
using UnityEngine;

namespace SilentNight.Core
{
    /// <summary>Persistent progression: wallet and upgrade levels. Saved as JSON in persistentDataPath.</summary>
    [Serializable]
    public class PlayerProfile
    {
        public const int MaxUpgradeLevel = 5;
        /// <summary>Catnip pouches the elf can carry at once.</summary>
        public const int MaxCatnip = 3;
        /// <summary>Price of one catnip pouch (flat).</summary>
        public const int CatnipCost = 40;

        public int Tips = 120;
        public int CompletedRuns;
        public int JetpackLevel;      // 0 = no jetpack
        public int SilentBootsLevel;  // reduces footstep noise and slipping
        public int SackLevel;         // capacity = 1 + SackLevel
        public int GripLevel;         // carry heavy gifts faster / less damage
        public float GadgetDurability = 100f;
        public int CatnipPouches;     // consumable; missing in old saves → 0

        public int SackCapacity => 1 + SackLevel;

        public event Action Changed;

        /// <summary>Max "level" for a workbench entry (Catnip = max pouches carried).</summary>
        public static int MaxLevelFor(UpgradeType t) => t == UpgradeType.Catnip ? MaxCatnip : MaxUpgradeLevel;

        public int GetLevel(UpgradeType t) => t switch
        {
            UpgradeType.Jetpack => JetpackLevel,
            UpgradeType.SilentBoots => SilentBootsLevel,
            UpgradeType.Sack => SackLevel,
            UpgradeType.Grip => GripLevel,
            UpgradeType.Catnip => CatnipPouches,
            _ => 0
        };

        /// <summary>Costs from the original game: base + level * step. Catnip is a flat price per pouch.</summary>
        public int GetCost(UpgradeType t) => t switch
        {
            UpgradeType.Jetpack => 100 + JetpackLevel * 60,
            UpgradeType.SilentBoots => 75 + SilentBootsLevel * 50,
            UpgradeType.Sack => 85 + SackLevel * 55,
            UpgradeType.Grip => 80 + GripLevel * 50,
            UpgradeType.Catnip => CatnipCost,
            _ => int.MaxValue
        };

        public bool CanBuy(UpgradeType t) => GetLevel(t) < MaxLevelFor(t) && Tips >= GetCost(t);

        /// <summary>Spends one catnip pouch. False when there is none.</summary>
        public bool TryUseCatnip()
        {
            if (CatnipPouches <= 0) return false;
            CatnipPouches--;
            NotifyChanged();
            Save();
            return true;
        }

        public bool TryBuy(UpgradeType t)
        {
            if (!CanBuy(t)) return false;
            Tips -= GetCost(t);
            switch (t)
            {
                case UpgradeType.Jetpack: JetpackLevel++; break;
                case UpgradeType.SilentBoots: SilentBootsLevel++; break;
                case UpgradeType.Sack: SackLevel++; break;
                case UpgradeType.Grip: GripLevel++; break;
                case UpgradeType.Catnip: CatnipPouches++; break;
            }
            NotifyChanged();
            Save();
            return true;
        }

        public void AddTips(int amount)
        {
            Tips = Mathf.Max(0, Tips + amount);
            NotifyChanged();
        }

        public void NotifyChanged() => Changed?.Invoke();

        // ---- persistence ----
        private static string SavePath => Path.Combine(Application.persistentDataPath, "profile.json");

        public void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning($"[PlayerProfile] Save failed: {e.Message}"); }
        }

        public static PlayerProfile Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    var p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(SavePath));
                    if (p != null)
                    {
                        p.CatnipPouches = Mathf.Clamp(p.CatnipPouches, 0, MaxCatnip);
                        return p;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[PlayerProfile] Load failed: {e.Message}"); }
            return new PlayerProfile();
        }
    }
}
