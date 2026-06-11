using System.Collections.Generic;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public static class ProceduralTextureLibrary
    {
        private static Texture2D s_concrete512;
        private static Texture2D s_paintedWall256;
        private static Texture2D s_woodGrain256;
        private static Texture2D s_ceilingTile256;
        private static Texture2D s_hazardStripe128;
        private static Texture2D s_exitSign128;
        private static Texture2D s_blobShadow64;
        private static Texture2D s_radiationPoster256;
        private static Texture2D s_safetyPoster256;
        private static Texture2D s_detectorPlaque256;

        public static Texture2D Concrete512
        {
            get
            {
                if (s_concrete512 == null)
                {
                    s_concrete512 = BuildConcrete512();
                }
                return s_concrete512;
            }
        }

        public static Texture2D PaintedWall256
        {
            get
            {
                if (s_paintedWall256 == null)
                {
                    s_paintedWall256 = BuildPaintedWall256();
                }
                return s_paintedWall256;
            }
        }

        public static Texture2D WoodGrain256
        {
            get
            {
                if (s_woodGrain256 == null)
                {
                    s_woodGrain256 = BuildWoodGrain256();
                }
                return s_woodGrain256;
            }
        }

        public static Texture2D CeilingTile256
        {
            get
            {
                if (s_ceilingTile256 == null)
                {
                    s_ceilingTile256 = BuildCeilingTile256();
                }
                return s_ceilingTile256;
            }
        }

        public static Texture2D HazardStripe128
        {
            get
            {
                if (s_hazardStripe128 == null)
                {
                    s_hazardStripe128 = BuildHazardStripe128();
                }
                return s_hazardStripe128;
            }
        }

        public static Texture2D ExitSign128
        {
            get
            {
                if (s_exitSign128 == null)
                {
                    s_exitSign128 = BuildExitSign128();
                }
                return s_exitSign128;
            }
        }

        public static Texture2D BlobShadow64
        {
            get
            {
                if (s_blobShadow64 == null)
                {
                    s_blobShadow64 = BuildBlobShadow64();
                }
                return s_blobShadow64;
            }
        }

        public static Texture2D RadiationPoster256
        {
            get
            {
                if (s_radiationPoster256 == null)
                {
                    s_radiationPoster256 = BuildRadiationPoster256();
                }
                return s_radiationPoster256;
            }
        }

        public static Texture2D SafetyPoster256
        {
            get
            {
                if (s_safetyPoster256 == null)
                {
                    s_safetyPoster256 = BuildSafetyPoster256();
                }
                return s_safetyPoster256;
            }
        }

        public static Texture2D DetectorPlaque256
        {
            get
            {
                if (s_detectorPlaque256 == null)
                {
                    s_detectorPlaque256 = BuildDetectorPlaque256();
                }
                return s_detectorPlaque256;
            }
        }

        private static Texture2D BuildConcrete512()
        {
            const int size = 512;
            System.Random rng = new System.Random(20461);
            float[] mottleOx = NoiseOffsets(rng, 4, size, 0.012f);
            float[] mottleOy = NoiseOffsets(rng, 4, size, 0.012f);
            float stainOx = NoiseOffsets(rng, 1, size, 0.004f)[0];
            float stainOy = NoiseOffsets(rng, 1, size, 0.004f)[0];

            float[] values = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float v = 0.62f;
                    float mottle = FractalTileableNoise(x, y, size, 0.012f, 4, mottleOx, mottleOy);
                    v += (mottle - 0.5f) * 0.10f;
                    float stain = TileableNoise(x, y, size, 0.004f, stainOx, stainOy);
                    if (stain < 0.38f)
                    {
                        v -= (0.38f - stain) * 0.35f;
                    }
                    v += ((float)rng.NextDouble() - 0.5f) * 0.035f;
                    values[y * size + x] = v;
                }
            }

            // Hairline cracks as wrapped random walks.
            for (int c = 0; c < 5; c++)
            {
                float cx = rng.Next(size);
                float cy = rng.Next(size);
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                int steps = 160 + rng.Next(120);
                for (int s = 0; s < steps; s++)
                {
                    angle += ((float)rng.NextDouble() - 0.5f) * 0.45f;
                    cx += Mathf.Cos(angle);
                    cy += Mathf.Sin(angle);
                    int px = (((int)cx) % size + size) % size;
                    int py = (((int)cy) % size + size) % size;
                    values[py * size + px] *= 0.84f;
                }
            }

            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = MakeColor(values[i], values[i], values[i]);
            }
            return CreateTexture("ConcreteProc512", size, pixels, TextureWrapMode.Repeat);
        }

        private static Texture2D BuildPaintedWall256()
        {
            const int size = 256;
            System.Random rng = new System.Random(8821);
            float[] noiseOx = NoiseOffsets(rng, 2, size, 0.03f);
            float[] noiseOy = NoiseOffsets(rng, 2, size, 0.03f);
            float streakOx = NoiseOffsets(rng, 1, size, 0.05f)[0];
            float streakOy = NoiseOffsets(rng, 1, size, 0.05f)[0];

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float v = 0.92f;
                    float n = FractalTileableNoise(x, y, size, 0.03f, 2, noiseOx, noiseOy);
                    v += (n - 0.5f) * 0.06f;
                    float streak = TileableNoise(x, 0f, size, 0.05f, streakOx, streakOy);
                    v += (streak - 0.5f) * 0.03f;
                    pixels[y * size + x] = MakeColor(v, v, v);
                }
            }
            return CreateTexture("PaintedWallProc256", size, pixels, TextureWrapMode.Repeat);
        }

        private static Texture2D BuildWoodGrain256()
        {
            const int size = 256;
            System.Random rng = new System.Random(3137);
            float[] plankShift = new float[4];
            for (int i = 0; i < 4; i++)
            {
                plankShift[i] = ((float)rng.NextDouble() - 0.5f) * 0.10f;
            }
            float distortOx = NoiseOffsets(rng, 1, size, 0.02f)[0];
            float distortOy = NoiseOffsets(rng, 1, size, 0.02f)[0];

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int plank = y / 64;
                int yLocal = y % 64;
                for (int x = 0; x < size; x++)
                {
                    float distort = TileableNoise(x, y, size, 0.02f, distortOx, distortOy);
                    float grain = Mathf.Sin(yLocal * 0.55f + distort * 5.0f + plank * 2.3f);
                    float shade = 1.0f + plankShift[plank] + grain * 0.06f;
                    shade += ((float)rng.NextDouble() - 0.5f) * 0.025f;
                    float r = 0.46f * shade;
                    float g = 0.31f * shade;
                    float b = 0.18f * shade;
                    if (yLocal < 2)
                    {
                        r *= 0.45f;
                        g *= 0.45f;
                        b *= 0.45f;
                    }
                    pixels[y * size + x] = MakeColor(r, g, b);
                }
            }
            return CreateTexture("WoodGrainProc256", size, pixels, TextureWrapMode.Repeat);
        }

        private static Texture2D BuildCeilingTile256()
        {
            const int size = 256;
            System.Random rng = new System.Random(5503);

            float[] values = new float[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float v = 0.89f + ((float)rng.NextDouble() - 0.5f) * 0.05f;
                    values[y * size + x] = v;
                }
            }

            // Sparse pinholes, wrapped so the tile stays seamless.
            for (int i = 0; i < 170; i++)
            {
                int px = rng.Next(size);
                int py = rng.Next(size);
                values[py * size + px] *= 0.5f;
                values[((py + 1) % size) * size + px] *= 0.75f;
                values[((py + size - 1) % size) * size + px] *= 0.75f;
                values[py * size + (px + 1) % size] *= 0.75f;
                values[py * size + (px + size - 1) % size] *= 0.75f;
            }

            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float v = values[i];
                pixels[i] = MakeColor(v, v * 0.99f, v * 0.96f);
            }
            return CreateTexture("CeilingTileProc256", size, pixels, TextureWrapMode.Repeat);
        }

        private static Texture2D BuildHazardStripe128()
        {
            const int size = 128;
            System.Random rng = new System.Random(9173);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wear = ((float)rng.NextDouble() - 0.5f) * 0.04f;
                    bool yellow = ((x + y) / 16) % 2 == 0;
                    if (yellow)
                    {
                        pixels[y * size + x] = MakeColor(0.9f + wear, 0.78f + wear, 0.1f + wear);
                    }
                    else
                    {
                        pixels[y * size + x] = MakeColor(0.07f + wear, 0.07f + wear, 0.07f + wear);
                    }
                }
            }
            return CreateTexture("HazardStripeProc128", size, pixels, TextureWrapMode.Repeat);
        }

        private static Texture2D BuildExitSign128()
        {
            const int size = 128;
            Color32 housing = MakeColor(0.05f, 0.05f, 0.05f);
            Color32 face = MakeColor(0.0f, 0.25f, 0.08f);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (x < 8 || x >= size - 8 || y < 8 || y >= size - 8)
                    {
                        pixels[y * size + x] = housing;
                    }
                    else
                    {
                        pixels[y * size + x] = face;
                    }
                }
            }
            DrawText(pixels, size, size, "EXIT", 64, 64, 4, MakeColor(0.4f, 1.0f, 0.5f));
            return CreateTexture("ExitSignProc128", size, pixels, TextureWrapMode.Clamp);
        }

        private static Texture2D BuildBlobShadow64()
        {
            const int size = 64;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size - 0.5f;
                    float ny = (y + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.Pow(Mathf.Clamp01(1.0f - r / 0.5f), 1.6f) * 0.6f;
                    pixels[y * size + x] = new Color32(0, 0, 0, ToByte(a));
                }
            }
            return CreateTexture("BlobShadowProc64", size, pixels, TextureWrapMode.Clamp);
        }

        private static Texture2D BuildRadiationPoster256()
        {
            const int size = 256;
            Color32 bg = MakeColor(0.95f, 0.9f, 0.6f);
            Color32 black = MakeColor(0.05f, 0.05f, 0.05f);
            Color32 magenta = MakeColor(0.72f, 0.0f, 0.5f);
            const int trefoilCx = 128;
            const int trefoilCy = 170;

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color32 c = bg;
                    if (x < 8 || x >= size - 8 || y < 8 || y >= size - 8)
                    {
                        c = black;
                    }
                    else
                    {
                        float dx = x - trefoilCx;
                        float dy = y - trefoilCy;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r <= 10.0f)
                        {
                            c = magenta;
                        }
                        else if (r >= 16.0f && r <= 48.0f)
                        {
                            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                            float a = Mathf.Repeat(angle - 90.0f, 120.0f);
                            if (a <= 30.0f || a >= 90.0f)
                            {
                                c = magenta;
                            }
                        }
                    }
                    pixels[y * size + x] = c;
                }
            }
            DrawText(pixels, size, size, "CAUTION", 128, 96, 2, black);
            DrawText(pixels, size, size, "RADIATION AREA", 128, 60, 2, black);
            return CreateTexture("RadiationPosterProc256", size, pixels, TextureWrapMode.Clamp);
        }

        private static Texture2D BuildSafetyPoster256()
        {
            const int size = 256;
            System.Random rng = new System.Random(4451);
            Color32 paper = MakeColor(0.97f, 0.97f, 0.97f);
            Color32 header = MakeColor(0.1f, 0.25f, 0.55f);
            Color32 barGray = MakeColor(0.8f, 0.8f, 0.8f);

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (y >= size - 48)
                    {
                        pixels[y * size + x] = header;
                    }
                    else
                    {
                        pixels[y * size + x] = paper;
                    }
                }
            }

            // Light gray bars suggesting body text.
            for (int i = 0; i < 6; i++)
            {
                int yLow = 178 - i * 24;
                int width = 150 + rng.Next(70);
                for (int y = yLow; y < yLow + 8; y++)
                {
                    for (int x = 24; x < 24 + width; x++)
                    {
                        pixels[y * size + x] = barGray;
                    }
                }
            }

            DrawText(pixels, size, size, "SAFETY FIRST", 128, 232, 2, MakeColor(1.0f, 1.0f, 1.0f));
            return CreateTexture("SafetyPosterProc256", size, pixels, TextureWrapMode.Clamp);
        }

        private static Texture2D BuildDetectorPlaque256()
        {
            const int size = 256;
            System.Random rng = new System.Random(7741);
            float ox = (float)(rng.NextDouble() * 64.0);
            float oy = (float)(rng.NextDouble() * 64.0);

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float v = 0.18f;
                    v += (Mathf.PerlinNoise(ox + x * 0.013f, oy + y * 0.85f) - 0.5f) * 0.07f;
                    v += ((float)rng.NextDouble() - 0.5f) * 0.02f;
                    if (x < 3 || x >= size - 3 || y < 3 || y >= size - 3)
                    {
                        v = 0.5f;
                    }
                    pixels[y * size + x] = MakeColor(v, v, v);
                }
            }

            Color32 amber = MakeColor(0.95f, 0.8f, 0.45f);
            DrawText(pixels, size, size, "RADIATION", 128, 150, 2, amber);
            DrawText(pixels, size, size, "DETECTOR", 128, 106, 2, amber);
            return CreateTexture("DetectorPlaqueProc256", size, pixels, TextureWrapMode.Clamp);
        }

        private static Texture2D CreateTexture(string name, int size, Color32[] pixels, TextureWrapMode wrapMode)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = name;
            tex.wrapMode = wrapMode;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 2;
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        // Periodic by construction: bilinear blend of four wrapped Perlin samples.
        private static float TileableNoise(float x, float y, float size, float frequency, float offsetX, float offsetY)
        {
            float u = x / size;
            float v = y / size;
            float n00 = Mathf.PerlinNoise(offsetX + x * frequency, offsetY + y * frequency);
            float n10 = Mathf.PerlinNoise(offsetX + (x - size) * frequency, offsetY + y * frequency);
            float n01 = Mathf.PerlinNoise(offsetX + x * frequency, offsetY + (y - size) * frequency);
            float n11 = Mathf.PerlinNoise(offsetX + (x - size) * frequency, offsetY + (y - size) * frequency);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
        }

        private static float FractalTileableNoise(int x, int y, int size, float baseFrequency, int octaves, float[] offsetsX, float[] offsetsY)
        {
            float sum = 0.0f;
            float amplitude = 1.0f;
            float total = 0.0f;
            float frequency = baseFrequency;
            for (int i = 0; i < octaves; i++)
            {
                sum += TileableNoise(x, y, size, frequency, offsetsX[i], offsetsY[i]) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.0f;
            }
            return sum / total;
        }

        private static float[] NoiseOffsets(System.Random rng, int octaves, float size, float baseFrequency)
        {
            float[] offsets = new float[octaves];
            float frequency = baseFrequency;
            for (int i = 0; i < octaves; i++)
            {
                offsets[i] = size * frequency + (float)(rng.NextDouble() * 64.0);
                frequency *= 2.0f;
            }
            return offsets;
        }

        private static byte ToByte(float v)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255.0f);
        }

        private static Color32 MakeColor(float r, float g, float b)
        {
            return new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
        }

        // 5x7 glyphs, one byte per row (top first), bit 4 = leftmost column.
        private static readonly Dictionary<char, byte[]> Font = new Dictionary<char, byte[]>
        {
            { ' ', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 } },
            { 'A', new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 } },
            { 'B', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E } },
            { 'C', new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E } },
            { 'D', new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E } },
            { 'E', new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F } },
            { 'F', new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 } },
            { 'G', new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0E } },
            { 'H', new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 } },
            { 'I', new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E } },
            { 'J', new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C } },
            { 'K', new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 } },
            { 'L', new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F } },
            { 'M', new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 } },
            { 'N', new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 } },
            { 'O', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E } },
            { 'P', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 } },
            { 'Q', new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D } },
            { 'R', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 } },
            { 'S', new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E } },
            { 'T', new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 } },
            { 'U', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E } },
            { 'V', new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 } },
            { 'W', new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 } },
            { 'X', new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 } },
            { 'Y', new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 } },
            { 'Z', new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F } },
            { '0', new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E } },
            { '1', new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E } },
            { '2', new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F } },
            { '3', new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E } },
            { '4', new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 } },
            { '5', new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E } },
            { '6', new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E } },
            { '7', new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 } },
            { '8', new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E } },
            { '9', new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C } },
        };

        private static void DrawText(Color32[] pixels, int textureWidth, int textureHeight, string text, int centerX, int centerY, int scale, Color32 color)
        {
            int advance = 6 * scale;
            int textWidth = (text.Length * 6 - 1) * scale;
            int textHeight = 7 * scale;
            int startX = centerX - textWidth / 2;
            int startY = centerY - textHeight / 2;
            for (int i = 0; i < text.Length; i++)
            {
                byte[] glyph;
                if (!Font.TryGetValue(char.ToUpperInvariant(text[i]), out glyph))
                {
                    continue;
                }
                int glyphX = startX + i * advance;
                for (int row = 0; row < 7; row++)
                {
                    int rowBits = glyph[row];
                    int baseY = startY + (6 - row) * scale;
                    for (int col = 0; col < 5; col++)
                    {
                        if ((rowBits & (0x10 >> col)) == 0)
                        {
                            continue;
                        }
                        for (int sy = 0; sy < scale; sy++)
                        {
                            int py = baseY + sy;
                            if (py < 0 || py >= textureHeight)
                            {
                                continue;
                            }
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = glyphX + col * scale + sx;
                                if (px < 0 || px >= textureWidth)
                                {
                                    continue;
                                }
                                pixels[py * textureWidth + px] = color;
                            }
                        }
                    }
                }
            }
        }
    }
}
