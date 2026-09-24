using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Editor
{
    /// <summary>Imports the independently authored Blender road kit. Does not rebuild existing scenes.</summary>
    public static class RoadKitBuilder
    {
        const string Art = "Assets/DrivingSchool/Art/RoadKit";
        const string Prefabs = "Assets/DrivingSchool/Prefabs/RoadKit";
        const string Materials = "Assets/DrivingSchool/Materials/RoadKit";
        const string Report = "artifacts/reports/road-kit-unity.json";
        const string Demo = "Assets/DrivingSchool/Scenes/RoadKit_Demo.unity";

        [Serializable] class ImportResult
        {
            public string name;
            public Vector3 boundsMetres;
            public int renderTriangles;
            public int renderers;
            public int colliders;
            public bool axisAndScalePass;
        }
        [Serializable] class ImportReport
        {
            public string unityVersion;
            public string checkedUtc;
            public bool passed;
            public bool surfaceContactPass;
            public bool socketSeamPass;
            public ImportResult[] modules;
        }

        [MenuItem("Driving School/Road Kit/Import and build demo")]
        public static void Build()
        {
            // Never close an unsaved user's scene. Batch mode has no user scene to save.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Prefabs);
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory("artifacts/reports");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreateMaterials();
            var report = new ImportReport { unityVersion = Application.unityVersion, checkedUtc = DateTime.UtcNow.ToString("O") };
            var checks = new List<ImportResult>();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                foreach (string path in Directory.GetFiles(Art, "*.fbx").OrderBy(p => p))
                {
                    string assetPath = path.Replace('\\', '/');
                    string name = Path.GetFileNameWithoutExtension(path);
                    var importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
                    importer.globalScale = 1;
                    importer.useFileScale = true;
                    importer.importCameras = false;
                    importer.importLights = false;
                    importer.importAnimation = false;
                    importer.importNormals = ModelImporterNormals.Import;
                    importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                    foreach (var material in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>())
                    {
                        var target = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/" + material.name + ".mat");
                        if (target == null) throw new InvalidOperationException("Missing road material: " + material.name);
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(material), target);
                    }
                    importer.SaveAndReimport();
                    var wrapper = new GameObject(name);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath), wrapper.transform);
                    model.name = "Geometry";
                    var axes = model.GetComponentsInChildren<Transform>();
                    Vector3 forward = axes.First(t => t.name.StartsWith("Axis_Forward", StringComparison.Ordinal)).position - model.transform.position;
                    Vector3 up = axes.First(t => t.name.StartsWith("Axis_Up", StringComparison.Ordinal)).position - model.transform.position;
                    if (Mathf.Abs(forward.magnitude - 1) > .001f || Mathf.Abs(up.magnitude - 1) > .001f)
                        throw new InvalidOperationException(name + ": FBX metre scale incorrect.");
                    model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.transform.rotation;
                    foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                    {
                        filter.gameObject.isStatic = true;
                        if (!filter.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                        filter.GetComponent<MeshRenderer>().enabled = false;
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = false; // Environment is static; paint never creates collider ridges.
                    }
                    var renderers = wrapper.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
                    Bounds bounds = renderers[0].bounds;
                    foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                    bool aligned = Vector3.Distance(axes.First(t => t.name.StartsWith("Axis_Forward", StringComparison.Ordinal)).position, Vector3.forward) < .001f
                        && Vector3.Distance(axes.First(t => t.name.StartsWith("Axis_Up", StringComparison.Ordinal)).position, Vector3.up) < .001f;
                    if (!aligned) throw new InvalidOperationException(name + ": orientation check failed.");
                    if (name == "RK_Road_Urban_20m" && Vector3.Distance(bounds.size, new Vector3(12.4f, .37f, 20)) > .003f)
                        throw new InvalidOperationException("Urban module dimensions incorrect: " + bounds.size);
                    var result = new ImportResult { name = name, boundsMetres = bounds.size,
                        renderTriangles = renderers.Sum(r => (int)r.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3),
                        renderers = renderers.Length, colliders = wrapper.GetComponentsInChildren<Collider>().Length, axisAndScalePass = aligned };
                    if (name.StartsWith("RK_Marking_", StringComparison.Ordinal) && result.colliders != 0)
                        throw new InvalidOperationException("Paint must not have physics colliders.");
                    PrefabUtility.SaveAsPrefabAsset(wrapper, Prefabs + "/" + name + ".prefab");
                    checks.Add(result);
                    UnityEngine.Object.DestroyImmediate(wrapper);
                }
                if (checks.Count != 16) throw new InvalidOperationException("Expected 16 road modules, found " + checks.Count);
                CreateDemo();
                Physics.SyncTransforms();
                // Real imported triangle meshes must support road, sidewalk and gravel contact.
                bool road = Contact(new Vector3(1.7f, 2, -20), 0);
                bool sidewalk = Contact(new Vector3(5.2f, 2, -20), .15f);
                bool shoulder = Contact(new Vector3(22, 2, -4.75f), -.03f);
                report.surfaceContactPass = road && sidewalk && shoulder;
                // End of first urban segment exactly meets the south junction socket.
                var south = GameObject.Find("Urban_South").GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("Socket_End", StringComparison.Ordinal));
                var centre = GameObject.Find("Intersection").GetComponentsInChildren<Transform>().First(t => t.name.StartsWith("Socket_South", StringComparison.Ordinal));
                report.socketSeamPass = Vector3.Distance(south.position, centre.position) < .001f;
                report.modules = checks.ToArray();
                report.passed = report.surfaceContactPass && report.socketSeamPass;
                File.WriteAllText(Report, JsonUtility.ToJson(report, true));
                if (!report.passed) throw new InvalidOperationException("Road kit contact/seam validation failed. See " + Report);
                EditorSceneManager.SaveScene(scene, Demo);
                AssetDatabase.SaveAssets();
                Debug.Log("ROAD_KIT_UNITY_PASS: " + checks.Count + " prefabs; imported axes, dimensions, static contacts and seam verified.");
            }
            catch
            {
                report.modules = checks.ToArray();
                File.WriteAllText(Report, JsonUtility.ToJson(report, true));
                throw;
            }
        }

        static bool Contact(Vector3 origin, float expectedY)
        {
            bool ok = Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5) && Mathf.Abs(hit.point.y - expectedY) < .003f;
            if (!ok) Debug.LogError("Road kit contact failed at " + origin + ": " + hit.point);
            return ok;
        }

        static void CreateMaterials()
        {
            var names = new[] { "Asphalt", "Gravel", "Paving", "Concrete", "White", "Yellow" };
            var colors = new[] { Color.white, Color.white, Color.white, new Color(.63f,.65f,.62f), new Color(.87f,.88f,.83f), new Color(.95f,.61f,.035f) };
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader unavailable.");
            for (int i = 0; i < names.Length; i++)
            {
                string name = "RK_" + names[i];
                string path = Materials + "/" + name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
                mat.SetColor("_BaseColor", colors[i]); mat.SetFloat("_Smoothness", .12f);
                string texturePath = Art + "/Textures/" + name + ".png";
                if (File.Exists(texturePath))
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    importer.wrapMode = TextureWrapMode.Repeat; importer.anisoLevel = 8;
                    importer.mipmapEnabled = true; importer.sRGBTexture = true; importer.SaveAndReimport();
                    mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
                }
                EditorUtility.SetDirty(mat);
            }
        }

        static GameObject Place(string module, string label, Vector3 position, float yaw = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + module + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = label; go.transform.SetPositionAndRotation(position, Quaternion.Euler(0,yaw,0));
            return go;
        }

        static void CreateDemo()
        {
            Place("RK_Road_Cross_24m", "Intersection", Vector3.zero);
            Place("RK_Road_Urban_20m", "Urban_South", new Vector3(0,0,-32));
            Place("RK_Road_Urban_20m", "Urban_North", new Vector3(0,0,12));
            Place("RK_Road_Urban_20m", "Urban_West", new Vector3(-32,0,0),90);
            Place("RK_Road_Rural_20m", "Rural_East", new Vector3(12,0,0),90);
            Place("RK_Road_Curve90_R14", "Bend_North", new Vector3(0,0,32));
            Place("RK_Road_Urban_20m", "Urban_AfterBend", new Vector3(14,0,46),90);
            Place("RK_Marking_Arrow_Straight", "ApproachArrow", new Vector3(1.8f,0,-18));
            Place("RK_Marking_Arrow_Left", "TurnArrow", new Vector3(-1.8f,0,18),180);
            var light = new GameObject("RoadKit Sun", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2.3f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48,-35,0);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.50f,.57f,.65f);
            var cam = new GameObject("RoadKit Camera", typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            cam.tag="MainCamera";cam.transform.position = new Vector3(-42,48,-58); cam.transform.LookAt(new Vector3(3,0,12));
            cam.fieldOfView=50;cam.nearClipPlane=.1f;cam.farClipPlane=300;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.13f,.18f,.21f);
            cam.GetUniversalAdditionalCameraData();
        }

        public static void Capture()
        {
            EditorSceneManager.OpenScene(Demo);
            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Road kit camera missing.");
            var rt = new RenderTexture(960,720,24);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                var texture=new Texture2D(960,720,TextureFormat.RGB24,false);
                texture.ReadPixels(new Rect(0,0,960,720),0,0);texture.Apply();
                File.WriteAllBytes("artifacts/visual-review/road-kit/unity-demo.png",texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            finally { camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt); }
            Debug.Log("ROAD_KIT_UNITY_CAPTURED");
        }
    }
}
