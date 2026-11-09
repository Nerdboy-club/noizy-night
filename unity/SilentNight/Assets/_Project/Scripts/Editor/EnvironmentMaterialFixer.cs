#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SilentNight.EditorTools
{
    /// <summary>
    /// After an FBX under Art/Environment/&lt;Room&gt;/ is imported (made by art/blender/prep_prop.py), builds a URP/Lit material
    /// from the textures saved next to it (&lt;Name&gt;_Textures/&lt;Name&gt;_albedo|normal|metalrough.png) and remaps the model to it.
    /// Also available as menu "Silent Night/Fix Environment Materials".
    /// </summary>
    public class EnvironmentMaterialFixer : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Art/Environment/";
        private const string ElfRoot = "Assets/_Project/Art/Characters/Elf/";
        private const string CatRoot = "Assets/_Project/Art/Characters/Cat/";

        private static bool Handles(string path) =>
            path.EndsWith(".fbx") && (path.StartsWith(Root) || path.StartsWith(ElfRoot) || path.StartsWith(CatRoot));

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (!Handles(path)) continue;
                string p = path;
                EditorApplication.delayCall += () => Fix(p);
            }
        }

        [MenuItem("Silent Night/Fix Environment Materials", priority = 31)]
        public static void FixAll()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root.TrimEnd('/'), ElfRoot.TrimEnd('/'), CatRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Handles(path)) Fix(path);
            }
        }

        private static void Fix(string fbxPath)
        {
            if (fbxPath.Contains("/Packs/"))
            {
                FixPackModel(fbxPath);
                return;
            }

            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(fbxPath);
            string texDir = dir + "/" + name + "_Textures/";
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + name + "_albedo.png");
            if (albedo == null) return; // not a prepared prop

            string matPath = dir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null) { Debug.LogWarning("[Silent Night] URP Lit shader not found."); return; }
                mat = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + name + "_normal.png");
            if (normal != null)
            {
                SetNormalMap(texDir + name + "_normal.png");
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(mat);

            // Remap every source material of the model to our URP material.
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            bool changed = false;
            foreach (Object o in all)
            {
                if (!(o is Material src) || src == mat) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name);
                if (!importer.GetExternalObjectMap().TryGetValue(id, out Object cur) || cur != mat)
                {
                    importer.AddRemap(id, mat);
                    changed = true;
                }
            }
            if (changed)
            {
                importer.SaveAndReimport();
                Debug.Log("[Silent Night] Environment material built for " + name);
            }
            AssetDatabase.SaveAssets();
        }

        private static void SetNormalMap(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null || ti.textureType == TextureImporterType.NormalMap) return;
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }

        private static void FixPackModel(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return;

            const string packMatsDir = "Assets/_Project/Settings/Materials/Packs";
            ProjectBootstrap.EnsureFolder(packMatsDir);

            bool isHoliday = fbxPath.Contains("KenneyHoliday");
            Texture2D holidayAtlas = isHoliday
                ? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Environment/Packs/KenneyHoliday/Textures/colormap.png")
                : null;

            Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            bool changed = false;

            foreach (Object o in all)
            {
                if (!(o is Material src)) continue;
                if (src.shader == lit && AssetDatabase.GetAssetPath(src).StartsWith(packMatsDir)) continue;

                string safeName = src.name.Replace(':', '_').Replace('/', '_').Replace(' ', '_');
                string matPath = $"{packMatsDir}/M_Pack_{(isHoliday ? "Holiday_" : "Furn_")}{safeName}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(lit) { name = Path.GetFileNameWithoutExtension(matPath) };
                    AssetDatabase.CreateAsset(mat, matPath);
                }

                if (isHoliday && holidayAtlas != null)
                {
                    mat.SetTexture("_BaseMap", holidayAtlas);
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetFloat("_Smoothness", 0.38f);
                    mat.SetFloat("_Metallic", 0.05f);
                }
                else
                {
                    Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                            : (src.HasProperty("_Color") ? src.GetColor("_Color") : new Color(0.75f, 0.55f, 0.38f));
                    string lower = src.name.ToLowerInvariant();
                    float smooth = 0.35f;
                    float metal = 0f;

                    if (lower.Contains("metal") || lower.Contains("silver") || lower.Contains("steel"))
                    {
                        smooth = 0.72f;
                        metal = 0.8f;
                    }
                    else if (lower.Contains("gold") || lower.Contains("brass") || lower.Contains("yellow"))
                    {
                        smooth = 0.75f;
                        metal = 0.65f;
                    }
                    else if (lower.Contains("glass") || lower.Contains("screen"))
                    {
                        smooth = 0.88f;
                        metal = 0.2f;
                    }
                    else if (lower.Contains("wood"))
                    {
                        smooth = 0.42f;
                    }
                    else if (lower.Contains("carpet") || lower.Contains("fabric") || lower.Contains("cushion") || lower.Contains("cloth"))
                    {
                        smooth = 0.15f;
                    }

                    mat.SetColor("_BaseColor", c);
                    mat.SetFloat("_Smoothness", smooth);
                    mat.SetFloat("_Metallic", metal);
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);

                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name);
                if (!importer.GetExternalObjectMap().TryGetValue(id, out Object cur) || cur != mat)
                {
                    importer.AddRemap(id, mat);
                    changed = true;
                }
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }
    }
}
#endif
