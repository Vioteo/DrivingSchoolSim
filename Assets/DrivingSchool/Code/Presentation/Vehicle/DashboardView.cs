using System;
using System.Collections.Generic;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Instrument-cluster telltales on the car model: turn arrows, low/high beam, parking lights, handbrake,
    /// battery, oil pressure, check engine, seat belt and the gear/selector display. Icons are drawn
    /// procedurally (no texture assets). Also plays the indicator relay click and the horn.
    /// Needles are animated by VehicleVisuals.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class DashboardView : MonoBehaviour
    {
        public enum Telltale { TurnLeft, TurnRight, LowBeam, HighBeam, Parking, Handbrake, Battery, Oil, CheckEngine, Seatbelt }

        public VehiclePhysicsAdapter adapter;
        public Transform model;
        public Material templateMaterial;         // URP Unlit (assigned by the scene builder so the shader ships in builds)
        [Tooltip("Car-space offset added to the automatically found telltale row position.")]
        public Vector3 rowOffset = Vector3.zero;
        public float iconSizeM = 0.016f, iconGapM = 0.004f;
        public bool sounds = true;

        static readonly Color Green = new Color(0.15f, 1f, 0.3f), Blue = new Color(0.2f, 0.45f, 1f), Red = new Color(1f, 0.12f, 0.08f), Amber = new Color(1f, 0.62f, 0.05f);

        readonly Dictionary<Telltale, Renderer> icons = new Dictionary<Telltale, Renderer>();
        Renderer gearGlyph; Material gearMat; readonly Dictionary<char, Texture2D> glyphs = new Dictionary<char, Texture2D>();
        AudioSource relay, horn; AudioClip tick, tock;
        bool lastLamp; float bulbCheck;
        Transform car, eye;

        public bool IsLit(Telltale t) => icons.TryGetValue(t, out var r) && r != null && r.enabled;
        public string GearText { get; private set; } = "";

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
            Vector3 centre; Vector3 right = Vector3.right;
            var rpm = VehicleRigUtil.Find(model, "GaugeFace_RPM"); var spd = VehicleRigUtil.Find(model, "GaugeFace_km");
            var hood = VehicleRigUtil.Find(model, "Instrument_Hood") ?? VehicleRigUtil.Find(model, "Cluster_Display");
            if (rpm != null && spd != null)
            {
                var a = CarBounds(rpm); var b = CarBounds(spd);
                centre = 0.5f * (a.center + b.center); centre.y = Mathf.Min(a.min.y, b.min.y) + iconSizeM * 0.2f;
            }
            else if (hood != null) { var h = CarBounds(hood); centre = new Vector3(h.center.x, h.center.y - 0.02f, h.min.z + 0.05f); }
            else centre = EyeInCar() + new Vector3(0f, -0.28f, 0.62f);
            centre += rowOffset;
            Vector3 toEye = (EyeInCar() - centre).normalized;
            centre += toEye * 0.004f; // just in front of the gauge faces
            Quaternion face = Quaternion.LookRotation(-toEye, Vector3.up); // quad normal (−Z) points at the eye

            var list = (Telltale[])Enum.GetValues(typeof(Telltale));
            float step = iconSizeM + iconGapM, x0 = -0.5f * step * (list.Length - 1);
            var root = new GameObject("Dashboard_Telltales").transform; root.SetParent(car, false);
            for (int i = 0; i < list.Length; i++)
            {
                var r = Quad("Telltale_" + list[i], root, centre + face * Vector3.right * (x0 + i * step), face, iconSizeM, TelltaleIcons.Draw(list[i]), ColorOf(list[i]));
                icons[list[i]] = r;
            }
            var gd = VehicleRigUtil.Find(model, "Gear_Display");
            Vector3 gearPos = gd != null ? CarBounds(gd).center + toEye * 0.004f : centre + face * Vector3.up * (iconSizeM * 1.6f);
            gearGlyph = Quad("Telltale_Gear", root, gearPos, face, iconSizeM * 1.3f, TelltaleIcons.Glyph('N'), Color.white);
            gearMat = gearGlyph.sharedMaterial;
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
                case Telltale.CheckEngine: return Amber;
                default: return Red;
            }
        }

        Renderer Quad(string name, Transform parent, Vector3 carPos, Quaternion carRot, float size, Texture2D tex, Color color)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false); q.transform.localPosition = carPos; q.transform.localRotation = carRot; q.transform.localScale = Vector3.one * size;
            var r = q.GetComponent<Renderer>();
            Material m;
            if (templateMaterial != null) m = new Material(templateMaterial);
            else { var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent Cutout"); m = new Material(sh); }
            m.mainTexture = tex; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color); else m.color = color;
            if (m.HasProperty("_AlphaClip")) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); }
            m.renderQueue = 2450;
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
}
