using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using DrivingSchool.Learning;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Editor
{
    /// <summary>Additive, reproducible scene assembly; never invokes ProjectBuilder.Prepare.</summary>
    public static partial class TrainingGroundBuilder
    {
        const string Base = "Assets/DrivingSchool";
        public const string ScenePath = Base + "/Scenes/Autodrome_Training.unity";
        const string Kit = Base + "/Prefabs/TrainingKit/";
        const string Data = Base + "/Data/Training/course-v2.json";
        static Material asphalt, zoneAsphalt, white, yellow, grass, concrete, teal, leafMat, trunkMat, gravelMat;
        static Transform environment, markings, props;
        static readonly List<string> sources = new List<string>();

        [MenuItem("Driving School/Training Ground/Build scene and models")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var p in new[] { Kit, Base + "/Materials/TrainingKit", Base + "/Data/Training", "artifacts/reports", "artifacts/visual-review/training-ground" })
                Directory.CreateDirectory(p);
            
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            sources.Clear(); ImportKit();
            
            var asphaltTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Base + "/Art/RoadKit/Textures/RK_Asphalt.png");
            var pavingTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Base + "/Art/RoadKit/Textures/RK_Paving.png");
            var gravelTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Base + "/Art/RoadKit/Textures/RK_Gravel.png");

            // Natural authentic colors and textures: slate dark asphalt, crisp concrete, rich lawn grass, bright white & safety yellow paint
            asphalt = Mat("Ground", new Color(.36f, .37f, .38f), 0.12f, asphaltTex, Vector2.one);
            zoneAsphalt = Mat("PracticeSurface", new Color(1f, 1f, .98f), 0.14f, asphaltTex, Vector2.one);
            white = Mat("Paint", new Color(.96f, .96f, .94f), 0.25f);
            yellow = Mat("SafetyYellow", new Color(1.0f, .75f, .10f), 0.25f);
            grass = Mat("Landscape", new Color(.22f, .42f, .18f), 0.08f);
            concrete = Mat("Paving", new Color(.75f, .76f, .74f), 0.20f, pavingTex, new Vector2(8, 8));
            gravelMat = Mat("Ballast", new Color(.45f, .44f, .42f), 0.10f, gravelTex, new Vector2(6, 6));
            teal = Mat("Identity", new Color(.035f, .42f, .46f), 0.25f);

            leafMat = AssetDatabase.LoadAssetAtPath<Material>(Base + "/Materials/Leaf.mat");
            trunkMat = AssetDatabase.LoadAssetAtPath<Material>(Base + "/Materials/Trunk.mat");
            if (!leafMat) leafMat = Mat("Leaf", new Color(0.18f, 0.38f, 0.15f), 0.15f);
            if (!trunkMat) trunkMat = Mat("Trunk", new Color(0.35f, 0.22f, 0.14f), 0.10f);
            
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            environment = new GameObject("01 / TERRAIN AND SURFACES").transform;
            markings = new GameObject("02 / MARKINGS AND LESSON ZONES").transform;
            props = new GameObject("03 / MODELS AND FACILITIES").transform;
            
            BuildConnectedLayout();
            
            var course = CreateCourse(); 
            CourseSession.Validate(course);
            File.WriteAllText(Data, JsonUtility.ToJson(course, true));
            AssetDatabase.ImportAsset(Data, ImportAssetOptions.ForceSynchronousImport);
            
            for (int i = 0; i < course.lessons.Length; i++)
            {
                var l = course.lessons[i];
                var root = new GameObject((i + 1).ToString("00") + " / " + l.id + " / " + l.title);
                root.transform.SetParent(markings);
                var socket = new GameObject("START / " + l.id);
                socket.transform.SetParent(root.transform);
                socket.transform.SetPositionAndRotation(new Vector3(l.startX, .05f, l.startZ), Quaternion.Euler(0, l.startYaw, 0));
                
                foreach (var g in l.gates)
                {
                    var go = new GameObject(g.instruction);
                    go.transform.SetParent(root.transform);
                    go.transform.SetPositionAndRotation(new Vector3(g.x, .1f, g.z), Quaternion.Euler(0, g.yaw, 0));
                }
                AddLessonBoard(i, l);
                foreach (var g in l.gates.Where(g => g.holdSeconds > 0))
                {
                    float y = Height(g.x, g.z) + .025f;
                    var r = Outline(g.x, g.z, g.width, g.length, yellow, y);
                    r.rotation = Quaternion.Euler(0, g.yaw, 0);
                }
            }
            
            CreatePlayer(course); 
            Lighting();
            
            Physics.SyncTransforms(); 
            ValidateScene(course);
            
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            AssetDatabase.SaveAssets();
            
            File.WriteAllText("artifacts/reports/training-ground-build.json", JsonUtility.ToJson(new BuildReport
            {
                utc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion,
                scene = ScenePath,
                lessonCount = course.lessons.Length,
                modelSources = sources.Distinct().ToArray(),
                renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length,
                colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length,
                staticChecksPassed = true
            }, true));
            
            Debug.Log("TRAINING_GROUND_BUILD_PASS");
        }

        [Serializable] class BuildReport { public string utc, unity, scene; public int lessonCount, renderers, colliders; public string[] modelSources; public bool staticChecksPassed; }

        static Material Mat(string name, Color color, float smoothness = 0.2f, Texture2D tex = null, Vector2? tiling = null, float metallic = 0f)
        {
            string path = Base + "/Materials/TrainingKit/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                if (tiling.HasValue) m.SetTextureScale("_BaseMap", tiling.Value);
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // Colours of the railway kit (RW_*) come from the Blender generator, not from the FBX,
        // whose embedded colours were imported as white for several materials.
        [Serializable] class KitMaterialSpec { public string name; public float[] color; public float metallic, smoothness, emission; }
        [Serializable] class KitMaterialList { public KitMaterialSpec[] materials; }

        static Dictionary<string, KitMaterialSpec> LoadKitMaterialSpecs()
        {
            var specs = new Dictionary<string, KitMaterialSpec>();
            string file = Base + "/Art/TrainingKit/RW_Materials.json";
            if (!File.Exists(file)) return specs;
            var list = JsonUtility.FromJson<KitMaterialList>(File.ReadAllText(file));
            if (list?.materials != null) foreach (var s in list.materials) specs[s.name] = s;
            return specs;
        }

        static Material KitMat(KitMaterialSpec s)
        {
            var color = new Color(s.color[0], s.color[1], s.color[2]).gamma;   // Blender values are linear
            var m = Mat(s.name, color, s.smoothness, null, null, s.metallic);
            if (s.emission > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", color * s.emission);
            }
            else m.DisableKeyword("_EMISSION");
            return m;
        }

        static void ImportKit()
        {
            var specs = LoadKitMaterialSpecs();
            foreach (var raw in Directory.GetFiles(Base + "/Art/TrainingKit", "*.fbx"))
            {
                string path = raw.Replace('\\', '/');
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.globalScale = 1; importer.useFileScale = true; importer.importAnimation = false; importer.importLights = false; importer.importCameras = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; importer.SaveAndReimport();
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(m),
                        specs.TryGetValue(m.name, out var spec) ? KitMat(spec) : Mat(m.name, m.HasProperty("_Color") ? m.color : Color.gray));
                importer.SaveAndReimport();
                var wrapper = new GameObject(Path.GetFileNameWithoutExtension(path));
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), wrapper.transform);
                var ts = model.GetComponentsInChildren<Transform>();
                var f = ts.First(t => t.name.StartsWith("Axis_Forward", StringComparison.Ordinal));
                var u = ts.First(t => t.name.StartsWith("Axis_Up", StringComparison.Ordinal));
                if (Mathf.Abs(f.position.magnitude - 1) > .001f || Mathf.Abs(u.position.magnitude - 1) > .001f) throw new Exception("Kit scale mismatch");
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(f.position, u.position)) * model.transform.rotation;
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                {
                    mf.gameObject.isStatic = true;
                    if (mf.name.StartsWith("DriveSurface")) { mf.gameObject.layer = 9; mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; }
                    // Detail_* (rails, sleepers, ballast, deck seams) is decorative and never blocks the car.
                    else if (!mf.name.Contains("Paint") && !mf.name.Contains("Reflector") && !mf.name.StartsWith("Detail", StringComparison.Ordinal))
                    { mf.gameObject.layer = 10; var c = mf.gameObject.AddComponent<BoxCollider>(); c.center = mf.sharedMesh.bounds.center; c.size = mf.sharedMesh.bounds.size; }
                }
                PrefabUtility.SaveAsPrefabAsset(wrapper, Kit + wrapper.name + ".prefab"); UnityEngine.Object.DestroyImmediate(wrapper);
            }
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Transform parent, int layer = 0)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cube); o.name = name; o.transform.SetParent(parent); o.transform.position = pos; o.transform.localScale = size;
            o.GetComponent<Renderer>().sharedMaterial = mat; o.layer = layer; o.isStatic = true;
            if (layer == 0) UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>()); return o;
        }

        static GameObject Place(string path, Vector3 pos, float yaw = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!prefab) throw new FileNotFoundException(path);
            var o = (GameObject)PrefabUtility.InstantiatePrefab(prefab, props); o.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); sources.Add(path); return o;
        }

        static GameObject Existing(string name, Vector3 pos, float yaw = 0) => Place(Base + "/Prefabs/" + name + ".prefab", pos, yaw);

        static void Cone(float x, float z)
        {
            var c = Existing("Props/DS_TrafficCone", new Vector3(x, 0, z)); 
            foreach (var t in c.GetComponentsInChildren<Transform>()) t.gameObject.layer = 10;
        }

        static Transform Outline(float x, float z, float width, float length, Material mat, float y = .04f)
        {
            var root = new GameObject("Paint rectangle").transform; root.SetParent(markings); root.position = new Vector3(x, y, z);
            foreach (float a in new[] { -1f, 1f })
            {
                Box("Paint", new Vector3(x + a * width / 2, y, z), new Vector3(.10f, .009f, length), mat, root);
                Box("Paint", new Vector3(x, y, z + a * length / 2), new Vector3(width, .009f, .10f), mat, root);
            }
            return root;
        }

        static void Text(string text, Vector3 pos, float yaw, float height, Material mat, Transform parent)
        {
            var go = new GameObject("Label / " + text); go.transform.SetParent(parent); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var tm = go.AddComponent<TextMesh>(); tm.text = text; tm.fontSize = 100; tm.characterSize = height / 10; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = mat.color;
        }

        static void GroundText(string text, Vector3 pos, float height)
        {
            Text(text, pos, 0, height, white, markings); markings.GetChild(markings.childCount - 1).rotation = Quaternion.Euler(90, 0, 0);
        }

        static CourseGate G(string instruction, float x, float z, float yaw = 0, float width = 8, float length = 8, float hold = 0, int direction = 0, bool whole = false, int gear = 0, float speed = 0, float tolerance = 40)
            => new CourseGate { instruction = instruction, x = x, z = z, yaw = yaw, width = width, length = length, holdSeconds = hold, direction = direction, wholeVehicle = whole, minimumGear = gear, minimumSpeed = speed, headingTolerance = tolerance };

        static CourseLesson L(string id, string title, string briefing, float x, float z, float yaw, CourseGate[] gates, CourseGate[] transfer)
            => new CourseLesson { id = id, title = title, briefing = briefing, startX = x, startZ = z, startYaw = yaw, gates = gates, transfer = transfer };

        static void CreatePlayer(TrainingCourse course)
        {
            var player = new GameObject("04 / PLAYER / layout test vehicle"); player.layer = 11;
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base + "/Prefabs/DS_Sedan_A.prefab"), player.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(-90, 180, 0);
            Bounds b = visual.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach (var r in visual.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            visual.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var c in visual.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            var body = player.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.Interpolate;
            var hull = player.AddComponent<BoxCollider>(); hull.center = new Vector3(0, .75f, 0); hull.size = new Vector3(course.vehicleWidth, 1.5f, course.vehicleLength);
            var vehicle = player.AddComponent<TrainingVehicle>();
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>(); cam.tag = "MainCamera";
            cam.transform.position = new Vector3(-150, 165, -190); cam.transform.LookAt(Vector3.zero); cam.nearClipPlane = .1f; cam.farClipPlane = 800;
            cam.backgroundColor = new Color(.58f, .72f, .88f); cam.clearFlags = CameraClearFlags.SolidColor; cam.GetUniversalAdditionalCameraData();
            var director = new GameObject("05 / LESSONS AND EXAM").AddComponent<TrainingGroundDirector>(); director.vehicle = vehicle; director.view = cam;
            director.courseFile = AssetDatabase.LoadAssetAtPath<TextAsset>(Data);
            var marker = Outline(0, 0, 1, 1, teal, .06f); marker.name = "Active lesson target / practice assist"; director.marker = marker; marker.gameObject.SetActive(false);
            player.transform.position = new Vector3(course.lessons[0].startX, 0, course.lessons[0].startZ);
            player.transform.rotation = Quaternion.Euler(0, course.lessons[0].startYaw, 0);
        }

        public static void ParkedVehicle(Vector3 pos, float yaw)
        {
            var car = new GameObject("Parked Sedan / Training Fleet");
            car.transform.SetParent(props);
            car.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base + "/Prefabs/DS_Sedan_A.prefab"), car.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(-90, 180, 0);
            Bounds b = visual.GetComponentsInChildren<Renderer>()[0].bounds;
            foreach (var r in visual.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            visual.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var c in visual.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            car.layer = 10;
            var box = car.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0.75f, 0);
            box.size = new Vector3(2.1f, 1.5f, 4.5f);
        }

        static void Lighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1.0f, 0.98f, 0.94f);
            sun.shadows = LightShadows.Soft;
            sun.shadowNormalBias = 0.1f;
            sun.shadowBias = 0.05f;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.76f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.58f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.32f, 0.22f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.70f, 0.80f, 0.90f);
            RenderSettings.fogDensity = 0.0022f;
        }

        static void ValidateScene(TrainingCourse course)
        {
            foreach (var l in course.lessons)
            {
                if (!Physics.Raycast(new Vector3(l.startX, 5, l.startZ), Vector3.down, out var hit, 8, 1 << 9) || Mathf.Abs(hit.point.y) > .03f)
                    throw new Exception("Unsupported lesson start " + l.id);
                if (Physics.CheckBox(new Vector3(l.startX, .75f, l.startZ), new Vector3(1.125f, .6f, 2.25f), Quaternion.Euler(0, l.startYaw, 0), 1 << 10))
                    throw new Exception("Blocked lesson start " + l.id);
                foreach (var g in l.gates)
                    if (!Physics.Raycast(new Vector3(g.x, 5, g.z), Vector3.down, out _, 8, 1 << 9)) throw new Exception("Unsupported gate " + l.id);
            }
            if (!Physics.Raycast(new Vector3((RampTopStart + RampTopEnd) / 2, 5, RampZ), Vector3.down, out var ramp, 8, 1 << 9) || Mathf.Abs(ramp.point.y - RampHeight) > .02f)
                throw new Exception("Ramp height/contact mismatch");
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader")) throw new Exception("Missing material: " + r.name);
        }

        public static void BuildAndRender() { Build(); RenderReview(); }

        [MenuItem("Driving School/Training Ground/Render review views")]
        public static void RenderReview()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var cam = Camera.main; 
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.60f, 0.76f, 0.92f); // Clear daytime sky blue with subtle haze

            Directory.CreateDirectory("artifacts/visual-review/training-ground");

            Capture(cam, "overview", new Vector3(-65, 65, -75), Vector3.zero, false, 60);
            Capture(cam, "plan", new Vector3(0, 140, 0), Vector3.zero, true, 54);
            Capture(cam, "exercise_hill_ramp", new Vector3(-36, 6, -2), new Vector3(-20, 0, 7.6f), false, 60);
            Capture(cam, "exercise_parallel_parking", new Vector3(42, 12, 24), new Vector3(32, 0, 36), false, 60);
            Capture(cam, "exercise_garage_90", new Vector3(-4, 14, -34), new Vector3(-18, 0, -18), false, 60);
            Capture(cam, "exercise_slalom", new Vector3(46, 18, -20), new Vector3(30, 0, -3), false, 60);
            Capture(cam, "exercise_90_turns", new Vector3(-10, 20, 8), new Vector3(-27, 0, 30), false, 60);
            Capture(cam, "exercise_crossroad", new Vector3(0, 16, -6), new Vector3(16, 0, 12), false, 60);
            Capture(cam, "exercise_railway", new Vector3(-36, 8, 16), new Vector3(-45, 0, 30), false, 60);
            Capture(cam, "sign_check", new Vector3(-44.5f, 2.2f, 42.8f), new Vector3(-39.6f, 2.4f, 41.7f), false, 50);
            Debug.Log("TRAINING_GROUND_RENDERS_PASS");
        }

        // View positions are given in scheme metres and scale with the layout.
        static void Capture(Camera cam, string name, Vector3 position, Vector3 target, bool ortho, float size)
        {
            position *= SchemeScale; target *= SchemeScale; if (ortho) size *= SchemeScale;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 650;
            cam.transform.position = position;
            cam.transform.LookAt(target);
            if (ortho) cam.transform.rotation = Quaternion.Euler(90, 0, 0);
            cam.orthographic = ortho;
            if (ortho) cam.orthographicSize = size;
            else cam.fieldOfView = size;

            int width = 1920;
            int height = 1080;
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            cam.Render();

            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            byte[] pngBytes = tex.EncodeToPNG();
            File.WriteAllBytes("artifacts/visual-review/training-ground/" + name + ".png", pngBytes);

            string desktopDir = @"C:\Users\AVSok\Desktop\Unity_Screenshots";
            Directory.CreateDirectory(desktopDir);
            File.WriteAllBytes(Path.Combine(desktopDir, name + ".png"), pngBytes);

            cam.targetTexture = null;
            RenderTexture.active = old;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}


