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
        [UnityTest]
        public IEnumerator Hub_TwoElvesCarryTheBikeTogether()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Hub.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            Type joinT = Type.GetType("SilentNight.Player.LocalCoopJoin, Assembly-CSharp", true);
            joinT.GetMethod("Join").Invoke(UnityEngine.Object.FindAnyObjectByType(joinT), new object[] { pad });
            yield return new WaitForSeconds(1f);

            Type carryT = Type.GetType("SilentNight.Player.ElfCarry, Assembly-CSharp", true);
            Type inputT = Type.GetType("SilentNight.Player.ElfInput, Assembly-CSharp", true);
            Type ctrlT = Type.GetType("SilentNight.Player.ElfController, Assembly-CSharp", true);
            Component p1 = null, p2 = null;
            foreach (var c in UnityEngine.Object.FindObjectsByType(carryT, FindObjectsSortMode.None))
            {
                var comp = (Component)c;
                int idx = (int)inputT.GetProperty("PlayerIndex").GetValue(comp.GetComponent(inputT));
                if (idx == 0) p1 = comp; else p2 = comp;
            }
            Assert.NotNull(p1); Assert.NotNull(p2);

            Type kindT = Type.GetType("SilentNight.Core.GiftKind, Assembly-CSharp", true);
            var bike = (Component)Type.GetType("SilentNight.Gifts.GiftFactory, Assembly-CSharp", true).GetMethod("Create")
                .Invoke(null, new object[] { Enum.Parse(kindT, "Huge"), p1.transform.position + p1.transform.forward, p1.transform.rotation });
            yield return null;
            Assert.IsTrue((bool)carryT.GetMethod("TryPickUp").Invoke(p1, new object[] { bike }), "p1 lifts the bike");
            ctrlT.GetMethod("Teleport").Invoke(p2.GetComponent(ctrlT), new object[] { p1.transform.position + p1.transform.right * 1.6f, p1.transform.rotation, true });
            yield return null;
            yield return null;

            var handle = carryT.GetProperty("CoCarryHandle").GetValue(p1);
            var interactor = p2.GetComponent(Type.GetType("SilentNight.Player.ElfInteractor, Assembly-CSharp", true));
            Assert.IsTrue((bool)handle.GetType().GetMethod("CanInteract").Invoke(handle, new object[] { interactor }), "p2 can grab the other end");
            handle.GetType().GetMethod("Interact").Invoke(handle, new object[] { interactor });
            yield return null;
            Assert.IsTrue((bool)carryT.GetProperty("IsCoCarrying").GetValue(p1), "p1 co-carrying");
            Assert.IsTrue((bool)carryT.GetProperty("IsCoCarrying").GetValue(p2), "p2 co-carrying");
            Assert.Greater((float)carryT.GetProperty("SpeedMultiplier").GetValue(p1), 0.8f, "faster together");

            Vector3 p2Start = p2.transform.position;
            inputT.GetField("DebugMoveOverride").SetValue(null, new Vector2(1f, 0f));
            yield return new WaitForSeconds(2f);
            inputT.GetField("DebugMoveOverride").SetValue(null, null);
            float drag = Vector3.Distance(p2Start, p2.transform.position);
            Debug.Log("[Smoke] helper dragged " + drag.ToString("0.00") + " m");
            Type ccT = Type.GetType("SilentNight.Player.CoCarry, Assembly-CSharp", true);
            object active = ccT.GetProperty("Active").GetValue(null);
            Assert.NotNull(active, "still carrying together after walking");
            Assert.Greater(drag, 0.5f, "the second elf is pulled along by the bike");

            handle.GetType().GetMethod("Interact").Invoke(handle, new object[] { interactor });
            yield return null;
            Assert.IsFalse((bool)carryT.GetProperty("IsCoCarrying").GetValue(p1), "released");
            Assert.IsTrue((bool)carryT.GetProperty("IsCarryingHuge").GetValue(p1), "front elf keeps the bike");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }
        [UnityTest]
        public IEnumerator House_CoCarriedBikeStopsAtTheWall()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/House.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(1f);
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            Type joinT = Type.GetType("SilentNight.Player.LocalCoopJoin, Assembly-CSharp", true);
            joinT.GetMethod("Join").Invoke(UnityEngine.Object.FindAnyObjectByType(joinT), new object[] { pad });
            yield return new WaitForSeconds(1f);

            Type carryT = Type.GetType("SilentNight.Player.ElfCarry, Assembly-CSharp", true);
            Type inputT = Type.GetType("SilentNight.Player.ElfInput, Assembly-CSharp", true);
            Type ctrlT = Type.GetType("SilentNight.Player.ElfController, Assembly-CSharp", true);
            Component p1 = null, p2 = null;
            foreach (var c in UnityEngine.Object.FindObjectsByType(carryT, FindObjectsSortMode.None))
            {
                var comp = (Component)c;
                if ((int)inputT.GetProperty("PlayerIndex").GetValue(comp.GetComponent(inputT)) == 0) p1 = comp; else p2 = comp;
            }
            ctrlT.GetMethod("Teleport").Invoke(p1.GetComponent(ctrlT), new object[] { new Vector3(0.9f, 0.02f, -5f), Quaternion.Euler(0f, 90f, 0f), true }); // kitchen side of the hallway door
            ctrlT.GetMethod("Teleport").Invoke(p2.GetComponent(ctrlT), new object[] { new Vector3(-0.9f, 0.02f, -5f), Quaternion.Euler(0f, -90f, 0f), true }); // hallway side
            Type kindT = Type.GetType("SilentNight.Core.GiftKind, Assembly-CSharp", true);
            var bike = (Component)Type.GetType("SilentNight.Gifts.GiftFactory, Assembly-CSharp", true).GetMethod("Create")
                .Invoke(null, new object[] { Enum.Parse(kindT, "Huge"), new Vector3(1.6f, 0.6f, -5f), Quaternion.identity });
            yield return null;
            Assert.IsTrue((bool)carryT.GetMethod("TryPickUp").Invoke(p1, new object[] { bike }));
            yield return null;
            var handle = carryT.GetProperty("CoCarryHandle").GetValue(p1);
            var interactor = p2.GetComponent(Type.GetType("SilentNight.Player.ElfInteractor, Assembly-CSharp", true));
            handle.GetType().GetMethod("Interact").Invoke(handle, new object[] { interactor });
            yield return null;
            yield return null;

            Type ccT = Type.GetType("SilentNight.Player.CoCarry, Assembly-CSharp", true);
            Type rigT = Type.GetType("SilentNight.Player.ThirdPersonCameraRig, Assembly-CSharp", true);
            var rig = UnityEngine.Object.FindAnyObjectByType(rigT);
            var yaw = rigT.GetField("_yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            inputT.GetField("DebugMoveOverride").SetValue(null, new Vector2(0f, 1f));
            bool blocked = false;
            float t = 0f;
            while (t < 2.5f)
            {
                yaw.SetValue(rig, 0f); // p1 walks along the kitchen wall: the bike would cut through the door frame
                object active = ccT.GetProperty("Active").GetValue(null);
                if (active != null && (bool)ccT.GetProperty("Blocked").GetValue(active)) blocked = true;
                t += Time.deltaTime;
                yield return null;
            }
            inputT.GetField("DebugMoveOverride").SetValue(null, null);
            Debug.Log("[Smoke] co-carry bike " + bike.transform.position + " p1 " + p1.transform.position + " p2 " + p2.transform.position + " active " + (ccT.GetProperty("Active").GetValue(null) != null));
            Assert.IsTrue(blocked, "the shared bike jams in the door frame");
            Assert.Less(p1.transform.position.z, -3.4f, "the front elf cannot just walk away along the wall");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }
        [UnityTest]
        public IEnumerator House_CaughtElfIsThrownOutInCoop()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/House.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(1f);
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            Type joinT = Type.GetType("SilentNight.Player.LocalCoopJoin, Assembly-CSharp", true);
            joinT.GetMethod("Join").Invoke(UnityEngine.Object.FindAnyObjectByType(joinT), new object[] { pad });
            yield return new WaitForSeconds(1f);

            Type inputT = Type.GetType("SilentNight.Player.ElfInput, Assembly-CSharp", true);
            Component p2 = null;
            foreach (var c in UnityEngine.Object.FindObjectsByType(inputT, FindObjectsSortMode.None))
                if ((int)inputT.GetProperty("PlayerIndex").GetValue(c) == 1) p2 = (Component)c;
            Assert.NotNull(p2, "second elf");

            bool caught = false;
            Type evT = Type.GetType("SilentNight.Core.GameEvents, Assembly-CSharp", true);
            var ev = evT.GetEvent("PlayerCaught");
            Action<string> h = _ => caught = true;
            ev.AddEventHandler(null, h);
            try
            {
                Type gT = Type.GetType("SilentNight.Stealth.Grandpa, Assembly-CSharp", true);
                var grandpa = UnityEngine.Object.FindAnyObjectByType(gT);
                gT.GetMethod("Catch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(grandpa, new object[] { p2.transform.position });
                yield return null;
                yield return null;
                Assert.IsFalse(caught, "run does not end while another elf is still in the house");
                Assert.AreEqual(1, (int)gT.GetProperty("ElvesThrownOut").GetValue(grandpa), "one elf thrown out");
                Type ragT = Type.GetType("SilentNight.Player.ElfRagdoll, Assembly-CSharp", true);
                Assert.IsTrue((bool)ragT.GetProperty("IsLimp").GetValue(p2.GetComponent(ragT)), "thrown elf lands in a heap");
            }
            finally { ev.RemoveEventHandler(null, h); }
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }
        [UnityTest]
        public IEnumerator Hub_ElvesRunningIntoEachOtherBonk()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Hub.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            Type joinT = Type.GetType("SilentNight.Player.LocalCoopJoin, Assembly-CSharp", true);
            joinT.GetMethod("Join").Invoke(UnityEngine.Object.FindAnyObjectByType(joinT), new object[] { pad });
            yield return new WaitForSeconds(1f);

            Type inputT = Type.GetType("SilentNight.Player.ElfInput, Assembly-CSharp", true);
            Type ctrlT = Type.GetType("SilentNight.Player.ElfController, Assembly-CSharp", true);
            Type ragT = Type.GetType("SilentNight.Player.ElfRagdoll, Assembly-CSharp", true);
            Type bonkT = Type.GetType("SilentNight.Player.ElfBonk, Assembly-CSharp", true);
            Component p1 = null, p2 = null;
            foreach (var c in UnityEngine.Object.FindObjectsByType(inputT, FindObjectsSortMode.None))
                if ((int)inputT.GetProperty("PlayerIndex").GetValue(c) == 0) p1 = (Component)c; else p2 = (Component)c;

            float yaw = Camera.main.transform.eulerAngles.y;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 p = new Vector3(0f, 0.02f, 0f);
            ctrlT.GetMethod("Teleport").Invoke(p1.GetComponent(ctrlT), new object[] { p, Quaternion.identity, true });
            ctrlT.GetMethod("Teleport").Invoke(p2.GetComponent(ctrlT), new object[] { p + fwd * 1.6f, Quaternion.identity, true });
            yield return null;
            bonkT.GetProperty("BonkCount").SetValue(null, 0);

            inputT.GetField("DebugMoveOverride").SetValue(null, new Vector2(0f, 1f));
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState { leftStick = new Vector2(0f, -1f) });
            float t = 0f;
            while (t < 1.2f && (int)bonkT.GetProperty("BonkCount").GetValue(null) == 0) { t += Time.deltaTime; yield return null; }
            inputT.GetField("DebugMoveOverride").SetValue(null, null);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
            yield return null;
            Assert.AreEqual(1, (int)bonkT.GetProperty("BonkCount").GetValue(null), "head-on collision bonks");
            Assert.IsTrue((bool)ragT.GetProperty("IsLimp").GetValue(p1.GetComponent(ragT)), "elf 1 falls");
            Assert.IsTrue((bool)ragT.GetProperty("IsLimp").GetValue(p2.GetComponent(ragT)), "elf 2 falls");
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
        }
        [UnityTest]
        public IEnumerator Coop_FriendsFollowIntoTheNextScene()
        {
            Type joinT = Type.GetType("SilentNight.Player.LocalCoopJoin, Assembly-CSharp", true);
            joinT.GetMethod("ClearRoster").Invoke(null, null);
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Hub.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(0.5f);
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            joinT.GetMethod("Join").Invoke(UnityEngine.Object.FindAnyObjectByType(joinT), new object[] { pad });
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, (int)joinT.GetProperty("RosterCount").GetValue(null), "friend remembered");

            op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/House.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(1f);
            Type gs = Type.GetType("SilentNight.Core.GameServices, Assembly-CSharp", true);
            var players = (System.Collections.ICollection)gs.GetProperty("Players").GetValue(null);
            Assert.AreEqual(2, players.Count, "friend arrived in the house too");

            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
            yield return null;
            Assert.AreEqual(0, (int)joinT.GetProperty("RosterCount").GetValue(null), "unplugged friend forgotten");
        }
    }
}
#endif
