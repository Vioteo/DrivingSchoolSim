using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Traffic prefabs of the route vehicles (T74): the transit kit's bus and minibus (Prefabs/Transit, TransitKitBuilder)
    /// with a kinematic body, a <see cref="TrafficVehicleView"/> and lamp overlays — brake, indicators, head and tail
    /// lamps — like the cars of VehicleAssembler.BuildTrafficPrefab. The kit's own lamp lenses glow all the time, so the
    /// brake and indicator lenses get a dark lens material and the overlays light them when needed.
    /// Menu: Driving School → Transit → Build traffic prefabs (also run by the district builder when they are missing).
    /// </summary>
    public static class TransitTrafficBuilder
    {
        const string Dir = "Assets/DrivingSchool/Prefabs/Transit/";
        /// <summary>Order = <see cref="Contracts.TransitKind"/>: Bus, Minibus.</summary>
        public static readonly string[] Sources = { "PT_Bus_City12", "PT_Minibus_Route" };
        // Body sizes of the kit (docs/transit-kit.md): length, width, wheelbase.
        static readonly Dictionary<string, (float l, float w, float wb)> Sizes = new Dictionary<string, (float, float, float)>
        {
            ["PT_Bus_City12"] = (12.0f, 2.55f, 5.95f), ["PT_Minibus_Route"] = (6.70f, 2.06f, 3.745f),
        };

        public static string PathOf(string source) => Dir + source + "_Traffic.prefab";

        [MenuItem("Driving School/Transit/Build traffic prefabs")]
        public static void BuildMenu() => Build();

        /// <summary>Traffic prefabs by kind (Bus, Minibus); a missing kit model gives null in its place (with a warning).</summary>
        public static GameObject[] Ensure()
        {
            var result = Sources.Select(s => AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(s))).ToArray();
            return result.All(x => x != null) ? result : Build();
        }

        public static GameObject[] Build()
        {
            var result = new GameObject[Sources.Length];
            for (int i = 0; i < Sources.Length; i++)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + Sources[i] + ".prefab");
                if (src == null) { Debug.LogWarning("TRANSIT_TRAFFIC: no " + Dir + Sources[i] + ".prefab — run Driving School/Transit/Import and build demo first"); continue; }
                result[i] = BuildOne(src, Sources[i]);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static GameObject BuildOne(GameObject src, string name)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                root.name = name + "_Traffic";
                var model = root.transform.Find("Model");
                if (model == null) throw new InvalidOperationException(name + ": no Model child");
                if (root.GetComponent<Collider>() == null) throw new InvalidOperationException(name + ": no body collider (COL_Body)");
                var body = root.GetComponent<Rigidbody>();
                if (body == null) body = root.AddComponent<Rigidbody>();   // no ?? with UnityEngine.Object
                body.isKinematic = true; body.mass = name.Contains("Bus_City") ? 11000f : 3500f; body.interpolation = RigidbodyInterpolation.Interpolate;
                var view = root.GetComponent<TrafficVehicleView>();
                if (view == null) view = root.AddComponent<TrafficVehicleView>();

                var red = VehicleAssembler.LampMaterial("TrafficLamp_Brake", new Color(1f, 0.05f, 0.02f));
                var amber = VehicleAssembler.LampMaterial("TrafficLamp_Turn", new Color(1f, 0.45f, 0.02f));
                var headMat = VehicleAssembler.LampMaterial("TrafficLamp_Head", new Color(1f, 0.93f, 0.78f));
                var tailMat = VehicleAssembler.LampMaterial("TrafficLamp_Tail", new Color(0.55f, 0.02f, 0.01f));
                var amberOff = LensOff("TrafficLamp_TurnLens", new Color(0.35f, 0.17f, 0.03f));
                var redOff = LensOff("TrafficLamp_BrakeLens", new Color(0.3f, 0.03f, 0.02f));
                var brake = new List<Renderer>(); var left = new List<Renderer>(); var right = new List<Renderer>();
                var head = new List<Renderer>(); var tail = new List<Renderer>();
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string n = r.name;
                    float x = root.transform.InverseTransformPoint(r.bounds.center).x;
                    if (n.StartsWith("Indicator_", StringComparison.Ordinal))
                    {
                        r.sharedMaterials = Enumerable.Repeat(amberOff, r.sharedMaterials.Length).ToArray();
                        var o = VehicleAssembler.Overlay(r, amber); if (o != null) (x < 0 ? left : right).Add(o);
                    }
                    else if (n.StartsWith("Brake_", StringComparison.Ordinal))
                    {
                        r.sharedMaterials = Enumerable.Repeat(redOff, r.sharedMaterials.Length).ToArray();
                        var o = VehicleAssembler.Overlay(r, red); if (o != null) brake.Add(o);
                    }
                    else if (n.StartsWith("Headlight_", StringComparison.Ordinal)) { var o = VehicleAssembler.Overlay(r, headMat, 1.003f); if (o != null) head.Add(o); }
                    else if (n.StartsWith("Taillight_", StringComparison.Ordinal)) { var o = VehicleAssembler.Overlay(r, tailMat, 1.002f); if (o != null) tail.Add(o); }
                }
                if (left.Count == 0 || right.Count == 0 || brake.Count == 0)
                    Debug.LogWarning($"TRANSIT_TRAFFIC {name}: lamps not found by name (left {left.Count}, right {right.Count}, brake {brake.Count})");
                // One headlamp beam, as on the cars (TrafficVehicleView switches it on in the dark near the camera).
                var size = Sizes[name];
                var beamGo = new GameObject("Headlamp beam"); beamGo.transform.SetParent(root.transform, false);
                float beamY = head.Count > 0 ? head.Average(h => root.transform.InverseTransformPoint(h.bounds.center).y) : 0.9f;
                float front = head.Count > 0 ? head.Max(h => root.transform.InverseTransformPoint(h.bounds.center).z) : size.l / 2;
                beamGo.transform.localPosition = new Vector3(0f, beamY, front + 0.1f);
                beamGo.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
                var beam = beamGo.AddComponent<Light>();
                beam.type = LightType.Spot; beam.spotAngle = 100f; beam.innerSpotAngle = 55f; beam.range = 60f; beam.intensity = 320f;
                beam.color = new Color(1f, 0.95f, 0.85f); beam.shadows = LightShadows.None; beam.enabled = false;
                view.Configure(model, brake.ToArray(), left.ToArray(), right.ToArray(), size.l, size.w, size.wb);
                view.ConfigureLights(head.ToArray(), tail.ToArray(), beam);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PathOf(name));
                Debug.Log($"TRANSIT_TRAFFIC {name}: {PathOf(name)}, brake {brake.Count}, left {left.Count}, right {right.Count}, head {head.Count}, tail {tail.Count}");
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Material LensOff(string name, Color colour)
        {
            const string dir = "Assets/DrivingSchool/Materials/Vehicles";
            Directory.CreateDirectory(dir);
            string path = dir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", colour); m.SetFloat("_Smoothness", 0.85f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
