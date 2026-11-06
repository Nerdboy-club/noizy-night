#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;

namespace SilentNight.Tests
{
    /// <summary>Procedural fallback sounds exist and are valid clips (the project has no audio files yet).</summary>
    public class AudioTests
    {
        [TestCase("footstep_carpet")]
        [TestCase("footstep_wood")]
        [TestCase("footstep_creaky")]
        [TestCase("footstep_tile")]
        [TestCase("footstep_ice")]
        [TestCase("landing")]
        [TestCase("gift_smash")]
        [TestCase("toy_alarm")]
        [TestCase("meow")]
        [TestCase("yowl")]
        [TestCase("gift_delivered")]
        [TestCase("stinger_caught")]
        [TestCase("music_house_cozy")]
        public void ProceduralClipExists(string name)
        {
            Type t = Type.GetType("SilentNight.Audio.ProceduralSfx, Assembly-CSharp", true);
            var clip = (AudioClip)t.GetMethod("Get").Invoke(null, new object[] { name });
            Assert.NotNull(clip, name);
            Assert.Greater(clip.length, 0.03f, name + " length");
            var data = new float[clip.samples];
            clip.GetData(data, 0);
            float peak = 0f;
            foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            Assert.That(peak, Is.InRange(0.05f, 1f), name + " peak level");
        }
    }
}
#endif
