#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SilentNight.Tests
{
    /// <summary>Co-op step 0: the elf registers in GameServices.Players and stays LocalPlayer.</summary>
    public class CoopFoundationTests
    {
        [UnityTest]
        public IEnumerator Hub_ElfIsRegisteredAsPlayerAndLocalPlayer()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Hub.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(0.5f);
            Type gs = Type.GetType("SilentNight.Core.GameServices, Assembly-CSharp", true);
            var players = (System.Collections.ICollection)gs.GetProperty("Players").GetValue(null);
            Assert.AreEqual(1, players.Count, "one elf registered");
            object local = gs.GetProperty("LocalPlayer").GetValue(null);
            Assert.NotNull(local, "LocalPlayer set");
            Assert.AreEqual("ElfController", local.GetType().Name);
        }
    }
}
#endif
