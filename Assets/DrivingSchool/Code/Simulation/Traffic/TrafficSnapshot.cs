using System;
using System.Collections.Generic;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.Traffic
{
    public enum ParticipantKind { Vehicle, Player, Pedestrian }
    public enum SimulationLod { Near, Background }

    /// <summary>Density and behaviour mix of a district (game parameters, data-driven: traffic-profile.json).</summary>
    public sealed class TrafficProfile
    {
        public int MaxVehicles = 8;
        public double SpawnIntervalSeconds = 2;
        public double MinSpawnDistanceM = 150;     // spawn/despawn freely beyond this distance from the player
        public double HiddenSpawnDistanceM = 40;   // ...or beyond this one when outside the player's view cone
        public double ViewConeDeg = 120;
        public double NearRadiusM = 250;           // full behaviour inside, background (1-2 Hz, IDM only) outside
        public double DecisionHz = 10;
        public double BackgroundHz = 2;
        public double RouteHorizonM = 200;
        public double JunctionLookaheadM = 35;
        public double DeadlockSeconds = 6;         // game rule: circular waiting longer than this -> lowest id goes
        public double HazardNoticeRangeM = 120;
        public double ChunkSizeM = 256;
        public DriverProfile[] Drivers = { DriverProfile.Normal() };
    }

    /// <summary>What the host tells the director about the player's car each tick.</summary>
    public struct PlayerSample
    {
        public double X, Y, Z;
        public double HeadingRad;
        public double SpeedMps;
        public bool LeftIndicator, RightIndicator, Hazard;
        public double LengthM, WidthM;
        public bool Present;
    }

    /// <summary>One participant as everyone sees it this tick.</summary>
    public sealed class ParticipantState
    {
        public string Id;
        public ParticipantKind Kind;
        public string PathId;                // null when off the graph
        public double S, D, SpeedMps, AccelerationMps2, HeadingRad, LengthM, WidthM;
        public Vec3d Position;
        public bool LeftIndicator, RightIndicator, BrakeLight, Hazard;
        public float SteeringRad;
        public SimulationLod Lod;
        public string DriverProfileId;
        public string Decision;              // human-readable reason for the debug overlay (T38)
    }

    public struct SignalState { public string GroupId; public SignalAspect Aspect; }

    public sealed class TrafficEvent { public string Kind, ParticipantId, OtherId; public double SimSeconds; }

    /// <summary>Immutable picture of the district after a tick: for presentation, rules (T18) and debugging (T38).</summary>
    public sealed class TrafficSnapshot
    {
        public long Tick;
        public double SimSeconds;
        public IReadOnlyList<ParticipantState> Participants = Array.Empty<ParticipantState>();
        public IReadOnlyList<SignalState> Signals = Array.Empty<SignalState>();
        public IReadOnlyList<ReservationTable.Entry> Reservations = Array.Empty<ReservationTable.Entry>();
        public IReadOnlyList<ManeuverPermit> Permits = Array.Empty<ManeuverPermit>();
        public IReadOnlyList<ManeuverNotice> Notices = Array.Empty<ManeuverNotice>();
        public IReadOnlyList<TrafficEvent> Events = Array.Empty<TrafficEvent>();
    }
}
