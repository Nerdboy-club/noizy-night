#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SilentNight.Tests
{
    /// <summary>Gift mechanics: fragile snow globe breaks on hard impacts only; music box rings when wound down.</summary>
    public class GiftMechanicsTests
    {
        private static Type T(string n) => Type.GetType(n + ", Assembly-CSharp", true);

        private static IEnumerator LoadHub()
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/Hub.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return null;
        }

        private static Component Spawn(string kind, Vector3 pos)
        {
            Type kindT = T("SilentNight.Core.GiftKind");
            object k = Enum.Parse(kindT, kind);
            MethodInfo create = T("SilentNight.Gifts.GiftFactory").GetMethod("Create");
            return (Component)create.Invoke(null, new object[] { k, pos, Quaternion.identity });
        }

        private static bool Broken(Component gift) => (bool)gift.GetType().GetProperty("IsBroken").GetValue(gift);

        [UnityTest]
        public IEnumerator Fragile_BreaksOnHardHit_SurvivesGentleDrop()
        {
            yield return LoadHub();
            yield return new WaitForSeconds(0.5f);

            // Away from the stations, above the rug (floor top y = 0).
            Component gentle = Spawn("Fragile", new Vector3(-2f, 0.25f, -1.5f));
            Component hard = Spawn("Fragile", new Vector3(2f, 1.5f, -1.5f));
            hard.GetComponent<Rigidbody>().linearVelocity = new Vector3(0f, -5f, 0f);
            yield return new WaitForSeconds(1.5f);

            Assert.IsFalse(Broken(gentle), "a gentle drop does not break the globe");
            Assert.IsTrue(Broken(hard), "a hard hit breaks the globe");
            float integrity = (float)hard.GetType().GetProperty("Integrity").GetValue(hard);
            Assert.AreEqual(0f, integrity, 0.01f, "broken globe pays nothing");
        }

        [UnityTest]
        public IEnumerator MusicBox_RingsWhenWoundDown_PickupRewinds()
        {
            yield return LoadHub();
            yield return new WaitForSeconds(0.5f);
            Component box = Spawn("Noisy", new Vector3(-2f, 0.3f, -1.5f));
            FieldInfo wind = box.GetType().GetField("_windLeft", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo ring = box.GetType().GetField("_ringLeft", BindingFlags.NonPublic | BindingFlags.Instance);
            wind.SetValue(box, 0.05f);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater((int)ring.GetValue(box), 0, "box starts ringing when the spring runs down");

            // Picking it up re-winds and silences it.
            var hold = new GameObject("TestHold").transform;
            box.GetType().GetMethod("OnPickedUp").Invoke(box, new object[] { hold });
            Assert.AreEqual(0, (int)ring.GetValue(box), "pickup stops the ringing");
            Assert.Greater((float)wind.GetValue(box), 30f, "pickup re-winds the key");
        }
        [UnityTest]
        public IEnumerator RcCar_DrivesOffByItselfAfterDrop()
        {
            yield return LoadHub();
            yield return new WaitForSeconds(0.5f);
            Component car = Spawn("RcCar", new Vector3(-2f, 0.2f, -1.5f));
            yield return new WaitForSeconds(0.5f);
            Vector3 start = car.transform.position;
            car.GetType().GetMethod("OnDropped").Invoke(car, new object[] { Vector3.zero });
            yield return new WaitForSeconds(3.5f);
            float moved = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(car.transform.position.x, 0f, car.transform.position.z));
            Debug.Log("[Smoke] rc car drove " + moved.ToString("0.00") + " m");
            Assert.Greater(moved, 1f, "rc car drives away on its own");
        }
    }
}
#endif
