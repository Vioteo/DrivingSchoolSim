using System;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Input;
using DrivingSchool.Learning;
using DrivingSchool.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T42: руль G29 поверх клавиатуры — раскладка как данные, поиск устройства, оси, подсказки уроков для руля.</summary>
    public class WheelInputTests
    {
        const string G29Caps = "{\"vendorId\":1133,\"productId\":49743,\"usage\":4,\"usagePage\":1}";

        [Test] public void DefaultProfileBindsEveryActionWithLabel()
        {
            var p = new WheelProfile();
            foreach (WheelAction a in Enum.GetValues(typeof(WheelAction)))
            {
                Assert.That(p.Find(a), Is.Not.Null, a.ToString());
                Assert.That(p.Label(a), Is.Not.Empty, a.ToString());
            }
            Assert.That(p.buttons.Select(b => b.control).Distinct().Count(), Is.EqualTo(p.buttons.Count), "Одна кнопка — одно действие");
            Assert.That(p.Label(WheelAction.LeftSignal), Does.Contain("лепест"), "Поворотники на лепестках, как в CCD");
        }

        [Test] public void DeviceIsMatchedByVendorAndProductNotBySimilarName()
        {
            var p = new WheelProfile();
            Assert.That(WheelDevice.TryIds(G29Caps, out int vid, out int pid) && vid == 0x046D && pid == 0xC24F);
            Assert.That(WheelDevice.Matches("Logitech G29 Driving Force Racing Wheel", G29Caps, p), Is.True);
            Assert.That(WheelDevice.Matches("Logitech G920 Driving Force Racing Wheel", "{\"vendorId\":1133,\"productId\":49762}", p), Is.False, "G920 — другая раскладка");
            Assert.That(WheelDevice.Matches("Some Racing Wheel", "{\"vendorId\":1234,\"productId\":1}", p), Is.False, "Чужое устройство");
            Assert.That(WheelDevice.Matches("Some Racing Wheel", null, p), Is.False, "Похожее имя без VID/PID");
            Assert.That(WheelDevice.Matches("Logitech G29 Driving Force Racing Wheel", null, p), Is.True, "Без VID/PID — по названию");
        }

        [Test] public void ProfileFileIsWrittenAndMissingActionsAreFilled()
        {
            string path = Path.Combine(Path.GetTempPath(), "ds-wheel-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                WheelProfile.FilePath = path;
                var first = WheelProfile.Load();
                Assert.That(File.Exists(path), "Раскладка по умолчанию записана в файл");
                first.buttons.RemoveAll(b => b.action == nameof(WheelAction.Horn));
                first.Find(WheelAction.LeftSignal).control = "button99";
                File.WriteAllText(path, JsonUtility.ToJson(first, true));
                var edited = WheelProfile.Load();
                Assert.That(edited.Find(WheelAction.LeftSignal).control, Is.EqualTo("button99"), "Правка игрока сохраняется");
                Assert.That(edited.Find(WheelAction.Horn), Is.Not.Null, "Недостающее действие берётся по умолчанию");
                File.WriteAllText(path, "не json");
                Assert.That(WheelProfile.Load().Find(WheelAction.Ignition), Is.Not.Null, "Битый файл → раскладка по умолчанию");
            }
            finally
            {
                WheelProfile.FilePath = null;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test] public void SteeringAndPedalShaping()
        {
            var t = new WheelTuning();
            Assert.That(t.ShapeSteering(0.5f), Is.EqualTo(0.5f).Within(1e-4), "900° — один к одному");
            t.steeringLockDeg = 450f;
            Assert.That(t.ShapeSteering(0.25f), Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(t.ShapeSteering(-0.9f), Is.EqualTo(-1f), "Упор");
            t.steeringLockDeg = 900f; t.steeringDeadzone = 0.05f;
            Assert.That(t.ShapeSteering(0.04f), Is.Zero);
            Assert.That(t.ShapeSteering(1f), Is.EqualTo(1f).Within(1e-4));
            t.steeringDeadzone = 0f; t.steeringLinearity = 1f;
            Assert.That(t.ShapeSteering(0.5f), Is.LessThan(0.5f), "Нелинейность — точнее в центре");
            Assert.That(t.ShapePedal(0.02f), Is.Zero, "Мёртвая зона педали 3 %");
            Assert.That(t.ShapePedal(1f), Is.EqualTo(1f).Within(1e-4));
            t.invertPedals = true;
            Assert.That(t.ShapePedal(0f), Is.EqualTo(1f).Within(1e-4));
        }

        [Test] public void LessonHintsNameWheelControlsForEveryKey()
        {
            var p = new WheelProfile();
            foreach (var k in GuidedText.Keys) Assert.That(LessonControls.WheelName(k, p), Is.Not.Null.And.Not.Empty, k);
            Assert.That(LessonControls.KeyName("clutch", true), Is.EqualTo("педаль сцепления"));
            Assert.That(LessonControls.KeyName("clutch", false), Is.EqualTo("Shift"));
            Assert.That(LessonControls.Format("Включите указатель — {left}.", true), Is.EqualTo("Включите указатель — <b>[" + WheelProfile.Current.Label(WheelAction.LeftSignal) + "]</b>."));
            Assert.That(LessonControls.Format("Включите указатель — {left}.", false), Is.EqualTo("Включите указатель — <b>[Q]</b>."));
        }

        [Test] public void GearRequestKeepsShiftLock()
        {
            var k = new KeyboardInputSource { automatic = true };
            Assert.That(k.RequestGear(1, brakeHeld: false), Is.False);
            Assert.That(k.ShiftLockRefused, Is.True);
            Assert.That(k.RequestGear(1, brakeHeld: true), Is.True);
            Assert.That(k.Read(1).selector, Is.EqualTo(AutomaticSelector.D));
            k.SelectPark();
            Assert.That(k.Read(2).selector, Is.EqualTo(AutomaticSelector.P));
        }

        sealed class FakeOverlay : IControlOverlay
        {
            public int resyncs;
            public bool IsConnected => true;
            public void ApplyAxes(KeyboardInputSource t) => t.SetAnalog(0.3f, 0.7f, 0f, 1f);
            public void ApplyButtons(KeyboardInputSource t) { t.SetHeld(true, false, false, false); }
            public void Resync() => resyncs++;
        }

        [Test] public void OverlayReplacesAxesAndAddsHeldButtons()
        {
            var fake = new FakeOverlay();
            var k = new KeyboardInputSource { Overlay = fake };
            Assert.That(k.IsConnected, Is.True);
            k.Poll(0.016f);
            var cmd = k.Read(1);
            Assert.That(cmd.steering, Is.EqualTo(0.3f).Within(1e-4));
            Assert.That(cmd.throttle, Is.EqualTo(0.7f).Within(1e-4));
            Assert.That(cmd.clutch, Is.EqualTo(1f).Within(1e-4));
            Assert.That(cmd.starter, Is.True, "Стартер с кнопки руля");
            k.ResetToParked();
            Assert.That(fake.resyncs, Is.EqualTo(1), "Респаун сбрасывает положение рычага");
        }
    }
}
