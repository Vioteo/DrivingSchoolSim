using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Input
{
    /// <summary>
    /// Turn-signal lever with self-cancel: after the wheel has been turned in the signalled direction
    /// past ArmThreshold, returning it below CancelThreshold switches the signal off. Engine-free.
    /// </summary>
    public sealed class TurnSignalStalk
    {
        public const float ArmThreshold = 0.25f, CancelThreshold = 0.08f;
        bool armed;
        public TurnSignal Position { get; private set; }

        public void Toggle(TurnSignal direction)
        {
            Position = Position == direction ? TurnSignal.Off : direction;
            armed = false;
        }

        public void Cancel() { Position = TurnSignal.Off; armed = false; }

        public TurnSignal Update(float steering)
        {
            if (Position == TurnSignal.Off) return Position;
            float s = Position == TurnSignal.Left ? -steering : steering;
            if (s > ArmThreshold) armed = true;
            else if (armed && s < CancelThreshold) { Position = TurnSignal.Off; armed = false; }
            return Position;
        }
    }
}
