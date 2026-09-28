using System;
namespace DrivingSchool.Contracts
{
    // What the road graph says about the player's car this tick (T65): built by PlayerRoadMonitor (Simulation) from the
    // traffic director, judged by CityRuleMonitor (Rules). Events (Entered*, LaneChange*) are set for one tick only.

    public enum RoadStopPlace { Lane, Crosswalk, BeforeCrosswalk, NoStoppingZone }

    [Serializable] public struct DriverRoadFacts
    {
        public double Seconds, X, Z;
        public float SpeedMps;                    // absolute
        public bool LeftIndicator, RightIndicator, Hazard;

        public bool OnRoad;                       // matched to a lane or a junction connection
        public bool InIntersection;               // on a connection of a junction with turns
        public bool AgainstDirection;             // the centre is on a lane of the opposite direction
        public bool LeftSideOverAxis;             // the left side of the car is over the road axis (oncoming side)
        public MarkingType AxisMarking;           // marking of the axis next to the car (None if the lane has no oncoming lane)
        public int LanesInDirection;              // lanes of the carriageway in the car's direction (0 off road)
        public bool RightmostLane;                // on the outermost lane of its direction (or the only one)
        public float LateralOffsetM;              // from the lane centre, right positive
        public float SpeedLimitKph;               // 0 = unknown (off road)

        // Junctions: set on the tick the car leaves an intersection (the movement is judged by the lanes it used).
        public LaneManeuver JunctionManeuver;     // None: no intersection left this tick
        public double JunctionEnteredSeconds;
        public bool JunctionFromAllowedLane;      // the movement is allowed from the lane the car came from (ПДД 8.5, 5.15.1)
        public LaneManeuver JunctionLaneAllowed;  // what that lane allows (None = unrestricted)
        public bool EnteredRoundabout, LeftRoundabout;

        public int LaneChange;                    // this tick: −1 moved one lane left, +1 right, 0 none
        public bool EnteredOnRed;                 // this tick: drove into an intersection on red or red with amber
        public bool EnteredClosedRailway;         // this tick: drove onto a level crossing while it is closed

        // Standing still.
        public RoadStopPlace StopPlace;           // what the car stands on or right before (worst of them)
        public bool WaitingForTraffic;            // a reason to stand: queue, stop line, pedestrians, closed crossing
        public bool CrosswalkSignalled;           // the crosswalk the car stands before has a pedestrian signal

        // Following.
        public float LeadGapM, LeadSpeedMps;      // bumper to bumper to the vehicle ahead on the car's lane; +inf if none
    }
}
