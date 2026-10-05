using System;
namespace DrivingSchool.Contracts
{
    // District layout (T29): which road kit modules are placed where and how their sockets are joined.
    // Compiled into WorldDocumentV2 by DistrictCompiler. JsonUtility-friendly.

    [Serializable] public sealed class DistrictLayout
    {
        public int schemaVersion = 1;
        public string id, name, revision;
        public ModuleInstance[] instances = Array.Empty<ModuleInstance>();
        public SocketJoin[] joins = Array.Empty<SocketJoin>();
        public SocketRef[] openSockets = Array.Empty<SocketRef>(); // district edge: traffic enters/leaves here
        public LayoutSign[] signs = Array.Empty<LayoutSign>();
        public LayoutApproach[] approaches = Array.Empty<LayoutApproach>();
        public LayoutSignalPlan[] signalPlans = Array.Empty<LayoutSignalPlan>();
        // T74: lanes for route vehicles, their stopping places and routes.
        public LayoutBusLane[] busLanes = Array.Empty<LayoutBusLane>();
        public LayoutTransitStop[] transitStops = Array.Empty<LayoutTransitStop>();
        public LayoutTransitRoute[] transitRoutes = Array.Empty<LayoutTransitRoute>();
    }

    // Pose of the prefab root: position of the local origin and Unity yaw (degrees, 0 = +Z, 90 = +X).
    [Serializable] public sealed class ModuleInstance
    {
        public string id, catalogId;
        public double x, y, z;
        public float yawDeg;
        public float speedLimitKph; // 0 = template default
    }

    [Serializable] public sealed class SocketRef { public string instanceId, socket; }

    [Serializable] public sealed class SocketJoin { public string instanceA, socketA, instanceB, socketB; }

    // Sign on a lane of an instance; the compiler places it on the right sidewalk and resolves its zone.
    [Serializable] public sealed class LayoutSign
    {
        public string id, code, value, instanceId, laneId;
        public string[] plaques = Array.Empty<string>();
        public float atS;
        public bool untilNextJunction;
    }

    // Priority of one approach (socket) of a junction instance, backed by signs from the layout.
    [Serializable] public sealed class LayoutApproach
    {
        public string instanceId, socket;
        public ApproachPriority priority;
        public string[] signIds = Array.Empty<string>();
    }

    // Traffic light plan of a junction instance. Vehicle groups are per approach socket, pedestrian groups per crossing arm.
    [Serializable] public sealed class LayoutSignalPlan
    {
        public string instanceId;
        public float offsetSeconds;
        public LayoutSignalStage[] stages = Array.Empty<LayoutSignalStage>();
    }

    [Serializable] public sealed class LayoutSignalStage
    {
        public string[] greenSockets = Array.Empty<string>(); // approaches with green
        public string[] walkSockets = Array.Empty<string>();  // crossings (by arm socket) with pedestrian green
        public float greenSeconds, greenFlashSeconds, amberSeconds, allRedSeconds, redAmberSeconds;
    }

    // A lane of an instance reserved for route vehicles (T74). entry: separated by a broken line (others may move onto it
    // to turn right, after entering the road); otherwise by a solid line.
    [Serializable] public sealed class LayoutBusLane
    {
        public string instanceId, laneId;
        public bool entry;
    }

    // Stopping place on a lane of an instance (T74): the compiler adds the sign 5.16 at atS and, when marked, records the
    // marking 1.17.1 over lengthM before it.
    [Serializable] public sealed class LayoutTransitStop
    {
        public string id, name, instanceId, laneId;
        public float atS, lengthM = 20;
        public bool marked = true, shelter = true;
    }

    // Route through stops in order (T74); the way between them is the shortest one the compiler finds, then back to the first.
    [Serializable] public sealed class LayoutTransitRoute
    {
        public string id, name;
        public TransitKind kind;
        public string[] stopIds = Array.Empty<string>();
    }
}
