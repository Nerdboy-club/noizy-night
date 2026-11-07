#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SilentNight.Tests
{
    /// <summary>
    /// Smoke tests for the built scenes (run: Unity -batchmode -runTests -testPlatform PlayMode).
    /// Game code lives in Assembly-CSharp, which an asmdef cannot reference, so types are reached by reflection.
    /// Any Debug.LogError / exception during a test fails it.
    /// </summary>
    public class SceneSmokeTests
    {
        private const string Scenes = "Assets/_Project/Scenes/";

        private static Type T(string fullName) => Type.GetType(fullName + ", Assembly-CSharp", true);

        private static Component Find(string fullName) => (Component)Object.FindAnyObjectByType(T(fullName));

        private static Object[] FindAll(string fullName) => Object.FindObjectsByType(T(fullName), FindObjectsSortMode.None);

        private static object Get(object o, string prop) => o.GetType().GetProperty(prop).GetValue(o);

        private static void SetMove(Vector2? v) => T("SilentNight.Player.ElfInput").GetField("DebugMoveOverride").SetValue(null, v);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static IEnumerator Load(string scene)
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode(Scenes + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return null;
        }

        private static Transform Pelvis()
        {
            Component rag = Find("SilentNight.Player.ElfRagdoll");
            Assert.NotNull(rag, "ElfRagdoll missing");
            return (Transform)Get(rag, "PelvisTransform");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetMove(null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Hub_ElfStandsAndWalks()
        {
            yield return Load("Hub");
            yield return new WaitForSeconds(2f);

            Component ctrl = Find("SilentNight.Player.ElfController");
            Assert.NotNull(ctrl, "ElfController missing");
            Transform pelvis = Pelvis();
            Assert.NotNull(pelvis, "ragdoll pelvis missing");

            float h = pelvis.position.y - ctrl.transform.position.y;
            Assert.That(h, Is.InRange(0.12f, 0.45f), "pelvis height above the feet while standing");
            Assert.Greater(Vector3.Dot(pelvis.up, Vector3.up), 0.8f, "pelvis upright while standing");

            Vector3 start = ctrl.transform.position;
            SetMove(new Vector2(0f, 1f));
            yield return new WaitForSeconds(2f);

            float moved = Vector3.Distance(Flat(start), Flat(ctrl.transform.position));
            Debug.Log($"[Smoke] walked {moved:0.00} m in 2 s, pelvis lag {Vector3.Distance(Flat(pelvis.position), Flat(ctrl.transform.position)):0.00} m");
            Assert.Greater(moved, 2f, "elf walks forward");
            Assert.Less(Vector3.Distance(Flat(pelvis.position), Flat(ctrl.transform.position)), 0.4f, "puppet keeps up with the controller");
            Assert.Greater(Vector3.Dot(pelvis.up, Vector3.up), 0.8f, "pelvis upright while walking");
            SetMove(null);
        }

        [UnityTest]
        public IEnumerator Hub_LimpThenGetsUp()
        {
            yield return Load("Hub");
            yield return new WaitForSeconds(1f);
            Component rag = Find("SilentNight.Player.ElfRagdoll");
            rag.GetType().GetMethod("GoLimp", Type.EmptyTypes).Invoke(rag, null);
            yield return new WaitForSeconds(0.5f);
            Assert.IsTrue((bool)Get(rag, "IsLimp"), "goes limp");
            yield return new WaitForSeconds(4f);
            Assert.IsFalse((bool)Get(rag, "IsLimp"), "gets back up");
            Component ctrl = Find("SilentNight.Player.ElfController");
            Transform pelvis = Pelvis();
            Assert.That(pelvis.position.y - ctrl.transform.position.y, Is.InRange(0.1f, 0.5f), "standing again");
            Assert.Greater(Vector3.Dot(pelvis.up, Vector3.up), 0.8f, "upright again");
        }

        [UnityTest]
        public IEnumerator Rooftop_HasOneTargetChimney()
        {
            yield return Load("Rooftop");
            yield return new WaitForSeconds(1f);
            Object[] chimneys = FindAll("SilentNight.Rooftop.RooftopChimney");
            Assert.That(chimneys.Length, Is.GreaterThanOrEqualTo(10), "chimney count");
            int targets = 0;
            var numbers = new HashSet<int>();
            foreach (Object c in chimneys)
            {
                if ((bool)Get(c, "IsTarget")) targets++;
                int n = (int)Get(c, "HouseNumber");
                Assert.Greater(n, 0, "every chimney has a number");
                Assert.IsTrue(numbers.Add(n), "house numbers are unique");
            }
            Assert.AreEqual(1, targets, "exactly one target chimney");
            Assert.NotNull(Find("SilentNight.Player.ElfController"), "elf spawned on the roof");
        }

        [UnityTest]
        public IEnumerator House_HasNpcsTreeAndStandingElf()
        {
            yield return Load("House");
            yield return new WaitForSeconds(2f);
            Assert.NotNull(Find("SilentNight.Stealth.Grandpa"), "Grandpa");
            Assert.NotNull(Find("SilentNight.Stealth.FatCat"), "cat");
            Assert.NotNull(Find("SilentNight.Gifts.TreeDropZone"), "tree drop zone");
            Component ctrl = Find("SilentNight.Player.ElfController");
            Transform pelvis = Pelvis();
            Assert.That(pelvis.position.y - ctrl.transform.position.y, Is.InRange(0.1f, 0.5f), "elf standing in the house");
        }
        [UnityTest]
        public IEnumerator House_WalkingOverToyTripsTheElf()
        {
            yield return Load("House");
            yield return new WaitForSeconds(1.5f);
            Component ctrl = Find("SilentNight.Player.ElfController");
            Component rag = Find("SilentNight.Player.ElfRagdoll");
            // Stand on the toy car and start walking: the trigger fires while moving faster than the trip speed.
            ctrl.GetType().GetMethod("Teleport").Invoke(ctrl, new object[] { new Vector3(7.8f, 0.02f, 5.4f), Quaternion.identity, true });
            yield return new WaitForSeconds(0.3f);
            SetMove(new Vector2(0f, 1f));
            float t = 0f;
            bool limp = false;
            while (t < 1.5f && !limp)
            {
                t += Time.deltaTime;
                limp = (bool)Get(rag, "IsLimp");
                yield return null;
            }
            SetMove(null);
            Assert.IsTrue(limp, "walking over a toy car trips the elf");
        }
        [UnityTest]
        public IEnumerator House_AllGiftsCanBeDeliveredUnderTheTree()
        {
            yield return Load("House");
            yield return new WaitForSeconds(1.5f);
            Component spawner = Find("SilentNight.Gifts.HouseGiftSpawner");
            Component tree = Find("SilentNight.Gifts.TreeDropZone");
            Component carry = Find("SilentNight.Player.ElfCarry");
            Component interactor = Find("SilentNight.Player.ElfInteractor");
            var gifts = (System.Collections.IList)spawner.GetType().GetField("SpawnedGifts").GetValue(spawner);
            int total = (int)Get(spawner, "TotalCount");
            Assert.Greater(total, 0, "house has gifts to deliver");
            foreach (object g in gifts)
            {
                bool picked = (bool)carry.GetType().GetMethod("TryPickUp").Invoke(carry, new[] { g });
                Assert.IsTrue(picked, "elf can pick up " + g);
                tree.GetType().GetMethod("Interact").Invoke(tree, new object[] { interactor });
                yield return null;
            }
            Assert.AreEqual(total, (int)Get(tree, "Delivered"), "every gift counted as delivered");
        }
        [UnityTest]
        public IEnumerator House_HugeBikeGetsStuckOnTheWall()
        {
            yield return Load("House");
            yield return new WaitForSeconds(1.5f);
            Component ctrl = Find("SilentNight.Player.ElfController");
            Component carry = Find("SilentNight.Player.ElfCarry");
            Component spawner = Find("SilentNight.Gifts.HouseGiftSpawner");
            object bike = null;
            foreach (object g in (System.Collections.IList)spawner.GetType().GetField("SpawnedGifts").GetValue(spawner))
                if (Get(g, "Kind").ToString() == "Huge") bike = g;
            Assume.That(bike != null, "order without a bike");

            // Hallway (x -3.5..0), facing the east wall (x = 0) with the bike held across.
            ctrl.GetType().GetMethod("Teleport").Invoke(ctrl, new object[] { new Vector3(-1.75f, 0.02f, -8f), Quaternion.Euler(0f, 90f, 0f), true });
            Component rig = Find("SilentNight.Player.ThirdPersonCameraRig");
            rig.GetType().GetField("_yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(rig, 90f);
            Assert.IsTrue((bool)carry.GetType().GetMethod("TryPickUp").Invoke(carry, new[] { bike }), "pick up the bike");
            yield return new WaitForSeconds(0.3f);

            SetMove(new Vector2(0f, 1f));
            bool blocked = false;
            float t = 0f;
            while (t < 3f && !blocked)
            {
                rig.GetType().GetField("_yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(rig, 90f);
                blocked = (bool)Get(ctrl, "HugeGiftBlocked");
                t += Time.deltaTime;
                yield return null;
            }
            SetMove(null);
            Assert.IsTrue(blocked, "the bike stops at the wall instead of clipping through");
            Assert.Less(ctrl.transform.position.x, -0.1f, "elf did not pass into the wall");
        }
        [UnityTest]
        public IEnumerator Rooftop_ElfRunsUpTheSnowRamp()
        {
            yield return Load("Rooftop");
            yield return new WaitForSeconds(1f);
            Component ctrl = Find("SilentNight.Player.ElfController");
            Component rig = Find("SilentNight.Player.ThirdPersonCameraRig");
            var yaw = rig.GetType().GetField("_yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ctrl.GetType().GetMethod("Teleport").Invoke(ctrl, new object[] { new Vector3(2.4f, 0.05f, -1f), Quaternion.Euler(0f, 90f, 0f), true });
            yield return new WaitForSeconds(0.3f);
            SetMove(new Vector2(0f, 1f));
            float t = 0f;
            while (t < 2.5f) { yaw.SetValue(rig, 90f); t += Time.deltaTime; yield return null; }
            SetMove(null);
            Vector3 p = ctrl.transform.position;
            Debug.Log("[Smoke] after ramp: " + p);
            Assert.Greater(p.x, 5.2f, "walked over the ramp onto the next roof");
            Assert.Greater(p.y, 0.3f, "stands on the higher roof");
        }
        [UnityTest]
        public IEnumerator House_ElfClimbsOntoKitchenCounter()
        {
            yield return Load("House");
            yield return new WaitForSeconds(1.5f);
            Component ctrl = Find("SilentNight.Player.ElfController");
            Component rig = Find("SilentNight.Player.ThirdPersonCameraRig");
            var yaw = rig.GetType().GetField("_yaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ctrl.GetType().GetMethod("Teleport").Invoke(ctrl, new object[] { new Vector3(6.5f, 0.02f, -9.6f), Quaternion.Euler(0f, 180f, 0f), true });
            yield return new WaitForSeconds(0.3f);
            SetMove(new Vector2(0f, 1f));
            float t = 0f;
            bool jumped = false, mantled = false;
            while (t < 2f)
            {
                yaw.SetValue(rig, 180f);
                if (!jumped && t > 0.2f) { T("SilentNight.Player.ElfInput").GetField("DebugJumpOverride").SetValue(null, true); jumped = true; }
                if ((bool)Get(ctrl, "IsMantling")) mantled = true;
                t += Time.deltaTime;
                yield return null;
            }
            SetMove(null);
            Vector3 p = ctrl.transform.position;
            Debug.Log("[Smoke] after mantle: " + p);
            Assert.IsTrue(mantled, "elf grabbed the counter edge");
            Assert.Greater(p.y, 0.85f, "elf is standing on the counter");
        }
        [UnityTest]
        public IEnumerator House_CrouchingUnderTableHidesTheElf()
        {
            yield return Load("House");
            yield return new WaitForSeconds(1f);
            Type hs = T("SilentNight.Stealth.HidingSpot");
            var isHidden = hs.GetMethod("IsHidden", new[] { typeof(Transform), typeof(bool) });
            var probe = new GameObject("probe").transform;
            probe.position = new Vector3(6.5f, 0f, -5.5f); // under the kitchen table
            Assert.IsTrue((bool)isHidden.Invoke(null, new object[] { probe, true }), "crouching under the table hides");
            Assert.IsFalse((bool)isHidden.Invoke(null, new object[] { probe, false }), "standing does not");
            probe.position = new Vector3(6.5f, 0f, -3f);
            Assert.IsFalse((bool)isHidden.Invoke(null, new object[] { probe, true }), "open floor does not");
            probe.position = new Vector3(-10.4f, 0f, 8f);
            Assert.IsTrue((bool)isHidden.Invoke(null, new object[] { probe, true }), "under Grandpa's bed hides");
        }
    }
}
#endif
