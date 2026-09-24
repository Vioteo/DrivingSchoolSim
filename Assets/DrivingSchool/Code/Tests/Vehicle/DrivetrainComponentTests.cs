using System;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;

namespace DrivingSchool.Tests
{
    public sealed class ClutchModelTests
    {
        [Test] public void PressedPedalTransmitsNothing()
        {
            var c = new ClutchModel(); c.Update(1f, 5000f, 0f, 240f, 0.01f);
            Assert.That(c.TransmittedTorqueNm, Is.EqualTo(0f)); Assert.That(c.IsLockedUp, Is.False);
        }
        [Test] public void HalfPedalGivesHalfCapacityInSlipDirection()
        {
            var c = new ClutchModel(); c.Update(0.5f, 3000f, 1000f, 240f, 0.01f);
            Assert.That(c.CapacityNm, Is.EqualTo(120f).Within(1e-3)); Assert.That(c.TransmittedTorqueNm, Is.EqualTo(120f).Within(1e-3));
        }
        [Test] public void EngineBrakingReversesTorqueSign()
        {
            var c = new ClutchModel(); c.Update(0f, 1500f, 2500f, 240f, 0.01f);
            Assert.That(c.TransmittedTorqueNm, Is.EqualTo(-240f).Within(1e-3));
        }
        [Test] public void EqualSpeedsLockUp()
        {
            var c = new ClutchModel(); c.Update(0f, 2000f, 1997f, 240f, 0.01f);
            Assert.That(c.IsLockedUp, Is.True);
        }
        [Test] public void PedalIsClampedAndNanRejected()
        {
            Assert.That(ClutchModel.Capacity(1.7f, 240f), Is.EqualTo(0f));
            Assert.That(ClutchModel.Capacity(-0.3f, 240f), Is.EqualTo(240f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ClutchModel.Capacity(float.NaN, 240f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClutchModel().Update(0f, float.PositiveInfinity, 0f, 240f, 0.01f));
        }
    }

    public sealed class GearboxModelTests
    {
        static GearboxModel Manual() => new GearboxModel(new[] { 3.6f, 2.1f, 1.4f, 1.05f, 0.84f, 0.69f }, -3.5f, 4.1f, 0.9f, TransmissionType.Manual);
        static GearboxModel Auto() => new GearboxModel(new[] { 3.6f, 2.1f, 1.4f, 1.05f, 0.84f, 0.69f }, -3.5f, 4.1f, 0.9f, TransmissionType.Automatic);

        [Test] public void FirstGearTorque() { var g = Manual(); g.UpdateManual(100f, 1, 0f); Assert.That(g.AxleTorqueNm, Is.EqualTo(1328.4f).Within(0.01)); }
        [Test] public void ReverseTorqueIsNegative() { var g = Manual(); g.UpdateManual(100f, -1, 0f); Assert.That(g.AxleTorqueNm, Is.EqualTo(-1291.5f).Within(0.01)); }
        [Test] public void NeutralTransmitsNothing() { var g = Manual(); g.UpdateManual(180f, 0, 50f); Assert.That(g.AxleTorqueNm, Is.EqualTo(0f)); }
        [Test] public void InvalidGearThrows()
        {
            var g = Manual();
            Assert.Throws<ArgumentOutOfRangeException>(() => g.UpdateManual(1f, 7, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.TryShiftManual(-2, 1f));
        }
        [Test] public void ManualGearNeedsClutchButNeutralDoesNot()
        {
            var g = Manual();
            Assert.That(g.TryShiftManual(1, 0.2f), Is.False); Assert.That(g.LastRequestRefused, Is.True); Assert.That(g.CurrentGear, Is.EqualTo(0));
            Assert.That(g.TryShiftManual(1, 0.9f), Is.True); Assert.That(g.CurrentGear, Is.EqualTo(1));
            Assert.That(g.TryShiftManual(0, 0f), Is.True); Assert.That(g.CurrentGear, Is.EqualTo(0));
        }
        [Test] public void AutomaticUpshiftsAndDownshifts()
        {
            var g = Auto();
            g.UpdateAutomatic(50f, 900f, AutomaticSelector.D, 0f, 0.1f);
            Assert.That(g.CurrentGear, Is.EqualTo(1));
            g.UpdateAutomatic(50f, 2700f, AutomaticSelector.D, 5f, 1f);
            Assert.That(g.CurrentGear, Is.EqualTo(2));
            g.UpdateAutomatic(50f, 1100f, AutomaticSelector.D, 3f, 1f);
            Assert.That(g.CurrentGear, Is.EqualTo(1));
        }
        [Test] public void ParkAndReverseRefusedAtSpeed()
        {
            var g = Auto();
            g.UpdateAutomatic(50f, 2000f, AutomaticSelector.D, 0f, 0.1f);
            g.UpdateAutomatic(50f, 2000f, AutomaticSelector.P, 15f / 3.6f, 0.1f);
            Assert.That(g.Selector, Is.EqualTo(AutomaticSelector.D)); Assert.That(g.LastRequestRefused, Is.True);
            g.UpdateAutomatic(50f, 2000f, AutomaticSelector.R, 10f, 0.1f);
            Assert.That(g.Selector, Is.EqualTo(AutomaticSelector.D));
            g.UpdateAutomatic(50f, 900f, AutomaticSelector.P, 0.2f, 0.1f);
            Assert.That(g.Selector, Is.EqualTo(AutomaticSelector.P)); Assert.That(g.ParkPawlEngaged, Is.True);
        }
    }

    public sealed class SurfaceFrictionTests
    {
        [Test] public void OrderingDryWetSnowIce()
        {
            Assert.That(SurfaceFrictionModel.GetFrictionCoefficient(SurfaceType.DryAsphalt), Is.GreaterThan(SurfaceFrictionModel.GetFrictionCoefficient(SurfaceType.WetAsphalt)));
            Assert.That(SurfaceFrictionModel.GetFrictionCoefficient(SurfaceType.WetAsphalt), Is.GreaterThan(SurfaceFrictionModel.GetFrictionCoefficient(SurfaceType.PackedSnow)));
            Assert.That(SurfaceFrictionModel.GetFrictionCoefficient(SurfaceType.PackedSnow), Is.GreaterThan(SurfaceFrictionModel.GetFrictionCoefficient(SurfaceType.BlackIce)));
        }
        [Test] public void StoppingDistanceGrowsOnWorseSurface()
        {
            float v = 60f / 3.6f;
            Assert.That(SurfaceFrictionModel.CalculateStoppingDistanceM(v, SurfaceType.DryAsphalt), Is.EqualTo(v * v / (2 * 0.95f * 9.81f)).Within(1e-3));
            Assert.That(SurfaceFrictionModel.CalculateStoppingDistanceM(v, SurfaceType.BlackIce), Is.GreaterThan(6f * SurfaceFrictionModel.CalculateStoppingDistanceM(v, SurfaceType.DryAsphalt)));
        }
        [Test] public void BlendIsContinuousAtEnds()
        {
            Assert.That(SurfaceFrictionModel.Blend(SurfaceType.DryAsphalt, SurfaceType.BlackIce, 0f), Is.EqualTo(0.95f).Within(1e-5));
            Assert.That(SurfaceFrictionModel.Blend(SurfaceType.DryAsphalt, SurfaceType.BlackIce, 1f), Is.EqualTo(0.15f).Within(1e-5));
            float e = 1e-3f, d = SurfaceFrictionModel.Blend(SurfaceType.DryAsphalt, SurfaceType.BlackIce, e) - SurfaceFrictionModel.Blend(SurfaceType.DryAsphalt, SurfaceType.BlackIce, 0f);
            Assert.That(Math.Abs(d / e), Is.LessThan(0.01f), "zero slope at the start of the blend");
        }
        [Test] public void NegativeLoadGivesNoForceAndNanThrows()
        {
            Assert.That(SurfaceFrictionModel.CalculateMaxTireForce(-100f, SurfaceType.DryAsphalt), Is.EqualTo(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceFrictionModel.CalculateMaxTireForce(float.NaN, SurfaceType.DryAsphalt));
        }
    }

    public sealed class TireModelTests
    {
        [Test] public void ForceNeverExceedsFrictionCircle()
        {
            for (float s = -20; s <= 20; s += 0.5f)
                for (float vy = -5; vy <= 5; vy += 0.5f)
                {
                    var f = TireModel.Force(s, 10f, vy, 3000f, 0.9f);
                    Assert.That(Math.Sqrt(f.fx * f.fx + f.fy * f.fy), Is.LessThanOrEqualTo(0.9 * 3000 + 1e-2));
                }
        }
        [Test] public void LateralForceOpposesSideSlip()
        {
            Assert.That(TireModel.Force(0f, 10f, 1f, 3000f, 0.9f).fy, Is.LessThan(0f));
            Assert.That(TireModel.Force(0f, 10f, -1f, 3000f, 0.9f).fy, Is.GreaterThan(0f));
        }
        [Test] public void NoLoadNoForce() { var f = TireModel.Force(5f, 10f, 1f, 0f, 0.9f); Assert.That(f.fx, Is.Zero); Assert.That(f.fy, Is.Zero); }
    }
}
