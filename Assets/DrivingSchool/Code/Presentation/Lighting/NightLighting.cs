using System.Collections.Generic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Night life of the town (T75), added by <see cref="WeatherController"/>: house windows light up at dusk — each window
    /// lit or dark, warm or cool, from <see cref="NightGlass.HousePattern"/>; every house takes one of
    /// <see cref="Variants"/> shifts of the pattern, so neighbouring houses of the same type differ — and entrances
    /// (под козырьком подъезда) and bus stop shelters get a lamp. The lamps glow everywhere; real lights are kept only for the entrances nearest to
    /// the camera (<see cref="maxEntranceLights"/>), the rest of the town is lit by glowing lenses and windows.
    /// Brightness and shares are game values.
    /// </summary>
    public sealed class NightLighting : MonoBehaviour
    {
        public const int Variants = 8;
        [Tooltip("Emission of a lit window at full night (HDR multiplier of the pattern colour).")]
        public float windowEmission = 1.6f;
        public Color entranceColor = new Color(1f, 0.8f, 0.56f);
        public float entranceEmission = 3.5f, entranceLightIntensity = 4.5f, entranceLightRange = 8f;
        public int maxEntranceLights = 24;
        public float entranceLightDistance = 110f;
        [Tooltip("Street lamps nearest to the camera that cast shadows: without them the wide beams light the car's cabin through its roof.")]
        public int shadowedStreetLamps = 4;
        public float streetShadowDistance = 45f;

        public int HouseRenderers { get; private set; }
        public int Entrances => entrances.Count;

        readonly List<Material> windowMats = new List<Material>();
        readonly List<Light> entrances = new List<Light>();
        Material entranceMat;
        float lastNight = -1f, budgetTimer;
        readonly List<(float d, Light l)> sort = new List<(float, Light)>();
        readonly List<Light> streetLights = new List<Light>();

        void Start()
        {
            foreach (var lamp in FindObjectsByType<StreetLampView>(FindObjectsSortMode.None))
                if (lamp.lampLight != null)
                {
                    lamp.lampLight.shadows = LightShadows.None;
                    lamp.lampLight.shadowNearPlane = 0.3f; lamp.lampLight.shadowBias = 0.05f; lamp.lampLight.shadowNormalBias = 0.4f;
                    streetLights.Add(lamp.lampLight);
                }
            SetupWindows();
            SetupShelters();
            ApplyNight(true);
        }

        /// <summary>0 by day, 1 at night; windows come on through dusk.</summary>
        public static float Night01 => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.55f, WeatherController.Daylight01));

        void SetupWindows()
        {
            var variants = new Dictionary<(Material, int), Material>();
            var houses = new HashSet<Transform>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials; bool changed = false;
                Transform house = null; int v = 0;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !(m.name == "House_Glass" || m.name == "House_GlassLight")) continue;
                    if (house == null)
                    {
                        var lod = r.GetComponentInParent<LODGroup>();
                        house = lod != null ? lod.transform : r.transform;
                        Vector3 p = house.position;
                        v = (int)(NightGlass.Hash((uint)Mathf.RoundToInt(p.x * 10f) * 73856093u ^ (uint)Mathf.RoundToInt(p.z * 10f) * 83492791u) % Variants);
                    }
                    if (!variants.TryGetValue((m, v), out var glow))
                    {
                        // Whole-texel shifts keep UV0 on texel centres (point sampling).
                        var offset = new Vector2((v * 23 % NightGlass.HouseSize) / (float)NightGlass.HouseSize, (v * 41 % NightGlass.HouseSize) / (float)NightGlass.HouseSize);
                        glow = NightGlass.MakeGlowing(m, NightGlass.HousePattern, offset);
                        variants[(m, v)] = glow; windowMats.Add(glow);
                    }
                    mats[i] = glow; changed = true;
                }
                if (changed) { r.sharedMaterials = mats; HouseRenderers++; houses.Add(house); }
            }
            foreach (var h in houses) AddEntrances(h);
        }

        /// <summary>
        /// Entrance lamps under the door canopies of the house kit (tools/build_houses.py): the front faces the root's +Z,
        /// the facade is at z = depth/2 (the body BoxCollider), doors at x = 0 (the townhouse: x = ±3.2), the canopy's
        /// underside at 2.58 m, 0…1.6 m out of the wall.
        /// </summary>
        void AddEntrances(Transform house)
        {
            var box = house.GetComponent<BoxCollider>();
            if (box == null || !house.name.Contains("DS_House_")) return;
            float front = box.center.z + box.size.z / 2f;
            float[] doors = house.name.Contains("Townhouse") ? new[] { -3.2f, 3.2f } : new[] { 0f };
            EnsureEntranceMat();
            foreach (float x in doors)
            {
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lamp.name = "Entrance lamp"; Destroy(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(house, false);
                lamp.transform.localPosition = new Vector3(x, 2.555f, front + 0.5f);
                lamp.transform.localScale = new Vector3(0.34f, 0.05f, 0.22f);
                var mr = lamp.GetComponent<MeshRenderer>(); mr.sharedMaterial = entranceMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var go = new GameObject("Entrance light"); go.transform.SetParent(house, false);
                go.transform.localPosition = new Vector3(x, 2.35f, front + 1.0f);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = entranceLightRange; l.intensity = entranceLightIntensity;
                l.color = entranceColor; l.shadows = LightShadows.None; l.enabled = false;
                entrances.Add(l);
            }
        }

        /// <summary>
        /// Bus stop shelters of the transit kit: a lamp under the roof (the "Roof_Glass" panel) lighting the bench and the
        /// kerb, so a waiting passenger is visible. Same budget as the entrance lamps.
        /// </summary>
        void SetupShelters()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.name != "Roof_Glass") continue;
                var shelter = r.transform.parent != null && r.transform.parent.parent != null ? r.transform.parent.parent : r.transform.parent;
                if (shelter == null || !(shelter.name.Contains("BusStop") || shelter.name.Contains("shelter"))) continue;
                var b = r.bounds;
                EnsureEntranceMat();
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lamp.name = "Shelter lamp"; Destroy(lamp.GetComponent<Collider>());
                lamp.transform.SetParent(shelter, false);
                lamp.transform.position = new Vector3(b.center.x, b.min.y - 0.06f, b.center.z);
                lamp.transform.rotation = shelter.rotation;
                lamp.transform.localScale = new Vector3(1.6f, 0.04f, 0.12f);
                var mr = lamp.GetComponent<MeshRenderer>(); mr.sharedMaterial = entranceMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var go = new GameObject("Shelter light"); go.transform.SetParent(shelter, false);
                go.transform.position = new Vector3(b.center.x, b.min.y - 0.3f, b.center.z);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 9f; l.intensity = 5f;
                l.color = new Color(0.95f, 0.95f, 1f); l.shadows = LightShadows.None; l.enabled = false;
                entrances.Add(l);
            }
        }

        void EnsureEntranceMat()
        {
            if (entranceMat != null) return;
            entranceMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Entrance lamp", hideFlags = HideFlags.DontSave };
            entranceMat.SetColor("_BaseColor", new Color(0.92f, 0.9f, 0.84f));
            entranceMat.EnableKeyword("_EMISSION"); entranceMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        void Update()
        {
            ApplyNight(false);
            budgetTimer -= Time.deltaTime;
            if (budgetTimer <= 0f) { budgetTimer = 0.4f; UpdateBudget(); }
        }

        void ApplyNight(bool force)
        {
            float night = Night01;
            if (!force && Mathf.Abs(night - lastNight) < 0.005f) return;
            lastNight = night;
            foreach (var m in windowMats) m.SetColor("_EmissionColor", Color.white * (windowEmission * night));
            if (entranceMat != null) entranceMat.SetColor("_EmissionColor", entranceColor * (entranceEmission * (night > 0.35f ? 1f : 0f)));
            budgetTimer = 0f;
        }

        /// <summary>Real lights for the nearest entrances only (Forward+ copes with many lights, but not with every door).</summary>
        void UpdateBudget()
        {
            bool on = lastNight > 0.35f;
            var cam = Camera.main;
            if (!on || cam == null) { foreach (var l in entrances) if (l.enabled) l.enabled = false; return; }
            Vector3 c = cam.transform.position; float max2 = entranceLightDistance * entranceLightDistance;
            sort.Clear();
            foreach (var l in entrances) { float d = (l.transform.position - c).sqrMagnitude; if (d < max2) sort.Add((d, l)); else if (l.enabled) l.enabled = false; }
            sort.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < sort.Count; i++) { bool want = i < maxEntranceLights; if (sort[i].l.enabled != want) sort[i].l.enabled = want; }

            // Shadows for the street lamps around the car only (URP shrinks the tiles to fit the 2048² atlas).
            float s2 = streetShadowDistance * streetShadowDistance;
            sort.Clear();
            foreach (var l in streetLights) { if (l == null) continue; float d = (l.transform.position - c).sqrMagnitude; if (d < s2) sort.Add((d, l)); else if (l.shadows != LightShadows.None) l.shadows = LightShadows.None; }
            sort.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < sort.Count; i++) { var want = i < shadowedStreetLamps ? LightShadows.Hard : LightShadows.None; if (sort[i].l.shadows != want) sort[i].l.shadows = want; }
        }

        void OnDestroy()
        {
            foreach (var m in windowMats) if (m != null) Destroy(m);
            if (entranceMat != null) Destroy(entranceMat);
        }
    }
}
