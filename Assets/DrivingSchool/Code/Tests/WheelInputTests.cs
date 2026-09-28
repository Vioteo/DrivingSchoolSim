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
            foreach (DriveAction a in Enum.GetValues(typeof(DriveAction)))
            {
                if (DriveActions.IsAxis(a)) continue;
                Assert.That(p.Find(a), Is.Not.Null, a.ToString());
                if (a != DriveAction.Neutral) Assert.That(p.Label(a), Is.Not.Empty, a.ToString());
            }
            var bound = p.buttons.Where(b => !string.IsNullOrEmpty(b.control)).Select(b => b.control).ToList();
            Assert.That(bound.Distinct().Count(), Is.EqualTo(bound.Count), "Одна кнопка — одно действие");
            Assert.That(WheelProfile.FriendlyName("trigger"), Is.EqualTo("✕"), "Кнопка 1 в HID Input System называется trigger");
            Assert.That(p.Label(DriveAction.LeftSignal), Does.Contain("лепест"), "Поворотники на лепестках, как в CCD");
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
                first.buttons.RemoveAll(b => b.action == nameof(DriveAction.Horn));
                first.throttle.calibrated = true; first.throttle.rawReleased = 1f; first.throttle.rawPressed = -1f;
                first.Find(DriveAction.LeftSignal).control = "button99";
                File.WriteAllText(path, JsonUtility.ToJson(first, true));
                var edited = WheelProfile.Load();
                Assert.That(edited.Find(DriveAction.LeftSignal).control, Is.EqualTo("button99"), "Правка игрока сохраняется");
                Assert.That(edited.Find(DriveAction.Horn), Is.Not.Null, "Недостающее действие берётся по умолчанию");
                Assert.That(edited.throttle.calibrated && edited.throttle.rawPressed == -1f, "Калибровка оси сохраняется в файле");
                File.WriteAllText(path, "не json");
                Assert.That(WheelProfile.Load().Find(DriveAction.Ignition), Is.Not.Null, "Битый файл → раскладка по умолчанию");
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
            var k = new KeyboardProfile();
            foreach (var key in GuidedText.Keys)
            {
                Assert.That(LessonControls.WheelName(key, p), Is.Not.Null.And.Not.Empty, key);
                Assert.That(LessonControls.KeyboardName(key, k), Is.Not.Null.And.Not.Empty, key);
            }
            Assert.That(LessonControls.WheelName("clutch", p), Is.EqualTo("педаль сцепления"));
            Assert.That(LessonControls.WheelName("left", p), Is.EqualTo("левый лепесток"));
            Assert.That(LessonControls.KeyboardName("clutch", k), Is.EqualTo("Shift"));
            Assert.That(LessonControls.KeyboardName("left", k), Is.EqualTo("Q"));
            Assert.That(LessonControls.KeyboardName("handbrake", k), Is.EqualTo("Пробел"));
            p.Assign(DriveAction.LeftSignal, "button8");
            Assert.That(LessonControls.WheelName("left", p), Is.EqualTo("L2"), "После переназначения подсказка называет новую кнопку");
        }

        [Test] public void AssigningTakenButtonOrKeyFreesPreviousAction()
        {
            var p = new WheelProfile();
            var taken = p.Assign(DriveAction.Horn, "button6");
            Assert.That(taken, Is.EqualTo(DriveAction.LeftSignal));
            Assert.That(p.Label(DriveAction.LeftSignal), Is.Null, "Левый поворотник больше не на лепестке");
            Assert.That(p.Label(DriveAction.Horn), Is.EqualTo("левый лепесток"));

            var k = new KeyboardProfile();
            Assert.That(k.Assign(DriveAction.Horn, UnityEngine.InputSystem.Key.Q), Is.EqualTo(DriveAction.LeftSignal));
            Assert.That(k.Primary(DriveAction.LeftSignal), Is.EqualTo(UnityEngine.InputSystem.Key.None));
            Assert.That(k.Label(DriveAction.Horn), Is.EqualTo("Q"));
            Assert.That(k.Assign(DriveAction.SteerLeft, UnityEngine.InputSystem.Key.LeftArrow), Is.Null, "Своя запасная клавиша не конфликт");
            Assert.That(k.Alternate(DriveAction.SteerLeft), Is.EqualTo(UnityEngine.InputSystem.Key.None));
        }

        [Test] public void KeyLabelsAreReadable()
        {
            Assert.That(KeyboardProfile.KeyLabel(UnityEngine.InputSystem.Key.Digit1), Is.EqualTo("1"));
            Assert.That(KeyboardProfile.KeyLabel(UnityEngine.InputSystem.Key.Digit0), Is.EqualTo("0"));
            Assert.That(KeyboardProfile.KeyLabel(UnityEngine.InputSystem.Key.Numpad5), Is.EqualTo("Num 5"));
            Assert.That(KeyboardProfile.KeyLabel(UnityEngine.InputSystem.Key.LeftShift), Is.EqualTo("Shift"));
        }

        [Test] public void GearRequestKeepsShiftLock()
        {
            var k = new KeyboardInputSource { automatic = true, Keys = new KeyboardProfile() };
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
            var k = new KeyboardInputSource { Overlay = fake, Keys = new KeyboardProfile() };
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
