using System;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Input;
using DrivingSchool.Simulation;

namespace DrivingSchool.Tests
{
    public sealed class VehicleLightsTests
    {
        [Test] public void IndicatorFlashesAtOnePointFiveHertz()
        {
            var l = new VehicleLightsModel(); int edges = 0; bool prev = false;
            for (int i = 0; i < 400; i++)
            {
                l.Update(new DriverCommand { ignition = true, turnSignal = TurnSignal.Left }, false, 0.01f);
                if (i == 0) Assert.That(l.LampOn, Is.True, "first flash is immediate");
                if (l.LampOn && !prev) edges++; prev = l.LampOn;
                Assert.That(l.RightActive, Is.False);
            }
            Assert.That(edges, Is.EqualTo(6), "4 s × 1.5 Hz");
        }
        [Test] public void IndicatorsNeedIgnitionButHazardDoesNot()
        {
            var l = new VehicleLightsModel();
            l.Update(new DriverCommand { turnSignal = TurnSignal.Right }, false, 0.01f);
            Assert.That(l.RightActive, Is.False);
            l.Update(new DriverCommand { hazard = true }, false, 0.01f);
            Assert.That(l.LeftActive && l.RightActive, Is.True);
        }
        [Test] public void HeadlightModes()
        {
            var l = new VehicleLightsModel();
            l.Update(new DriverCommand { headlights = HeadlightMode.Parking }, false, 0.01f);
            Assert.That(l.Parking, Is.True); Assert.That(l.LowBeam, Is.False);
            l.Update(new DriverCommand { ignition = true, headlights = HeadlightMode.LowBeam, highBeam = true }, false, 0.01f);
            Assert.That(l.LowBeam && l.HighBeam, Is.True);
            l.Update(new DriverCommand { ignition = true, headlights = HeadlightMode.Parking, highBeam = true }, false, 0.01f);
            Assert.That(l.HighBeam, Is.False, "high beam only on top of low beam");
            l.Update(new DriverCommand { ignition = true, flashHighBeam = true }, false, 0.01f);
            Assert.That(l.HighBeam, Is.True, "flash works with lights off");
        }
        [Test] public void BrakeAndReverseLights()
        {
            var l = new VehicleLightsModel();
            l.Update(new DriverCommand { brake = 0.3f }, false, 0.01f); Assert.That(l.Brake, Is.True);
            l.Update(new DriverCommand { brake = 0.01f }, false, 0.01f); Assert.That(l.Brake, Is.False);
            l.Update(new DriverCommand { ignition = true }, true, 0.01f); Assert.That(l.Reverse, Is.True);
        }
    }

    public sealed class TurnSignalStalkTests
    {
        [Test] public void SelfCancelsAfterTurnCompleted()
        {
            var s = new TurnSignalStalk(); s.Toggle(TurnSignal.Left);
            Assert.That(s.Update(0f), Is.EqualTo(TurnSignal.Left));
            Assert.That(s.Update(-0.6f), Is.EqualTo(TurnSignal.Left));
            Assert.That(s.Update(-0.02f), Is.EqualTo(TurnSignal.Off));
        }
        [Test] public void OppositeSteeringDoesNotCancel()
        {
            var s = new TurnSignalStalk(); s.Toggle(TurnSignal.Right);
            s.Update(-0.8f); Assert.That(s.Update(0f), Is.EqualTo(TurnSignal.Right));
        }
        [Test] public void ToggleSameDirectionSwitchesOff()
        {
            var s = new TurnSignalStalk(); s.Toggle(TurnSignal.Right); s.Toggle(TurnSignal.Right);
            Assert.That(s.Position, Is.EqualTo(TurnSignal.Off));
        }
    }

