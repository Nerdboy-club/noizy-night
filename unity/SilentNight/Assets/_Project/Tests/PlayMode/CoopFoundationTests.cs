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
        [UnityTest]
        public IEnumerator Hub_SecondElfJoinsOnGamepad()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Hub.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try
            {
                Type joinT = Type.GetType("SilentNight.Player.LocalCoopJoin, Assembly-CSharp", true);
                var join = (Component)UnityEngine.Object.FindAnyObjectByType(joinT);
                Assert.NotNull(join, "LocalCoopJoin on player 1");
                joinT.GetMethod("Join").Invoke(join, new object[] { pad });
            }
            finally { }
            yield return new WaitForSeconds(1f);
            Type gs = Type.GetType("SilentNight.Core.GameServices, Assembly-CSharp", true);
            var players = (System.Collections.ICollection)gs.GetProperty("Players").GetValue(null);
            Assert.AreEqual(2, players.Count, "second elf registered");
            Type rigT = Type.GetType("SilentNight.Player.ThirdPersonCameraRig, Assembly-CSharp", true);
            var rig = UnityEngine.Object.FindAnyObjectByType(rigT);
            Assert.IsTrue((bool)rigT.GetProperty("IsFramingGroup").GetValue(rig), "camera frames both elves");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
            yield return new WaitForSeconds(0.3f);
            players = (System.Collections.ICollection)gs.GetProperty("Players").GetValue(null);
            Assert.AreEqual(1, players.Count, "elf leaves when the pad is unplugged");
        }
        [UnityTest]
        public IEnumerator Hub_FriendHelpsDownedElfUp()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Hub.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            Type joinT = Type.GetType("SilentNight.Player.LocalCoopJoin, Assembly-CSharp", true);
            joinT.GetMethod("Join").Invoke(UnityEngine.Object.FindAnyObjectByType(joinT), new object[] { pad });
            yield return new WaitForSeconds(1f);

            Type ragT = Type.GetType("SilentNight.Player.ElfRagdoll, Assembly-CSharp", true);
            var rags = UnityEngine.Object.FindObjectsByType(ragT, FindObjectsSortMode.None);
            Assert.AreEqual(2, rags.Length, "two elves");
            var victim = (Component)rags[0];
            var helper = (Component)rags[1];
            ragT.GetMethod("GoLimp", Type.EmptyTypes).Invoke(victim, null);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue((bool)ragT.GetProperty("IsDowned").GetValue(victim), "downed in co-op");

            Type helpT = Type.GetType("SilentNight.Player.ElfHelpUp, Assembly-CSharp", true);
            Type intT = Type.GetType("SilentNight.Player.ElfInteractor, Assembly-CSharp", true);
            var help = victim.GetComponent(helpT);
            var interactor = helper.GetComponent(intT);
            Assert.IsTrue((bool)helpT.GetMethod("CanInteract").Invoke(help, new object[] { interactor }), "friend can help");
            helpT.GetMethod("Interact").Invoke(help, new object[] { interactor });
            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse((bool)ragT.GetProperty("IsLimp").GetValue(victim), "helped up well before the 4 s downed time");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }
    }
}
#endif
