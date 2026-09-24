using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Scene weather and time of day: sun/ambient/fog/background, rain or snow particles around the camera,
    /// wet-road gloss and tyre grip (SurfaceType) for every registered vehicle. F5/F6 cycle presets.
    /// Values are visual/gameplay calibration, not meteorological data.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class WeatherController : MonoBehaviour
    {
        public static WeatherConditions Current { get; private set; } = WeatherConditions.FromPreset(WeatherPreset.ClearDay);

        public WeatherPreset preset = WeatherPreset.ClearDay;
        public Light sun;
        public Camera viewCamera;
        public Material precipitationMaterial;   // URP Particles/Unlit (assigned by the builder)
        public List<VehiclePhysicsAdapter> vehicles = new List<VehiclePhysicsAdapter>();
        public List<Renderer> roadRenderers = new List<Renderer>();
        public bool hotkeys = true;

        ParticleSystem rain, snow;
        readonly Dictionary<Material, float> drySmoothness = new Dictionary<Material, float>();
        float transition = 1f; WeatherConditions from, to;

        void Start()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            foreach (var r in roadRenderers)
                if (r != null) foreach (var m in r.sharedMaterials)
                        if (m != null && m.HasProperty("_Smoothness") && !drySmoothness.ContainsKey(m)) drySmoothness[m] = m.GetFloat("_Smoothness");
            rain = CreatePrecipitation("Rain", false);
            snow = CreatePrecipitation("Snow", true);
            SetPreset(preset, true);
        }

        void OnDestroy()
        {
            // Materials are shared assets: restore their dry look when leaving play mode.
            foreach (var kv in drySmoothness) if (kv.Key != null) kv.Key.SetFloat("_Smoothness", kv.Value);
        }

        public void SetPreset(WeatherPreset p, bool instant = false)
        {
            preset = p;
            from = Current; to = WeatherConditions.FromPreset(p);
            transition = instant ? 1f : 0f;
            Current = to; // gameplay (grip, wipers) switches at once; visuals blend
            foreach (var v in vehicles) if (v != null) v.surface = to.surface;
            if (instant) ApplyVisuals(to, to, 1f);
        }

        void Update()
        {
            if (hotkeys && Keyboard.current != null)
            {
                int n = System.Enum.GetValues(typeof(WeatherPreset)).Length;
                if (Keyboard.current.f5Key.wasPressedThisFrame) SetPreset((WeatherPreset)(((int)preset + 1) % n));
                if (Keyboard.current.f6Key.wasPressedThisFrame) SetPreset((WeatherPreset)(((int)preset + n - 1) % n));
            }
            if (transition < 1f)
            {
                transition = Mathf.Min(1f, transition + Time.deltaTime / 3f);
                ApplyVisuals(from, to, transition);
            }
            if (viewCamera != null)
            {
                var p = viewCamera.transform.position;
                if (rain != null) rain.transform.position = p + Vector3.up * 12f;
                if (snow != null) snow.transform.position = p + Vector3.up * 10f;
            }
        }

        void ApplyVisuals(WeatherConditions a, WeatherConditions b, float t)
        {
            float rainI = Mathf.Lerp(a.rainIntensity01, b.rainIntensity01, t);
            float snowI = Mathf.Lerp(a.snowIntensity01, b.snowIntensity01, t);
            float fog = Mathf.Lerp(a.fogDensity01, b.fogDensity01, t);
            float day = Mathf.Lerp(Daylight(a.timeOfDayHours), Daylight(b.timeOfDayHours), t);
            float cloud = Mathf.Clamp01(Mathf.Max(rainI * 1.2f, snowI, fog * 0.8f, b.preset == WeatherPreset.Overcast ? 0.6f : 0f));

            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(0.02f, 1.35f, day) * Mathf.Lerp(1f, 0.25f, cloud);
                sun.color = Color.Lerp(new Color(0.55f, 0.62f, 0.85f), new Color(1f, 0.97f, 0.92f), day);
                sun.shadowStrength = Mathf.Lerp(1f, 0.3f, cloud);
                float elev = Mathf.Lerp(-8f, 48f, day);
                sun.transform.rotation = Quaternion.Euler(Mathf.Max(elev, 5f), -35f, 0f);
            }
            Color skyDay = Color.Lerp(new Color(0.58f, 0.72f, 0.88f), new Color(0.55f, 0.58f, 0.62f), cloud);
            Color sky = Color.Lerp(new Color(0.02f, 0.025f, 0.05f), skyDay, day);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * 1.05f;
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.03f, 0.03f, 0.05f), new Color(0.5f, 0.55f, 0.6f), day);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.02f, 0.02f, 0.02f), new Color(0.25f, 0.28f, 0.22f), day);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = Color.Lerp(sky, new Color(0.7f, 0.72f, 0.74f) * Mathf.Lerp(0.08f, 1f, day), fog);
            RenderSettings.fogDensity = 0.0015f + fog * 0.05f + rainI * 0.006f + snowI * 0.012f;
            if (viewCamera != null) { viewCamera.clearFlags = CameraClearFlags.SolidColor; viewCamera.backgroundColor = RenderSettings.fogColor; }

            SetRate(rain, rainI * 9000f); SetRate(snow, snowI * 2500f);
            float wet = Mathf.Clamp01(Mathf.Max(rainI * 1.5f, b.surface == SurfaceType.WetAsphalt ? 0.6f : 0f));
            foreach (var kv in drySmoothness) if (kv.Key != null) kv.Key.SetFloat("_Smoothness", Mathf.Lerp(kv.Value, 0.82f, wet));
        }

        static float Daylight(float hours) => Mathf.Clamp01(Mathf.InverseLerp(5.5f, 8f, hours) * Mathf.InverseLerp(21.5f, 19f, hours));

        static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var e = ps.emission; e.rateOverTime = rate;
            if (rate > 0f && !ps.isPlaying) ps.Play(); else if (rate <= 0f && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        ParticleSystem CreatePrecipitation(string name, bool isSnow)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = isSnow ? 9f : 1.6f;
            main.startSpeed = isSnow ? 1.2f : 11f;
            main.startSize = isSnow ? 0.035f : 0.012f;
            main.startColor = isSnow ? new Color(1f, 1f, 1f, 0.9f) : new Color(0.75f, 0.8f, 0.88f, 0.45f);
            main.maxParticles = isSnow ? 20000 : 30000;
            main.gravityModifier = isSnow ? 0.02f : 0.3f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(40f, 40f, 1f); // local Z becomes vertical after the rotation below
            shape.rotation = new Vector3(90f, 0f, 0f); // emit downwards
            var em = ps.emission; em.rateOverTime = 0f;
            if (isSnow) { var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.3f; }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = isSnow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f; r.lengthScale = 2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            Material m = precipitationMaterial;
            if (m == null) { var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit"); if (sh != null) m = new Material(sh); }
            if (m != null) r.sharedMaterial = m;
            return ps;
        }
    }
}
