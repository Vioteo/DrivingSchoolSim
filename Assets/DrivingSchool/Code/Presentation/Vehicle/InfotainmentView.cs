using System.Globalization;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Simulation;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Centre screen (Infotainment_Glass) as a trip computer: clock and weather, fuel in the tank and range, instantaneous
    /// and trip-average consumption, trip distance, a slippery-road warning. The screen is self-lit (unlit materials),
    /// so it reads at night; it is dark with the ignition off. Text uses TextMesh with the built-in font.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class InfotainmentView : MonoBehaviour
    {
        public VehiclePhysicsAdapter adapter;
        public Transform model;
        public Color background = new Color(0.015f, 0.03f, 0.05f);
        public Color text = new Color(0.82f, 0.9f, 0.95f);
        public Color accent = new Color(1f, 0.62f, 0.2f);
        public Color warning = new Color(1f, 0.35f, 0.2f);
        [Tooltip("Average used for the range until the trip is long enough to have its own, l/100 km.")]
        public float defaultAverage = 8f;

        Transform car; Renderer screen; Material screenMat;
        TextMesh clock, weather, fuel, range, instant, average, trip, alert;
        double tripMetres; float tripLitres, lastFuel = -1f;
        readonly CultureInfo ru = CultureInfo.GetCultureInfo("ru-RU");
        static Font font;

        public bool IsOn => screen != null && screen.enabled;
        public string FuelLine => fuel != null ? fuel.text : "";

        void Start()
        {
            if (adapter == null) adapter = GetComponentInParent<VehiclePhysicsAdapter>();
            car = adapter != null ? adapter.transform : transform;
            if (model == null) model = car;
            var glass = VehicleRigUtil.Find(model, "Infotainment_Glass");
            var mf = glass != null ? glass.GetComponent<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) { enabled = false; return; }
            Build(glass, mf.sharedMesh.bounds);
        }

        void Build(Transform glass, Bounds lb)
        {
            // Screen plane: the glass is a thin slab; its thinnest local axis is the normal, turned towards the driver.
            Vector3 size = lb.size;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            Vector3 n = car.InverseTransformDirection(glass.TransformDirection(thin == 0 ? Vector3.right : thin == 1 ? Vector3.up : Vector3.forward)).normalized;
            Vector3 c = car.InverseTransformPoint(glass.TransformPoint(lb.center));
            var eyeT = VehicleRigUtil.Find(model, "Socket_DriverEye");
            Vector3 eye = eyeT != null ? car.InverseTransformPoint(eyeT.position) : new Vector3(-0.37f, 1.15f, -0.3f);
            if (Vector3.Dot(n, eye - c) < 0f) n = -n;
            // Width/height: the two remaining axes measured in car space.
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n).normalized, right = Vector3.Cross(up, n);
            float w = 0f, h = 0f;
            foreach (var corner in Corners(lb))
            {
                Vector3 p = car.InverseTransformPoint(glass.TransformPoint(corner)) - c;
                w = Mathf.Max(w, Mathf.Abs(Vector3.Dot(p, right))); h = Mathf.Max(h, Mathf.Abs(Vector3.Dot(p, up)));
            }
            w *= 2f * 0.96f; h *= 2f * 0.94f;

            var root = new GameObject("Infotainment_Screen").transform; root.SetParent(car, false);
            root.localPosition = c + n * 0.0035f; root.localRotation = Quaternion.LookRotation(-n, up); // local −Z faces the driver
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "Screen_Background";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(root, false); q.transform.localScale = new Vector3(w, h, 1f);
            q.transform.localRotation = Quaternion.identity;
            screen = q.GetComponent<Renderer>(); screen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; screen.receiveShadows = false;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            screenMat = sh != null ? new Material(sh) : new Material(screen.sharedMaterial);
            screenMat.SetColor("_BaseColor", background); screen.sharedMaterial = screenMat;

            float pad = 0.06f * h, line = h / 7.2f;
            float left = -w / 2 + pad, rightX = w / 2 - pad, top = h / 2 - pad;
            clock = Text(root, "Clock", left, top, line * 0.8f, TextAnchor.UpperLeft, text);
            weather = Text(root, "Weather", rightX, top, line * 0.8f, TextAnchor.UpperRight, text);
            fuel = Text(root, "Fuel", left, top - line * 1.35f, line * 1.35f, TextAnchor.UpperLeft, accent);
            range = Text(root, "Range", rightX, top - line * 1.6f, line * 0.8f, TextAnchor.UpperRight, text);
            instant = Text(root, "Instant", left, top - line * 3.1f, line * 0.8f, TextAnchor.UpperLeft, text);
            average = Text(root, "Average", left, top - line * 4.1f, line * 0.8f, TextAnchor.UpperLeft, text);
            trip = Text(root, "Trip", left, top - line * 5.1f, line * 0.8f, TextAnchor.UpperLeft, text);
            alert = Text(root, "Alert", rightX, top - line * 5.1f, line * 0.8f, TextAnchor.UpperRight, warning);
        }

        static Vector3[] Corners(Bounds b)
        {
            var r = new Vector3[8]; int k = 0;
            for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) for (int l = -1; l <= 1; l += 2)
                        r[k++] = b.center + Vector3.Scale(b.extents, new Vector3(i, j, l));
            return r;
        }

        TextMesh Text(Transform root, string name, float x, float y, float height, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Screen_" + name); go.transform.SetParent(root, false);
            // TextMesh reads towards its local −Z; the root's −Z faces the driver. Slightly in front of the background.
            go.transform.localPosition = new Vector3(x, y, -0.0012f);
            var t = go.AddComponent<TextMesh>();
            t.fontSize = 64; t.characterSize = height * 10f / 64f; t.anchor = anchor; t.alignment = anchor == TextAnchor.UpperRight ? TextAlignment.Right : TextAlignment.Left;
            t.color = color; t.text = "";
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.font = font;
            var r = go.GetComponent<MeshRenderer>(); if (font != null) r.sharedMaterial = font.material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return t;
        }

        void LateUpdate()
        {
            if (adapter == null || adapter.Solver == null || screen == null) return;
            var st = adapter.CurrentState; bool power = adapter.LastCommand.ignition;

            // Trip counters; a jump up in fuel means refuelling or a respawn: start a new trip.
            if (lastFuel >= 0f && st.fuelLitres > lastFuel + 0.01f) { tripMetres = 0; tripLitres = 0f; }
            if (lastFuel >= 0f) tripLitres += Mathf.Max(0f, lastFuel - st.fuelLitres);
            tripMetres += Mathf.Abs(st.signedSpeedMps) * Time.deltaTime;
            lastFuel = st.fuelLitres;

            screenMat.SetColor("_BaseColor", power ? background : Color.black);
            foreach (var t in new[] { clock, weather, fuel, range, instant, average, trip, alert }) t.gameObject.SetActive(power);
            if (!power) return;

            var w = WeatherController.Current;
            float hours = (w.timeOfDayHours + Time.timeSinceLevelLoad / 3600f) % 24f;
            clock.text = $"{(int)hours:00}:{(int)(hours % 1f * 60f):00}";
            weather.text = WeatherName(w.preset);
            fuel.text = st.fuelLitres.ToString("F1", ru) + " л";
            float km = (float)(tripMetres / 1000.0);
            float avg = km > 0.3f && tripLitres > 0f ? tripLitres / km * 100f : defaultAverage;
            range.text = "запас " + Mathf.FloorToInt(st.fuelLitres / Mathf.Max(avg, 1f) * 100f) + " км";
            float per100 = FuelModel.PerHundredKm(st.fuelFlowLitresPerHour, st.signedSpeedMps);
            instant.text = st.engine != EnginePhase.Running ? "Расход: —"
                         : float.IsInfinity(per100) ? "Расход: " + st.fuelFlowLitresPerHour.ToString("F1", ru) + " л/ч"
                         : "Расход: " + per100.ToString("F1", ru) + " л/100 км";
            average.text = km > 0.3f ? "Средний: " + avg.ToString("F1", ru) + " л/100 км" : "Средний: —";
            trip.text = "Поездка: " + km.ToString("F1", ru) + " км";
            alert.text = st.fuelLitres < 7f ? "Мало топлива" : adapter.surface == SurfaceType.BlackIce || adapter.surface == SurfaceType.PackedSnow ? "Скользко" : "";
        }

        static string WeatherName(WeatherPreset p)
        {
            switch (p)
            {
                case WeatherPreset.Overcast: return "Пасмурно";
                case WeatherPreset.Rain: return "Дождь";
                case WeatherPreset.HeavyRain: return "Ливень";
                case WeatherPreset.Fog: return "Туман";
                case WeatherPreset.Snow: return "Снег";
                case WeatherPreset.ClearNight: return "Ясная ночь";
                case WeatherPreset.RainNight: return "Дождь, ночь";
                case WeatherPreset.FullMoonNight: return "Полнолуние";
                default: return "Ясно";
            }
        }

        void OnDestroy() { if (screenMat != null) Destroy(screenMat); }
    }
}
