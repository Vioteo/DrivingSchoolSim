using DrivingSchool.Input;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T66: a held steering key asks for no more than the tyres can give — the car used to slide and lean.</summary>
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
    }
}
