using System;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;

namespace DrivingSchool.Presentation.Physics
{
    /// <summary>
    /// Unity adapter of the pure VehicleSolver (T10). Four raycast suspensions give normal loads and contact
    /// velocities; the solver returns tyre forces that are applied to the Rigidbody in FixedUpdate.
    /// A kinematic (or missing) Rigidbody switches to the headless PlanarChassis so EditMode tests can drive
    /// the car without PhysX. Wheel order: 0 FL, 1 FR, 2 RL, 3 RR.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class VehiclePhysicsAdapter : MonoBehaviour
    {
        static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

        [Header("Chassis Specifications (from vehicle.json)")]
        [SerializeField] float massKg = 1350f;
        [SerializeField] float wheelbaseM = 2.72f;
        [SerializeField] float trackM = 1.71f;
        [SerializeField] float wheelRadiusM = 0.327f;
        [SerializeField] Vector3 centreOfMass = new Vector3(0f, 0.51f, -0.1f);
        [SerializeField] float maxSteeringAngleDeg = 32f;
        public TextAsset vehicleJson;                       // optional overwrite of the spec
        public TransmissionType transmission = TransmissionType.Manual;
        public DriveLayout drive = DriveLayout.RearWheelDrive;

        [Header("Suspension (raycast)")]
        [Tooltip("Measure wheel positions and radius from Wheel_* children; otherwise use wheelbase/track above.")]
        public bool measureFromModel = true;
        public float travelUpM = 0.12f, travelDownM = 0.10f;
        [Range(0.1f, 1f)] public float dampingRatio = 0.35f;
        public float antiRollNpm = 9000f;
        public LayerMask groundMask = ~0;

        [Header("Surface")]
        public SurfaceType surface = SurfaceType.DryAsphalt; // set by weather

        public VehicleSolver Solver { get; private set; }
        public EngineModel Engine => Solver?.Engine;
        public int CurrentGear => Solver != null ? Solver.Gearbox.CurrentGear : 0;
        public VehicleState CurrentState { get; private set; }
        public DriverCommand LastCommand { get; private set; }
        public Rigidbody Body { get; private set; }
        public bool Headless => Body == null || Body.isKinematic;

        /// <summary>Wheel centre in car-local space (after suspension), for visuals.</summary>
        public readonly Vector3[] WheelCentreLocal = new Vector3[4];
        public readonly float[] Compression = new float[4];
        public readonly bool[] Grounded = new bool[4];
        public float WheelRadius => wheelRadiusM;
        public float WheelbaseM => wheelbaseM;
        public float TrackM => trackM;

        readonly Vector3[] restLocal = new Vector3[4];
        readonly WheelContact[] contacts = new WheelContact[4];
        readonly Vector3[] hitPoint = new Vector3[4], hitNormal = new Vector3[4];
        readonly float[] springRate = new float[4], damper = new float[4], lastCompression = new float[4];
        PlanarChassis planar;
        long tick;
        bool configured;

        void Awake()
        {
            InitializeEngine();
            SetupRigidbody();
        }

        /// <summary>Builds (or rebuilds) the solver from the serialized spec. Keeps the name for older callers.</summary>
        public void InitializeEngine()
        {
            if (Solver == null) Rebuild();
        }

        public void SetTransmission(TransmissionType type)
        {
            transmission = type; Rebuild();
        }

        public VehicleSpec BuildSpec()
        {
            var spec = new VehicleSpec();
            if (vehicleJson != null) JsonUtility.FromJsonOverwrite(vehicleJson.text, spec);
            spec.massKg = massKg; spec.wheelbaseM = wheelbaseM; spec.trackM = trackM; spec.wheelRadiusM = wheelRadiusM;
            spec.centreOfMassM = new[] { centreOfMass.x, centreOfMass.y, centreOfMass.z };
            spec.maxSteerDeg = maxSteeringAngleDeg; spec.transmission = transmission; spec.drive = drive;
            return spec;
        }

        void Rebuild()
        {
            MeasureWheels();
            Solver = new VehicleSolver(BuildSpec());
            planar = null;
        }

        public void SetupRigidbody()
        {
            Body = GetComponent<Rigidbody>();
            if (Body != null)
            {
                if (GetComponent<Collider>() == null)
                {
                    var box = gameObject.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.8f, 0f);
                    box.size = new Vector3(trackM + 0.1f, 1.0f, wheelbaseM + 1.6f);
                }
                Body.mass = massKg;
                Body.interpolation = RigidbodyInterpolation.Interpolate;
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                // Pitch/yaw ≈ 2000–2200 kg·m², roll ≈ 550 kg·m² for a 1350 kg sedan; PhysX derives it from the hull box otherwise.
                Body.inertiaTensor = new Vector3(massKg * 1.5f, massKg * 1.6f, massKg * 0.4f);
                Body.inertiaTensorRotation = Quaternion.identity;
                // Centre of mass goes last: while the inertia tensor is still automatic, Unity 6 recomputes the mass
                // properties and a freshly created body silently drops an earlier centerOfMass (reads back as zero).
                Body.centerOfMass = centreOfMass;
            }
            ConfigureSuspension();
        }

