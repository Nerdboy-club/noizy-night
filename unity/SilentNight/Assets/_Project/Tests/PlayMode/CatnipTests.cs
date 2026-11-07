#if UNITY_EDITOR
using System;
using NUnit.Framework;

namespace SilentNight.Tests
{
    /// <summary>Catnip pouches: bought at the workbench for tips, max 3, consumed on use.</summary>
    public class CatnipTests
    {
        private static Type T(string n) => Type.GetType(n + ", Assembly-CSharp", true);

        // PlayerProfile saves to persistentDataPath/profile.json (shared with the real game): keep the player's save intact.
        private static string SavePath => System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "profile.json");
        private string _backup;

        [SetUp]
        public void Backup() => _backup = System.IO.File.Exists(SavePath) ? System.IO.File.ReadAllText(SavePath) : null;

        [TearDown]
        public void Restore()
        {
            if (_backup != null) System.IO.File.WriteAllText(SavePath, _backup);
            else if (System.IO.File.Exists(SavePath)) System.IO.File.Delete(SavePath);
        }

        [Test]
        public void BuyAndUseCatnip()
        {
            Type profT = T("SilentNight.Core.PlayerProfile");
            object p = Activator.CreateInstance(profT);
            profT.GetField("Tips").SetValue(p, 200);
            object catnip = Enum.Parse(T("SilentNight.Core.UpgradeType"), "Catnip");
            var buy = profT.GetMethod("TryBuy");
            Assert.IsTrue((bool)buy.Invoke(p, new[] { catnip }), "buy 1");
            Assert.IsTrue((bool)buy.Invoke(p, new[] { catnip }), "buy 2");
            Assert.IsTrue((bool)buy.Invoke(p, new[] { catnip }), "buy 3");
            Assert.IsFalse((bool)buy.Invoke(p, new[] { catnip }), "max 3 pouches");
            Assert.AreEqual(3, (int)profT.GetField("CatnipPouches").GetValue(p));
            Assert.AreEqual(80, (int)profT.GetField("Tips").GetValue(p), "3 x 40 tips spent");
            Assert.IsTrue((bool)profT.GetMethod("TryUseCatnip").Invoke(p, null), "use one");
            Assert.AreEqual(2, (int)profT.GetField("CatnipPouches").GetValue(p));
        }
    }
}
#endif
