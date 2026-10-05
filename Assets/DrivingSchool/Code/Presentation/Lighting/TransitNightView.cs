using System.Collections.Generic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Buses and route minibuses of the transit kit (T75), added by <see cref="TrafficVehicleView"/>: the saloon is lit at
    /// night (side, rear and door glass glow cool-white; the windscreen and the driver's side glass stay dark), the route
    /// displays (amber LED) and the amber side markers glow day and night. Materials are shared by all vehicles.
    /// </summary>
    public sealed class TransitNightView : MonoBehaviour
    {
        static readonly Color SaloonColor = new Color(0.86f, 0.92f, 1f) * 0.65f;
        static readonly Color DisplayColor = new Color(1f, 0.62f, 0.12f) * 2.2f;
        static readonly Color MarkerColor = new Color(1f, 0.55f, 0.12f) * 3f;

        static readonly Dictionary<Material, Material> saloon = new Dictionary<Material, Material>(), display = new Dictionary<Material, Material>(), marker = new Dictionary<Material, Material>();
        static float lastNight = -1f; static int lastFrame = -1;

        /// <summary>True for a model with saloon side glass (transit kit naming, docs/transit-kit.md).</summary>
        public static bool IsTransit(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Glass_Side")) return true;
            return false;
        }

        void Awake()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name; var src = r.sharedMaterial; if (src == null) continue;
                if (n.StartsWith("Glass_Side") || n.StartsWith("Glass_Rear") || (n.StartsWith("Door_") && n.EndsWith("Glass")))
                {
                    if (n.EndsWith("_Seal")) continue;
                    r.sharedMaterial = Get(saloon, src, Color.black);
                }
                else if (n.StartsWith("Display_") && n.EndsWith("_Text") || n.StartsWith("RouteDisplay_") && n.EndsWith("_Text"))
                    r.sharedMaterial = Get(display, src, DisplayColor);
                else if (n.StartsWith("SideMarker"))
                    r.sharedMaterial = Get(marker, src, MarkerColor);
            }
            lastNight = -1f;
        }

        static Material Get(Dictionary<Material, Material> cache, Material src, Color emission)
        {
            if (cache.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(src) { name = src.name + " (night)", hideFlags = HideFlags.DontSave };
            m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", emission);
            return cache[src] = m;
        }

        void Update()
        {
            if (lastFrame == Time.frameCount) return;   // shared materials: once per frame for all vehicles
            lastFrame = Time.frameCount;
            float night = NightLighting.Night01;
            if (Mathf.Abs(night - lastNight) < 0.01f) return;
            lastNight = night;
            foreach (var m in saloon.Values) if (m != null) m.SetColor("_EmissionColor", SaloonColor * night);
        }
    }
}
