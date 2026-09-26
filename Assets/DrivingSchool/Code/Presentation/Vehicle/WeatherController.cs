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
        /// <summary>
        /// Rain / snow intensity that actually reaches the car glass. Follows the particle emission blend and the time
        /// the drops need to fall from the emitter (≈1 s rain, ≈5.5 s snow), so the windows do not get wet before the
        /// precipitation is visible and stay wet while the last drops are still falling.
        /// </summary>
        public static float GlassRain01 { get; private set; }
        public static float GlassSnow01 { get; private set; }
        /// <summary>0 = night, 1 = full day, blended with the visuals (street lamps, dial illumination, sky).</summary>
        public static float Daylight01 { get; private set; } = 1f;
        public const float RainFallSeconds = 1.0f, SnowFallSeconds = 5.5f, BlendSeconds = 3f;

        public WeatherPreset preset = WeatherPreset.ClearDay;
        public Light sun;
        public Camera viewCamera;
        public Material precipitationMaterial;   // URP Particles/Unlit (assigned by the builder)
        [Tooltip("DrivingSchool/Sky material (assigned by the builder so the shader ships in builds); created at run time when empty.")]
        public Material skyMaterial;
        public List<VehiclePhysicsAdapter> vehicles = new List<VehiclePhysicsAdapter>();
        public List<Renderer> roadRenderers = new List<Renderer>();
        public bool hotkeys = true;

        ParticleSystem rain, snow;
        readonly Dictionary<Material, float> drySmoothness = new Dictionary<Material, float>();
        float transition = 1f; WeatherConditions from, to;
        float changeTime, glassFromRain, glassFromSnow; bool glassSettled = true;

        void Start()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (skyMaterial == null) { var sh = Shader.Find("DrivingSchool/Sky"); if (sh != null) skyMaterial = new Material(sh); }
            else skyMaterial = new Material(skyMaterial); // the asset is not modified in play mode
            if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            foreach (var r in roadRenderers)
                if (r != null) foreach (var m in r.sharedMaterials)
                        if (m != null && m.HasProperty("_Smoothness") && !drySmoothness.ContainsKey(m)) drySmoothness[m] = m.GetFloat("_Smoothness");
            rain = CreatePrecipitation("Rain", false);
            snow = CreatePrecipitation("Snow", true);
            ShieldVehicles(rain); ShieldVehicles(snow);
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
            glassFromRain = GlassRain01; glassFromSnow = GlassSnow01; changeTime = Time.time; glassSettled = instant;
            if (instant) { GlassRain01 = to.rainIntensity01; GlassSnow01 = to.snowIntensity01; }
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
            if (!glassSettled)
            {
                float since = Time.time - changeTime;
                float tr = Mathf.Clamp01((since - RainFallSeconds) / BlendSeconds), ts = Mathf.Clamp01((since - SnowFallSeconds) / BlendSeconds);
                GlassRain01 = Mathf.Lerp(glassFromRain, to.rainIntensity01, tr);
                GlassSnow01 = Mathf.Lerp(glassFromSnow, to.snowIntensity01, ts);
                glassSettled = tr >= 1f && ts >= 1f;
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

            Daylight01 = day;
            // The directional light is the sun by day and the moon by night (weak, cold, higher in the sky).
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(0.035f, 1.35f, day) * Mathf.Lerp(1f, 0.25f, cloud);
                sun.color = Color.Lerp(new Color(0.55f, 0.64f, 0.9f), new Color(1f, 0.97f, 0.92f), day);
                sun.shadowStrength = Mathf.Lerp(1f, 0.3f, cloud) * Mathf.Lerp(0.6f, 1f, day);
                float elev = Mathf.Lerp(32f, 48f, day), yaw = Mathf.Lerp(150f, -35f, day);
                sun.transform.rotation = Quaternion.Euler(elev, yaw, 0f);
            }
            Color skyDay = Color.Lerp(new Color(0.58f, 0.72f, 0.88f), new Color(0.55f, 0.58f, 0.62f), cloud);
            Color sky = Color.Lerp(new Color(0.012f, 0.016f, 0.03f), skyDay, day);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.03f, 0.035f, 0.06f), skyDay * 1.05f, day);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.025f, 0.025f, 0.035f), new Color(0.5f, 0.55f, 0.6f), day);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.015f, 0.015f, 0.015f), new Color(0.25f, 0.28f, 0.22f), day);
            // Car paint and glass reflect the environment: without this they mirror the bright default sky at night.
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.06f, 1f, day);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = Color.Lerp(sky, new Color(0.7f, 0.72f, 0.74f) * Mathf.Lerp(0.05f, 1f, day), fog);
            RenderSettings.fogDensity = 0.0015f + fog * 0.05f + rainI * 0.006f + snowI * 0.012f;
            if (skyMaterial != null)
            {
                Color zenith = Color.Lerp(new Color(0.004f, 0.006f, 0.014f), Color.Lerp(new Color(0.22f, 0.42f, 0.78f), new Color(0.5f, 0.53f, 0.57f), cloud), day);
                skyMaterial.SetColor("_ZenithColor", Color.Lerp(zenith, RenderSettings.fogColor, fog));
                skyMaterial.SetColor("_HorizonColor", RenderSettings.fogColor);
                skyMaterial.SetColor("_GroundColor", RenderSettings.fogColor * 0.6f);
                skyMaterial.SetColor("_GlowColor", new Color(0.05f, 0.03f, 0.015f) * (1f - day) * (1f + cloud));
                float clear = (1f - cloud) * (1f - fog);
                skyMaterial.SetFloat("_Stars", Mathf.Clamp01(1f - day * 1.6f) * clear);
                Vector3 toLight = sun != null ? -sun.transform.forward : new Vector3(0.3f, 0.5f, -0.8f);
                skyMaterial.SetVector("_SunDir", toLight);
                skyMaterial.SetColor("_SunColor", new Color(1f, 0.95f, 0.85f, Mathf.Clamp01(day * 2f - 1f) * clear));
                skyMaterial.SetVector("_MoonDir", toLight);
                skyMaterial.SetFloat("_Moon", Mathf.Clamp01(1f - day * 2f) * clear);
                RenderSettings.skybox = skyMaterial;
                if (viewCamera != null) viewCamera.clearFlags = CameraClearFlags.Skybox;
            }
            else if (viewCamera != null) { viewCamera.clearFlags = CameraClearFlags.SolidColor; viewCamera.backgroundColor = RenderSettings.fogColor; }

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

        /// <summary>Drops and flakes die on the car body instead of falling through the roof into the cabin.</summary>
        void ShieldVehicles(ParticleSystem ps)
        {
            if (ps == null) return;
            var trigger = ps.trigger;
            foreach (var v in vehicles)
            {
                if (v == null) continue;
                foreach (var c in v.GetComponents<Collider>()) { trigger.AddCollider(c); trigger.enabled = true; }
            }
            trigger.enter = ParticleSystemOverlapAction.Kill;
            trigger.inside = ParticleSystemOverlapAction.Kill;
            trigger.outside = ParticleSystemOverlapAction.Ignore;
            trigger.exit = ParticleSystemOverlapAction.Ignore;
            trigger.radiusScale = 1f;
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
