namespace DrivingSchool.Contracts
{
    // What a signal group shows. Meaning of signals for drivers: ПДД РФ, п. 6.2–6.3 (редакцию сверить при реализации правил).
    // Pedestrian groups use Red, Green, GreenFlashing and Off only.
    public enum SignalAspect { Off, Red, RedAmber, Amber, Green, GreenFlashing, AmberFlashing }
}
