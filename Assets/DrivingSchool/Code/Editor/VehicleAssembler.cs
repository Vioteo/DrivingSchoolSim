using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Presentation;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// One assembler for every car model (T48). A catalogue entry (Data/Vehicles/vehicles.json) becomes
    ///  • a player car: physics, visuals, lights, dashboard, windshield rain, screen, mirrors — the stack the sedan got first;
    ///  • a traffic prefab (Prefabs/Vehicles/&lt;id&gt;_Traffic.prefab): kinematic body, collider, wheels, brake and turn lamps.
    /// The model only has to follow the naming contract (docs/vehicle-models.md, VehicleModelContract); nothing here is
    /// specific to one car. Reports: artifacts/reports/vehicle-audit-&lt;id&gt;.md.
    /// </summary>
    public static class VehicleAssembler
    {
        public const string CatalogPath = "Assets/DrivingSchool/Data/Vehicles/vehicles.json";
        const string Base = "Assets/DrivingSchool";
        const string PrefabDir = Base + "/Prefabs/Vehicles";
        const string MaterialDir = Base + "/Materials";
        const string ReportDir = "artifacts/reports";

        /// <summary>Materials shared by the player cars of one scene (made by the scene builder).</summary>
        public sealed class PlayerMaterials { public Material telltale, dialWater, glassWater, mirror; }

        public static VehicleCatalog LoadCatalog()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
            if (text == null) throw new FileNotFoundException(CatalogPath);
            var catalog = JsonUtility.FromJson<VehicleCatalog>(text.text);
            catalog.Validate();
            return catalog;
        }

        [MenuItem("Driving School/Vehicles/Import, audit and build traffic prefabs")]
        public static void BuildAll()
        {
            var catalog = LoadCatalog();
            bool ok = true;
            foreach (var v in catalog.vehicles)
            {
                ImportModel(v.model);
                if (!string.IsNullOrEmpty(v.trafficModel)) ImportModel(v.trafficModel);
                if (v.player) ok &= AuditToFile(v, traffic: false).UsableForPlayer;
                if (v.traffic) { ok &= AuditToFile(v, traffic: true).UsableForTraffic; BuildTrafficPrefab(v); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(ok ? "VEHICLES_ASSEMBLE_PASS" : "VEHICLES_ASSEMBLE_FAIL (see artifacts/reports/vehicle-audit-*.md)");
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        // ------------------------------------------------------------------ import

        /// <summary>
        /// FBX import settings of a car (art-pipeline.md: no manual Inspector settings): readable meshes, no cameras or lights,
        /// every embedded material remapped to a shared URP material in Materials/ by name (Paint_*, Lamp_*, Glass…),
        /// so all cars look alike and VehicleLightsView finds the lamp materials. Prefabs are left alone.
        /// </summary>
        public static void ImportModel(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            bool dirty = importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard || importer.importCameras || importer.importLights || !importer.isReadable;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.importCameras = false; importer.importLights = false; importer.isReadable = true;
            if (dirty) importer.SaveAndReimport();
            var remaps = importer.GetExternalObjectMap();
            bool changed = false;
            foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var id = new AssetImporter.SourceAssetIdentifier(embedded);
                if (remaps.ContainsKey(id) && remaps[id] != null) continue;
                importer.AddRemap(id, SharedMaterial(embedded));
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        static Material SharedMaterial(Material embedded)
        {
            string name = embedded.name, path = MaterialDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            // Same recipe as ProjectBuilder.Import made for the first sedan.
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            var colour = embedded.HasProperty("_Color") ? embedded.color : Color.gray;
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", name.Contains("Paint") ? .65f : .25f);
            if (name.Contains("Aluminium") || name == "Chrome" || name == "Mirror") m.SetFloat("_Metallic", .8f);
            if (name.StartsWith("Glass", StringComparison.Ordinal))
            {
                float alpha = name == "Glass" ? .13f : Mathf.Clamp(colour.a, .3f, .9f);
                m.SetColor("_BaseColor", name == "Glass" ? new Color(.25f, .45f, .5f, alpha) : new Color(colour.r, colour.g, colour.b, alpha));
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            }
            if (name.StartsWith("Lamp_", StringComparison.Ordinal) || name == "Ink") { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", m.GetColor("_BaseColor") * 1.5f); }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ model under a car root

        /// <summary>
        /// Instantiates the model under <paramref name="parent"/>: rotated by the catalogue, centred in X/Z, tyres on the
        /// ground (y = 0 of the root), without colliders (the car root owns the hull).
        /// </summary>
        public static GameObject InstantiateModel(VehicleEntry v, Transform parent, bool traffic)
        {
            string path = traffic ? v.TrafficModelOrFull : v.model;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new FileNotFoundException("Vehicle model not found: " + path + " (" + v.id + ")");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            visual.name = Path.GetFileNameWithoutExtension(path);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(v.modelEulerDeg[0], v.modelEulerDeg[1], v.modelEulerDeg[2]);
            // Scale stays as imported: the legacy FBX root carries ×100 over centimetre meshes (Blender units export).
            var b = VehicleRigUtil.WorldBounds(visual.transform);
            visual.transform.position -= new Vector3(b.center.x - parent.position.x, b.min.y - parent.position.y, b.center.z - parent.position.z);
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            return visual;
        }

        public static VehicleModelContract.Report AuditToFile(VehicleEntry v, bool traffic)
        {
            var root = new GameObject("Audit " + v.id);
            try
            {
                var model = InstantiateModel(v, root.transform, traffic);
                var report = VehicleModelContract.Audit(root.transform, model.transform);
                report.ModelName = v.id + (traffic ? " (трафик)" : "");
                Directory.CreateDirectory(ReportDir);
                File.WriteAllText($"{ReportDir}/vehicle-audit-{v.id}{(traffic ? "-traffic" : "")}.md", report.ToMarkdown());
                bool pass = traffic ? report.UsableForTraffic : report.UsableForPlayer;
                Debug.Log($"VEHICLE_AUDIT {(pass ? "PASS" : "FAIL")} {v.id}{(traffic ? " traffic" : "")} wheelbase {report.WheelbaseM:F2} track {report.TrackM:F2} radius {report.WheelRadiusM:F3}");
                return report;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------ player car

        /// <summary>
        /// The full player stack on any catalogue car. Layers: the car on <paramref name="playerLayer"/>, mirror glass on
        /// <paramref name="mirrorLayer"/>; the suspension rays ignore both and Ignore Raycast.
        /// </summary>
        public static VehicleController BuildPlayer(VehicleEntry v, Transform parent, Camera viewer, PlayerMaterials mats, int playerLayer, int mirrorLayer)
        {
            if (!v.player) throw new ArgumentException(v.id + " is not a player car");
            var player = new GameObject("05 / PLAYER / " + v.id);
            if (parent != null) player.transform.SetParent(parent, false);
            var visual = InstantiateModel(v, player.transform, traffic: false);
            var audit = VehicleModelContract.Audit(player.transform, visual.transform);
            if (!audit.UsableForTraffic) throw new Exception(v.id + ": model does not meet the contract:\n" + audit.ToMarkdown());
            foreach (var t in player.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = playerLayer;
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("MirrorSurface", StringComparison.Ordinal)) t.gameObject.layer = mirrorLayer;

            var b = LocalBounds(player.transform, visual);
            var hull = player.AddComponent<BoxCollider>();
            float clearance = v.hullClearanceM;
            hull.center = new Vector3(0f, (clearance + b.max.y) / 2f, b.center.z);
            hull.size = new Vector3(b.size.x * 0.88f, b.max.y - clearance, b.size.z * 0.98f);

            var body = player.AddComponent<Rigidbody>(); body.mass = v.spec.massKg; body.interpolation = RigidbodyInterpolation.Interpolate;
            var adapter = player.AddComponent<VehiclePhysicsAdapter>();
            adapter.ApplySpec(v.spec);
            adapter.groundMask = ~((1 << playerLayer) | (1 << mirrorLayer) | (1 << 2));
            var controller = player.AddComponent<VehicleController>();
            var visuals = player.AddComponent<VehicleVisuals>(); visuals.adapter = adapter; visuals.model = visual.transform;
            var lights = player.AddComponent<VehicleLightsView>(); lights.adapter = adapter; lights.model = visual.transform;
            var dash = player.AddComponent<DashboardView>(); dash.adapter = adapter; dash.model = visual.transform; dash.templateMaterial = mats.telltale; dash.dialMaterial = mats.dialWater;
            var rain = player.AddComponent<WindshieldRainView>(); rain.adapter = adapter; rain.visuals = visuals; rain.model = visual.transform; rain.templateMaterial = mats.glassWater;
            var screen = player.AddComponent<InfotainmentView>(); screen.adapter = adapter; screen.model = visual.transform;
            var mirrors = player.AddComponent<VehicleMirrorRig>(); mirrors.model = visual.transform; mirrors.viewer = viewer; mirrors.templateMaterial = mats.mirror;
            return controller;
        }

        // ------------------------------------------------------------------ traffic car

        /// <summary>
        /// Prefabs/Vehicles/&lt;id&gt;_Traffic.prefab: root identity with a kinematic Rigidbody, a box hull and
        /// <see cref="TrafficVehicleView"/>; the model below it. Brake and turn lamps get emissive overlays that the view
        /// switches on and off (the lens itself never disappears).
        /// </summary>
        public static GameObject BuildTrafficPrefab(VehicleEntry v)
        {
            Directory.CreateDirectory(PrefabDir);
            var root = new GameObject(v.id + "_Traffic");
            try
            {
                var visual = InstantiateModel(v, root.transform, traffic: true);
                visual.name = "Model";
                var b = LocalBounds(root.transform, visual);
                var hull = root.AddComponent<BoxCollider>();
                hull.center = new Vector3(0f, (v.hullClearanceM + b.max.y) / 2f, b.center.z);
                hull.size = new Vector3(Mathf.Min(b.size.x, v.spec.trackM + 0.35f), b.max.y - v.hullClearanceM, b.size.z);
                var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.mass = v.spec.massKg; body.interpolation = RigidbodyInterpolation.Interpolate;
                var view = root.AddComponent<TrafficVehicleView>();

                var red = LampMaterial("TrafficLamp_Brake", new Color(1f, 0.05f, 0.02f));
                var amber = LampMaterial("TrafficLamp_Turn", new Color(1f, 0.45f, 0.02f));
                var brake = new List<Renderer>(); var left = new List<Renderer>(); var right = new List<Renderer>();
                foreach (var r in visual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool isRed = mats.Any(m => m != null && m.name.StartsWith("Lamp_Red", StringComparison.Ordinal));
                    bool isAmber = mats.Any(m => m != null && m.name.StartsWith("Lamp_Amber", StringComparison.Ordinal));
                    if (!isRed && !isAmber) continue;
                    var cb = VehicleRigUtil.CarSpaceBounds(root.transform, r);
                    bool outer = cb.center.z > b.max.z - 0.8f || cb.center.z < b.min.z + 0.8f || r.name.Contains("Mirror");
                    if (!outer) continue;   // needles and cabin lamps
                    var overlay = Overlay(r, isAmber ? amber : red);
                    if (overlay == null) continue;
                    if (isAmber) (cb.center.x < 0 ? left : right).Add(overlay);
                    else if (cb.center.z < b.center.z) brake.Add(overlay);
                    else UnityEngine.Object.DestroyImmediate(overlay.gameObject);
                }
                var audit = VehicleModelContract.Audit(root.transform, visual.transform);
                // Body width without the mirrors: the track plus the tyres and a little overhang.
                view.Configure(visual.transform, brake.ToArray(), left.ToArray(), right.ToArray(), b.size.z, audit.TrackM + 0.35f, audit.WheelbaseM);
                string path = PrefabDir + "/" + v.id + "_Traffic.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"VEHICLE_TRAFFIC_PREFAB {v.id}: {path}, brake {brake.Count}, left {left.Count}, right {right.Count}");
                return prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static string TrafficPrefabPath(VehicleEntry v) => PrefabDir + "/" + v.id + "_Traffic.prefab";

        static Renderer Overlay(MeshRenderer lamp, Material material)
        {
            var mf = lamp.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return null;
            var go = new GameObject(lamp.name + "_Glow");
            go.transform.SetParent(lamp.transform, false);
            go.transform.localScale = Vector3.one * 1.004f;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Enumerable.Repeat(material, mf.sharedMesh.subMeshCount).ToArray();
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.enabled = false;
            return r;
        }

        static Material LampMaterial(string name, Color colour)
        {
            Directory.CreateDirectory(MaterialDir + "/Vehicles");
            string path = MaterialDir + "/Vehicles/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", colour * 4f);   // HDR: bloom picks it up
            EditorUtility.SetDirty(m);
            return m;
        }

        static Bounds LocalBounds(Transform root, GameObject visual)
        {
            var wb = VehicleRigUtil.WorldBounds(visual.transform);
            return new Bounds(root.InverseTransformPoint(wb.center), wb.size);
        }
    }
}
