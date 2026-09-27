using DrivingSchool.Contracts;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T49: блокировка селектора АКПП на клавиатуре — из P только с нажатым тормозом.</summary>
    public class ShiftLockTests
    {
        [Test] public void AutomaticSelectorLeavesParkOnlyWithBrake()
        {
            Assert.That(DrivingSchool.Input.KeyboardInputSource.ShiftLocked(true, AutomaticSelector.P, false), Is.True, "Из P без тормоза — нельзя");
            Assert.That(DrivingSchool.Input.KeyboardInputSource.ShiftLocked(true, AutomaticSelector.P, true), Is.False, "С тормозом — можно");
            Assert.That(DrivingSchool.Input.KeyboardInputSource.ShiftLocked(true, AutomaticSelector.D, false), Is.False, "Из D в N — без тормоза");
            Assert.That(DrivingSchool.Input.KeyboardInputSource.ShiftLocked(false, AutomaticSelector.P, false), Is.False, "Механика не блокируется");
        }
    }
}
