using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DrivingSchool.Editor
{
    /// <summary>Dedicated, repeatable traffic import. Does not run ProjectBuilder.Prepare.</summary>
    public static class TrafficAssetBuilder
    {
        const string Art = "Assets/DrivingSchool/Art/Traffic";
        const string Prefabs = "Assets/DrivingSchool/Prefabs/Traffic";
        const string Materials = "Assets/DrivingSchool/Materials/Traffic";
        const string ScenePath = "Assets/DrivingSchool/Scenes/TrafficShowroom.unity";
        const string Evidence = "artifacts/visual-review/traffic";

        [MenuItem("Driving School/Traffic/Build prefabs and showroom")]
        public static void Build()
        {
            // Interactive invocation preserves unsaved work through Unity's usual save dialog.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var path in new[] { Prefabs, Materials, Evidence, "artifacts/reports" }) Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            var paths = Directory.GetFiles(Art, "*.fbx").OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (paths.Length < 32) throw new InvalidOperationException("Expected at least 18 signs, 11 supplementary plates and 3 signals.");
            var report = new List<string> { "Traffic v1 — Unity import / prefab / signal checks", DateTime.UtcNow.ToString("O") };
            foreach (var path in paths) Import(path.Replace('\\', '/'));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int signIndex = 0, signalIndex = 0, plaqueIndex = 0;
            foreach (var path in paths)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                bool signal = name.StartsWith("DS_Signal_", StringComparison.Ordinal);
                bool plaque = name.StartsWith("DS_Plaque_", StringComparison.Ordinal);
                var root = new GameObject(name);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/')));
                model.transform.SetParent(root.transform, false);
                model.name = "Model";
                var nodes = model.GetComponentsInChildren<Transform>();
                var front = nodes.Single(t => t.name == "Socket_Front" || t.name.StartsWith("Socket_Front."));
                var upSocket = nodes.Single(t => t.name == "Socket_Up" || t.name.StartsWith("Socket_Up."));
                var forward = (front.position - model.transform.position).normalized;
                var up = (upSocket.position - model.transform.position).normalized;
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.transform.rotation;
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = BoundsOf(renderers);
                Require(bounds.min.y > -.005f && bounds.min.y < .005f, name + " ground pivot");
                Require(plaque ? Mathf.Abs(bounds.size.y - .36f) < .005f : bounds.size.y > 2.7f && bounds.size.y < 3.5f, name + " metre scale");
                Require(Vector3.Dot((front.position - model.transform.position).normalized, Vector3.forward) > .999f, name + " +Z facing");
                foreach (var post in nodes.Where(t => t.name == "Post" || t.name.StartsWith("Post.")))
                {
                    var postBounds = post.GetComponent<Renderer>().bounds;
                    var pole = root.AddComponent<CapsuleCollider>();
                    pole.center = postBounds.center; pole.height = postBounds.size.y;
                    pole.radius = Mathf.Max(postBounds.size.x, postBounds.size.z) * .5f; pole.direction = 1;
                }
                var solid = renderers.Where(r => signal ? r.name.StartsWith("Module_") || r.name.StartsWith("Backplate") : r.name.StartsWith("Plate_Back"));
                // Separate boxes preserve the gap between a sign and its supplementary plate.
                foreach (var solidRenderer in solid)
                {
                    var solidBounds = solidRenderer.bounds;
                    var board = root.AddComponent<BoxCollider>(); board.center = solidBounds.center; board.size = solidBounds.size;
                }
                if (signal)
                {
                    var view = root.AddComponent<TrafficSignalView>();
                    Renderer[] Lamps(string prefix) => renderers.Where(r => r.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                    view.Configure(name.Contains("Pedestrian"), Lamps("Lamp_Red_"), Lamps("Lamp_Amber_"), Lamps("Lamp_Green_"), Lamps("Lamp_Arrow_"));
                    ValidateSignal(view, renderers);
                }
                int triangles = model.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.triangles.Length / 3);
                report.Add($"PASS {name}: bounds={bounds.size:F3} minY={bounds.min.y:F4}; triangles={triangles}; renderers={renderers.Length}; colliders={root.GetComponents<Collider>().Length}; front=+Z; prefab root=identity");
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + name + ".prefab"));
                if (signal) instance.transform.position = new Vector3((signalIndex++ - 1) * 2.3f, 0, -7.5f);
                else if (plaque) { int index = plaqueIndex++; instance.transform.position = new Vector3((index % 6 - 2.5f) * 1.2f, .35f + (index / 6) * .55f, 2); }
                else { int index = signIndex++; instance.transform.position = new Vector3((index % 6 - 2.5f) * 2.25f, 0, -(index / 6) * 2.5f); }
            }
            SetupStage();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            report.Add("PASS: all available aspects, dark lens visibility, pedestrian rejection, arrow on/off, prefab creation. LODs and traffic scheduling not included.");
            File.WriteAllLines("artifacts/reports/traffic-unity-check.txt", report);
            Debug.Log("TRAFFIC_UNITY_PASS: 32 prefabs + TrafficShowroom");
        }

        static void Import(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
            importer.isReadable = true; importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var matPath = Materials + "/" + source.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = source.name };
                    var color = source.HasProperty("_Color") ? source.color : Color.gray;
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_Smoothness", source.name.Contains("Lens") ? .65f : .35f);
                    material.SetFloat("_Metallic", source.name.EndsWith("Steel") ? .75f : .05f);
                    if (source.name.EndsWith("On", StringComparison.Ordinal))
                    {
                        material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2);
                    }
                    AssetDatabase.CreateAsset(material, matPath);
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), material);
            }
            importer.SaveAndReimport();
        }

        static Bounds BoundsOf(IEnumerable<Renderer> source)
        {
            var all = source.ToArray();
            if (all.Length == 0) throw new InvalidOperationException("Missing render geometry");
            var bounds = all[0].bounds;
            foreach (var renderer in all.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Traffic check failed: " + message);
        }

        static void ValidateSignal(TrafficSignalView view, Renderer[] all)
        {
            Require(all.Any(r => r.name.StartsWith("Lamp_Red_")), "red emitters exist");
            Require(all.Any(r => r.name.StartsWith("Lamp_Green_")), "green emitters exist");
            if (!view.IsPedestrian) Require(all.Any(r => r.name.StartsWith("Lamp_Amber_")), "amber emitters exist");
            if (view.name.Contains("VehicleArrow")) Require(view.HasArrow, "arrow emitters exist");
            foreach (TrafficSignalView.Aspect aspect in Enum.GetValues(typeof(TrafficSignalView.Aspect)))
            {
                if (view.IsPedestrian && (aspect == TrafficSignalView.Aspect.Amber || aspect == TrafficSignalView.Aspect.RedAmber))
                {
                    bool rejected = false;
                    try { view.SetAspect(aspect); } catch (ArgumentException) { rejected = true; }
                    Require(rejected, "invalid pedestrian aspect rejected");
                    continue;
                }
                view.SetAspect(aspect);
                foreach (var lamp in all.Where(r => r.name.StartsWith("Lamp_")))
                {
                    bool expected = lamp.name.StartsWith("Lamp_Red_") ? aspect == TrafficSignalView.Aspect.Red || aspect == TrafficSignalView.Aspect.RedAmber
                        : lamp.name.StartsWith("Lamp_Amber_") ? aspect == TrafficSignalView.Aspect.Amber || aspect == TrafficSignalView.Aspect.RedAmber
                        : lamp.name.StartsWith("Lamp_Green_") && aspect == TrafficSignalView.Aspect.Green;
                    Require(lamp.enabled == expected, view.name + " " + aspect + " " + lamp.name);
                }
                Require(all.Where(r => r.name.StartsWith("Lens_")).All(r => r.enabled), "dark lenses retained");
            }
            view.SetAspect(TrafficSignalView.Aspect.Red, true);
            Require(all.Where(r => r.name.StartsWith("Lamp_Arrow_")).All(r => r.enabled), "arrow on");
            view.SetAspect(TrafficSignalView.Aspect.Red);
            Require(all.Where(r => r.name.StartsWith("Lamp_Arrow_")).All(r => !r.enabled), "arrow off");
        }

        static void SetupStage()
        {
            var matPath = Materials + "/Traffic_Stage.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(.1f, .13f, .16f));
                AssetDatabase.CreateAsset(material, matPath);
            }
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Display ground";
            ground.transform.localScale = Vector3.one * 10; ground.GetComponent<Renderer>().sharedMaterial = material;
            var sun = new GameObject("Key light", typeof(Light)).GetComponent<Light>(); sun.type = LightType.Directional;
            sun.intensity = 2; sun.transform.rotation = Quaternion.Euler(45, -35, 0); sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.6f, .65f, .7f);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.transform.position = new Vector3(-10, 16, 23); camera.transform.LookAt(new Vector3(0, 1.4f, -2));
            camera.orthographic = true; camera.orthographicSize = 6.8f; camera.nearClipPlane = .05f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .13f, .16f);
            camera.GetUniversalAdditionalCameraData();
        }

        [MenuItem("Driving School/Traffic/Capture showroom")]
        public static void Capture()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            var camera = Camera.main;
            var target = new RenderTexture(1500, 1000, 24);
            var image = new Texture2D(1500, 1000, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1500, 1000), 0, 0); image.Apply();
                Directory.CreateDirectory(Evidence); File.WriteAllBytes(Evidence + "/unity-overview.png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            }
            Debug.Log("TRAFFIC_CAPTURE_PASS");
        }
    }
}
