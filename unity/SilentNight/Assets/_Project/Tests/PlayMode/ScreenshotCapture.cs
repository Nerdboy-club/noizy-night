#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SilentNight.Tests
{
    /// <summary>
    /// Renders review screenshots of every scene (run explicitly, WITHOUT -nographics):
    ///   Unity -batchmode -projectPath ... -runTests -testPlatform PlayMode -testFilter SilentNight.Tests.ScreenshotCapture
    /// Output folder: env SN_SHOT_DIR, else &lt;project&gt;/Screenshots.
    /// </summary>
    [Explicit("Visual review only")]
    public class ScreenshotCapture
    {
        private static string Dir
        {
            get
            {
                string d = Environment.GetEnvironmentVariable("SN_SHOT_DIR");
                if (string.IsNullOrEmpty(d)) d = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        private static IEnumerator Load(string scene)
        {
            AsyncOperation op = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Project/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!op.isDone) yield return null;
            yield return new WaitForSeconds(2f);
        }

        private static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = prev;
            RenderTexture.active = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }

        /// <summary>Temporary camera copying the main camera's render settings (URP data, post-processing).</summary>
        private static Camera ViewCam(Vector3 pos, Vector3 lookAt, float fov = 55f)
        {
            Camera main = Camera.main;
            var go = main != null ? UnityEngine.Object.Instantiate(main.gameObject) : new GameObject("ShotCam", typeof(Camera));
            foreach (var b in go.GetComponents<Behaviour>()) if (!(b is Camera) && b.GetType().Name != "UniversalAdditionalCameraData") b.enabled = false;
            var cam = go.GetComponent<Camera>();
            cam.enabled = false;
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
            cam.fieldOfView = fov;
            return cam;
        }

        private static void Views(string prefix, (string name, Vector3 pos, Vector3 at)[] views)
        {
            if (Camera.main != null) Shot(Camera.main, prefix + "_gameplay");
            foreach (var v in views)
            {
                Camera c = ViewCam(v.pos, v.at);
                Shot(c, prefix + "_" + v.name);
                UnityEngine.Object.Destroy(c.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CaptureAll()
        {
            yield return Load("Hub");
            Views("hub", new[]
            {
                ("overview", new Vector3(-6f, 3.2f, -6.5f), new Vector3(1f, 0.6f, 2f)),
                ("workbench", new Vector3(2.5f, 1.4f, 0f), new Vector3(5.2f, 0.8f, 1.5f)),
            });

            yield return Load("Rooftop");
            Views("roof", new[]
            {
                ("overview", new Vector3(-4f, 5f, -8f), new Vector3(14f, 0f, 8f)),
            });

            yield return Load("House");
            Views("house", new[]
            {
                ("living", new Vector3(1.0f, 2.2f, 1.0f), new Vector3(8f, 0.6f, 7f)),
                ("kitchen", new Vector3(1.0f, 2.2f, -1.0f), new Vector3(8f, 0.5f, -8f)),
                ("hallway", new Vector3(-1.75f, 1.6f, -10f), new Vector3(-1.75f, 0.8f, 3f)),
                ("bedroom", new Vector3(-4.5f, 2.2f, 5f), new Vector3(-10f, 0.5f, 9f)),
                ("elf_close", new Vector3(8.6f, 0.8f, 2.4f), new Vector3(9.4f, 0.35f, 3.5f)),
            });
            yield return null;
            Assert.Pass("Screenshots written to " + Dir);
        }
    }
}
#endif
