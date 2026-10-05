using System.Collections.Generic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Lights of a consist (T75), added by <see cref="RailwayCrossingView"/> to every train. The consist's local +Z is the
    /// direction of travel and its cars run behind z = 0. At the head: white lamps (buffer lamps and the top searchlight)
    /// glow, the searchlight casts a long narrow beam along the track and a wide low one lights the rails ahead; the red
    /// lamps of the head are dark. At the tail: red lamps glow, white ones are dark; a freight train gets a red tail marker.
    /// EMU saloon windows glow cool-white at night (cab glass stays dark, see <see cref="NightGlass"/>) and light the
    /// ground beside the car. Real lights follow the train's visibility (RailwayCrossingView hides the renderers).
    /// </summary>
    public sealed class TrainLightsView : MonoBehaviour
    {
        public Color headLampEmission = new Color(1f, 0.97f, 0.9f) * 14f;
        public Color tailLampEmission = new Color(1f, 0.05f, 0.02f) * 9f;
        public Color saloonEmission = new Color(1f, 1f, 1f) * 1.05f;
        public float searchlightIntensity = 4000f, searchlightRange = 220f, ditchIntensity = 220f;

        Renderer probe;                                   // a body renderer: visible ⇔ the train is shown
        readonly List<Light> beams = new List<Light>(), saloonLights = new List<Light>();
        readonly List<Material> saloonGlass = new List<Material>();
        float lastNight = -1f;

        static Material darkWhite, darkRed;

        void Awake()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;
            // Cars: the "Model" under each car root; z of the car = its renderers' centre along the consist.
            float minZ = float.MaxValue, maxZ = float.MinValue, frontLampZ = float.MinValue, tailLampZ = float.MaxValue;
            foreach (var r in renderers)
            {
                float z = transform.InverseTransformPoint(r.bounds.center).z;
                minZ = Mathf.Min(minZ, transform.InverseTransformPoint(r.bounds.min).z, transform.InverseTransformPoint(r.bounds.max).z);
                maxZ = Mathf.Max(maxZ, transform.InverseTransformPoint(r.bounds.min).z, transform.InverseTransformPoint(r.bounds.max).z);
                if (r.name.Contains("Lamp_")) { frontLampZ = Mathf.Max(frontLampZ, z); tailLampZ = Mathf.Min(tailLampZ, z); }
                if (probe == null && r.name.Contains("_Paint_")) probe = r;
            }
            if (probe == null) probe = renderers[0];
            bool tailHasLamps = tailLampZ < frontLampZ - 10f;
            var saloonSource = new Dictionary<Material, Material>();
            foreach (var r in renderers)
            {
                float z = transform.InverseTransformPoint(r.bounds.center).z;
                bool front = Mathf.Abs(z - frontLampZ) < 3f, tail = tailHasLamps && Mathf.Abs(z - tailLampZ) < 3f;
                if (r.name.Contains("Lamp_White"))
                {
                    if (front) r.sharedMaterial = Glowing(r.sharedMaterial, headLampEmission);
                    else if (tail) r.sharedMaterial = DarkWhite(r.sharedMaterial);
                }
                else if (r.name.Contains("Lamp_Red"))
                {
                    if (tail) r.sharedMaterial = Glowing(r.sharedMaterial, tailLampEmission);
                    else if (front) r.sharedMaterial = DarkRed(r.sharedMaterial);
                }
                else if (r.name.StartsWith("EMU_") && r.name.EndsWith("_Glass"))
                {
                    var src = r.sharedMaterial;
                    if (!saloonSource.TryGetValue(src, out var m))
                    {
                        m = NightGlass.MakeGlowing(src, NightGlass.TrainPattern, Vector2.zero);
                        saloonSource[src] = m; saloonGlass.Add(m);
                    }
                    r.sharedMaterial = m;
                    // Saloon light on the ground beside the car (the light sits inside the body: the outer walls face away from it).
                    var l = NewLight("Saloon light", LightType.Point, new Vector3(0f, 2.3f, z), Quaternion.identity);
                    l.range = 8f; l.intensity = 5f; l.color = new Color(0.86f, 0.94f, 1f);
                    saloonLights.Add(l);
                }
            }
            if (frontLampZ > float.MinValue)
            {
                float y = 3.6f, z = maxZ + 0.1f;
                var search = NewLight("Searchlight", LightType.Spot, new Vector3(0f, y, z), Quaternion.Euler(2.5f, 0f, 0f));
                search.spotAngle = 16f; search.innerSpotAngle = 6f; search.range = searchlightRange; search.intensity = searchlightIntensity;
                search.color = new Color(1f, 0.97f, 0.92f);
                var ditch = NewLight("Track light", LightType.Spot, new Vector3(0f, 2.2f, z), Quaternion.Euler(12f, 0f, 0f));
                ditch.spotAngle = 75f; ditch.innerSpotAngle = 40f; ditch.range = 45f; ditch.intensity = ditchIntensity;
                ditch.color = new Color(1f, 0.97f, 0.92f);
                beams.Add(search); beams.Add(ditch);
            }
            if (!tailHasLamps)
            {
                // Freight tail: a red marker lamp on the buffer beam of the last wagon.
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Tail marker"; Destroy(marker.GetComponent<Collider>());
                marker.transform.SetParent(transform, false);
                marker.transform.localPosition = new Vector3(-1.1f, 1.3f, minZ - 0.03f);
                marker.transform.localScale = new Vector3(0.22f, 0.22f, 0.06f);
                var mr = marker.GetComponent<MeshRenderer>();
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var mat = new Material(lit) { name = "Tail marker", hideFlags = HideFlags.DontSave };
                mat.SetColor("_BaseColor", new Color(0.6f, 0.02f, 0.02f));
                mr.sharedMaterial = Glowing(mat, tailLampEmission);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        Light NewLight(string name, LightType type, Vector3 localPos, Quaternion localRot)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            var l = go.AddComponent<Light>(); l.type = type; l.shadows = LightShadows.None; l.enabled = false;
            return l;
        }

        static Material Glowing(Material src, Color emission)
        {
            var m = new Material(src) { name = src.name + " (lit)", hideFlags = HideFlags.DontSave };
            m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        static Material DarkWhite(Material src) => darkWhite != null ? darkWhite : darkWhite = Dark(src, new Color(0.32f, 0.33f, 0.34f));
        static Material DarkRed(Material src) => darkRed != null ? darkRed : darkRed = Dark(src, new Color(0.16f, 0.015f, 0.015f));

        /// <summary>A lamp that is off: a glossy dark lens, no glow.</summary>
        static Material Dark(Material src, Color lens)
        {
            var m = new Material(src) { name = src.name + " (off)", hideFlags = HideFlags.DontSave };
            m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", lens);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
            return m;
        }

        void Update()
        {
            bool shown = probe != null && probe.enabled && probe.gameObject.activeInHierarchy;
            float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.6f, WeatherController.Daylight01));
            foreach (var l in beams) if (l != null) l.enabled = shown && night > 0.05f;
            foreach (var l in saloonLights) if (l != null) l.enabled = shown && night > 0.3f;
            if (Mathf.Abs(night - lastNight) > 0.01f)
            {
                lastNight = night;
                foreach (var m in saloonGlass) m.SetColor("_EmissionColor", saloonEmission * night);
                foreach (var l in beams) if (l != null) l.intensity = (l.name == "Searchlight" ? searchlightIntensity : ditchIntensity) * Mathf.Lerp(0.35f, 1f, night);
            }
        }
    }
}
