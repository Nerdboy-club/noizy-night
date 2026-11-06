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
            path.EndsWith(".fbx") && !path.Contains("/Packs/") && (path.StartsWith(Root) || path.StartsWith(ElfRoot) || path.StartsWith(CatRoot));

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
        private static void FixAll()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root.TrimEnd('/'), ElfRoot.TrimEnd('/'), CatRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Handles(path)) Fix(path);
            }
        }

        private static void Fix(string fbxPath)
        {
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
    }
}
#endif
