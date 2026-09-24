using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Editor
{
    /// <summary>Imports the cone without opening or changing any scene.</summary>
    public static class TrafficConeAssetBuilder
    {
        const string ModelPath = "Assets/DrivingSchool/Art/Props/DS_TrafficCone.fbx";
        const string MaterialFolder = "Assets/DrivingSchool/Materials/Props";
        const string PrefabPath = "Assets/DrivingSchool/Prefabs/Props/DS_TrafficCone.prefab";

        [MenuItem("Driving School/Props/Build traffic cone prefab")]
        public static void Build()
        {
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException(ModelPath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader missing");
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
            {
                var path = MaterialFolder + "/" + source.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = source.name, enableInstancing = true };
                    var color = source.name.Contains("Orange") ? new Color(1, .37f, .07f)
                        : source.name.Contains("White") ? new Color(.945f, .955f, .93f)
                        : new Color(.147f, .164f, .179f);
                    material.SetColor("_BaseColor", color);
                    material.SetFloat("_Smoothness", source.name.Contains("Rubber") ? .18f : .65f);
                    AssetDatabase.CreateAsset(material, path);
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), material);
            }
            importer.SaveAndReimport();
            var root = new GameObject("DS_TrafficCone");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.transform.SetParent(root.transform, false);
                model.name = "Model";
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                if (Mathf.Abs(bounds.size.y - .75f) > .005f || Mathf.Abs(bounds.min.y) > .005f
                    || Mathf.Abs(bounds.size.x - .46f) > .005f)
                    throw new InvalidOperationException("Unexpected cone import scale or axes: " + bounds);
                var foot = root.AddComponent<BoxCollider>();
                foot.center = new Vector3(0, .031f, 0);
                foot.size = new Vector3(.46f, .062f, .46f);
                // An eight-sided convex tapered hull is inexpensive and supports a future Rigidbody.
                var vertices = new Vector3[16];
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4;
                    vertices[i] = new Vector3(Mathf.Cos(angle) * .196f, .062f, Mathf.Sin(angle) * .196f);
                    vertices[i + 8] = new Vector3(Mathf.Cos(angle) * .03f, .75f, Mathf.Sin(angle) * .03f);
                }
                var indices = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) % 8;
                    indices.AddRange(new[] { i, i + 8, next, next, i + 8, next + 8 });
                }
                for (int i = 1; i < 7; i++) indices.AddRange(new[] { 0, i, i + 1, 8, i + 9, i + 8 });
                var hull = new Mesh { name = "TrafficCone_Collision", vertices = vertices, triangles = indices.ToArray() };
                hull.RecalculateNormals();
                var hullPath = "Assets/DrivingSchool/Art/Props/DS_TrafficCone_Collision.asset";
                var savedHull = AssetDatabase.LoadAssetAtPath<Mesh>(hullPath);
                if (savedHull == null) { AssetDatabase.CreateAsset(hull, hullPath); savedHull = hull; }
                else { EditorUtility.CopySerialized(hull, savedHull); UnityEngine.Object.DestroyImmediate(hull); }
                var collision = root.AddComponent<MeshCollider>();
                collision.sharedMesh = savedHull;
                collision.convex = true;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("artifacts/reports");
                File.WriteAllText("artifacts/reports/traffic-cone-unity.txt",
                    "PASS: URP materials; ground pivot; identity root; dimensions " + bounds.size.ToString("F3")
                    + "; box foot + convex eight-sided hull. Static prop; Rigidbody not added.\n");
                Debug.Log("CONE_UNITY_PASS " + PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
