using System.Collections.Generic;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Simulation;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Water on the windshield seen from the cabin. A transparent quad is fitted inside Glass_Windshield;
    /// its texture is the WindshieldWetnessModel grid. Rain (and snow, whiter) accumulates faster with speed,
    /// the wiper blades clear the sectors they sweep (geometry taken from the Wiper_Pivot_* parts).
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class WindshieldRainView : MonoBehaviour
    {
        public VehiclePhysicsAdapter adapter;
        public VehicleVisuals visuals;
        public Transform model;
        public Material templateMaterial;        // URP Unlit, transparent (assigned by the builder)
        public int gridWidth = 128, gridHeight = 64;
        [Range(0f, 1f)] public float maxAlpha = 0.6f;
        public float runOffPerSecond = 0.015f;

        WindshieldWetnessModel water;
        readonly List<WiperBlade> blades = new List<WiperBlade>();
        Texture2D tex; Color32[] pixels; bool[] mask; Material mat; Renderer quad;
        float lastAngle; int frame;
        Transform car;

        public WindshieldWetnessModel Water => water;
        public int BladeCount => blades.Count;

        /// <summary>Mean water level over the cells the blades can reach in a full sweep (the wiped zone only).</summary>
        public float SweptCoverage() { SweptZone(out float coverage, out _); return coverage; }

        /// <summary>Share of the visible glass (mask) that a full sweep of all blades passes over.</summary>
        public float SweptShare() { SweptZone(out _, out float share); return share; }

        void SweptZone(out float coverage, out float share)
        {
            coverage = share = 0f;
            if (water == null || blades.Count == 0) return;
            var probe = new WindshieldWetnessModel(water.Width, water.Height, water.WidthM, water.HeightM);
            for (int i = 0; i < probe.Cells.Length; i++) probe.Cells[i] = 1f;
            foreach (var b in blades) probe.Wipe(b, 0f, 1f);
            float sum = 0f; int swept = 0, glass = 0, sweptGlass = 0;
            for (int i = 0; i < probe.Cells.Length; i++)
            {
                bool s = probe.Cells[i] == 0f;
                if (s) { sum += water.Cells[i]; swept++; }
                if (mask[i]) { glass++; if (s) sweptGlass++; }
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

        void Build()
        {
            var glassT = VehicleRigUtil.Find(model, "Glass_Windshield");
            var glass = glassT != null ? glassT.GetComponentInChildren<MeshFilter>() : null;
            var pts = new List<Vector3>();
            if (glass != null && glass.sharedMesh != null && glass.sharedMesh.isReadable)
                foreach (var v in glass.sharedMesh.vertices) pts.Add(car.InverseTransformPoint(glass.transform.TransformPoint(v)));
            if (pts.Count < 3)
            {   // generic sedan windshield
                pts.AddRange(new[] { new Vector3(-0.7f, 0.95f, 0.95f), new Vector3(0.7f, 0.95f, 0.95f), new Vector3(-0.6f, 1.35f, 0.25f), new Vector3(0.6f, 1.35f, 0.25f) });
            }
            float minY = float.MaxValue, maxY = float.MinValue; foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            float band = 0.06f * (maxY - minY) + 0.01f;
            Vector3 bot = Vector3.zero, top = Vector3.zero; int nb = 0, nt = 0;
            float botMinX = float.MaxValue, botMaxX = float.MinValue, topMinX = float.MaxValue, topMaxX = float.MinValue;
            foreach (var p in pts)
            {
                if (p.y < minY + band) { bot += p; nb++; botMinX = Mathf.Min(botMinX, p.x); botMaxX = Mathf.Max(botMaxX, p.x); }
                if (p.y > maxY - band) { top += p; nt++; topMinX = Mathf.Min(topMinX, p.x); topMaxX = Mathf.Max(topMaxX, p.x); }
            }
            bot /= Mathf.Max(1, nb); top /= Mathf.Max(1, nt);
            bot.x = 0.5f * (botMinX + botMaxX); top.x = 0.5f * (topMinX + topMaxX);
            Vector3 uDir = Vector3.right, vDir = (top - bot).normalized;
            Vector3 normal = Vector3.Cross(uDir, vDir).normalized; // points up/forward (outside) for a raked windshield
            if (normal.y < 0f) normal = -normal;
            float width = botMaxX - botMinX, height = (top - bot).magnitude, topWidth = topMaxX - topMinX;
            Vector3 origin = bot - uDir * (0.5f * width) - normal * 0.012f; // lower-left corner, just inside the glass

            water = new WindshieldWetnessModel(gridWidth, gridHeight, width, height, 7);
            pixels = new Color32[gridWidth * gridHeight]; mask = new bool[pixels.Length];
            for (int y = 0; y < gridHeight; y++)
            {
                float t = (y + 0.5f) / gridHeight, half = 0.5f * Mathf.Lerp(width, topWidth, t) - 0.02f;
                for (int x = 0; x < gridWidth; x++) mask[y * gridWidth + x] = Mathf.Abs((x + 0.5f) / gridWidth * width - 0.5f * width) < half;
            }
            tex = new Texture2D(gridWidth, gridHeight, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WindshieldWater" };

            var go = new GameObject("Windshield_Water"); go.transform.SetParent(car, false);
            var mf = go.AddComponent<MeshFilter>(); quad = go.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = "WindshieldWaterQuad" };
            mesh.vertices = new[] { origin, origin + uDir * width, origin + vDir * height, origin + uDir * width + vDir * height };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 }; // double-sided
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
            mat = templateMaterial != null ? new Material(templateMaterial) : MakeTransparentUnlit();
            mat.mainTexture = tex; if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            quad.sharedMaterial = mat; quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; quad.receiveShadows = false;

            // Wiper blades in windshield-plane coordinates.
            if (visuals != null)
                for (int i = 0; i < visuals.WiperCount; i++)
                {
                    if (!visuals.WiperGeometry(i, out var pivot, out var tip, out var axis, out var sweepDeg)) continue;
                    Vector2 P = Plane(pivot, origin, uDir, vDir), T0 = Plane(tip, origin, uDir, vDir);
                    Vector3 tipEnd = pivot + Quaternion.AngleAxis(sweepDeg, axis) * (tip - pivot);
                    Vector2 T1 = Plane(tipEnd, origin, uDir, vDir);
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

        void LateUpdate()
        {
            if (water == null || adapter == null || adapter.Solver == null) return;
            var w = WeatherController.Current;
            float dt = Time.deltaTime; var st = adapter.CurrentState;
            float precip = Mathf.Max(w.rainIntensity01, w.snowIntensity01 * 0.6f);
            water.AddRain(precip, Mathf.Max(0f, st.signedSpeedMps), dt);
            water.Dry(runOffPerSecond + (precip <= 0f ? 0.05f : 0f), dt);
            float a = st.wiperAngle01;
            if (a != lastAngle) foreach (var b in blades) water.Wipe(b, lastAngle, a);
            lastAngle = a;
            if ((++frame & 1) != 0) return;
            bool snow = w.snowIntensity01 > w.rainIntensity01;
            byte cr = snow ? (byte)245 : (byte)190, cg = snow ? (byte)245 : (byte)205, cb = snow ? (byte)250 : (byte)225;
            var cells = water.Cells;
            for (int i = 0; i < cells.Length; i++)
                pixels[i] = new Color32(cr, cg, cb, mask[i] ? (byte)(Mathf.Clamp01(cells[i]) * maxAlpha * 255f) : (byte)0);
            tex.SetPixels32(pixels); tex.Apply(false);
            quad.enabled = water.Coverage > 0.0005f;
        }
    }
}
