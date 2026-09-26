using System;
using System.Collections.Generic;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Instrument-cluster telltales on the car model: turn arrows, low/high beam, parking lights, handbrake,
    /// battery, oil pressure, check engine, seat belt, low fuel, the gear/selector display and the fuel level bar.
    /// Icons are drawn procedurally (no texture assets). With the side lights on the dial scales and displays switch
    /// to the amber night illumination. Also plays the indicator relay click and the horn.
    /// Needles are animated by VehicleVisuals.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class DashboardView : MonoBehaviour
    {
        public enum Telltale { TurnLeft, TurnRight, LowBeam, HighBeam, Parking, Handbrake, Battery, Oil, CheckEngine, Seatbelt, LowFuel }

        public VehiclePhysicsAdapter adapter;
        public Transform model;
        public Material templateMaterial;         // URP Unlit (assigned by the scene builder so the shader ships in builds)
        [Tooltip("Transparent URP Unlit for the printed dial scales; created at runtime when empty.")]
        public Material dialMaterial;
        [Tooltip("Car-space offset added to the automatically found telltale row position.")]
        public Vector3 rowOffset = Vector3.zero;
        public float iconSizeM = 0.016f, iconGapM = 0.004f;
        public bool sounds = true;
        [Header("Illumination and fuel")]
        [Tooltip("Colour of the dial scales and displays with the side lights on (night illumination).")]
        public Color nightIllumination = new Color(1f, 0.52f, 0.14f);
        public Color dayPrint = new Color(0.86f, 0.93f, 0.95f);
        [Tooltip("Low-fuel lamp comes on below this many litres.")]
        public float reserveLitres = 7f;
        public int fuelSegments = 8;

        static readonly Color Green = new Color(0.15f, 1f, 0.3f), Blue = new Color(0.2f, 0.45f, 1f), Red = new Color(1f, 0.12f, 0.08f), Amber = new Color(1f, 0.62f, 0.05f);

        readonly Dictionary<Telltale, Renderer> icons = new Dictionary<Telltale, Renderer>();
        Renderer gearGlyph; Material gearMat; readonly Dictionary<char, Texture2D> glyphs = new Dictionary<char, Texture2D>();
        AudioSource relay, horn; AudioClip tick, tock;
        bool lastLamp; float bulbCheck;
        Transform car, eye;
        readonly List<Material> dialMats = new List<Material>();
        Renderer fuelBar; Material fuelMat; int fuelShown = -1; bool fuelBlink;

        public bool IsLit(Telltale t) => icons.TryGetValue(t, out var r) && r != null && r.enabled;
        public string GearText { get; private set; } = "";
        /// <summary>Lit segments of the fuel bar (0…fuelSegments), −1 when the bar is off.</summary>
        public int FuelSegmentsLit => fuelBar != null && fuelBar.enabled ? fuelShown : -1;
        public bool NightIllumination { get; private set; }
        public int DialCount { get; private set; }
        /// <summary>Car-space position of a telltale icon (for tests and the self-check).</summary>
        public bool TryGetIconPosition(Telltale t, out Vector3 carPos) { carPos = default; if (!icons.TryGetValue(t, out var r) || r == null) return false; carPos = car.InverseTransformPoint(r.transform.position); return true; }

        void Start()
        {
            if (adapter == null) adapter = GetComponentInParent<VehiclePhysicsAdapter>();
            car = adapter != null ? adapter.transform : transform;
            if (model == null) model = car;
            eye = VehicleRigUtil.Find(model, "Socket_DriverEye");
            BuildRow();
            if (sounds) BuildAudio();
        }

        Vector3 EyeInCar() => eye != null ? car.InverseTransformPoint(eye.position) : new Vector3(-0.37f, 1.15f, -0.3f);

        void BuildRow()
        {
            var root = new GameObject("Dashboard_Telltales").transform; root.SetParent(car, false);
            var cluster = VehicleRigUtil.Find(model, "Cluster_Display");
            if (cluster != null && cluster.GetComponentInChildren<Renderer>(true) != null) BuildClusterLayout(root, CarBounds(cluster));
            else BuildFallbackRow(root);
            gearMat = gearGlyph.sharedMaterial;
            BuildDials(root);
        }

        /// <summary>
        /// Telltales on the display between the two dials, where the driver sees them over the steering-wheel rim:
        /// turn arrows (large) and high beam on top, the gear in the middle, the other lamps in two rows below.
        /// </summary>
        void BuildClusterLayout(Transform root, Bounds b)
        {
            Vector3 eyeP = EyeInCar();
            Vector3 anchor = new Vector3(b.center.x, b.max.y, b.min.z) + rowOffset;
            Vector3 toEye = (eyeP - anchor).normalized;
            Quaternion face = Quaternion.LookRotation(-toEye, Vector3.up); // quad normal (−Z) points at the eye
            Vector3 right = face * Vector3.right, up = face * Vector3.up;
            Vector3 P(float dx, float dy) => anchor + right * dx + up * dy + toEye * 0.003f;
            float big = iconSizeM * 1.4f, small = iconSizeM * 1.0f, pitch = small + iconGapM * 0.5f;
            float w = Mathf.Max(0.05f, b.size.x);
            Add(root, Telltale.TurnLeft, P(-0.315f * w, -0.013f), face, big);
            Add(root, Telltale.TurnRight, P(0.315f * w, -0.013f), face, big);
            Add(root, Telltale.HighBeam, P(0f, -0.013f), face, small);
            gearGlyph = Quad("Telltale_Gear", root, P(0f, -0.034f), face, iconSizeM * 1.4f, TelltaleIcons.Glyph('N'), Color.white);
            // Fuel level bar under the gear, above the steering-wheel rim: pump icon + segments, E on the left.
            fuelBar = Quad("Fuel_Bar", root, P(0f, -0.0545f), face, 1f, FuelBarTexture.Draw(fuelSegments, fuelSegments, false), Color.white);
            fuelBar.transform.localScale = new Vector3(Mathf.Min(0.058f, w * 0.9f), Mathf.Min(0.058f, w * 0.9f) / FuelBarTexture.Aspect, 1f);
            fuelMat = fuelBar.sharedMaterial;
            // The lower half of the cluster is behind the steering-wheel rim, so the other lamps go into the upper
            // inner part of the dials (as on most real clusters): engine lamps in the tachometer, lights and
            // handbrake/seat belt in the speedometer.
            PlaceInDial(root, "GaugeFace_RPM", new[] { Telltale.Battery, Telltale.Oil, Telltale.CheckEngine, Telltale.LowFuel }, small, pitch);
            PlaceInDial(root, "GaugeFace_km", new[] { Telltale.LowBeam, Telltale.Parking, Telltale.Handbrake, Telltale.Seatbelt }, small, pitch);
            // The model's static "N" and odometer sit where the live gear is drawn now.
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Gear_Display", StringComparison.Ordinal) || t.name.StartsWith("Odometer", StringComparison.Ordinal))
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        void PlaceInDial(Transform root, string facePrefix, Telltale[] lamps, float size, float pitch)
        {
            Transform faceT = null;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith(facePrefix, StringComparison.Ordinal)) { faceT = t; break; }
            Vector3 centre;
            if (faceT != null)
            {
                var b = CarBounds(faceT);
                float radius = 0.5f * Mathf.Min(b.size.x, b.size.y);
                centre = new Vector3(b.center.x, b.center.y + 0.31f * radius, b.min.z - 0.006f); // between the hub and the numbers
            }
            else centre = EyeInCar() + new Vector3(0f, -0.2f, 0.65f);
            centre += rowOffset;
            Quaternion face = Quaternion.LookRotation(-(EyeInCar() - centre).normalized, Vector3.up);
            for (int i = 0; i < lamps.Length; i++) Add(root, lamps[i], centre + face * Vector3.right * ((i - 0.5f * (lamps.Length - 1)) * pitch), face, size);
        }

        void BuildFallbackRow(Transform root)
        {
            Vector3 centre;
            var hood = VehicleRigUtil.Find(model, "Instrument_Hood");
            if (hood != null) { var h = CarBounds(hood); centre = new Vector3(h.center.x, h.center.y - 0.02f, h.min.z - 0.01f); }
            else centre = EyeInCar() + new Vector3(0f, -0.28f, 0.62f);
            centre += rowOffset;
            Vector3 toEye = (EyeInCar() - centre).normalized;
            centre += toEye * 0.004f;
            Quaternion face = Quaternion.LookRotation(-toEye, Vector3.up);
            var list = (Telltale[])Enum.GetValues(typeof(Telltale));
            float step = iconSizeM + iconGapM, x0 = -0.5f * step * (list.Length - 1);
            for (int i = 0; i < list.Length; i++) Add(root, list[i], centre + face * Vector3.right * (x0 + i * step), face, iconSizeM);
            gearGlyph = Quad("Telltale_Gear", root, centre + face * Vector3.up * (iconSizeM * 1.6f), face, iconSizeM * 1.3f, TelltaleIcons.Glyph('N'), Color.white);
        }

        void Add(Transform root, Telltale t, Vector3 pos, Quaternion rot, float size) => icons[t] = Quad("Telltale_" + t, root, pos, rot, size, TelltaleIcons.Draw(t), ColorOf(t));

        // ------------------------------------------------------------------ dials

        /// <summary>
        /// Printed scales of the tachometer (0–8 ×1000 rpm, red from redline) and speedometer (0–200 km/h). They are drawn
        /// here because the imported model has labels at every 1600 rpm; the zero is at −130° and the scale sweeps
        /// 260° clockwise, matching VehicleVisuals.
        /// </summary>
        void BuildDials(Transform root)
        {
            var visuals = GetComponent<VehicleVisuals>();
            float sweep = visuals != null ? visuals.needleSweepDeg : 260f;
            float tachoMax = visuals != null ? visuals.tachoMaxRpm : 8000f, speedoMax = visuals != null ? visuals.speedoMaxKph : 200f;
            float redline = adapter != null && adapter.Solver != null ? adapter.Solver.Spec.redlineRpm : 6500f;
            int dials = 0;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("GaugeFace_", StringComparison.Ordinal)) continue;
                bool tacho = t.name.IndexOf("RPM", StringComparison.OrdinalIgnoreCase) >= 0;
                var b = CarBounds(t);
                float radius = 0.5f * Mathf.Min(b.size.x, b.size.y);
                if (radius < 0.02f) continue;
                Vector2 c = new Vector2(b.center.x, b.center.y);
                // Hide the model's own ticks and numbers inside this face.
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("GaugeNumber", StringComparison.Ordinal) || r.name.StartsWith("GaugeTick", StringComparison.Ordinal))
                    {
                        var rb = VehicleRigUtil.CarSpaceBounds(car, r);
                        if ((new Vector2(rb.center.x, rb.center.y) - c).magnitude < radius * 1.05f) r.enabled = false;
                    }
                Texture2D tex = tacho
                    ? GaugeDial.Draw(radius, sweep, tachoMax / 1000f, 1f, 0.5f, 1f, redline / 1000f)
                    : GaugeDial.Draw(radius, sweep, speedoMax, 20f, 10f, 20f, float.MaxValue);
                var pos = new Vector3(b.center.x, b.center.y, b.min.z - 0.004f); // over the face, under the needle
                var q = Quad("Dial_" + (tacho ? "RPM" : "Speed"), root, pos, Quaternion.identity, radius * 2f, tex, Color.white, dialMaterial != null ? dialMaterial : TransparentUnlit());
                q.enabled = true; dials++; dialMats.Add(q.sharedMaterial);
            }
            DialCount = dials;
        }

        static Material transparentUnlit;
        static Material TransparentUnlit()
        {
            if (transparentUnlit != null) return transparentUnlit;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = 3000;
            return transparentUnlit = m;
        }

        Bounds CarBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(car.InverseTransformPoint(t.position), Vector3.zero);
            var b = VehicleRigUtil.CarSpaceBounds(car, rs[0]); foreach (var r in rs) b.Encapsulate(VehicleRigUtil.CarSpaceBounds(car, r)); return b;
        }

        static Color ColorOf(Telltale t)
        {
            switch (t)
            {
                case Telltale.TurnLeft: case Telltale.TurnRight: case Telltale.LowBeam: case Telltale.Parking: return Green;
                case Telltale.HighBeam: return Blue;
                case Telltale.CheckEngine: case Telltale.LowFuel: return Amber;
                default: return Red;
            }
        }

        static Mesh quadMesh;
        static Mesh QuadMesh()
        {
            if (quadMesh != null) return quadMesh;
            // Faces −Z like the built-in Quad, but without a collider (a MeshCollider under the dynamic car body is an error).
            quadMesh = new Mesh { name = "TelltaleQuad" };
            quadMesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quadMesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quadMesh.RecalculateBounds();
            return quadMesh;
        }

        Renderer Quad(string name, Transform parent, Vector3 carPos, Quaternion carRot, float size, Texture2D tex, Color color, Material baseMaterial = null)
        {
            var q = new GameObject(name);
            q.transform.SetParent(parent, false); q.transform.localPosition = carPos; q.transform.localRotation = carRot; q.transform.localScale = Vector3.one * size;
            q.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var r = q.AddComponent<MeshRenderer>();
            Material m;
            if (baseMaterial != null) m = new Material(baseMaterial);
            else if (templateMaterial != null) m = new Material(templateMaterial);
            else { var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent Cutout"); m = new Material(sh); }
            m.mainTexture = tex; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color); else m.color = color;
            if (baseMaterial == null)
            {
                if (m.HasProperty("_AlphaClip")) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); }
                m.renderQueue = 2450;
            }
            r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.enabled = false;
            return r;
        }

        void BuildAudio()
        {
            tick = Click("relay_tick", 2400f); tock = Click("relay_tock", 1700f);
            relay = gameObject.AddComponent<AudioSource>(); relay.spatialBlend = 0.6f; relay.volume = 0.35f; relay.playOnAwake = false;
            horn = gameObject.AddComponent<AudioSource>(); horn.clip = HornClip(); horn.loop = true; horn.volume = 0.6f; horn.spatialBlend = 0.3f; horn.playOnAwake = false;
        }

        static AudioClip Click(string name, float hz)
        {
            int rate = 44100, n = rate / 40; var d = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)rate; d[i] = Mathf.Sin(2 * Mathf.PI * hz * t) * Mathf.Exp(-t * 350f) * 0.8f; }
            var c = AudioClip.Create(name, n, 1, rate, false); c.SetData(d, 0); return c;
        }

        static AudioClip HornClip()
        {
            int rate = 44100, n = rate / 2; var d = new float[n]; // two-tone horn, 420/500 Hz, loops seamlessly (integer cycles)
            for (int i = 0; i < n; i++) { float t = i / (float)rate; d[i] = 0.3f * Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 420f * t)) + 0.3f * Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 500f * t)); }
            var c = AudioClip.Create("horn", n, 1, rate, false); c.SetData(d, 0); return c;
        }

        void LateUpdate()
        {
            if (adapter == null || adapter.Solver == null) return;
            var st = adapter.CurrentState; var cmd = adapter.LastCommand;
            bool power = cmd.ignition;
            bool running = st.engine == EnginePhase.Running;
            // Bulb check: all warning lamps for 2 s after ignition on.
            bulbCheck = power ? bulbCheck + Time.deltaTime : 0f;
            bool check = power && bulbCheck < 2f;

            Set(Telltale.TurnLeft, st.leftIndicator && st.indicatorLampOn);
            Set(Telltale.TurnRight, st.rightIndicator && st.indicatorLampOn);
            Set(Telltale.LowBeam, st.lowBeam && !st.highBeam || check);
            Set(Telltale.HighBeam, st.highBeam || check);
            Set(Telltale.Parking, st.parkingLights && power || check);
            Set(Telltale.Handbrake, power && (cmd.handbrake || check));
            Set(Telltale.Battery, power && (!running || check));
            Set(Telltale.Oil, power && (!running || check));
            Set(Telltale.CheckEngine, power && (st.engine == EnginePhase.Stalled || check));
            Set(Telltale.Seatbelt, power && !cmd.seatbelt);
            Set(Telltale.LowFuel, power && (st.fuelLitres < reserveLitres || check));

            // Night illumination: printed scales go amber with the side lights; with the ignition off they are just
            // print, lit by whatever light reaches the cabin.
            NightIllumination = power && st.parkingLights;
            Color print = NightIllumination ? nightIllumination : power ? dayPrint : dayPrint * Mathf.Lerp(0.12f, 1f, WeatherController.Daylight01);
            foreach (var m in dialMats) if (m != null) m.SetColor("_BaseColor", print);
            if (gearMat != null) gearMat.SetColor("_BaseColor", NightIllumination ? nightIllumination : Color.white);

            if (fuelBar != null)
            {
                fuelBar.enabled = power;
                float level = adapter.Solver.Fuel.Level01;
                int lit = Mathf.Clamp(Mathf.CeilToInt(level * fuelSegments - 0.05f), 0, fuelSegments);
                bool blink = st.fuelLitres < reserveLitres && (Time.time % 1f) < 0.5f; // last segment flashes on reserve
                if (lit != fuelShown || blink != fuelBlink)
                {
                    fuelShown = lit; fuelBlink = blink;
                    var tex = FuelBarTexture.Draw(lit, fuelSegments, blink);
                    var old = fuelMat.mainTexture; fuelMat.mainTexture = tex; if (fuelMat.HasProperty("_BaseMap")) fuelMat.SetTexture("_BaseMap", tex);
                    if (old != null && old != tex) Destroy(old);
                }
                fuelMat.SetColor("_BaseColor", NightIllumination ? nightIllumination : Color.white);
            }

            GearText = st.transmission == TransmissionType.Automatic ? st.selector.ToString() + (st.selector == AutomaticSelector.D && st.gear > 0 ? st.gear.ToString() : "")
                                                                     : st.gear == 0 ? "N" : st.gear < 0 ? "R" : st.gear.ToString();
            if (gearGlyph != null)
            {
                gearGlyph.enabled = power;
                char c = GearText[0];
                if (!glyphs.TryGetValue(c, out var tex)) glyphs[c] = tex = TelltaleIcons.Glyph(c);
                gearMat.mainTexture = tex; if (gearMat.HasProperty("_BaseMap")) gearMat.SetTexture("_BaseMap", tex);
            }

            if (relay != null)
            {
                bool lamp = st.indicatorLampOn && (st.leftIndicator || st.rightIndicator);
                if (lamp != lastLamp) relay.PlayOneShot(lamp ? tick : tock);
                lastLamp = lamp;
                if (st.horn && !horn.isPlaying) horn.Play(); else if (!st.horn && horn.isPlaying) horn.Stop();
            }
        }

        void Set(Telltale t, bool on) { if (icons.TryGetValue(t, out var r) && r != null) r.enabled = on; }
    }

    /// <summary>Procedural 64×64 telltale icons and 5×7 glyphs (alpha = shape).</summary>
    public static class TelltaleIcons
    {
        const int N = 64;

        public static Texture2D Draw(DashboardView.Telltale t)
        {
            var px = new Color32[N * N];
            Func<float, float, bool> f;
            switch (t)
            {
                case DashboardView.Telltale.TurnLeft: f = (x, y) => Arrow(-x, y); break;
                case DashboardView.Telltale.TurnRight: f = Arrow; break;
                case DashboardView.Telltale.LowBeam: f = (x, y) => Lamp(x, y) || Rays(x, y, -0.35f); break;
                case DashboardView.Telltale.HighBeam: f = (x, y) => Lamp(x, y) || Rays(x, y, 0f); break;
                case DashboardView.Telltale.Parking: f = (x, y) => Ring(x - 0.28f, y, 0.2f, 0.07f) || Ring(x + 0.28f, y, 0.2f, 0.07f); break;
                case DashboardView.Telltale.Handbrake: f = (x, y) => Ring(x, y, 0.7f, 0.09f) || Letter('P', x, y, 0.5f) || Arc(x, y); break;
                case DashboardView.Telltale.Battery: f = (x, y) => Box(x, y, 0.75f, 0.5f, 0.08f, -0.1f) || Rect(x, y, -0.5f, 0.4f, -0.3f, 0.52f) || Rect(x, y, 0.3f, 0.4f, 0.5f, 0.52f) || Rect(x, y, -0.5f, -0.14f, -0.2f, -0.06f) || Rect(x, y, 0.2f, -0.14f, 0.5f, -0.06f) || Rect(x, y, 0.31f, -0.25f, 0.39f, 0.05f); break;
                case DashboardView.Telltale.Oil: f = (x, y) => Rect(x, y, -0.7f, -0.3f, 0.3f, 0.05f) || Rect(x, y, -0.45f, 0.05f, -0.3f, 0.25f) || Line(x, y, 0.3f, 0.0f, 0.75f, 0.2f, 0.07f) || Drop(x - 0.72f, y + 0.25f); break;
                case DashboardView.Telltale.CheckEngine: f = (x, y) => Box(x, y, 0.8f, 0.45f, 0.09f, 0f) || Rect(x, y, -0.25f, 0.45f, 0.25f, 0.62f) || Rect(x, y, -0.95f, -0.15f, -0.8f, 0.15f) || Rect(x, y, 0.8f, -0.1f, 0.95f, 0.3f); break;
                case DashboardView.Telltale.LowFuel: f = Pump; break;
                default: f = (x, y) => Disk(x + 0.05f, y - 0.6f, 0.16f) || Line(x, y, -0.05f, 0.4f, -0.05f, -0.45f, 0.12f) || Line(x, y, -0.55f, 0.35f, 0.45f, -0.5f, 0.08f) || Line(x, y, -0.05f, -0.1f, 0.4f, 0.15f, 0.1f); break;
            }
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    float x = (i + 0.5f) / N * 2f - 1f, y = (j + 0.5f) / N * 2f - 1f;
                    px[j * N + i] = f(x, y) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            return Make(px, N, t.ToString());
        }

        // 5×7 glyphs for the gear display, rows top→bottom.
        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            {'P', new[]{"####.","#...#","#...#","####.","#....","#....","#...."}},
            {'R', new[]{"####.","#...#","#...#","####.","#.#..","#..#.","#...#"}},
            {'N', new[]{"#...#","##..#","#.#.#","#..##","#...#","#...#","#...#"}},
            {'D', new[]{"###..","#..#.","#...#","#...#","#...#","#..#.","###.."}},
            {'1', new[]{"..#..",".##..","..#..","..#..","..#..","..#..",".###."}},
            {'2', new[]{".###.","#...#","....#","...#.","..#..",".#...","#####"}},
            {'3', new[]{"####.","....#","....#",".###.","....#","....#","####."}},
            {'4', new[]{"...#.","..##.",".#.#.","#..#.","#####","...#.","...#."}},
            {'5', new[]{"#####","#....","####.","....#","....#","#...#",".###."}},
            {'6', new[]{".###.","#....","#....","####.","#...#","#...#",".###."}},
        };

        public static Texture2D Glyph(char c)
        {
            var px = new Color32[N * N];
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++)
                {
                    float x = (i + 0.5f) / N * 2f - 1f, y = (j + 0.5f) / N * 2f - 1f;
                    px[j * N + i] = Letter(c, x, y, 1.2f) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            return Make(px, N, "Glyph_" + c);
        }

        static bool Letter(char c, float x, float y, float scale)
        {
            if (!Font.TryGetValue(c, out var rows)) return false;
            // glyph box: width = scale, height = 1.4 × scale (5:7 cells), centred
            float gx = (x / scale + 0.5f) * 5f, gy = (0.5f - y / (1.4f * scale)) * 7f;
            int ix = Mathf.FloorToInt(gx), iy = Mathf.FloorToInt(gy);
            return ix >= 0 && ix < 5 && iy >= 0 && iy < 7 && rows[iy][ix] == '#';
        }

        static Texture2D Make(Color32[] px, int n, string name)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true); return tex;
        }

        static bool Arrow(float x, float y) => (x > 0.1f && Mathf.Abs(y) < 0.8f - (x - 0.1f) * 0.9f && x < 0.95f) || (x > -0.85f && x <= 0.15f && Mathf.Abs(y) < 0.25f);
        static bool Lamp(float x, float y) => Ring(x - 0.15f, y, 0.55f, 0.1f) && x > -0.2f || Rect(x, y, -0.25f, -0.55f, -0.15f, 0.55f);
        static bool Rays(float x, float y, float slope)
        {
            for (int k = -2; k <= 2; k++) { float y0 = k * 0.25f; if (Line(x, y, -0.95f, y0 + slope * 0.6f, -0.4f, y0, 0.06f)) return true; }
            return false;
        }
        /// <summary>Fuel pump symbol: body with a window, nozzle hose on the right.</summary>
        public static bool Pump(float x, float y) =>
            Box(x, y, 0.38f, 0.7f, 0.12f, 0f) && x < 0.38f || Rect(x, y, -0.26f, 0.15f, 0.26f, 0.45f) || Rect(x, y, -0.5f, -0.85f, 0.5f, -0.7f)
            || Line(x, y, 0.38f, 0.35f, 0.62f, 0.2f, 0.07f) || Line(x, y, 0.62f, 0.2f, 0.62f, -0.45f, 0.07f) || Line(x, y, 0.62f, -0.45f, 0.45f, -0.55f, 0.07f);

        static bool Arc(float x, float y) { float r = Mathf.Sqrt(x * x + y * y); return r > 0.82f && r < 0.95f && Mathf.Abs(y) < 0.5f; }
        static bool Ring(float x, float y, float r, float w) { float d = Mathf.Sqrt(x * x + y * y); return d > r - w && d < r + w; }
        static bool Disk(float x, float y, float r) => x * x + y * y < r * r;
        static bool Drop(float x, float y) => Disk(x, y + 0.08f, 0.1f) || (Mathf.Abs(x) < 0.1f - (y + 0.08f) * 0.5f && y > -0.08f && y < 0.12f);
        static bool Rect(float x, float y, float x0, float y0, float x1, float y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
        static bool Box(float x, float y, float hw, float hh, float w, float cy) => Rect(x, y, -hw, cy - hh, hw, cy + hh) && !Rect(x, y, -hw + w, cy - hh + w, hw - w, cy + hh - w);
        static bool Line(float x, float y, float x0, float y0, float x1, float y1, float w)
        {
            float dx = x1 - x0, dy = y1 - y0, t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy));
            float px = x0 + t * dx - x, py = y0 + t * dy - y; return px * px + py * py < w * w;
        }
    }

    /// <summary>Segmented fuel-level bar for the cluster display: pump icon, "E", segments, "F" (alpha = shape).</summary>
    public static class FuelBarTexture
    {
        public const int W = 256, H = 40;
        public const float Aspect = (float)W / H;

        public static Texture2D Draw(int lit, int segments, bool blinkOff)
        {
            var px = new Color32[W * H];
            void Fill(int x0, int y0, int x1, int y1, byte a)
            {
                for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++) for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++) px[y * W + x] = new Color32(a, a, a, 255); // cut-out material: brightness in RGB
            }
            // Pump icon in the left 40 px.
            for (int y = 0; y < H; y++) for (int x = 0; x < 40; x++)
                    if (TelltaleIcons.Pump((x + 0.5f) / 20f - 1f, (y + 0.5f) / 20f - 1f)) px[y * W + x] = new Color32(255, 255, 255, 255);
            // Segments between x = 50 and 246; unlit ones are drawn as dim frames so the scale is readable.
            float step = 196f / segments;
            for (int i = 0; i < segments; i++)
            {
                int x0 = 50 + Mathf.RoundToInt(i * step), x1 = 50 + Mathf.RoundToInt((i + 1) * step) - 5;
                bool on = i < lit && !(blinkOff && i == lit - 1 && lit <= 1);
                if (on) Fill(x0, 8, x1, 32, 255);
                else { Fill(x0, 8, x1, 11, 70); Fill(x0, 29, x1, 32, 70); Fill(x0, 8, x0 + 3, 32, 70); Fill(x1 - 3, 8, x1, 32, 70); }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "FuelBar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }
    }

    /// <summary>
    /// Procedural printed dial: ticks, stroke-font numbers and an optional red zone on a transparent texture.
    /// Angles are measured from 12 o'clock, clockwise as seen by the driver; zero sits at −sweep/2.
    /// </summary>
    public static class GaugeDial
    {
        const int N = 1024;
        static readonly Color32 Ink = new Color32(214, 236, 240, 255), RedInk = new Color32(255, 70, 50, 255);

        // Seven-segment strokes on a 1 × 1.6 box: a top, b upper right, c lower right, d bottom, e lower left, f upper left, g middle.
        static readonly string[] Segments = { "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgedc", "abc", "abcdefg", "abcdfg" };

        /// <param name="radius">face radius, metres (the texture spans 2 × radius)</param>
        /// <param name="max">scale value at the end of the sweep</param>
        /// <param name="major">major tick step</param> <param name="minor">minor tick step</param>
        /// <param name="labelEvery">numbers at multiples of this value</param> <param name="redFrom">start of the red zone</param>
        public static Texture2D Draw(float radius, float sweepDeg, float max, float major, float minor, float labelEvery, float redFrom)
        {
            var px = new Color32[N * N];
            float s = N / (2f * radius); // pixels per metre
            float start = -0.5f * sweepDeg;
            float Angle(float value) => start + sweepDeg * Mathf.Clamp01(value / max);

            // Red zone band.
            if (redFrom < max)
            {
                float a0 = Angle(redFrom), a1 = Angle(max), r0 = 0.845f * radius * s, r1 = 0.905f * radius * s;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = x + 0.5f - N / 2f, dy = y + 0.5f - N / 2f, r = Mathf.Sqrt(dx * dx + dy * dy);
                        if (r < r0 - 1 || r > r1 + 1) continue;
                        float a = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                        if (a < a0 || a > a1) continue;
                        float cov = Mathf.Clamp01(Mathf.Min(r - r0, r1 - r) + 0.5f);
                        Blend(px, x, y, RedInk, cov);
                    }
            }
            // Ticks.
            for (float v = 0f; v <= max + 1e-3f; v += minor)
            {
                bool isMajor = Mathf.Abs(v / major - Mathf.Round(v / major)) < 1e-3f;
                float a = Angle(v) * Mathf.Deg2Rad;
                float rIn = (isMajor ? 0.77f : 0.84f) * radius, rOut = 0.91f * radius;
                var col = v >= redFrom - 1e-3f ? RedInk : Ink;
                Line(px, Polar(a, rIn, s), Polar(a, rOut, s), (isMajor ? 0.0011f : 0.0007f) * s, col);
            }
            // Numbers.
            int digits = Mathf.RoundToInt(max).ToString().Length;
            float h = (digits >= 3 ? 0.10f : digits == 2 ? 0.12f : 0.15f) * radius, gap = 0.28f * h, dw = 0.58f * h, stroke = 0.085f * h;
            for (float v = 0f; v <= max + 1e-3f; v += labelEvery)
            {
                string text = Mathf.RoundToInt(v).ToString();
                float a = Angle(v) * Mathf.Deg2Rad;
                Vector2 c = Polar(a, 0.62f * radius, s);
                float totalW = (text.Length * dw + (text.Length - 1) * gap) * s;
                var col = v >= redFrom - 1e-3f ? RedInk : Ink;
                for (int i = 0; i < text.Length; i++)
                {
                    float x0 = c.x - totalW / 2f + i * (dw + gap) * s, y0 = c.y - h * s / 2f;
                    Digit(px, text[i] - '0', x0, y0, dw * s, h * s, stroke * s, col);
                }
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "GaugeDial", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }

        static Vector2 Polar(float aRad, float r, float s) => new Vector2(N / 2f + Mathf.Sin(aRad) * r * s, N / 2f + Mathf.Cos(aRad) * r * s);

        static void Digit(Color32[] px, int d, float x0, float y0, float w, float h, float stroke, Color32 col)
        {
            if (d < 0 || d > 9) return;
            float m = y0 + h * 0.5f, t = y0 + h, r = x0 + w;
            foreach (char seg in Segments[d])
            {
                Vector2 p, q;
                switch (seg)
                {
                    case 'a': p = new Vector2(x0, t); q = new Vector2(r, t); break;
                    case 'b': p = new Vector2(r, t); q = new Vector2(r, m); break;
                    case 'c': p = new Vector2(r, m); q = new Vector2(r, y0); break;
                    case 'd': p = new Vector2(x0, y0); q = new Vector2(r, y0); break;
                    case 'e': p = new Vector2(x0, m); q = new Vector2(x0, y0); break;
                    case 'f': p = new Vector2(x0, t); q = new Vector2(x0, m); break;
                    default: p = new Vector2(x0, m); q = new Vector2(r, m); break;
                }
                Line(px, p, q, stroke, col);
            }
        }

        /// <summary>Anti-aliased thick segment with round caps.</summary>
        static void Line(Color32[] px, Vector2 p, Vector2 q, float halfWidth, Color32 col)
        {
            int xmin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p.x, q.x) - halfWidth - 2)), xmax = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(p.x, q.x) + halfWidth + 2));
            int ymin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(p.y, q.y) - halfWidth - 2)), ymax = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(p.y, q.y) + halfWidth + 2));
            Vector2 d = q - p; float len2 = Mathf.Max(1e-6f, d.sqrMagnitude);
            for (int y = ymin; y <= ymax; y++)
                for (int x = xmin; x <= xmax; x++)
                {
                    var c = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(c - p, d) / len2);
                    float dist = (p + d * t - c).magnitude;
                    float cov = Mathf.Clamp01(halfWidth + 0.5f - dist);
                    if (cov > 0f) Blend(px, x, y, col, cov);
                }
        }

        static void Blend(Color32[] px, int x, int y, Color32 col, float cov)
        {
            int i = y * N + x; byte a = (byte)(cov * 255f);
            if (a > px[i].a) px[i] = new Color32(col.r, col.g, col.b, a);
        }
    }
}
