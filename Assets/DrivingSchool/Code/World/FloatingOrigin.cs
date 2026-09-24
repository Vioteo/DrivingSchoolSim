using System;
using UnityEngine;

namespace DrivingSchool.World
{
    /// <summary>
    /// Double-precision 3D vector for canonical SI meter coordinates in large world environments.
    /// </summary>
    [Serializable]
    public struct Vector3d : IEquatable<Vector3d>
    {
        public double x;
        public double y;
        public double z;

        public static readonly Vector3d Zero = new Vector3d(0, 0, 0);
        public static readonly Vector3d One = new Vector3d(1, 1, 1);

        public Vector3d(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public double SqrMagnitude => x * x + y * y + z * z;
        public double Magnitude => Math.Sqrt(SqrMagnitude);

        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3d operator *(Vector3d a, double d) => new Vector3d(a.x * d, a.y * d, a.z * d);
        public static Vector3d operator /(Vector3d a, double d) => new Vector3d(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3d a, Vector3d b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3d a, Vector3d b) => !(a == b);

        public static double Distance(Vector3d a, Vector3d b) => (a - b).Magnitude;

        public bool Equals(Vector3d other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is Vector3d other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }

    /// <summary>
    /// Service and mathematical model for floating origin coordinate systems across 10x10 km territories.
    /// Maintains 64-bit double precision canonical positions while projecting local single-precision float coordinates
    /// centered on the active observer chunk to eliminate rendering jitter and physics precision degradation.
    /// </summary>
    public sealed class FloatingOrigin
    {
        public const int DefaultChunkSizeM = 256;
        public const double DefaultShiftThresholdM = 500.0;

        public int ChunkSizeM { get; }
        public double ShiftThresholdM { get; set; }
        public Vector3d CurrentOrigin { get; private set; }
        public int ShiftCount { get; private set; }

        public event Action<Vector3d, Vector3d, Vector3> OnOriginShifted;

        public FloatingOrigin(int chunkSizeM = DefaultChunkSizeM, double shiftThresholdM = DefaultShiftThresholdM)
        {
            ChunkSizeM = chunkSizeM > 0 ? chunkSizeM : DefaultChunkSizeM;
            ShiftThresholdM = shiftThresholdM > 0.0 ? shiftThresholdM : DefaultShiftThresholdM;
            CurrentOrigin = Vector3d.Zero;
            ShiftCount = 0;
        }

        public void Reset()
        {
            CurrentOrigin = Vector3d.Zero;
            ShiftCount = 0;
        }

        public void SetOrigin(double x, double y, double z)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) ||
                double.IsNaN(y) || double.IsInfinity(y) ||
                double.IsNaN(z) || double.IsInfinity(z))
            {
                return;
            }
            CurrentOrigin = new Vector3d(x, y, z);
        }

        /// <summary>
        /// Converts 64-bit canonical world coordinates to 32-bit local Unity coordinates relative to current origin.
        /// </summary>
        public Vector3 ToLocal(double worldX, double worldY, double worldZ)
        {
            float lx = (float)(worldX - CurrentOrigin.x);
            float ly = (float)(worldY - CurrentOrigin.y);
            float lz = (float)(worldZ - CurrentOrigin.z);
            return new Vector3(lx, ly, lz);
        }

        public Vector3 ToLocal(Vector3d worldPos) => ToLocal(worldPos.x, worldPos.y, worldPos.z);

        /// <summary>
        /// Converts local Unity coordinates back to 64-bit canonical world coordinates.
        /// </summary>
        public Vector3d ToWorld(float localX, float localY, float localZ)
        {
            return new Vector3d(
                CurrentOrigin.x + localX,
                CurrentOrigin.y + localY,
                CurrentOrigin.z + localZ
            );
        }

        public Vector3d ToWorld(Vector3 localPos) => ToWorld(localPos.x, localPos.y, localPos.z);

        /// <summary>
        /// Calculates discrete chunk grid indices for canonical coordinates based on chunk size (256m).
        /// </summary>
        public void GetChunkCoordinates(double worldX, double worldZ, out int chunkX, out int chunkZ)
        {
            chunkX = (int)Math.Floor(worldX / ChunkSizeM);
            chunkZ = (int)Math.Floor(worldZ / ChunkSizeM);
        }

        /// <summary>
        /// Returns the canonical center coordinates for a given chunk index.
        /// </summary>
        public Vector3d GetChunkCenter(int chunkX, int chunkZ)
        {
            double cx = (chunkX + 0.5) * ChunkSizeM;
            double cz = (chunkZ + 0.5) * ChunkSizeM;
            return new Vector3d(cx, 0.0, cz);
        }

        /// <summary>
        /// Checks if focus position exceeds threshold distance from current origin.
        /// If threshold is exceeded, atomically re-centers the origin to the nearest chunk boundary.
        /// Returns true if a shift occurred and populates shiftDelta.
        /// Guards against NaN and Infinity coordinates to prevent origin corruption.
        /// </summary>
        public bool CheckAndShift(double focusWorldX, double focusWorldY, double focusWorldZ, out Vector3 shiftDelta)
        {
            shiftDelta = Vector3.zero;

            if (double.IsNaN(focusWorldX) || double.IsInfinity(focusWorldX) ||
                double.IsNaN(focusWorldY) || double.IsInfinity(focusWorldY) ||
                double.IsNaN(focusWorldZ) || double.IsInfinity(focusWorldZ))
            {
                return false;
            }

            double dx = focusWorldX - CurrentOrigin.x;
            double dz = focusWorldZ - CurrentOrigin.z;
            double distSqr = dx * dx + dz * dz;

            if (distSqr < ShiftThresholdM * ShiftThresholdM)
            {
                return false;
            }

            // Snap new origin to integer multiple of chunk size (256m grid)
            double newOriginX = Math.Round(focusWorldX / ChunkSizeM) * ChunkSizeM;
            double newOriginZ = Math.Round(focusWorldZ / ChunkSizeM) * ChunkSizeM;
            double newOriginY = 0.0;

            return Shift(newOriginX, newOriginY, newOriginZ, out shiftDelta);
        }

        /// <summary>
        /// Atomically shifts the floating origin to explicit canonical world coordinates.
        /// Rejects NaN / Infinity coordinates to prevent origin corruption.
        /// </summary>
        public bool Shift(double newOriginX, double newOriginY, double newOriginZ, out Vector3 shiftDelta)
        {
            shiftDelta = Vector3.zero;

            if (double.IsNaN(newOriginX) || double.IsInfinity(newOriginX) ||
                double.IsNaN(newOriginY) || double.IsInfinity(newOriginY) ||
                double.IsNaN(newOriginZ) || double.IsInfinity(newOriginZ))
            {
                return false;
            }

            var oldOrigin = CurrentOrigin;
            var newOrigin = new Vector3d(newOriginX, newOriginY, newOriginZ);

            shiftDelta = new Vector3(
                (float)(newOrigin.x - oldOrigin.x),
                (float)(newOrigin.y - oldOrigin.y),
                (float)(newOrigin.z - oldOrigin.z)
            );

            CurrentOrigin = newOrigin;
            ShiftCount++;

            OnOriginShifted?.Invoke(oldOrigin, newOrigin, shiftDelta);
            return true;
        }
    }
}