        void MeasureWheels()
        {
            bool found = false;
            if (measureFromModel)
            {
                var all = GetComponentsInChildren<Transform>(true);
                var pos = new Vector3[4]; int n = 0; float radius = 0f;
                for (int k = 0; k < 4; k++)
                    foreach (var t in all)
                        if (t.name == WheelNames[k])
                        {
                            var b = RendererBounds(t);
                            pos[k] = transform.InverseTransformPoint(b.size.sqrMagnitude > 0 ? b.center : t.position);
                            if (b.size.y > 0.2f) radius += b.size.y * 0.5f;
                            n++; break;
                        }
                if (n == 4)
                {
                    found = true;
                    for (int k = 0; k < 4; k++) restLocal[k] = pos[k];
                    wheelbaseM = Mathf.Abs(0.5f * (pos[0].z + pos[1].z) - 0.5f * (pos[2].z + pos[3].z));
                    trackM = 0.5f * (Mathf.Abs(pos[1].x - pos[0].x) + Mathf.Abs(pos[3].x - pos[2].x));
                    if (radius > 0.4f) wheelRadiusM = radius / 4f;
                    if (pos[0].z < pos[2].z || pos[0].x > pos[1].x)
                        Debug.LogError($"{name}: Wheel_FL is not front-left in car space (FL {pos[0]}, FR {pos[1]}, RL {pos[2]}). Check the model rotation under the car root.", this);
                }
            }
            if (!found)
            {
                float y = wheelRadiusM;
                restLocal[0] = new Vector3(-trackM / 2, y, wheelbaseM / 2); restLocal[1] = new Vector3(trackM / 2, y, wheelbaseM / 2);
                restLocal[2] = new Vector3(-trackM / 2, y, -wheelbaseM / 2); restLocal[3] = new Vector3(trackM / 2, y, -wheelbaseM / 2);
            }
            for (int k = 0; k < 4; k++) WheelCentreLocal[k] = restLocal[k];
            configured = false;
        }

