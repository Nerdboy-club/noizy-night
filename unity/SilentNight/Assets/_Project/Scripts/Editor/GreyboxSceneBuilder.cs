#if UNITY_EDITOR
using System.Collections.Generic;
using SilentNight.Audio;
using SilentNight.Core;
using SilentNight.FX;
using SilentNight.Gifts;
using SilentNight.Stealth;
using SilentNight.UI;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SilentNight.EditorTools
{
    /// <summary>
    /// Menu "Silent Night/Build Greybox Scenes": builds Boot, Hub and House from primitives, saves them under
    /// Assets/_Project/Scenes and sets Build Settings to [Boot, Hub, House]. Re-running overwrites everything.
    /// Coordinates are Unity world metres (prototype three.js positions with Z negated, re-arranged into real rooms).
    /// </summary>
    public static class GreyboxSceneBuilder
    {
        private const string ScenesFolder = "Assets/_Project/Scenes";
        private const string BootPath = ScenesFolder + "/Boot.unity";
        private const string HubPath = ScenesFolder + "/Hub.unity";
        private const string HousePath = ScenesFolder + "/House.unity";
        private const string RooftopPath = ScenesFolder + "/Rooftop.unity";
        private const string HouseNavMeshFolder = ScenesFolder + "/House";
        private const string HouseNavMeshPath = HouseNavMeshFolder + "/NavMesh-House.asset";
        private const string LightingSettingsPath = ProjectBootstrap.SettingsFolder + "/SilentNight_Lighting.lighting";

        /// <summary>Bake environment lighting (ambient probe + reflection) after saving each gameplay scene.
        /// No lightmaps are baked (baked/realtime GI are off), so this is quick.</summary>
        private const bool BakeEnvironmentLighting = true;

        private const float DoorW = 1.4f;   // ≥ 1.2 m: the huge gift (1.64 m) passes only when carried lengthwise
        private const float DoorH = 2.65f;  // Human-scale tall doorways vs miniature 0.55x elf
        private const string AutoBuildKey = "SilentNight_AutoSceneRebuild_v5";

        private static Dictionary<string, Material> _mats;
        private static VolumeProfile _profile;

        [InitializeOnLoadMethod]
        private static void AutoRebuildIfNeeded()
        {
            if (Application.isBatchMode) return;
            if (EditorPrefs.GetInt(AutoBuildKey, 0) >= 1) return;

            EditorApplication.delayCall += TryRunAutoRebuild;
            EditorApplication.playModeStateChanged += OnPlayModeChangedForAutoRebuild;
        }

        private static void OnPlayModeChangedForAutoRebuild(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += TryRunAutoRebuild;
        }

        private static void TryRunAutoRebuild()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;
            if (EditorPrefs.GetInt(AutoBuildKey, 0) >= 1) return;

            EditorPrefs.SetInt(AutoBuildKey, 1);
            EditorApplication.playModeStateChanged -= OnPlayModeChangedForAutoRebuild;
            Debug.Log("[Silent Night] Auto-rebuilding scenes (compact Hub + all 4 3D models + human-scale House interior)…");
            BuildAll();
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("Silent Night/Build Greybox Scenes", priority = 10)]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ProjectBootstrap.EnsureTagsAndLayers();
            if (GraphicsSettings.defaultRenderPipeline == null) ProjectBootstrap.EnsureRenderPipeline();
            else ProjectBootstrap.EnsureRenderPipeline();
            _mats = ProjectBootstrap.EnsureMaterials();
            EnvironmentMaterialFixer.FixAll();
            _profile = ProjectBootstrap.EnsureVolumeProfile();
            var panelSettings = ProjectBootstrap.EnsurePanelSettings();
            ProjectBootstrap.EnsureFolder(ScenesFolder);
            LightingSettings lighting = EnsureLightingSettings();

            try
            {
                EditorUtility.DisplayProgressBar("Silent Night", "Building Boot scene…", 0.1f);
                BuildBoot(panelSettings);
                EditorUtility.DisplayProgressBar("Silent Night", "Building Hub scene…", 0.35f);
                BuildHub(lighting);
                EditorUtility.DisplayProgressBar("Silent Night", "Building Rooftop scene…", 0.5f);
                BuildRooftop(lighting);
                EditorUtility.DisplayProgressBar("Silent Night", "Building House scene + NavMesh…", 0.65f);
                BuildHouse(lighting);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootPath, true),
                new EditorBuildSettingsScene(HubPath, true),
                new EditorBuildSettingsScene(RooftopPath, true),
                new EditorBuildSettingsScene(HousePath, true),
            };
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Single);
            Debug.Log("[Silent Night] Greybox scenes built: Boot, Hub, Rooftop, House (Build Settings updated). Open Boot and press Play.");
        }

        // ------------------------------------------------------------------ Boot

        private static void BuildBoot(UnityEngine.UIElements.PanelSettings panelSettings)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;

            new GameObject("GameFlow").AddComponent<GameFlow>();
            new GameObject("AudioManager").AddComponent<AudioManager>();
            var ui = new GameObject("UIRoot").AddComponent<UIRoot>(); // creates its own EventSystem at runtime
            if (panelSettings != null) SetRef(ui, "panelSettings", panelSettings);

            EditorSceneManager.SaveScene(scene, BootPath);
        }

        // ------------------------------------------------------------------ Hub

        private static void BuildHub(LightingSettings lighting)
        {
            _texCache.Clear();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Lightmapping.lightingSettings = lighting;

            SetupEnvironmentLighting(
                new Color(0.65f, 0.52f, 0.40f), new Color(0.48f, 0.36f, 0.27f), new Color(0.26f, 0.19f, 0.14f),
                true, new Color(0.18f, 0.14f, 0.18f), 0.005f);
            CreateCamera(new Vector3(0f, 2.2f, -4.8f), Quaternion.Euler(12f, 0f, 0f));
            CreateMoonlight(0.45f, Quaternion.Euler(50f, -35f, 0f));
            CreateGlobalVolume();

            Transform env = new GameObject("Environment").transform;

            // Cozy, compact 11m x 11m (-5.5..5.5) Santa's Workshop floor, Nordic rug with gold fringe, snow outside.
            const float half = 5.5f, h = 3.8f, t = 0.36f;
            Floor(env, "Floor", -half, half, -half, half, ProjectBootstrap.MatWood, SurfaceType.Wood);
            BuildNordicRug(env, "WorkshopRug", new Vector3(0f, 0f, 0.2f), new Vector2(4.8f, 4.8f));
            Box(env, "SnowGround", new Vector3(0f, -0.25f, 0f), new Vector3(80f, 0.3f, 80f), ProjectBootstrap.MatSnow);

            // Warm brick & timber walls (3.8 m high) + full wainscoting, chair rail, baseboard, and crown molding.
            WallAlongX(env, "WallN", half, -(half + 0.18f), half + 0.18f, h, t, ProjectBootstrap.MatBrick);
            WallAlongX(env, "WallS", -half, -(half + 0.18f), half + 0.18f, h, t, ProjectBootstrap.MatBrick);
            WallAlongZ(env, "WallW", -half, -half, half, h, t, ProjectBootstrap.MatBrick);
            WallAlongZ(env, "WallE", half, -half, half, h, t, ProjectBootstrap.MatBrick);
            Box(env, "Ceiling", new Vector3(0f, h + 0.05f, 0f), new Vector3(half * 2f + 0.5f, 0.1f, half * 2f + 0.5f), ProjectBootstrap.MatDarkWood);
            for (int i = 0; i < 5; i++)
            {
                float bx = -4.4f + i * 2.2f;
                Box(env, "RoofBeam", new Vector3(bx, h - 0.12f, 0f), new Vector3(0.24f, 0.24f, half * 2f + 0.1f), ProjectBootstrap.MatDarkWood, false);
            }
            // Timber corner pillars
            foreach (float px in new[] { -5.22f, 5.22f })
                foreach (float pz in new[] { -5.22f, 5.22f })
                    Box(env, "CornerTimber", new Vector3(px, h * 0.5f, pz), new Vector3(0.24f, h, 0.24f), ProjectBootstrap.MatDarkWood, false);

            // Architectural trim: wainscoting (1.15m), brass/oak chair rail, baseboard, cream crown molding
            BuildWallTrimX(env, "TrimN", 5.29f, -5.2f, 5.2f, h, -1f);
            BuildWallTrimX(env, "TrimS", -5.29f, -5.2f, 5.2f, h, 1f);
            BuildWallTrimZ(env, "TrimW", -5.29f, -5.2f, 5.2f, h, 1f);
            BuildWallTrimZ(env, "TrimE", 5.29f, -5.2f, 5.2f, h, -1f);

            // Glowing moonlit workshop windows with snow sills, cross muntins, and velvet curtains
            BuildWindow(env, "WindowS_W", new Vector3(-3.0f, 2.15f, -5.28f), FaceN, 1.85f, 1.45f, ProjectBootstrap.MatVelvetGreen);
            BuildWindow(env, "WindowS_E", new Vector3(3.0f, 2.15f, -5.28f), FaceN, 1.85f, 1.45f, ProjectBootstrap.MatVelvetGreen);
            BuildWindow(env, "WindowW_N", new Vector3(-5.28f, 2.15f, 2.2f), FaceE, 1.75f, 1.40f, ProjectBootstrap.MatVelvetRed);
            BuildWindow(env, "WindowE_N", new Vector3(5.28f, 2.15f, 2.2f), FaceW, 1.75f, 1.40f, ProjectBootstrap.MatVelvetRed);

            // Framed winter storybook paintings
            BuildFramedPainting(env, "Painting_Workshop1", new Vector3(0f, 2.25f, -5.27f), FaceN, 1.45f, 1.0f, ProjectBootstrap.MatPainting2);

            // Brass wall sconces
            BuildBrassSconce(env, new Vector3(-1.55f, 2.25f, 5.24f), FaceS);
            BuildBrassSconce(env, new Vector3(1.95f, 2.25f, 5.24f), FaceS);
            BuildBrassSconce(env, new Vector3(-5.24f, 2.25f, 0.0f), FaceE);
            BuildBrassSconce(env, new Vector3(5.24f, 2.25f, -0.5f), FaceW);

            // Toy shelves along West and East walls
            if (PackExists(KF + "bookcaseOpen.fbx"))
            {
                for (int s = 0; s < 2; s++)
                {
                    float sz = -2.8f + s * 1.6f;
                    GameObject sw = PackFit(env, KF + "bookcaseOpen.fbx", new Vector3(-4.95f, 0f, sz), FaceE, new Vector3(0.5f, 2.1f, 1.35f), ColliderMode.Box);
                    SnapTo(sw, Vector3.left, -5.24f);
                    FillShelves(env, sw, PresentModels, 0.28f, 3, 10 + s, true);

                    GameObject se = PackFit(env, KF + "bookcaseOpen.fbx", new Vector3(4.95f, 0f, sz), FaceW, new Vector3(0.5f, 2.1f, 1.35f), ColliderMode.Box);
                    SnapTo(se, Vector3.right, 5.24f);
                    FillShelves(env, se, PresentModels, 0.28f, 3, 30 + s, true);
                }
            }
            else
            {
                Box(env, "ShelfW0", new Vector3(-4.95f, 1.1f, -2.0f), new Vector3(0.5f, 2.2f, 2.8f), ProjectBootstrap.MatWood);
                Box(env, "ShelfE0", new Vector3(4.95f, 1.1f, -2.0f), new Vector3(0.5f, 2.2f, 2.8f), ProjectBootstrap.MatWood);
            }

            if (PackExists(KF + "cardboardBoxClosed.fbx"))
            {
                GameObject c1 = PackFit(env, KF + "cardboardBoxClosed.fbx", new Vector3(-4.3f, 0f, -4.4f), 10f, new Vector3(0.80f, 0.70f, 0.80f), ColliderMode.Box);
                PackFit(env, KF + "cardboardBoxOpen.fbx", new Vector3(-3.5f, 0f, -4.5f), 25f, new Vector3(0.70f, 0.60f, 0.70f), ColliderMode.Box);
                PackFit(env, KH + "present-a-cube.fbx", new Vector3(-4.25f, TopY(c1), -4.4f), -15f, new Vector3(0.50f, 0.45f, 0.50f), ColliderMode.Box);
                PackFit(env, KF + "cardboardBoxClosed.fbx", new Vector3(4.3f, 0f, -4.4f), -20f, new Vector3(0.80f, 0.70f, 0.80f), ColliderMode.Box);
                PackFit(env, KH + "present-b-rectangle.fbx", new Vector3(3.5f, 0f, -4.5f), 18f, new Vector3(0.60f, 0.42f, 0.60f), ColliderMode.Box);
            }

            // 1) Custom 3D ChristmasTree.fbx + 3D Ornaments (Ornament_A/B/C.fbx) in the NE corner
            BuildTree(env, new Vector3(3.85f, 0f, 3.85f), 0.95f);
            PresentsAround(env, new Vector3(3.85f, 0f, 3.85f), 0.85f, 1.45f, 8, 101, 0.22f, 0.38f, 4.9f);

            // 2) Custom 3D Fireplace.fbx against the North-West wall
            BuildFireplaceNorth(env, new Vector3(-3.35f, 0f, 5.30f));
            PackFit(env, KH + "wreath-decorated.fbx", new Vector3(-3.35f, 2.35f, 4.78f), FaceS + HolidayYaw, new Vector3(0.85f, 0.85f, 0.28f), ColliderMode.None);
            PackFit(env, KH + "nutcracker.fbx", new Vector3(-1.85f, 0f, 4.75f), FaceS + HolidayYaw, new Vector3(0.55f, 1.15f, 0.55f), ColliderMode.Box);
            PackFit(env, KH + "nutcracker.fbx", new Vector3(1.85f, 0f, 4.75f), FaceS + HolidayYaw, new Vector3(0.55f, 1.15f, 0.55f), ColliderMode.Box);
            PackFit(env, KH + "snowman-hat.fbx", new Vector3(4.55f, 0f, 2.35f), FaceW + HolidayYaw, new Vector3(0.68f, 0.92f, 0.68f), ColliderMode.Box);

            LightStrand(env, new Vector3(-5.0f, 0f, 5.20f), new Vector3(5.0f, 0f, 5.20f), 3.45f);
            LightStrand(env, new Vector3(-5.20f, 0f, -4.9f), new Vector3(-5.20f, 0f, 4.9f), 3.45f);
            LightStrand(env, new Vector3(5.20f, 0f, -4.9f), new Vector3(5.20f, 0f, 4.9f), 3.45f);

            // Sculpted Brass Workshop Chandeliers
            float[] lx = { -2.0f, 2.0f }, lz = { -1.6f, 1.8f };
            foreach (float x in lx)
                foreach (float z in lz)
                    BuildBrassChandelier(env, new Vector3(x, 2.95f, z), h);

            // Conveyor: Conveyor builds its own belt at runtime. Belt runs along -Z towards the sleigh.
            var conveyor = new GameObject("Conveyor");
            conveyor.transform.SetPositionAndRotation(new Vector3(-3.35f, 0f, 0.6f), Quaternion.Euler(0f, 180f, 0f));
            conveyor.AddComponent<Conveyor>();
            Box(env, "ChuteHood", new Vector3(-3.35f, 2.45f, 3.6f), new Vector3(1.75f, 0.26f, 1.35f), ProjectBootstrap.MatBrass, false);
            Box(env, "ChuteTrim", new Vector3(-3.35f, 2.30f, 3.6f), new Vector3(1.85f, 0.08f, 1.45f), ProjectBootstrap.MatRed, false);
            Box(env, "ChutePostL", new Vector3(-4.25f, 1.20f, 4.0f), new Vector3(0.11f, 2.4f, 0.11f), ProjectBootstrap.MatMetal);
            Box(env, "ChutePostR", new Vector3(-2.45f, 1.20f, 4.0f), new Vector3(0.11f, 2.4f, 0.11f), ProjectBootstrap.MatMetal);

            // 3) Custom 3D OrderBoard.fbx (converted from 77580a7346fea1e2a5b1cc1c0b68d3e7.glb) centered on the North wall!
            var board = new GameObject("OrderBoard");
            board.transform.position = new Vector3(0f, 0f, 4.65f);
            board.AddComponent<OrderBoardStation>();
            GameObject boardModel = Model(board.transform, "Hub/OrderBoard.fbx", "OrderBoardModel", new Vector3(0f, 1.05f, 5.16f), 0f, 1.05f);
            if (boardModel != null)
            {
                SnapTo(boardModel, Vector3.forward, 5.26f);
            }
            else
            {
                Box(board.transform, "Board", new Vector3(0f, 1.85f, 5.18f), new Vector3(2.8f, 1.65f, 0.12f), ProjectBootstrap.MatDarkWood);
                Box(board.transform, "BoardFelt", new Vector3(0f, 1.85f, 5.11f), new Vector3(2.55f, 1.4f, 0.03f), ProjectBootstrap.MatVelvetGreen, false);
            }
            MakeStaticChildren(board);

            // 4) Custom 3D Workbench.fbx (upgrades) on the East side.
            var bench = new GameObject("Workbench");
            bench.transform.position = new Vector3(3.45f, 0f, 0.65f);
            bench.AddComponent<WorkbenchStation>();
            GameObject benchModel = Model(bench.transform, "Hub/Workbench.fbx", "WorkbenchModel", new Vector3(3.45f, 0f, 0.65f), -90f, 1.35f);
            if (benchModel != null)
            {
                Bounds bb = BoundsOf(benchModel);
                var col = bench.AddComponent<BoxCollider>();
                col.center = bench.transform.InverseTransformPoint(bb.center);
                col.size = bb.size;
            }
            else
            {
                Box(bench.transform, "BenchTop", new Vector3(3.45f, 0.9f, 0.65f), new Vector3(1.2f, 0.1f, 2.2f), ProjectBootstrap.MatWood);
            }
            MakeStaticChildren(bench);

            // Sculpted Santa's Sleigh with golden runners, trim, lanterns & launch pad.
            var sleigh = new GameObject("Sleigh");
            sleigh.transform.position = new Vector3(0f, 0f, -3.1f);
            sleigh.AddComponent<SleighLoadZone>();
            BuildDetailedSleigh(sleigh.transform, new Vector3(0f, 0f, -3.1f), 0.92f);
            Disc(env, "LaunchPadOuter", new Vector3(0f, 0.010f, -3.1f), 4.0f, 0.02f, ProjectBootstrap.MatBrass, false);
            Disc(env, "LaunchPadInner", new Vector3(0f, 0.016f, -3.1f), 3.6f, 0.02f, ProjectBootstrap.MatVelvetRed, false);
            MakeStaticChildren(sleigh);

            // Warm shadow-casting key lights
            Light convL = Point(env, "ConveyorLight", new Vector3(-3.1f, 2.9f, 1.2f), new Color(1f, 0.78f, 0.48f), 7f, 12f);
            convL.shadows = LightShadows.Soft;
            Light benchL = Point(env, "WorkbenchLight", new Vector3(3.1f, 2.9f, 0.8f), new Color(0.95f, 0.88f, 0.78f), 7f, 12f);
            benchL.shadows = LightShadows.Soft;
            Light boardL = Point(env, "OrderBoardLight", new Vector3(0f, 2.75f, 4.1f), new Color(1f, 0.86f, 0.62f), 6.5f, 10f);
            boardL.shadows = LightShadows.Soft;
            Light sleighL = Point(env, "SleighLight", new Vector3(0f, 3.0f, -2.2f), new Color(1f, 0.82f, 0.55f), 8f, 13f);
            sleighL.shadows = LightShadows.Soft;
            Point(env, "FireplaceGlow", new Vector3(-3.35f, 0.75f, 4.4f), new Color(1f, 0.55f, 0.22f), 5.5f, 9f);

            // Player spawns in the cozy center with clear path North (+Z) for smoke tests.
            CreateSpawnAndPlayer(new Vector3(0f, 0f, 0.2f), Quaternion.identity);

            MakeStatic(env.gameObject);
            SaveAndBake(scene, HubPath);
        }


        // ------------------------------------------------------------------ Rooftop

        /// <summary>City rooftops: a grid of snowy roofs separated by small gaps, ice patches, one chimney per roof.
        /// The right chimney (order address) is picked at runtime by RooftopLevelController.</summary>
        private static void BuildRooftop(LightingSettings lighting)
        {
            _texCache.Clear();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Lightmapping.lightingSettings = lighting;

            SetupEnvironmentLighting(
                new Color(0.28f, 0.38f, 0.58f), new Color(0.20f, 0.27f, 0.42f), new Color(0.12f, 0.16f, 0.24f),
                true, new Color(0.08f, 0.12f, 0.24f), 0.006f);
            CreateCamera(new Vector3(0f, 3f, -6f), Quaternion.Euler(15f, 0f, 0f));
            CreateMoonlight(1.1f, Quaternion.Euler(48f, -32f, 0f));
            CreateGlobalVolume();

            Transform env = new GameObject("Environment").transform;
            Box(env, "StreetSnow", new Vector3(20f, -8.3f, 5f), new Vector3(160f, 0.6f, 120f), ProjectBootstrap.MatSnow);

            // Glowing Full Moon & Starry Winter Sky backdrop
            Ball(env, "FullMoon", new Vector3(52f, 44f, 68f), Vector3.one * 9.5f, ProjectBootstrap.MatEmissiveGold);
            Ball(env, "MoonHalo", new Vector3(52f, 44f, 70f), Vector3.one * 15f, ProjectBootstrap.MatEmissiveMoon);
            var starRng = new System.Random(42);
            for (int s = 0; s < 75; s++)
            {
                float sa = (float)(starRng.NextDouble() * Mathf.PI * 2f);
                float sd = 65f + (float)starRng.NextDouble() * 45f;
                float sy = 16f + (float)starRng.NextDouble() * 42f;
                float sr = 0.22f + (float)starRng.NextDouble() * 0.35f;
                Ball(env, "Star", new Vector3(18f + Mathf.Cos(sa) * sd, sy, 8f + Mathf.Sin(sa) * sd),
                    Vector3.one * sr, s % 3 == 0 ? ProjectBootstrap.MatEmissiveGold : ProjectBootstrap.MatEmissiveMoon);
            }

            const int cols = 5, rows = 3;
            const float w = 8f, d = 7f, gap = 1.0f;
            float[,] heights =
            {
                { 0f, 0.4f, 0.1f, 0.5f, 0.2f },
                { 0.3f, 0f, 0.45f, 0.1f, 0.4f },
                { 0.1f, 0.4f, 0f, 0.3f, 0f },
            };
            var rng = new System.Random(7);
            var chimneys = new List<Object>();
            var chimPos = new Vector2[rows, cols]; // chimney x/z per roof: traversal toys keep clear of it
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    float x = c * (w + gap), z = r * (d + gap), y = heights[r, c];
                    string tag = r + "_" + c;
                    // Building body + snowy roof deck (deck top = y).
                    Box(env, "Body_" + tag, new Vector3(x, y - 4.1f, z), new Vector3(w, 8f, d), ProjectBootstrap.MatBrick);
                    // Architectural cornice & snow overhang around roof eaves
                    Box(env, "Cornice_" + tag, new Vector3(x, y - 0.24f, z), new Vector3(w + 0.24f, 0.14f, d + 0.24f), ProjectBootstrap.MatCreamTrim, false);
                    // Glowing cottage windows on building facades (2 stories)
                    for (int wy = 0; wy < 2; wy++)
                    {
                        float winY = y - 1.55f - wy * 2.2f;
                        for (int wx = -1; wx <= 1; wx += 2)
                        {
                            float winX = x + wx * 2.1f;
                            string winMat = ((r + c + wy + wx) % 3 == 0) ? ProjectBootstrap.MatEmissiveGold : ProjectBootstrap.MatEmissiveWarm;
                            // South & North facade windows
                            Box(env, "WinFrameS", new Vector3(winX, winY, z - d * 0.5f - 0.02f), new Vector3(1.05f, 1.25f, 0.06f), ProjectBootstrap.MatCreamTrim, false);
                            Box(env, "WinGlassS", new Vector3(winX, winY, z - d * 0.5f - 0.04f), new Vector3(0.85f, 1.05f, 0.04f), winMat, false);
                            Box(env, "WinSillS", new Vector3(winX, winY - 0.64f, z - d * 0.5f - 0.08f), new Vector3(1.15f, 0.10f, 0.16f), ProjectBootstrap.MatSnow, false);

                            Box(env, "WinFrameN", new Vector3(winX, winY, z + d * 0.5f + 0.02f), new Vector3(1.05f, 1.25f, 0.06f), ProjectBootstrap.MatCreamTrim, false);
                            Box(env, "WinGlassN", new Vector3(winX, winY, z + d * 0.5f + 0.04f), new Vector3(0.85f, 1.05f, 0.04f), winMat, false);
                        }
                    }

                    GameObject deck = Box(env, "Deck_" + tag, new Vector3(x, y - 0.1f, z), new Vector3(w, 0.2f, d), ProjectBootstrap.MatSnow);
                    deck.AddComponent<SurfaceTag>().Surface = SurfaceType.Default;
                    // Low parapet on the street side + snow cap.
                    Box(env, "Parapet_" + tag, new Vector3(x, y + 0.15f, z - d * 0.5f + 0.1f), new Vector3(w, 0.3f, 0.2f), ProjectBootstrap.MatDarkWood, true);
                    Box(env, "ParapetSnow_" + tag, new Vector3(x, y + 0.32f, z - d * 0.5f + 0.1f), new Vector3(w + 0.06f, 0.06f, 0.26f), ProjectBootstrap.MatSnow, false);

                    // Ice patch on every third roof.
                    if ((r * cols + c) % 3 == 1)
                    {
                        GameObject ice = Box(env, "Ice_" + tag, new Vector3(x + (float)(rng.NextDouble() - 0.5) * 2f, y + 0.02f, z + (float)(rng.NextDouble() - 0.5) * 2f),
                            new Vector3(3.5f, 0.04f, 2.8f), ProjectBootstrap.MatIce);
                        ice.AddComponent<SurfaceTag>().Surface = SurfaceType.Ice;
                    }

                    // Fairy lights along the street edge.
                    for (int k = 0; k < 6; k++)
                        Ball(env, "Fairy", new Vector3(x - w * 0.5f + 0.8f + k * 1.3f, y + 0.45f, z - d * 0.5f + 0.15f), Vector3.one * 0.13f,
                            k % 2 == 0 ? ProjectBootstrap.MatEmissiveWarm : ProjectBootstrap.MatEmissiveGold);

                    // Antenna / decor.
                    if ((r + c) % 2 == 0)
                    {
                        Box(env, "AntennaPole", new Vector3(x + w * 0.35f, y + 0.9f, z + d * 0.3f), new Vector3(0.06f, 1.8f, 0.06f), ProjectBootstrap.MatMetal, false);
                        Box(env, "AntennaBar", new Vector3(x + w * 0.35f, y + 1.5f, z + d * 0.3f), new Vector3(0.8f, 0.05f, 0.05f), ProjectBootstrap.MatMetal, false);
                    }

                    // Code-customized 3D Dormer Windows & Glowing Skylights per roof
                    float dormerScale = 0.88f + ((r * 3 + c * 5) % 4) * 0.07f;
                    float dormerX = x + ((r + c) % 2 == 0 ? -1.85f : 1.85f);
                    float dormerZ = z + d * 0.5f - 0.95f;
                    GameObject dormer1 = Model(env, "Rooftop/DormerWindow.fbx", "Dormer_" + tag, new Vector3(dormerX, y, dormerZ), 180f, dormerScale);
                    if (dormer1 != null)
                    {
                        var dbc = dormer1.AddComponent<BoxCollider>();
                        dbc.center = new Vector3(0f, 0.8f, 0f);
                        dbc.size = new Vector3(1.8f, 1.6f, 1.6f);
                        Point(env, "DormerGlow_" + tag, new Vector3(dormerX, y + 0.75f, dormerZ - 0.85f), new Color(1f, 0.78f, 0.42f), 2.2f, 5.5f);
                    }
                    // Second smaller dormer on larger/alternate roofs
                    if ((r + c) % 3 == 0)
                    {
                        float d2X = x - (dormerX - x) * 0.85f;
                        GameObject dormer2 = Model(env, "Rooftop/DormerWindow.fbx", "DormerS_" + tag, new Vector3(d2X, y, z - d * 0.5f + 0.95f), 0f, dormerScale * 0.86f);
                        if (dormer2 != null)
                        {
                            var dbc2 = dormer2.AddComponent<BoxCollider>();
                            dbc2.center = new Vector3(0f, 0.8f, 0f);
                            dbc2.size = new Vector3(1.8f, 1.6f, 1.6f);
                        }
                    }
                    // Glowing skylight window embedded in roof deck (like reference image)
                    if ((r * cols + c) % 2 == 0)
                    {
                        float skyX = x + (dormerX > x ? -1.75f : 1.75f);
                        float skyZ = z - 0.9f;
                        BuildRooftopSkylight(env, "Skylight_" + tag, new Vector3(skyX, y + 0.02f, skyZ),
                            ((r + c) % 4 == 0) ? ProjectBootstrap.MatEmissiveGold : ProjectBootstrap.MatEmissiveWarm);
                    }

                    // Tall 3D Brick Chimney with snow cap, icicles, bolted brass plaque ("CHIMNEY Nº. X"), and Co-op Boost / Solo Ladder.
                    float cx = x + (float)(rng.NextDouble() - 0.5) * (w - 3.4f);
                    float cz = z + (float)(rng.NextDouble() - 0.5) * (d - 3.4f) + 0.35f;
                    chimPos[r, c] = new Vector2(cx, cz);

                    var ch = new GameObject("Chimney_" + tag);
                    ch.transform.SetParent(env, false);
                    ch.transform.position = new Vector3(cx, y + 0.9f, cz);
                    var chCol = ch.AddComponent<BoxCollider>();
                    chCol.size = new Vector3(0.92f, 1.85f, 0.92f);

                    float chimYaw = ((r * 5 + c * 3) % 4) * 90f;
                    float chimScaleXZ = 1.22f + ((r + c) % 3) * 0.06f;
                    GameObject chimMesh = Model(ch.transform, "Rooftop/Chimney.fbx", "ChimneyMesh_" + tag, new Vector3(cx, y, cz), chimYaw, 1.0f);
                    if (chimMesh != null)
                    {
                        chimMesh.transform.localScale = new Vector3(chimScaleXZ, 0.96f, chimScaleXZ);
                    }
                    else
                    {
                        Box(ch.transform, "ChimneyBrick_" + tag, new Vector3(cx, y + 0.9f, cz), new Vector3(0.9f, 1.8f, 0.9f), ProjectBootstrap.MatBrick, false);
                        Box(ch.transform, "ChimneyCollar_" + tag, new Vector3(cx, y + 1.72f, cz), new Vector3(1.06f, 0.12f, 1.06f), ProjectBootstrap.MatCreamTrim, false);
                        Box(ch.transform, "ChimneyCap_" + tag, new Vector3(cx, y + 1.86f, cz), new Vector3(1.15f, 0.14f, 1.15f), ProjectBootstrap.MatSnow, false);
                    }
                    Box(ch.transform, "ChimneyHole_" + tag, new Vector3(cx, y + 1.94f, cz), new Vector3(0.6f, 0.04f, 0.6f), ProjectBootstrap.MatDark, false);

                    var chim = ch.AddComponent<SilentNight.Rooftop.RooftopChimney>();
                    var smokeGo = new GameObject("ChimneySmoke");
                    smokeGo.transform.SetParent(ch.transform, false);
                    smokeGo.transform.position = new Vector3(cx, y + 2.05f, cz);
                    smokeGo.AddComponent<SilentNight.FX.ChimneySmoke>();

                    chimneys.Add(chim);
                }
            }

            // Traversal toys: snowy ramps between roofs, icy slopes, crate perches and soft snow drifts.
            BuildRooftopRamps(env, heights, rows, cols, w, d, gap);
            BuildRooftopIceSlopes(env, heights, chimPos, w + gap, d + gap);
            BuildRooftopCushions(env, heights, chimPos, w, w + gap, d + gap);

            // Santa's sleigh parked on the first roof.
            var sleigh = new GameObject("ParkedSleigh").transform;
            sleigh.SetParent(env, false);
            BuildDetailedSleigh(sleigh, new Vector3(-1.8f, 0f, 0f), 0.88f);

            // Distant city silhouettes with lit windows.
            for (int i = 0; i < 60; i++)
            {
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float dist = 55f + (float)rng.NextDouble() * 40f;
                Vector3 p = new Vector3(20f + Mathf.Cos(ang) * dist, 0f, 8f + Mathf.Sin(ang) * dist);
                float bh = 10f + (float)rng.NextDouble() * 25f;
                Box(env, "Tower", new Vector3(p.x, bh * 0.5f - 8f, p.z), new Vector3(5f + (float)rng.NextDouble() * 5f, bh, 5f + (float)rng.NextDouble() * 5f), ProjectBootstrap.MatDark, false);
                for (int k = 0; k < 4; k++)
                    Ball(env, "Window", new Vector3(p.x + (float)(rng.NextDouble() - 0.5) * 3f, bh * 0.2f + k * 3f - 6f, p.z - 2.6f), Vector3.one * 0.5f, ProjectBootstrap.MatEmissiveWarm);
            }

            // Falling snow.
            CreateSnowParticles(env, new Vector3(18f, 12f, 8f));

            // Warm lights across the rooftops so the snow & brick sparkle against the cool moonlight.
            Point(env, "SleighGlow", new Vector3(-1.8f, 2.5f, 0f), new Color(1f, 0.78f, 0.45f), 4.5f, 10f);
            Point(env, "RooftopWarmCenter", new Vector3(18f, 3.5f, 8f), new Color(1f, 0.76f, 0.42f), 4.0f, 18f);
            Point(env, "RooftopWarmEast", new Vector3(34f, 3.5f, 8f), new Color(1f, 0.76f, 0.42f), 4.0f, 18f);

            // Respawn volume below the roofs.
            var kill = new GameObject("FallRespawn");
            kill.transform.position = new Vector3(18f, -4f, 8f);
            var kc = kill.AddComponent<BoxCollider>();
            kc.isTrigger = true;
            kc.size = new Vector3(200f, 2f, 160f);
            kill.AddComponent<SilentNight.Player.FallRespawn>();

            // Level controller (picks the target chimney at runtime).
            var ctrl = new GameObject("RooftopLevel").AddComponent<SilentNight.Rooftop.RooftopLevelController>();
            SetArray(ctrl, "chimneys", chimneys);

            CreateSpawnAndPlayer(new Vector3(0.3f, 0.05f, -1.6f), Quaternion.Euler(0f, 60f, 0f));

            MakeStatic(env.gameObject);
            SaveAndBake(scene, RooftopPath);
        }

        /// <summary>Tilted slab (box with collider) whose top face centre line runs from <paramref name="from"/> to
        /// <paramref name="to"/>. Its +Z points from -> to (uphill when from is the low end).</summary>
        private static GameObject Slab(Transform parent, string name, Vector3 from, Vector3 to, float width, float thick, string mat)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            Quaternion rot = Quaternion.LookRotation(dir / len, Vector3.up);
            Vector3 center = (from + to) * 0.5f - rot * Vector3.up * (thick * 0.5f);
            GameObject go = Box(parent, name, center, new Vector3(width, thick, len), mat);
            go.transform.rotation = rot;
            return go;
        }

        private static void BuildRooftopSkylight(Transform parent, string name, Vector3 pos, string glassMat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = pos;
            // Wooden outer frame
            Box(root, "Frame", pos + new Vector3(0f, 0.04f, 0f), new Vector3(1.45f, 0.08f, 1.15f), ProjectBootstrap.MatDarkWood, false);
            // Warm glowing panes
            Box(root, "Glass", pos + new Vector3(0f, 0.06f, 0f), new Vector3(1.28f, 0.05f, 0.98f), glassMat, false);
            // Mullion bars across the glass
            Box(root, "Bar1", pos + new Vector3(-0.32f, 0.08f, 0f), new Vector3(0.05f, 0.04f, 1.02f), ProjectBootstrap.MatDarkWood, false);
            Box(root, "Bar2", pos + new Vector3(0.32f, 0.08f, 0f), new Vector3(0.05f, 0.04f, 1.02f), ProjectBootstrap.MatDarkWood, false);
            // Snow drift along the edge
            Ball(root, "SnowRim", pos + new Vector3(0f, 0.07f, 0.56f), new Vector3(1.48f, 0.14f, 0.24f), ProjectBootstrap.MatSnow);
        }

        /// <summary>Crooked wooden plank gangways across rooftop gaps (with timber beams, crooked planks & snow patches).</summary>
        private static void BuildRooftopRamps(Transform env, float[,] heights, int rows, int cols, float w, float d, float gap)
        {
            float pitchX = w + gap, pitchZ = d + gap;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    if ((r + c) % 2 != 0) continue;
                    float yA = heights[r, c], yB = heights[r, c + 1];
                    float edgeA = c * pitchX + w * 0.5f, edgeB = edgeA + gap;
                    float z = r * pitchZ + (r % 2 == 0 ? -1.1f : 1.1f);
                    Vector3 low = new Vector3(edgeA - 0.45f, yA + 0.02f, z);
                    Vector3 high = new Vector3(edgeB + 0.45f, yB + 0.02f, z);
                    BuildWoodenPlankBridge(env, $"PlankBridge_{r}_{c}", low, high, 1.25f);
                }
            }
        }

        private static void BuildWoodenPlankBridge(Transform parent, string name, Vector3 from, Vector3 to, float width)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 0.1f) return;
            Quaternion rot = Quaternion.LookRotation(dir / len, Vector3.up);
            Vector3 mid = (from + to) * 0.5f;

            var bridge = new GameObject(name).transform;
            bridge.SetParent(parent, false);
            bridge.SetPositionAndRotation(mid, rot);

            // Walkable collider slab
            var colGo = new GameObject("BridgeCollider");
            colGo.transform.SetParent(bridge, false);
            var bc = colGo.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -0.04f, 0f);
            bc.size = new Vector3(width, 0.14f, len);
            colGo.AddComponent<SurfaceTag>().Surface = SurfaceType.Wood;

            // Use custom 3D PlankBridge model if present (native length along X is ~2.19m, width along Y/Z is ~1.18m)
            GameObject bridgeMesh = Model(bridge, "Rooftop/PlankBridge.fbx", "PlankBridgeMesh", mid + rot * new Vector3(0f, -0.14f, 0f), 0f, 1.0f);
            if (bridgeMesh != null)
            {
                bridgeMesh.transform.rotation = rot * Quaternion.Euler(0f, 90f, 0f);
                bridgeMesh.transform.localScale = new Vector3(len / 2.18f, 1.0f, width / 1.18f);
                return;
            }

            // Two longitudinal timber support beams underneath
            float beamOffset = width * 0.36f;
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject beam = Box(bridge, "Beam", mid + rot * new Vector3(s * beamOffset, -0.09f, 0f),
                    new Vector3(0.11f, 0.11f, len * 0.98f), ProjectBootstrap.MatDarkWood, false);
                beam.transform.rotation = rot;
            }

            // 8 crooked wooden planks + snow patches on top
            int plankCount = 8;
            float step = len / plankCount;
            for (int i = 0; i < plankCount; i++)
            {
                float lz = -len * 0.5f + (i + 0.5f) * step;
                float tiltYaw = ((i * 37) % 7 - 3) * 1.4f;
                float pw = width * (0.95f + ((i * 13) % 5) * 0.02f);
                string mat = (i % 2 == 0) ? ProjectBootstrap.MatWood : ProjectBootstrap.MatDarkWood;

                GameObject plank = Box(bridge, "Plank_" + i, mid + rot * new Vector3(0f, -0.02f, lz),
                    new Vector3(pw, 0.055f, step * 0.84f), mat, false);
                plank.transform.rotation = rot * Quaternion.Euler(0f, tiltYaw, 0f);

                if (i % 2 == 0)
                {
                    float sx = (i % 4 == 0 ? -1f : 1f) * (width * 0.22f);
                    Ball(bridge, "PlankSnow_" + i, mid + rot * new Vector3(sx, 0.02f, lz),
                        new Vector3(pw * 0.44f, 0.055f, step * 0.68f), ProjectBootstrap.MatSnow);
                }
            }
        }

        /// <summary>Icy slopes on four roofs: a 1.9 m ice slab rising 0.5 m (~14.7°) onto a wooden roof hatch, on the
        /// side of the roof away from the chimney. IceSlope drags a standing elf downhill.</summary>
        private static void BuildRooftopIceSlopes(Transform env, float[,] heights, Vector2[,] chimPos, float pitchX, float pitchZ)
        {
            int[,] roofs = { { 0, 2 }, { 1, 3 }, { 2, 0 }, { 2, 4 } };
            const float rise = 0.5f, run = 1.9f;
            for (int i = 0; i < roofs.GetLength(0); i++)
            {
                int r = roofs[i, 0], c = roofs[i, 1];
                string tag = r + "_" + c;
                float x = c * pitchX, y = heights[r, c], z = r * pitchZ;
                float side = chimPos[r, c].x >= x ? -1f : 1f;
                float hx = x + side * 2.2f;

                // Roof hatch the slope leads onto (z + 0.3 .. z + 1.7).
                Box(env, "IceHatch_" + tag, new Vector3(hx, y + rise * 0.5f, z + 1.0f), new Vector3(1.4f, rise, 1.4f), ProjectBootstrap.MatDarkWood);
                Box(env, "IceHatchSnow_" + tag, new Vector3(hx, y + rise - 0.03f, z + 1.0f), new Vector3(1.5f, 0.06f, 1.5f), ProjectBootstrap.MatSnow, false);

                Vector3 low = new Vector3(hx, y, z + 0.3f - run);
                Vector3 high = new Vector3(hx, y + rise, z + 0.3f);
                GameObject slab = Slab(env, "IceSlope_" + tag, low, high, 1.2f, 0.12f, ProjectBootstrap.MatIce);
                slab.AddComponent<SurfaceTag>().Surface = SurfaceType.Ice;

                var zoneGo = new GameObject("IceSlopeZone_" + tag);
                zoneGo.transform.SetParent(env, false);
                zoneGo.transform.SetPositionAndRotation((low + high) * 0.5f, slab.transform.rotation);
                var bc = zoneGo.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.center = new Vector3(0f, 0.3f, 0f);
                bc.size = new Vector3(1.2f, 0.6f, (high - low).magnitude - 0.1f);
                zoneGo.AddComponent<SurfaceTag>().Surface = SurfaceType.Ice;
                zoneGo.AddComponent<SilentNight.Rooftop.IceSlope>();
            }
        }

        /// <summary>Soft snow drifts: two under crate perches (steps 0.5/1.0/1.5 m up to a 2 m water-tank stand — jumping
        /// off it onto bare roof is a ragdoll fall, onto the drift it is not) and three in jump-gap landing zones.</summary>
        private static void BuildRooftopCushions(Transform env, float[,] heights, Vector2[,] chimPos, float w, float pitchX, float pitchZ)
        {
            int[,] perches = { { 0, 3 }, { 2, 1 } };
            for (int i = 0; i < perches.GetLength(0); i++)
            {
                int r = perches[i, 0], c = perches[i, 1];
                string tag = r + "_" + c;
                float x = c * pitchX, y = heights[r, c], z = r * pitchZ;
                float px = x + (chimPos[r, c].x >= x ? -2f : 2f);

                float[] stepH = { 0.5f, 1.0f, 1.5f };
                for (int k = 0; k < stepH.Length; k++)
                    Box(env, $"PerchCrate_{tag}_{k}", new Vector3(px, y + stepH[k] * 0.5f, z - 2.0f + k * 0.9f), new Vector3(0.9f, stepH[k], 0.9f),
                        k % 2 == 0 ? ProjectBootstrap.MatWood : ProjectBootstrap.MatDarkWood, true, k * 4f);

                // Custom 3D Wooden Water Tank barrel on timber stand (fallback to box if missing)
                GameObject wt = Model(env, "Rooftop/WaterTank.fbx", "WaterTank_" + tag, new Vector3(px, y, z + 0.85f), i * 90f, 0.95f);
                if (wt != null)
                {
                    var wtCol = wt.AddComponent<BoxCollider>();
                    wtCol.center = new Vector3(0f, 1.05f, 0f);
                    wtCol.size = new Vector3(1.45f, 2.1f, 1.45f);
                }
                else
                {
                    Box(env, "PerchStand_" + tag, new Vector3(px, y + 1.0f, z + 0.85f), new Vector3(1.2f, 2.0f, 1.2f), ProjectBootstrap.MatMetal);
                    Box(env, "PerchSnow_" + tag, new Vector3(px, y + 1.97f, z + 0.85f), new Vector3(1.3f, 0.06f, 1.3f), ProjectBootstrap.MatSnow, false);
                }

                SnowCushionAt(env, "SnowDrift_Perch_" + tag, new Vector3(px, y, z + 2.5f), new Vector2(1.6f, 1.8f));
            }

            // Landing zones just past jump gaps (higher roof -> lower roof), at the west edge of the lower roof.
            int[,] landings = { { 0, 4 }, { 1, 1 }, { 2, 2 } };
            for (int i = 0; i < landings.GetLength(0); i++)
            {
                int r = landings[i, 0], c = landings[i, 1];
                float x = c * pitchX, y = heights[r, c], z = r * pitchZ;
                float dz = chimPos[r, c].y > z + 0.4f ? -2.2f : 2.6f; // keep clear of the chimney
                SnowCushionAt(env, $"SnowDrift_Gap_{r}_{c}", new Vector3(x - w * 0.5f + 0.85f, y, z + dz), new Vector2(1.4f, 1.4f));
            }
        }

        /// <summary>Snow pile: walkable 12 cm base (collider), visual mound, and a SnowCushion trigger 0.1–1.8 m above it.</summary>
        private static void SnowCushionAt(Transform parent, string name, Vector3 ground, Vector2 size)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = ground;
            GameObject b = Box(root, "Base", ground + Vector3.up * 0.06f, new Vector3(size.x, 0.12f, size.y), ProjectBootstrap.MatSnow);
            b.AddComponent<SurfaceTag>().Surface = SurfaceType.Default;
            Ball(root, "Mound", ground + Vector3.up * 0.12f, new Vector3(size.x * 0.95f, 0.55f, size.y * 0.95f), ProjectBootstrap.MatSnow);
            Ball(root, "Lump", ground + new Vector3(size.x * 0.25f, 0.18f, -size.y * 0.2f), new Vector3(0.6f, 0.4f, 0.6f), ProjectBootstrap.MatSnow);
            Ball(root, "Lump", ground + new Vector3(-size.x * 0.28f, 0.15f, size.y * 0.22f), new Vector3(0.55f, 0.35f, 0.5f), ProjectBootstrap.MatSnow);

            var zone = new GameObject("CushionZone");
            zone.transform.SetParent(root, false);
            zone.transform.position = ground + Vector3.up * 0.95f;
            var bc = zone.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(size.x, 1.7f, size.y);
            zone.AddComponent<SilentNight.Rooftop.SnowCushion>();
        }

        private static void CreateSnowParticles(Transform parent, Vector3 center)
        {
            var go = new GameObject("Snowfall");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 11f;
            main.startSpeed = 0.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.maxParticles = 2500;
            main.gravityModifier = 0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 220f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(70f, 1f, 50f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f); // all axes must use the same curve mode
            vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            vel.space = ParticleSystemSimulationSpace.World;

            Shader sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh2 == null) sh2 = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh2 != null)
            {
                string matPath = ProjectBootstrap.MaterialsFolder + "/SnowFlake.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (m == null)
                {
                    m = new Material(sh2) { name = "SnowFlake" };
                    AssetDatabase.CreateAsset(m, matPath);
                }
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
            }
        }

        // ------------------------------------------------------------------ House

        private static void BuildHouse(LightingSettings lighting)
        {
            _texCache.Clear();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Lightmapping.lightingSettings = lighting;

            // Cozy defaults (HouseMoodController re-applies cozy/spooky at runtime).
            SetupEnvironmentLighting(
                new Color(0.60f, 0.48f, 0.36f), new Color(0.45f, 0.34f, 0.25f), new Color(0.24f, 0.18f, 0.14f),
                true, new Color(0.22f, 0.16f, 0.14f), 0.006f);
            CreateCamera(new Vector3(6f, 2.5f, 3.5f), Quaternion.Euler(15f, 90f, 0f));
            CreateMoonlight(0.38f, Quaternion.Euler(50f, 30f, 0f));
            CreateGlobalVolume();

            Transform env = new GameObject("Environment").transform;
            Transform moodProps = new GameObject("MoodProps").transform; // toggled by HouseMoodController (not static)
            var warm = new List<Light>();
            var flicker = new List<Light>();
            var cozyOnly = new List<GameObject>();
            var spookyOnly = new List<GameObject>();

            // ---- floors (top at y = 0), one per room with its surface ----
            Floor(env, "Floor_Living", 0f, 12f, 0f, 11f, ProjectBootstrap.MatWood, SurfaceType.Wood);
            BuildNordicRug(env, "LivingRoomRug", new Vector3(5.8f, 0f, 5.8f), new Vector2(8.8f, 8.2f));
            Floor(env, "Floor_Kitchen", 0f, 12f, -11f, 0f, ProjectBootstrap.MatTile, SurfaceType.Tile);
            Floor(env, "Floor_Hallway", -3.5f, 0f, -11f, 11f, ProjectBootstrap.MatWood, SurfaceType.Wood);
            BuildNordicRug(env, "HallRunnerRug", new Vector3(-1.75f, 0f, -6.5f), new Vector2(1.6f, 6.2f));
            Floor(env, "Floor_Bedroom", -12f, -3.5f, 4f, 11f, ProjectBootstrap.MatWood, SurfaceType.Wood);
            Floor(env, "Floor_Study", -12f, -3.5f, 0.5f, 4f, ProjectBootstrap.MatDarkWood, SurfaceType.Wood);
            Floor(env, "Floor_Garage", -12f, -3.5f, -11f, 0.5f, ProjectBootstrap.MatConcrete, SurfaceType.Default);
            Box(env, "SnowGround", new Vector3(0f, -0.25f, 0f), new Vector3(80f, 0.3f, 80f), ProjectBootstrap.MatSnow);

            // ---- walls: 4.2 m high so rooms feel spacious and ceiling beams never block the camera ----
            const float h = 4.2f, ext = 0.3f, inner = 0.2f;
            // Per-room exterior & interior walls with vintage striped wallpapers
            WallAlongX(env, "Ext_N_Bedroom", 11f, -12.15f, -3.5f, h, ext, ProjectBootstrap.MatWallNavy);
            WallAlongX(env, "Ext_N_Hall", 11f, -3.5f, 0f, h, ext, ProjectBootstrap.MatWallOchre);
            WallAlongX(env, "Ext_N_Living", 11f, 0f, 12.15f, h, ext, ProjectBootstrap.MatWallSpruce);

            WallAlongX(env, "Ext_S_Garage", -11f, -12.15f, -3.5f, h, ext, ProjectBootstrap.MatConcrete);
            WallAlongX(env, "Ext_S_Hall", -11f, -3.5f, 0f, h, ext, ProjectBootstrap.MatWallOchre);
            WallAlongX(env, "Ext_S_Kitchen", -11f, 0f, 12.15f, h, ext, ProjectBootstrap.MatWall);

            WallAlongZ(env, "Ext_W_Garage", -12f, -11f, 0.5f, h, ext, ProjectBootstrap.MatConcrete);
            WallAlongZ(env, "Ext_W_Study", -12f, 0.5f, 4f, h, ext, ProjectBootstrap.MatWallBurgundy);
            WallAlongZ(env, "Ext_W_Bedroom", -12f, 4f, 11f, h, ext, ProjectBootstrap.MatWallNavy);

            WallAlongZ(env, "Ext_E_Kitchen", 12f, -11f, 0f, h, ext, ProjectBootstrap.MatWall);
            WallAlongZ(env, "Ext_E_Living", 12f, 0f, 11f, h, ext, ProjectBootstrap.MatWallSpruce);

            WallAlongZ(env, "Wall_Hall_East_S", 0f, -11f, 0f, h, inner, ProjectBootstrap.MatWallOchre, -5f);      // → kitchen
            WallAlongZ(env, "Wall_Hall_East_N", 0f, 0f, 11f, h, inner, ProjectBootstrap.MatWallSpruce, 4f);       // → living
            WallAlongX(env, "Wall_Living_Kitchen", 0f, 0f, 12f, h, inner, ProjectBootstrap.MatWallSpruce, 3f);    // living ↔ kitchen
            WallAlongZ(env, "Wall_Hall_West_S", -3.5f, -11f, 0.5f, h, inner, ProjectBootstrap.MatWallOchre, -5f); // → garage
            WallAlongZ(env, "Wall_Hall_West_M", -3.5f, 0.5f, 4f, h, inner, ProjectBootstrap.MatWallBurgundy, 2.25f); // → study
            WallAlongZ(env, "Wall_Hall_West_N", -3.5f, 4f, 11f, h, inner, ProjectBootstrap.MatWallNavy, 7.5f);    // → bedroom
            WallAlongX(env, "Wall_Bedroom_Study", 4f, -12f, -3.5f, h, inner, ProjectBootstrap.MatWallNavy);

            // Creaky physical doors: push slowly = silent, normal = creak, slam = БАХ! (living room and Grandpa's bedroom).
            SilentNight.Stealth.CreakyDoor.Create(env, new Vector3(0f, 0f, 3.3f), DoorW, DoorH, -90f, Mat(ProjectBootstrap.MatDarkWood));
            SilentNight.Stealth.CreakyDoor.Create(env, new Vector3(-3.5f, 0f, 6.8f), DoorW, DoorH, -90f, Mat(ProjectBootstrap.MatDarkWood));
            WallAlongX(env, "Wall_Study_Garage", 0.5f, -12f, -3.5f, h, inner, ProjectBootstrap.MatWallBurgundy);

            // Architectural trim: wainscoting + chair rail + baseboards + crown molding along exterior walls
            BuildWallTrimX(env, "Trim_LivingN", 10.82f, 0.2f, 11.8f, h, -1f);
            BuildWallTrimZ(env, "Trim_LivingE", 11.82f, 0.2f, 10.8f, h, -1f);
            BuildWallTrimX(env, "Trim_KitchenS", -10.82f, 0.2f, 11.8f, h, 1f);
            BuildWallTrimZ(env, "Trim_KitchenE", 11.82f, -10.8f, -0.2f, h, -1f);
            BuildWallTrimX(env, "Trim_BedroomN", 10.82f, -11.8f, -3.7f, h, -1f);
            BuildWallTrimZ(env, "Trim_BedroomW", -11.82f, 4.2f, 10.8f, h, 1f);
            BuildWallTrimZ(env, "Trim_StudyW", -11.82f, 0.7f, 3.8f, h, 1f);

            // Glowing Moonlit Windows with velvet curtains & snowy sills
            BuildWindow(env, "Win_LivingN1", new Vector3(2.8f, 2.35f, 10.80f), FaceS, 1.85f, 1.55f, ProjectBootstrap.MatVelvetRed);
            BuildWindow(env, "Win_LivingN2", new Vector3(6.2f, 2.35f, 10.80f), FaceS, 1.85f, 1.55f, ProjectBootstrap.MatVelvetRed);
            BuildWindow(env, "Win_LivingE", new Vector3(11.80f, 2.35f, 6.6f), FaceW, 1.85f, 1.55f, ProjectBootstrap.MatVelvetRed);
            BuildWindow(env, "Win_KitchenS", new Vector3(6.2f, 2.35f, -10.80f), FaceN, 1.85f, 1.45f, ProjectBootstrap.MatVelvetGreen);
            BuildWindow(env, "Win_KitchenE", new Vector3(11.80f, 2.35f, -1.5f), FaceW, 1.65f, 1.45f, ProjectBootstrap.MatVelvetGreen);
            BuildWindow(env, "Win_BedroomN", new Vector3(-8.8f, 2.35f, 10.80f), FaceS, 1.85f, 1.55f, ProjectBootstrap.MatVelvetRed);
            BuildWindow(env, "Win_StudyW", new Vector3(-11.80f, 2.35f, 2.3f), FaceE, 1.55f, 1.45f, ProjectBootstrap.MatVelvetRed);

            // Framed Winter Storybook Paintings
            BuildFramedPainting(env, "Art_LivingW", new Vector3(0.13f, 2.35f, 7.6f), FaceE, 1.4f, 1.0f, ProjectBootstrap.MatPainting0);
            BuildFramedPainting(env, "Art_LivingS", new Vector3(7.2f, 2.35f, 0.13f), FaceN, 1.5f, 1.05f, ProjectBootstrap.MatPainting1);
            BuildFramedPainting(env, "Art_HallW1", new Vector3(-3.37f, 2.35f, -1.2f), FaceE, 1.25f, 0.9f, ProjectBootstrap.MatPainting2);
            BuildFramedPainting(env, "Art_HallE1", new Vector3(-0.13f, 2.35f, -8.2f), FaceW, 1.25f, 0.9f, ProjectBootstrap.MatPainting3);
            BuildFramedPainting(env, "Art_BedroomS", new Vector3(-7.5f, 2.35f, 4.13f), FaceN, 1.4f, 1.0f, ProjectBootstrap.MatPainting1);
            BuildFramedPainting(env, "Art_KitchenW", new Vector3(0.13f, 2.35f, -2.2f), FaceE, 1.3f, 0.95f, ProjectBootstrap.MatPainting0);

            // Ceiling with high wooden beams (4.2m).
            Box(env, "Ceiling", new Vector3(0f, h + 0.05f, 0f), new Vector3(24.6f, 0.1f, 22.6f), ProjectBootstrap.MatCreamTrim);
            for (int i = 0; i < 9; i++)
                Box(env, "CeilingBeam", new Vector3(-11f + i * 2.75f, h - 0.10f, 0f), new Vector3(0.24f, 0.20f, 22.4f), ProjectBootstrap.MatDarkWood, false);

            // ---- Living room (x 0..12, z 0..11): tree, fireplace (exit), gifts arrive here ----
            BuildTree(env, new Vector3(5.5f, 0f, 6.5f), 1.32f);
            PresentsAround(env, new Vector3(5.5f, 0f, 6.5f), 1.05f, 1.85f, 10, 202, 0.26f, 0.48f, 10.5f);
            TrainLoop(env, new Vector3(5.5f, 0.015f, 3.4f), 0.48f);
            var treeZoneGo = new GameObject("TreeDropZone");
            treeZoneGo.transform.position = new Vector3(5.5f, 0f, 6.5f);
            var placement = new GameObject("PlacementCenter").transform;
            placement.SetParent(treeZoneGo.transform, false);
            var treeZone = treeZoneGo.AddComponent<TreeDropZone>();

            var spawnerGo = new GameObject("HouseGiftSpawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(8.4f, 0f, 1.3f), Quaternion.identity);
            var spawner = spawnerGo.AddComponent<HouseGiftSpawner>();

            SetRef(treeZone, "placementCenter", placement);
            SetRef(treeZone, "spawner", spawner);

            BuildFireplaceEast(env, moodProps, cozyOnly, new Vector3(11.85f, 0f, 3.5f));
            PackFit(env, KH + "wreath-decorated.fbx", new Vector3(11.15f, 2.35f, 3.5f), FaceW + HolidayYaw, new Vector3(0.35f, 1.15f, 1.15f), ColliderMode.None);
            PackFit(env, KH + "nutcracker.fbx", new Vector3(10.85f, 0f, 1.25f), FaceW + HolidayYaw, new Vector3(0.72f, 1.45f, 0.72f), ColliderMode.Box);
            var chimney = new GameObject("ChimneyExit");
            chimney.transform.position = new Vector3(9.7f, 0f, 3.5f);
            chimney.AddComponent<ChimneyExit>();

            // Human-scale Living Room Furniture (~1.5x larger than miniature elf).
            if (PackFit(env, KF + "loungeSofa.fbx", new Vector3(8.3f, 0f, 9.65f), FaceS, new Vector3(3.55f, 1.38f, 1.42f), ColliderMode.Box) == null)
            {
                Box(env, "Sofa", new Vector3(8.3f, 0.38f, 9.65f), new Vector3(3.5f, 0.75f, 1.35f), ProjectBootstrap.MatFabric);
                Box(env, "SofaBack", new Vector3(8.3f, 0.98f, 10.35f), new Vector3(3.5f, 1.35f, 0.28f), ProjectBootstrap.MatFabric);
            }
            // Giant Coffee Table on legs: an elf can walk/crouch right under it (TopSlab keeps crawlspace clear).
            GameObject coffeeTbl = PackFit(env, KF + "tableCoffee.fbx", new Vector3(8.5f, 0f, 8.15f), FaceS, new Vector3(1.85f, 0.78f, 0.95f), ColliderMode.TopSlab);
            if (coffeeTbl == null)
            {
                Box(env, "CoffeeTable", new Vector3(8.5f, 0.74f, 8.15f), new Vector3(1.85f, 0.08f, 0.95f), ProjectBootstrap.MatDarkWood);
                foreach (float dx in new[] { -0.82f, 0.82f })
                    foreach (float dz in new[] { -0.38f, 0.38f })
                        Box(env, "CoffeeTableLeg", new Vector3(8.5f + dx, 0.36f, 8.15f + dz), new Vector3(0.08f, 0.72f, 0.08f), ProjectBootstrap.MatDarkWood);
            }
            else
            {
                float cty = TopY(coffeeTbl);
                PackFit(env, KF + "books.fbx", new Vector3(8.15f, cty, 8.15f), 20f, new Vector3(0.38f, 0.26f, 0.32f), ColliderMode.None);
                PackFit(env, KF + "plantSmall1.fbx", new Vector3(8.85f, cty, 8.15f), 0f, new Vector3(0.32f, 0.38f, 0.32f), ColliderMode.None);
            }
            HideSpot(env, "HidingSpot_CoffeeTable", new Vector3(8.5f, 0.34f, 8.15f), new Vector3(1.65f, 0.72f, 0.82f), "под журнальным столиком");

            if (PackFit(env, KF + "loungeChair.fbx", new Vector3(1.9f, 0f, 9.35f), FaceS + 25f, new Vector3(1.45f, 1.35f, 1.45f), ColliderMode.Box) == null)
            {
                Box(env, "Armchair", new Vector3(1.9f, 0.45f, 9.35f), new Vector3(1.35f, 0.9f, 1.35f), ProjectBootstrap.MatRed);
            }
            PackFit(env, KF + "loungeChair.fbx", new Vector3(10.45f, 0f, 6.55f), FaceW - 15f, new Vector3(1.40f, 1.32f, 1.40f), ColliderMode.Box);
            GameObject sideTbl = PackFit(env, KF + "cabinetBedDrawer.fbx", new Vector3(3.25f, 0f, 10.15f), FaceS, new Vector3(0.78f, 0.85f, 0.72f), ColliderMode.Box);
            if (sideTbl != null)
            {
                SnapTo(sideTbl, Vector3.forward, 10.78f);
                PackFit(env, KF + "lampRoundTable.fbx", new Vector3(3.25f, TopY(sideTbl), 10.15f), FaceS, new Vector3(0.45f, 0.55f, 0.45f), ColliderMode.None);
            }
            PackFit(env, KF + "lampRoundFloor.fbx", new Vector3(10.55f, 0f, 10.0f), FaceS, new Vector3(0.68f, 2.35f, 0.68f), ColliderMode.Box);
            PackFit(env, KF + "pottedPlant.fbx", new Vector3(0.85f, 0f, 10.05f), FaceS, new Vector3(0.85f, 1.65f, 0.85f), ColliderMode.Box);
            GameObject lrShelf = PackFit(env, KF + "bookcaseOpen.fbx", new Vector3(4.65f, 0f, 10.35f), FaceS, new Vector3(1.85f, 2.95f, 0.62f), ColliderMode.Box);
            SnapTo(lrShelf, Vector3.forward, 10.78f);
            FillShelves(env, lrShelf, BookModels, 0.36f, 3, 250, true);
            GameObject lrShelfW = PackFit(env, KF + "bookcaseOpen.fbx", new Vector3(0.55f, 0f, 6.2f), FaceE, new Vector3(0.62f, 2.95f, 1.85f), ColliderMode.Box);
            SnapTo(lrShelfW, Vector3.left, 0.14f);
            FillShelves(env, lrShelfW, BookModels, 0.36f, 3, 275, true);

            // Old TV with the remote on the stand edge: knock the remote off and the TV hisses white noise.
            SilentNight.Stealth.NoisyTV.Create(env, new Vector3(11.55f, 0f, 8.5f), -90f, Mat(ProjectBootstrap.MatDarkWood));
            // Squeaky toy on the carpet.
            Ball(env, "SqueakyToy", new Vector3(7.0f, 0.12f, 4.6f), Vector3.one * 0.22f, ProjectBootstrap.MatRed);
            NoiseZoneAt(new Vector3(7.0f, 0.3f, 4.6f), new Vector3(0.9f, 0.6f, 0.9f), NoiseSource.Object, 40f, 2f);
            // Toys on the floor: trip the elf unless it sneaks [C] over them.
            Toy(env, "ToyCar", new Vector3(7.8f, 0f, 5.4f), 25f, new Vector3(0.40f, 0.20f, 0.30f), ProjectBootstrap.MatRed, null);
            Toy(env, "ToyBlocks", new Vector3(4.0f, 0f, 5.2f), 0f, new Vector3(0.60f, 0.20f, 0.50f), ProjectBootstrap.MatGold, "Хрусть! Эльф споткнулся о кубики.");
            Toy(env, "ToyCar2", new Vector3(2.5f, 0f, 8.5f), -40f, new Vector3(0.40f, 0.20f, 0.30f), ProjectBootstrap.MatGreen, null);
            Toy(env, "RubberDuck", new Vector3(6.0f, 0f, -3.5f), 0f, new Vector3(0.35f, 0.20f, 0.30f), ProjectBootstrap.MatGold, "Пиии! Эльф наступил на резиновую уточку.");
            Toy(env, "ToyTrain", new Vector3(-1.75f, 0f, 3.0f), 90f, new Vector3(0.50f, 0.20f, 0.40f), ProjectBootstrap.MatRed, "Ту-ту! Эльф наступил на игрушечный паровозик.");

            // Garland along the north and east walls (cozy only).
            var garland = new GameObject("Garland").transform;
            garland.SetParent(moodProps, false);
            for (int i = 0; i < 10; i++)
            {
                float x = 0.8f + i * 1.15f;
                float sag = Mathf.Sin(i / 9f * Mathf.PI) * 0.28f;
                Ball(garland, "Bulb", new Vector3(x, 3.65f - sag, 10.75f), Vector3.one * 0.12f,
                    i % 2 == 0 ? ProjectBootstrap.MatEmissiveWarm : ProjectBootstrap.MatEmissiveGold);
            }
            LightStrand(garland, new Vector3(0.8f, 0f, 10.78f), new Vector3(11.2f, 0f, 10.78f), 3.75f);
            LightStrand(garland, new Vector3(11.78f, 0f, 0.8f), new Vector3(11.78f, 0f, 10.2f), 3.75f);
            cozyOnly.Add(garland.gameObject);

            BuildBrassChandelier(env, new Vector3(5.8f, 3.2f, 5.8f), h);
            BuildBrassSconce(env, new Vector3(8.5f, 2.4f, 10.76f), FaceS);
            BuildBrassSconce(env, new Vector3(0.14f, 2.4f, 1.5f), FaceE);

            Light treeLight = Point(env, "TreeLight", new Vector3(5.5f, 2.8f, 5.2f), new Color(1f, 0.80f, 0.50f), 6.5f, 11f);
            treeLight.shadows = LightShadows.Soft;
            warm.Add(treeLight);
            Light fireLight = Point(env, "FireLight", new Vector3(9.4f, 1.1f, 3.5f), new Color(1f, 0.58f, 0.22f), 6f, 10f);
            fireLight.shadows = LightShadows.Soft;
            warm.Add(fireLight);
            warm.Add(Point(env, "SconceLight", new Vector3(8.5f, 2.4f, 10.2f), new Color(1f, 0.72f, 0.38f), 4f, 9f));

            // ---- Hallway (x -3.5..0, z -11..11): stairs, towering grandfather clock, coat rack, creaky boards ----
            for (int i = 0; i < 9; i++)
            {
                float stepH = (i + 1) * 0.25f;
                GameObject step = Box(env, "Stair" + i, new Vector3(-0.85f, stepH * 0.5f, 6.75f + i * 0.32f), new Vector3(1.45f, stepH, 0.32f), ProjectBootstrap.MatDarkWood);
                step.AddComponent<SurfaceTag>().Surface = SurfaceType.CreakyWood;
            }
            GameObject landing = Box(env, "StairLanding", new Vector3(-0.85f, 1.125f, 10.15f), new Vector3(1.45f, 2.25f, 1.6f), ProjectBootstrap.MatDarkWood);
            landing.AddComponent<SurfaceTag>().Surface = SurfaceType.CreakyWood;
            Box(env, "Banister", new Vector3(-1.62f, 1.85f, 8.6f), new Vector3(0.08f, 0.08f, 4.7f), ProjectBootstrap.MatWood, false);
            Box(env, "BanisterPost", new Vector3(-1.62f, 0.92f, 6.65f), new Vector3(0.10f, 1.85f, 0.10f), ProjectBootstrap.MatWood);

            // Towering 3.05m Grandfather Clock
            Box(env, "GrandfatherClock", new Vector3(-0.48f, 1.52f, 1.0f), new Vector3(0.58f, 3.04f, 0.68f), ProjectBootstrap.MatDarkWood);
            Box(env, "ClockTrimTop", new Vector3(-0.48f, 3.08f, 1.0f), new Vector3(0.68f, 0.12f, 0.78f), ProjectBootstrap.MatBrass, false);
            Box(env, "ClockFace", new Vector3(-0.78f, 2.48f, 1.0f), new Vector3(0.03f, 0.48f, 0.48f), ProjectBootstrap.MatPaper, false);
            Box(env, "ClockPendulum", new Vector3(-0.78f, 1.55f, 1.0f), new Vector3(0.03f, 0.72f, 0.12f), ProjectBootstrap.MatBrass, false);
            PackFit(env, KF + "coatRackStanding.fbx", new Vector3(-2.95f, 0f, -9.8f), FaceE, new Vector3(0.75f, 2.45f, 0.75f), ColliderMode.Box);
            GameObject hallConsole = PackFit(env, KF + "cabinetBedDrawer.fbx", new Vector3(-3.05f, 0f, -3.4f), FaceE, new Vector3(0.65f, 0.95f, 1.25f), ColliderMode.Box);
            if (hallConsole != null)
            {
                SnapTo(hallConsole, Vector3.left, -3.38f);
                PackFit(env, KF + "plantSmall1.fbx", new Vector3(-3.05f, TopY(hallConsole), -3.4f), FaceE, new Vector3(0.35f, 0.42f, 0.35f), ColliderMode.None);
            }

            CreakyBoard(env, new Vector3(-1.2f, 0f, -2.0f), new Vector2(2.0f, 2.4f));
            CreakyBoard(env, new Vector3(-2.4f, 0f, 2.4f), new Vector2(1.8f, 2.0f));
            CreakyBoard(env, new Vector3(-2.5f, 0f, 7.5f), new Vector2(1.6f, 1.8f)); // in front of grandpa's door

            BuildBrassChandelier(env, new Vector3(-1.75f, 3.25f, -4f), h);
            flicker.Add(Point(env, "HallLight", new Vector3(-1.75f, 2.9f, -4f), new Color(1f, 0.82f, 0.58f), 4.5f, 10f));

            // ---- Kitchen (x 0..12, z -11..0): tile, giant human dining table, towering fridge, cat ----
            // Note: CounterSouth top stays at y = 0.90 around x = 6.5 so House_ElfClimbsOntoKitchenCounter mantle test passes!
            GameObject cs = Box(env, "CounterSouth", new Vector3(6.5f, 0.45f, -10.45f), new Vector3(9.2f, 0.9f, 0.78f), ProjectBootstrap.MatWood);
            GameObject ce = Box(env, "CounterEast", new Vector3(11.45f, 0.45f, -7.5f), new Vector3(0.78f, 0.9f, 3.8f), ProjectBootstrap.MatWood);
            if (PackExists(KF + "kitchenCabinet.fbx"))
            {
                HideRenderer(cs);
                HideRenderer(ce);
                string[] southUnits =
                {
                    "kitchenCabinet.fbx", "kitchenCabinetDrawer.fbx", "kitchenSink.fbx", "kitchenCabinet.fbx",
                    "kitchenCabinetDrawer.fbx", "kitchenCabinet.fbx", "kitchenCabinet.fbx", "kitchenStove.fbx",
                    "kitchenCabinetDrawer.fbx", "kitchenCabinetCornerRound.fbx",
                };
                for (int i = 0; i < southUnits.Length; i++)
                {
                    float ux = 2.45f + i * 0.9f;
                    GameObject u = PackModel(env, KF + southUnits[i], new Vector3(ux, 0f, -10.45f), FaceN, new Vector3(0f, 0.90f, 0f), ColliderMode.None);
                    FitDepth(u, 0.78f, false);
                    SnapTo(u, Vector3.back, -10.85f);
                }
                Box(env, "CounterSouthWorktop", new Vector3(6.5f, 0.885f, -10.45f), new Vector3(9.25f, 0.03f, 0.80f), ProjectBootstrap.MatWood, false);
                string[] eastUnits = { "kitchenCabinet.fbx", "kitchenCabinetDrawer.fbx", "kitchenCabinet.fbx", "kitchenCabinetDrawer.fbx" };
                for (int i = 0; i < eastUnits.Length; i++)
                {
                    float uz = -8.85f + i * 0.9f;
                    GameObject u = PackModel(env, KF + eastUnits[i], new Vector3(11.45f, 0f, uz), FaceW, new Vector3(0f, 0.90f, 0f), ColliderMode.None);
                    FitDepth(u, 0.78f, true);
                    SnapTo(u, Vector3.right, 11.85f);
                }
                Box(env, "CounterEastWorktop", new Vector3(11.45f, 0.885f, -7.5f), new Vector3(0.80f, 0.03f, 3.85f), ProjectBootstrap.MatWood, false);
                // Human-scaled countertop props and upper cabinets kept away from x = 6.5 (the mantle test spot).
                PackFit(env, KF + "kitchenCoffeeMachine.fbx", new Vector3(2.55f, 0.91f, -10.45f), FaceN, new Vector3(0.52f, 0.56f, 0.50f), ColliderMode.None);
                PackFit(env, KF + "toaster.fbx", new Vector3(3.45f, 0.91f, -10.45f), FaceN, new Vector3(0.46f, 0.36f, 0.36f), ColliderMode.None);
                PackFit(env, KF + "kitchenMicrowave.fbx", new Vector3(9.65f, 0.91f, -10.45f), FaceN, new Vector3(0.72f, 0.48f, 0.55f), ColliderMode.None);
                PackFit(env, KF + "kitchenBlender.fbx", new Vector3(11.45f, 0.91f, -6.3f), FaceW, new Vector3(0.42f, 0.56f, 0.42f), ColliderMode.None);
                GameObject hood = PackFit(env, KF + "hoodModern.fbx", new Vector3(8.75f, 2.05f, -10.45f), FaceN, new Vector3(1.25f, 1.05f, 0.75f), ColliderMode.None);
                SnapTo(hood, Vector3.back, -10.85f);
                GameObject upCab1 = PackFit(env, KF + "kitchenCabinetUpperDouble.fbx", new Vector3(3.25f, 2.05f, -10.55f), FaceN, new Vector3(2.15f, 1.05f, 0.55f), ColliderMode.None);
                SnapTo(upCab1, Vector3.back, -10.85f);
                GameObject upCab2 = PackFit(env, KF + "kitchenCabinetUpperDouble.fbx", new Vector3(11.45f, 2.05f, -7.5f), FaceW, new Vector3(0.55f, 1.05f, 2.15f), ColliderMode.None);
                SnapTo(upCab2, Vector3.right, 11.85f);
            }
            else
            {
                Box(env, "CounterSouthTop", new Vector3(6.5f, 0.92f, -10.45f), new Vector3(9.25f, 0.04f, 0.80f), ProjectBootstrap.MatTile, false);
            }
            // Towering Human Refrigerator (2.65m tall)
            if (PackFit(env, KF + "kitchenFridgeLarge.fbx", new Vector3(11.2f, 0f, -3.2f), FaceW, new Vector3(1.25f, 2.65f, 1.25f), ColliderMode.Box) == null)
                Box(env, "Fridge", new Vector3(11.2f, 1.32f, -3.2f), new Vector3(1.15f, 2.65f, 1.15f), ProjectBootstrap.MatMetal);

            // Giant Human Dining Table (2.6m x 1.06m x 1.45m) + 4 tall chairs
            GameObject kitTbl = PackFit(env, KF + "tableCloth.fbx", new Vector3(6.5f, 0f, -5.5f), FaceS, new Vector3(2.60f, 1.06f, 1.45f), ColliderMode.TopSlab);
            if (kitTbl == null)
            {
                Box(env, "KitchenTable", new Vector3(6.5f, 1.02f, -5.5f), new Vector3(2.60f, 0.08f, 1.45f), ProjectBootstrap.MatWood);
                foreach (float dx in new[] { -1.15f, 1.15f })
                    foreach (float dz in new[] { -0.60f, 0.60f })
                        Box(env, "TableLeg", new Vector3(6.5f + dx, 0.50f, -5.5f + dz), new Vector3(0.10f, 1.0f, 0.10f), ProjectBootstrap.MatDarkWood);
            }
            else
            {
                float kty = TopY(kitTbl);
                PackFit(env, KF + "plantSmall1.fbx", new Vector3(6.5f, kty, -5.5f), 0f, new Vector3(0.38f, 0.45f, 0.38f), ColliderMode.None);
            }
            HideSpot(env, "HidingSpot_KitchenTable", new Vector3(6.5f, 0.48f, -5.5f), new Vector3(2.35f, 0.98f, 1.25f), "под кухонным столом");
            if (PackFit(env, KF + "chairCushion.fbx", new Vector3(4.75f, 0f, -5.5f), FaceE, new Vector3(0.82f, 1.32f, 0.82f), ColliderMode.Box) == null)
                Box(env, "ChairW", new Vector3(4.75f, 0.35f, -5.5f), new Vector3(0.7f, 0.7f, 0.7f), ProjectBootstrap.MatDarkWood);
            if (PackFit(env, KF + "chairCushion.fbx", new Vector3(8.25f, 0f, -5.5f), FaceW, new Vector3(0.82f, 1.32f, 0.82f), ColliderMode.Box) == null)
                Box(env, "ChairE", new Vector3(8.25f, 0.35f, -5.5f), new Vector3(0.7f, 0.7f, 0.7f), ProjectBootstrap.MatDarkWood);
            PackFit(env, KF + "chairCushion.fbx", new Vector3(6.5f, 0f, -4.35f), FaceS, new Vector3(0.82f, 1.32f, 0.82f), ColliderMode.Box);
            PackFit(env, KF + "chairCushion.fbx", new Vector3(6.5f, 0f, -6.65f), FaceN, new Vector3(0.82f, 1.32f, 0.82f), ColliderMode.Box);

            Disc(env, "CatBed", new Vector3(3.2f, 0.015f, -2.6f), 1.15f, 0.04f, ProjectBootstrap.MatVelvetRed, false);
            GameObject catGo = NPCFactory.CreateCat(new Vector3(3.2f, 0f, -2.6f), Quaternion.Euler(0f, 90f, 0f));
            var catModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/Cat/Cat.fbx");
            if (catModel != null) catGo.GetComponent<NPCVisual>().Configure(NPCKind.Cat, catModel);
            var catCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Characters/Cat/Cat.controller");
            if (catCtrl != null) catGo.GetComponent<NPCVisual>().AnimatorController = catCtrl;
            BuildBrassChandelier(env, new Vector3(6.5f, 3.15f, -5.5f), h);
            Light kitLight = Point(env, "KitchenLight", new Vector3(6.5f, 2.8f, -5.5f), new Color(1f, 0.92f, 0.78f), 6.5f, 12f);
            kitLight.shadows = LightShadows.Soft;
            flicker.Add(kitLight);

            // ---- Grandpa's bedroom (x -12..-3.5, z 4..11) ----
            // Giant Human King-Size Bed on legs: crawl space underneath to hide right next to Grandpa.
            GameObject bedFrame = Box(env, "BedFrame", new Vector3(-10.0f, 0.72f, 8f), new Vector3(3.05f, 0.12f, 2.20f), ProjectBootstrap.MatDarkWood);
            GameObject bedModel = PackFit(env, KF + "bedDouble.fbx", new Vector3(-10.0f, 0f, 8f), FaceE, new Vector3(3.15f, 1.38f, 2.35f), ColliderMode.None);
            if (bedModel != null)
            {
                HideRenderer(bedFrame);
                SnapTo(bedModel, Vector3.left, -11.8f);
            }
            else
            {
                foreach (float dx in new[] { -1.35f, 1.35f })
                    foreach (float dz in new[] { -0.95f, 0.95f })
                        Box(env, "BedLeg", new Vector3(-10.0f + dx, 0.34f, 8f + dz), new Vector3(0.12f, 0.68f, 0.12f), ProjectBootstrap.MatDarkWood);
                Box(env, "Mattress", new Vector3(-10.0f, 0.88f, 8f), new Vector3(2.85f, 0.20f, 2.05f), ProjectBootstrap.MatPaper);
                Box(env, "Headboard", new Vector3(-11.55f, 0.85f, 8f), new Vector3(0.14f, 1.7f, 2.25f), ProjectBootstrap.MatDarkWood);
            }
            HideSpot(env, "HidingSpot_Bed", new Vector3(-10.1f, 0.34f, 8f), new Vector3(2.75f, 0.72f, 1.95f), "под кроватью Деда");
            GameObject ns = PackFit(env, KF + "cabinetBedDrawer.fbx", new Vector3(-11.35f, 0f, 6.1f), FaceE, new Vector3(0.82f, 0.90f, 0.82f), ColliderMode.Box);
            if (ns != null)
            {
                SnapTo(ns, Vector3.left, -11.8f);
                PackFit(env, KF + "lampRoundTable.fbx", new Vector3(-11.35f, TopY(ns), 6.1f), FaceE, new Vector3(0.46f, 0.54f, 0.46f), ColliderMode.None);
            }
            else
            {
                Box(env, "Nightstand", new Vector3(-11.35f, 0.45f, 6.1f), new Vector3(0.75f, 0.90f, 0.75f), ProjectBootstrap.MatDarkWood);
                Ball(env, "BedsideLamp", new Vector3(-11.35f, 1.1f, 6.1f), Vector3.one * 0.35f, ProjectBootstrap.MatEmissiveWarm);
            }
            // Towering 2.95m Human Wardrobe + Dresser + Reading Armchair
            GameObject wd = PackFit(env, KF + "bookcaseClosedDoors.fbx", new Vector3(-6.0f, 0f, 10.35f), FaceS, new Vector3(2.25f, 2.95f, 0.85f), ColliderMode.Box);
            if (wd != null) SnapTo(wd, Vector3.forward, 10.78f);
            else Box(env, "Wardrobe", new Vector3(-6.0f, 1.47f, 10.35f), new Vector3(2.25f, 2.95f, 0.85f), ProjectBootstrap.MatDarkWood);
            GameObject dresser = PackFit(env, KF + "cabinetBedDrawer.fbx", new Vector3(-8.55f, 0f, 10.35f), FaceS, new Vector3(1.45f, 1.15f, 0.75f), ColliderMode.Box);
            if (dresser != null) SnapTo(dresser, Vector3.forward, 10.78f);
            PackFit(env, KF + "loungeChair.fbx", new Vector3(-5.1f, 0f, 5.2f), FaceN - 25f, new Vector3(1.38f, 1.28f, 1.38f), ColliderMode.Box);
            BuildNordicRug(env, "BedroomRug", new Vector3(-7.4f, 0f, 7.5f), new Vector2(4.2f, 3.6f));
            BuildBrassSconce(env, new Vector3(-11.76f, 2.35f, 8f), FaceE);
            warm.Add(Point(env, "BedsideLight", new Vector3(-11.0f, 1.55f, 6.1f), new Color(1f, 0.74f, 0.45f), 3.5f, 8f));

            var patrolRoot = new GameObject("GrandpaPatrolPoints").transform;
            Transform p1 = new GameObject("Patrol_Kitchen").transform;
            p1.SetParent(patrolRoot, false); p1.position = new Vector3(9.5f, 0f, -4.5f);
            Transform p2 = new GameObject("Patrol_Hallway").transform;
            p2.SetParent(patrolRoot, false); p2.position = new Vector3(-1.8f, 0f, -7f);
            // Head on the pillow (-X), gets up towards +X (into the room).
            NPCFactory.CreateGrandpa(new Vector3(-10.4f, 0f, 8f), Quaternion.Euler(0f, 90f, 0f), new[] { p1, p2 });

            // ---- Study (x -12..-3.5, z 0.5..4) — floor-to-ceiling bookcases & human executive desk ----
            string[] bookMats = { ProjectBootstrap.MatRed, ProjectBootstrap.MatGreen, ProjectBootstrap.MatFabric, ProjectBootstrap.MatGold };
            for (int i = 0; i < 4; i++)
            {
                float x = -11.0f + i * 1.55f;
                GameObject bs = PackFit(env, KF + "bookcaseOpen.fbx", new Vector3(x, 0f, 3.55f), FaceS, new Vector3(1.48f, 3.05f, 0.58f), ColliderMode.Box);
                if (bs != null)
                {
                    SnapTo(bs, Vector3.forward, 3.88f);
                    FillShelves(env, bs, BookModels, 0.36f, 3, 300 + i * 7, true);
                }
                else
                {
                    Box(env, "Bookshelf" + i, new Vector3(x, 1.5f, 3.55f), new Vector3(1.45f, 3.0f, 0.55f), ProjectBootstrap.MatDarkWood);
                    for (int row = 0; row < 3; row++)
                        Box(env, "Books", new Vector3(x, 0.75f + row * 0.8f, 3.28f), new Vector3(1.2f, 0.45f, 0.08f), bookMats[(i + row) % bookMats.Length], false);
                }
            }
            GameObject desk = PackFit(env, KF + "desk.fbx", new Vector3(-10.2f, 0f, 1.35f), FaceN, new Vector3(2.15f, 1.06f, 1.05f), ColliderMode.Box);
            if (desk != null)
            {
                float dy = TopY(desk);
                PackFit(env, KF + "lampSquareTable.fbx", new Vector3(-10.95f, dy, 1.15f), FaceN, new Vector3(0.42f, 0.54f, 0.42f), ColliderMode.None);
                PackFit(env, KF + "computerScreen.fbx", new Vector3(-10.15f, dy, 1.12f), FaceN, new Vector3(0.72f, 0.56f, 0.32f), ColliderMode.None);
                PackFit(env, KF + "computerKeyboard.fbx", new Vector3(-10.15f, dy, 1.52f), FaceN, new Vector3(0.55f, 0.10f, 0.24f), ColliderMode.None);
                PackFit(env, KF + "books.fbx", new Vector3(-9.45f, dy, 1.25f), FaceN + 15f, new Vector3(0.42f, 0.34f, 0.34f), ColliderMode.None);
            }
            else
            {
                Box(env, "Desk", new Vector3(-10.2f, 0.53f, 1.35f), new Vector3(2.15f, 1.06f, 1.0f), ProjectBootstrap.MatWood);
                Ball(env, "DeskLamp", new Vector3(-10.95f, 1.25f, 1.15f), Vector3.one * 0.28f, ProjectBootstrap.MatEmissiveWarm);
            }
            if (PackFit(env, KF + "chairDesk.fbx", new Vector3(-10.2f, 0f, 2.25f), FaceS, new Vector3(0.82f, 1.32f, 0.82f), ColliderMode.Box) == null)
                Box(env, "DeskChair", new Vector3(-10.2f, 0.4f, 2.25f), new Vector3(0.7f, 0.8f, 0.7f), ProjectBootstrap.MatFabric);
            if (PackFit(env, KF + "pottedPlant.fbx", new Vector3(-4.8f, 0f, 1.25f), FaceN, new Vector3(0.82f, 1.65f, 0.82f), ColliderMode.Box) == null)
                Box(env, "Globe", new Vector3(-4.8f, 0.75f, 1.25f), new Vector3(0.6f, 1.5f, 0.6f), ProjectBootstrap.MatDarkWood);
            BuildBrassChandelier(env, new Vector3(-7.8f, 3.2f, 2.2f), h);
            flicker.Add(Point(env, "StudyLight", new Vector3(-7.8f, 2.8f, 2.2f), new Color(1f, 0.85f, 0.6f), 4.5f, 8.5f));

            // ---- Garage (x -12..-3.5, z -11..0.5): ice puddle, human-scale family SUV, towering storage racks ----
            GameObject ice = Disc(env, "IcePuddle", new Vector3(-8.5f, 0.015f, -7.8f), 4.4f, 0.03f, ProjectBootstrap.MatIce, true);
            ice.AddComponent<SurfaceTag>().Surface = SurfaceType.Ice;
            BuildGarageCar(env, new Vector3(-10.1f, 0f, -2.8f), 1.25f);
            if (PackExists(KF + "bookcaseOpen.fbx"))
            {
                for (int i = 0; i < 2; i++)
                {
                    float gx = -8.9f + i * 1.95f;
                    GameObject gs = PackFit(env, KF + "bookcaseOpen.fbx", new Vector3(gx, 0f, -10.45f), FaceN, new Vector3(1.85f, 2.85f, 0.68f), ColliderMode.Box);
                    SnapTo(gs, Vector3.back, -10.82f);
                    FillShelves(env, gs, GarageShelfModels, 0.38f, 3, 400 + i * 11, true);
                }
                GameObject gc1 = PackFit(env, KF + "cardboardBoxClosed.fbx", new Vector3(-5.1f, 0f, -10.0f), 5f, new Vector3(1.15f, 1.0f, 1.15f), ColliderMode.Box);
                PackFit(env, KF + "cardboardBoxOpen.fbx", new Vector3(-5.1f, TopY(gc1), -10.0f), 20f, new Vector3(0.95f, 0.85f, 0.95f), ColliderMode.Box);
                PackFit(env, KF + "trashcan.fbx", new Vector3(-4.1f, 0f, -10.1f), FaceN, new Vector3(0.72f, 0.95f, 0.72f), ColliderMode.Box);
            }
            else
            {
                Box(env, "GarageShelf", new Vector3(-8f, 1.4f, -10.5f), new Vector3(3.6f, 2.8f, 0.65f), ProjectBootstrap.MatMetal);
            }
            Box(env, "ToolBench", new Vector3(-11.25f, 0.56f, -6.2f), new Vector3(1.05f, 1.12f, 2.6f), ProjectBootstrap.MatDarkWood);
            PackFit(env, KF + "cardboardBoxOpen.fbx", new Vector3(-11.25f, 1.12f, -5.5f), FaceE, new Vector3(0.75f, 0.62f, 0.75f), ColliderMode.None);
            PackFit(env, KF + "radio.fbx", new Vector3(-11.25f, 1.12f, -6.7f), FaceE, new Vector3(0.50f, 0.40f, 0.36f), ColliderMode.None);
            flicker.Add(Point(env, "GarageLight", new Vector3(-8f, 3.1f, -5f), new Color(0.82f, 0.88f, 1f), 5f, 11f));

            // Cobwebs (spooky only).
            var cobwebs = new GameObject("Cobwebs").transform;
            cobwebs.SetParent(moodProps, false);
            Box(cobwebs, "Web", new Vector3(-11.7f, 3.8f, -10.7f), new Vector3(0.6f, 0.6f, 0.02f), ProjectBootstrap.MatPaper, false, 45f);
            Box(cobwebs, "Web", new Vector3(-3.7f, 3.8f, 10.7f), new Vector3(0.6f, 0.6f, 0.02f), ProjectBootstrap.MatPaper, false, -45f);
            Box(cobwebs, "Web", new Vector3(11.7f, 3.8f, -10.7f), new Vector3(0.6f, 0.6f, 0.02f), ProjectBootstrap.MatPaper, false, -45f);
            Box(cobwebs, "Web", new Vector3(-11.7f, 3.8f, 3.8f), new Vector3(0.5f, 0.5f, 0.02f), ProjectBootstrap.MatPaper, false, 45f);
            spookyOnly.Add(cobwebs.gameObject);

            // ---- mood controller ----
            var moodGo = new GameObject("HouseMood");
            var mood = moodGo.AddComponent<HouseMoodController>();
            SetArray(mood, "warmLights", warm.ConvertAll(l => (Object)l));
            SetArray(mood, "spookyFlickerLights", flicker.ConvertAll(l => (Object)l));
            SetArray(mood, "cozyOnlyObjects", cozyOnly.ConvertAll(g => (Object)g));
            SetArray(mood, "spookyOnlyObjects", spookyOnly.ConvertAll(g => (Object)g));

            // Soot cloud: the elf just fell down the chimney.
            var soot = new GameObject("ArrivalSoot");
            soot.transform.position = new Vector3(9.6f, 0.5f, 3.5f);
            soot.AddComponent<SilentNight.FX.SootPuff>();

            // ---- player (arrives through the chimney) ----
            CreateSpawnAndPlayer(new Vector3(8.9f, 0f, 3.5f), Quaternion.Euler(0f, -90f, 0f));

            MakeStatic(env.gameObject);

            // ---- NavMesh ----
            BakeHouseNavMesh();

            SaveAndBake(scene, HousePath);
        }

        private static void BakeHouseNavMesh()
        {
            var go = new GameObject("NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            int mask = ~0;
            mask = Exclude(mask, GameConstants.LayerPlayer);
            mask = Exclude(mask, GameConstants.LayerGift);
            mask = Exclude(mask, GameConstants.LayerNPC);
            mask = Exclude(mask, GameConstants.LayerInteractable);
            mask = Exclude(mask, GameConstants.LayerCameraIgnore);
            mask = Exclude(mask, "Ignore Raycast"); // noise trigger zones
            surface.layerMask = mask;

            ProjectBootstrap.EnsureFolder(HouseNavMeshFolder);
            if (AssetDatabase.LoadAssetAtPath<Object>(HouseNavMeshPath) != null) AssetDatabase.DeleteAsset(HouseNavMeshPath);

            Physics.SyncTransforms();
            surface.BuildNavMesh();

            // VERIFY: the built NavMeshData lives only in memory; saving it as an asset makes the scene reference persist
            // (this is what the NavMeshSurface inspector "Bake" button does too).
            if (surface.navMeshData != null)
            {
                AssetDatabase.CreateAsset(surface.navMeshData, HouseNavMeshPath);
                EditorUtility.SetDirty(surface);
                AssetDatabase.SaveAssets();
            }
            else
            {
                Debug.LogError("[Silent Night] NavMesh bake produced no data — select 'NavMesh' in House and press Bake.");
            }
        }

        private static int Exclude(int mask, string layerName)
        {
            int l = LayerMask.NameToLayer(layerName);
            return l >= 0 ? mask & ~(1 << l) : mask;
        }

        // ------------------------------------------------------------------ shared scene setup

        private static void SetupEnvironmentLighting(Color sky, Color equator, Color ground, bool fog, Color fogColor, float fogDensity)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        }

        private static void CreateCamera(Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetPositionAndRotation(pos, rot);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.08f);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 250f;
            cam.fieldOfView = 60f;
            go.AddComponent<AudioListener>();
            go.AddComponent<CinemachineBrain>(); // ThirdPersonCameraRig creates the CinemachineCamera at runtime

            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.renderShadows = true;
        }

        private static void CreateMoonlight(float intensity, Quaternion rot)
        {
            var go = new GameObject("Moonlight");
            go.transform.rotation = rot;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.6f, 0.68f, 1f);
            l.intensity = intensity;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.8f;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.sun = l;
        }

        private static void CreateGlobalVolume()
        {
            var go = new GameObject("Global Volume");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 0f;
            v.sharedProfile = _profile;
        }

        private static void CreateSpawnAndPlayer(Vector3 pos, Quaternion rot)
        {
            var spawn = new GameObject("PlayerSpawn");
            try { spawn.tag = GameConstants.TagSpawn; }
            catch (UnityException) { Debug.LogError("[Silent Night] Tag PlayerSpawn missing — run Silent Night ▸ Setup Project."); }
            spawn.transform.SetPositionAndRotation(pos, rot);

            GameObject elf = SilentNight.Player.PlayerSetup.CreatePlayer(pos, 0);
            elf.transform.rotation = rot;
            var rag = elf.GetComponent<SilentNight.Player.ElfRagdoll>();
            if (rag != null)
                rag.ModelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SilentNight.Player.ElfRagdoll.DefaultModelPath);
        }

        private static LightingSettings EnsureLightingSettings()
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "SilentNight_Lighting" };
                AssetDatabase.CreateAsset(ls, LightingSettingsPath); // VERIFY: .lighting extension for LightingSettings assets
            }
            ls.bakedGI = false;    // realtime lights only; no lightmaps
            ls.realtimeGI = false;
            ls.autoGenerate = false; // VERIFY: LightingSettings.autoGenerate (Unity 2020.1+)
            EditorUtility.SetDirty(ls);
            AssetDatabase.SaveAssets();
            return ls;
        }

        private static void SaveAndBake(Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);
            if (!BakeEnvironmentLighting || Application.isBatchMode) return; // batch builds (CI / tests) skip the bake
            try
            {
                // Generates the ambient probe / reflection for the Trilight environment (no lightmaps: GI is off).
                if (Lightmapping.Bake()) EditorSceneManager.SaveScene(scene, path);
                else Debug.LogWarning($"[Silent Night] Environment lighting bake failed for {path} (optional: Window ▸ Rendering ▸ Lighting ▸ Generate Lighting).");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Silent Night] Environment lighting bake skipped for {path}: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ props

        private static void BuildTree(Transform parent, Vector3 basePos, float s)
        {
            GameObject treeModel = Model(parent, "LivingRoom/ChristmasTree.fbx", "ChristmasTreeModel", basePos, 0f, 1.25f * s);
            if (treeModel != null)
            {
                // Collider only around the pot, so gifts can be placed right next to the tree.
                var potCol = new GameObject("TreePotCollider");
                potCol.transform.SetParent(parent, false);
                potCol.transform.position = basePos + Vector3.up * 0.35f * s;
                potCol.AddComponent<BoxCollider>().size = new Vector3(0.95f * s, 0.7f * s, 0.95f * s);

                // Hang 3D custom ornaments (Ornament_A/B/C.fbx) + glowing star topper on the tree!
                string[] ornModels = { "Decor/Ornament_A.fbx", "Decor/Ornament_B.fbx", "Decor/Ornament_C.fbx" };
                for (int i = 0; i < 12; i++)
                {
                    float tier = i / 4; // 0, 1, 2
                    float a = (i * 1.35f) + tier * 0.5f;
                    float y = (0.95f + tier * 0.62f + (i % 2) * 0.18f) * s;
                    float r = (0.82f - tier * 0.20f) * s;
                    Vector3 op = basePos + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    Model(parent, ornModels[i % ornModels.Length], "Ornament_" + i, op, (i * 57f) % 360f, 0.85f * s);
                }
                Ball(parent, "TreeStar", basePos + Vector3.up * 3.18f * s, Vector3.one * 0.24f * s, ProjectBootstrap.MatEmissiveGold);
                return;
            }
            Disc(parent, "TreeTrunk", basePos + Vector3.up * 0.35f * s, 0.3f * s, 0.7f * s, ProjectBootstrap.MatDarkWood, true);
            Ball(parent, "TreeTier1", basePos + Vector3.up * 1.15f * s, new Vector3(1.9f, 1.1f, 1.9f) * s, ProjectBootstrap.MatGreen);
            Ball(parent, "TreeTier2", basePos + Vector3.up * 1.85f * s, new Vector3(1.45f, 1.0f, 1.45f) * s, ProjectBootstrap.MatGreen);
            Ball(parent, "TreeTier3", basePos + Vector3.up * 2.5f * s, new Vector3(0.95f, 0.9f, 0.95f) * s, ProjectBootstrap.MatGreen);
            Ball(parent, "TreeStar", basePos + Vector3.up * 3.05f * s, Vector3.one * 0.25f * s, ProjectBootstrap.MatEmissiveWarm);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                float r = (i % 2 == 0 ? 0.88f : 0.68f) * s;
                float y = (i % 2 == 0 ? 1.1f : 1.8f) * s;
                string m = i % 3 == 0 ? ProjectBootstrap.MatGold : (i % 3 == 1 ? ProjectBootstrap.MatRed : ProjectBootstrap.MatEmissiveWarm);
                Ball(parent, "Bauble", basePos + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), Vector3.one * 0.13f * s, m);
            }
        }

        /// <summary>Hub decor fireplace against the north wall (z = wallZ), using the custom 3D Fireplace.fbx model.</summary>
        private static void BuildFireplaceNorth(Transform parent, Vector3 wallBase)
        {
            float x = wallBase.x, z = wallBase.z - 0.25f;
            GameObject fp = Model(parent, "LivingRoom/Fireplace.fbx", "FireplaceModel", new Vector3(x, 0f, z), 180f, 1.55f);
            if (fp != null)
            {
                Bounds b = BoundsOf(fp);
                float wallFace = wallBase.z;
                fp.transform.position += new Vector3(0f, 0f, wallFace - b.max.z);
                b = BoundsOf(fp);
                var slab = new GameObject("FireplaceCollider");
                slab.transform.SetParent(parent, false);
                slab.transform.position = b.center;
                slab.AddComponent<BoxCollider>().size = b.size;
                Box(parent, "FireplaceEmbers", new Vector3(x, 0.18f, z - 0.15f), new Vector3(0.65f, 0.14f, 0.25f), ProjectBootstrap.MatEmissiveWarm, false);
                return;
            }
            Box(parent, "FireplaceBreast", new Vector3(x, 2f, z), new Vector3(2.6f, 4f, 0.5f), ProjectBootstrap.MatBrick);
            Box(parent, "FireplaceHole", new Vector3(x, 0.55f, z - 0.27f), new Vector3(1.2f, 1.0f, 0.04f), ProjectBootstrap.MatDark, false);
            Box(parent, "FireplaceMantle", new Vector3(x, 1.25f, z - 0.3f), new Vector3(2.4f, 0.12f, 0.6f), ProjectBootstrap.MatDarkWood);
            Box(parent, "FireplaceEmbers", new Vector3(x, 0.15f, z - 0.35f), new Vector3(0.8f, 0.12f, 0.25f), ProjectBootstrap.MatEmissiveWarm, false);
            Box(parent, "Hearth", new Vector3(x, 0.05f, z - 0.7f), new Vector3(2.6f, 0.1f, 0.9f), ProjectBootstrap.MatBrick);
        }

        /// <summary>House fireplace against the east wall (inner face near x = wallBase.x). Embers are cozy-only.</summary>
        private static void BuildFireplaceEast(Transform parent, Transform moodProps, List<GameObject> cozyOnly, Vector3 wallBase)
        {
            float x = wallBase.x - 0.25f, z = wallBase.z;
            GameObject fp = Model(parent, "LivingRoom/Fireplace.fbx", "FireplaceModel", new Vector3(x, 0f, z), -90f, 2.0f);
            if (fp != null)
            {
                // Back flush against the wall (inner face x = wallBase.x - 0.25 - 0.25).
                Bounds b = BoundsOf(fp);
                float wallFace = wallBase.x - 0.5f;
                fp.transform.position += new Vector3(wallFace - b.max.x, 0f, 0f);
                b = BoundsOf(fp);
                // Collider only on the back slab: the hearth stays open so the chimney exit zone is reachable.
                var slab = new GameObject("FireplaceCollider");
                slab.transform.SetParent(parent, false);
                float depth = b.size.x;
                slab.transform.position = new Vector3(b.max.x - depth * 0.42f, b.center.y, b.center.z);
                slab.AddComponent<BoxCollider>().size = new Vector3(depth * 0.84f, b.size.y, b.size.z);
            }
            else
            {
                Box(parent, "FireplaceBreast", new Vector3(x, 1.5f, z), new Vector3(0.5f, 3f, 2.6f), ProjectBootstrap.MatBrick);
                Box(parent, "FireplaceHole", new Vector3(x - 0.27f, 0.55f, z), new Vector3(0.04f, 1.0f, 1.2f), ProjectBootstrap.MatDark, false);
                Box(parent, "FireplaceColL", new Vector3(x - 0.45f, 0.6f, z - 0.85f), new Vector3(0.4f, 1.2f, 0.4f), ProjectBootstrap.MatBrick);
                Box(parent, "FireplaceColR", new Vector3(x - 0.45f, 0.6f, z + 0.85f), new Vector3(0.4f, 1.2f, 0.4f), ProjectBootstrap.MatBrick);
                Box(parent, "FireplaceMantle", new Vector3(x - 0.45f, 1.26f, z), new Vector3(0.6f, 0.12f, 2.4f), ProjectBootstrap.MatDarkWood);
                Box(parent, "Hearth", new Vector3(x - 0.7f, 0.05f, z), new Vector3(0.9f, 0.1f, 2.6f), ProjectBootstrap.MatBrick);
                for (int i = 0; i < 3; i++)
                    Box(parent, "Stocking", new Vector3(x - 0.78f, 1.0f, z - 0.7f + i * 0.7f), new Vector3(0.06f, 0.35f, 0.18f), ProjectBootstrap.MatRed, false);

            }
            GameObject embers = Box(moodProps, "FireEmbers", new Vector3(x - 0.35f, 0.18f, z), new Vector3(0.25f, 0.15f, 0.8f), ProjectBootstrap.MatEmissiveWarm, false);
            cozyOnly.Add(embers);
        }

        /// <summary>Darker floorboard patch (CreakyWood surface) + a CreakyFloor noise trigger above it.</summary>
        private static void CreakyBoard(Transform parent, Vector3 floorCenter, Vector2 size)
        {
            Patch(parent, "CreakyBoards", floorCenter, size, ProjectBootstrap.MatDarkWood, SurfaceType.CreakyWood);
            NoiseZoneAt(floorCenter + Vector3.up * 0.6f, new Vector3(size.x, 1.2f, size.y), NoiseSource.CreakyFloor, 45f, 1.2f);
        }

        private static void NoiseZoneAt(Vector3 center, Vector3 size, NoiseSource source, float loudness, float cooldown)
        {
            GameObject z = NPCFactory.CreateNoiseZone(center, size, source, loudness, cooldown);
            z.layer = 2; // Ignore Raycast: keeps triggers out of raycasts and the NavMesh bake; triggers still fire
        }

        // ------------------------------------------------------------------ primitives

        private static Material Mat(string key)
        {
            if (_mats != null && _mats.TryGetValue(key, out Material m) && m != null) return m;
            Debug.LogWarning($"[Silent Night] Material '{key}' missing.");
            return null;
        }

        /// <summary>Cube at a world position (parents are never scaled).</summary>
        /// <summary>Trigger volume under furniture where a crouching elf is hidden from Grandpa.</summary>
        private static void HideSpot(Transform parent, string name, Vector3 center, Vector3 size, string label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = size;
            int ignore = LayerMask.NameToLayer("Ignore Raycast");
            if (ignore >= 0) go.layer = ignore;
            go.AddComponent<HidingSpot>().Configure(label, true);
        }

        /// <summary>Small floor toy (visual only) + trigger with a TripHazard.</summary>
        private static void Toy(Transform parent, string name, Vector3 floorPos, float yaw, Vector3 trigger, string mat, string toast)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(floorPos, Quaternion.Euler(0f, yaw, 0f));
            GameObject body = Box(root.transform, name + "Body", floorPos + Vector3.up * 0.06f, new Vector3(trigger.x * 0.7f, 0.1f, trigger.z * 0.6f), mat, false, yaw);
            body.transform.SetParent(root.transform, true);
            Ball(root.transform, name + "Wheel", floorPos + Vector3.up * 0.13f, new Vector3(trigger.x * 0.35f, 0.08f, trigger.z * 0.35f), ProjectBootstrap.MatDark);
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = trigger;
            col.center = new Vector3(0f, trigger.y * 0.5f, 0f);
            int ignore = LayerMask.NameToLayer("Ignore Raycast");
            if (ignore >= 0) root.layer = ignore; // keep it out of the NavMesh and camera collisions
            var hazard = root.AddComponent<SilentNight.Player.TripHazard>();
            if (!string.IsNullOrEmpty(toast))
            {
                var so = new SerializedObject(hazard);
                SerializedProperty p = so.FindProperty("toastText");
                if (p != null) { p.stringValue = toast; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        private const string EnvRoot = "Assets/_Project/Art/Environment/";

        /// <summary>Instantiates a prepared environment FBX (pivot = floor centre). Returns null if the asset is missing.</summary>
        private static GameObject Model(Transform parent, string relPath, string name, Vector3 pos, float yaw, float scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnvRoot + relPath);
            if (asset == null)
            {
                Debug.LogWarning("[Silent Night] Model missing, using greybox: " + relPath);
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        /// <summary>World-space bounds of all renderers under go.</summary>
        private static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.one);
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ------------------------------------------------------------------ CC0 Kenney packs (Furniture Kit, Holiday Kit)

        private const string PackRoot = EnvRoot + "Packs/";
        private const string KF = "KenneyFurniture/";
        private const string KH = "KenneyHoliday/";

        // Yaw that turns a KenneyFurniture model's front (native -Z, checked in the FBX data) towards a world direction.
        // KenneyHoliday figurines (snowman, nutcracker, gingerbread, wreath, hanging lantern) face native +Z: add HolidayYaw.
        private const float FaceS = 0f, FaceN = 180f, FaceE = -90f, FaceW = 90f;
        private const float HolidayYaw = 180f;

        private enum ColliderMode { None, Box, TopSlab }

        private static readonly string[] PresentModels =
        {
            KH + "present-a-cube.fbx", KH + "present-a-rectangle.fbx", KH + "present-a-round.fbx",
            KH + "present-b-cube.fbx", KH + "present-b-rectangle.fbx", KH + "present-b-round.fbx",
        };
        private static readonly string[] BookModels =
            { KF + "books.fbx", KF + "books.fbx", KF + "books.fbx", KF + "plantSmall1.fbx", KF + "radio.fbx" };
        private static readonly string[] GarageShelfModels =
            { KF + "cardboardBoxClosed.fbx", KF + "cardboardBoxOpen.fbx", KF + "cardboardBoxClosed.fbx", KF + "radio.fbx", KF + "books.fbx" };

        /// <summary>bookcaseOpen shelf-board tops as a fraction of the model height (measured from the FBX).</summary>
        private static readonly float[] ShelfLevels = { 0.148f, 0.42f, 0.693f };

        private static bool PackExists(string packRelPath) => AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + packRelPath) != null;

        /// <summary>Instantiates a pack FBX with its pivot at <paramref name="pos"/> (no fitting). Null if missing.</summary>
        private static GameObject PackRaw(Transform parent, string packRelPath, Vector3 pos, float yaw, float scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + packRelPath);
            if (asset == null)
            {
                Debug.LogWarning("[Silent Night] Pack model missing, using greybox: " + packRelPath);
                return null;
            }
            var go = (GameObject)(parent != null ? PrefabUtility.InstantiatePrefab(asset, parent) : PrefabUtility.InstantiatePrefab(asset));
            go.name = System.IO.Path.GetFileNameWithoutExtension(packRelPath);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        /// <summary>Pack model scaled UNIFORMLY (Kenney models have arbitrary native scale): factor = targetSize.y / height,
        /// or (y == 0) targetSize.x / world width, or (x == 0 too) targetSize.z / world depth — world sizes after the yaw.
        /// Renderer bounds bottom goes to floorPos.y and are centred on floorPos in XZ.
        /// Box = one BoxCollider over the bounds; TopSlab = a 6 cm collider at the top surface only (crawl space stays free).
        /// Returns null (and logs a warning) when the asset is missing so callers keep their greybox fallback.</summary>
        private static GameObject PackModel(Transform parent, string packRelPath, Vector3 floorPos, float yaw, Vector3 targetSize, ColliderMode mode)
        {
            return PackCore(parent, packRelPath, floorPos, yaw, mode, size =>
                targetSize.y > 0f ? targetSize.y / Mathf.Max(1e-5f, size.y)
                : targetSize.x > 0f ? targetSize.x / Mathf.Max(1e-5f, size.x)
                : targetSize.z / Mathf.Max(1e-5f, size.z));
        }

        /// <summary>Like PackModel, but the uniform factor makes the model fit INSIDE maxSize (every axis &gt; 0 is a limit).</summary>
        private static GameObject PackFit(Transform parent, string packRelPath, Vector3 floorPos, float yaw, Vector3 maxSize, ColliderMode mode)
        {
            return PackCore(parent, packRelPath, floorPos, yaw, mode, size =>
            {
                float f = float.MaxValue;
                if (maxSize.x > 0f) f = Mathf.Min(f, maxSize.x / Mathf.Max(1e-5f, size.x));
                if (maxSize.y > 0f) f = Mathf.Min(f, maxSize.y / Mathf.Max(1e-5f, size.y));
                if (maxSize.z > 0f) f = Mathf.Min(f, maxSize.z / Mathf.Max(1e-5f, size.z));
                return f == float.MaxValue ? 1f : f;
            });
        }

        private static GameObject PackCore(Transform parent, string packRelPath, Vector3 floorPos, float yaw, ColliderMode mode, System.Func<Vector3, float> factorOf)
        {
            GameObject go = PackRaw(null, packRelPath, Vector3.zero, 0f, 1f);
            if (go == null) return null;
            Transform t = go.transform;
            Bounds lb = BoundsOf(go); // identity transform at the origin: world bounds == local bounds

            float c = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)), s = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad));
            var worldSize = new Vector3(c * lb.size.x + s * lb.size.z, lb.size.y, s * lb.size.x + c * lb.size.z);
            float f = factorOf(worldSize);
            if (float.IsNaN(f) || float.IsInfinity(f) || f <= 0f) f = 1f;

            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 off = q * (lb.center * f);
            t.localScale = Vector3.one * f;
            t.SetPositionAndRotation(new Vector3(floorPos.x - off.x, floorPos.y - lb.min.y * f, floorPos.z - off.z), q);
            if (parent != null) t.SetParent(parent, true);

            if (mode != ColliderMode.None)
            {
                var bc = go.AddComponent<BoxCollider>(); // local space: follows the root's rotation and uniform scale
                if (mode == ColliderMode.Box)
                {
                    bc.center = lb.center;
                    bc.size = lb.size;
                }
                else
                {
                    float slab = Mathf.Min(lb.size.y, 0.06f / f);
                    bc.center = new Vector3(lb.center.x, lb.max.y - slab * 0.5f, lb.center.z);
                    bc.size = new Vector3(lb.size.x, slab, lb.size.z);
                }
            }
            return go;
        }

        /// <summary>Moves go along one world axis so its bounds face in <paramref name="dir"/> sits at <paramref name="coord"/>
        /// (e.g. dir = forward, coord = wall face z: the back of the model touches the north wall).</summary>
        private static void SnapTo(GameObject go, Vector3 dir, float coord)
        {
            if (go == null) return;
            Bounds b = BoundsOf(go);
            Vector3 d = Vector3.zero;
            if (dir.x > 0.5f) d.x = coord - b.max.x; else if (dir.x < -0.5f) d.x = coord - b.min.x;
            if (dir.y > 0.5f) d.y = coord - b.max.y; else if (dir.y < -0.5f) d.y = coord - b.min.y;
            if (dir.z > 0.5f) d.z = coord - b.max.z; else if (dir.z < -0.5f) d.z = coord - b.min.z;
            go.transform.position += d;
        }

        /// <summary>Non-uniform squash of the model's native depth (local Z) to a world depth (cabinets fit the counter collider).</summary>
        private static void FitDepth(GameObject go, float depth, bool depthAlongWorldX)
        {
            if (go == null) return;
            Bounds b = BoundsOf(go);
            float cur = depthAlongWorldX ? b.size.x : b.size.z;
            if (cur < 1e-4f) return;
            Vector3 s = go.transform.localScale;
            s.z *= depth / cur;
            go.transform.localScale = s;
        }

        private static float TopY(GameObject go) => go != null ? BoundsOf(go).max.y : 0f;

        private static void HideRenderer(GameObject go)
        {
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r != null) r.enabled = false;
        }

        /// <summary>Puts small pack models on the three shelves of a bookcaseOpen (and on top), no colliders.</summary>
        private static void FillShelves(Transform parent, GameObject bookcase, string[] items, float itemH, int perShelf, int seed, bool onTop)
        {
            if (bookcase == null || items == null || items.Length == 0 || perShelf < 1) return;
            Bounds b = BoundsOf(bookcase);
            bool alongX = Mathf.Abs(bookcase.transform.right.x) > 0.5f; // native X = bookcase width
            float width = alongX ? b.size.x : b.size.z;
            float depth = alongX ? b.size.z : b.size.x;
            float slot = width * 0.8f / perShelf;
            float yaw = bookcase.transform.eulerAngles.y;
            Vector3 axis = alongX ? Vector3.right : Vector3.forward;
            var rng = new System.Random(seed);
            int levels = ShelfLevels.Length + (onTop ? 1 : 0);
            for (int l = 0; l < levels; l++)
            {
                float y = l < ShelfLevels.Length ? b.min.y + b.size.y * ShelfLevels[l] + 0.003f : b.max.y;
                for (int i = 0; i < perShelf; i++)
                {
                    float u = ((i + 0.5f) / perShelf - 0.5f) * width * 0.8f;
                    Vector3 p = new Vector3(b.center.x, y, b.center.z) + axis * u;
                    float h = itemH * (0.75f + 0.25f * (float)rng.NextDouble());
                    Vector3 max = alongX ? new Vector3(slot * 0.9f, h, depth * 0.75f) : new Vector3(depth * 0.75f, h, slot * 0.9f);
                    PackFit(parent, items[rng.Next(items.Length)], p, yaw + rng.Next(-12, 13), max, ColliderMode.None);
                }
            }
        }

        /// <summary>Wrapped presents on the floor in a ring around a point (decor, no colliders). Skips spots with |x| or |z| &gt; limit.</summary>
        private static void PresentsAround(Transform parent, Vector3 center, float rMin, float rMax, int count, int seed, float hMin, float hMax, float limit)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.6f) / count * Mathf.PI * 2f;
                float r = Mathf.Lerp(rMin, rMax, (float)rng.NextDouble());
                Vector3 p = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (Mathf.Abs(p.x) > limit || Mathf.Abs(p.z) > limit) continue;
                float h = Mathf.Lerp(hMin, hMax, (float)rng.NextDouble());
                if (PackFit(parent, PresentModels[rng.Next(PresentModels.Length)], p, rng.Next(0, 360), new Vector3(h * 1.6f, h, h * 1.6f), ColliderMode.None) == null) return;
            }
        }

        /// <summary>Strands of coloured fairy lights (lights-colored, native strand along X) between two points on a wall,
        /// strand tops at topY. Decor only: no colliders.</summary>
        private static void LightStrand(Transform parent, Vector3 from, Vector3 to, float topY)
        {
            Vector3 d = to - from;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.1f) return;
            bool alongX = Mathf.Abs(d.x) >= Mathf.Abs(d.z);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.5f));
            float seg = len / n;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = from + d * ((i + 0.5f) / n);
                Vector3 max = alongX ? new Vector3(seg, 10f, 10f) : new Vector3(10f, 10f, seg);
                GameObject s = PackFit(parent, KH + "lights-colored.fbx", new Vector3(c.x, 0f, c.z), alongX ? 0f : 90f, max, ColliderMode.None);
                if (s == null) return;
                SnapTo(s, Vector3.up, topY);
            }
        }

        /// <summary>Static toy train set: a 4 x 2 tile loop of Kenney trainset rails with a locomotive and a wagon (decor).</summary>
        private static void TrainLoop(Transform parent, Vector3 center, float tile)
        {
            Vector3 P(int c, int r) => new Vector3(center.x + (c - 1.5f) * tile, center.y, center.z + (r - 0.5f) * tile);

            GameObject first = PackRaw(parent, KH + "trainset-rail-straight.fbx", P(1, 0), 0f, 1f);
            if (first == null) return;
            Bounds nb = BoundsOf(first);
            float s = tile / Mathf.Max(1e-4f, Mathf.Max(nb.size.x, nb.size.z)); // straight piece = one tile long
            first.transform.localScale = Vector3.one * s;
            PackRaw(parent, KH + "trainset-rail-straight.fbx", P(2, 0), 0f, s);
            PackRaw(parent, KH + "trainset-rail-straight.fbx", P(1, 1), 0f, s);
            PackRaw(parent, KH + "trainset-rail-straight.fbx", P(2, 1), 0f, s);
            RailCorner(parent, P(0, 0), s, 1, 1);   // SW: joins east + north
            RailCorner(parent, P(3, 0), s, -1, 1);  // SE: joins west + north
            RailCorner(parent, P(0, 1), s, 1, -1);  // NW: joins east + south
            RailCorner(parent, P(3, 1), s, -1, -1); // NE: joins west + south
            PackRaw(parent, KH + "train-locomotive.fbx", new Vector3(center.x, center.y, P(0, 0).z), 0f, s);
            PackRaw(parent, KH + "train-wagon.fbx", new Vector3(center.x, center.y, P(0, 1).z), 180f, s);
        }

        /// <summary>Corner rail whose arc joins the tile sides (sx, sz): tries the four 90° yaws and keeps the one whose
        /// bounds centre lies towards those sides (robust to the importer's axis conversion).</summary>
        private static void RailCorner(Transform parent, Vector3 tileCenter, float scale, int sx, int sz)
        {
            GameObject go = PackRaw(parent, KH + "trainset-rail-corner.fbx", tileCenter, 0f, scale);
            if (go == null) return;
            for (int k = 0; k < 4; k++)
            {
                go.transform.rotation = Quaternion.Euler(0f, k * 90f, 0f);
                Vector3 d = BoundsOf(go).center - tileCenter;
                if (Mathf.Sign(d.x) == sx && Mathf.Sign(d.z) == sz) return;
            }
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, string mat, bool collider = true, float yaw = 0f)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.position = center;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            Material m = Mat(mat);
            if (m != null) go.GetComponent<MeshRenderer>().sharedMaterial = m;
            ApplyTexture(go.GetComponent<MeshRenderer>(), m, mat, size);
            return go;
        }

        // ---- optional tiling textures: drop <MaterialKey>.png (Wall, Wood, DarkWood, Carpet, Tile, Concrete, Snow, Brick) into
        // Assets/_Project/Art/Environment/Textures/ and rebuild; they are applied with world-size tiling.
        private const string TexRoot = "Assets/_Project/Art/Environment/Textures/";
        private static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        private static float TileMeters(string key)
        {
            switch (key)
            {
                case ProjectBootstrap.MatTile: return 1.8f;
                case ProjectBootstrap.MatSnow: return 4f;
                case ProjectBootstrap.MatWall:
                case ProjectBootstrap.MatWallBurgundy:
                case ProjectBootstrap.MatWallSpruce:
                case ProjectBootstrap.MatWallNavy:
                case ProjectBootstrap.MatWallOchre: return 2.8f;
                case ProjectBootstrap.MatCarpet: return 2.4f;
                case ProjectBootstrap.MatConcrete: return 3f;
                case ProjectBootstrap.MatBrick: return 2.2f;
                case ProjectBootstrap.MatPainting0:
                case ProjectBootstrap.MatPainting1:
                case ProjectBootstrap.MatPainting2:
                case ProjectBootstrap.MatPainting3:
                case ProjectBootstrap.MatEmissiveMoon: return 0f; // 1:1 non-tiled
                default: return 2.4f;
            }
        }

        private static void ApplyTexture(MeshRenderer r, Material m, string key, Vector3 size)
        {
            if (r == null || m == null) return;
            float t = TileMeters(key);
            if (t <= 0f) return;

            if (!_texCache.TryGetValue(key, out Texture2D tex))
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexRoot + key + ".png");
                _texCache[key] = tex;
                if (tex != null)
                {
                    m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", Color.white);
                    EditorUtility.SetDirty(m);
                }
            }
            if (tex == null) return;

            bool flat = size.y < 0.4f;
            Vector2 tiles = flat ? new Vector2(size.x / t, size.z / t) : new Vector2(Mathf.Max(size.x, size.z) / t, size.y / t);
            Vector2 clamped = new Vector2(Mathf.Max(0.2f, tiles.x), Mathf.Max(0.2f, tiles.y));

            var tiler = r.gameObject.GetComponent<WorldUvTiling>();
            if (tiler == null) tiler = r.gameObject.AddComponent<WorldUvTiling>();
            tiler.Configure(clamped);
        }

        /// <summary>Decorative sphere without collider.</summary>
        private static GameObject Ball(Transform parent, string name, Vector3 center, Vector3 size, string mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.position = center;
            t.localScale = size;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Material m = Mat(mat);
            if (m != null) go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>Cylinder (disc, trunk). The capsule collider is replaced by a box (flat capsules are useless).</summary>
        private static GameObject Disc(Transform parent, string name, Vector3 center, float diameter, float height, string mat, bool collider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.position = center;
            t.localScale = new Vector3(diameter, height * 0.5f, diameter); // primitive cylinder is 2 units tall
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (collider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = Vector3.zero;
                bc.size = new Vector3(1f, 2f, 1f);
            }
            Material m = Mat(mat);
            if (m != null) go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>Room floor slab with its top at y = 0.</summary>
        private static GameObject Floor(Transform parent, string name, float x0, float x1, float z0, float z1, string mat, SurfaceType surface)
        {
            GameObject go = Box(parent, name, new Vector3((x0 + x1) * 0.5f, -0.1f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.2f, z1 - z0), mat);
            go.AddComponent<SurfaceTag>().Surface = surface;
            return go;
        }

        /// <summary>Thin floor overlay (rug, creaky boards) 2 cm high with its own surface.</summary>
        private static GameObject Patch(Transform parent, string name, Vector3 floorCenter, Vector2 size, string mat, SurfaceType surface)
        {
            GameObject go = Box(parent, name, new Vector3(floorCenter.x, 0.01f, floorCenter.z), new Vector3(size.x, 0.02f, size.y), mat);
            go.AddComponent<SurfaceTag>().Surface = surface;
            return go;
        }

        /// <summary>Wall running along X at the given z, with doorway gaps (door centres on X) and wooden door frames.</summary>
        private static void WallAlongX(Transform parent, string name, float z, float x0, float x1, float h, float thick, string mat, params float[] doors)
        {
            var list = new List<float>(doors); list.Sort();
            float cur = x0; int i = 0;
            foreach (float c in list)
            {
                float a = c - DoorW * 0.5f, b = c + DoorW * 0.5f;
                if (a - cur > 0.01f) Box(parent, $"{name}_{i++}", new Vector3((cur + a) * 0.5f, h * 0.5f, z), new Vector3(a - cur, h, thick), mat);
                if (h > DoorH) Box(parent, $"{name}_Lintel{i++}", new Vector3(c, (DoorH + h) * 0.5f, z), new Vector3(DoorW, h - DoorH, thick), mat);
                // Wooden door architrave (visual only: no colliders so doorways remain 1.4m wide)
                Box(parent, $"{name}_FrameTop", new Vector3(c, DoorH + 0.05f, z), new Vector3(DoorW + 0.18f, 0.10f, thick + 0.06f), ProjectBootstrap.MatDarkWood, false);
                Box(parent, $"{name}_FrameL", new Vector3(a - 0.04f, DoorH * 0.5f, z), new Vector3(0.08f, DoorH, thick + 0.05f), ProjectBootstrap.MatDarkWood, false);
                Box(parent, $"{name}_FrameR", new Vector3(b + 0.04f, DoorH * 0.5f, z), new Vector3(0.08f, DoorH, thick + 0.05f), ProjectBootstrap.MatDarkWood, false);
                cur = b;
            }
            if (x1 - cur > 0.01f) Box(parent, $"{name}_{i}", new Vector3((cur + x1) * 0.5f, h * 0.5f, z), new Vector3(x1 - cur, h, thick), mat);
        }

        /// <summary>Wall running along Z at the given x, with doorway gaps (door centres on Z) and wooden door frames.</summary>
        private static void WallAlongZ(Transform parent, string name, float x, float z0, float z1, float h, float thick, string mat, params float[] doors)
        {
            var list = new List<float>(doors); list.Sort();
            float cur = z0; int i = 0;
            foreach (float c in list)
            {
                float a = c - DoorW * 0.5f, b = c + DoorW * 0.5f;
                if (a - cur > 0.01f) Box(parent, $"{name}_{i++}", new Vector3(x, h * 0.5f, (cur + a) * 0.5f), new Vector3(thick, h, a - cur), mat);
                if (h > DoorH) Box(parent, $"{name}_Lintel{i++}", new Vector3(x, (DoorH + h) * 0.5f, c), new Vector3(thick, h - DoorH, DoorW), mat);
                // Wooden door architrave (visual only: no colliders so doorways remain 1.4m wide)
                Box(parent, $"{name}_FrameTop", new Vector3(x, DoorH + 0.05f, c), new Vector3(thick + 0.06f, 0.10f, DoorW + 0.18f), ProjectBootstrap.MatDarkWood, false);
                Box(parent, $"{name}_FrameL", new Vector3(x, DoorH * 0.5f, a - 0.04f), new Vector3(thick + 0.05f, DoorH, 0.08f), ProjectBootstrap.MatDarkWood, false);
                Box(parent, $"{name}_FrameR", new Vector3(x, DoorH * 0.5f, b + 0.04f), new Vector3(thick + 0.05f, DoorH, 0.08f), ProjectBootstrap.MatDarkWood, false);
                cur = b;
            }
            if (z1 - cur > 0.01f) Box(parent, $"{name}_{i}", new Vector3(x, h * 0.5f, (cur + z1) * 0.5f), new Vector3(thick, h, z1 - cur), mat);
        }

        // ------------------------------------------------------------------ Architectural & Tactile Prop Builders

        /// <summary>Builds wainscoting panels (1.15m), oak/brass chair rail, baseboard, and cream crown molding along X.</summary>
        private static void BuildWallTrimX(Transform parent, string name, float zFace, float x0, float x1, float wallH, float normalZ)
        {
            float len = x1 - x0;
            float cx = (x0 + x1) * 0.5f;
            Box(parent, name + "_Wainscot", new Vector3(cx, 0.575f, zFace), new Vector3(len, 1.15f, 0.04f), ProjectBootstrap.MatDarkWood, false);
            Box(parent, name + "_ChairRail", new Vector3(cx, 1.17f, zFace + normalZ * 0.015f), new Vector3(len, 0.07f, 0.07f), ProjectBootstrap.MatWood, false);
            Box(parent, name + "_BrassInlay", new Vector3(cx, 1.215f, zFace + normalZ * 0.02f), new Vector3(len, 0.02f, 0.06f), ProjectBootstrap.MatBrass, false);
            Box(parent, name + "_Baseboard", new Vector3(cx, 0.08f, zFace + normalZ * 0.015f), new Vector3(len, 0.16f, 0.06f), ProjectBootstrap.MatCreamTrim, false);
            Box(parent, name + "_Crown", new Vector3(cx, wallH - 0.07f, zFace + normalZ * 0.02f), new Vector3(len, 0.14f, 0.08f), ProjectBootstrap.MatCreamTrim, false);
        }

        /// <summary>Builds wainscoting panels (1.15m), oak/brass chair rail, baseboard, and cream crown molding along Z.</summary>
        private static void BuildWallTrimZ(Transform parent, string name, float xFace, float z0, float z1, float wallH, float normalX)
        {
            float len = z1 - z0;
            float cz = (z0 + z1) * 0.5f;
            Box(parent, name + "_Wainscot", new Vector3(xFace, 0.575f, cz), new Vector3(0.04f, 1.15f, len), ProjectBootstrap.MatDarkWood, false);
            Box(parent, name + "_ChairRail", new Vector3(xFace + normalX * 0.015f, 1.17f, cz), new Vector3(0.07f, 0.07f, len), ProjectBootstrap.MatWood, false);
            Box(parent, name + "_BrassInlay", new Vector3(xFace + normalX * 0.02f, 1.215f, cz), new Vector3(0.06f, 0.02f, len), ProjectBootstrap.MatBrass, false);
            Box(parent, name + "_Baseboard", new Vector3(xFace + normalX * 0.015f, 0.08f, cz), new Vector3(0.06f, 0.16f, len), ProjectBootstrap.MatCreamTrim, false);
            Box(parent, name + "_Crown", new Vector3(xFace + normalX * 0.02f, wallH - 0.07f, cz), new Vector3(0.08f, 0.14f, len), ProjectBootstrap.MatCreamTrim, false);
        }

        /// <summary>Nordic knitted Christmas rug with cream/gold trim border (visual trim is colliderless).</summary>
        private static GameObject BuildNordicRug(Transform parent, string name, Vector3 floorCenter, Vector2 size)
        {
            GameObject rug = Patch(parent, name, floorCenter, size, ProjectBootstrap.MatCarpet, SurfaceType.Carpet);
            float y = floorCenter.y + 0.012f;
            float b = 0.08f;
            Box(parent, name + "_TrimN", new Vector3(floorCenter.x, y, floorCenter.z + size.y * 0.5f - b * 0.5f), new Vector3(size.x, 0.018f, b), ProjectBootstrap.MatGold, false);
            Box(parent, name + "_TrimS", new Vector3(floorCenter.x, y, floorCenter.z - size.y * 0.5f + b * 0.5f), new Vector3(size.x, 0.018f, b), ProjectBootstrap.MatGold, false);
            Box(parent, name + "_TrimW", new Vector3(floorCenter.x - size.x * 0.5f + b * 0.5f, y, floorCenter.z), new Vector3(b, 0.018f, size.y), ProjectBootstrap.MatGold, false);
            Box(parent, name + "_TrimE", new Vector3(floorCenter.x + size.x * 0.5f - b * 0.5f, y, floorCenter.z), new Vector3(b, 0.018f, size.y), ProjectBootstrap.MatGold, false);
            return rug;
        }

        /// <summary>Glowing moonlit winter window with cross muntins, snow sill, brass curtain rod, and velvet drapes.</summary>
        private static void BuildWindow(Transform parent, string name, Vector3 wallCenter, float yaw, float width, float height, string curtainMat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(wallCenter, Quaternion.Euler(0f, yaw, 0f));

            void LocalBox(string part, Vector3 lp, Vector3 sz, string mat)
            {
                GameObject go = Box(root, part, Vector3.zero, sz, mat, false);
                go.transform.localPosition = lp;
                go.transform.localRotation = Quaternion.identity;
            }

            // Outer frame + glowing moonlit glass (local -Z faces into the room)
            LocalBox("Frame", new Vector3(0f, 0f, 0f), new Vector3(width + 0.16f, height + 0.16f, 0.05f), ProjectBootstrap.MatCreamTrim);
            LocalBox("Glass", new Vector3(0f, 0f, -0.028f), new Vector3(width, height, 0.02f), ProjectBootstrap.MatEmissiveMoon);
            // Cross muntins in front of glass (-Z)
            LocalBox("MuntinV", new Vector3(0f, 0f, -0.042f), new Vector3(0.055f, height, 0.03f), ProjectBootstrap.MatCreamTrim);
            LocalBox("MuntinH1", new Vector3(0f, height * 0.16f, -0.042f), new Vector3(width, 0.055f, 0.03f), ProjectBootstrap.MatCreamTrim);
            LocalBox("MuntinH2", new Vector3(0f, -height * 0.18f, -0.042f), new Vector3(width, 0.055f, 0.03f), ProjectBootstrap.MatCreamTrim);
            // Snow drift on sill + wooden sill
            LocalBox("Sill", new Vector3(0f, -height * 0.5f - 0.06f, -0.06f), new Vector3(width + 0.28f, 0.08f, 0.16f), ProjectBootstrap.MatCreamTrim);
            LocalBox("SnowDrift", new Vector3(0f, -height * 0.5f + 0.04f, -0.045f), new Vector3(width * 0.90f, 0.09f, 0.05f), ProjectBootstrap.MatSnow);
            // Brass curtain rod + velvet curtains
            LocalBox("CurtainRod", new Vector3(0f, height * 0.5f + 0.14f, -0.07f), new Vector3(width + 0.48f, 0.04f, 0.04f), ProjectBootstrap.MatBrass);
            LocalBox("CurtainL", new Vector3(-width * 0.5f - 0.06f, -0.05f, -0.07f), new Vector3(0.28f, height + 0.25f, 0.07f), curtainMat);
            LocalBox("CurtainR", new Vector3(width * 0.5f + 0.06f, -0.05f, -0.07f), new Vector3(0.28f, height + 0.25f, 0.07f), curtainMat);
        }

        /// <summary>Framed winter night painting with ornate gold/wood frame and brass picture light.</summary>
        private static void BuildFramedPainting(Transform parent, string name, Vector3 wallCenter, float yaw, float width, float height, string paintingMat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(wallCenter, Quaternion.Euler(0f, yaw, 0f));

            void LocalBox(string part, Vector3 lp, Vector3 sz, string mat)
            {
                GameObject go = Box(root, part, Vector3.zero, sz, mat, false);
                go.transform.localPosition = lp;
                go.transform.localRotation = Quaternion.identity;
            }

            LocalBox("OuterFrame", new Vector3(0f, 0f, 0f), new Vector3(width + 0.14f, height + 0.14f, 0.05f), ProjectBootstrap.MatGold);
            LocalBox("InnerMatte", new Vector3(0f, 0f, -0.015f), new Vector3(width + 0.04f, height + 0.04f, 0.04f), ProjectBootstrap.MatDarkWood);
            LocalBox("Canvas", new Vector3(0f, 0f, -0.03f), new Vector3(width, height, 0.02f), paintingMat);
            LocalBox("PictureLamp", new Vector3(0f, height * 0.5f + 0.12f, -0.06f), new Vector3(width * 0.45f, 0.04f, 0.08f), ProjectBootstrap.MatBrass);
        }

        /// <summary>Sculpted brass wall sconce with warm glowing lampshade.</summary>
        private static void BuildBrassSconce(Transform parent, Vector3 wallCenter, float yaw)
        {
            var root = new GameObject("BrassSconce").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(wallCenter, Quaternion.Euler(0f, yaw, 0f));

            void LocalBox(string part, Vector3 lp, Vector3 sz, string mat)
            {
                GameObject go = Box(root, part, Vector3.zero, sz, mat, false);
                go.transform.localPosition = lp;
                go.transform.localRotation = Quaternion.identity;
            }

            LocalBox("Backplate", new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.26f, 0.03f), ProjectBootstrap.MatBrass);
            LocalBox("Arm", new Vector3(0f, -0.04f, -0.10f), new Vector3(0.04f, 0.04f, 0.18f), ProjectBootstrap.MatBrass);
            GameObject shade = Disc(root, "Shade", Vector3.zero, 0.24f, 0.20f, ProjectBootstrap.MatEmissiveWarm, false);
            shade.transform.localPosition = new Vector3(0f, 0.08f, -0.16f);
        }

        /// <summary>Sculpted brass ceiling chandelier with 4 warm glowing candle bulbs.</summary>
        private static void BuildBrassChandelier(Transform parent, Vector3 bulbCenter, float ceilingH)
        {
            float cordLen = Mathf.Max(0.2f, ceilingH - bulbCenter.y);
            Disc(parent, "ChandelierRose", new Vector3(bulbCenter.x, ceilingH - 0.02f, bulbCenter.z), 0.32f, 0.04f, ProjectBootstrap.MatBrass, false);
            Box(parent, "ChandelierRod", new Vector3(bulbCenter.x, bulbCenter.y + cordLen * 0.5f, bulbCenter.z), new Vector3(0.04f, cordLen, 0.04f), ProjectBootstrap.MatBrass, false);
            Disc(parent, "ChandelierRing", bulbCenter, 0.85f, 0.06f, ProjectBootstrap.MatBrass, false);
            Box(parent, "ChandelierCrossX", bulbCenter, new Vector3(0.95f, 0.04f, 0.05f), ProjectBootstrap.MatBrass, false);
            Box(parent, "ChandelierCrossZ", bulbCenter, new Vector3(0.05f, 0.04f, 0.95f), ProjectBootstrap.MatBrass, false);

            Vector3[] offsets = { new Vector3(-0.42f, 0.12f, 0f), new Vector3(0.42f, 0.12f, 0f), new Vector3(0f, 0.12f, -0.42f), new Vector3(0f, 0.12f, 0.42f) };
            foreach (Vector3 off in offsets)
            {
                Disc(parent, "CandleCup", bulbCenter + new Vector3(off.x, 0.04f, off.z), 0.14f, 0.04f, ProjectBootstrap.MatBrass, false);
                Ball(parent, "CandleGlow", bulbCenter + off, new Vector3(0.16f, 0.22f, 0.16f), ProjectBootstrap.MatEmissiveWarm);
            }
        }

        /// <summary>Sculpted Santa's Sleigh with curved golden runners, brass trim, velvet seat, and gift sack.</summary>
        private static void BuildDetailedSleigh(Transform parent, Vector3 center, float s = 1f)
        {
            GameObject sleighMesh = Model(parent, "Hub/Sleigh.fbx", "SantaSleigh", center, 90f, s * 1.05f);
            if (sleighMesh != null)
            {
                var col = sleighMesh.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.65f, 0f);
                col.size = new Vector3(2.85f, 1.25f, 1.65f);
                return;
            }

            foreach (float sx in new[] { -0.78f * s, 0.78f * s })
            {
                Box(parent, "RunnerBlade", center + new Vector3(sx, 0.06f * s, 0f), new Vector3(0.10f * s, 0.12f * s, 3.6f * s), ProjectBootstrap.MatBrass);
                Box(parent, "RunnerCurl", center + new Vector3(sx, 0.32f * s, 1.75f * s), new Vector3(0.10f * s, 0.45f * s, 0.22f * s), ProjectBootstrap.MatBrass, false);
                Box(parent, "RunnerStrutF", center + new Vector3(sx, 0.25f * s, 0.95f * s), new Vector3(0.08f * s, 0.30f * s, 0.10f * s), ProjectBootstrap.MatBrass, false);
                Box(parent, "RunnerStrutB", center + new Vector3(sx, 0.25f * s, -0.95f * s), new Vector3(0.08f * s, 0.30f * s, 0.10f * s), ProjectBootstrap.MatBrass, false);
            }

            Box(parent, "SleighFloor", center + new Vector3(0f, 0.42f * s, 0f), new Vector3(1.72f * s, 0.16f * s, 2.85f * s), ProjectBootstrap.MatDarkWood);
            Box(parent, "SleighSideL", center + new Vector3(-0.82f * s, 0.78f * s, 0f), new Vector3(0.12f * s, 0.68f * s, 2.75f * s), ProjectBootstrap.MatRed);
            Box(parent, "SleighSideR", center + new Vector3(0.82f * s, 0.78f * s, 0f), new Vector3(0.12f * s, 0.68f * s, 2.75f * s), ProjectBootstrap.MatRed);
            Box(parent, "SleighTrimL", center + new Vector3(-0.84f * s, 1.13f * s, 0f), new Vector3(0.15f * s, 0.06f * s, 2.80f * s), ProjectBootstrap.MatBrass, false);
            Box(parent, "SleighTrimR", center + new Vector3(0.84f * s, 1.13f * s, 0f), new Vector3(0.15f * s, 0.06f * s, 2.80f * s), ProjectBootstrap.MatBrass, false);
            Box(parent, "SleighNose", center + new Vector3(0f, 0.75f * s, 1.42f * s), new Vector3(1.75f * s, 0.65f * s, 0.22f * s), ProjectBootstrap.MatRed);
            Box(parent, "SleighNoseTrim", center + new Vector3(0f, 1.08f * s, 1.44f * s), new Vector3(1.80f * s, 0.08f * s, 0.26f * s), ProjectBootstrap.MatBrass, false);
            Box(parent, "SleighBack", center + new Vector3(0f, 1.02f * s, -1.32f * s), new Vector3(1.75f * s, 0.95f * s, 0.24f * s), ProjectBootstrap.MatRed);
            Box(parent, "SeatCushion", center + new Vector3(0f, 0.62f * s, -0.35f * s), new Vector3(1.52f * s, 0.22f * s, 0.85f * s), ProjectBootstrap.MatVelvetGreen, false);
            Ball(parent, "GiftSack", center + new Vector3(0f, 1.05f * s, -0.78f * s), new Vector3(1.25f * s, 0.88f * s, 0.95f * s), ProjectBootstrap.MatVelvetRed);
            Ball(parent, "SleighLanternL", center + new Vector3(-0.92f * s, 1.05f * s, 1.35f * s), Vector3.one * 0.20f * s, ProjectBootstrap.MatEmissiveWarm);
            Ball(parent, "SleighLanternR", center + new Vector3(0.92f * s, 1.05f * s, 1.35f * s), Vector3.one * 0.20f * s, ProjectBootstrap.MatEmissiveWarm);
        }

        /// <summary>Detailed family station wagon in the garage with roof rack, windows, bumpers, and headlights.</summary>
        private static void BuildGarageCar(Transform parent, Vector3 floorCenter, float s = 1f)
        {
            Box(parent, "CarBody", floorCenter + new Vector3(0f, 0.58f * s, 0f), new Vector3(1.82f * s, 0.76f * s, 3.85f * s), ProjectBootstrap.MatRed);
            Box(parent, "CarCabin", floorCenter + new Vector3(0f, 1.22f * s, -0.22f * s), new Vector3(1.58f * s, 0.58f * s, 2.15f * s), ProjectBootstrap.MatRed);
            Box(parent, "WindshieldF", floorCenter + new Vector3(0f, 1.22f * s, 0.86f * s), new Vector3(1.44f * s, 0.44f * s, 0.06f * s), ProjectBootstrap.MatEmissiveMoon, false);
            Box(parent, "WindshieldB", floorCenter + new Vector3(0f, 1.22f * s, -1.30f * s), new Vector3(1.44f * s, 0.44f * s, 0.06f * s), ProjectBootstrap.MatEmissiveMoon, false);
            Box(parent, "SideWindows", floorCenter + new Vector3(0f, 1.22f * s, -0.22f * s), new Vector3(1.62f * s, 0.42f * s, 1.85f * s), ProjectBootstrap.MatDark, false);
            Box(parent, "BumperF", floorCenter + new Vector3(0f, 0.32f * s, 1.96f * s), new Vector3(1.88f * s, 0.16f * s, 0.14f * s), ProjectBootstrap.MatMetal, false);
            Box(parent, "BumperB", floorCenter + new Vector3(0f, 0.32f * s, -1.96f * s), new Vector3(1.88f * s, 0.16f * s, 0.14f * s), ProjectBootstrap.MatMetal, false);
            Box(parent, "RoofRack", floorCenter + new Vector3(0f, 1.54f * s, -0.22f * s), new Vector3(1.35f * s, 0.06f * s, 1.65f * s), ProjectBootstrap.MatMetal, false);
            foreach (float sx in new[] { -0.62f * s, 0.62f * s })
            {
                Ball(parent, "Headlight", floorCenter + new Vector3(sx, 0.62f * s, 1.94f * s), new Vector3(0.24f * s, 0.22f * s, 0.08f * s), ProjectBootstrap.MatEmissiveWarm);
                Box(parent, "Taillight", floorCenter + new Vector3(sx, 0.62f * s, -1.94f * s), new Vector3(0.26f * s, 0.16f * s, 0.06f * s), ProjectBootstrap.MatRed, false);
            }
            foreach (float dx in new[] { -0.92f * s, 0.92f * s })
                foreach (float dz in new[] { -1.25f * s, 1.25f * s })
                {
                    Box(parent, "Wheel", floorCenter + new Vector3(dx, 0.32f * s, dz), new Vector3(0.22f * s, 0.64f * s, 0.64f * s), ProjectBootstrap.MatDark, false);
                    Box(parent, "Hubcap", floorCenter + new Vector3(dx * 1.06f, 0.32f * s, dz), new Vector3(0.06f * s, 0.34f * s, 0.34f * s), ProjectBootstrap.MatMetal, false);
                }
        }

        private static Light Point(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            return l;
        }

        // ------------------------------------------------------------------ static flags & serialized refs

        private const StaticEditorFlags EnvFlags =
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;

        /// <summary>Marks root + all children static (no ContributeGI: there are no lightmaps).</summary>
        private static void MakeStatic(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<Light>() == null) GameObjectUtility.SetStaticEditorFlags(t.gameObject, EnvFlags);
        }

        /// <summary>Static visuals under a (non-static) station root.</summary>
        private static void MakeStaticChildren(GameObject root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root.transform) GameObjectUtility.SetStaticEditorFlags(t.gameObject, EnvFlags);
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { Debug.LogError($"[Silent Night] {target.GetType().Name}.{field} not found."); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, List<Object> values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null || !p.isArray) { Debug.LogError($"[Silent Night] {target.GetType().Name}.{field} not found."); return; }
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
