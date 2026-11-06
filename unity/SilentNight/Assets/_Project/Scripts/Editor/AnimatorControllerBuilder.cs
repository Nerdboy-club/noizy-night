#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SilentNight.EditorTools
{
    /// <summary>
    /// Menu "Silent Night/Build Animator Controllers".
    /// For Elf / Grandpa / Cat: gathers clips from Assets/_Project/Art/Characters/{X}/Animations and writes
    /// Assets/_Project/Art/Characters/{X}/{X}.controller (rebuilt in place, the asset GUID is kept).
    /// Parameters match the runtime drivers:
    ///   ElfAnimator: float Speed, float VerticalSpeed, bool Grounded, bool Crouch, bool Carrying, bool Jetpack, trigger Jump.
    ///   NPCVisual:   float Speed, bool Sleeping, int Alert (AlertLevel: 0 Asleep .. 4 Chasing).
    /// Clips are matched by case-insensitive keywords in "fileName clipName". A missing clip falls back to a
    /// similar one (e.g. Sneak → Walk); if nothing fits the state has no motion. Every miss is logged.
    /// </summary>
    public static class AnimatorControllerBuilder
    {
        private const string Root = "Assets/_Project/Art/Characters";

        // Speeds from ElfController / Grandpa / FatCat defaults.
        private const float ElfWalk = 4.2f, ElfRun = 6.4f, ElfCrouch = 2.4f;
        private const float GrandpaWalk = 1.2f, GrandpaRun = 3.2f;
        private const float CatWalk = 0.9f, CatRun = 2.0f;

        private sealed class Clip
        {
            public AnimationClip Asset;
            public string Key; // lower-case "file clip"
        }

        [MenuItem("Silent Night/Build Animator Controllers", priority = 20)]
        public static void BuildAll()
        {
            BuildElf();
            BuildNpc("Grandpa", GrandpaWalk, GrandpaRun);
            BuildNpc("Cat", CatWalk, CatRun);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Silent Night] Animator controllers built. Assign them to the Animator of each model prefab.");
        }

        // ------------------------------------------------------------------ Elf

        private static void BuildElf()
        {
            const string name = "Elf";
            List<Clip> clips = GatherClips(name);
            AnimatorController ctrl = PrepareController(name);

            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Carrying", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Jetpack", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            SetDefaultBool(ctrl, "Grounded", true);

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

            AnimationClip idle = Find(clips, name, "Idle", new[] { "idle" }, null, new[] { "crouch", "sneak", "carry", "sleep" });
            AnimationClip walk = Find(clips, name, "Walk", new[] { "walk" }, null, new[] { "crouch", "sneak", "carry" });
            AnimationClip run = Find(clips, name, "Run", null, new[] { "run", "sprint", "jog" }, new[] { "carry", "jump" });
            AnimationClip crouchIdle = Find(clips, name, "CrouchIdle", new[] { "crouch", "idle" }, null, null);
            AnimationClip sneak = Find(clips, name, "Sneak", null, new[] { "sneak", "crouch walk", "crouched walk", "crouch_walk", "crouchwalk", "crouched_walk" }, null);
            AnimationClip carryIdle = Find(clips, name, "CarryIdle", new[] { "carry", "idle" }, null, null);
            AnimationClip carryWalk = Find(clips, name, "CarryWalk", new[] { "carry" }, new[] { "walk", "run" }, null);
            AnimationClip jump = Find(clips, name, "Jump", new[] { "jump" }, null, null);
            AnimationClip fall = Find(clips, name, "Fall", null, new[] { "fall", "falling" }, null);
            AnimationClip jet = Find(clips, name, "Jetpack", null, new[] { "jetpack", "fly", "flying", "hover" }, null);

            // Fallbacks for missing clips.
            run = Fallback(name, "Run", run, walk);
            crouchIdle = Fallback(name, "CrouchIdle", crouchIdle, idle);
            sneak = Fallback(name, "Sneak", sneak, walk);
            carryIdle = Fallback(name, "CarryIdle", carryIdle, idle);
            carryWalk = Fallback(name, "CarryWalk", carryWalk, walk);
            fall = Fallback(name, "Fall", fall, jump != null ? jump : idle);
            jet = Fallback(name, "Jetpack", jet, fall);

            AnimatorState loco = TreeState(ctrl, sm, "Locomotion", "Speed", new Vector3(300, 0),
                (idle, 0f), (walk, ElfWalk), (run, ElfRun));
            AnimatorState crouch = TreeState(ctrl, sm, "Crouch", "Speed", new Vector3(300, 120),
                (crouchIdle, 0f), (sneak, ElfCrouch));
            AnimatorState carry = TreeState(ctrl, sm, "Carry", "Speed", new Vector3(300, -120),
                (carryIdle, 0f), (carryWalk, ElfWalk));
            AnimatorState jumpSt = ClipState(sm, "Jump", jump, new Vector3(600, -60));
            AnimatorState fallSt = ClipState(sm, "Fall", fall, new Vector3(600, 60));
            AnimatorState jetSt = ClipState(sm, "Jetpack", jet, new Vector3(600, 200));
            sm.defaultState = loco;

            // Any State → Jetpack / Jump (Jetpack first = higher priority).
            AnimatorStateTransition t = sm.AddAnyStateTransition(jetSt);
            Setup(t, 0.15f); t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Jetpack");

            t = sm.AddAnyStateTransition(jumpSt);
            Setup(t, 0.08f); t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            t = jetSt.AddTransition(fallSt); Setup(t, 0.2f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Jetpack");

            // Jump → land / fall.
            t = jumpSt.AddTransition(loco); Setup(t, 0.12f, 0.5f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            t = jumpSt.AddTransition(fallSt); Setup(t, 0.2f, 0.85f);

            // Ground states → Fall (only for real drops, not small steps).
            foreach (AnimatorState s in new[] { loco, crouch, carry })
            {
                t = s.AddTransition(fallSt); Setup(t, 0.2f);
                t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                t.AddCondition(AnimatorConditionMode.Less, -2.5f, "VerticalSpeed");
            }

            // Fall → landing.
            t = fallSt.AddTransition(crouch); Setup(t, 0.1f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            t.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");
            t = fallSt.AddTransition(carry); Setup(t, 0.1f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            t.AddCondition(AnimatorConditionMode.If, 0f, "Carrying");
            t = fallSt.AddTransition(loco); Setup(t, 0.1f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            // Locomotion family. Crouch wins over Carry.
            t = loco.AddTransition(crouch); Setup(t, 0.15f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");
            t = loco.AddTransition(carry); Setup(t, 0.15f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Carrying");
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");

            t = crouch.AddTransition(carry); Setup(t, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");
            t.AddCondition(AnimatorConditionMode.If, 0f, "Carrying");
            t = crouch.AddTransition(loco); Setup(t, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Carrying");

            t = carry.AddTransition(crouch); Setup(t, 0.15f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");
            t = carry.AddTransition(loco); Setup(t, 0.15f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Carrying");

            Finish(ctrl);
        }

        // ------------------------------------------------------------------ NPC (Grandpa / Cat)

        private static void BuildNpc(string name, float walkSpeed, float runSpeed)
        {
            List<Clip> clips = GatherClips(name);
            AnimatorController ctrl = PrepareController(name);

            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Sleeping", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Alert", AnimatorControllerParameterType.Int);
            SetDefaultBool(ctrl, "Sleeping", true);

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

            AnimationClip sleep = Find(clips, name, "Sleep", null, new[] { "sleep", "lying", "laying", "asleep" }, null);
            AnimationClip idle = Find(clips, name, "Idle", new[] { "idle" }, null, new[] { "sleep", "alert" });
            AnimationClip walk = Find(clips, name, "Walk", new[] { "walk" }, null, null);
            AnimationClip run = Find(clips, name, "Run", null, new[] { "run", "jog", "sprint", "chase" }, null);
            AnimationClip alert = Find(clips, name, "Alert", null, new[] { "alert", "surprise", "look around", "looking around", "search", "angry", "suspicious" }, null);

            run = Fallback(name, "Run", run, walk);
            sleep = Fallback(name, "Sleep", sleep, idle);
            alert = Fallback(name, "Alert", alert, idle);

            AnimatorState sleepSt = ClipState(sm, "Sleep", sleep, new Vector3(300, -120));
            AnimatorState loco = TreeState(ctrl, sm, "Locomotion", "Speed", new Vector3(300, 0),
                (idle, 0f), (walk, walkSpeed), (run, runSpeed));
            AnimatorState alertSt = ClipState(sm, "Alert", alert, new Vector3(300, 120));
            sm.defaultState = sleepSt;

            AnimatorStateTransition t = sleepSt.AddTransition(loco); Setup(t, 0.4f);
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sleeping");

            t = loco.AddTransition(sleepSt); Setup(t, 0.4f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Sleeping");
            t = alertSt.AddTransition(sleepSt); Setup(t, 0.4f);
            t.AddCondition(AnimatorConditionMode.If, 0f, "Sleeping");

            // Standing still while Suspicious or above → Alert pose.
            t = loco.AddTransition(alertSt); Setup(t, 0.25f);
            t.AddCondition(AnimatorConditionMode.Greater, 1f, "Alert");
            t.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sleeping");

            t = alertSt.AddTransition(loco); Setup(t, 0.2f);
            t.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");
            t = alertSt.AddTransition(loco); Setup(t, 0.3f);
            t.AddCondition(AnimatorConditionMode.Less, 2f, "Alert");

            Finish(ctrl);
        }

        // ------------------------------------------------------------------ clips

        private static List<Clip> GatherClips(string character)
        {
            var result = new List<Clip>();
            string folder = $"{Root}/{character}/Animations";
            ProjectBootstrap.EnsureFolder(folder);

            var seen = new HashSet<string>();
            var guids = new List<string>(AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }));
            guids.AddRange(AssetDatabase.FindAssets("t:Model", new[] { folder })); // FBX files holding clips
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path)) continue;
                string file = Path.GetFileNameWithoutExtension(path);
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var clip = o as AnimationClip;
                    if (clip == null || clip.name.StartsWith("__preview__")) continue;
                    result.Add(new Clip { Asset = clip, Key = (file + " " + clip.name).ToLowerInvariant().Replace('-', ' ') });
                }
            }
            if (result.Count == 0)
                Debug.LogWarning($"[Silent Night] {character}: no animation clips in {folder} — controller built with empty states.");
            else
                Debug.Log($"[Silent Night] {character}: found {result.Count} clip(s).");
            return result;
        }

        /// <param name="all">every keyword must appear</param>
        /// <param name="any">at least one must appear (ignored if null)</param>
        /// <param name="none">none may appear</param>
        private static AnimationClip Find(List<Clip> clips, string character, string label, string[] all, string[] any, string[] none)
        {
            foreach (Clip c in clips)
            {
                if (all != null && !ContainsAll(c.Key, all)) continue;
                if (any != null && !ContainsAny(c.Key, any)) continue;
                if (none != null && ContainsAny(c.Key, none)) continue;
                return c.Asset;
            }
            if (clips.Count > 0) Debug.LogWarning($"[Silent Night] {character}: no clip found for '{label}'.");
            return null;
        }

        private static AnimationClip Fallback(string character, string label, AnimationClip clip, AnimationClip fallback)
        {
            if (clip != null) return clip;
            if (fallback != null) Debug.LogWarning($"[Silent Night] {character}: '{label}' uses fallback clip '{fallback.name}'.");
            return fallback;
        }

        private static bool ContainsAll(string s, string[] keys)
        {
            foreach (string k in keys) if (!s.Contains(k)) return false;
            return true;
        }

        private static bool ContainsAny(string s, string[] keys)
        {
            foreach (string k in keys) if (s.Contains(k)) return true;
            return false;
        }

        // ------------------------------------------------------------------ controller helpers

        /// <summary>Loads (and clears) or creates Root/{name}/{name}.controller with one empty "Base Layer".</summary>
        private static AnimatorController PrepareController(string name)
        {
            string folder = $"{Root}/{name}";
            ProjectBootstrap.EnsureFolder(folder);
            string path = $"{folder}/{name}.controller";

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl == null)
            {
                ctrl = AnimatorController.CreateAnimatorControllerAtPath(path); // adds "Base Layer"
            }
            else
            {
                // VERIFY: clearing layers/parameters and destroying old sub-assets keeps the asset GUID intact.
                ctrl.layers = new AnimatorControllerLayer[0];
                ctrl.parameters = new AnimatorControllerParameter[0];
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o == null || o == ctrl) continue;
                    Object.DestroyImmediate(o, true);
                }
                ctrl.AddLayer("Base Layer");
            }
            return ctrl;
        }

        private static void SetDefaultBool(AnimatorController ctrl, string param, bool value)
        {
            AnimatorControllerParameter[] ps = ctrl.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == param) ps[i].defaultBool = value;
            ctrl.parameters = ps;
        }

        private static AnimatorState ClipState(AnimatorStateMachine sm, string name, AnimationClip clip, Vector3 pos)
        {
            AnimatorState s = sm.AddState(name, pos);
            s.motion = clip;
            s.writeDefaultValues = true;
            if (clip == null) Debug.LogWarning($"[Silent Night] State '{name}' has no motion.");
            return s;
        }

        private static AnimatorState TreeState(AnimatorController ctrl, AnimatorStateMachine sm, string name, string param,
            Vector3 pos, params (AnimationClip clip, float threshold)[] children)
        {
            AnimatorState s = sm.AddState(name, pos);
            s.writeDefaultValues = true;

            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = param,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            int added = 0;
            var used = new HashSet<float>();
            foreach (var c in children)
            {
                if (c.clip == null || !used.Add(c.threshold)) continue;
                tree.AddChild(c.clip, c.threshold);
                added++;
            }

            if (added == 0)
            {
                Object.DestroyImmediate(tree);
                Debug.LogWarning($"[Silent Night] State '{name}' has no motion.");
                return s;
            }

            // VERIFY: a BlendTree created with `new` must be added as a sub-asset to persist.
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            s.motion = tree;
            return s;
        }

        private static void Setup(AnimatorStateTransition t, float duration, float exitTime = -1f)
        {
            t.duration = duration;
            t.hasFixedDuration = true;
            t.interruptionSource = TransitionInterruptionSource.None;
            if (exitTime >= 0f)
            {
                t.hasExitTime = true;
                t.exitTime = exitTime;
            }
            else
            {
                t.hasExitTime = false;
            }
        }

        private static void Finish(AnimatorController ctrl)
        {
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Silent Night] Wrote {AssetDatabase.GetAssetPath(ctrl)}");
        }
    }
}
#endif
