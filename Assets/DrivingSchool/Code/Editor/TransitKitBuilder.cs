using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Imports the public transport kit (PT_*, tools/build_transit.py): bus, route minibus, tram,
    /// tram tracks, stops. Writes only its own materials, prefabs, demo scene and report;
    /// does not touch other scenes or the build scene list.
    /// </summary>
    public static class TransitKitBuilder
    {
        const string Art = "Assets/DrivingSchool/Art/Transit";
        const string Prefabs = "Assets/DrivingSchool/Prefabs/Transit";
        const string Materials = "Assets/DrivingSchool/Materials/Transit";
        const string Demo = "Assets/DrivingSchool/Scenes/Transit_Demo.unity";
        const string Manifest = "artifacts/reports/transit-manifest.json";
        const string Report = "artifacts/reports/transit-unity.json";
        const string Evidence = "artifacts/visual-review/transit";

        static readonly string[] Vehicles = { "PT_Bus_City12", "PT_Minibus_Route", "PT_Tram_City" };
        static readonly string[] Surfaces = { "PT_Road_TramUrban_20m", "PT_TramTrack_Grass_20m", "PT_TramTrack_Grass_Curve90_R25", "PT_TramStop_Platform_30m" };
        const int ExpectedAssets = 11;

        [Serializable] class MaterialEntry { public string name; public float[] color; public float metallic; public float roughness; public float emission; }
        [Serializable] class MaterialList { public MaterialEntry[] materials; }
        [Serializable] class ManifestAsset { public string id; public string kind; public float[] size; public float[] bounds_min; }
        [Serializable] class ManifestFile { public ManifestAsset[] assets; }

        [Serializable] class ImportResult
        {
            public string name;
            public Vector3 boundsMetres;
            public Vector3 expectedMetres;
            public float minY;
            public int triangles;
            public int colliders;
            public string layer;
            public bool pass;
            public string note;
        }
        [Serializable] class ImportReport
        {
            public string unityVersion;
            public string checkedUtc;
            public bool passed;
            public bool trackSeamPass;
            public ImportResult[] assets;
        }

        [MenuItem("Driving School/Transit/Import and build demo")]
        public static void Build()
        {
            // Never close an unsaved user's scene. Batch mode has no user scene to save.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var dir in new[] { Prefabs, Materials, Evidence, "artifacts/reports" }) Directory.CreateDirectory(dir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var manifest = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(Manifest)).assets.ToDictionary(a => a.id);
            CreateMaterials();
            var report = new ImportReport { unityVersion = Application.unityVersion, checkedUtc = DateTime.UtcNow.ToString("O") };
            var results = new List<ImportResult>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                var paths = Directory.GetFiles(Art, "PT_*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                if (paths.Length != ExpectedAssets) throw new InvalidOperationException($"Expected {ExpectedAssets} transit FBX, found {paths.Length}.");
                foreach (var path in paths)
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (!manifest.TryGetValue(name, out var entry)) throw new InvalidOperationException(name + " missing in " + Manifest);
                    Import(path, Surfaces.Contains(name) || !Vehicles.Contains(name));
                    results.Add(BuildPrefab(path, name, entry));
                }
                CreateDemo();
                Physics.SyncTransforms();
                // Road tracks end exactly where the lawn track starts: same rail line for AI trams.
                var roadEnd = Find("Road_B", "Socket_Tram_R_End");
                var grassStart = Find("Grass_A", "Socket_Tram_R_Start");
                report.trackSeamPass = Vector3.Distance(roadEnd.position, grassStart.position) < .001f;
                report.assets = results.ToArray();
                report.passed = report.trackSeamPass && results.All(r => r.pass);
                File.WriteAllText(Report, JsonUtility.ToJson(report, true));
                if (!report.passed) throw new InvalidOperationException("Transit kit validation failed. See " + Report);
                EditorSceneManager.SaveScene(scene, Demo);
                AssetDatabase.SaveAssets();
                Debug.Log("TRANSIT_UNITY_PASS: " + results.Count + " prefabs; axes, metre bounds, ground contact, colliders and track seam verified.");
            }
            catch
            {
                report.assets = results.ToArray();
                File.WriteAllText(Report, JsonUtility.ToJson(report, true));
                throw;
            }
        }

        static void Import(string path, bool isStatic)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.isReadable = false;
            importer.generateSecondaryUV = isStatic;
            importer.SaveAndReimport();
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var target = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/" + source.name + ".mat");
                if (target == null) throw new InvalidOperationException("Missing transit material: " + source.name);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), target);
            }
            importer.SaveAndReimport();
        }

        static ImportResult BuildPrefab(string path, string name, ManifestAsset entry)
        {
            bool vehicle = Vehicles.Contains(name);
            var root = new GameObject(name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
                model.name = "Model";
                var nodes = model.GetComponentsInChildren<Transform>(true);
                Transform Node(string n) => nodes.Single(t => t.name == n || t.name.StartsWith(n + ".", StringComparison.Ordinal));
                Vector3 forward = Node("Socket_Front").position - model.transform.position;
                Vector3 up = Node("Socket_Up").position - model.transform.position;
                if (Mathf.Abs(forward.magnitude - 1) > .001f || Mathf.Abs(up.magnitude - 1) > .001f)
                    throw new InvalidOperationException(name + ": FBX is not in metres (Socket_Front/Up not 1 m from the root).");
                // Blender -Y front must arrive as Unity +Z. Correct the Model child only; the root stays identity.
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.transform.rotation;
                bool facing = Vector3.Dot((Node("Socket_Front").position - root.transform.position).normalized, Vector3.forward) > .999f
                              && Vector3.Dot((Node("Socket_Up").position - root.transform.position).normalized, Vector3.up) > .999f;

                string layerName = vehicle ? null : Surfaces.Contains(name) ? "DriveSurface" : "SolidProp";
                int layer = layerName == null ? -1 : LayerMask.NameToLayer(layerName);
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!vehicle) filter.gameObject.isStatic = true;
                    if (!filter.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                    filter.GetComponent<MeshRenderer>().enabled = false;
                    if (vehicle)
                    {
                        // Moving bodies get a primitive; a concave mesh collider is not allowed on them.
                        var b = filter.sharedMesh.bounds;
                        var box = root.AddComponent<BoxCollider>();
                        box.center = root.transform.InverseTransformPoint(filter.transform.TransformPoint(b.center));
                        box.size = Abs(root.transform.InverseTransformVector(filter.transform.TransformVector(b.size)));
                    }
                    else
                    {
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = false;
                        if (layer >= 0) filter.gameObject.layer = layer;
                    }
                }
                var renderers = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                // Manifest sizes are Blender X/Y/Z; in Unity the Y (length) axis becomes Z.
                var expected = new Vector3(entry.size[0], entry.size[2], entry.size[1]);
                float expectedMinY = entry.bounds_min[2];
                var result = new ImportResult
                {
                    name = name, boundsMetres = bounds.size, expectedMetres = expected, minY = bounds.min.y,
                    triangles = renderers.Select(r => r.GetComponent<MeshFilter>().sharedMesh)
                        .Sum(m => (int)(Enumerable.Range(0, m.subMeshCount).Sum(i => (long)m.GetIndexCount(i)) / 3)),
                    colliders = root.GetComponentsInChildren<Collider>().Length,
                    layer = layerName == null ? "(default)" : layer >= 0 ? layerName : layerName + " not defined in TagManager: default layer used",
                };
                var notes = new List<string>();
                if (!facing) notes.Add("front is not +Z / up is not +Y");
                if (Vector3.Distance(bounds.size, expected) > .01f) notes.Add("bounds differ from the Blender manifest");
                if (Mathf.Abs(bounds.min.y - expectedMinY) > .005f) notes.Add("ground pivot moved");
                if (result.colliders == 0 && !name.StartsWith("PT_Marking_", StringComparison.Ordinal)) notes.Add("no collider");
                if (name.StartsWith("PT_Marking_", StringComparison.Ordinal) && result.colliders != 0) notes.Add("paint must not collide");
                if (vehicle) notes.AddRange(CheckVehicle(name, nodes, root.transform));
                result.pass = notes.Count == 0;
                result.note = string.Join("; ", notes);
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static IEnumerable<string> CheckVehicle(string name, Transform[] nodes, Transform root)
        {
            bool tram = name == "PT_Tram_City";
            var required = tram
                ? new[] { "Bogie_F_Pivot", "Bogie_R_Pivot", "Wheelset_F1", "Wheelset_F2", "Wheelset_R1", "Wheelset_R2", "Pantograph_Pivot", "Socket_DriverEye", "MirrorSurface_L", "MirrorSurface_R" }
                : new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "Socket_DriverEye", "Socket_CentreOfMass", "MirrorSurface_L", "MirrorSurface_R" };
            foreach (var n in required)
                if (!nodes.Any(t => t.name == n)) yield return "missing " + n;
            if (!tram)
            {
                var fl = root.InverseTransformPoint(nodes.First(t => t.name == "Wheel_FL").position);
                // Unity +Z forward, +X right: the front-left wheel is at x < 0, z > 0.
                if (!(fl.x < 0 && fl.z > 0)) yield return "Wheel_FL is not front-left: " + fl;
            }
            foreach (var pivot in nodes.Where(t => t.name.StartsWith("Door_", StringComparison.Ordinal) && t.name.EndsWith("_Pivot", StringComparison.Ordinal)))
                if (root.InverseTransformPoint(pivot.position).x <= 0) yield return pivot.name + " is not on the right side";
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static void CreateMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader unavailable.");
            var list = JsonUtility.FromJson<MaterialList>(File.ReadAllText(Art + "/PT_Materials.json"));
            foreach (var m in list.materials)
            {
                string path = Materials + "/" + m.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(shader) { name = m.name }; AssetDatabase.CreateAsset(mat, path); }
                // Palette colours are linear (Blender); URP material colours are authored in gamma space.
                var color = new Color(m.color[0], m.color[1], m.color[2], m.color[3]).gamma;
                color.a = m.color[3];
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", m.metallic);
                mat.SetFloat("_Smoothness", 1 - m.roughness);
                if (m.emission > 0)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    mat.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * m.emission);
                }
                if (m.color[3] < 1)
                {
                    mat.SetFloat("_Surface", 1);
                    mat.SetFloat("_Blend", 0);
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        static Transform Find(string instance, string socket) =>
            GameObject.Find(instance).GetComponentsInChildren<Transform>().First(t => t.name == socket || t.name.StartsWith(socket + ".", StringComparison.Ordinal));

        static GameObject Place(string asset, string label, Vector3 position, float yaw = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + asset + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = label;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            return go;
        }

        static void CreateDemo()
        {
            // Street with embedded tracks, then a lawn track with a platform and a right-hand curve.
            Place("PT_Road_TramUrban_20m", "Road_A", Vector3.zero);
            Place("PT_Road_TramUrban_20m", "Road_B", new Vector3(0, 0, 20));
            Place("PT_TramTrack_Grass_20m", "Grass_A", new Vector3(0, 0, 40));
            Place("PT_TramTrack_Grass_20m", "Grass_B", new Vector3(0, 0, 60));
            Place("PT_TramTrack_Grass_20m", "Grass_C", new Vector3(0, 0, 80));
            Place("PT_TramTrack_Grass_Curve90_R25", "Grass_Curve", new Vector3(0, 0, 100));
            Place("PT_TramStop_Platform_30m", "Tram_Platform", new Vector3(4.75f, 0, 70));
            Place("PT_Tram_City", "Tram", new Vector3(1.75f, 0, 70));
            // Bus stop on the right-hand sidewalk (top 0.15 m): shelter opens to the road (-X).
            Place("PT_BusStop_Shelter", "Bus_Shelter", new Vector3(8.35f, .15f, 12), -90);
            Place("PT_Sign_5_16_BusStop", "Sign_5_16", new Vector3(7.6f, .15f, 4), 180);
            Place("PT_Marking_1_17_Zigzag_20m", "Zigzag_1_17", new Vector3(7.0f, 0, 2));
            Place("PT_Bus_City12", "Bus", new Vector3(5.25f, 0, 12));
            Place("PT_Minibus_Route", "Minibus", new Vector3(-5.25f, 0, 14), 180);
            var light = new GameObject("Transit Sun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2.2f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.50f, .57f, .65f);
            var cam = new GameObject("Transit Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(-26, 22, -18); cam.transform.LookAt(new Vector3(4, 0, 30));
            cam.fieldOfView = 50; cam.nearClipPlane = .1f; cam.farClipPlane = 400;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .64f, .72f);
            cam.GetUniversalAdditionalCameraData();
        }

        /// <summary>Screenshot of the demo scene (run without -nographics).</summary>
        public static void Capture()
        {
            EditorSceneManager.OpenScene(Demo);
            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Transit demo camera missing.");
            Directory.CreateDirectory(Evidence);
            var rt = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                File.WriteAllBytes(Evidence + "/unity-demo.png", texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture);
            }
            Debug.Log("TRANSIT_UNITY_CAPTURED");
        }
    }
}
