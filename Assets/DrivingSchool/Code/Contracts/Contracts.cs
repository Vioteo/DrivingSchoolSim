using System;
namespace DrivingSchool.Contracts
{
    // SI units; Unity coordinates: X right, Y up, Z forward. Clutch 1 = disengaged.
    [Serializable] public struct DriverCommand
    {
        public long sequence;
        public float steering, throttle, brake, clutch;
        public bool handbrake, ignition, starter;
        public int requestedGear; // -1 R, 0 N, 1..6; explicit selection, not increment.
        // Automatic transmission selector; ignored by a manual gearbox. default = P.
        public AutomaticSelector selector;
        // Body controls are explicit driver actions, never derived from steering angle.
        public TurnSignal turnSignal;
        public bool hazard;
        public HeadlightMode headlights;
        public bool highBeam, flashHighBeam, horn, seatbelt, washer;
        public WiperMode wipers;
        public void Validate()
        {
            if (!Finite(steering)||!Unit(throttle)||!Unit(brake)||!Unit(clutch)||steering < -1 || steering > 1 || requestedGear < -1 || requestedGear > 6)
                throw new ArgumentOutOfRangeException("DriverCommand");
            if (!Enum.IsDefined(typeof(AutomaticSelector), selector) || !Enum.IsDefined(typeof(TurnSignal), turnSignal) ||
                !Enum.IsDefined(typeof(HeadlightMode), headlights) || !Enum.IsDefined(typeof(WiperMode), wipers))
                throw new ArgumentOutOfRangeException("DriverCommand");
        }
        static bool Finite(float v) { return !float.IsNaN(v)&&!float.IsInfinity(v); }
        static bool Unit(float v) { return Finite(v)&&v>=0&&v<=1; }
    }
    public enum EnginePhase { Off, Ignition, Cranking, Running, Stalled }
    [Serializable] public struct VehicleState
    {
        public long tick; public double simulationSeconds;
        public float signedSpeedMps, engineRpm, steeringRadians, clutchTorqueNm;
        public int gear; public EnginePhase engine;
        public bool leftIndicator, rightIndicator, lowBeam, highBeam, brakeLight;
        // leftIndicator/rightIndicator = signal switched on (incl. hazard); indicatorLampOn = flasher phase.
        public bool indicatorLampOn, hazard, parkingLights, reverseLight, horn, seatbelt, handbrake;
        public TransmissionType transmission; public AutomaticSelector selector;
        public WiperMode wipers; public float wiperAngle01; // 0 = parked, 1 = full sweep
        public float engineTorqueNm, wheelSpeedFrontRadS, wheelSpeedRearRadS;
        public float fuelLitres, fuelFlowLitresPerHour; // fuel in the tank; instantaneous consumption (0 on overrun cut-off)
    }
    public interface IInputSource { DriverCommand Read(long tick); bool IsConnected { get; } }
    public interface IForceFeedbackOutput : IDisposable
    {
        bool IsAvailable { get; }
        void SetNormalizedTorque(float torque); // [-1,1], backend clamps; not physical Nm.
        void Stop(); // Must be idempotent; call on focus loss, pause, disconnect and dispose.
    }
    [Serializable] public sealed class WorldDocument
    {
        public int schemaVersion=1; public string id,name; public int chunkSizeM=256;
        public RoadNode[] nodes=Array.Empty<RoadNode>();
        public RoadSegment[] segments=Array.Empty<RoadSegment>();
        public Lane[] lanes=Array.Empty<Lane>();
        public WorldObject[] objects=Array.Empty<WorldObject>();
        public District[] districts=Array.Empty<District>();
    }
    [Serializable] public sealed class RoadNode { public string id; public double x,y,z; }
    [Serializable] public sealed class RoadSegment { public string id,fromNode,toNode; public float widthM,speedLimitKph; public int laneCount; }
    [Serializable] public sealed class Lane { public string id,segmentId,fromNode,toNode; public int index; public float widthM; public string[] successors=Array.Empty<string>(); }
    [Serializable] public sealed class WorldObject { public string id,catalogId; public double x,y,z; public float yawDeg; }
    [Serializable] public sealed class District { public string id; public double minX,minZ; public float sizeM; }
    [Serializable] public sealed class RuleEvent
    {
        public string id,ruleId,ruleRevision,participantId,evidenceId,explanationKey;
        public double simulationSeconds,x,y,z; public int penalty;
    }
    [Serializable] public sealed class LessonDefinition
    {
        public int schemaVersion=1; public string id,title,worldId,contentStatus;
        public float timeLimitSeconds,targetDistanceM,stopSpeedMps,requiredStopSeconds;
    }
    public enum SessionPhase { Briefing, Ready, Running, Passed, Failed, Cancelled }
    [Serializable] public sealed class SessionResult
    {
        public string lessonId,reason; public SessionPhase phase;
        public float elapsedSeconds; public string[] enabledAssists=Array.Empty<string>();
        public RuleEvent[] events=Array.Empty<RuleEvent>();
    }
    [Serializable] public sealed class TheoryContentPack
    {
        public int schemaVersion=1; public string id,revision,source; public bool isOfficial;
        public TheoryQuestion[] questions=Array.Empty<TheoryQuestion>();
    }
    [Serializable] public sealed class TheoryQuestion
    {
        public string id,text,explanation,ruleReference,topic;
        public string[] answers=Array.Empty<string>(); public int correctIndex;
    }
}