        static Bounds RendererBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            // Calipers sit inside the wheel empty; the tyre is the largest renderer.
            Bounds best = rs[0].bounds;
            foreach (var r in rs) if (r.bounds.size.y > best.size.y) best = r.bounds;
            return best;
        }

        void ConfigureSuspension()
        {
            if (Solver == null) return;
            float g = 9.81f, zc = centreOfMass.z;
            float zf = 0.5f * (restLocal[0].z + restLocal[1].z), zr = 0.5f * (restLocal[2].z + restLocal[3].z);
            float frontShare = Mathf.Clamp01((zc - zr) / Mathf.Max(0.1f, zf - zr));
            for (int k = 0; k < 4; k++)
            {
                float load = massKg * g * 0.5f * (k < 2 ? frontShare : 1f - frontShare);
                // The car sits in the modelled pose when the spring is compressed by travelDown.
                springRate[k] = load / travelDownM;
                damper[k] = 2f * dampingRatio * Mathf.Sqrt(springRate[k] * load / g);
                lastCompression[k] = travelDownM;
            }
            configured = true;
        }

        void FixedUpdate()
        {
            // Driven externally (VehicleController); nothing here keeps a parked car quiet in PhysX.
        }

        /// <summary>One physics step: raycasts, solver, forces. Call from FixedUpdate (or tests).</summary>
        public VehicleState Step(DriverCommand cmd, float dtSeconds)
        {
            if (dtSeconds <= 0f || float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds))
                throw new ArgumentOutOfRangeException(nameof(dtSeconds), "dtSeconds must be positive and finite.");
            cmd.Validate();
            InitializeEngine();
            if (!configured) ConfigureSuspension();
            tick++;
            LastCommand = cmd;

            if (Headless) return CurrentState = StepHeadless(cmd, dtSeconds);

            float mu = SurfaceFrictionModel.GetFrictionCoefficient(surface);
            for (int k = 0; k < 4; k++)
            {
                Vector3 up = transform.up;
                Vector3 origin = transform.TransformPoint(restLocal[k] + Vector3.up * travelUpM);
                float maxLen = travelUpM + travelDownM + wheelRadiusM;
                if (GroundRay(origin, -up, maxLen, out var hit))
                {
                    float comp = maxLen - hit.distance;
                    float vComp = (comp - lastCompression[k]) / dtSeconds;
                    lastCompression[k] = comp;
                    float fz = Mathf.Max(0f, springRate[k] * comp + damper[k] * vComp);
                    Compression[k] = comp; Grounded[k] = true; hitPoint[k] = hit.point; hitNormal[k] = hit.normal;
                    WheelCentreLocal[k] = restLocal[k] + Vector3.up * (comp - travelDownM);

                    Vector3 n = hit.normal;
                    Vector3 f = Vector3.ProjectOnPlane(transform.forward, n).normalized;
                    Vector3 r = Vector3.Cross(n, f);
                    Vector3 v = Body.GetPointVelocity(hit.point);
                    float localMu = mu;
                    var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                    if (tag != null) localMu = (tag.followWeather && surface != SurfaceType.DryAsphalt ? Mathf.Min(mu, SurfaceFrictionModel.GetFrictionCoefficient(tag.surface)) : SurfaceFrictionModel.GetFrictionCoefficient(tag.surface)) * tag.frictionScale;
                    contacts[k] = new WheelContact { grounded = true, normalForceN = fz, velocityRightMps = Vector3.Dot(v, r), velocityForwardMps = Vector3.Dot(v, f), frictionCoefficient = localMu };
                }
                else
                {
                    Compression[k] = 0f; lastCompression[k] = 0f; Grounded[k] = false;
                    WheelCentreLocal[k] = restLocal[k] - Vector3.up * travelDownM;
                    contacts[k] = new WheelContact { grounded = false, velocityForwardMps = Vector3.Dot(Body.GetPointVelocity(transform.TransformPoint(restLocal[k])), transform.forward) };
                }
            }

            // Anti-roll bars move load from the extended to the compressed wheel of each axle.
            for (int axle = 0; axle < 2; axle++)
            {
                int l = axle * 2, rr = l + 1;
                if (!Grounded[l] || !Grounded[rr]) continue;
                float d = (Compression[l] - Compression[rr]) * antiRollNpm;
                contacts[l].normalForceN = Mathf.Max(0f, contacts[l].normalForceN + d);
                contacts[rr].normalForceN = Mathf.Max(0f, contacts[rr].normalForceN - d);
            }

            var state = Solver.Step(cmd, contacts, dtSeconds);

            for (int k = 0; k < 4; k++)
            {
                if (!Grounded[k]) continue;
                Vector3 n = hitNormal[k];
                Vector3 f = Vector3.ProjectOnPlane(transform.forward, n).normalized;
                Vector3 r = Vector3.Cross(n, f);
                var w = Solver.Wheels[k];
                Body.AddForceAtPosition(n * contacts[k].normalForceN, hitPoint[k]);
                // Tyre forces act at the contact patch, lifted slightly to tame the roll moment of a rigid body.
                Vector3 at = hitPoint[k] + transform.up * (0.25f * wheelRadiusM);
                Body.AddForceAtPosition(r * w.forceRightN + f * w.forceForwardN, at);
            }
            Body.AddForce(transform.forward * Solver.DragForceForwardN);
            return CurrentState = state;
        }

        readonly RaycastHit[] hits = new RaycastHit[8];

        bool GroundRay(Vector3 origin, Vector3 dir, float length, out RaycastHit best)
        {
            best = default; float bestD = float.MaxValue;
            int n = UnityEngine.Physics.RaycastNonAlloc(origin, dir, hits, length, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.attachedRigidbody == Body || hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < bestD) { bestD = hits[i].distance; best = hits[i]; }
            }
            return bestD < float.MaxValue;
        }

        VehicleState StepHeadless(DriverCommand cmd, float dt)
        {
            if (planar == null)
            {
                planar = new PlanarChassis { X = transform.position.x, Z = transform.position.z, Yaw = transform.eulerAngles.y * Mathf.Deg2Rad };
            }
            planar.Surface = surface;
            planar.Step(Solver, cmd, dt);
            for (int k = 0; k < 4; k++) { Grounded[k] = true; Compression[k] = travelDownM; WheelCentreLocal[k] = restLocal[k]; }
            var p = new Vector3((float)planar.X, transform.position.y, (float)planar.Z);
            var q = Quaternion.Euler(0f, planar.Yaw * Mathf.Rad2Deg, 0f);
            if (Body != null) { Body.position = p; Body.rotation = q; }
            transform.SetPositionAndRotation(p, q);
            return Solver.State;
        }

        /// <summary>Teleports the car and restarts the drivetrain in the parked, engine-off state.</summary>
        public void ResetAt(Vector3 position, Quaternion rotation)
        {
            Rebuild();
            ConfigureSuspension();
            transform.SetPositionAndRotation(position, rotation);
            if (Body != null)
            {
                Body.position = position; Body.rotation = rotation;
                if (!Body.isKinematic)
                {
#if UNITY_6000_0_OR_NEWER
                    Body.linearVelocity = Vector3.zero;
#else
                    Body.velocity = Vector3.zero;
#endif
                    Body.angularVelocity = Vector3.zero;
                }
            }
            UnityEngine.Physics.SyncTransforms();
        }

        /// <summary>
        /// Computes exact Ackermann steering geometry angles for front wheels.
        /// Inner wheel turns more sharply than outer wheel.
        /// </summary>
        public static (float leftSteerDeg, float rightSteerDeg) CalculateAckermann(
            float normalizedSteer, float wheelbase, float track, float maxSteerDeg = 32f)
            => SteeringGeometry.Ackermann(normalizedSteer, wheelbase, track, maxSteerDeg);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int k = 0; k < 4; k++)
            {
                var c = transform.TransformPoint(Application.isPlaying ? WheelCentreLocal[k] : restLocal[k]);
                Gizmos.DrawWireSphere(c, wheelRadiusM);
            }
        }
    }
}
