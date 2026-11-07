using System.Reflection;
using SilentNight.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SilentNight.Player
{
    /// <summary>Builds a complete player GameObject. Usable from the editor scene builder and at runtime.</summary>
    public static class PlayerSetup
    {
        /// <summary>Elf size relative to the original 1.1 m design (the houses are human-sized, elves are tiny).</summary>
        public const float ElfScale = 0.55f;
        public const float ControllerHeight = 1.1f * ElfScale;
        public const float ControllerRadius = 0.3f * ElfScale;

        /// <summary>Player 1 (keyboard/mouse + free gamepads), with its own third-person camera and LocalCoopJoin.</summary>
        /// <param name="pos">Feet position.</param>
        /// <param name="themeIndex">0 green, 1 red, 2 blue, 3 purple.</param>
        public static GameObject CreatePlayer(Vector3 pos, int themeIndex) => CreatePlayer(pos, themeIndex, 0, null);

        /// <summary>
        /// Builds an elf for a local player slot.
        /// playerIndex 0 = player 1: camera rig + LocalCoopJoin, all non-claimed devices (old behaviour).
        /// playerIndex 1..3 = extra local elf: listens only to <paramref name="gamepad"/>, no camera rig of its own
        /// (shared screen: player 1's rig frames the group, ElfController falls back to Camera.main yaw for movement),
        /// and stays where it is created instead of snapping to PlayerSpawn.
        /// TODO (split-screen step): give extra elves their own rig/Camera/Brain + viewport when split-screen is on.
        /// </summary>
        public static GameObject CreatePlayer(Vector3 pos, int themeIndex, int playerIndex, Gamepad gamepad)
        {
            bool primary = playerIndex <= 0;
            var go = new GameObject(primary ? "Elf" : "Elf_P" + (playerIndex + 1));
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
            var input = go.AddComponent<ElfInput>();
            if (!primary) input.AssignPlayer(playerIndex, gamepad);
            var visual = go.AddComponent<ElfVisual>();
            visual.ThemeIndex = themeIndex;
            visual.EnsureAnchors();
            var carry = go.AddComponent<ElfCarry>();
            go.AddComponent<ElfInteractor>();
            var controller = go.AddComponent<ElfController>();
            if (!primary) DisableSpawnSnap(controller);
            go.AddComponent<ElfAnimator>();
            go.AddComponent<ElfRagdoll>();
            go.AddComponent<CatnipThrower>(); // [F] / LB: throw catnip (after ElfInput + ElfRagdoll, which it caches in Awake)
            go.AddComponent<ElfBonk>(); // co-op: elves running into each other bonk heads / shove (no-op solo)
            if (primary)
            {
                go.AddComponent<ThirdPersonCameraRig>();
                go.AddComponent<LocalCoopJoin>();
            }

            SetLayerRecursive(go.transform, go.layer);
            // After the layer pass: ElfHelpUp builds its own trigger child on the Interactable layer (in Start).
            go.AddComponent<ElfHelpUp>(); // co-op: a friend can help this elf up when it is downed
            // Co-op: a friend can grab the other end of this elf's bike (trigger child on the Interactable layer).
            ElfCoCarryHandle.Ensure(carry);
            // Co-op: "Эльф N" label above the head (separate root object, built in Start; hidden in solo).
            ElfNameTag.Ensure(go);
            return go;
        }

        /// <summary>
        /// Extra elves join next to player 1, so ElfController.Start must not teleport them to the (single) PlayerSpawn.
        /// The flag is a private serialized field; set it by reflection until step 3 (PlayerSpawner, spawn per
        /// PlayerIndex) gives ElfController a public API. Missing field = harmless (LocalCoopJoin re-places the elf).
        /// </summary>
        private static void DisableSpawnSnap(ElfController controller)
        {
            FieldInfo f = typeof(ElfController).GetField("moveToSpawnOnStart", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(bool)) f.SetValue(controller, false);
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
