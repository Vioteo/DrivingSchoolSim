using System;

namespace DrivingSchool.Simulation
{
    /// <summary>Wiper blade in windshield plane metres; angles from +u (right) towards +v (up), radians.</summary>
    [Serializable] public struct WiperBlade
    {
        public float pivotU, pivotV, innerRadiusM, outerRadiusM, parkAngleRad, sweepRad; // sweep is signed
        public float AngleAt(float angle01) => parkAngleRad + sweepRad * angle01;
    }

    /// <summary>
    /// Water on the windshield as a coarse grid (0 = dry, 1 = fully wet). Rain adds drops (more with speed),
    /// water slowly runs off, wiper blades clear the sector they sweep. Deterministic for a given seed.
    /// </summary>
    public sealed class WindshieldWetnessModel
    {
        public readonly int Width, Height;
        public readonly float WidthM, HeightM;
        readonly float[] cells;
        readonly Random rng;
        float dropBudget;

        public WindshieldWetnessModel(int width, int height, float widthM, float heightM, int seed = 1)
        {
            if (width < 4 || height < 4 || !(widthM > 0) || !(heightM > 0)) throw new ArgumentException("Invalid windshield grid.");
            Width = width; Height = height; WidthM = widthM; HeightM = heightM;
            cells = new float[width * height]; rng = new Random(seed);
        }

        public float this[int x, int y] => cells[y * Width + x];
        public float[] Cells => cells;

        public float Coverage { get { float s = 0; foreach (var c in cells) s += c; return s / cells.Length; } }

        public void Clear() { Array.Clear(cells, 0, cells.Length); }

        /// <summary>Adds rain; drops per second grow with forward speed (head wind).</summary>
        public void AddRain(float intensity01, float speedMps, float dtSeconds)
        {
            if (float.IsNaN(intensity01) || float.IsNaN(speedMps) || float.IsNaN(dtSeconds) || dtSeconds < 0) throw new ArgumentOutOfRangeException(nameof(intensity01));
            intensity01 = Math.Max(0f, Math.Min(1f, intensity01));
            float perSecond = intensity01 * (300f + 30f * Math.Min(40f, Math.Abs(speedMps))) * (Width * Height / 8192f);
            dropBudget += perSecond * dtSeconds;
            while (dropBudget >= 1f)
            {
                dropBudget -= 1f;
                int cx = rng.Next(Width), cy = rng.Next(Height), r = rng.Next(0, 2);
                for (int y = Math.Max(0, cy - r); y <= Math.Min(Height - 1, cy + r); y++)
                    for (int x = Math.Max(0, cx - r); x <= Math.Min(Width - 1, cx + r); x++)
                    {
                        int i = y * Width + x; cells[i] = Math.Min(1f, cells[i] + 0.6f + 0.4f * (float)rng.NextDouble());
                    }
            }
        }

        /// <summary>Water slowly runs off / evaporates.</summary>
        public void Dry(float ratePerSecond, float dtSeconds)
        {
            float d = Math.Max(0f, ratePerSecond) * dtSeconds;
            for (int i = 0; i < cells.Length; i++) cells[i] = Math.Max(0f, cells[i] - d);
        }

        /// <summary>Clears every cell the blade passed while moving from angle01From to angle01To.</summary>
        public int Wipe(WiperBlade blade, float angle01From, float angle01To)
        {
            float a0 = blade.AngleAt(angle01From), a1 = blade.AngleAt(angle01To);
            float lo = Math.Min(a0, a1), hi = Math.Max(a0, a1);
            float cellM = Math.Max(WidthM / Width, HeightM / Height);
            float pad = cellM / Math.Max(0.05f, blade.outerRadiusM);  // blade thickness in radians at the tip
            lo -= pad; hi += pad;
            int cleared = 0;
            for (int y = 0; y < Height; y++)
            {
                float v = (y + 0.5f) * HeightM / Height - blade.pivotV;
                for (int x = 0; x < Width; x++)
                {
                    float u = (x + 0.5f) * WidthM / Width - blade.pivotU;
                    float r = (float)Math.Sqrt(u * u + v * v);
                    if (r < blade.innerRadiusM || r > blade.outerRadiusM) continue;
                    float a = (float)Math.Atan2(v, u);
                    // bring a into [lo - π, lo + π) so the comparison works across the ±π seam
                    while (a < lo - Math.PI) a += 2f * (float)Math.PI;
                    while (a >= lo + Math.PI) a -= 2f * (float)Math.PI;
                    if (a < lo || a > hi) continue;
                    int i = y * Width + x;
                    if (cells[i] > 0f) { cells[i] = 0f; cleared++; }
                }
            }
            return cleared;
        }
    }
}
