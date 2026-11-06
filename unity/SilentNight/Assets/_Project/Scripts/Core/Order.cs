using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilentNight.Core
{
    /// <summary>A delivery order on the Order Board.</summary>
    [Serializable]
    public class Order
    {
        public string Address;
        public string Description;
        public HouseMood Mood;
        public int BaseReward;
        public float TipMultiplier = 1f;
        public List<GiftKind> Gifts = new List<GiftKind>();

        private static readonly string[] Streets =
        {
            "пер. Пряничный", "ул. Снежная", "пр. Рождественских Елей", "ул. Леденцовая",
            "пер. Олений", "ул. Мишурная", "бул. Звёздный", "тупик Крампуса"
        };

        /// <summary>Random order. ~15% chance of a rare Spooky house with x2 tips.</summary>
        public static Order Generate(System.Random rng = null)
        {
            rng ??= new System.Random();
            bool spooky = rng.NextDouble() < 0.15;
            var o = new Order
            {
                Address = $"{Streets[rng.Next(Streets.Length)]} {rng.Next(1, 99)}",
                Mood = spooky ? HouseMood.Spooky : HouseMood.Cozy,
                TipMultiplier = spooky ? 2f : 1f + (float)Math.Round(rng.NextDouble() * 0.5f, 2),
                BaseReward = 120 + rng.Next(0, 6) * 20
            };
            o.Gifts.Add(GiftKind.Small);
            if (rng.NextDouble() < 0.7) o.Gifts.Add(GiftKind.Noisy);
            if (rng.NextDouble() < 0.6) o.Gifts.Add(GiftKind.Small);
            if (rng.NextDouble() < 0.5) o.Gifts.Add(GiftKind.Fragile);
            if (rng.NextDouble() < 0.4) o.Gifts.Add(GiftKind.RcCar);
            o.Gifts.Add(GiftKind.Huge);
            o.Description = spooky
                ? "РЕДКИЙ ЗАКАЗ (чаевые x2): в доме погас свет, в коридоре тикают старинные часы."
                : "Уютная праздничная ночь, гирлянды и тёплый камин.";
            return o;
        }
    }
}
