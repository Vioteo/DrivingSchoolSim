using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DrivingSchool.Editor
{
    /// <summary>Imports only the original house kit; does not rebuild existing scenes.</summary>
    public static class HouseKitBuilder
    {
        const string Root = "Assets/DrivingSchool";
        static readonly string[] Names = { "DS_House_Cottage", "DS_House_Townhouse", "DS_House_Brick5", "DS_House_Modern8" };
        static readonly Vector3[] Sizes = { new Vector3(9,6,8), new Vector3(16,6,9), new Vector3(21,15,12), new Vector3(19,24,13) };
        [Serializable] sealed class ExportManifest { public ExportAsset[] assets; }
        [Serializable] sealed class ExportAsset { public string id; public ExportLod[] lods; }
        [Serializable] sealed class ExportLod { public ExportBounds bounds; }
        [Serializable] sealed class ExportBounds { public float[] min; public float[] max; }

        [MenuItem("Driving School/Houses/Build prefabs and gallery")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Root + "/Prefabs/Houses");
            Directory.CreateDirectory(Root + "/Materials/Houses");
            Directory.CreateDirectory("artifacts/reports/houses");
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("House kit requires URP/Lit.");
            var report = new StringBuilder("House kit Unity import validation\n");
            var manifest = JsonUtility.FromJson<ExportManifest>(File.ReadAllText("artifacts/reports/houses/manifest.json"));
            var prefabs = new GameObject[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                string path = Root + "/Art/Houses/" + Names[i] + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new FileNotFoundException(path);
                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.bakeAxisConversion = true;
                importer.generateSecondaryUV = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                {
                    string matPath = Root + "/Materials/Houses/" + source.name + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null)
                    {
                        mat = new Material(shader) { name = source.name };
                        mat.SetColor("_BaseColor", source.HasProperty("_Color") ? source.color : Color.gray);
                        mat.SetFloat("_Smoothness", source.name.Contains("Glass") ? .65f : .2f);
                        mat.SetFloat("_Metallic", source.name.Contains("Glass") || source.name.Contains("Metal") ? .25f : 0);
                        AssetDatabase.CreateAsset(mat, matPath);
                    }
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), mat);
                }
                importer.SaveAndReimport();
                var root = new GameObject(Names[i]);
                try
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
                    // Reimport measured Blender -Y front as Unity -Z. Turn visual to +Z,
                    // retaining an identity prefab root and the FBX's unit/axis conversion.
                    visual.transform.localRotation = Quaternion.Euler(0, 180, 0) * visual.transform.localRotation;
                    foreach (var group in visual.GetComponentsInChildren<LODGroup>()) UnityEngine.Object.DestroyImmediate(group);
                    var lods = new LOD[3];
                    var bounds = new Bounds();
                    int previousCount = int.MaxValue;
                    for (int level = 0; level < 3; level++)
                    {
                        var renderers = visual.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.EndsWith("_LOD" + level, StringComparison.Ordinal)).ToArray();
                        if (renderers.Length != 1) throw new InvalidDataException(Names[i] + " missing LOD " + level);
                        var mesh = renderers[0].GetComponent<MeshFilter>().sharedMesh;
                        int triangles = 0;
                        for (int s = 0; s < mesh.subMeshCount; s++) triangles += (int)mesh.GetIndexCount(s) / 3;
                        if (triangles >= previousCount) throw new InvalidDataException("LOD reduction failed");
                        previousCount = triangles;
                        if (renderers[0].sharedMaterials.Any(m => m == null || m.shader != shader)) throw new InvalidDataException("Missing URP material");
                        lods[level] = new LOD(new[] { .35f, .14f, .025f }[level], renderers);
                        if (level == 0) bounds = renderers[0].bounds;
                        report.AppendLine(Names[i] + " LOD" + level + ": " + triangles + " triangles, " + mesh.subMeshCount + " materials");
                    }
                    var sourceBounds = manifest.assets.Single(a => a.id == Names[i]).lods[0].bounds;
                    var expectedMin = new Vector3(-sourceBounds.max[0], sourceBounds.min[2], -sourceBounds.max[1]);
                    var expectedMax = new Vector3(-sourceBounds.min[0], sourceBounds.max[2], -sourceBounds.min[1]);
                    if (Vector3.Distance(bounds.min, expectedMin) > .005f || Vector3.Distance(bounds.max, expectedMax) > .005f)
                        throw new InvalidDataException("Unexpected imported metres / axis / ground pivot: " + bounds);
                    var lodGroup = root.AddComponent<LODGroup>();
                    lodGroup.SetLODs(lods); lodGroup.RecalculateBounds();
                    var collider = root.AddComponent<BoxCollider>();
                    collider.center = Vector3.up * Sizes[i].y / 2;
                    collider.size = Sizes[i];
                    report.AppendLine("Bounds: " + bounds + "; identity root; ground pivot; body BoxCollider PASS");
                    prefabs[i] = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/Houses/" + Names[i] + ".prefab");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            try
            {
                var positions = new[] { new Vector3(-25,0,10), new Vector3(-7,0,10), new Vector3(18,0,10), new Vector3(43,0,10) };
                for (int i = 0; i < prefabs.Length; i++)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                    instance.transform.position = positions[i];
                    GameObjectUtility.SetStaticEditorFlags(instance, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                }
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Gallery Ground";
                ground.transform.position = new Vector3(10,-.16f,5); ground.transform.localScale = new Vector3(110,.3f,65);
                ground.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Houses/House_Stone.mat");
                var light = new GameObject("Sun", typeof(Light)).GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.1f;
                light.transform.rotation = Quaternion.Euler(45,-35,0); light.shadows = LightShadows.Soft;
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f,.60f,.66f);
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>(); camera.tag = "MainCamera";
                camera.transform.position = new Vector3(72,47,84); camera.transform.LookAt(new Vector3(9,9,10)); camera.farClipPlane = 500; camera.fieldOfView = 43;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.24f,.30f,.35f); camera.GetUniversalAdditionalCameraData();
                EditorSceneManager.SaveScene(scene, Root + "/Scenes/Houses.unity");
                // Explicit LOD0 render documents actual imported geometry and URP materials.
                foreach (var group in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<LODGroup>())) group.ForceLOD(0);
                var rt = new RenderTexture(1440, 900, 24); camera.targetTexture = rt;
                var previousRT = RenderTexture.active;
                var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
                try
                {
                    camera.Render(); RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0,0,1440,900),0,0); image.Apply();
                    File.WriteAllBytes("artifacts/visual-review/houses/unity-gallery.png", image.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null; RenderTexture.active = previousRT;
                    UnityEngine.Object.DestroyImmediate(image); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                    foreach (var group in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<LODGroup>())) group.ForceLOD(-1);
                }
                report.AppendLine("PASS: 4 prefabs, 3 LODs each, URP materials, imported metre bounds, main-volume colliders, independent Houses scene.");
                report.AppendLine("Scope: exterior scenery only; no interiors; roof, awnings and balcony collision not modelled; runtime performance not measured.");
                File.WriteAllText("artifacts/reports/houses/unity-import.txt", report.ToString());
                AssetDatabase.SaveAssets(); Debug.Log("HOUSES_UNITY_PASS");
            }
            finally
            {
                if (!Application.isBatchMode && previousScene.IsValid()) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
            }
        }
    }
}
