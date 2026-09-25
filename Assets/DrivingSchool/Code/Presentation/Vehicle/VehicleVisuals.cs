using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Animates the named parts of the car model from the physics adapter: wheel spin/steer/suspension,
    /// steering wheel (900° lock-to-lock), pedals, gauge needles, wipers, gear lever and handbrake.
    /// Rotation axes are derived in car space, so the FBX axis convention does not matter.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class VehicleVisuals : MonoBehaviour
    {
        public VehiclePhysicsAdapter adapter;
        public Transform model;                    // root of the imported car model (defaults to children of adapter)
        [Header("Gauges")]
        public float speedoMaxKph = 200f, tachoMaxRpm = 8000f, needleSweepDeg = 260f;
        [Tooltip("+1 = clockwise as seen by the driver")] public float needleDirection = 1f;
        [Header("Controls")]
        public float pedalTravelDeg = 18f, gearLeverTiltDeg = 11f, handbrakeLiftDeg = 22f;
        [Tooltip("Wiper sweep in degrees; the direction is chosen so the blade tip rises.")]
        public float wiperSweepDeg = 95f;

        readonly VehicleRigUtil.Pose[] wheels = new VehicleRigUtil.Pose[4];
        readonly Vector3[] wheelRestCentre = new Vector3[4];
        readonly System.Collections.Generic.List<VehicleRigUtil.Pose>[] calipers = new System.Collections.Generic.List<VehicleRigUtil.Pose>[4];
        VehicleRigUtil.Pose steeringWheel, needleRpm, needleSpeed, gearLever, handbrakeLever;
        VehicleRigUtil.Pose[] pedals = new VehicleRigUtil.Pose[3]; // clutch, brake, throttle
        VehicleRigUtil.Pose[] wipers = new VehicleRigUtil.Pose[0];
        float[] wiperSign = new float[0]; Vector3[] wiperAxis = new Vector3[0];
        Vector3 columnAxis, needleAxis;
        float smoothedRpm, smoothedSpeed, clutchV, brakeV, throttleV;
        Transform car;

        public int WiperCount => wipers.Length;

        void Start()
        {
            if (adapter == null) adapter = GetComponentInParent<VehiclePhysicsAdapter>();
            car = adapter != null ? adapter.transform : transform;
            if (model == null) model = car;
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int k = 0; k < 4; k++)
            {
                var t = VehicleRigUtil.Find(model, names[k]);
                if (t == null) continue;
                wheels[k] = new VehicleRigUtil.Pose(car, t);
                wheelRestCentre[k] = wheels[k].RestPositionInCar;
                // Calipers are modelled inside the wheel empty; they steer and bounce but must not spin.
                calipers[k] = new System.Collections.Generic.List<VehicleRigUtil.Pose>();
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    if (c != t && c.name.StartsWith("BrakeCaliper", System.StringComparison.Ordinal)) calipers[k].Add(new VehicleRigUtil.Pose(car, c));
            }
            var sw = VehicleRigUtil.Find(model, "SteeringWheel_Pivot");
            if (sw != null) { steeringWheel = new VehicleRigUtil.Pose(car, sw); columnAxis = VehicleRigUtil.AlignedAxisInCar(car, sw, Vector3.forward); }
            var nr = VehicleRigUtil.Find(model, "Needle_RPM"); var ns = VehicleRigUtil.Find(model, "Needle_Speed");
            if (nr != null) { needleRpm = new VehicleRigUtil.Pose(car, nr); needleAxis = VehicleRigUtil.AlignedAxisInCar(car, nr, Vector3.forward); }
            if (ns != null) { needleSpeed = new VehicleRigUtil.Pose(car, ns); if (nr == null) needleAxis = VehicleRigUtil.AlignedAxisInCar(car, ns, Vector3.forward); }
            string[] pedalNames = { "Pedal_Clutch", "Pedal_Brake", "Pedal_Throttle" };
            for (int i = 0; i < 3; i++) { var p = VehicleRigUtil.Find(model, pedalNames[i]); if (p != null) pedals[i] = new VehicleRigUtil.Pose(car, p); }
            var gl = VehicleRigUtil.Find(model, "GearLever_Pivot"); if (gl != null) gearLever = new VehicleRigUtil.Pose(car, gl);
            var hb = VehicleRigUtil.Find(model, "Handbrake_Pivot"); if (hb != null) handbrakeLever = new VehicleRigUtil.Pose(car, hb);
            SetupWipers();
        }

        void SetupWipers()
        {
            var list = VehicleRigUtil.FindPrefix(model, "Wiper_Pivot");
            wipers = new VehicleRigUtil.Pose[list.Count]; wiperSign = new float[list.Count]; wiperAxis = new Vector3[list.Count];
            Vector3 normal = WindshieldNormal();
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                wipers[i] = new VehicleRigUtil.Pose(car, t);
                // Blades sweep in the windshield plane, so they rotate about the glass normal. The pivot's own local
                // axes are aligned with the car (local Y = car up), which swung the blades flat over the bonnet.
                wiperAxis[i] = normal;
                // Pick the sweep direction in which the blade tip rises.
                Vector3 tip = FarthestPoint(t);
                Vector3 pivot = car.InverseTransformPoint(t.position);
                Vector3 arm = tip - pivot;
                float rise = (Quaternion.AngleAxis(20f, wiperAxis[i]) * arm).y - arm.y;
                wiperSign[i] = rise >= 0f ? 1f : -1f;
            }
        }

        /// <summary>Car-space outward normal of Glass_Windshield (up and forward for a raked screen).</summary>
        Vector3 WindshieldNormal()
        {
            var glass = VehicleRigUtil.Find(model, "Glass_Windshield");
            var r = glass != null ? glass.GetComponentInChildren<Renderer>(true) : null;
            if (r != null)
            {
                var b = VehicleRigUtil.CarSpaceBounds(car, r);
                // The raked screen runs from front-bottom (max z, min y) to rear-top (min z, max y).
                var up = new Vector3(0f, b.size.y, -b.size.z);
                if (up.sqrMagnitude > 1e-6f) return Vector3.Cross(Vector3.right, up.normalized).normalized;
            }
            return (Vector3.up + 0.6f * Vector3.forward).normalized;
        }

        Vector3 FarthestPoint(Transform pivot)
        {
            Vector3 p0 = car.InverseTransformPoint(pivot.position), best = p0; float bestD = -1f;
            foreach (var r in pivot.GetComponentsInChildren<Renderer>(true))
            {
                var b = VehicleRigUtil.CarSpaceBounds(car, r);
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float d = (c - p0).sqrMagnitude; if (d > bestD) { bestD = d; best = c; }
                }
            }
            return best;
        }

        /// <summary>Car-space pivot, arm tip and axis of wiper i for the windshield rain overlay.</summary>
        public bool WiperGeometry(int i, out Vector3 pivot, out Vector3 tip, out Vector3 axis, out float sweepSignedDeg)
        {
            pivot = tip = axis = Vector3.zero; sweepSignedDeg = 0;
            if (i < 0 || i >= wipers.Length) return false;
            pivot = wipers[i].RestPositionInCar;
            var saved = wipers[i].part.rotation; wipers[i].Apply(Quaternion.identity);
            tip = FarthestPoint(wipers[i].part); wipers[i].part.rotation = saved;
            axis = wiperAxis[i]; sweepSignedDeg = wiperSign[i] * wiperSweepDeg;
            return true;
        }

        void LateUpdate()
        {
            if (adapter == null || adapter.Solver == null) return;
            var s = adapter.Solver; var st = adapter.CurrentState; var cmd = adapter.LastCommand;

            for (int k = 0; k < 4; k++)
            {
                if (wheels[k] == null) continue;
                var w = s.Wheels[k];
                var rot = Quaternion.AngleAxis(w.steerAngleRad * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(w.spinAngleRad * Mathf.Rad2Deg, Vector3.right);
                float lift = adapter.Compression[k] - adapter.travelDownM; // + = pushed up into the arch
                wheels[k].Apply(rot, wheelRestCentre[k] + Vector3.up * lift);
                var steerRot = Quaternion.AngleAxis(w.steerAngleRad * Mathf.Rad2Deg, Vector3.up);
                if (calipers[k] != null)
                    foreach (var c in calipers[k])
                        c.Apply(steerRot, wheelRestCentre[k] + Vector3.up * lift + steerRot * (c.RestPositionInCar - wheelRestCentre[k]));
            }

            steeringWheel?.Apply(Quaternion.AngleAxis(s.SteeringWheelDeg, columnAxis));

            smoothedRpm = Mathf.Lerp(smoothedRpm, st.engineRpm, 1f - Mathf.Exp(-12f * Time.deltaTime));
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, Mathf.Abs(st.signedSpeedMps) * 3.6f, 1f - Mathf.Exp(-8f * Time.deltaTime));
            bool power = cmd.ignition;
            needleRpm?.Apply(Quaternion.AngleAxis(needleDirection * needleSweepDeg * Mathf.Clamp01(smoothedRpm / tachoMaxRpm), needleAxis));
            needleSpeed?.Apply(Quaternion.AngleAxis(needleDirection * needleSweepDeg * Mathf.Clamp01((power ? smoothedSpeed : 0f) / speedoMaxKph), needleAxis));

            float k2 = 1f - Mathf.Exp(-20f * Time.deltaTime);
            clutchV = Mathf.Lerp(clutchV, cmd.clutch, k2); brakeV = Mathf.Lerp(brakeV, cmd.brake, k2); throttleV = Mathf.Lerp(throttleV, cmd.throttle, k2);
            float[] pv = { clutchV, brakeV, throttleV };
            for (int i = 0; i < 3; i++) pedals[i]?.Apply(Quaternion.AngleAxis(pv[i] * pedalTravelDeg, Vector3.right));

            for (int i = 0; i < wipers.Length; i++)
                wipers[i].Apply(Quaternion.AngleAxis(wiperSign[i] * wiperSweepDeg * st.wiperAngle01, wiperAxis[i]));

            if (gearLever != null)
            {
                Vector2 g = GearPosition(st);
                gearLever.Apply(Quaternion.AngleAxis(g.x * gearLeverTiltDeg, Vector3.forward) * Quaternion.AngleAxis(-g.y * gearLeverTiltDeg, Vector3.right));
            }
            handbrakeLever?.Apply(Quaternion.AngleAxis(cmd.handbrake ? -handbrakeLiftDeg : 0f, Vector3.right));
        }

        /// <summary>H-pattern: x = gate (−1 left … +1 right), y = +1 forward / −1 back. АКПП: P/R/N/D along y.</summary>
        static Vector2 GearPosition(VehicleState st)
        {
            if (st.transmission == TransmissionType.Automatic)
            {
                switch (st.selector) { case AutomaticSelector.P: return new Vector2(0, 1f); case AutomaticSelector.R: return new Vector2(0, 0.35f); case AutomaticSelector.N: return new Vector2(0, -0.3f); default: return new Vector2(0, -1f); }
            }
            switch (st.gear)
            {
                case 1: return new Vector2(-1, 1); case 2: return new Vector2(-1, -1);
                case 3: return new Vector2(0, 1); case 4: return new Vector2(0, -1);
                case 5: return new Vector2(1, 1); case 6: return new Vector2(1, -1);
                case -1: return new Vector2(1.6f, -1);
                default: return Vector2.zero;
            }
        }
    }
}
