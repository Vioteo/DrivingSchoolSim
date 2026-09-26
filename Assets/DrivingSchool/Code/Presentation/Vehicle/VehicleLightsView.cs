using System.Collections.Generic;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Exterior lighting of the player car. Lamp lenses are found by material (Lamp_White/Red/Amber) or by
    /// name (Headlight, Taillight, TurnSignal, Indicator, LampUnit, ReverseLight) and classified by car-space
    /// position. Each lens material slot gets its own instance whose emission follows VehicleState; real
    /// Light sources are added for headlamps (low/high differ), tail/brake, reverse and indicators.
    /// A centre high-mounted brake light is added at the rear window.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class VehicleLightsView : MonoBehaviour
    {
        public enum LampRole { Headlamp, Parking, Tail, Reverse, IndicatorLeft, IndicatorRight }

        public VehiclePhysicsAdapter adapter;
        public Transform model;
        public bool createLightSources = true;
        [Header("Intensities (URP emission multipliers)")]
        public float headEmission = 6f, parkingEmission = 1.2f, tailEmission = 1.5f, brakeEmission = 6f, reverseEmission = 4f, indicatorEmission = 6f;
        [Header("Headlamp beams (URP light intensity, inverse-square falloff)")]
        [Tooltip("Peak of the low beam pattern; a lamp 0.65 m high gives ≈0.7 at 20 m and ≈0.15 at 40 m ahead.")]
        public float lowBeamIntensity = 320f;
        public float highBeamIntensity = 1100f;
        public float lowBeamRange = 90f, highBeamRange = 220f;

        sealed class Slot { public Renderer renderer; public int index; public Material material; public LampRole role; public Color baseColor; }
        readonly List<Slot> slots = new List<Slot>();
        Light lowL, lowR, highL, highR, tailL, tailR, reverse, indL, indR, indFL, indFR;
        Renderer centreBrake; Material centreBrakeMat;
        Transform car;

        public int LampSlotCount => slots.Count;
        public int CountRole(LampRole r) { int n = 0; foreach (var s in slots) if (s.role == r) n++; return n; }

        void Start()
        {
            if (adapter == null) adapter = GetComponentInParent<VehiclePhysicsAdapter>();
            car = adapter != null ? adapter.transform : transform;
            if (model == null) model = car;
            Classify();
            if (createLightSources) CreateLights();
        }

        void Classify()
        {
            var carBounds = new Bounds(); bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var b = VehicleRigUtil.CarSpaceBounds(car, r);
                if (first) { carBounds = b; first = false; } else carBounds.Encapsulate(b);
            }
            float frontZ = carBounds.center.z;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                string n = r.name;
                bool byName = Has(n, "Headlight") || Has(n, "Taillight") || Has(n, "TurnSignal") || Has(n, "Indicator") || Has(n, "LampUnit") || Has(n, "ReverseLight") || Has(n, "DRL") || Has(n, "TailLightBar");
                if (Has(n, "Housing") || Has(n, "Reflector") || Has(n, "Ceiling") || Has(n, "Interior")) continue;
                Material[] instanced = null;
                for (int i = 0; i < shared.Length; i++)
                {
                    var m = shared[i]; if (m == null) continue;
                    string mn = m.name;
                    bool white = Has(mn, "Lamp_White"), red = Has(mn, "Lamp_Red"), amber = Has(mn, "Lamp_Amber");
                    if (!white && !red && !amber && !byName) continue;
                    if (!white && !red && !amber) { white = Has(n, "Headlight") || Has(n, "Reverse") || Has(n, "DRL"); red = Has(n, "Tail"); amber = Has(n, "TurnSignal") || Has(n, "Indicator"); }
                    if (!white && !red && !amber) continue;
                    var b = VehicleRigUtil.CarSpaceBounds(car, r);
                    // Needle blades use Lamp_Red inside the cabin: accept only lenses on the outer shell.
                    bool outer = b.center.z > carBounds.max.z - 0.7f || b.center.z < carBounds.min.z + 0.7f || Has(n, "Mirror");
                    if (!outer) continue;
                    bool front = b.center.z > frontZ;
                    LampRole role;
                    if (amber) role = b.center.x < 0 ? LampRole.IndicatorLeft : LampRole.IndicatorRight;
                    else if (red) role = LampRole.Tail;
                    else role = front ? LampRole.Headlamp : LampRole.Reverse;
                    if (instanced == null) instanced = r.materials; // instances for this renderer only
                    var mat = instanced[i];
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    var baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                    slots.Add(new Slot { renderer = r, index = i, material = mat, role = role, baseColor = baseColor });
                }
            }
        }

        static bool Has(string s, string part) => s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        void CreateLights()
        {
            Vector3 hl = Centroid(LampRole.Headlamp, -1), hr = Centroid(LampRole.Headlamp, 1);
            Vector3 tl = Centroid(LampRole.Tail, -1), tr = Centroid(LampRole.Tail, 1);
            Vector3 rv = Centroid(LampRole.Reverse, 0);
            // The beam shape (cut-off line, hot spot, kick-up to the kerb side) comes from a procedural cookie; the sources sit
            // just in front of the lenses so the lamp housings do not shadow their own light.
            var lowCookie = HeadlampCookie.Low(); var highCookie = HeadlampCookie.High();
            Vector3 ahead = Vector3.forward * 0.25f;
            lowL = Spot("Headlamp_Low_L", hl + ahead, HeadlampCookie.LowSpotAngle, lowBeamRange, lowBeamIntensity, 0f, cookie: lowCookie);
            lowR = Spot("Headlamp_Low_R", hr + ahead, HeadlampCookie.LowSpotAngle, lowBeamRange, lowBeamIntensity, 0f, cookie: lowCookie);
            highL = Spot("Headlamp_High_L", hl + ahead, HeadlampCookie.HighSpotAngle, highBeamRange, highBeamIntensity, 0f, cookie: highCookie);
            highR = Spot("Headlamp_High_R", hr + ahead, HeadlampCookie.HighSpotAngle, highBeamRange, highBeamIntensity, 0f, cookie: highCookie);
            highL.shadows = highR.shadows = LightShadows.None; // the low beam already casts; two more shadow maps add little
            tailL = Point("Tail_L", tl + Vector3.back * 0.15f, new Color(1f, 0.05f, 0.02f), 4f);
            tailR = Point("Tail_R", tr + Vector3.back * 0.15f, new Color(1f, 0.05f, 0.02f), 4f);
            reverse = Spot("Reverse", rv, 110f, 12f, 1.2f, -25f, true);
            indL = Point("Indicator_RL", Centroid(LampRole.IndicatorLeft, 0, false) + Vector3.back * 0.1f, new Color(1f, 0.55f, 0.05f), 3f);
            indR = Point("Indicator_RR", Centroid(LampRole.IndicatorRight, 0, false) + Vector3.back * 0.1f, new Color(1f, 0.55f, 0.05f), 3f);
            indFL = Point("Indicator_FL", Centroid(LampRole.IndicatorLeft, 0, true) + Vector3.forward * 0.1f, new Color(1f, 0.55f, 0.05f), 3f);
            indFR = Point("Indicator_FR", Centroid(LampRole.IndicatorRight, 0, true) + Vector3.forward * 0.1f, new Color(1f, 0.55f, 0.05f), 3f);
            CreateCentreBrakeLight();
        }

        Vector3 Centroid(LampRole role, int side, bool? front = null)
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var s in slots)
            {
                if (s.role != role) continue;
                var c = VehicleRigUtil.CarSpaceBounds(car, s.renderer).center;
                if (side < 0 && c.x > 0 || side > 0 && c.x < 0) continue;
                if (front.HasValue && (c.z > 0) != front.Value) continue;
                sum += c; n++;
            }
            if (n > 0) return sum / n;
            // Fallback when the model has no such lens: guess from body size.
            float z = role == LampRole.Headlamp || front == true ? 2.1f : -2.15f;
            return new Vector3(side * 0.6f, 0.7f, z);
        }

        Light Spot(string name, Vector3 carPos, float angle, float range, float intensity, float pitchDeg, bool backwards = false, Texture2D cookie = null)
        {
            var go = new GameObject(name); go.transform.SetParent(car, false);
            go.transform.localPosition = carPos;
            go.transform.localRotation = Quaternion.Euler(-pitchDeg, backwards ? 180f : 0f, 0f);
            var l = go.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f;
            l.range = range; l.intensity = intensity; l.color = backwards ? Color.white : new Color(1f, 0.96f, 0.88f);
            l.shadows = backwards ? LightShadows.None : LightShadows.Soft; l.shadowNearPlane = 0.1f; l.enabled = false;
            if (cookie != null) { l.cookie = cookie; l.innerSpotAngle = angle * 0.95f; }
            return l;
        }

        Light Point(string name, Vector3 carPos, Color c, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(car, false); go.transform.localPosition = carPos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = range; l.color = c; l.intensity = 0f; l.shadows = LightShadows.None;
            return l;
        }

        void CreateCentreBrakeLight()
        {
            var glass = VehicleRigUtil.Find(model, "Glass_Rear");
            Vector3 pos; float width = 0.5f;
            if (glass != null && glass.GetComponentInChildren<Renderer>() != null)
            {
                var b = VehicleRigUtil.CarSpaceBounds(car, glass.GetComponentInChildren<Renderer>());
                pos = new Vector3(0f, b.max.y - 0.04f, b.max.z - 0.03f); // top of the rear window, inside the glass
                width = Mathf.Min(0.6f, b.size.x * 0.4f);
            }
            else pos = new Vector3(0f, 1.3f, -1.5f);
            var q = GameObject.CreatePrimitive(PrimitiveType.Cube); q.name = "BrakeLight_Centre";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(car, false); q.transform.localPosition = pos; q.transform.localScale = new Vector3(width, 0.03f, 0.02f);
            centreBrake = q.GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            centreBrakeMat = shader != null ? new Material(shader) : new Material(centreBrake.sharedMaterial);
            centreBrakeMat.SetColor("_BaseColor", new Color(0.25f, 0.02f, 0.02f));
            centreBrakeMat.EnableKeyword("_EMISSION");
            centreBrake.sharedMaterial = centreBrakeMat;
        }

        void LateUpdate()
        {
            if (adapter == null || adapter.Solver == null) return;
            var st = adapter.CurrentState;
            bool indOnL = st.indicatorLampOn && st.leftIndicator, indOnR = st.indicatorLampOn && st.rightIndicator;
            foreach (var s in slots)
            {
                float e = 0f; Color c = s.baseColor;
                switch (s.role)
                {
                    case LampRole.Headlamp: e = st.lowBeam || st.highBeam ? headEmission * (st.highBeam ? 1.4f : 1f) : st.parkingLights ? parkingEmission : 0f; c = new Color(1f, 0.97f, 0.9f); break;
                    case LampRole.Tail: e = st.brakeLight ? brakeEmission : st.parkingLights ? tailEmission : 0f; c = new Color(1f, 0.04f, 0.02f); break;
                    case LampRole.Reverse: e = st.reverseLight ? reverseEmission : 0f; c = Color.white; break;
                    case LampRole.IndicatorLeft: e = indOnL ? indicatorEmission : 0f; c = new Color(1f, 0.5f, 0.02f); break;
                    case LampRole.IndicatorRight: e = indOnR ? indicatorEmission : 0f; c = new Color(1f, 0.5f, 0.02f); break;
                }
                s.material.SetColor("_EmissionColor", c * e);
            }
            if (centreBrakeMat != null) centreBrakeMat.SetColor("_EmissionColor", st.brakeLight ? new Color(1f, 0.04f, 0.02f) * brakeEmission : Color.black);
            if (lowL == null) return;
            bool low = st.lowBeam, high = st.highBeam;
            lowL.enabled = lowR.enabled = low || high;
            highL.enabled = highR.enabled = high;
            // No eye adaptation (auto exposure) in the pipeline: by day the beams would out-shine the sun on the verge.
            float adapt = Mathf.Lerp(1f, 0.12f, WeatherController.Daylight01);
            lowL.intensity = lowR.intensity = lowBeamIntensity * adapt;
            highL.intensity = highR.intensity = highBeamIntensity * adapt;
            float tail = st.brakeLight ? 2.2f : st.parkingLights ? 0.5f : 0f;
            tailL.intensity = tailR.intensity = tail;
            reverse.enabled = st.reverseLight;
            indL.intensity = indFL.intensity = indOnL ? 2f : 0f;
            indR.intensity = indFR.intensity = indOnR ? 2f : 0f;
        }
    }

    /// <summary>
    /// Procedural light cookies for the headlamps (right-hand traffic). The low beam has the ECE-style asymmetric
    /// cut-off: flat 0.6° below the horizon on the oncoming side, rising at 15° on the kerb side, a hot spot just
    /// under the cut-off and a weak wide foreground fill. The high beam is a symmetric long-range hot spot.
    /// Shapes are visual calibration, not photometric data.
    /// </summary>
    public static class HeadlampCookie
    {
        public const float LowSpotAngle = 110f, HighSpotAngle = 70f;
        const int N = 256;
        static Texture2D low, high;

        public static Texture2D Low() => low != null ? low : low = Draw("HeadlampCookie_Low", LowSpotAngle, LowPattern);
        public static Texture2D High() => high != null ? high : high = Draw("HeadlampCookie_High", HighSpotAngle, HighPattern);

        /// <summary>Relative intensity for a direction given as yaw (+ right) and pitch (+ up), degrees.</summary>
        public static float LowPattern(float yaw, float pitch)
        {
            float cut = yaw < 0f ? -0.6f : Mathf.Min(-0.6f + yaw * 0.268f, 1.2f); // tan 15° kick-up, capped
            float edge = Mathf.Clamp01((cut - pitch) / 0.5f + 0.5f);               // soft 0.5° edge
            float hot = Mathf.Exp(-Sq((yaw - 2f) / 10f) - Sq((pitch + 1.6f) / 2.2f));
            float wide = 0.22f * Mathf.Exp(-Sq(yaw / 32f) - Sq((pitch + 4f) / 5f));
            float fore = 0.07f * Mathf.Exp(-Sq(yaw / 50f)) * Mathf.Clamp01(-pitch / 6f);
            return Mathf.Clamp01((hot + wide + fore) * edge + 0.012f * (1f - edge) * Mathf.Exp(-Sq(yaw / 30f)));
        }

        public static float HighPattern(float yaw, float pitch)
        {
            float hot = Mathf.Exp(-Sq(yaw / 6f) - Sq((pitch + 0.3f) / 2.5f));
            float wide = 0.3f * Mathf.Exp(-Sq(yaw / 18f) - Sq((pitch + 1f) / 5f));
            return Mathf.Clamp01(hot + wide);
        }

        static float Sq(float x) => x * x;

        static Texture2D Draw(string name, float spotAngle, System.Func<float, float, float> pattern)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[N * N];
            float t = Mathf.Tan(0.5f * spotAngle * Mathf.Deg2Rad);
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    // Spot cookies map onto the plane at unit distance: u ↔ tan(yaw), v ↔ tan(pitch).
                    float x = ((i + 0.5f) / N * 2f - 1f) * t, y = ((j + 0.5f) / N * 2f - 1f) * t;
                    float yaw = Mathf.Atan(x) * Mathf.Rad2Deg, pitch = Mathf.Atan2(y, Mathf.Sqrt(1f + x * x)) * Mathf.Rad2Deg;
                    // Fade to black at the cone rim so the round spot edge never shows.
                    float r = Mathf.Sqrt(x * x + y * y) / t, rim = Mathf.Clamp01((1f - r) / 0.12f);
                    byte v = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(pattern(yaw, pitch) * rim));
                    px[j * N + i] = new Color32(v, v, v, v);
                }
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }
    }
}
