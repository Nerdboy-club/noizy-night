#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SilentNight.EditorTools
{
    /// <summary>
    /// Renders high-resolution 1600x1000 preview PNGs of Hub, Rooftop, and House in Edit Mode
    /// so we can visually inspect lighting, materials, normal maps, and architectural details.
    /// </summary>
    public static class ScenePreviewRenderer
    {
        public static string OutputFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../preview_shots"));

        [MenuItem("Silent Night/Render Preview Screenshots", priority = 40)]
        public static void CaptureAll()
        {
            Directory.CreateDirectory(OutputFolder);

            CaptureScene("Assets/_Project/Scenes/Hub.unity", "hub_overview.png",
                new Vector3(0f, 3.3f, -6.6f), new Vector3(0f, 1.4f, 3.2f), 58f);

            CaptureScene("Assets/_Project/Scenes/Hub.unity", "hub_fireplace_conveyor.png",
                new Vector3(3.2f, 2.6f, -1.5f), new Vector3(-3.8f, 1.5f, 5.8f), 55f);

            CaptureScene("Assets/_Project/Scenes/Rooftop.unity", "rooftop_overview.png",
                new Vector3(-3.8f, 3.8f, -5.2f), new Vector3(14f, 0.8f, 8f), 58f);

            CaptureScene("Assets/_Project/Scenes/House.unity", "house_living.png",
                new Vector3(1.4f, 2.7f, 1.4f), new Vector3(7.2f, 1.3f, 7.2f), 58f);

            CaptureScene("Assets/_Project/Scenes/House.unity", "house_kitchen.png",
                new Vector3(2.0f, 2.7f, -2.0f), new Vector3(7.8f, 1.0f, -7.8f), 58f);

            CaptureScene("Assets/_Project/Scenes/House.unity", "house_bedroom.png",
                new Vector3(-4.4f, 2.6f, 5.2f), new Vector3(-9.8f, 1.1f, 8.2f), 58f);

            CaptureScene("Assets/_Project/Scenes/House.unity", "house_study_hall.png",
                new Vector3(-4.2f, 2.5f, 1.2f), new Vector3(-9.5f, 1.2f, 2.6f), 58f);

            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Boot.unity", OpenSceneMode.Single);
            Debug.Log("[Silent Night] Preview screenshots saved to: " + OutputFolder);
        }

        private static void CaptureScene(string scenePath, string fileName, Vector3 camPos, Vector3 lookAt, float fov)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // Ensure NPC placeholders are built in Edit Mode so Grandpa & Cat appear in the preview
            foreach (var npc in Object.FindObjectsByType<SilentNight.Stealth.NPCVisual>(FindObjectsSortMode.None))
            {
                npc.EnsureBuilt();
            }

            // Ensure WorldUvTiling is applied on all renderers
            foreach (var tiler in Object.FindObjectsByType<SilentNight.FX.WorldUvTiling>(FindObjectsSortMode.None))
            {
                tiler.Apply();
            }

            const int W = 1600, H = 1000;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);

            var camGo = new GameObject("PreviewCamera");
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.03f, 0.05f, 0.12f);
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 250f;
                cam.transform.position = camPos;
                cam.transform.LookAt(lookAt);

                var uacd = cam.GetUniversalAdditionalCameraData();
                uacd.renderPostProcessing = true;
                uacd.renderShadows = true;

                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();

                File.WriteAllBytes(Path.Combine(OutputFolder, fileName), tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }

        [MenuItem("Silent Night/Build All + Render Previews", priority = 41)]
        public static void BuildAndCapture()
        {
            TactileArtGenerator.GenerateAll();
            GreyboxSceneBuilder.BuildAll();
            CaptureAll();
        }
    }
}
#endif
