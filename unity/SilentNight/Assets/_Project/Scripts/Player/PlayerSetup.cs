using SilentNight.Core;
using UnityEngine;

namespace SilentNight.Player
{
    /// <summary>Builds a complete player GameObject. Usable from the editor scene builder and at runtime.</summary>
    public static class PlayerSetup
    {
        /// <summary>Elf size relative to the original 1.1 m design (the houses are human-sized, elves are tiny).</summary>
        public const float ElfScale = 0.55f;
        public const float ControllerHeight = 1.1f * ElfScale;
        public const float ControllerRadius = 0.3f * ElfScale;

        /// <param name="pos">Feet position.</param>
        /// <param name="themeIndex">0 green, 1 red, 2 blue, 3 purple.</param>
        public static GameObject CreatePlayer(Vector3 pos, int themeIndex)
        {
            var go = new GameObject("Elf");
            TrySetTag(go, GameConstants.TagPlayer);
            int layer = LayerMask.NameToLayer(GameConstants.LayerPlayer);
            if (layer >= 0) go.layer = layer;
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);

            var cc = go.AddComponent<CharacterController>();
            cc.height = ControllerHeight;
            cc.radius = ControllerRadius;
            cc.center = new Vector3(0f, ControllerHeight * 0.5f, 0f);
            cc.stepOffset = 0.3f * ElfScale;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;

            // Order matters at runtime (Awake runs inside AddComponent): visual → carry → interactor → controller → animator → rig.
            go.AddComponent<ElfInput>();
            var visual = go.AddComponent<ElfVisual>();
            visual.ThemeIndex = themeIndex;
            visual.EnsureAnchors();
            go.AddComponent<ElfCarry>();
            go.AddComponent<ElfInteractor>();
            go.AddComponent<ElfController>();
            go.AddComponent<ElfAnimator>();
            go.AddComponent<ElfRagdoll>();
            go.AddComponent<CatnipThrower>(); // [F] / LB: throw catnip (after ElfInput + ElfRagdoll, which it caches in Awake)
            go.AddComponent<ThirdPersonCameraRig>();

            SetLayerRecursive(go.transform, go.layer);
            return go;
        }

        private static void TrySetTag(GameObject go, string tag)
        {
            try { go.tag = tag; }
            catch (UnityException) { Debug.LogWarning($"[PlayerSetup] Tag '{tag}' is not defined."); }
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayerRecursive(t.GetChild(i), layer);
        }
    }
}
