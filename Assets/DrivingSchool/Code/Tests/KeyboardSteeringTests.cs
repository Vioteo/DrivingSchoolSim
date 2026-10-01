using DrivingSchool.Input;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T66: a held steering key asks for no more than the tyres can give — the car used to slide and lean. T71: small taps.</summary>
    public sealed class KeyboardSteeringTests
    {
        const float Wheelbase = 2.72f, MaxSteer = 32f, Lateral = 6.5f;

        static float LateralAt(float kmh)
        {
            float v = kmh / 3.6f, share = KeyboardInputSource.MaxKeyboardSteer(v, Wheelbase, MaxSteer, Lateral);
            return v * v * Mathf.Tan(share * MaxSteer * Mathf.Deg2Rad) / Wheelbase;
        }

        [Test]
        public void FullLockAtParkingSpeedsLessAtSpeed()
        {
            Assert.That(KeyboardInputSource.MaxKeyboardSteer(0f, Wheelbase, MaxSteer, Lateral), Is.EqualTo(1f));
            Assert.That(KeyboardInputSource.MaxKeyboardSteer(10f / 3.6f, Wheelbase, MaxSteer, Lateral), Is.EqualTo(1f));
            float at50 = KeyboardInputSource.MaxKeyboardSteer(50f / 3.6f, Wheelbase, MaxSteer, Lateral);
            Assert.That(at50, Is.LessThan(0.2f), "50 km/h: the old curve gave 0.75 of lock (24° of wheel)");
            Assert.That(at50, Is.GreaterThan(0.1f), "still enough for a town bend");
        }

        [TestCase(30f), TestCase(50f), TestCase(70f), TestCase(90f)]
        public void AHeldKeyStaysWithinTheGrip(float kmh)
        {
            Assert.That(LateralAt(kmh), Is.LessThanOrEqualTo(Lateral + 0.05f));
            Assert.That(LateralAt(kmh), Is.GreaterThan(Lateral * 0.9f));
        }

        const float Dt = 1f / 60f, Rate = 2.5f, Return = 3.5f;   // кадр 60 fps, скорости руления при 100 %

        static float Limit(float kmh) => KeyboardInputSource.MaxKeyboardSteer(kmh / 3.6f, Wheelbase, MaxSteer, Lateral);

        static float Step(float s, int key, float kmh) =>
            KeyboardInputSource.StepKeyboardSteer(s, key, kmh / 3.6f, Dt, Rate, Return, Wheelbase, MaxSteer, Lateral);

        /// <summary>Наибольший угол (доля полного) от нажатия клавиши «вправо» на <paramref name="seconds"/>.</summary>
        static float TapPeak(float kmh, float seconds)
        {
            float s = 0f, peak = 0f;
            int frames = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < frames + 60; i++) { s = Step(s, i < frames ? 1 : 0, kmh); peak = Mathf.Max(peak, s); }
            return peak;
        }

        /// <summary>T71: до исправления на 60 км/ч касание 0,1 с давало 0,7 предела, 0,2 с — весь предел.</summary>
        [TestCase(40f), TestCase(60f), TestCase(90f)]
        public void ATapIsASmallCorrection(float kmh)
        {
            float share = TapPeak(kmh, 0.1f) / Limit(kmh);
            Assert.That(share, Is.LessThan(0.35f), "касание 0,1 с — подруливание, а не поворот");
            Assert.That(share, Is.GreaterThan(0.1f), "но заметное");
            Assert.That(TapPeak(kmh, 0.2f) / Limit(kmh), Is.LessThan(0.6f));
        }

        [TestCase(40f), TestCase(60f), TestCase(90f)]
        public void AHeldKeyReachesTheLimitWithinASecond(float kmh)
        {
            float s = 0f;
            for (int i = 0; i < 60; i++) s = Step(s, 1, kmh);
            Assert.That(s, Is.EqualTo(Limit(kmh)).Within(1e-4f));
        }

        [Test]
        public void ParkingSpeedStillTurnsToFullLockQuickly()
        {
            float s = 0f;
            for (int i = 0; i < 30; i++) s = Step(s, 1, 5f);
            Assert.That(s, Is.GreaterThan(0.9f), "0,5 с на 5 км/ч — почти полный угол, как раньше");
        }

        [TestCase(40f), TestCase(60f), TestCase(90f)]
        public void AReleasedKeyCentresWithinAThirdOfASecond(float kmh)
        {
            float s = Limit(kmh);
            for (int i = 0; i < 20; i++) s = Step(s, 0, kmh);
            Assert.That(s, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TheOppositeKeyFirstReturnsAtTheReturnRate()
        {
            float s = Limit(60f), back = Step(s, -1, 60f), released = Step(s, 0, 60f);
            Assert.That(back, Is.EqualTo(released).Within(1e-6f));
        }
    }
}
