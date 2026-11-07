#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;

namespace SilentNight.Tests
{
    /// <summary>Results recap: a quiet run gets the "Тише мыши" rank and 3-5 recap lines.</summary>
    public class RunStatsTests
    {
        [Test]
        public void QuietRunGetsMouseRankAndRecap()
        {
            Type t = Type.GetType("SilentNight.UI.RunStats, Assembly-CSharp", true);
            t.GetMethod("Reset").Invoke(null, null);
            Assert.AreEqual("QuietAsMouse", t.GetProperty("Rank").GetValue(null).ToString());
            var lines = (IList)t.GetMethod("BuildRecapLines").Invoke(null, new object[] { 5 });
            Assert.That(lines.Count, Is.InRange(3, 5), "recap lines");
            foreach (object l in lines) Assert.IsFalse(string.IsNullOrEmpty((string)l));
        }
    }
}
#endif
