#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SilentNight.EditorTools
{
    /// <summary>
    /// Procedural PBR Texture & Normal Map Generator for "Tactile Christmas Stop-Motion" visual style.
    /// Inspired by the original Three.js TactileMaterials.ts & ApartmentLevel.ts.
    /// Generates 512x512 Albedo + Normal maps under Assets/_Project/Art/Environment/Textures/.
    /// </summary>
    public static class TactileArtGenerator
    {
        public const string TexFolder = "Assets/_Project/Art/Environment/Textures";
        private const int Res = 512;

        [MenuItem("Silent Night/Generate Tactile Art Textures", priority = 5)]
        public static void GenerateAll()
        {
            ProjectBootstrap.EnsureFolder(TexFolder);

            GenerateWoodPlanks("Wood", new Color(0.76f, 0.50f, 0.28f), new Color(0.64f, 0.39f, 0.20f), new Color(0.28f, 0.16f, 0.08f), 8);
            GenerateWoodPlanks("DarkWood", new Color(0.36f, 0.20f, 0.11f), new Color(0.27f, 0.14f, 0.07f), new Color(0.12f, 0.06f, 0.03f), 6);
            GenerateNordicCarpet("Carpet");
            GenerateKitchenTile("Tile");
            GenerateConcrete("Concrete");
            GenerateSnow("Snow");
            GenerateBrick("Brick");

            // Vintage striped wallpapers with gold pinstripes & diamond motifs
            GenerateWallpaper("Wall", new Color(0.88f, 0.80f, 0.67f), new Color(0.83f, 0.74f, 0.60f), new Color(0.95f, 0.84f, 0.52f));
            GenerateWallpaper("WallBurgundy", new Color(0.46f, 0.13f, 0.16f), new Color(0.38f, 0.09f, 0.12f), new Color(0.88f, 0.68f, 0.26f));
            GenerateWallpaper("WallSpruce", new Color(0.12f, 0.29f, 0.21f), new Color(0.09f, 0.23f, 0.16f), new Color(0.86f, 0.69f, 0.28f));
            GenerateWallpaper("WallNavy", new Color(0.14f, 0.20f, 0.34f), new Color(0.10f, 0.15f, 0.27f), new Color(0.82f, 0.70f, 0.36f));
            GenerateWallpaper("WallOchre", new Color(0.66f, 0.48f, 0.26f), new Color(0.58f, 0.41f, 0.21f), new Color(0.92f, 0.78f, 0.40f));

            // Framed winter storybook paintings & moonlit window view
            for (int i = 0; i < 4; i++)
                GeneratePainting($"Painting{i}", i);
            GenerateWindowView("WindowView");

            AssetDatabase.Refresh();
            ConfigureImporters();
            AssetDatabase.SaveAssets();
            Debug.Log("[Silent Night] Generated tactile PBR textures, wallpapers, normal maps, and winter paintings.");
        }

        public static void EnsureGenerated()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexFolder}/WallBurgundy.png") == null ||
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexFolder}/Wood_n.png") == null ||
                AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexFolder}/WindowView.png") == null)
            {
                GenerateAll();
            }
        }

        // ------------------------------------------------------------------ Wood Planks
        private static void GenerateWoodPlanks(string name, Color colA, Color colB, Color grooveCol, int plankRows)
        {
            var albedo = new Color[Res * Res];
            var height = new float[Res * Res];
            int rowH = Res / plankRows;

            for (int y = 0; y < Res; y++)
            {
                int row = y / rowH;
                int ry = y % rowH;
                int stagger = (row * 173) % (Res / 2);

                for (int x = 0; x < Res; x++)
                {
                    int sx = (x + stagger) % Res;
                    int plankId = row * 7 + (sx / (Res / 2));
                    float plankTone = Hash(plankId * 31 + 7);
                    Color baseCol = Color.Lerp(colA, colB, plankTone);

                    // Wood grain waves + subtle knots
                    float grain = Mathf.Sin((x * 0.06f) + Mathf.Sin(y * 0.14f + plankId) * 2.5f) * 0.5f + 0.5f;
                    float fine = Mathf.PerlinNoise((x + plankId * 37) * 0.08f, (y + row * 53) * 0.28f);
                    float cFactor = 0.88f + grain * 0.14f + (fine - 0.5f) * 0.10f;

                    bool hGroove = ry < 3 || ry >= rowH - 2;
                    bool vGroove = sx < 3 || Mathf.Abs(sx - Res / 2) < 2;

                    if (hGroove || vGroove)
                    {
                        albedo[y * Res + x] = grooveCol;
                        height[y * Res + x] = 0.15f;
                    }
                    else
                    {
                        // Soft bevel near plank edges
                        float bevel = 1f;
                        if (ry < 7) bevel = Mathf.Lerp(0.4f, 1f, (ry - 3) / 4f);
                        else if (ry > rowH - 6) bevel = Mathf.Lerp(0.4f, 1f, (rowH - 2 - ry) / 4f);

                        albedo[y * Res + x] = ClampColor(baseCol * (cFactor * Mathf.Lerp(0.88f, 1f, bevel)));
                        height[y * Res + x] = 0.55f + grain * 0.18f + fine * 0.12f * bevel;
                    }
                }
            }

            SavePair(name, albedo, height, 3.2f);
        }

        // ------------------------------------------------------------------ Nordic Knitted Christmas Rug
        private static void GenerateNordicCarpet(string name)
        {
            var albedo = new Color[Res * Res];
            var height = new float[Res * Res];

            Color crimson = new Color(0.58f, 0.11f, 0.13f);
            Color deepRed = new Color(0.44f, 0.07f, 0.09f);
            Color forest = new Color(0.11f, 0.34f, 0.19f);
            Color cream = new Color(0.95f, 0.90f, 0.78f);
            Color gold = new Color(0.88f, 0.68f, 0.22f);

            for (int y = 0; y < Res; y++)
            {
                for (int x = 0; x < Res; x++)
                {
                    // Knit stitch pattern
                    int kx = x % 8;
                    int ky = y % 8;
                    float stitch = (Mathf.Sin(kx * Mathf.PI / 4f) * Mathf.Cos(ky * Mathf.PI / 4f)) * 0.5f + 0.5f;

                    // Tileable Nordic geometric diamond + snowflake motif
                    int gx = (x / 8) % 16;
                    int gy = (y / 8) % 16;
                    int dx = Mathf.Abs(gx - 8);
                    int dy = Mathf.Abs(gy - 8);
                    int manhattan = dx + dy;

                    Color c = ((x / 64 + y / 64) % 2 == 0) ? crimson : deepRed;

                    // Border stripes every 64 px
                    int bx = x % 64;
                    int by = y % 64;
                    if (bx < 4 || bx >= 60 || by < 4 || by >= 60)
                        c = gold;
                    else if (bx < 9 || bx >= 55 || by < 9 || by >= 55)
                        c = forest;
                    else if (manhattan == 4 || manhattan == 5)
                        c = cream;
                    else if (manhattan < 4 && (dx == dy || dx == 0 || dy == 0))
                        c = gold;
                    else if (manhattan == 2)
                        c = forest;

                    float fuzz = (Hash(x * 73 + y * 199) - 0.5f) * 0.06f;
                    albedo[y * Res + x] = ClampColor(c * (0.90f + stitch * 0.16f + fuzz));
                    height[y * Res + x] = 0.4f + stitch * 0.45f + fuzz;
                }
            }

            SavePair(name, albedo, height, 4.0f);
        }

        // ------------------------------------------------------------------ Glossy Checkerboard Kitchen Tile
        private static void GenerateKitchenTile(string name)
        {
            var albedo = new Color[Res * Res];
            var height = new float[Res * Res];

            Color creamTile = new Color(0.94f, 0.91f, 0.84f);
            Color warmTerracotta = new Color(0.76f, 0.36f, 0.24f);
            Color slateAccent = new Color(0.22f, 0.31f, 0.38f);
            Color groutCol = new Color(0.55f, 0.52f, 0.48f);

            int tileSize = Res / 4; // 4x4 tiles per texture repeat

            for (int y = 0; y < Res; y++)
            {
                int ty = y / tileSize;
                int ly = y % tileSize;
                for (int x = 0; x < Res; x++)
                {
                    int tx = x / tileSize;
                    int lx = x % tileSize;

                    bool grout = lx < 4 || lx >= tileSize - 4 || ly < 4 || ly >= tileSize - 4;
                    if (grout)
                    {
                        albedo[y * Res + x] = groutCol;
                        height[y * Res + x] = 0.1f;
                        continue;
                    }

                    // Bevel around tile edge
                    float bevelX = Mathf.Min(lx - 4, tileSize - 5 - lx) / 8f;
                    float bevelY = Mathf.Min(ly - 4, tileSize - 5 - ly) / 8f;
                    float bevel = Mathf.Clamp01(Mathf.Min(bevelX, bevelY));

                    bool even = (tx + ty) % 2 == 0;
                    Color baseCol = even ? creamTile : ((tx % 2 == 0) ? warmTerracotta : slateAccent);
                    float marble = Mathf.PerlinNoise((x + tx * 19) * 0.03f, (y + ty * 31) * 0.03f) * 0.08f;

                    albedo[y * Res + x] = ClampColor(baseCol * (0.95f + marble) * Mathf.Lerp(0.88f, 1.03f, bevel));
                    height[y * Res + x] = 0.3f + bevel * 0.65f + marble * 0.2f;
                }
            }

            SavePair(name, albedo, height, 3.8f);
        }

        // ------------------------------------------------------------------ Vintage Striped Wallpaper
        private static void GenerateWallpaper(string name, Color primary, Color stripe, Color goldAccent)
        {
            var albedo = new Color[Res * Res];
            var height = new float[Res * Res];

            int period = 64; // 8 stripes across 512px
            for (int y = 0; y < Res; y++)
            {
                for (int x = 0; x < Res; x++)
                {
                    int sx = x % period;
                    bool inStripe = sx >= 14 && sx < 46;
                    bool goldPin = (sx == 12 || sx == 13 || sx == 46 || sx == 47);

                    // Subtle diamond damask in center of wide stripe
                    int dy = y % 64;
                    int dDist = Mathf.Abs(sx - 30) + Mathf.Abs(dy - 32);
                    bool diamond = dDist == 10 || dDist == 11 || dDist == 3;

                    Color c = inStripe ? stripe : primary;
                    float h = 0.5f;

                    if (goldPin)
                    {
                        c = Color.Lerp(c, goldAccent, 0.65f);
                        h = 0.78f;
                    }
                    else if (diamond && inStripe)
                    {
                        c = Color.Lerp(c, goldAccent, 0.45f);
                        h = 0.72f;
                    }

                    // Linen paper weave
                    float weave = ((x % 4 == 0 ? 0.02f : -0.01f) + (y % 4 == 0 ? 0.02f : -0.01f));
                    float noise = (Mathf.PerlinNoise(x * 0.12f, y * 0.12f) - 0.5f) * 0.04f;

                    albedo[y * Res + x] = ClampColor(c * (1f + weave + noise));
                    height[y * Res + x] = h + weave * 2.5f + noise;
                }
            }

            SavePair(name, albedo, height, 2.2f);
        }

        // ------------------------------------------------------------------ Warm Terracotta Brick
        private static void GenerateBrick(string name)
        {
            var albedo = new Color[Res * Res];
            var height = new float[Res * Res];

            Color brickA = new Color(0.64f, 0.27f, 0.16f);
            Color brickB = new Color(0.50f, 0.20f, 0.12f);
            Color mortar = new Color(0.82f, 0.77f, 0.68f);

            int rows = 8;
            int cols = 4;
            int rowH = Res / rows;
            int colW = Res / cols;

            for (int y = 0; y < Res; y++)
            {
                int r = y / rowH;
                int ry = y % rowH;
                int offset = (r % 2) * (colW / 2);

                for (int x = 0; x < Res; x++)
                {
                    int sx = (x + offset) % Res;
                    int c = sx / colW;
                    int rx = sx % colW;

                    bool isMortar = ry < 5 || ry >= rowH - 4 || rx < 5 || rx >= colW - 4;
                    if (isMortar)
                    {
                        float mNoise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.12f;
                        albedo[y * Res + x] = ClampColor(mortar * (0.92f + mNoise));
                        height[y * Res + x] = 0.15f + mNoise;
                    }
                    else
                    {
                        float bTone = Hash(r * 19 + c * 47);
                        Color bc = Color.Lerp(brickA, brickB, bTone);
                        float rough = (Mathf.PerlinNoise((x + r * 31) * 0.09f, (y + c * 17) * 0.09f) - 0.5f) * 0.16f;
                        albedo[y * Res + x] = ClampColor(bc * (1f + rough));
                        height[y * Res + x] = 0.72f + rough * 0.8f;
                    }
                }
            }

            SavePair(name, albedo, height, 4.2f);
        }

        // ------------------------------------------------------------------ Sparkling Winter Snow
        private static void GenerateSnow(string name)
        {
            var albedo = new Color[Res * Res];
            var height = new float[Res * Res];

            Color snowWhite = new Color(0.94f, 0.97f, 1.0f);
            Color snowBlue = new Color(0.82f, 0.89f, 0.98f);

            for (int y = 0; y < Res; y++)
            {
                for (int x = 0; x < Res; x++)
                {
                    float drift = Mathf.PerlinNoise(x * 0.025f, y * 0.025f);
                    float detail = Mathf.PerlinNoise(x * 0.1f + 13f, y * 0.1f + 29f);
                    float sparkle = Hash(x * 313 + y * 997) > 0.985f ? 0.08f : 0f;

                    Color c = Color.Lerp(snowBlue, snowWhite, drift * 0.7f + detail * 0.3f) + Color.white * sparkle;
                    albedo[y * Res + x] = ClampColor(c);
                    height[y * Res + x] = drift * 0.6f + detail * 0.4f;
                }
            }

            SavePair(name, albedo, height, 2.0f);
        }

        // ------------------------------------------------------------------ Garage Concrete
        private static void GenerateConcrete(string name)
        {
            var albedo = new Color[Res * Res];
            var height = new float[Res * Res];
            Color baseCol = new Color(0.48f, 0.50f, 0.53f);

            for (int y = 0; y < Res; y++)
            {
                for (int x = 0; x < Res; x++)
                {
                    float n1 = Mathf.PerlinNoise(x * 0.04f, y * 0.04f);
                    float n2 = Mathf.PerlinNoise(x * 0.16f + 7f, y * 0.16f + 11f);
                    float v = 0.88f + n1 * 0.16f + (n2 - 0.5f) * 0.08f;
                    albedo[y * Res + x] = ClampColor(baseCol * v);
                    height[y * Res + x] = 0.4f + n1 * 0.3f + n2 * 0.3f;
                }
            }

            SavePair(name, albedo, height, 2.4f);
        }

        // ------------------------------------------------------------------ Storybook Winter Paintings
        private static void GeneratePainting(string name, int variant)
        {
            const int W = 256, H = 256;
            var pixels = new Color[W * H];

            Color skyTop = variant == 1 ? new Color(0.05f, 0.16f, 0.24f) : new Color(0.05f, 0.09f, 0.22f);
            Color skyBot = variant == 1 ? new Color(0.16f, 0.42f, 0.38f) : new Color(0.18f, 0.26f, 0.48f);
            Color snow = new Color(0.90f, 0.94f, 0.99f);
            Color snowShadow = new Color(0.68f, 0.78f, 0.92f);
            Color pine = new Color(0.08f, 0.24f, 0.16f);
            Color warmWindow = new Color(1.0f, 0.82f, 0.38f);
            Color cottage = new Color(0.45f, 0.24f, 0.15f);

            int moonX = variant % 2 == 0 ? 185 : 70;
            int moonY = 195;

            for (int y = 0; y < H; y++)
            {
                float ty = (float)y / H;
                for (int x = 0; x < W; x++)
                {
                    float tx = (float)x / W;
                    Color c = Color.Lerp(skyBot, skyTop, Mathf.Clamp01((ty - 0.3f) / 0.7f));

                    // Aurora wave for variant 1
                    if (variant == 1 && y > 120)
                    {
                        float wave = Mathf.Sin(tx * 10f + ty * 4f) * 0.5f + 0.5f;
                        c = Color.Lerp(c, new Color(0.2f, 0.85f, 0.55f), wave * 0.35f * Mathf.Clamp01(1f - Mathf.Abs(ty - 0.72f) * 4f));
                    }

                    // Full moon + halo
                    float md = Vector2.Distance(new Vector2(x, y), new Vector2(moonX, moonY));
                    if (md < 22f) c = new Color(1f, 0.96f, 0.82f);
                    else if (md < 45f) c = Color.Lerp(c, new Color(0.95f, 0.9f, 0.7f), (1f - (md - 22f) / 23f) * 0.35f);

                    // Stars
                    if (y > 110 && Hash(x * 197 + y * 541 + variant * 71) > 0.994f)
                        c = Color.white;

                    // Rolling snowy hills
                    float hill1 = 85f + Mathf.Sin(tx * 5f + variant) * 18f;
                    float hill2 = 62f + Mathf.Cos(tx * 4f + variant * 2f) * 14f;
                    if (y < hill1) c = snowShadow;
                    if (y < hill2) c = snow;

                    // Pine trees
                    for (int p = 0; p < 5; p++)
                    {
                        float px = 32f + p * 48f + ((variant * 17 + p * 13) % 15);
                        float py = 55f + (p % 2) * 12f;
                        float dy = y - py;
                        if (dy >= 0 && dy < 65f)
                        {
                            float halfW = (1f - dy / 65f) * 20f * (1f + ((int)dy % 14) * 0.03f);
                            if (Mathf.Abs(x - px) < halfW)
                                c = dy > 52f || Mathf.Abs(x - px) > halfW - 2.5f ? snow : pine;
                        }
                    }

                    // Cozy cottage with glowing windows in center
                    if (variant != 1)
                    {
                        int cx = 128, cy = 54;
                        if (Mathf.Abs(x - cx) < 26 && y >= cy && y < cy + 28)
                        {
                            c = cottage;
                            if (Mathf.Abs(x - (cx - 10)) < 5 && y >= cy + 8 && y < cy + 18) c = warmWindow;
                            if (Mathf.Abs(x - (cx + 10)) < 5 && y >= cy + 8 && y < cy + 18) c = warmWindow;
                        }
                        // Snowy roof
                        int ry = y - (cy + 28);
                        if (ry >= 0 && ry < 18 && Mathf.Abs(x - cx) < (32 - ry * 1.6f))
                            c = snow;
                    }

                    pixels[(H - 1 - y) * W + x] = ClampColor(c);
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes($"{TexFolder}/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ------------------------------------------------------------------ Moonlit Window Glass View
        private static void GenerateWindowView(string name)
        {
            const int W = 256, H = 256;
            var pixels = new Color[W * H];

            Color skyTop = new Color(0.05f, 0.11f, 0.25f);
            Color skyMid = new Color(0.14f, 0.28f, 0.52f);
            Color skyGlow = new Color(0.32f, 0.52f, 0.78f);
            Color snowHill = new Color(0.75f, 0.86f, 0.98f);
            Color pineDark = new Color(0.06f, 0.16f, 0.22f);
            Color frostCol = new Color(0.86f, 0.94f, 1.0f);

            for (int y = 0; y < H; y++)
            {
                float ty = (float)y / H;
                for (int x = 0; x < W; x++)
                {
                    float tx = (float)x / W;
                    Color c = ty > 0.45f
                        ? Color.Lerp(skyMid, skyTop, (ty - 0.45f) / 0.55f)
                        : Color.Lerp(skyGlow, skyMid, ty / 0.45f);

                    // Moon glow in upper right (y=192 is near top of texture)
                    float md = Vector2.Distance(new Vector2(x, y), new Vector2(185f, 198f));
                    if (md < 18f) c = new Color(1.0f, 0.97f, 0.86f);
                    else if (md < 65f) c = Color.Lerp(c, new Color(0.65f, 0.82f, 1.0f), Mathf.Pow(1f - (md - 18f) / 47f, 2f) * 0.55f);

                    // Distant snowy hills & upright pine silhouettes (base at py, tip at py + 72)
                    float h1 = 62f + Mathf.Sin(tx * 5.5f + 0.5f) * 14f;
                    if (y < h1) c = Color.Lerp(snowHill, skyGlow, 0.25f);

                    for (int p = 0; p < 6; p++)
                    {
                        float px = 22f + p * 42f;
                        float py = 32f + (p % 2) * 10f;
                        float dy = y - py;
                        if (dy >= 0 && dy < 72f)
                        {
                            float halfW = (1f - dy / 72f) * 18f * (1f + ((int)dy % 14) * 0.025f);
                            if (Mathf.Abs(x - px) < halfW)
                                c = dy > 56f || Mathf.Abs(x - px) > halfW - 2f ? snowHill : pineDark;
                        }
                    }

                    // Falling snowflakes & stars
                    if (Hash(x * 311 + y * 619) > 0.991f)
                        c = Color.white;

                    // Soft frost vignette around pane edges
                    float edgeX = Mathf.Min(tx, 1f - tx);
                    float edgeY = Mathf.Min(ty, 1f - ty);
                    float edge = Mathf.Min(edgeX, edgeY);
                    if (edge < 0.08f)
                    {
                        float f = (1f - edge / 0.08f) * (0.35f + 0.35f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                        c = Color.Lerp(c, frostCol, f);
                    }

                    // Store at (H - 1 - y) because Unity's Cube -Z face maps v=0 to top and v=1 to bottom!
                    pixels[(H - 1 - y) * W + x] = ClampColor(c);
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes($"{TexFolder}/{name}.png", tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // ------------------------------------------------------------------ Helpers
        private static void SavePair(string name, Color[] albedo, float[] height, float bumpStrength)
        {
            var texA = new Texture2D(Res, Res, TextureFormat.RGBA32, true);
            texA.SetPixels(albedo);
            texA.Apply();
            File.WriteAllBytes($"{TexFolder}/{name}.png", texA.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texA);

            var normals = new Color[Res * Res];
            for (int y = 0; y < Res; y++)
            {
                int yUp = (y + 1) % Res;
                int yDn = (y - 1 + Res) % Res;
                for (int x = 0; x < Res; x++)
                {
                    int xR = (x + 1) % Res;
                    int xL = (x - 1 + Res) % Res;
                    float dx = (height[y * Res + xR] - height[y * Res + xL]) * bumpStrength;
                    float dy = (height[yUp * Res + x] - height[yDn * Res + x]) * bumpStrength;
                    Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                    normals[y * Res + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            var texN = new Texture2D(Res, Res, TextureFormat.RGBA32, true);
            texN.SetPixels(normals);
            texN.Apply();
            File.WriteAllBytes($"{TexFolder}/{name}_n.png", texN.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texN);
        }

        private static void ConfigureImporters()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;

                bool isNormal = path.EndsWith("_n.png", StringComparison.OrdinalIgnoreCase);
                string fn = Path.GetFileNameWithoutExtension(path);
                bool isPainting = fn.StartsWith("Painting", StringComparison.OrdinalIgnoreCase) || fn.Equals("WindowView", StringComparison.OrdinalIgnoreCase);
                bool dirty = false;

                TextureImporterType targetType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != targetType) { ti.textureType = targetType; dirty = true; }

                TextureWrapMode targetWrap = isPainting ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                if (ti.wrapMode != targetWrap) { ti.wrapMode = targetWrap; dirty = true; }

                if (ti.filterMode != FilterMode.Trilinear) { ti.filterMode = FilterMode.Trilinear; dirty = true; }
                if (ti.anisoLevel != 8) { ti.anisoLevel = 8; dirty = true; }

                if (dirty) ti.SaveAndReimport();
            }
        }

        private static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / (float)0x7fffffff;
        }

        private static Color ClampColor(Color c)
        {
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }
    }
}
#endif
