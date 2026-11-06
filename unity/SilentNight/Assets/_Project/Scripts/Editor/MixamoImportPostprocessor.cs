#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SilentNight.EditorTools
{
    /// <summary>
    /// Import settings for character models under Assets/_Project/Art/Characters/**:
    /// Humanoid rig (Cat: Generic — a quadruped cannot be Humanoid), scale factor 1, materials imported for models.
    /// Files inside an "Animations" sub-folder: clips renamed after the file (Mixamo names every clip "mixamo.com"),
    /// root motion baked into pose, loopTime on for looping clips (Idle/Walk/Run/Sneak/Carry/...).
    /// Settings are applied on first import only (so manual tweaks survive); use
    /// "Silent Night/Reapply Character Import Settings" to force them again.
    /// </summary>
    public class MixamoImportPostprocessor : AssetPostprocessor
    {
        public const string CharactersRoot = "Assets/_Project/Art/Characters/";

        private static readonly string[] LoopKeywords =
            { "idle", "walk", "run", "sneak", "carry", "crouch", "jog", "sprint", "fall", "jetpack", "fly", "hover", "sleep", "breath" };
        private static readonly string[] NoLoopKeywords =
            { "jump", "land", "start", "stop", "turn", "to_", "wake", "getup", "get up", "standup", "stand up" };

        private static bool _forceApply;

        private static bool IsCharacterPath(string path) =>
            path.Replace('\\', '/').StartsWith(CharactersRoot, System.StringComparison.OrdinalIgnoreCase);

        private static bool IsAnimationPath(string path) =>
            path.Replace('\\', '/').IndexOf("/Animations/", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsCatPath(string path) =>
            path.Replace('\\', '/').IndexOf("/Characters/Cat/", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private bool ShouldApply => _forceApply || assetImporter.importSettingsMissing;

        private void OnPreprocessModel()
        {
            if (!IsCharacterPath(assetPath)) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null || !ShouldApply) return;

            bool anim = IsAnimationPath(assetPath);
            bool cat = IsCatPath(assetPath);

            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = true;

            if (cat)
            {
                mi.animationType = ModelImporterAnimationType.Generic;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; // VERIFY: available since 2020.x
            }

            // Models get their materials; animation-only files don't need copies of them.
            mi.materialImportMode = anim ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportStandard;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsCharacterPath(assetPath) || !IsAnimationPath(assetPath)) return;
            var mi = assetImporter as ModelImporter;
            if (mi == null || !ShouldApply) return;

            // VERIFY: defaultClipAnimations is populated during OnPreprocessAnimation (common pattern, works on first import).
            ModelImporterClipAnimation[] clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;

            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation c = clips[i];
                if (string.IsNullOrEmpty(c.name) || c.name.ToLowerInvariant().Contains("mixamo") || clips.Length == 1)
                    c.name = clips.Length == 1 ? fileName : fileName + "_" + i;

                bool loop = ShouldLoop(fileName + " " + c.name);
                c.loopTime = loop;
                c.loopPose = false;

                // In-place animation: the CharacterController / NavMeshAgent moves the character.
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalPositionXZ = true;
                clips[i] = c;
            }
            mi.clipAnimations = clips;
        }

        public static bool ShouldLoop(string name)
        {
            string n = name.ToLowerInvariant();
            foreach (string k in NoLoopKeywords) if (n.Contains(k)) return false;
            foreach (string k in LoopKeywords) if (n.Contains(k)) return true;
            return false;
        }

        [MenuItem("Silent Night/Reapply Character Import Settings", priority = 30)]
        private static void ReapplyAll()
        {
            if (!AssetDatabase.IsValidFolder(CharactersRoot.TrimEnd('/')))
            {
                Debug.LogWarning("[Silent Night] " + CharactersRoot + " does not exist yet.");
                return;
            }
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { CharactersRoot.TrimEnd('/') });
            _forceApply = true;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string g in guids)
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                _forceApply = false;
            }
            Debug.Log($"[Silent Night] Re-imported {guids.Length} character model(s).");
        }
    }
}
#endif
