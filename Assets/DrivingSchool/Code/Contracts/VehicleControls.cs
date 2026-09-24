using System;
namespace DrivingSchool.Contracts
{
    public enum TransmissionType { Manual, Automatic }
    public enum AutomaticSelector { P, R, N, D }
    public enum TurnSignal { Off, Left, Right }
    // Parking = габаритные огни; LowBeam = ближний. Дальний — отдельный флаг поверх ближнего.
    public enum HeadlightMode { Off, Parking, LowBeam }
    public enum WiperMode { Off, Interval, Low, High }
}
