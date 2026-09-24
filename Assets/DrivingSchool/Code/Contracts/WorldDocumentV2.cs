using System;
namespace DrivingSchool.Contracts
{
    // Road graph v2 (T11 + T27). SI units, Unity axes (X right, Y up, Z forward), double world coordinates.
    // JsonUtility-friendly: public fields, arrays of plain DTOs, enums as ints, no dictionaries or polymorphism.
    // Lane direction: index > 0 runs fromNode -> toNode of its segment, index < 0 runs back; |index| 1 is next to the axis.

    [Serializable] public struct Vec3d
    {
        public double x, y, z;
        public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    }

    [Flags] public enum LaneManeuver { None = 0, Straight = 1, Right = 2, Left = 4, UTurn = 8 }
    public enum MarkingType { None, Solid, Dashed, DoubleSolid, SolidDashed, DashedSolid }
    public enum BoundarySide { Left, Right }
    public enum ApproachPriority { Equal, Main, Secondary, Signalized }
    public enum SignalGroupKind { Vehicle, VehicleArrow, Pedestrian }
    public enum ZoneKind { Parking, NoStopping, NoParking, KeepJunctionClear }
    public enum SpawnRole { Vehicle, Pedestrian }
    public enum SpawnEdge { Interior, DistrictEdge }

    [Serializable] public sealed class WorldDocumentV2
    {
        public int schemaVersion = 2;
        public string id, name, revision;
        public int chunkSizeM = 256;
        public RoadNode[] nodes = Array.Empty<RoadNode>();
        public RoadSegmentV2[] segments = Array.Empty<RoadSegmentV2>();
        public LaneV2[] lanes = Array.Empty<LaneV2>();
        public Junction[] junctions = Array.Empty<Junction>();
        public LaneConnection[] connections = Array.Empty<LaneConnection>();
        public ConflictZone[] conflictZones = Array.Empty<ConflictZone>();
        public StopLine[] stopLines = Array.Empty<StopLine>();
        public PedestrianCrossing[] crossings = Array.Empty<PedestrianCrossing>();
        public SignalGroup[] signalGroups = Array.Empty<SignalGroup>();
        public SignalPlan[] signalPlans = Array.Empty<SignalPlan>();
        public TrafficSignalAttachment[] signals = Array.Empty<TrafficSignalAttachment>();
        // T27 city semantics.
        public LaneBoundary[] boundaries = Array.Empty<LaneBoundary>();
        public SignPlacement[] signs = Array.Empty<SignPlacement>();
        public JunctionApproach[] approaches = Array.Empty<JunctionApproach>();
        public SidewalkPath[] sidewalks = Array.Empty<SidewalkPath>();
        public Zone[] zones = Array.Empty<Zone>();
        public SpawnPoint[] spawnPoints = Array.Empty<SpawnPoint>();
        public WorldObject[] objects = Array.Empty<WorldObject>();
        public District[] districts = Array.Empty<District>();
    }

    // Cubic Bezier in world coordinates; straight segments keep p1/p2 at 1/3 and 2/3 of the chord.
    [Serializable] public struct CubicCurve { public Vec3d p0, p1, p2, p3; }

    [Serializable] public sealed class RoadSegmentV2
    {
        public string id, fromNode, toNode;
        public CubicCurve curve;
        public float speedLimitKph, laneWidthM;
        public int lanesForward, lanesBackward;
    }

    [Serializable] public sealed class LaneV2
    {
        public string id, segmentId;
        public int index;
        public float widthM, speedLimitKph;
        public Vec3d[] centerline = Array.Empty<Vec3d>();
        public string[] successors = Array.Empty<string>(); // direct continuation lanes (start == this end), outside junctions
        public string leftNeighborId, rightNeighborId;      // same-direction neighbours, empty if none
        public string oncomingLaneId;                       // lane across the axis, empty if none
        public LaneManeuver allowedManeuvers = LaneManeuver.None; // None = not restricted
    }

    [Serializable] public sealed class Junction
    {
        public string id, nodeId, signalPlanId;
        public string[] connectionIds = Array.Empty<string>();
    }

    [Serializable] public sealed class LaneConnection
    {
        public string id, junctionId, fromLaneId, toLaneId, signalGroupId;
        public LaneManeuver maneuver;
        public float speedLimitKph;
        public Vec3d[] centerline = Array.Empty<Vec3d>();
    }

    // Two connections overlap here; s ranges are measured along each connection's centerline.
    [Serializable] public sealed class ConflictZone
    {
        public string id, junctionId, connectionA, connectionB;
        public float fromSA, toSA, fromSB, toSB;
        public bool merge; // both end in the same lane
    }

    [Serializable] public sealed class StopLine { public string id, laneId; public float s; }

    [Serializable] public sealed class PedestrianCrossing
    {
        public string id, signalGroupId;
        public Vec3d a, b;
        public float widthM;
        public string[] laneIds = Array.Empty<string>();       // lanes and connections the walkway crosses
        public string[] sidewalkIds = Array.Empty<string>();
    }

    [Serializable] public sealed class SignalGroup
    {
        public string id, junctionId;
        public SignalGroupKind kind;
        public string[] connectionIds = Array.Empty<string>();
        public string[] crossingIds = Array.Empty<string>();
    }

    [Serializable] public sealed class SignalPlan
    {
        public string id, junctionId;
        public float offsetSeconds;
        public SignalStage[] stages = Array.Empty<SignalStage>();
    }

    // One stage: listed groups get green; afterwards green flashes, then amber, then all-red.
    [Serializable] public sealed class SignalStage
    {
        public string[] greenGroupIds = Array.Empty<string>();
        public float greenSeconds, greenFlashSeconds, amberSeconds, allRedSeconds, redAmberSeconds;
    }

    [Serializable] public sealed class TrafficSignalAttachment
    {
        public string id, signalGroupId, catalogId;
        public double x, y, z; public float yawDeg;
    }

    [Serializable] public sealed class LaneBoundary
    {
        public string id, laneId;
        public BoundarySide side;
        public MarkingType type;
        public float fromS, toS;
    }

    [Serializable] public sealed class SignPlacement
    {
        public string id, code, value, catalogId;
        public string[] plaques = Array.Empty<string>();
        public double x, y, z; public float yawDeg;
        public string[] laneIds = Array.Empty<string>();
        public float atS;
        public bool untilNextJunction;
        public string zoneEndLaneId; public float zoneEndS;
    }

    [Serializable] public sealed class JunctionApproach
    {
        public string id, junctionId, laneId, stopLineId;
        public ApproachPriority priority;
        public string[] sourceSignIds = Array.Empty<string>();
    }

    [Serializable] public sealed class SidewalkPath
    {
        public string id;
        public float widthM;
        public Vec3d[] points = Array.Empty<Vec3d>();
        public string[] linkedIds = Array.Empty<string>(); // sidewalks or crossings touching either end
    }

    [Serializable] public sealed class Zone
    {
        public string id, laneId;
        public ZoneKind kind;
        public float fromS, toS;
    }

    [Serializable] public sealed class SpawnPoint
    {
        public string id, pathId; // lane or sidewalk id
        public SpawnRole role;
        public SpawnEdge edge;
        public float s;
    }
}
