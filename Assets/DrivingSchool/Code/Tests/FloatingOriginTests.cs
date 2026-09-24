using System;
using NUnit.Framework;
using UnityEngine;
using DrivingSchool.World;

namespace DrivingSchool.Tests
{
    [TestFixture]
    public class FloatingOriginTests
    {
        [Test]
        public void Canonical_ToUnityAndToWorld_RoundTripPrecision()
        {
            var origin = new FloatingOrigin(256, 500.0);
            origin.SetOrigin(2048.0, 0.0, -1024.0);

            // Arbitrary double coordinates in 10x10 km territory
            double worldX = 4850.123456;
            double worldY = 12.5;
            double worldZ = -4920.654321;

            Vector3 local = origin.ToLocal(worldX, worldY, worldZ);
            Vector3d roundTrip = origin.ToWorld(local);

            Assert.That(roundTrip.x, Is.EqualTo(worldX).Within(1e-4), "X coordinate round-trip must preserve sub-millimeter precision");
            Assert.That(roundTrip.y, Is.EqualTo(worldY).Within(1e-4), "Y coordinate round-trip must preserve sub-millimeter precision");
            Assert.That(roundTrip.z, Is.EqualTo(worldZ).Within(1e-4), "Z coordinate round-trip must preserve sub-millimeter precision");
        }

        [Test]
        public void ChunkIndices_AlignsWith256mGrid()
        {
            var origin = new FloatingOrigin(256, 500.0);

            origin.GetChunkCoordinates(0.0, 0.0, out int cx0, out int cz0);
            Assert.That(cx0, Is.Zero);
            Assert.That(cz0, Is.Zero);

            origin.GetChunkCoordinates(255.9, 255.9, out int cx1, out int cz1);
            Assert.That(cx1, Is.Zero);
            Assert.That(cz1, Is.Zero);

            origin.GetChunkCoordinates(256.0, 512.0, out int cx2, out int cz2);
            Assert.That(cx2, Is.EqualTo(1));
            Assert.That(cz2, Is.EqualTo(2));

            // Negative coordinates
            origin.GetChunkCoordinates(-1.0, -256.0, out int cxNeg, out int czNeg);
            Assert.That(cxNeg, Is.EqualTo(-1));
            Assert.That(czNeg, Is.EqualTo(-1));

            origin.GetChunkCoordinates(-257.0, -513.0, out int cxNeg2, out int czNeg2);
            Assert.That(cxNeg2, Is.EqualTo(-2));
            Assert.That(czNeg2, Is.EqualTo(-3));

            // 10x10 km corner
            origin.GetChunkCoordinates(4800.0, -4800.0, out int cxCorner, out int czCorner);
            Assert.That(cxCorner, Is.EqualTo(18));
            Assert.That(czCorner, Is.EqualTo(-19));
        }

        [Test]
        public void ChunkCenter_ReturnsExactCoordinates()
        {
            var origin = new FloatingOrigin(256, 500.0);

            var c0 = origin.GetChunkCenter(0, 0);
            Assert.That(c0.x, Is.EqualTo(128.0).Within(1e-6));
            Assert.That(c0.z, Is.EqualTo(128.0).Within(1e-6));

            var c1 = origin.GetChunkCenter(2, -3);
            Assert.That(c1.x, Is.EqualTo(640.0).Within(1e-6));  // (2 + 0.5) * 256 = 640
            Assert.That(c1.z, Is.EqualTo(-640.0).Within(1e-6)); // (-3 + 0.5) * 256 = -640
        }

        [Test]
        public void Shift_TriggersWhenDistanceThresholdExceeded()
        {
            var origin = new FloatingOrigin(256, 500.0);
            Assert.That(origin.CurrentOrigin, Is.EqualTo(Vector3d.Zero));
            Assert.That(origin.ShiftCount, Is.Zero);

            // Move within threshold (400m < 500m)
            bool shifted1 = origin.CheckAndShift(400.0, 0.0, 0.0, out var delta1);
            Assert.That(shifted1, Is.False);
            Assert.That(origin.ShiftCount, Is.Zero);

            // Move beyond threshold (520m >= 500m)
            bool shifted2 = origin.CheckAndShift(520.0, 0.0, 0.0, out var delta2);
            Assert.That(shifted2, Is.True);
            Assert.That(origin.ShiftCount, Is.EqualTo(1));

            // Snapped to nearest 256m chunk quantum (520 / 256 = 2.031 -> round is 2 * 256 = 512)
            Assert.That(origin.CurrentOrigin.x, Is.EqualTo(512.0).Within(1e-6));
            Assert.That(delta2.x, Is.EqualTo(512.0f).Within(1e-4f));

            // Player local position is now centered close to zero
            Vector3 playerLocal = origin.ToLocal(520.0, 0.0, 0.0);
            Assert.That(playerLocal.x, Is.EqualTo(8.0f).Within(1e-4f));
        }

