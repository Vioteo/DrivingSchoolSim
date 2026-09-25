using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Simulation;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Water and snow on every window of the car (Glass_* parts): windshield, side and rear glass.
    /// Each pane keeps a WindshieldWetnessModel grid; the GlassWater shader turns it into drops that refract the
    /// scene, a water film when soaked, or stuck flakes and frost in snow. Precipitation reaches the glass only
    /// when the particles do (WeatherController.GlassRain01/GlassSnow01). The wiper blades (Wiper_Pivot_*) clear
    /// the windshield; head wind adds drops on the windshield and shields the rear glass.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class WindshieldRainView : MonoBehaviour
    {
        public VehiclePhysicsAdapter adapter;
        public VehicleVisuals visuals;
        public Transform model;
        [Tooltip("GlassWater material (DrivingSchool/GlassWater). An old URP Unlit water material is replaced at runtime.")]
        public Material templateMaterial;
        public int gridWidth = 128, gridHeight = 64;      // windshield grid; other panes use the same cell size
        [Range(0f, 1f)] public float maxAlpha = 0.6f;     // fallback look without the GlassWater shader
        public float runOffPerSecond = 0.015f;
        [Tooltip("Size of one repeat of the drop atlas on the glass, metres.")]
        public float dropTileM = 0.32f;

        sealed class Pane
        {
            public string name;
            public bool windshield;
            public float rainFactor;                   // share of the precipitation this pane catches at standstill
            public WindshieldWetnessModel water;
            public bool[] mask;
            public Texture2D tex; public byte[] buf; public Color32[] fallbackPixels;
            public Material mat; public Renderer renderer;
            public Vector3 origin, u, v, normal;       // car space
        }

        readonly List<Pane> panes = new List<Pane>();
        readonly List<WiperBlade> blades = new List<WiperBlade>();
        Pane front;
        static Texture2D dropAtlas;
        bool glassShader;
        float lastAngle, cameraCheck; int frame;
        Transform car;

        public WindshieldWetnessModel Water => front?.water;
        public int BladeCount => blades.Count;
        public int PaneCount => panes.Count;
        /// <summary>Mean water level of a pane by its model name (Glass_Front_L…), −1 if there is no such pane.</summary>
        public float PaneCoverage(string glassName) { foreach (var p in panes) if (p.name == glassName) return p.water.Coverage; return -1f; }

        /// <summary>Mean water level over the cells the blades can reach in a full sweep (the wiped zone only).</summary>
        public float SweptCoverage() { SweptZone(out float coverage, out _); return coverage; }

        /// <summary>Share of the visible glass (mask) that a full sweep of all blades passes over.</summary>
        public float SweptShare() { SweptZone(out _, out float share); return share; }

        void SweptZone(out float coverage, out float share)
        {
            coverage = share = 0f;
            if (front == null || blades.Count == 0) return;
            var water = front.water;
            var probe = new WindshieldWetnessModel(water.Width, water.Height, water.WidthM, water.HeightM);
            for (int i = 0; i < probe.Cells.Length; i++) probe.Cells[i] = 1f;
            foreach (var b in blades) probe.Wipe(b, 0f, 1f);
            float sum = 0f; int swept = 0, glass = 0, sweptGlass = 0;
            for (int i = 0; i < probe.Cells.Length; i++)
            {
                bool s = probe.Cells[i] == 0f;
                if (s) { sum += water.Cells[i]; swept++; }
                if (front.mask[i]) { glass++; if (s) sweptGlass++; }
            }
            coverage = swept > 0 ? sum / swept : 0f;
            share = glass > 0 ? (float)sweptGlass / glass : 0f;
        }

        void Start()
        {
            if (adapter == null) adapter = GetComponentInParent<VehiclePhysicsAdapter>();
            car = adapter != null ? adapter.transform : transform;
            if (model == null) model = car;
            if (visuals == null) visuals = GetComponentInParent<VehicleVisuals>();
            Build();
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            var shader = templateMaterial != null && templateMaterial.shader.name == "DrivingSchool/GlassWater" ? templateMaterial.shader : Shader.Find("DrivingSchool/GlassWater");
            glassShader = shader != null && shader.isSupported;
            if (glassShader && dropAtlas == null) dropAtlas = BuildDropAtlas(512, 1100, 11);

            var glassParts = VehicleRigUtil.FindPrefix(model, "Glass_");
            // Cabin centre: the panes' outward normals point away from it.
            Vector3 cabin = Vector3.zero; int nc = 0;
            var meshes = new List<(Transform t, Vector3[] pts, int[] tris)>();
            foreach (var t in glassParts)
            {
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var vs = mf.sharedMesh.vertices; var pts = new Vector3[vs.Length];
                for (int i = 0; i < vs.Length; i++) { pts[i] = car.InverseTransformPoint(t.TransformPoint(vs[i])); cabin += pts[i]; nc++; }
                meshes.Add((t, pts, mf.sharedMesh.triangles));
            }
            if (nc > 0) cabin /= nc;

            foreach (var (t, pts, tris) in meshes)
            {
                bool ws = t.name == "Glass_Windshield";
                var p = MakePane(t.name, pts, tris, cabin, ws);
                if (p == null) continue;
                panes.Add(p);
                if (ws) front = p;
            }
            if (front == null) // no readable windshield in the model: generic sedan windshield
            {
                var pts = new[] { new Vector3(-0.7f, 0.95f, 0.95f), new Vector3(0.7f, 0.95f, 0.95f), new Vector3(0.6f, 1.35f, 0.25f), new Vector3(-0.6f, 1.35f, 0.25f) };
                front = MakePane("Glass_Windshield", pts, new[] { 0, 1, 2, 0, 2, 3 }, new Vector3(0f, 1f, 0f), true);
                panes.Add(front);
            }
            BuildBlades();
        }

        Pane MakePane(string name, Vector3[] pts, int[] tris, Vector3 cabin, bool ws)
        {
            if (pts.Length < 3 || tris.Length < 3) return null;
            Vector3 centre = Vector3.zero; foreach (var q in pts) centre += q; centre /= pts.Length;
            Vector3 normal = Vector3.Cross(pts[tris[1]] - pts[tris[0]], pts[tris[2]] - pts[tris[0]]).normalized;
            if (Vector3.Dot(normal, centre - cabin) < 0f) normal = -normal; // outwards
            if (normal.sqrMagnitude < 0.5f) return null;

            // Plane axes: u along the glass horizontally, v up the glass.
            bool side = Mathf.Abs(normal.x) > 0.6f;
            Vector3 u = side ? Vector3.ProjectOnPlane(Vector3.forward, normal).normalized : Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            Vector3 v = Vector3.Cross(normal, u).normalized;
            if (v.y < 0f) v = -v;

            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var q in pts) { float a = Vector3.Dot(q, u), b = Vector3.Dot(q, v); minU = Mathf.Min(minU, a); maxU = Mathf.Max(maxU, a); minV = Mathf.Min(minV, b); maxV = Mathf.Max(maxV, b); }
            float width = maxU - minU, height = maxV - minV;
            if (width < 0.05f || height < 0.05f) return null;
            // Just inside the glass (cabin side) so the car's own transparent glass does not hide it.
            Vector3 inset = -normal * 0.006f;
            Vector3 origin = u * minU + v * minV + normal * Vector3.Dot(centre, normal);

            int gw, gh;
            if (ws) { gw = gridWidth; gh = gridHeight; }
            else { float cell = 1.4f / Mathf.Max(4, gridWidth); gw = Mathf.Clamp(Mathf.RoundToInt(width / cell), 8, 256); gh = Mathf.Clamp(Mathf.RoundToInt(height / cell), 8, 256); }

            var p = new Pane
            {
                name = name, windshield = ws,
                rainFactor = ws ? 1f : name == "Glass_Rear" ? 0.7f : 0.45f,
                water = new WindshieldWetnessModel(gw, gh, width, height, 7 + panes.Count * 13),
                origin = origin, u = u, v = v, normal = normal,
            };

            // Glass outline in grid cells (for coverage statistics).
            var poly = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) poly[i] = new Vector2((Vector3.Dot(pts[i], u) - minU) / width, (Vector3.Dot(pts[i], v) - minV) / height);
            p.mask = new bool[gw * gh];
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                    p.mask[y * gw + x] = InsideTriangles(new Vector2((x + 0.5f) / gw, (y + 0.5f) / gh), poly, tris);

            // Overlay mesh = the glass itself, slightly inside, UV = plane coordinates; double-sided.
            var go = new GameObject("Water_" + name); go.transform.SetParent(car, false);
            var mf = go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>();
            var verts = new Vector3[pts.Length]; var uvs = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) { verts[i] = pts[i] + inset; uvs[i] = poly[i]; }
            var both = new int[tris.Length * 2];
            for (int i = 0; i < tris.Length; i += 3)
            {
                both[i] = tris[i]; both[i + 1] = tris[i + 1]; both[i + 2] = tris[i + 2];
                both[tris.Length + i] = tris[i]; both[tris.Length + i + 1] = tris[i + 2]; both[tris.Length + i + 2] = tris[i + 1];
            }
            var mesh = new Mesh { name = "GlassWater_" + name, vertices = verts, uv = uvs, triangles = both };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mf.sharedMesh = mesh;

            p.tex = new Texture2D(gw, gh, glassShader ? TextureFormat.R8 : TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Wet_" + name };
            if (glassShader)
            {
                p.buf = new byte[gw * gh];
                p.mat = new Material(templateMaterial != null && templateMaterial.shader.name == "DrivingSchool/GlassWater" ? templateMaterial : new Material(Shader.Find("DrivingSchool/GlassWater")));
                p.mat.SetTexture("_WetMap", p.tex);
                p.mat.SetTexture("_DropTex", dropAtlas);
                p.mat.SetVector("_DropTiling", new Vector4(width / dropTileM, height / dropTileM, 0, 0));
            }
            else
            {
                p.fallbackPixels = new Color32[gw * gh];
                p.mat = templateMaterial != null ? new Material(templateMaterial) : MakeTransparentUnlit();
                p.mat.mainTexture = p.tex; if (p.mat.HasProperty("_BaseMap")) p.mat.SetTexture("_BaseMap", p.tex);
            }
            mr.sharedMaterial = p.mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.enabled = false;
            p.renderer = mr;
            return p;
        }

        static bool InsideTriangles(Vector2 q, Vector2[] poly, int[] tris)
        {
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector2 a = poly[tris[i]], b = poly[tris[i + 1]], c = poly[tris[i + 2]];
                float d1 = Cross(q, a, b), d2 = Cross(q, b, c), d3 = Cross(q, c, a);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                if (!(neg && pos)) return true;
            }
            return false;
        }
        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        void BuildBlades()
        {
            if (front == null) return;
            Vector3 o = front.origin; Vector3 uDir = front.u, vDir = front.v;
            float width = front.water.WidthM;
            if (visuals != null)
                for (int i = 0; i < visuals.WiperCount; i++)
                {
                    if (!visuals.WiperGeometry(i, out var pivot, out var tip, out var axis, out var sweepDeg)) continue;
                    Vector2 P = Plane(pivot, o, uDir, vDir), T0 = Plane(tip, o, uDir, vDir);
                    Vector3 tipEnd = pivot + Quaternion.AngleAxis(sweepDeg, axis) * (tip - pivot);
                    Vector2 T1 = Plane(tipEnd, o, uDir, vDir);
                    float a0 = Mathf.Atan2(T0.y - P.y, T0.x - P.x), a1 = Mathf.Atan2(T1.y - P.y, T1.x - P.x);
                    float sweep = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    if (Mathf.Abs(sweep) < 0.5f) sweep = Mathf.Sign(sweep == 0 ? 1 : sweep) * Mathf.Abs(sweepDeg) * Mathf.Deg2Rad;
                    float len = (T0 - P).magnitude;
                    blades.Add(new WiperBlade { pivotU = P.x, pivotV = P.y, innerRadiusM = 0.18f * len, outerRadiusM = len * 1.02f, parkAngleRad = a0, sweepRad = sweep });
                }
            if (blades.Count == 0) // two blades parked at the bottom, sweeping up to the left (LHD)
            {
                blades.Add(new WiperBlade { pivotU = 0.3f * width, pivotV = 0.02f, innerRadiusM = 0.08f, outerRadiusM = 0.52f * width, parkAngleRad = 0.05f, sweepRad = 1.75f });
                blades.Add(new WiperBlade { pivotU = 0.78f * width, pivotV = 0.02f, innerRadiusM = 0.08f, outerRadiusM = 0.45f * width, parkAngleRad = 0.05f, sweepRad = 1.75f });
            }
        }

        static Vector2 Plane(Vector3 p, Vector3 origin, Vector3 u, Vector3 v) { var d = p - origin; return new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v)); }

        /// <summary>
        /// Tiling drop atlas. RG = surface normal of the drop (a spherical cap, some drops stretched as if running),
        /// B = wetness level at which the drop shows up (small drops first, big ones on a soaked glass), A = coverage.
        /// </summary>
        static Texture2D BuildDropAtlas(int size, int count, int seed)
        {
            var rng = new System.Random(seed);
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 255, 0);
            for (int k = 0; k < count; k++)
            {
                float big = (float)rng.NextDouble();
                float r = Mathf.Lerp(1.8f, 8.5f, big * big);                       // mostly small drops
                float stretch = rng.NextDouble() < 0.18 ? Mathf.Lerp(1.4f, 2.4f, (float)rng.NextDouble()) : 1f;
                float cx = (float)rng.NextDouble() * size, cy = (float)rng.NextDouble() * size;
                float threshold = Mathf.Clamp01(0.05f + 0.75f * (float)rng.NextDouble() * 0.7f + 0.3f * big);
                byte th = (byte)(threshold * 255f);
                int ext = Mathf.CeilToInt(r * stretch) + 2;
                for (int y = -ext; y <= ext; y++)
                    for (int x = -ext; x <= ext; x++)
                    {
                        float dx = (x + 0.5f - (cx - Mathf.Floor(cx))) / r, dy = (y + 0.5f - (cy - Mathf.Floor(cy))) / (r * stretch);
                        // Running drops have a round head at the bottom and a thin tail above.
                        if (stretch > 1f && dy > 0f) dx /= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(dy));
                        float q = Mathf.Sqrt(dx * dx + dy * dy);
                        float cov = Mathf.Clamp01((1f - q) * r * 0.9f);
                        if (cov <= 0f) continue;
                        int ix = ((Mathf.FloorToInt(cx) + x) % size + size) % size, iy = ((Mathf.FloorToInt(cy) + y) % size + size) % size;
                        int idx = iy * size + ix;
                        byte a = (byte)(cov * 255f);
                        if (a <= px[idx].a) continue;
                        float nx = Mathf.Clamp(dx, -1f, 1f) * 0.95f, ny = Mathf.Clamp(dy, -1f, 1f) * 0.95f;
                        px[idx] = new Color32((byte)(128 + nx * 127f), (byte)(128 + ny * 127f), th, a);
                    }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "GlassDropAtlas", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }

        static Material MakeTransparentUnlit()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            m.renderQueue = 3100;
            return m;
        }

        // ------------------------------------------------------------------ update

        void LateUpdate()
        {
            if (panes.Count == 0 || adapter == null || adapter.Solver == null) return;
            float dt = Time.deltaTime; var st = adapter.CurrentState;
            float rain = WeatherController.GlassRain01, snow = WeatherController.GlassSnow01;
            float precip = Mathf.Max(rain, snow * 0.6f);
            bool snowing = snow > rain || (precip <= 0f && WeatherController.Current.snowIntensity01 > 0f);
            float speed = st.signedSpeedMps;

            foreach (var p in panes)
            {
                float amount = precip * p.rainFactor;
                float wind = 0f;
                if (p.windshield) wind = Mathf.Max(0f, speed);                    // head wind drives drops onto the windshield
                else if (p.name == "Glass_Rear") amount /= 1f + Mathf.Abs(speed) / 8f; // the roof shields the rear glass at speed
                else amount *= 1f + Mathf.Clamp01(Mathf.Abs(speed) / 20f) * 0.5f;   // side glass gets spray at speed
                p.water.AddRain(amount, wind, dt);
                float runOff = runOffPerSecond + (precip <= 0f ? 0.05f : 0f);
                if (!p.windshield && !snowing) runOff += 0.01f + 0.0015f * Mathf.Abs(speed); // vertical glass drains faster
                p.water.Dry(runOff, dt);
            }
            float a = st.wiperAngle01;
            if (a != lastAngle && front != null) foreach (var b in blades) front.water.Wipe(b, lastAngle, a);
            lastAngle = a;

            if (glassShader && (cameraCheck -= dt) <= 0f) { cameraCheck = 1f; RequestSceneColor(); }
            if ((++frame & 1) != 0) return;
            float light = Mathf.Clamp(RenderSettings.ambientSkyColor.maxColorComponent * 1.3f, 0.08f, 1.2f);
            foreach (var p in panes) Upload(p, snowing, light);
        }

        void Upload(Pane p, bool snowing, float light)
        {
            var cells = p.water.Cells; var mask = p.mask;
            if (glassShader)
            {
                for (int i = 0; i < cells.Length; i++) p.buf[i] = mask[i] ? (byte)(Mathf.Clamp01(cells[i]) * 255f) : (byte)0;
                p.tex.SetPixelData(p.buf, 0); p.tex.Apply(false);
                p.mat.SetFloat("_Snow", snowing ? 1f : 0f);
                p.mat.SetFloat("_Light", light);
            }
            else
            {
                byte cr = snowing ? (byte)245 : (byte)190, cg = snowing ? (byte)245 : (byte)205, cb = snowing ? (byte)250 : (byte)225;
                for (int i = 0; i < cells.Length; i++)
                    p.fallbackPixels[i] = new Color32(cr, cg, cb, mask[i] ? (byte)(Mathf.Clamp01(cells[i]) * maxAlpha * 255f) : (byte)0);
                p.tex.SetPixels32(p.fallbackPixels); p.tex.Apply(false);
            }
            p.renderer.enabled = p.water.Coverage > 0.0005f;
        }

        /// <summary>The drops refract _CameraOpaqueTexture: ask URP for it on every camera that can see the glass.</summary>
        static void RequestSceneColor()
        {
            foreach (var cam in Camera.allCameras)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null && data.requiresColorOption != CameraOverrideOption.On) data.requiresColorOption = CameraOverrideOption.On;
            }
        }
    }
}
