using DrivingSchool.Simulation.Traffic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Kinematic body of an AI car: placed from the snapshot, reports contacts. No decisions here.
    /// Wheels spin by the distance travelled and the front ones steer; brake and turn lamps are emissive overlays
    /// switched on and off (made by VehicleAssembler.BuildTrafficPrefab).
    /// Lights (T64): the headlamp and tail lenses glow whenever the car moves (low beam or DRL is required by day,
    /// ПДД РФ 19.5 — редакцию сверить); in the dark a real headlamp beam is switched on for cars near the camera.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TrafficVehicleView : MonoBehaviour
    {
        static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

        [SerializeField] Transform model;
        [SerializeField] Renderer[] brakeLamps = new Renderer[0], leftLamps = new Renderer[0], rightLamps = new Renderer[0];
        [SerializeField] float lengthM = 4.4f, widthM = 1.8f, wheelbaseM = 2.6f;
        [SerializeField] Renderer[] headLamps = new Renderer[0], tailLamps = new Renderer[0];
        [SerializeField] Light beam;
        [Tooltip("Daylight level (WeatherController.Daylight01) below which the headlamp beam is on.")]
        [SerializeField] float beamBelowDaylight = 0.45f;
        [Tooltip("Only cars this close to the camera light the road: the rest show glowing lenses.")]
        [SerializeField] float beamDistanceM = 90f;
        TrafficDirectorHost host;
        Rigidbody body;
        string id;
        VehicleRigUtil.Pose[] wheels;
        float wheelRadius = 0.32f, spinDeg;
        Vector3 lastPosition; bool placed;
        // Body motion on the springs (T52): pitch under braking/acceleration, roll in turns; visual only, on the model.
        Quaternion modelRest; float pitch, roll, pitchVel, rollVel, lastYaw, lastContactReport = -10f;
        const float PitchPerMps2 = 0.55f, RollPerMps2 = 0.9f, BodyFrequencyHz = 1.6f, BodyDamping = 0.6f;

        public string AgentId => id;
        public float LengthM => lengthM;
        public float WidthM => widthM;
        public float WheelbaseM => wheelbaseM;

        /// <summary>Called by the prefab builder (T64): headlamp and tail lenses, the headlamp beam.</summary>
        public void ConfigureLights(Renderer[] head, Renderer[] tail, Light headBeam)
        {
            headLamps = head; tailLamps = tail; beam = headBeam;
        }

        public bool BeamOn => beam != null && beam.enabled;
        public int HeadLampCount => headLamps.Length;
        public int TailLampCount => tailLamps.Length;

        /// <summary>Called by the prefab builder.</summary>
        public void Configure(Transform modelRoot, Renderer[] brake, Renderer[] left, Renderer[] right, float length, float width, float wheelbase)
        {
            model = modelRoot; brakeLamps = brake; leftLamps = left; rightLamps = right; lengthM = length; widthM = width; wheelbaseM = wheelbase;
        }

        internal void Bind(TrafficDirectorHost owner, string agentId)
        {
            host = owner; id = agentId; placed = false;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (wheels == null) { FindWheels(); if (model != null) modelRest = model.localRotation; }
            pitch = roll = pitchVel = rollVel = 0f;
        }

        void FindWheels()
        {
            wheels = new VehicleRigUtil.Pose[4];
            var root = model != null ? model : transform;
            float r = 0f; int n = 0;
            for (int k = 0; k < 4; k++)
            {
                var t = VehicleRigUtil.Find(root, WheelNames[k]);
                if (t == null) continue;
                wheels[k] = new VehicleRigUtil.Pose(transform, t);
                var rs = t.GetComponentsInChildren<Renderer>(true);
                foreach (var x in rs) { r += x.bounds.size.y / 2f; n++; break; }
            }
            if (n > 0) wheelRadius = Mathf.Max(0.2f, r / n);
        }

        internal void Apply(ParticipantState p)
        {
            var position = new Vector3((float)p.Position.x, (float)p.Position.y, (float)p.Position.z);
            var rotation = Quaternion.Euler(0, (float)(p.HeadingRad * Mathf.Rad2Deg), 0);
            float yaw = (float)(p.HeadingRad * Mathf.Rad2Deg);
            if (!placed) { transform.SetPositionAndRotation(position, rotation); body.position = position; body.rotation = rotation; lastPosition = position; lastYaw = yaw; placed = true; }
            else { body.MovePosition(position); body.MoveRotation(rotation); }
            float travelled = Vector3.Dot(position - lastPosition, rotation * Vector3.forward);
            lastPosition = position;
            float dt = Mathf.Max(Time.fixedDeltaTime, 1e-4f);
            float yawRate = Mathf.DeltaAngle(lastYaw, yaw) * Mathf.Deg2Rad / dt; lastYaw = yaw;
            float lateral = (float)p.SpeedMps * yawRate;             // centripetal acceleration, + = turning right
            Spring(ref pitch, ref pitchVel, -(float)p.AccelerationMps2 * PitchPerMps2, dt);   // nose dips under braking
            Spring(ref roll, ref rollVel, lateral * RollPerMps2, dt);                          // leans out of the turn
            if (model != null) model.localRotation = Quaternion.Euler(Mathf.Clamp(pitch, -4f, 4f), 0f, Mathf.Clamp(roll, -5f, 5f)) * modelRest;
            spinDeg = Mathf.Repeat(spinDeg + travelled / wheelRadius * Mathf.Rad2Deg, 360f);
            float steerDeg = (float)p.SteeringRad * Mathf.Rad2Deg;
            if (wheels != null)
                for (int k = 0; k < 4; k++)
                    if (wheels[k] != null)
                        wheels[k].Apply(Quaternion.AngleAxis(k < 2 ? steerDeg : 0f, Vector3.up) * Quaternion.AngleAxis(spinDeg, Vector3.right));
            bool blink = Mathf.Repeat(Time.time, 0.8f) < 0.4f;
            Set(brakeLamps, p.BrakeLight);
            Set(leftLamps, (p.LeftIndicator || p.Hazard) && blink);
            Set(rightLamps, (p.RightIndicator || p.Hazard) && blink);
            Set(headLamps, true);
            Set(tailLamps, true);
            if (beam != null)
            {
                var cam = Camera.main;
                bool near = cam == null || (cam.transform.position - transform.position).sqrMagnitude < beamDistanceM * beamDistanceM;
                bool on = WeatherController.Daylight01 < beamBelowDaylight && near;
                if (beam.enabled != on) beam.enabled = on;
            }
        }

        static void Spring(ref float x, ref float vel, float target, float dt)
        {
            float w = 2f * Mathf.PI * BodyFrequencyHz;
            vel += (w * w * (target - x) - 2f * BodyDamping * w * vel) * dt;
            x += vel * dt;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (host != null) { host.ReportContact(id, host.IsPlayer(collision.transform)); lastContactReport = Time.time; }
        }

        // While something keeps pressing against the car it stays put (the hold time restarts, T52).
        void OnCollisionStay(Collision collision)
        {
            if (host == null || Time.time - lastContactReport < 0.5f) return;
            if (collision.rigidbody == null || collision.rigidbody.isKinematic) return;   // resting against static things is not a crash
            host.ReportContact(id, host.IsPlayer(collision.transform)); lastContactReport = Time.time;
        }

        static void Set(Renderer[] lamps, bool on)
        {
            foreach (var r in lamps) if (r != null) r.enabled = on;
        }
    }
}