    public sealed class WiperTests
    {
        [Test] public void LowModeSweepsAndParksWhenSwitchedOff()
        {
            var w = new WiperModel(); float max = 0;
            for (int i = 0; i < 100; i++) { w.Update(WiperMode.Low, false, true, 0.01f); max = Math.Max(max, w.Angle01); }
            Assert.That(max, Is.GreaterThan(0.95f));
            for (int i = 0; i < 300; i++) w.Update(WiperMode.Off, false, true, 0.01f);
            Assert.That(w.Moving, Is.False); Assert.That(w.Angle01, Is.EqualTo(0f));
        }
        [Test] public void HighIsFasterThanLow()
        {
            var lo = new WiperModel(); var hi = new WiperModel();
            for (int i = 0; i < 1000; i++) { lo.Update(WiperMode.Low, false, true, 0.01f); hi.Update(WiperMode.High, false, true, 0.01f); }
            Assert.That(hi.CompletedCycles, Is.GreaterThan(lo.CompletedCycles));
        }
        [Test] public void WasherGivesThreeSweepsAndNoPowerFreezes()
        {
            var w = new WiperModel();
            w.Update(WiperMode.Off, true, true, 0.01f);
            for (int i = 0; i < 1000; i++) w.Update(WiperMode.Off, false, true, 0.01f);
            Assert.That(w.CompletedCycles, Is.EqualTo(WiperModel.WasherSweeps));
            var p = new WiperModel(); for (int i = 0; i < 30; i++) p.Update(WiperMode.High, false, true, 0.01f);
            float a = p.Angle01; p.Update(WiperMode.High, false, false, 0.5f);
            Assert.That(p.Angle01, Is.EqualTo(a));
        }
        [Test] public void IntervalWaitsBeforeSweeping()
        {
            var w = new WiperModel();
            for (int i = 0; i < 300; i++) w.Update(WiperMode.Interval, false, true, 0.01f);
            Assert.That(w.Moving, Is.False);
            for (int i = 0; i < 150; i++) w.Update(WiperMode.Interval, false, true, 0.01f);
            Assert.That(w.Moving || w.CompletedCycles > 0, Is.True);
        }
    }

    public sealed class WindshieldWetnessTests
    {
        static WiperBlade Blade() => new WiperBlade { pivotU = 0.35f, pivotV = 0.02f, innerRadiusM = 0.08f, outerRadiusM = 0.55f, parkAngleRad = 0.05f, sweepRad = 1.9f };

        [Test] public void RainWetsGlassFasterWhenDriving()
        {
            var still = new WindshieldWetnessModel(64, 32, 1.4f, 0.7f, 3); var moving = new WindshieldWetnessModel(64, 32, 1.4f, 0.7f, 3);
            for (int i = 0; i < 100; i++) { still.AddRain(0.5f, 0f, 0.02f); moving.AddRain(0.5f, 20f, 0.02f); }
            Assert.That(still.Coverage, Is.GreaterThan(0.05f));
            Assert.That(moving.Coverage, Is.GreaterThan(still.Coverage));
        }
        [Test] public void FullWipeClearsSweptSectorOnly()
        {
            var m = new WindshieldWetnessModel(64, 32, 1.4f, 0.7f, 5);
            for (int i = 0; i < 400; i++) m.AddRain(1f, 10f, 0.02f);
            float before = m.Coverage;
            var b = Blade(); float prev = 0;
            for (float a = 0.05f; a <= 1.0001f; a += 0.05f) { m.Wipe(b, prev, a); prev = a; }
            Assert.That(m.Coverage, Is.LessThan(before * 0.8f));
            // a cell in the middle of the swept arc is dry, the far right corner is untouched
            int cx = (int)((b.pivotU + 0.3f * (float)Math.Cos(1.0)) / 1.4f * 64), cy = (int)((b.pivotV + 0.3f * (float)Math.Sin(1.0)) / 0.7f * 32);
            Assert.That(m[cx, cy], Is.EqualTo(0f));
            Assert.That(m[63, 31], Is.GreaterThan(0f));
        }
        [Test] public void DryingReducesWater()
        {
            var m = new WindshieldWetnessModel(16, 8, 1f, 0.5f);
            for (int i = 0; i < 100; i++) m.AddRain(1f, 0f, 0.05f);
            float c = m.Coverage; m.Dry(0.5f, 1f);
            Assert.That(m.Coverage, Is.LessThan(c));
        }
    }
}
