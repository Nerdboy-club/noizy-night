using System.Collections.Generic;
using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Rooftop
{
    /// <summary>
    /// Rooftop scene brain: picks which chimney belongs to the order address (random each run), numbers all the others
    /// and posts the objective.
    /// </summary>
    public class RooftopLevelController : MonoBehaviour
    {
        [SerializeField] private RooftopChimney[] chimneys = new RooftopChimney[0];

        private void Start()
        {
            Order order = GameServices.ActiveOrder;
            int target = ParseNumber(order != null ? order.Address : null);
            if (target <= 0) target = 12;
            RooftopChimney.TargetNumber = target;

            if (chimneys == null || chimneys.Length == 0) return;
            var rng = new System.Random();
            int targetIdx = rng.Next(chimneys.Length);
            var used = new HashSet<int> { target };
            for (int i = 0; i < chimneys.Length; i++)
            {
                if (chimneys[i] == null) continue;
                if (i == targetIdx) { chimneys[i].Setup(target, true); continue; }
                int n;
                do { n = rng.Next(1, 99); } while (!used.Add(n));
                chimneys[i].Setup(n, false);
            }

            Invoke(nameof(PostObjective), 0.6f);
        }

        private void PostObjective()
        {
            Order order = GameServices.ActiveOrder;
            string addr = order != null ? order.Address : "дом № " + RooftopChimney.TargetNumber;
            GameEvents.RaiseObjective("Найдите дымоход: " + addr);
            GameEvents.RaiseToast("Олени высадили вас на крыше. Ищите " + addr + ".");
        }

        private static int ParseNumber(string address)
        {
            if (string.IsNullOrEmpty(address)) return 0;
            int end = address.Length - 1;
            while (end >= 0 && !char.IsDigit(address[end])) end--;
            if (end < 0) return 0;
            int start = end;
            while (start > 0 && char.IsDigit(address[start - 1])) start--;
            return int.TryParse(address.Substring(start, end - start + 1), out int v) ? v : 0;
        }
    }
}
