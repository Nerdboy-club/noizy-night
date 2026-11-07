#if UNITY_EDITOR
using System.Collections.Generic;
using SilentNight.Audio;
using SilentNight.Core;
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
        private const float DoorH = 2.2f;

        private static Dictionary<string, Material> _mats;
        private static VolumeProfile _profile;

        // ------------------------------------------------------------------ menu

        [MenuItem("Silent Night/Build Greybox Scenes", priority = 10)]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ProjectBootstrap.EnsureTagsAndLayers();
            if (GraphicsSettings.defaultRenderPipeline == null) ProjectBootstrap.EnsureRenderPipeline();
            _mats = ProjectBootstrap.EnsureMaterials();
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
                new Color(0.45f, 0.36f, 0.30f), new Color(0.32f, 0.25f, 0.20f), new Color(0.14f, 0.10f, 0.08f),
                true, new Color(0.20f, 0.15f, 0.13f), 0.008f);
            CreateCamera(new Vector3(0f, 2.5f, -7f), Quaternion.Euler(12f, 0f, 0f));
            CreateMoonlight(0.35f, Quaternion.Euler(55f, -35f, 0f));
            CreateGlobalVolume();

            Transform env = new GameObject("Environment").transform;

            // Floor, rug, snow outside.
            Floor(env, "Floor", -8f, 8f, -8f, 8f, ProjectBootstrap.MatWood, SurfaceType.Wood);
            Patch(env, "Rug", new Vector3(0f, 0f, 0.5f), new Vector2(6f, 6f), ProjectBootstrap.MatCarpet, SurfaceType.Carpet);
            Box(env, "SnowGround", new Vector3(0f, -0.25f, 0f), new Vector3(80f, 0.3f, 80f), ProjectBootstrap.MatSnow);

            // Walls (5 m brick) + dark wainscot.
            const float h = 4f, t = 0.4f;
            WallAlongX(env, "WallN", 8f, -8.2f, 8.2f, h, t, ProjectBootstrap.MatBrick);
            WallAlongX(env, "WallS", -8f, -8.2f, 8.2f, h, t, ProjectBootstrap.MatBrick);
            WallAlongZ(env, "WallW", -8f, -8f, 8f, h, t, ProjectBootstrap.MatBrick);
            WallAlongZ(env, "WallE", 8f, -8f, 8f, h, t, ProjectBootstrap.MatBrick);
            Box(env, "WainscotN", new Vector3(0f, 0.575f, 7.77f), new Vector3(15.6f, 1.15f, 0.06f), ProjectBootstrap.MatDarkWood, false);
            Box(env, "WainscotS", new Vector3(0f, 0.575f, -7.77f), new Vector3(15.6f, 1.15f, 0.06f), ProjectBootstrap.MatDarkWood, false);
            Box(env, "WainscotW", new Vector3(-7.77f, 0.575f, 0f), new Vector3(0.06f, 1.15f, 15.6f), ProjectBootstrap.MatDarkWood, false);
            Box(env, "WainscotE", new Vector3(7.77f, 0.575f, 0f), new Vector3(0.06f, 1.15f, 15.6f), ProjectBootstrap.MatDarkWood, false);

            // Decor: shelves with parcels, crates, a tree, a fireplace, hanging lanterns.
            for (int i = 0; i < 2; i++)
            {
                float z = -3.6f + i * 3.4f;
                Box(env, "ShelfW" + i, new Vector3(-7.45f, 1.2f, z), new Vector3(0.5f, 2.4f, 3f), ProjectBootstrap.MatWood);
                Box(env, "ShelfE" + i, new Vector3(7.45f, 1.2f, z), new Vector3(0.5f, 2.4f, 3f), ProjectBootstrap.MatWood);
                for (int k = 0; k < 3; k++)
                {
                    string m = k == 0 ? ProjectBootstrap.MatRed : (k == 1 ? ProjectBootstrap.MatGreen : ProjectBootstrap.MatGold);
                    Box(env, "ParcelW", new Vector3(-7.1f, 2.55f, z - 1f + k), new Vector3(0.35f, 0.3f, 0.45f), m, false);
                    Box(env, "ParcelE", new Vector3(7.1f, 2.55f, z - 1f + k), new Vector3(0.35f, 0.3f, 0.45f), m, false);
                }
            }
            Box(env, "Crate1", new Vector3(-6.2f, 0.4f, -6.4f), new Vector3(0.8f, 0.8f, 0.8f), ProjectBootstrap.MatWood);
            Box(env, "Crate2", new Vector3(-5.3f, 0.35f, -6.5f), new Vector3(0.7f, 0.7f, 0.7f), ProjectBootstrap.MatWood, true, 20f);
            Box(env, "Crate3", new Vector3(-6.1f, 1.15f, -6.4f), new Vector3(0.6f, 0.7f, 0.6f), ProjectBootstrap.MatRed, true, 10f);
            Box(env, "Crate4", new Vector3(6.3f, 0.4f, -6.4f), new Vector3(0.8f, 0.8f, 0.8f), ProjectBootstrap.MatGreen);
            BuildTree(env, new Vector3(6f, 0f, 6f), 1.0f);
            BuildFireplaceNorth(env, new Vector3(-5f, 0f, 7.8f)); // emissive embers only: hub keeps max 3 point lights
            float[] lx = { -2.5f, 2.5f }, lz = { -2f, 2.5f };
            foreach (float x in lx)
                foreach (float z in lz)
                {
                    Box(env, "LanternCord", new Vector3(x, 3.4f, z), new Vector3(0.02f, 1.2f, 0.02f), ProjectBootstrap.MatDark, false);
                    Ball(env, "Lantern", new Vector3(x, 2.8f, z), Vector3.one * 0.35f, ProjectBootstrap.MatEmissiveWarm);
                }

            // Conveyor: Conveyor builds its own belt at runtime (no renderers here). Belt runs along -Z towards the sleigh.
            var conveyor = new GameObject("Conveyor");
            conveyor.transform.SetPositionAndRotation(new Vector3(-5.2f, 0f, 3f), Quaternion.Euler(0f, 180f, 0f));
            conveyor.AddComponent<Conveyor>();
            Box(env, "ChuteHood", new Vector3(-5.2f, 2.7f, 6.1f), new Vector3(1.8f, 0.25f, 1.4f), ProjectBootstrap.MatMetal, false);
            Box(env, "ChutePostL", new Vector3(-6.2f, 1.35f, 6.6f), new Vector3(0.12f, 2.7f, 0.12f), ProjectBootstrap.MatMetal);
            Box(env, "ChutePostR", new Vector3(-4.2f, 1.35f, 6.6f), new Vector3(0.12f, 2.7f, 0.12f), ProjectBootstrap.MatMetal);

            // Order board on the north wall.
            var board = new GameObject("OrderBoard");
            board.transform.position = new Vector3(0f, 0f, 7.1f);
            board.AddComponent<OrderBoardStation>();
            Box(board.transform, "Board", new Vector3(0f, 1.9f, 7.7f), new Vector3(3f, 1.8f, 0.12f), ProjectBootstrap.MatDarkWood);
            Box(board.transform, "BoardFrame", new Vector3(0f, 2.86f, 7.66f), new Vector3(3.2f, 0.12f, 0.16f), ProjectBootstrap.MatGold, false);
            for (int i = 0; i < 3; i++)
                Box(board.transform, "OrderNote" + i, new Vector3(-0.95f + i * 0.95f, 1.9f, 7.63f), new Vector3(0.65f, 0.85f, 0.02f), ProjectBootstrap.MatPaper, false);
            MakeStaticChildren(board);

            // Workbench (upgrades).
            var bench = new GameObject("Workbench");
            bench.transform.position = new Vector3(5.2f, 0f, 1.5f);
            bench.AddComponent<WorkbenchStation>();
            GameObject benchModel = Model(bench.transform, "Hub/Workbench.fbx", "WorkbenchModel", new Vector3(5.2f, 0f, 1.5f), -90f, 1.3f);
            if (benchModel != null)
            {
                Bounds bb = BoundsOf(benchModel);
                var col = bench.AddComponent<BoxCollider>();
                col.center = bench.transform.InverseTransformPoint(bb.center);
                col.size = bb.size;
            }
            else
            {
                Box(bench.transform, "BenchTop", new Vector3(5.2f, 0.9f, 1.5f), new Vector3(1.2f, 0.1f, 2.6f), ProjectBootstrap.MatWood);
                foreach (float dx in new[] { -0.5f, 0.5f })
                    foreach (float dz in new[] { -1.15f, 1.15f })
                        Box(bench.transform, "BenchLeg", new Vector3(5.2f + dx, 0.425f, 1.5f + dz), new Vector3(0.1f, 0.85f, 0.1f), ProjectBootstrap.MatDarkWood);
                Box(bench.transform, "DemoJetpack", new Vector3(5.2f, 1.2f, 0.8f), new Vector3(0.45f, 0.5f, 0.3f), ProjectBootstrap.MatMetal, false);
                Box(bench.transform, "DemoBoot", new Vector3(5.2f, 1.05f, 2.2f), new Vector3(0.25f, 0.2f, 0.4f), ProjectBootstrap.MatRed, false);
                Box(bench.transform, "ToolRack", new Vector3(5.9f, 1.6f, 1.5f), new Vector3(0.08f, 1.2f, 2.4f), ProjectBootstrap.MatDarkWood, false);
            }
            MakeStaticChildren(bench);

            // Sleigh with launch pad.
            var sleigh = new GameObject("Sleigh");
            sleigh.transform.position = new Vector3(0f, 0f, -4.8f);
            sleigh.AddComponent<SleighLoadZone>();
            Box(sleigh.transform, "RunnerL", new Vector3(-0.75f, 0.08f, -4.8f), new Vector3(0.12f, 0.16f, 3.6f), ProjectBootstrap.MatGold);
            Box(sleigh.transform, "RunnerR", new Vector3(0.75f, 0.08f, -4.8f), new Vector3(0.12f, 0.16f, 3.6f), ProjectBootstrap.MatGold);
            Box(sleigh.transform, "Body", new Vector3(0f, 0.65f, -4.8f), new Vector3(1.7f, 0.8f, 2.8f), ProjectBootstrap.MatRed);
            Box(sleigh.transform, "SeatBack", new Vector3(0f, 1.25f, -6.05f), new Vector3(1.7f, 0.7f, 0.3f), ProjectBootstrap.MatRed);
            Box(sleigh.transform, "FrontCurl", new Vector3(0f, 0.75f, -3.25f), new Vector3(1.7f, 0.6f, 0.2f), ProjectBootstrap.MatGold);
            Box(sleigh.transform, "SackDecor", new Vector3(0f, 1.25f, -5.3f), new Vector3(1.0f, 0.6f, 0.9f), ProjectBootstrap.MatFabric, false);
            Disc(env, "LaunchPad", new Vector3(0f, 0.012f, -4.8f), 4.4f, 0.02f, ProjectBootstrap.MatGold, false);
            MakeStaticChildren(sleigh);

            // Lights: max 3 point lights, no shadows.
            Point(env, "ConveyorLight", new Vector3(-5.2f, 3.0f, 3f), new Color(1f, 0.72f, 0.4f), 6f, 11f);
            Point(env, "WorkbenchLight", new Vector3(5.2f, 3.0f, 1.5f), new Color(0.65f, 0.85f, 1f), 5f, 10f);
            Point(env, "SleighLight", new Vector3(0f, 3.2f, -2.5f), new Color(1f, 0.78f, 0.5f), 7f, 13f);

            // Player.
            CreateSpawnAndPlayer(new Vector3(0f, 0f, 0.5f), Quaternion.identity);

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
                new Color(0.22f, 0.30f, 0.48f), new Color(0.15f, 0.21f, 0.34f), new Color(0.08f, 0.10f, 0.16f),
                true, new Color(0.10f, 0.14f, 0.25f), 0.010f);
            CreateCamera(new Vector3(0f, 3f, -6f), Quaternion.Euler(15f, 0f, 0f));
            CreateMoonlight(0.9f, Quaternion.Euler(50f, -30f, 0f));
            CreateGlobalVolume();

            Transform env = new GameObject("Environment").transform;
            Box(env, "StreetSnow", new Vector3(20f, -8.3f, 5f), new Vector3(160f, 0.6f, 120f), ProjectBootstrap.MatSnow);

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
                    GameObject deck = Box(env, "Deck_" + tag, new Vector3(x, y - 0.1f, z), new Vector3(w, 0.2f, d), ProjectBootstrap.MatSnow);
                    deck.AddComponent<SurfaceTag>().Surface = SurfaceType.Default;
                    // Low parapet on the street side.
                    Box(env, "Parapet_" + tag, new Vector3(x, y + 0.15f, z - d * 0.5f + 0.1f), new Vector3(w, 0.3f, 0.2f), ProjectBootstrap.MatDarkWood, true);

                    // Ice patch on every third roof.
                    if ((r * cols + c) % 3 == 1)
                    {
                        GameObject ice = Box(env, "Ice_" + tag, new Vector3(x + (float)(rng.NextDouble() - 0.5) * 2f, y + 0.02f, z + (float)(rng.NextDouble() - 0.5) * 2f),
                            new Vector3(3.5f, 0.04f, 2.8f), ProjectBootstrap.MatIce);
                        ice.AddComponent<SurfaceTag>().Surface = SurfaceType.Ice;
                    }

                    // Fairy lights along the street edge.
                    for (int k = 0; k < 6; k++)
                        Ball(env, "Fairy", new Vector3(x - w * 0.5f + 0.8f + k * 1.3f, y + 0.45f, z - d * 0.5f + 0.15f), Vector3.one * 0.12f, ProjectBootstrap.MatEmissiveWarm);

                    // Antenna / decor.
                    if ((r + c) % 2 == 0)
                    {
                        Box(env, "AntennaPole", new Vector3(x + w * 0.35f, y + 0.9f, z + d * 0.3f), new Vector3(0.06f, 1.8f, 0.06f), ProjectBootstrap.MatMetal, false);
                        Box(env, "AntennaBar", new Vector3(x + w * 0.35f, y + 1.5f, z + d * 0.3f), new Vector3(0.8f, 0.05f, 0.05f), ProjectBootstrap.MatMetal, false);
                    }

                    // Chimney with a floating number label (numbers are assigned at runtime).
                    float cx = x + (float)(rng.NextDouble() - 0.5) * (w - 3f);
                    float cz = z + (float)(rng.NextDouble() - 0.5) * (d - 3f) + 0.4f;
                    chimPos[r, c] = new Vector2(cx, cz);
                    GameObject ch = Box(env, "Chimney_" + tag, new Vector3(cx, y + 0.9f, cz), new Vector3(0.9f, 1.8f, 0.9f), ProjectBootstrap.MatBrick);
                    Box(env, "ChimneyCap_" + tag, new Vector3(cx, y + 1.86f, cz), new Vector3(1.15f, 0.14f, 1.15f), ProjectBootstrap.MatSnow, false);
                    Box(env, "ChimneyHole_" + tag, new Vector3(cx, y + 1.94f, cz), new Vector3(0.6f, 0.04f, 0.6f), ProjectBootstrap.MatDark, false);
                    var chim = ch.AddComponent<SilentNight.Rooftop.RooftopChimney>();
                    var smokeGo = new GameObject("ChimneySmoke");
                    smokeGo.transform.SetParent(ch.transform, false);
                    smokeGo.transform.position = new Vector3(cx, y + 1.95f, cz);
                    smokeGo.AddComponent<SilentNight.FX.ChimneySmoke>();

                    var labelGo = new GameObject("NumberLabel");
                    labelGo.transform.SetParent(ch.transform, false);
                    labelGo.transform.position = new Vector3(cx, y + 2.6f, cz);
                    labelGo.AddComponent<SilentNight.Rooftop.Billboard>();
                    var tm = labelGo.AddComponent<TextMesh>();
                    tm.text = "№ ?";
                    tm.fontSize = 64;
                    tm.characterSize = 0.04f;
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    tm.color = new Color(1f, 0.92f, 0.65f);
                    if (font != null)
                    {
                        tm.font = font;
                        labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                    }
                    SetRef(chim, "numberLabel", tm);
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
            Box(sleigh, "RunnerL", new Vector3(-2.4f, 0.08f, 0f), new Vector3(0.12f, 0.16f, 3.2f), ProjectBootstrap.MatGold);
            Box(sleigh, "RunnerR", new Vector3(-1.2f, 0.08f, 0f), new Vector3(0.12f, 0.16f, 3.2f), ProjectBootstrap.MatGold);
            Box(sleigh, "Body", new Vector3(-1.8f, 0.6f, 0f), new Vector3(1.5f, 0.7f, 2.6f), ProjectBootstrap.MatRed);
            Box(sleigh, "SeatBack", new Vector3(-1.8f, 1.1f, -1.2f), new Vector3(1.5f, 0.6f, 0.25f), ProjectBootstrap.MatRed);

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

            // Warm lights near the sleigh (the moon does the rest).
            Point(env, "SleighGlow", new Vector3(-1.8f, 2.5f, 0f), new Color(1f, 0.75f, 0.45f), 3f, 8f);

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

        /// <summary>Snowy ramps across the gap between side-by-side roofs (along X, no parapets there) that differ by
        /// >= 0.3 m, on every other edge (checkerboard): run 1.75 m, 10–14.5°. The other gaps stay jumps.</summary>
        private static void BuildRooftopRamps(Transform env, float[,] heights, int rows, int cols, float w, float d, float gap)
        {
            float pitchX = w + gap, pitchZ = d + gap;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    if ((r + c) % 2 != 0) continue;
                    float yA = heights[r, c], yB = heights[r, c + 1];
                    if (Mathf.Abs(yA - yB) < 0.29f) continue;
                    float edgeA = c * pitchX + w * 0.5f, edgeB = edgeA + gap;
                    float z = r * pitchZ + (r % 2 == 0 ? -1f : 1f);
                    Vector3 low, high;
                    if (yA < yB) { low = new Vector3(edgeA - 0.7f, yA, z); high = new Vector3(edgeB + 0.05f, yB, z); }
                    else { low = new Vector3(edgeB + 0.7f, yB, z); high = new Vector3(edgeA - 0.05f, yA, z); }

                    GameObject ramp = Slab(env, $"SnowRamp_{r}_{c}", low, high, 1.2f, 0.2f, ProjectBootstrap.MatSnow);
                    ramp.AddComponent<SurfaceTag>().Surface = SurfaceType.Default;
                    // Snow lumps along the sides (decor).
                    Vector3 mid = (low + high) * 0.5f;
                    Ball(env, "RampSnowL", mid + new Vector3(0f, 0.02f, -0.62f), new Vector3(1.4f, 0.18f, 0.22f), ProjectBootstrap.MatSnow);
                    Ball(env, "RampSnowR", mid + new Vector3(0f, 0.02f, 0.62f), new Vector3(1.4f, 0.18f, 0.22f), ProjectBootstrap.MatSnow);
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
                Box(env, "PerchStand_" + tag, new Vector3(px, y + 1.0f, z + 0.85f), new Vector3(1.2f, 2.0f, 1.2f), ProjectBootstrap.MatMetal);
                Box(env, "PerchSnow_" + tag, new Vector3(px, y + 1.97f, z + 0.85f), new Vector3(1.3f, 0.06f, 1.3f), ProjectBootstrap.MatSnow, false);
                Box(env, "PerchFlagPole_" + tag, new Vector3(px + 0.5f, y + 2.5f, z + 0.4f), new Vector3(0.04f, 1.0f, 0.04f), ProjectBootstrap.MatMetal, false);
                Box(env, "PerchFlag_" + tag, new Vector3(px + 0.5f, y + 2.85f, z + 0.6f), new Vector3(0.02f, 0.25f, 0.4f), ProjectBootstrap.MatRed, false);

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
                new Color(0.55f, 0.42f, 0.32f), new Color(0.42f, 0.30f, 0.22f), new Color(0.20f, 0.14f, 0.10f),
                true, new Color(0.25f, 0.18f, 0.14f), 0.012f);
            CreateCamera(new Vector3(6f, 2.5f, 3.5f), Quaternion.Euler(15f, 90f, 0f));
            CreateMoonlight(0.25f, Quaternion.Euler(50f, 30f, 0f));
            CreateGlobalVolume();

            Transform env = new GameObject("Environment").transform;
            Transform moodProps = new GameObject("MoodProps").transform; // toggled by HouseMoodController (not static)
            var warm = new List<Light>();
            var flicker = new List<Light>();
            var cozyOnly = new List<GameObject>();
            var spookyOnly = new List<GameObject>();

            // ---- floors (top at y = 0), one per room with its surface ----
            Floor(env, "Floor_Living", 0f, 12f, 0f, 11f, ProjectBootstrap.MatCarpet, SurfaceType.Carpet);
            Floor(env, "Floor_Kitchen", 0f, 12f, -11f, 0f, ProjectBootstrap.MatTile, SurfaceType.Tile);
            Floor(env, "Floor_Hallway", -3.5f, 0f, -11f, 11f, ProjectBootstrap.MatWood, SurfaceType.Wood);
            Floor(env, "Floor_Bedroom", -12f, -3.5f, 4f, 11f, ProjectBootstrap.MatWood, SurfaceType.Wood);
            Floor(env, "Floor_Study", -12f, -3.5f, 0.5f, 4f, ProjectBootstrap.MatDarkWood, SurfaceType.Wood);
            Floor(env, "Floor_Garage", -12f, -3.5f, -11f, 0.5f, ProjectBootstrap.MatConcrete, SurfaceType.Default);
            Box(env, "SnowGround", new Vector3(0f, -0.25f, 0f), new Vector3(80f, 0.3f, 80f), ProjectBootstrap.MatSnow);

            // ---- walls: 3 m high; exterior 0.3, interior 0.2; doorways 1.4 x 2.2 ----
            const float h = 3f, ext = 0.3f, inner = 0.2f;
            string wall = ProjectBootstrap.MatWall;
            WallAlongX(env, "Ext_N", 11f, -12.15f, 12.15f, h, ext, wall);
            WallAlongX(env, "Ext_S", -11f, -12.15f, 12.15f, h, ext, wall);
            WallAlongZ(env, "Ext_W", -12f, -11f, 11f, h, ext, wall);
            WallAlongZ(env, "Ext_E", 12f, -11f, 11f, h, ext, wall);
            WallAlongZ(env, "Wall_Hall_East", 0f, -11f, 11f, h, inner, wall, 4f, -5f);           // → living, kitchen
            WallAlongX(env, "Wall_Living_Kitchen", 0f, 0f, 12f, h, inner, wall, 3f);           // living ↔ kitchen
            WallAlongZ(env, "Wall_Hall_West", -3.5f, -11f, 11f, h, inner, wall, 7.5f, 2.25f, -5f); // → bedroom, study, garage
            WallAlongX(env, "Wall_Bedroom_Study", 4f, -12f, -3.5f, h, inner, wall);

            // Creaky physical doors: push slowly = silent, normal = creak, slam = БАХ! (living room and Grandpa's bedroom).
            SilentNight.Stealth.CreakyDoor.Create(env, new Vector3(0f, 0f, 3.3f), DoorW, DoorH, -90f, Mat(ProjectBootstrap.MatDarkWood));
            SilentNight.Stealth.CreakyDoor.Create(env, new Vector3(-3.5f, 0f, 6.8f), DoorW, DoorH, -90f, Mat(ProjectBootstrap.MatDarkWood));
            WallAlongX(env, "Wall_Study_Garage", 0.5f, -12f, -3.5f, h, inner, wall);

            // ---- Living room (x 0..12, z 0..11): tree, fireplace (exit), gifts arrive here ----
            BuildTree(env, new Vector3(5.5f, 0f, 6.5f), 1f);
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
            var chimney = new GameObject("ChimneyExit");
            chimney.transform.position = new Vector3(10.6f, 0f, 3.5f);
            chimney.AddComponent<ChimneyExit>();

            // Furniture.
            Box(env, "Sofa", new Vector3(8.5f, 0.25f, 9.9f), new Vector3(2.4f, 0.5f, 0.9f), ProjectBootstrap.MatFabric);
            Box(env, "SofaBack", new Vector3(8.5f, 0.7f, 10.45f), new Vector3(2.4f, 0.9f, 0.2f), ProjectBootstrap.MatFabric);
            Box(env, "SofaArmL", new Vector3(7.2f, 0.4f, 9.9f), new Vector3(0.2f, 0.8f, 0.9f), ProjectBootstrap.MatFabric);
            Box(env, "SofaArmR", new Vector3(9.8f, 0.4f, 9.9f), new Vector3(0.2f, 0.8f, 0.9f), ProjectBootstrap.MatFabric);
            // Coffee table on legs: an elf can crouch under it.
            Box(env, "CoffeeTable", new Vector3(8.5f, 0.53f, 8.4f), new Vector3(1.2f, 0.06f, 0.6f), ProjectBootstrap.MatDarkWood);
            foreach (float dx in new[] { -0.55f, 0.55f })
                foreach (float dz in new[] { -0.25f, 0.25f })
                    Box(env, "CoffeeTableLeg", new Vector3(8.5f + dx, 0.25f, 8.4f + dz), new Vector3(0.06f, 0.5f, 0.06f), ProjectBootstrap.MatDarkWood);
            HideSpot(env, "HidingSpot_CoffeeTable", new Vector3(8.5f, 0.23f, 8.4f), new Vector3(1.05f, 0.5f, 0.48f), "под журнальным столиком");
            Box(env, "Armchair", new Vector3(1.8f, 0.3f, 9.6f), new Vector3(0.9f, 0.6f, 0.9f), ProjectBootstrap.MatRed);
            Box(env, "ArmchairBack", new Vector3(1.8f, 0.75f, 10.0f), new Vector3(0.9f, 0.9f, 0.15f), ProjectBootstrap.MatRed);
            // Old TV with the remote on the stand edge: knock the remote off and the TV hisses white noise.
            SilentNight.Stealth.NoisyTV.Create(env, new Vector3(11.55f, 0f, 8.5f), -90f, Mat(ProjectBootstrap.MatDarkWood));
            // Squeaky toy on the carpet.
            Ball(env, "SqueakyToy", new Vector3(7.0f, 0.12f, 4.6f), Vector3.one * 0.22f, ProjectBootstrap.MatRed);
            NoiseZoneAt(new Vector3(7.0f, 0.3f, 4.6f), new Vector3(0.9f, 0.6f, 0.9f), NoiseSource.Object, 40f, 2f);
            // Toys on the floor: trip the elf unless it sneaks [C] over them.
            Toy(env, "ToyCar", new Vector3(7.8f, 0f, 5.4f), 25f, new Vector3(0.40f, 0.20f, 0.30f), ProjectBootstrap.MatRed, null);
            Toy(env, "ToyBlocks", new Vector3(4.0f, 0f, 5.2f), 0f, new Vector3(0.60f, 0.20f, 0.50f), ProjectBootstrap.MatGold, "Хрусть! Эльф споткнулся о кубики.");
            Toy(env, "ToyCar2", new Vector3(2.5f, 0f, 8.5f), -40f, new Vector3(0.40f, 0.20f, 0.30f), ProjectBootstrap.MatGreen, null);
            Toy(env, "RubberDuck", new Vector3(6.0f, 0f, -4.5f), 0f, new Vector3(0.35f, 0.20f, 0.30f), ProjectBootstrap.MatGold, "Пиии! Эльф наступил на резиновую уточку.");
            Toy(env, "ToyTrain", new Vector3(-1.75f, 0f, 3.0f), 90f, new Vector3(0.50f, 0.20f, 0.40f), ProjectBootstrap.MatRed, "Ту-ту! Эльф наступил на игрушечный паровозик.");

            // Garland along the north wall (cozy only).
            var garland = new GameObject("Garland").transform;
            garland.SetParent(moodProps, false);
            for (int i = 0; i < 10; i++)
            {
                float x = 0.8f + i * 1.15f;
                float sag = Mathf.Sin(i / 9f * Mathf.PI) * 0.3f;
                Ball(garland, "Bulb", new Vector3(x, 2.75f - sag, 10.75f), Vector3.one * 0.12f, ProjectBootstrap.MatEmissiveWarm);
            }
            cozyOnly.Add(garland.gameObject);

            warm.Add(Point(env, "TreeLight", new Vector3(5.5f, 2.3f, 5.0f), new Color(1f, 0.75f, 0.45f), 4f, 7f));
            warm.Add(Point(env, "FireLight", new Vector3(10.9f, 0.8f, 3.5f), new Color(1f, 0.55f, 0.2f), 4f, 7f));
            Ball(env, "SconceLiving", new Vector3(6f, 2.4f, 10.75f), new Vector3(0.3f, 0.25f, 0.2f), ProjectBootstrap.MatEmissiveWarm);
            warm.Add(Point(env, "SconceLight", new Vector3(6f, 2.4f, 10.4f), new Color(1f, 0.65f, 0.3f), 3f, 8f));

            // ---- Hallway (x -3.5..0, z -11..11): stairs, clock, creaky boards ----
            for (int i = 0; i < 9; i++)
            {
                float stepH = (i + 1) * 0.22f;
                GameObject step = Box(env, "Stair" + i, new Vector3(-0.85f, stepH * 0.5f, 6.75f + i * 0.3f), new Vector3(1.3f, stepH, 0.3f), ProjectBootstrap.MatDarkWood);
                step.AddComponent<SurfaceTag>().Surface = SurfaceType.CreakyWood;
            }
            GameObject landing = Box(env, "StairLanding", new Vector3(-0.85f, 0.99f, 10.1f), new Vector3(1.3f, 1.98f, 1.6f), ProjectBootstrap.MatDarkWood);
            landing.AddComponent<SurfaceTag>().Surface = SurfaceType.CreakyWood;
            Box(env, "Banister", new Vector3(-1.55f, 1.6f, 8.6f), new Vector3(0.06f, 0.06f, 4.6f), ProjectBootstrap.MatWood, false);
            Box(env, "BanisterPost", new Vector3(-1.55f, 0.8f, 6.65f), new Vector3(0.08f, 1.6f, 0.08f), ProjectBootstrap.MatWood);

            Box(env, "GrandfatherClock", new Vector3(-0.4f, 1.05f, 1.0f), new Vector3(0.4f, 2.1f, 0.5f), ProjectBootstrap.MatDarkWood);
            Box(env, "ClockFace", new Vector3(-0.61f, 1.75f, 1.0f), new Vector3(0.02f, 0.35f, 0.35f), ProjectBootstrap.MatPaper, false);
            Box(env, "ClockPendulum", new Vector3(-0.61f, 1.1f, 1.0f), new Vector3(0.02f, 0.5f, 0.08f), ProjectBootstrap.MatGold, false);

            CreakyBoard(env, new Vector3(-1.2f, 0f, -2.0f), new Vector2(2.0f, 2.4f));
            CreakyBoard(env, new Vector3(-2.4f, 0f, 2.4f), new Vector2(1.8f, 2.0f));
            CreakyBoard(env, new Vector3(-2.5f, 0f, 7.5f), new Vector2(1.6f, 1.8f)); // in front of grandpa's door

            Ball(env, "HallLamp", new Vector3(-1.75f, 2.85f, -4f), Vector3.one * 0.3f, ProjectBootstrap.MatEmissiveWarm);
            flicker.Add(Point(env, "HallLight", new Vector3(-1.75f, 2.6f, -4f), new Color(1f, 0.8f, 0.55f), 3f, 8f));

            // ---- Kitchen (x 0..12, z -11..0): tile, table, fridge, cat ----
            Box(env, "CounterSouth", new Vector3(6.5f, 0.45f, -10.55f), new Vector3(9f, 0.9f, 0.6f), ProjectBootstrap.MatWood);
            Box(env, "CounterSouthTop", new Vector3(6.5f, 0.92f, -10.55f), new Vector3(9.05f, 0.04f, 0.65f), ProjectBootstrap.MatTile, false);
            Box(env, "CounterEast", new Vector3(11.55f, 0.45f, -7.5f), new Vector3(0.6f, 0.9f, 3.5f), ProjectBootstrap.MatWood);
            Box(env, "Fridge", new Vector3(11.4f, 0.95f, -3.2f), new Vector3(0.8f, 1.9f, 0.8f), ProjectBootstrap.MatMetal);
            Box(env, "KitchenTable", new Vector3(6.5f, 0.76f, -5.5f), new Vector3(1.8f, 0.06f, 1.0f), ProjectBootstrap.MatWood);
            foreach (float dx in new[] { -0.8f, 0.8f })
                foreach (float dz in new[] { -0.42f, 0.42f })
                    Box(env, "TableLeg", new Vector3(6.5f + dx, 0.365f, -5.5f + dz), new Vector3(0.08f, 0.73f, 0.08f), ProjectBootstrap.MatDarkWood);
            HideSpot(env, "HidingSpot_KitchenTable", new Vector3(6.5f, 0.33f, -5.5f), new Vector3(1.65f, 0.7f, 0.85f), "под кухонным столом");
            Box(env, "ChairW", new Vector3(5.2f, 0.225f, -5.5f), new Vector3(0.45f, 0.45f, 0.45f), ProjectBootstrap.MatDarkWood);
            Box(env, "ChairE", new Vector3(7.8f, 0.225f, -5.5f), new Vector3(0.45f, 0.45f, 0.45f), ProjectBootstrap.MatDarkWood);
            Disc(env, "CatBed", new Vector3(3.2f, 0.015f, -2.6f), 0.9f, 0.03f, ProjectBootstrap.MatFabric, false);
            GameObject catGo = NPCFactory.CreateCat(new Vector3(3.2f, 0f, -2.6f), Quaternion.Euler(0f, 90f, 0f));
            // Serialize the cat model into the scene so player builds have it too (NPCVisual only auto-loads in the editor).
            var catModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/Cat/Cat.fbx");
            if (catModel != null) catGo.GetComponent<NPCVisual>().Configure(NPCKind.Cat, catModel);
            var catCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Characters/Cat/Cat.controller");
            if (catCtrl != null) catGo.GetComponent<NPCVisual>().AnimatorController = catCtrl;
            Ball(env, "KitchenLamp", new Vector3(6.5f, 2.85f, -5.5f), Vector3.one * 0.35f, ProjectBootstrap.MatEmissiveWarm);
            flicker.Add(Point(env, "KitchenLight", new Vector3(6.5f, 2.6f, -5.5f), new Color(1f, 0.9f, 0.75f), 5f, 10f));

            // ---- Grandpa's bedroom (x -12..-3.5, z 4..11) ----
            // Bed on legs: crawl space underneath to hide right next to Grandpa.
            Box(env, "BedFrame", new Vector3(-10.4f, 0.55f, 8f), new Vector3(2.2f, 0.1f, 1.5f), ProjectBootstrap.MatDarkWood);
            foreach (float dx in new[] { -1.0f, 1.0f })
                foreach (float dz in new[] { -0.65f, 0.65f })
                    Box(env, "BedLeg", new Vector3(-10.4f + dx, 0.25f, 8f + dz), new Vector3(0.1f, 0.5f, 0.1f), ProjectBootstrap.MatDarkWood);
            HideSpot(env, "HidingSpot_Bed", new Vector3(-10.4f, 0.23f, 8f), new Vector3(1.9f, 0.5f, 1.2f), "под кроватью Деда");
            Box(env, "Mattress", new Vector3(-10.35f, 0.67f, 8f), new Vector3(2.0f, 0.14f, 1.4f), ProjectBootstrap.MatPaper);
            Box(env, "Headboard", new Vector3(-11.55f, 0.6f, 8f), new Vector3(0.1f, 1.2f, 1.6f), ProjectBootstrap.MatDarkWood);
            Box(env, "Blanket", new Vector3(-9.95f, 0.765f, 8f), new Vector3(1.3f, 0.05f, 1.46f), ProjectBootstrap.MatRed, false);
            Box(env, "Pillow", new Vector3(-11.15f, 0.8f, 8f), new Vector3(0.4f, 0.12f, 0.9f), ProjectBootstrap.MatPaper, false);
            Box(env, "Nightstand", new Vector3(-11.5f, 0.3f, 6.6f), new Vector3(0.5f, 0.6f, 0.5f), ProjectBootstrap.MatDarkWood);
            Ball(env, "BedsideLamp", new Vector3(-11.5f, 0.75f, 6.6f), Vector3.one * 0.25f, ProjectBootstrap.MatEmissiveWarm);
            Box(env, "Wardrobe", new Vector3(-6.0f, 1.0f, 10.55f), new Vector3(1.6f, 2.0f, 0.6f), ProjectBootstrap.MatDarkWood);
            Patch(env, "BedroomRug", new Vector3(-8.0f, 0f, 7.5f), new Vector2(2.5f, 2.0f), ProjectBootstrap.MatCarpet, SurfaceType.Carpet);
            warm.Add(Point(env, "BedsideLight", new Vector3(-11.4f, 0.95f, 6.6f), new Color(1f, 0.7f, 0.4f), 1.5f, 4f));

            var patrolRoot = new GameObject("GrandpaPatrolPoints").transform;
            Transform p1 = new GameObject("Patrol_Kitchen").transform;
            p1.SetParent(patrolRoot, false); p1.position = new Vector3(9.5f, 0f, -4.5f);
            Transform p2 = new GameObject("Patrol_Hallway").transform;
            p2.SetParent(patrolRoot, false); p2.position = new Vector3(-1.8f, 0f, -7f);
            // Head on the pillow (-X), gets up towards +X (into the room).
            NPCFactory.CreateGrandpa(new Vector3(-10.4f, 0f, 8f), Quaternion.Euler(0f, 90f, 0f), new[] { p1, p2 });

            // ---- Study (x -12..-3.5, z 0.5..4) — narrow ----
            string[] bookMats = { ProjectBootstrap.MatRed, ProjectBootstrap.MatGreen, ProjectBootstrap.MatFabric, ProjectBootstrap.MatGold };
            for (int i = 0; i < 4; i++)
            {
                float x = -11.1f + i * 1.4f;
                Box(env, "Bookshelf" + i, new Vector3(x, 1.1f, 3.65f), new Vector3(1.3f, 2.2f, 0.4f), ProjectBootstrap.MatDarkWood);
                for (int row = 0; row < 3; row++)
                    Box(env, "Books", new Vector3(x, 0.55f + row * 0.6f, 3.42f), new Vector3(1.1f, 0.35f, 0.06f), bookMats[(i + row) % bookMats.Length], false);
            }
            Box(env, "Desk", new Vector3(-10.6f, 0.38f, 1.2f), new Vector3(1.5f, 0.76f, 0.7f), ProjectBootstrap.MatWood);
            Box(env, "DeskChair", new Vector3(-10.6f, 0.25f, 1.85f), new Vector3(0.45f, 0.5f, 0.45f), ProjectBootstrap.MatFabric);
            Ball(env, "DeskLamp", new Vector3(-11.1f, 0.9f, 1.1f), Vector3.one * 0.2f, ProjectBootstrap.MatEmissiveWarm);
            Box(env, "Globe", new Vector3(-5.0f, 0.5f, 1.1f), new Vector3(0.4f, 1.0f, 0.4f), ProjectBootstrap.MatDarkWood);
            flicker.Add(Point(env, "StudyLight", new Vector3(-8f, 2.6f, 2.2f), new Color(1f, 0.85f, 0.6f), 3.5f, 7f));

            // ---- Garage (x -12..-3.5, z -11..0.5): ice puddle, car, shelves ----
            GameObject ice = Disc(env, "IcePuddle", new Vector3(-8.5f, 0.015f, -7.8f), 4.4f, 0.03f, ProjectBootstrap.MatIce, true);
            ice.AddComponent<SurfaceTag>().Surface = SurfaceType.Ice;
            Box(env, "CarBody", new Vector3(-10.3f, 0.6f, -2.6f), new Vector3(1.8f, 0.9f, 3.8f), ProjectBootstrap.MatRed);
            Box(env, "CarCabin", new Vector3(-10.3f, 1.35f, -2.8f), new Vector3(1.6f, 0.6f, 2.0f), ProjectBootstrap.MatMetal);
            foreach (float dx in new[] { -0.92f, 0.92f })
                foreach (float dz in new[] { -1.3f, 1.3f })
                    Box(env, "Wheel", new Vector3(-10.3f + dx, 0.3f, -2.6f + dz), new Vector3(0.2f, 0.6f, 0.6f), ProjectBootstrap.MatDark, false);
            Box(env, "GarageShelf", new Vector3(-8f, 1.0f, -10.6f), new Vector3(3f, 2f, 0.5f), ProjectBootstrap.MatMetal);
            Box(env, "Crate1", new Vector3(-5.2f, 0.4f, -10.2f), new Vector3(0.8f, 0.8f, 0.8f), ProjectBootstrap.MatWood);
            Box(env, "Crate2", new Vector3(-5.2f, 1.15f, -10.2f), new Vector3(0.7f, 0.7f, 0.7f), ProjectBootstrap.MatWood, true, 15f);
            Box(env, "ToolBench", new Vector3(-11.4f, 0.45f, -6f), new Vector3(0.8f, 0.9f, 2.0f), ProjectBootstrap.MatDarkWood);
            flicker.Add(Point(env, "GarageLight", new Vector3(-8f, 2.7f, -5f), new Color(0.8f, 0.86f, 1f), 4f, 10f));

            // Cobwebs (spooky only).
            var cobwebs = new GameObject("Cobwebs").transform;
            cobwebs.SetParent(moodProps, false);
            Box(cobwebs, "Web", new Vector3(-11.7f, 2.7f, -10.7f), new Vector3(0.6f, 0.6f, 0.02f), ProjectBootstrap.MatPaper, false, 45f);
            Box(cobwebs, "Web", new Vector3(-3.7f, 2.7f, 10.7f), new Vector3(0.6f, 0.6f, 0.02f), ProjectBootstrap.MatPaper, false, -45f);
            Box(cobwebs, "Web", new Vector3(11.7f, 2.7f, -10.7f), new Vector3(0.6f, 0.6f, 0.02f), ProjectBootstrap.MatPaper, false, -45f);
            Box(cobwebs, "Web", new Vector3(-11.7f, 2.7f, 3.8f), new Vector3(0.5f, 0.5f, 0.02f), ProjectBootstrap.MatPaper, false, 45f);
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
            soot.transform.position = new Vector3(10.1f, 0.5f, 3.5f);
            soot.AddComponent<SilentNight.FX.SootPuff>();

            // ---- player (arrives through the chimney) ----
            CreateSpawnAndPlayer(new Vector3(9.4f, 0f, 3.5f), Quaternion.Euler(0f, -90f, 0f));

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
                potCol.transform.position = basePos + Vector3.up * 0.3f;
                potCol.AddComponent<BoxCollider>().size = new Vector3(0.9f, 0.6f, 0.9f);
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

        /// <summary>Hub decor fireplace against the north wall (z = wallZ).</summary>
        private static void BuildFireplaceNorth(Transform parent, Vector3 wallBase)
        {
            float x = wallBase.x, z = wallBase.z - 0.25f;
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
            GameObject fp = Model(parent, "LivingRoom/Fireplace.fbx", "FireplaceModel", new Vector3(x, 0f, z), -90f, 1.75f);
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
                slab.transform.position = new Vector3(b.max.x - depth * 0.15f, b.center.y, b.center.z);
                slab.AddComponent<BoxCollider>().size = new Vector3(depth * 0.3f, b.size.y, b.size.z);
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
                case ProjectBootstrap.MatTile: return 1.5f;
                case ProjectBootstrap.MatSnow: return 4f;
                case ProjectBootstrap.MatWall: return 3f;
                case ProjectBootstrap.MatCarpet: return 3f;
                case ProjectBootstrap.MatConcrete: return 3f;
                default: return 2.5f;
            }
        }

        private static void ApplyTexture(MeshRenderer r, Material m, string key, Vector3 size)
        {
            if (r == null || m == null) return;
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
            float t = TileMeters(key);
            bool flat = size.y < 0.4f;
            Vector2 tiles = flat ? new Vector2(size.x / t, size.z / t) : new Vector2(Mathf.Max(size.x, size.z) / t, size.y / t);
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(0.25f, tiles.x), Mathf.Max(0.25f, tiles.y), 0f, 0f));
            r.SetPropertyBlock(mpb);
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

        /// <summary>Wall running along X at the given z, with doorway gaps (door centres on X).</summary>
        private static void WallAlongX(Transform parent, string name, float z, float x0, float x1, float h, float thick, string mat, params float[] doors)
        {
            var list = new List<float>(doors); list.Sort();
            float cur = x0; int i = 0;
            foreach (float c in list)
            {
                float a = c - DoorW * 0.5f, b = c + DoorW * 0.5f;
                if (a - cur > 0.01f) Box(parent, $"{name}_{i++}", new Vector3((cur + a) * 0.5f, h * 0.5f, z), new Vector3(a - cur, h, thick), mat);
                if (h > DoorH) Box(parent, $"{name}_Lintel{i++}", new Vector3(c, (DoorH + h) * 0.5f, z), new Vector3(DoorW, h - DoorH, thick), mat);
                cur = b;
            }
            if (x1 - cur > 0.01f) Box(parent, $"{name}_{i}", new Vector3((cur + x1) * 0.5f, h * 0.5f, z), new Vector3(x1 - cur, h, thick), mat);
        }

        /// <summary>Wall running along Z at the given x, with doorway gaps (door centres on Z).</summary>
        private static void WallAlongZ(Transform parent, string name, float x, float z0, float z1, float h, float thick, string mat, params float[] doors)
        {
            var list = new List<float>(doors); list.Sort();
            float cur = z0; int i = 0;
            foreach (float c in list)
            {
                float a = c - DoorW * 0.5f, b = c + DoorW * 0.5f;
                if (a - cur > 0.01f) Box(parent, $"{name}_{i++}", new Vector3(x, h * 0.5f, (cur + a) * 0.5f), new Vector3(thick, h, a - cur), mat);
                if (h > DoorH) Box(parent, $"{name}_Lintel{i++}", new Vector3(x, (DoorH + h) * 0.5f, c), new Vector3(thick, h - DoorH, DoorW), mat);
                cur = b;
            }
            if (z1 - cur > 0.01f) Box(parent, $"{name}_{i}", new Vector3(x, h * 0.5f, (cur + z1) * 0.5f), new Vector3(thick, h, z1 - cur), mat);
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