        [Test]
        public void Shift_MaintainsCanonicalWorldPositionsOfLandmarks()
        {
            var origin = new FloatingOrigin(256, 500.0);

            // Landmark at canonical coordinate
            double landmarkX = 1200.0;
            double landmarkY = 5.0;
            double landmarkZ = 800.0;

            Vector3 localBefore = origin.ToLocal(landmarkX, landmarkY, landmarkZ);
            Assert.That(localBefore.x, Is.EqualTo(1200.0f).Within(1e-3f));

            // Player triggers shift by moving to (600, 0, 600)
            origin.CheckAndShift(600.0, 0.0, 600.0, out var shiftDelta);
            Assert.That(origin.ShiftCount, Is.EqualTo(1));

            // Landmark local position after shift
            Vector3 localAfter = origin.ToLocal(landmarkX, landmarkY, landmarkZ);

            // Reconstruct canonical position from localAfter
            Vector3d reconstructed = origin.ToWorld(localAfter);
            Assert.That(reconstructed.x, Is.EqualTo(landmarkX).Within(1e-4));
            Assert.That(reconstructed.y, Is.EqualTo(landmarkY).Within(1e-4));
            Assert.That(reconstructed.z, Is.EqualTo(landmarkZ).Within(1e-4));

            // Verify local shift offset relationship
            Assert.That(localBefore.x - localAfter.x, Is.EqualTo(shiftDelta.x).Within(1e-3f));
            Assert.That(localBefore.z - localAfter.z, Is.EqualTo(shiftDelta.z).Within(1e-3f));
        }

        [Test]
        public void Shift_VelocityIsInvariantAcrossOriginShift()
        {
            var origin = new FloatingOrigin(256, 500.0);

            double p1 = 495.0; // t = 0
            double p2 = 505.0; // t = 1, crosses 500m threshold
            double dt = 1.0;
            double canonicalVelocity = (p2 - p1) / dt; // 10 m/s

            Vector3 localP1 = origin.ToLocal(p1, 0, 0);

            // Shift occurs at p2
            origin.CheckAndShift(p2, 0, 0, out var delta);
            Vector3 localP2 = origin.ToLocal(p2, 0, 0);

            // Local velocity accounting for delta offset
            float reconstructedVelocity = (localP2.x + delta.x - localP1.x) / (float)dt;

            Assert.That(reconstructedVelocity, Is.EqualTo((float)canonicalVelocity).Within(1e-4f),
                "Physical velocity must be completely invariant across origin shift");
        }

        [Test]
        public void Shift_Traversing10kmHighwayGeneratesPeriodicShifts()
        {
            var origin = new FloatingOrigin(256, 500.0);

            // Simulate car driving from X = -4500 to X = +4500 across 10x10 km world
            int shiftEvents = 0;
            origin.OnOriginShifted += (oldO, newO, d) => shiftEvents++;

            for (double x = -4500.0; x <= 4500.0; x += 50.0)
            {
                origin.CheckAndShift(x, 0.0, 0.0, out _);

                // Invariance check: player local coordinates relative to current origin NEVER exceed ~500m
                Vector3 local = origin.ToLocal(x, 0.0, 0.0);
                Assert.That(Math.Abs(local.x), Is.LessThanOrEqualTo(500.0f + 1e-3f),
                    $"Local coordinate at world X={x} exceeded safe float bounds: {local.x}");
            }

            Assert.That(shiftEvents, Is.GreaterThan(10), "Traversing 9 km must trigger multiple origin shifts");
            Assert.That(origin.ShiftCount, Is.EqualTo(shiftEvents));
        }

        [Test]
        public void CheckAndShift_GuardsAgainstNanAndInfinity_PreventsOriginCorruption()
        {
            var origin = new FloatingOrigin(256, 500.0);
            var initialOrigin = origin.CurrentOrigin;

            // NaN inputs
            bool shiftedNanX = origin.CheckAndShift(double.NaN, 0.0, 0.0, out var deltaNanX);
            Assert.That(shiftedNanX, Is.False, "CheckAndShift must return false on NaN focus coordinate");
            Assert.That(deltaNanX, Is.EqualTo(Vector3.zero));
            Assert.That(origin.CurrentOrigin, Is.EqualTo(initialOrigin), "Origin must not be corrupted by NaN");

            bool shiftedNanZ = origin.CheckAndShift(0.0, 0.0, double.NaN, out _);
            Assert.That(shiftedNanZ, Is.False);
            Assert.That(origin.CurrentOrigin, Is.EqualTo(initialOrigin));

            // Infinity inputs
            bool shiftedInf = origin.CheckAndShift(double.PositiveInfinity, 0.0, 0.0, out _);
            Assert.That(shiftedInf, Is.False, "CheckAndShift must return false on Infinity focus coordinate");
            Assert.That(origin.CurrentOrigin, Is.EqualTo(initialOrigin));
        }

        [Test]
        public void Shift_GuardsAgainstNanAndInfinity_PreventsOriginCorruption()
        {
            var origin = new FloatingOrigin(256, 500.0);
            var initialOrigin = origin.CurrentOrigin;

            bool shifted = origin.Shift(double.NaN, 0.0, double.NaN, out var delta);
            Assert.That(shifted, Is.False, "Shift must return false on NaN origin target");
            Assert.That(delta, Is.EqualTo(Vector3.zero));
            Assert.That(origin.CurrentOrigin, Is.EqualTo(initialOrigin));

            bool shiftedInf = origin.Shift(double.NegativeInfinity, 0.0, 100.0, out _);
            Assert.That(shiftedInf, Is.False);
            Assert.That(origin.CurrentOrigin, Is.EqualTo(initialOrigin));
        }
    }
}
