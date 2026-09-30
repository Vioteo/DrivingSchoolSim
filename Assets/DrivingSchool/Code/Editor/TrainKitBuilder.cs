using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// T69: rolling stock kit (RS_*, tools/build_trains.py) — EMU cars, a diesel locomotive section, freight cars and a
    /// catenary mast. Imports the FBX (metres, baked axes), makes URP materials from RS_Materials.json and prefabs
    /// Prefabs/Trains/RS_*.prefab (identity root + child "Model", a box collider over the body for cars).
    /// Consists (<see cref="BuildConsist"/>) are assembled by the scene builders. Touches only its own folders.
    /// </summary>
    public static class TrainKitBuilder
    {
        const string Art = "Assets/DrivingSchool/Art/Trains";
        public const string Prefabs = "Assets/DrivingSchool/Prefabs/Trains";
        const string Materials = "Assets/DrivingSchool/Materials/Trains";
        const string Report = "artifacts/reports/trains-unity.json";

        /// <summary>EMU: head, motor car with the pantograph, trailer, head turned round at the tail.</summary>
        public static readonly string[] ElectricTrain = { "RS_EMU_Head", "RS_EMU_Motor", "RS_EMU_Trailer", "-RS_EMU_Head" };
        /// <summary>Freight: locomotive section and eight mixed cars.</summary>
        public static readonly string[] FreightTrain =
            { "RS_Loco_Diesel", "RS_Wagon_Box", "RS_Wagon_Tank", "RS_Wagon_Tank", "RS_Wagon_Gondola", "RS_Wagon_Hopper", "RS_Wagon_Box", "RS_Wagon_Gondola", "RS_Wagon_Tank" };

        [Serializable] class MaterialEntry { public string name; public float[] color; public float metallic, smoothness, emission; }
        [Serializable] class MaterialList { public MaterialEntry[] materials; }
        [Serializable] class Result { public string name; public Vector3 size; public float pitch; public int triangles; public bool pass; public string note; }
        [Serializable] class ReportFile { public string unity, utc; public bool passed; public Result[] assets; }

        [MenuItem("Driving School/Trains/Import rolling stock")]
        public static void Build()
        {
            foreach (var d in new[] { Prefabs, Materials, "artifacts/reports" }) Directory.CreateDirectory(d);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var mats = CreateMaterials();
            var results = new List<Result>();
            foreach (var path in Directory.GetFiles(Art, "RS_*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal))
            {
                Import(path, mats);
                results.Add(BuildPrefab(path));
            }
            var report = new ReportFile { unity = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"), assets = results.ToArray() };
            report.passed = results.Count >= 9 && results.All(r => r.pass);
            File.WriteAllText(Report, JsonUtility.ToJson(report, true));
            AssetDatabase.SaveAssets();
            if (!report.passed) throw new InvalidOperationException("Rolling stock import failed, see " + Report);
            Debug.Log("TRAINS_UNITY_PASS: " + results.Count + " prefabs");
        }

        static Dictionary<string, Material> CreateMaterials()
        {
            var list = JsonUtility.FromJson<MaterialList>(File.ReadAllText(Art + "/RS_Materials.json"));
            var map = new Dictionary<string, Material>();
            foreach (var e in list.materials)
            {
                string path = Materials + "/" + e.name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                var c = new Color(e.color[0], e.color[1], e.color[2]).gamma;   // Blender values are linear
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Metallic", e.metallic);
                m.SetFloat("_Smoothness", e.smoothness);
                if (e.emission > 0f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    m.SetColor("_EmissionColor", c * e.emission);
                }
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                map[e.name] = m;
            }
            return map;
        }

        static void Import(string path, Dictionary<string, Material> mats)
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
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.isReadable = false;
            importer.SaveAndReimport();
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                string key = source.name.Split('.')[0];
                if (!mats.TryGetValue(key, out var target)) throw new InvalidOperationException("No rolling stock material " + source.name + " in " + path);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), target);
            }
            importer.SaveAndReimport();
        }

        static Result BuildPrefab(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var root = new GameObject(name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
                model.name = "Model";
                // The import puts the model's front coupler at z = 0 with the car along +Z; turned round, the car runs
                // along −Z behind its front, which faces +Z (the direction of travel of a consist).
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
                var b = new Bounds(); bool any = false; int tris = 0;
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf && mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                var res = new Result { name = name, size = b.size, triangles = tris };
                var rear = Find(model.transform, "Socket_Rear");
                if (rear != null)
                {
                    res.pitch = -root.transform.InverseTransformPoint(rear.position).z;
                    // body collider: the car's box without the pantograph, couplers and bogies
                    var col = root.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, 2.75f, -res.pitch / 2f);
                    col.size = new Vector3(3.2f, 3.3f, res.pitch - 0.8f);
                    // front at z = 0, the car behind it (−Z), rails at y = 0
                    res.pass = res.pitch > 10f && b.min.y > -0.05f && b.max.z < 0.2f && b.min.z > -res.pitch - 0.2f && b.size.x < 3.8f;
                    res.note = $"pitch {res.pitch:0.00} m, bounds z {b.min.z:0.00}…{b.max.z:0.00}, y {b.min.y:0.00}…{b.max.y:0.00}";
                }
                else
                {
                    res.pass = Find(model.transform, "Socket_Wire") != null && b.size.y > 6.5f;   // the catenary mast (messenger 6.5 m + top)
                    res.note = "mast, height " + b.size.y.ToString("0.00");
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
                return res;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static Transform Find(Transform t, string prefix) =>
            t.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.StartsWith(prefix, StringComparison.Ordinal));

        /// <summary>Pitch of a car prefab, metres (front coupler face to the next car's), from its Socket_Rear.</summary>
        public static float Pitch(GameObject prefab)
        {
            var rear = Find(prefab.transform, "Socket_Rear");
            if (rear == null) throw new InvalidOperationException(prefab.name + ": no Socket_Rear");
            return -prefab.transform.InverseTransformPoint(rear.position).z;
        }

        /// <summary>
        /// A consist under <paramref name="parent"/>: a kinematic rigidbody whose local +Z is the direction of travel,
        /// the front coupler at z = 0, the cars behind it. "-Name" puts the car turned round (a cab at the tail).
        /// Returns the rigidbody; <paramref name="length"/> — coupler to coupler, metres.
        /// </summary>
        public static Rigidbody BuildConsist(string name, string[] cars, Transform parent, int layer, out float length)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            float z = 0f;
            foreach (var entry in cars)
            {
                bool reversed = entry.StartsWith("-", StringComparison.Ordinal);
                string id = reversed ? entry.Substring(1) : entry;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + id + ".prefab");
                if (prefab == null) throw new InvalidOperationException("No " + id + " prefab — run Driving School/Trains/Import rolling stock");
                float pitch = Pitch(prefab);
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
                if (reversed) car.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, z - pitch), Quaternion.Euler(0f, 180f, 0f));
                else car.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, z), Quaternion.identity);
                z -= pitch;
                foreach (var t in car.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = layer; t.gameObject.isStatic = false; }
            }
            length = -z;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.mass = 80000f * cars.Length / 5f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            return rb;
        }

        /// <summary>
        /// Catenary along a track that runs along world X at <paramref name="railZ"/> (rail top <paramref name="railY"/>):
        /// masts every <paramref name="spacing"/> m on the far side (+Z), none within <paramref name="keepClearX"/> m of
        /// <paramref name="crossX"/> (the road); contact wire 5.3 m and messenger 6.5 m over the rails (tools/build_trains.py WIRE, MESSENGER), as thin strips.
        /// </summary>
        public static void BuildCatenary(Transform parent, float railZ, float railY, float westX, float eastX, float crossX, float keepClearX, float spacing, int layer)
        {
            var mastPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/RS_Catenary_Mast.prefab");
            if (mastPrefab == null) throw new InvalidOperationException("No RS_Catenary_Mast prefab — run Driving School/Trains/Import rolling stock");
            var root = new GameObject("Catenary").transform;
            root.SetParent(parent, true);
            // The mast model's pole stands at model x = −3.1 in Unity (Blender +X); yaw 90 puts it on the +Z side of an X track.
            for (float x = westX + 5f; x <= eastX - 5f; x += spacing)
            {
                if (Mathf.Abs(x - crossX) < keepClearX) continue;
                var m = (GameObject)PrefabUtility.InstantiatePrefab(mastPrefab, root);
                m.transform.SetPositionAndRotation(new Vector3(x, railY, railZ), Quaternion.Euler(0f, 90f, 0f));
                foreach (var t in m.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = layer; t.gameObject.isStatic = true; }
                // no mast inside a bridge pier, a house or a road object: test the pole's box against other colliders (not the ground)
                var pole = m.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.bounds).Aggregate((p, q) => { p.Encapsulate(q); return p; });
                var poleCentre = new Vector3(x, railY + 3f, railZ + 3.1f);
                Physics.SyncTransforms();
                if (Physics.CheckBox(poleCentre, new Vector3(0.6f, 2.5f, 0.6f), Quaternion.identity, ~(1 << 9), QueryTriggerInteraction.Ignore))
                    UnityEngine.Object.DestroyImmediate(m);
                else if (pole.max.z < railZ + 2.5f)
                    throw new InvalidOperationException($"Catenary mast reaches only z {pole.max.z:0.0}: its pole must stand on the +Z side of the track at z {poleCentre.z:0.0}");
            }
            var steel = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/RS_Steel.mat");
            foreach (var (h, r, n) in new[] { (5.3f, 0.012f, "Contact wire"), (6.5f, 0.01f, "Messenger wire") })
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = n; w.transform.SetParent(root, true);
                w.transform.SetPositionAndRotation(new Vector3((westX + eastX) / 2f, railY + h, railZ), Quaternion.identity);
                w.transform.localScale = new Vector3(eastX - westX - 6f, r * 2f, r * 2f);
                UnityEngine.Object.DestroyImmediate(w.GetComponent<Collider>());
                var mr = w.GetComponent<MeshRenderer>(); mr.sharedMaterial = steel; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                w.layer = layer; w.isStatic = true;
            }
        }
    }
}
