#if UNITY_EDITOR
using System.Collections.Generic;
using SilentNight.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SilentNight.EditorTools
{
    /// <summary>
    /// Menu "Silent Night/Setup Project": layers + tags, URP asset + renderer, post-processing profile,
    /// Always Included Shaders, Input System handler, linear colour space and shared greybox materials.
    /// Idempotent: re-running reuses/overwrites the existing assets.
    /// </summary>
    public static class ProjectBootstrap
    {
        public const string SettingsFolder = "Assets/_Project/Settings";
        public const string MaterialsFolder = "Assets/_Project/Settings/Materials";
        public const string UrpAssetPath = SettingsFolder + "/SilentNight_URP.asset";
        public const string UrpRendererPath = SettingsFolder + "/SilentNight_URP_Renderer.asset";
        public const string VolumeProfilePath = SettingsFolder + "/SilentNight_PostProcess.asset";
        public const string LitShaderName = "Universal Render Pipeline/Lit";
        public const string SimpleLitShaderName = "Universal Render Pipeline/Simple Lit";
        public const string PanelSettingsPath = "Assets/_Project/UI/GamePanelSettings.asset";

        // Material keys used by the scene builder.
        public const string MatWood = "Wood";
        public const string MatDarkWood = "DarkWood";
        public const string MatCarpet = "Carpet";
        public const string MatTile = "Tile";
        public const string MatIce = "Ice";
        public const string MatWall = "Wall";
        public const string MatBrick = "Brick";
        public const string MatSnow = "Snow";
        public const string MatEmissiveWarm = "EmissiveWarm";
        public const string MatConcrete = "Concrete";
        public const string MatRed = "Red";
        public const string MatGreen = "Green";
        public const string MatMetal = "Metal";
        public const string MatFabric = "Fabric";
        public const string MatGold = "Gold";
        public const string MatDark = "Dark";
        public const string MatPaper = "Paper";

        private static readonly string[] RequiredLayers =
        {
            GameConstants.LayerPlayer, GameConstants.LayerGift, GameConstants.LayerNPC,
            GameConstants.LayerInteractable, GameConstants.LayerCameraIgnore
        };

        private static readonly string[] RequiredTags = { GameConstants.TagSpawn };

        // ------------------------------------------------------------------ menu

        [MenuItem("Silent Night/Setup Project", priority = 0)]
        public static void SetupProject()
        {
            EnsureTagsAndLayers();
            EnsureFolder(SettingsFolder);
            EnsureFolder(MaterialsFolder);
            EnsureMaterials();
            EnsureRenderPipeline();
            EnsureVolumeProfile();
            EnsureAlwaysIncludedShader(LitShaderName);
            EnsureAlwaysIncludedShader(SimpleLitShaderName);
            EnsurePanelSettings();
            EnsureLinearColorSpace();
            bool inputChanged = EnsureInputSystemHandler();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Silent Night] Project setup complete. Next: Silent Night ▸ Build Greybox Scenes.");

            if (inputChanged)
            {
                if (EditorUtility.DisplayDialog("Silent Night",
                        "Active Input Handling switched to the Input System package.\nUnity must restart for this to take effect.\n\nRestart now?",
                        "Restart", "Later"))
                {
                    // VERIFY: reopening the current project restarts the editor (it prompts to save open scenes first).
                    EditorApplication.OpenProject(System.IO.Directory.GetCurrentDirectory());
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Silent Night", "Project setup complete.\nNext: Silent Night ▸ Build Greybox Scenes.", "OK");
            }
        }

        // ------------------------------------------------------------------ folders

        /// <summary>Creates an "Assets/..." folder hierarchy if missing.</summary>
        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash <= 0) return;
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------ tags & layers

        public static void EnsureTagsAndLayers()
        {
            Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (objs == null || objs.Length == 0)
            {
                Debug.LogError("[Silent Night] ProjectSettings/TagManager.asset not found.");
                return;
            }
            var so = new SerializedObject(objs[0]);

            SerializedProperty tags = so.FindProperty("tags");
            if (tags != null)
            {
                foreach (string tag in RequiredTags)
                {
                    bool found = false;
                    for (int i = 0; i < tags.arraySize; i++)
                        if (tags.GetArrayElementAtIndex(i).stringValue == tag) { found = true; break; }
                    if (found) continue;
                    tags.InsertArrayElementAtIndex(tags.arraySize);
                    tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
                    Debug.Log($"[Silent Night] Added tag '{tag}'.");
                }
            }

            SerializedProperty layers = so.FindProperty("layers");
            if (layers != null)
            {
                foreach (string layer in RequiredLayers)
                {
                    bool found = false;
                    for (int i = 0; i < layers.arraySize; i++)
                        if (layers.GetArrayElementAtIndex(i).stringValue == layer) { found = true; break; }
                    if (found) continue;

                    bool placed = false;
                    for (int i = 8; i < layers.arraySize; i++) // 0..7 are built-in
                    {
                        SerializedProperty p = layers.GetArrayElementAtIndex(i);
                        if (!string.IsNullOrEmpty(p.stringValue)) continue;
                        p.stringValue = layer;
                        placed = true;
                        Debug.Log($"[Silent Night] Added layer '{layer}' at index {i}.");
                        break;
                    }
                    if (!placed) Debug.LogError($"[Silent Night] No free user layer for '{layer}'.");
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ materials

        private static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        /// <summary>Creates/updates all shared greybox materials and returns them by key.</summary>
        public static Dictionary<string, Material> EnsureMaterials()
        {
            EnsureFolder(MaterialsFolder);
            MatCache.Clear();
            Make(MatWood, new Color(0.55f, 0.33f, 0.17f), 0.35f);
            Make(MatDarkWood, new Color(0.26f, 0.14f, 0.07f), 0.35f);
            Make(MatCarpet, new Color(0.52f, 0.11f, 0.13f), 0.05f);
            Make(MatTile, new Color(0.86f, 0.85f, 0.80f), 0.75f);
            Make(MatIce, new Color(0.72f, 0.88f, 0.99f), 0.95f, 0.1f);
            Make(MatWall, new Color(0.84f, 0.76f, 0.62f), 0.1f);
            Make(MatBrick, new Color(0.58f, 0.25f, 0.13f), 0.1f);
            Make(MatSnow, new Color(0.92f, 0.95f, 1.0f), 0.3f);
            Make(MatEmissiveWarm, new Color(1.0f, 0.86f, 0.62f), 0.3f, 0f, new Color(1.0f, 0.62f, 0.25f) * 2.5f);
            Make(MatConcrete, new Color(0.42f, 0.43f, 0.45f), 0.15f);
            Make(MatRed, new Color(0.75f, 0.12f, 0.12f), 0.3f);
            Make(MatGreen, new Color(0.10f, 0.38f, 0.18f), 0.15f);
            Make(MatMetal, new Color(0.60f, 0.63f, 0.68f), 0.55f, 0.8f);
            Make(MatFabric, new Color(0.20f, 0.30f, 0.50f), 0.05f);
            Make(MatGold, new Color(0.88f, 0.66f, 0.18f), 0.6f, 0.8f);
            Make(MatDark, new Color(0.07f, 0.06f, 0.06f), 0.1f);
            Make(MatPaper, new Color(0.95f, 0.91f, 0.80f), 0.05f);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, Material>(MatCache);
        }

        private static void Make(string key, Color color, float smoothness, float metallic = 0f, Color? emission = null)
        {
            string path = $"{MaterialsFolder}/M_{key}.mat";
            Shader shader = Shader.Find(LitShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Silent Night] Shader '{LitShaderName}' not found — is URP installed?");
                shader = Shader.Find("Standard");
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(shader);
            else if (mat.shader != shader) mat.shader = shader;

            mat.name = "M_" + key;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            mat.enableInstancing = true;

            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            MatCache[key] = mat;
        }

        // ------------------------------------------------------------------ URP

        public static UniversalRenderPipelineAsset EnsureRenderPipeline()
        {
            EnsureFolder(SettingsFolder);

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, UrpRendererPath);
            }
            AssignPostProcessData(rendererData);

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, UrpAssetPath);
            }
            else
            {
                // Make sure the renderer list points at our renderer.
                var so = new SerializedObject(urp);
                SerializedProperty list = so.FindProperty("m_RendererDataList");
                if (list != null)
                {
                    if (list.arraySize == 0) list.InsertArrayElementAtIndex(0);
                    list.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
                    SerializedProperty def = so.FindProperty("m_DefaultRendererIndex");
                    if (def != null) def.intValue = 0;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            ConfigureUrpAsset(urp);
            EditorUtility.SetDirty(rendererData);
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
            Debug.Log($"[Silent Night] URP asset assigned to Graphics settings and {names.Length} quality level(s).");
            return urp;
        }

        /// <summary>The renderer needs PostProcessData or bloom/tonemapping do nothing.</summary>
        private static void AssignPostProcessData(UniversalRendererData rendererData)
        {
            var so = new SerializedObject(rendererData);
            SerializedProperty prop = so.FindProperty("postProcessData");
            if (prop == null)
            {
                Debug.LogWarning("[Silent Night] UniversalRendererData has no 'postProcessData' property — check post-processing on the renderer asset manually.");
                return;
            }
            if (prop.objectReferenceValue != null) return;

            Object ppd = null;
            // VERIFY: default PostProcessData asset location inside the URP package.
            ppd = AssetDatabase.LoadAssetAtPath<Object>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (ppd == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:PostProcessData");
                if (guids.Length > 0) ppd = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (ppd == null)
            {
                Debug.LogWarning("[Silent Night] Default PostProcessData not found. Select " + UrpRendererPath + " and assign Post-processing ▸ Data manually.");
                return;
            }
            prop.objectReferenceValue = ppd;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureUrpAsset(UniversalRenderPipelineAsset urp)
        {
            // Public setters exist for these:
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 40f;
            urp.shadowCascadeCount = 2;

            // The rest via serialized names (internal setters in URP 17). VERIFY: property names (URP 17.0).
            var so = new SerializedObject(urp);
            SetInt(so, "m_MainLightRenderingMode", 1);          // PerPixel
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 2048);
            SetInt(so, "m_AdditionalLightsRenderingMode", 1);   // PerPixel
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_SoftShadowQuality", 2, warn: false);  // Medium (Unity 6)
            SetBool(so, "m_SupportsHDR", true);
            SetInt(so, "m_MSAA", 4);
            SetFloat(so, "m_RenderScale", 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(SerializedObject so, string name, int v, bool warn = true)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { if (warn) Debug.LogWarning($"[Silent Night] URP asset property '{name}' not found — set it in the inspector."); return; }
            p.intValue = v;
        }

        private static void SetBool(SerializedObject so, string name, bool v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Silent Night] URP asset property '{name}' not found — set it in the inspector."); return; }
            p.boolValue = v;
        }

        private static void SetFloat(SerializedObject so, string name, float v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Silent Night] URP asset property '{name}' not found — set it in the inspector."); return; }
            p.floatValue = v;
        }

        // ------------------------------------------------------------------ post-processing

        public static VolumeProfile EnsureVolumeProfile()
        {
            EnsureFolder(SettingsFolder);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }
            else
            {
                // Rebuild components from scratch (idempotent).
                for (int i = profile.components.Count - 1; i >= 0; i--)
                {
                    VolumeComponent c = profile.components[i];
                    if (c != null) Object.DestroyImmediate(c, true);
                }
                profile.components.Clear();
            }

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.65f));

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.45f);

            // Miniature / stop-motion diorama look: the far room softly out of focus.
            var dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(5f);
            dof.gaussianEnd.Override(16f);
            dof.gaussianMaxRadius.Override(1.1f);
            dof.highQualitySampling.Override(true);

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(8f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.15f);
            color.contrast.Override(14f);
            color.saturation.Override(12f);
            color.colorFilter.Override(new Color(1f, 0.96f, 0.9f));

            foreach (VolumeComponent c in profile.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ graphics settings

        /// <summary>Lets runtime Shader.Find(...) succeed in player builds.
        /// Note: an always-included Lit shader increases build time (all variants are compiled).</summary>
        public static void EnsureAlwaysIncludedShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning($"[Silent Night] Shader '{shaderName}' not found."); return; }

            Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (objs == null || objs.Length == 0) { Debug.LogWarning("[Silent Night] GraphicsSettings.asset not found."); return; }
            var so = new SerializedObject(objs[0]);
            SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) { Debug.LogWarning("[Silent Night] m_AlwaysIncludedShaders not found."); return; }

            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;

            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Silent Night] Added '{shaderName}' to Always Included Shaders.");
        }

        // ------------------------------------------------------------------ UI Toolkit

        /// <summary>Creates/updates the runtime PanelSettings asset used by UIRoot (Boot scene).</summary>
        public static UnityEngine.UIElements.PanelSettings EnsurePanelSettings()
        {
            EnsureFolder("Assets/_Project/UI");
            var ps = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(PanelSettingsPath);
            bool isNew = ps == null;
            if (isNew) ps = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();

            ps.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;

            if (ps.themeStyleSheet == null)
            {
                // VERIFY: Unity creates Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss the first time a
                // UI Toolkit asset is created via the editor menus. A theme cannot be generated from code, so if none
                // exists the theme stays empty (UIRoot styles everything with Game.uss anyway).
                UnityEngine.UIElements.ThemeStyleSheet theme = null;
                foreach (string guid in AssetDatabase.FindAssets("t:ThemeStyleSheet"))
                {
                    var t = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                    if (t == null) continue;
                    if (theme == null || t.name.Contains("Default")) theme = t;
                    if (t.name.Contains("DefaultRuntime")) break;
                }
                if (theme != null) ps.themeStyleSheet = theme;
                else Debug.LogWarning("[Silent Night] No ThemeStyleSheet found: PanelSettings theme left empty. " +
                                      "Optional fix: Assets ▸ Create ▸ UI Toolkit ▸ Default Runtime Theme File, assign it to " + PanelSettingsPath);
            }

            if (isNew) AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            else EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();
            return ps;
        }

        private static void EnsureLinearColorSpace()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        /// <summary>Sets Player ▸ Active Input Handling to "Input System Package (New)". Returns true if changed.
        /// There is no public API for this, so it is written through the serialized PlayerSettings.</summary>
        private static bool EnsureInputSystemHandler()
        {
            Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            Object playerSettings = objs != null && objs.Length > 0 ? objs[0] : null;
            if (playerSettings == null)
            {
                Debug.LogWarning("[Silent Night] ProjectSettings.asset not found: set Edit ▸ Project Settings ▸ Player ▸ Active Input Handling = Input System Package (New) manually.");
                return false;
            }
            var so = new SerializedObject(playerSettings);
            // VERIFY: serialized name "activeInputHandler" (0 = old, 1 = new Input System, 2 = both).
            SerializedProperty p = so.FindProperty("activeInputHandler");
            if (p == null)
            {
                Debug.LogWarning("[Silent Night] 'activeInputHandler' not found: set Active Input Handling manually (Project Settings ▸ Player).");
                return false;
            }
            if (p.intValue == 1 || p.intValue == 2) return false; // new or both: fine
            p.intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[Silent Night] Active Input Handling set to Input System Package (restart required).");
            return true;
        }
    }
}
#endif
