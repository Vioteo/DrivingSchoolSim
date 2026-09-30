using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation;
using DrivingSchool.Presentation.Physics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Reproducible vehicle test range (T41): straight road with speed bumps and a cone slalom, a 90° curve,
    /// a second straight, a manoeuvring pad with an ice patch and a parked car for collision tests, lamp posts
    /// for night driving; road C south of the pad with a railway crossing (barriers, signals, a train) and a hill
    /// for hill starts. The player sedan gets the full vehicle stack (physics, visuals, lights, dashboard,
    /// mirrors, windshield rain) and the scene gets weather and the test director. Every player car of the vehicle catalogue
    /// (Data/Vehicles/vehicles.json) is assembled the same way by VehicleAssembler (T48); M switches between them.
    /// Overwrites only Scenes/VehicleTestRange.unity and Materials/VehicleTestRange/*.
    /// </summary>
    public static class VehicleTestRangeBuilder
    {
        const string Base = "Assets/DrivingSchool";
        public const string ScenePath = Base + "/Scenes/VehicleTestRange.unity";
        const string MatDir = Base + "/Materials/VehicleTestRange";
        const string SedanPrefab = Base + "/Prefabs/DS_Sedan_A.prefab";
        const int LayerMirror = 8, LayerGround = 9, LayerProps = 10, LayerPlayer = 11;

        // Layout (metres). Road A runs along +Z, the curve turns right, road B runs along +X to the pad.
        public const float RoadWidth = DrivingSchool.Presentation.TestRangeLayout.RoadWidth, RoadAStartZ = DrivingSchool.Presentation.TestRangeLayout.RoadAStartZ, RoadAEndZ = DrivingSchool.Presentation.TestRangeLayout.RoadAEndZ, CurveRadius = DrivingSchool.Presentation.TestRangeLayout.CurveRadius, RoadBEndX = DrivingSchool.Presentation.TestRangeLayout.RoadBEndX;   // раскладка — одна на рантайм и генератор
        public const float BumpRubberZ = DrivingSchool.Presentation.TestRangeLayout.BumpRubberZ, BumpAsphaltZ = DrivingSchool.Presentation.TestRangeLayout.BumpAsphaltZ;
        public static readonly Vector3 PadCentre = new Vector3(195f, 0f, 290f);
        public const float PadSize = 90f;

        static Material asphalt, paint, grass, pad, ice, concrete, lampLens;
        static Transform env, props;
        static readonly List<Renderer> roadRenderers = new List<Renderer>();

        [MenuItem("Driving School/Vehicle Test Range/Build scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory("artifacts/reports");
            roadRenderers.Clear();
            var asphaltTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Base + "/Art/RoadKit/Textures/RK_Asphalt.png");
            asphalt = Lit("Asphalt", new Color(0.33f, 0.34f, 0.35f), 0.15f, asphaltTex, new Vector2(1, 30));
            pad = Lit("PadAsphalt", new Color(0.38f, 0.39f, 0.40f), 0.15f, asphaltTex, new Vector2(12, 12));
            paint = Lit("Paint", new Color(0.95f, 0.95f, 0.93f), 0.3f);
            grass = Lit("Grass", new Color(0.23f, 0.40f, 0.19f), 0.05f);
            ice = Lit("Ice", new Color(0.78f, 0.86f, 0.93f), 0.9f);
            concrete = Lit("Concrete", new Color(0.62f, 0.62f, 0.60f), 0.2f);
            lampLens = Lit("LampLens", new Color(0.75f, 0.75f, 0.72f), 0.6f);
            var skyMat = Sky("Sky");
            var postFx = PostFx("PostFX");
            var telltaleMat = Unlit("Telltale", cutout: true, transparent: false);
            var waterMat = Unlit("WindshieldWater", cutout: false, transparent: true);   // dial scales
            var glassWaterMat = GlassWater("GlassWater");                                   // drops and snow on the windows
            var mirrorMat = Unlit("MirrorView", cutout: false, transparent: false);
            var precipMat = Particles("Precipitation");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            env = new GameObject("01 / ROADS AND GROUND").transform;
            props = new GameObject("02 / PROPS AND OBSTACLES").transform;

            BuildRoads();
            BuildProps(out var obstacle);
            BuildRoadC(out var crossing);
            var district = BuildDistrict();

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.35f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45, -35, 0);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera"; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1200f; cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true; // bloom on lamps, tone mapping for night contrast
            var volume = new GameObject("00 / POST FX").AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = postFx;

            // Every player car of the catalogue gets the same stack (T48); the first is active, M switches (PlayerVehicleSelector).
            var catalog = VehicleAssembler.LoadCatalog();
            var playerEntries = catalog.Players().ToList();
            var mats = new VehicleAssembler.PlayerMaterials { telltale = telltaleMat, dialWater = waterMat, glassWater = glassWaterMat, mirror = mirrorMat };
            var cars = new List<VehicleController>();
            foreach (var entry in playerEntries)
            {
                VehicleAssembler.ImportModel(entry.model);
                cars.Add(VehicleAssembler.BuildPlayer(entry, null, cam, mats, LayerPlayer, LayerMirror));
            }
            var controller = cars[0];
            var player = controller.gameObject;
            var visual = controller.GetComponent<VehicleVisuals>().model.gameObject;
            var rig = cam.gameObject.AddComponent<DriverCameraRig>(); rig.car = player.transform; rig.model = visual.transform;

            var weather = new GameObject("03 / WEATHER").AddComponent<WeatherController>();
            weather.sun = sun; weather.viewCamera = cam; weather.precipitationMaterial = precipMat; weather.skyMaterial = skyMat;
            RenderSettings.skybox = skyMat;
            weather.vehicles.Add(controller.GetComponent<VehiclePhysicsAdapter>());
            weather.roadRenderers.AddRange(roadRenderers);
            weather.roadRenderers.AddRange(district.roadRenderers);

            var spawn = new GameObject("SPAWN / start of road A").transform; spawn.SetParent(env);
            spawn.SetPositionAndRotation(new Vector3(-2f, 0.02f, RoadAStartZ + 10f), Quaternion.identity);
            var crash = new GameObject("SPAWN / crash test").transform; crash.SetParent(env);
            crash.SetPositionAndRotation(new Vector3(obstacle.position.x - 28f, 0.02f, obstacle.position.z), Quaternion.Euler(0, 90, 0));
            foreach (var car in cars) car.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var railSpawn = new GameObject("SPAWN / railway crossing").transform; railSpawn.SetParent(env);
            railSpawn.SetPositionAndRotation(new Vector3(RoadCX + 2f, 0.02f, RailZ - 45f), Quaternion.identity);
            var hillSpawn = new GameObject("SPAWN / hill").transform; hillSpawn.SetParent(env);
            hillSpawn.SetPositionAndRotation(new Vector3(RoadCX + 2f, 0.02f, HillStartZ - 25f), Quaternion.identity);

            var director = new GameObject("04 / TEST DIRECTOR").AddComponent<VehicleTestRangeDirector>();
            director.player = controller; director.weather = weather; director.cameraRig = rig; director.spawn = spawn; director.crashSpawn = crash;
            director.obstacleCar = obstacle.GetComponent<Rigidbody>();
            director.mirrors = player.GetComponent<VehicleMirrorRig>(); director.dashboard = player.GetComponent<DashboardView>();
            director.lights = player.GetComponent<VehicleLightsView>(); director.visuals = player.GetComponent<VehicleVisuals>();
            director.windshield = player.GetComponent<WindshieldRainView>();
            director.railwaySpawn = railSpawn; director.hillSpawn = hillSpawn; director.crossing = crossing;
            director.districtSpawn = district.entrySpawn;
            var hostSo = new SerializedObject(district.traffic); hostSo.FindProperty("player").objectReferenceValue = player.transform; hostSo.ApplyModifiedPropertiesWithoutUndo();

            var selector = new GameObject("05 / PLAYER SELECTOR (M)").AddComponent<PlayerVehicleSelector>();
            selector.cars = cars.ToArray(); selector.ids = playerEntries.Select(e => e.id).ToArray(); selector.titles = playerEntries.Select(e => e.title).ToArray();
            selector.director = director; selector.cameraRig = rig; selector.weather = weather; selector.traffic = district.traffic;

            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.0015f;
            Physics.SyncTransforms();
            foreach (var car in cars) Validate(car.gameObject, obstacle, spawn, crash);
            for (int i = 1; i < cars.Count; i++) cars[i].gameObject.SetActive(false);
            ValidateRoadC(railSpawn, hillSpawn);

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
            AssetDatabase.SaveAssets();
            File.WriteAllText("artifacts/reports/vehicle-test-range-build.json", JsonUtility.ToJson(new Report
            {
                utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, scene = ScenePath,
                renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length,
                colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length,
                staticChecksPassed = true
            }, true));
            Debug.Log("VEHICLE_TEST_RANGE_BUILD_PASS");
        }

        [Serializable] class Report { public string utc, unity, scene; public int renderers, colliders; public bool staticChecksPassed; }

        // ------------------------------------------------------------------ materials

        static Material Lit(string name, Color color, float smoothness, Texture2D tex = null, Vector2? tiling = null)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smoothness);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            EditorUtility.SetDirty(m); return m;
        }

        static Material Unlit(string name, bool cutout, bool transparent)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", Color.white);
            if (cutout) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; }
            if (transparent)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_Cull", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent + 100;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            EditorUtility.SetDirty(m); return m;
        }

        static Material GlassWater(string name)
        {
            string path = MatDir + "/" + name + ".mat";
            var shader = Shader.Find("DrivingSchool/GlassWater");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            EditorUtility.SetDirty(m); return m;
        }

        static Material Sky(string name)
        {
            string path = MatDir + "/" + name + ".mat";
            var shader = Shader.Find("DrivingSchool/Sky");
            if (shader == null) throw new Exception("Shader DrivingSchool/Sky not found");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            EditorUtility.SetDirty(m); return m;
        }

        /// <summary>Bloom (lamps, headlights, moon), neutral tone mapping and a light vignette.</summary>
        static VolumeProfile PostFx(string name)
        {
            string path = MatDir + "/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.7f); bloom.scatter.Override(0.72f);
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var vignette = profile.Add<Vignette>(true); vignette.intensity.Override(0.18f); vignette.smoothness.Override(0.5f);
            foreach (var c in profile.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile); return profile;
        }

        static Material Particles(string name)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(m); return m;
        }

        // ------------------------------------------------------------------ roads

        static void BuildRoads()
        {
            // x −470…450 (the town district reaches x −460 since T65), z −200…500.
            Box("Ground", new Vector3(-35f, -0.1f, 140f), new Vector3(970f, 0.2f, 720f), grass, env, LayerGround, true);   // x −520…450, z −220…500 (T66: the town grew west and south)

            // Road A (two lanes, dashed centre line 1.5 m dash / 4.5 m gap for visual reference only).
            float lenA = RoadAEndZ - RoadAStartZ;
            Road(Box("Road A", new Vector3(0f, 0f, RoadAStartZ + lenA / 2), new Vector3(RoadWidth, 0.04f, lenA), asphalt, env, LayerGround, true));
            EdgeLines(new Vector3(0, 0, RoadAStartZ), new Vector3(0, 0, RoadAEndZ));

            // Curve: 90° to the right, centre at (R, RoadAEndZ).
            Vector3 c = new Vector3(CurveRadius, 0f, RoadAEndZ);
            var arc = ArcMesh("Curve R" + CurveRadius, c, CurveRadius, RoadWidth, 180f, 90f, 36, 0.02f);
            arc.GetComponent<Renderer>().sharedMaterial = asphalt; Road(arc.GetComponent<Renderer>());
            foreach (float off in new[] { -RoadWidth / 2 + 0.25f, RoadWidth / 2 - 0.25f })
                ArcMesh("Curve edge line", c, CurveRadius + off, 0.12f, 180f, 90f, 36, 0.025f, false).GetComponent<Renderer>().sharedMaterial = paint;

            // Road B along +X.
            float lenB = RoadBEndX - CurveRadius;
            Road(Box("Road B", new Vector3(CurveRadius + lenB / 2, 0f, RoadAEndZ + CurveRadius), new Vector3(lenB, 0.04f, RoadWidth), asphalt, env, LayerGround, true));
            EdgeLines(new Vector3(CurveRadius, 0, RoadAEndZ + CurveRadius), new Vector3(RoadBEndX, 0, RoadAEndZ + CurveRadius));

            // Manoeuvring pad with an ice patch (grip test) — the road B joins its west edge.
            Road(Box("Pad", PadCentre, new Vector3(PadSize, 0.04f, PadSize), pad, env, LayerGround, true));
            var icePatch = Box("Ice patch 20×20", PadCentre + new Vector3(15f, 0.001f, -25f), new Vector3(20f, 0.04f, 20f), ice, env, LayerGround, true);
            var tag = icePatch.AddComponent<SurfaceTag>(); tag.surface = SurfaceType.BlackIce; tag.followWeather = false;

            Label("ЛЕЖАЧИЙ ПОЛИЦЕЙСКИЙ", new Vector3(-RoadWidth / 2 - 2f, 1.5f, BumpRubberZ - 6f), 90f);
            Label("ИСКУССТВЕННАЯ НЕРОВНОСТЬ", new Vector3(-RoadWidth / 2 - 2f, 1.5f, BumpAsphaltZ - 6f), 90f);
            Label("СЛАЛОМ", new Vector3(-RoadWidth / 2 - 2f, 1.5f, 185f), 90f);
            Label("ПОВОРОТ R" + CurveRadius, new Vector3(-RoadWidth / 2 - 2f, 1.5f, RoadAEndZ - 12f), 90f);
            Label("ЛЁД", PadCentre + new Vector3(15f, 1.2f, -37f), 0f);
            Label("СТОЛКНОВЕНИЕ", PadCentre + new Vector3(0f, 2.5f, 8f), 0f);
        }

        static void Road(GameObject go) { Road(go.GetComponent<Renderer>()); }
        static void Road(Renderer r) { if (r != null) roadRenderers.Add(r); }

        static void EdgeLines(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; float len = d.magnitude; Vector3 dir = d / len; Vector3 side = Vector3.Cross(Vector3.up, dir);
            var rot = Quaternion.LookRotation(dir);
            foreach (float s in new[] { -RoadWidth / 2 + 0.25f, RoadWidth / 2 - 0.25f })
            {
                var e = Box("Edge line", a + dir * (len / 2) + side * s + Vector3.up * 0.021f, new Vector3(0.12f, 0.005f, len), paint, env, 0, false);
                e.transform.rotation = rot;
            }
            for (float t = 2f; t + 1.5f < len; t += 6f)
            {
                var m = Box("Centre dash", a + dir * (t + 0.75f) + Vector3.up * 0.021f, new Vector3(0.12f, 0.005f, 1.5f), paint, env, 0, false);
                m.transform.rotation = rot;
            }
        }

        static GameObject ArcMesh(string name, Vector3 centre, float radius, float width, float fromDeg, float toDeg, int segments, float y, bool collider = true)
        {
            var go = new GameObject(name); go.transform.SetParent(env); go.layer = LayerGround; go.isStatic = true;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)segments) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(centre + dir * (radius - width / 2) + Vector3.up * y); v.Add(centre + dir * (radius + width / 2) + Vector3.up * y);
                float s = i / (float)segments * Mathf.Abs(toDeg - fromDeg) * Mathf.Deg2Rad * radius / 8f;
                uv.Add(new Vector2(0, s)); uv.Add(new Vector2(1, s));
                if (i < segments) { int k = i * 2; tri.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            // Winding must face up; flip if the arc direction produced downward normals.
            mesh.RecalculateNormals();
            if (mesh.normals[0].y < 0f) { tri.Reverse(); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); }
            mesh.RecalculateBounds();
            string path = MatDir + "/" + name.Replace(' ', '_') + "_" + radius.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".asset"; // имя файла не должно зависеть от языка Windows
            AssetDatabase.CreateAsset(mesh, path);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>();
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Transform parent, int layer, bool collider)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cube); o.name = name; o.transform.SetParent(parent); o.transform.position = pos; o.transform.localScale = size;
            o.GetComponent<Renderer>().sharedMaterial = mat; o.layer = layer; o.isStatic = true;
            if (!collider) UnityEngine.Object.DestroyImmediate(o.GetComponent<Collider>());
            return o;
        }

        static void Label(string text, Vector3 pos, float yaw)
        {
            var go = new GameObject("Label / " + text); go.transform.SetParent(env); go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var tm = go.AddComponent<TextMesh>(); tm.text = text; tm.fontSize = 100; tm.characterSize = 0.06f; tm.anchor = TextAnchor.MiddleCenter; tm.color = Color.white;
        }

        // ------------------------------------------------------------------ props

        static void BuildProps(out Transform obstacle)
        {
            // Speed bumps from the art kit (Z = drive over, X = across road, ground-centre pivot).
            PlaceBump(Base + "/Art/SpeedBumps/DS_SpeedBump_Rubber_7m.fbx", new Vector3(0f, 0.02f, BumpRubberZ));
            PlaceBump(Base + "/Art/SpeedBumps/DS_SpeedBump_Asphalt_3p5m.fbx", new Vector3(-1.75f, 0.02f, BumpAsphaltZ));
            PlaceBump(Base + "/Art/SpeedBumps/DS_SpeedBump_Asphalt_3p5m.fbx", new Vector3(1.75f, 0.02f, BumpAsphaltZ));

            // Cone slalom, 12 m pitch on the right lane centre line.
            var cone = AssetDatabase.LoadAssetAtPath<GameObject>(Base + "/Prefabs/Props/DS_TrafficCone.prefab");
            if (cone != null)
                for (int i = 0; i < 5; i++)
                {
                    var c = (GameObject)PrefabUtility.InstantiatePrefab(cone, props);
                    c.transform.position = new Vector3(2f, 0.02f, 180f + i * 12f);
                    foreach (var t in c.GetComponentsInChildren<Transform>()) t.gameObject.layer = LayerProps;
                    if (c.GetComponentInChildren<Collider>() == null) { var bc = c.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 0.35f, 0); bc.size = new Vector3(0.4f, 0.7f, 0.4f); }
                    if (c.GetComponent<Rigidbody>() == null) { var rb = c.AddComponent<Rigidbody>(); rb.mass = 4f; }
                }

            // Parked sedan to crash into: dynamic body so the impact pushes it.
            var car = new GameObject("Obstacle car (dynamic)"); car.transform.SetParent(props);
            car.transform.SetPositionAndRotation(PadCentre + new Vector3(-10f, 0.02f, 10f), Quaternion.identity);
            var visual = Sedan(car.transform);
            var b = LocalBounds(car.transform, visual);
            var box = car.AddComponent<BoxCollider>(); box.center = new Vector3(0, b.center.y, b.center.z); box.size = new Vector3(b.size.x * 0.9f, b.size.y, b.size.z); // rests on the tyres' contact plane
            var body = car.AddComponent<Rigidbody>(); body.mass = 1250f; body.centerOfMass = new Vector3(0, 0.5f, 0);
#if UNITY_6000_0_OR_NEWER
            body.linearDamping = 0.6f; body.angularDamping = 2f;
#else
            body.drag = 0.6f; body.angularDrag = 2f;
#endif
            SetLayer(car, LayerProps);
            obstacle = car.transform;

            // Concrete barrier blocks at the east edge of the pad.
            for (int i = -2; i <= 2; i++)
                Box("Concrete block", PadCentre + new Vector3(PadSize / 2 - 3f, 0.4f, i * 2.2f), new Vector3(0.8f, 0.8f, 2f), concrete, props, LayerProps, true).isStatic = false;

            // Lamp posts for night driving: road A every 40 m and along road B.
            var lamp = AssetDatabase.LoadAssetAtPath<GameObject>(Base + "/Prefabs/TrainingKit/TK_Lamp_7m.prefab");
            var posts = new List<Vector3>();
            for (float z = RoadAStartZ + 20f; z < RoadAEndZ; z += 40f) posts.Add(new Vector3(RoadWidth / 2 + 1.5f, 0f, z));
            for (float x = CurveRadius + 20f; x < RoadBEndX; x += 40f) posts.Add(new Vector3(x, 0f, RoadAEndZ + CurveRadius - RoadWidth / 2 - 1.5f));
            foreach (var p in posts)
            {
                // The arm of TK_Lamp_7m points along local +Z: turn it over the road.
                bool onRoadA = p.z < RoadAEndZ;
                var rot = Quaternion.Euler(0f, onRoadA ? -90f : 0f, 0f);
                if (lamp != null)
                {
                    var o = (GameObject)PrefabUtility.InstantiatePrefab(lamp, props); o.transform.SetPositionAndRotation(p, rot);
                    AddStreetLight(o.transform);
                }
                else
                {
                    var post = Box("Lamp post", p + Vector3.up * 3.5f, new Vector3(0.15f, 7f, 0.15f), concrete, props, 0, false).transform;
                    var light = new GameObject("Street light").AddComponent<Light>(); light.transform.SetParent(post, true);
                    light.transform.position = p + Vector3.up * 6.8f; light.type = LightType.Spot; light.spotAngle = 120f; light.innerSpotAngle = 70f;
                    light.range = 40f; light.intensity = 120f; light.color = new Color(1f, 0.74f, 0.45f);
                    light.transform.rotation = Quaternion.Euler(90, 0, 0); light.shadows = LightShadows.None;
                    var view = post.gameObject.AddComponent<StreetLampView>(); view.lampLight = light;
                }
            }
        }

        static void PlaceBump(string path, Vector3 pos)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) { Debug.LogWarning("Speed bump model missing: " + path + " — procedural fallback"); ProceduralBump(pos); return; }
            var o = (GameObject)PrefabUtility.InstantiatePrefab(src, props); o.name = Path.GetFileNameWithoutExtension(path);
            o.transform.position = pos;
            // Normalise: long axis across the road (X), sits on the road surface.
            var b = VehicleRigUtil.WorldBounds(o.transform);
            if (b.size.z > b.size.x) { o.transform.rotation = Quaternion.Euler(0, 90, 0) * o.transform.rotation; b = VehicleRigUtil.WorldBounds(o.transform); }
            o.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
            foreach (var mf in o.GetComponentsInChildren<MeshFilter>()) if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            SetLayer(o, LayerGround);
        }

        static void ProceduralBump(Vector3 pos)
        {
            // Cosine hump 0.06 m high, 0.9 m long, across the road.
            const int n = 16; const float h = 0.06f, len = 0.9f, w = RoadWidth;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float z = -len / 2 + len * i / n, y = h * 0.5f * (1f + Mathf.Cos(Mathf.PI * (2f * z / len)));
                v.Add(new Vector3(-w / 2, y, z)); v.Add(new Vector3(w / 2, y, z));
                if (i < n) { int k = i * 2; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            }
            var mesh = new Mesh { name = "SpeedBump_Procedural" }; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals();
            if (mesh.normals[0].y < 0f) { t.Reverse(); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); } // raycasts ignore back faces
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, MatDir + "/SpeedBump_Procedural_" + pos.z.ToString("F0") + ".asset");
            var go = new GameObject("Speed bump (procedural)"); go.transform.SetParent(props); go.transform.position = pos; go.layer = LayerGround;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = paint; go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static GameObject Sedan(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SedanPrefab);
            if (prefab == null) throw new FileNotFoundException(SedanPrefab);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // The sedan FBX still uses the legacy axis export (docs/vehicles.md): same correction as TrainingGroundBuilder.
            visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.Euler(-90, 180, 0);
            var b = VehicleRigUtil.WorldBounds(visual.transform);
            visual.transform.position -= new Vector3(b.center.x - parent.position.x, b.min.y - parent.position.y, b.center.z - parent.position.z);
            foreach (var c in visual.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            return visual;
        }

        static Bounds LocalBounds(Transform root, GameObject visual)
        {
            var wb = VehicleRigUtil.WorldBounds(visual.transform);
            return new Bounds(root.InverseTransformPoint(wb.center), wb.size);
        }

        static void SetLayer(GameObject o, int layer) { foreach (var t in o.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

        // ------------------------------------------------------------------ town district (T51)

        /// <summary>
        /// The district west of road A (DistrictBuilder) with bots, pedestrians, signals and signs, and a short road that joins
        /// its east entrance to road A at Z = DistrictOrigin.z. Traffic cars: every traffic entry of the vehicle catalogue.
        /// </summary>
        static DistrictBuilder.Result BuildDistrict()
        {
            var catalog = VehicleAssembler.LoadCatalog();
            var prefabs = new List<GameObject>();
            foreach (var v in catalog.Traffic())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleAssembler.TrafficPrefabPath(v));
                if (prefab == null) { VehicleAssembler.ImportModel(v.TrafficModelOrFull); prefab = VehicleAssembler.BuildTrafficPrefab(v); }
                prefabs.Add(prefab);
            }
            var origin = DrivingSchool.Presentation.TestRangeLayout.DistrictOrigin;
            var district = DistrictBuilder.Build(origin, null, LayerGround, LayerProps, prefabs.ToArray());
            // Link road: from the open end of the roundabout's east arm to the west edge of road A.
            var end = district.world.lanes.First(l => l.id == DistrictBuilder.EntryLane).centerline[0];
            float x0 = (float)end.x, x1 = -RoadWidth / 2;
            float axisZ = (float)end.z - 1.825f;   // the entry lane is the right-hand (north) lane of an E–W street
            if (Mathf.Abs(axisZ - TestRangeLayout.DistrictEntryZ) > 0.05f) throw new Exception("District entrance moved: axis z " + axisZ + ", layout says " + TestRangeLayout.DistrictEntryZ);
            Road(Box("Road to the town district", new Vector3((x0 + x1) / 2, 0f, axisZ), new Vector3(x1 - x0 + 0.6f, 0.04f, RoadWidth), asphalt, env, LayerGround, true));
            Label("ГОРОД (U)", new Vector3(-RoadWidth / 2 - 2f, 1.5f, axisZ + 9f), 0f);
            BuildCityRailway(district);
            // Street lights of the district (T64): the builder places the posts, the lights come from the same helper as road A's.
            var lamps = district.root.Find("Lamps");
            if (lamps != null) foreach (Transform post in lamps) AddStreetLight(post);
            return district;
        }

        // ------------------------------------------------------------------ road C: railway crossing and hill

        // Road C leaves the south edge of the pad and runs south along X = RoadCX. Driving north (+Z) keeps to x > RoadCX.
        public const float RoadCX = DrivingSchool.Presentation.TestRangeLayout.RoadCX, RoadCEndZ = DrivingSchool.Presentation.TestRangeLayout.RoadCEndZ, RailZ = DrivingSchool.Presentation.TestRangeLayout.RailZ, HillStartZ = DrivingSchool.Presentation.TestRangeLayout.HillStartZ, HillEndZ = DrivingSchool.Presentation.TestRangeLayout.HillEndZ;
        public const float HillGrade = DrivingSchool.Presentation.TestRangeLayout.HillGrade, HillRamp = DrivingSchool.Presentation.TestRangeLayout.HillRamp, HillBlend = 3f;
        const float CrossingHalfLength = DrivingSchool.Presentation.TestRangeLayout.CrossingHalfLength;                // TK_RailwayCrossing_Tracks: ramps + deck along the road
        const float TrackWestX = 40f, TrackEastX = 440f;       // the track ends (the ground ends at x 450)
        static float RoadCStartZ => PadCentre.z - PadSize / 2f;

        static void BuildRoadC(out RailwayCrossingView crossing)
        {
            RoadCSegment(RoadCEndZ, HillStartZ);
            RoadCSegment(HillEndZ, RailZ - CrossingHalfLength);
            RoadCSegment(RailZ + CrossingHalfLength, RoadCStartZ);
            Road(Box("Road C turning area", new Vector3(RoadCX, 0f, RoadCEndZ - 12f), new Vector3(30f, 0.04f, 24f), pad, env, LayerGround, true));
            BuildHill();
            crossing = BuildRailway();
            Label("Ж/Д ПЕРЕЕЗД", new Vector3(RoadCX - RoadWidth / 2 - 2f, 1.5f, RailZ - 28f), 90f);
            Label($"ГОРКА {HillGrade * 100f:F0}%", new Vector3(RoadCX - RoadWidth / 2 - 2f, 1.5f, HillStartZ - 8f), 90f);
            for (float z = RoadCEndZ + 15f; z < RoadCStartZ - 5f; z += 40f)
                if (Mathf.Abs(z - RailZ) > 14f) LampPost(new Vector3(RoadCX + RoadWidth / 2 + 1.5f, 0f, z), Quaternion.Euler(0f, -90f, 0f));
        }

        static void RoadCSegment(float z0, float z1)
        {
            float len = z1 - z0;
            Road(Box("Road C", new Vector3(RoadCX, 0f, z0 + len / 2f), new Vector3(RoadWidth, 0.04f, len), asphalt, env, LayerGround, true));
            EdgeLines(new Vector3(RoadCX, 0f, z0), new Vector3(RoadCX, 0f, z1));
        }

        // ---- hill: up at HillGrade, a flat top, down again; the grade blends in over HillBlend metres

        static float Ramp(float z, float a, float b)
        {
            if (z <= a || z >= b) return 0f;
            return Mathf.Min(1f, Mathf.Min((z - a) / HillBlend, (b - z) / HillBlend));
        }

        /// <summary>Grade (dy/dz) of road C at z.</summary>
        public static float HillSlope(float z) => HillGrade * (Ramp(z, HillStartZ, HillStartZ + HillRamp) - Ramp(z, HillEndZ - HillRamp, HillEndZ));

        /// <summary>Height of the hill's road surface above the flat road at z.</summary>
        public static float HillHeight(float z)
        {
            if (z <= HillStartZ) return 0f;
            float h = 0f, step = 0.05f;
            for (float s = HillStartZ; s < Mathf.Min(z, HillEndZ); s += step) h += HillSlope(s + step * 0.5f) * Mathf.Min(step, z - s);
            return Mathf.Max(0f, h);
        }

        static void BuildHill()
        {
            const float step = 0.5f, kerbW = 0.3f, kerbH = 0.15f, y0 = 0.02f;
            float hw = RoadWidth / 2f;
            var road = new MeshData(); var kerb = new MeshData(); var marks = new MeshData();
            int n = Mathf.CeilToInt((HillEndZ - HillStartZ) / step);
            for (int i = 0; i < n; i++)
            {
                float za = HillStartZ + i * step, zb = Mathf.Min(HillEndZ, za + step);
                float ha = HillHeight(za) + y0, hb = HillHeight(zb) + y0;
                // asphalt
                road.Quad(new Vector3(RoadCX - hw, ha, za), new Vector3(RoadCX + hw, ha, za), new Vector3(RoadCX + hw, hb, zb), new Vector3(RoadCX - hw, hb, zb),
                          new Vector2(0, za / 9.67f), new Vector2(1, za / 9.67f), new Vector2(1, zb / 9.67f), new Vector2(0, zb / 9.67f));
                // kerbs (top and inner face) and the retaining walls down to the ground
                foreach (float s in new[] { -1f, 1f })
                {
                    float xi = RoadCX + s * hw, xo = RoadCX + s * (hw + kerbW);
                    kerb.QuadAuto(new Vector3(xi, ha, za), new Vector3(xi, hb, zb), new Vector3(xi, hb + kerbH, zb), new Vector3(xi, ha + kerbH, za), -s);
                    kerb.QuadAuto(new Vector3(xi, ha + kerbH, za), new Vector3(xi, hb + kerbH, zb), new Vector3(xo, hb + kerbH, zb), new Vector3(xo, ha + kerbH, za), 0f);
                    kerb.QuadAuto(new Vector3(xo, 0f, za), new Vector3(xo, 0f, zb), new Vector3(xo, hb + kerbH, zb), new Vector3(xo, ha + kerbH, za), s);
                }
                // edge lines and centre dashes (1.5 m on / 4.5 m off), 5 mm above the asphalt
                foreach (float x in new[] { -hw + 0.25f, hw - 0.25f }) marks.Strip(RoadCX + x, 0.12f, za, zb, ha + 0.005f, hb + 0.005f);
                float phase = Mathf.Repeat(za - HillStartZ, 6f);
                if (phase < 1.5f) marks.Strip(RoadCX, 0.12f, za, zb, ha + 0.005f, hb + 0.005f);
            }
            // stop lines half way up each climb, across the lane that climbs there
            StopLineOnHill(marks, RoadCX + hw / 2f, HillStartZ + HillRamp * 0.55f);   // northbound lane
            StopLineOnHill(marks, RoadCX - hw / 2f, HillEndZ - HillRamp * 0.55f);     // southbound lane
            var roadGo = MeshObject("Hill " + (HillGrade * 100f).ToString("F0") + "% (road)", road, asphalt, true);
            Road(roadGo);
            MeshObject("Hill kerbs and walls", kerb, concrete, true);
            MeshObject("Hill markings", marks, paint, false);
        }

        static void StopLineOnHill(MeshData m, float xc, float z)
        {
            float w = RoadWidth / 2f - 0.3f, d = 0.4f;
            float ha = HillHeight(z - d / 2) + 0.027f, hb = HillHeight(z + d / 2) + 0.027f;
            m.Quad(new Vector3(xc - w / 2, ha, z - d / 2), new Vector3(xc + w / 2, ha, z - d / 2), new Vector3(xc + w / 2, hb, z + d / 2), new Vector3(xc - w / 2, hb, z + d / 2));
        }

        static GameObject MeshObject(string name, MeshData data, Material mat, bool collider)
        {
            var mesh = data.ToMesh(name);
            string path = MatDir + "/" + name.Replace(' ', '_').Replace('%', 'p').Replace('/', '_') + ".asset";
            // Rebuilding in an open editor: overwriting the asset with CreateAsset left the new MeshCollider on a stale mesh
            // (raycasts missed the hill until the next domain reload). Copy into the existing asset instead; its GUID stays.
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; mesh.name = System.IO.Path.GetFileNameWithoutExtension(path); EditorUtility.SetDirty(mesh); }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name); go.transform.SetParent(env); go.layer = LayerGround; go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        /// <summary>Small mesh accumulator: flat-shaded quads with optional UVs.</summary>
        sealed class MeshData
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2? ua = null, Vector2? ub = null, Vector2? uc = null, Vector2? ud = null)
            {
                int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua ?? new Vector2(0, 0)); uv.Add(ub ?? new Vector2(1, 0)); uv.Add(uc ?? new Vector2(1, 1)); uv.Add(ud ?? new Vector2(0, 1));
                // a, b, c, d counter-clockwise seen from above/outside -> Unity wants clockwise
                t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            /// <summary>Quad whose normal is turned towards +X (s &gt; 0), −X (s &lt; 0) or up (s = 0).</summary>
            public void QuadAuto(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float s)
            {
                Vector3 n = Vector3.Cross(c - a, b - a);
                Vector3 want = s > 0 ? Vector3.right : s < 0 ? Vector3.left : Vector3.up;
                if (Vector3.Dot(n, want) >= 0f) Quad(a, b, c, d); else Quad(a, d, c, b);
            }
            public void Strip(float xc, float w, float za, float zb, float ha, float hb)
                => Quad(new Vector3(xc - w / 2, ha, za), new Vector3(xc + w / 2, ha, za), new Vector3(xc + w / 2, hb, zb), new Vector3(xc - w / 2, hb, zb));
            public void Box(Vector3 c, Vector3 s)
            {
                Vector3 h = s / 2f;
                Vector3 p(float x, float y, float z) => c + Vector3.Scale(h, new Vector3(x, y, z));
                QuadAuto(p(-1, 1, -1), p(1, 1, -1), p(1, 1, 1), p(-1, 1, 1), 0f);                       // top
                QuadAuto(p(-1, -1, -1), p(-1, 1, -1), p(-1, 1, 1), p(-1, -1, 1), -1f);                  // -X
                QuadAuto(p(1, -1, -1), p(1, 1, -1), p(1, 1, 1), p(1, -1, 1), 1f);                       // +X
                int k = v.Count;
                Quad(p(-1, -1, -1), p(1, -1, -1), p(1, 1, -1), p(-1, 1, -1)); FixFacing(k, Vector3.back);  // -Z
                k = v.Count;
                Quad(p(-1, -1, 1), p(1, -1, 1), p(1, 1, 1), p(-1, 1, 1)); FixFacing(k, Vector3.forward); // +Z
            }
            void FixFacing(int k, Vector3 want)
            {
                int ti = t.Count - 6;
                Vector3 n = Vector3.Cross(v[t[ti + 1]] - v[t[ti]], v[t[ti + 2]] - v[t[ti]]);
                if (Vector3.Dot(n, want) < 0f) { for (int i = 0; i < 6; i += 3) { int tmp = t[ti + i + 1]; t[ti + i + 1] = t[ti + i + 2]; t[ti + i + 2] = tmp; } }
            }
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        // ---- railway crossing

        static Transform PlaceKit(string path, Vector3 pos, float yaw, Transform parent, bool dynamic = false)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) throw new FileNotFoundException(path);
            var o = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            o.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            // Kit prefabs are marked static for batching; moving parts (barrier booms) must not be batched.
            if (dynamic) foreach (var t in o.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            return o.transform;
        }

        static Material KitMat(string name, Color fallback, float smoothness)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(Base + "/Materials/TrainingKit/" + name + ".mat");
            return m != null ? m : Lit(name, fallback, smoothness);
        }

        /// <summary>
        /// Level crossing in the town (T56): barriers, lights and a stop line at the RK2_Road_RailCrossing_20m module, a track
        /// west–east across the district and its own train. The traffic host closes the crossing's signal groups for the bots
        /// and the pedestrians while the crossing is not open.
        /// </summary>
        static void BuildCityRailway(DistrictBuilder.Result district)
        {
            const string Kit = Base + "/Prefabs/TrainingKit/";
            var rail = district.rail;
            var root = new GameObject("RAILWAY (town)").transform; root.SetParent(district.root, true);
            Vector3 L(double x, double z) => DistrictBuilder.RailPoint(rail, x, z);
            float yaw = rail.yawDeg;
            var centre = L(0, 10);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) > 1f && Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) > 1f) throw new Exception("Town railway expects a N–S road at the crossing, yaw " + yaw);
            // Right of each approach: barrier 5 m before the rails, the light 1 m before the barrier (local z 10 ∓ 5).
            var bA = PlaceKit(Kit + "TK_RailwayBarrier.prefab", L(4.25, 5), yaw, root, true);
            var bB = PlaceKit(Kit + "TK_RailwayBarrier.prefab", L(-4.25, 15), yaw + 180f, root, true);
            var sA = PlaceKit(Kit + "TK_RailwaySignal.prefab", L(5.0, 4), yaw, root, true);
            var sB = PlaceKit(Kit + "TK_RailwaySignal.prefab", L(-5.0, 16), yaw + 180f, root, true);
            // The track runs just south of the start of road A (z −30) and on to the east, so the train comes from far away.
            float west = TestRangeLayout.DistrictMinX + 3f, east = TrackEastX;
            if (centre.z > RoadAStartZ - 3.5f) throw new Exception("Town railway would cross road A: track z " + centre.z);
            BuildTrack(root, centre.z, centre.x, west, east, 6.2f);
            var view = root.gameObject.AddComponent<RailwayCrossingView>();
            view.barriers = new[] { bA, bB }; view.signals = new[] { sA, sB };
            AddTrains(view, root, centre.z, east - 2f);
            TrainKitBuilder.BuildCatenary(root, centre.z, RailTopY, west, east, centre.x, 12f, 50f, LayerProps);
            view.crossingCentre = centre; view.trainDirection = Vector3.left;
            view.trackHalfLength = Mathf.Min(east - centre.x, centre.x - west) - 3f;
            // A town train at 40 km/h (T65, safety margins): the lights flash 6 s before the booms go down (7 s), the
            // train comes ≈ 30 s after the first flash, so the booms are down well before it; they rise 5 s after it.
            view.trainSpeedKmh = 40f;
            view.trainArrivesAfter = Mathf.Min(30f, view.trackHalfLength / (view.trainSpeedKmh / 3.6f));
            view.warningBeforeLowering = 6f; view.lowerSeconds = 7f; view.clearDelaySeconds = 5f; view.autoIntervalSeconds = 120f;
            CheckMargins(view, "Town railway");
            SetLayer(root.gameObject, LayerProps);
            // Bots and pedestrians: every signal group of the crossing module follows this crossing.
            var host = new SerializedObject(district.traffic);
            var groups = district.world.signalGroups.Where(g => g.junctionId.StartsWith(rail.id + "/", StringComparison.Ordinal)).Select(g => g.id).ToArray();
            if (groups.Length != 3) throw new Exception("Town railway: expected 3 signal groups, found " + groups.Length);
            host.FindProperty("railway").objectReferenceValue = view;
            var arr = host.FindProperty("railwayGroups"); arr.arraySize = groups.Length;
            for (int i = 0; i < groups.Length; i++) arr.GetArrayElementAtIndex(i).stringValue = groups[i];
            host.ApplyModifiedPropertiesWithoutUndo();
        }

        static RailwayCrossingView BuildRailway()
        {
            const string Kit = Base + "/Prefabs/TrainingKit/";
            var root = new GameObject("06 / RAILWAY CROSSING").transform;
            // The module's rails run along its local Z; turned 90° they cross road C (which runs along Z).
            PlaceKit(Kit + "TK_RailwayCrossing_Tracks.prefab", new Vector3(RoadCX, 0f, RailZ), 90f, root);
            // Barriers and signals stand on the right of each approach; the booms cover the right-hand lane.
            var bN = PlaceKit(Kit + "TK_RailwayBarrier.prefab", new Vector3(RoadCX + RoadWidth / 2 + 0.6f, 0f, RailZ - 10f), 0f, root, true);
            var bS = PlaceKit(Kit + "TK_RailwayBarrier.prefab", new Vector3(RoadCX - RoadWidth / 2 - 0.6f, 0f, RailZ + 10f), 180f, root, true);
            var sN = PlaceKit(Kit + "TK_RailwaySignal.prefab", new Vector3(RoadCX + RoadWidth / 2 + 1.4f, 0f, RailZ - 11.8f), 0f, root, true);
            var sS = PlaceKit(Kit + "TK_RailwaySignal.prefab", new Vector3(RoadCX - RoadWidth / 2 - 1.4f, 0f, RailZ + 11.8f), 180f, root, true);
            // Stop lines before the barriers, right-hand lane of each approach.
            Box("Stop line (railway, north)", new Vector3(RoadCX + RoadWidth / 4, 0.021f, RailZ - 13.5f), new Vector3(RoadWidth / 2 - 0.3f, 0.005f, 0.4f), paint, root, 0, false);
            Box("Stop line (railway, south)", new Vector3(RoadCX - RoadWidth / 4, 0.021f, RailZ + 13.5f), new Vector3(RoadWidth / 2 - 0.3f, 0.005f, 0.4f), paint, root, 0, false);
            // Warning sign "level crossing with barrier", facing each approach.
            var sign = AssetDatabase.LoadAssetAtPath<GameObject>(Base + "/Prefabs/Traffic/DS_Sign_RailwayBarrier.prefab");
            if (sign != null)
            {
                var a = (GameObject)PrefabUtility.InstantiatePrefab(sign, root);
                a.transform.SetPositionAndRotation(new Vector3(RoadCX + RoadWidth / 2 + 1.2f, 0f, RailZ - 60f), Quaternion.Euler(0f, 180f, 0f));
                var b = (GameObject)PrefabUtility.InstantiatePrefab(sign, root);
                b.transform.SetPositionAndRotation(new Vector3(RoadCX - RoadWidth / 2 - 1.2f, 0f, RailZ + 45f), Quaternion.identity);
            }
            BuildTrack(root);
            var view = root.gameObject.AddComponent<RailwayCrossingView>();
            view.barriers = new[] { bN, bS }; view.signals = new[] { sN, sS };
            AddTrains(view, root, RailZ, TrackEastX - 2f);
            TrainKitBuilder.BuildCatenary(root, RailZ, RailTopY, TrackWestX, TrackEastX, RoadCX, 14f, 50f, LayerProps);
            view.crossingCentre = new Vector3(RoadCX, 0f, RailZ); view.trainDirection = Vector3.left;
            view.trackHalfLength = RoadCX - TrackWestX - 5f;
            view.trainSpeedKmh = 40f;
            view.trainArrivesAfter = Mathf.Min(25f, (TrackEastX - RoadCX - 5f) / (view.trainSpeedKmh / 3.6f));
            view.warningBeforeLowering = 6f; view.lowerSeconds = 7f; view.clearDelaySeconds = 5f;
            CheckMargins(view, "Test range railway");
            return view;
        }

        /// <summary>Game safety margins of a crossing (T65): the booms are down at least 8 s before the train.</summary>
        static void CheckMargins(RailwayCrossingView view, string what)
        {
            float down = view.warningBeforeLowering + view.lowerSeconds;
            if (view.trainArrivesAfter < down + 8f) throw new Exception(what + ": the train comes " + view.trainArrivesAfter + " s after the first flash, the booms are down only at " + down + " s");
            if (view.clearDelaySeconds < 3f) throw new Exception(what + ": the booms would rise right behind the train");
        }

        static void BuildTrack(Transform root) => BuildTrack(root, RailZ, RoadCX, TrackWestX, TrackEastX, 5.5f);

        /// <summary>Track along X at <paramref name="railZ"/> from <paramref name="westX"/> to <paramref name="eastX"/>, leaving a gap of ±<paramref name="moduleHalf"/> around the crossing module at <paramref name="crossX"/>.</summary>
        static void BuildTrack(Transform root, float railZ, float crossX, float westX, float eastX, float moduleHalf)
        {
            var ballastMat = KitMat("RW_Ballast", new Color(0.42f, 0.40f, 0.38f), 0.05f);
            var sleeperMat = KitMat("RW_Sleeper", new Color(0.45f, 0.44f, 0.42f), 0.1f);
            var railMat = KitMat("RW_RailSteel", new Color(0.35f, 0.33f, 0.31f), 0.5f);
            const float gauge = 1.6f;   // rail centres of the module are 0.8 m off the axis
            foreach (var (x0, x1, tag) in new[] { (westX, crossX - moduleHalf, "west"), (crossX + moduleHalf, eastX, "east") })
            {
                float len = x1 - x0, xc = (x0 + x1) / 2f;
                // ballast bed (trapezoid) and sleepers every 0.55 m, as two meshes
                var bed = new MeshData();
                float top = 0.03f, bot = -0.05f, wt = 1.45f, wb = 2.1f;
                bed.QuadAuto(new Vector3(x0, top, railZ - wt), new Vector3(x1, top, railZ - wt), new Vector3(x1, top, railZ + wt), new Vector3(x0, top, railZ + wt), 0f);
                foreach (float s in new[] { -1f, 1f })
                {
                    var q = new[] { new Vector3(x0, top, railZ + s * wt), new Vector3(x1, top, railZ + s * wt), new Vector3(x1, bot, railZ + s * wb), new Vector3(x0, bot, railZ + s * wb) };
                    Vector3 nrm = Vector3.Cross(q[2] - q[0], q[1] - q[0]);
                    if (nrm.z * s < 0f) bed.Quad(q[0], q[3], q[2], q[1]); else bed.Quad(q[0], q[1], q[2], q[3]);
                }
                var bedGo = MeshObject($"Track ballast ({tag})", bed, ballastMat, false); bedGo.transform.SetParent(root, true);
                var bc = bedGo.AddComponent<BoxCollider>(); bc.center = new Vector3(xc, -0.01f, railZ); bc.size = new Vector3(len, 0.08f, wt * 2f);
                var sl = new MeshData();
                for (float x = x0 + 0.3f; x < x1 - 0.2f; x += 0.55f) sl.Box(new Vector3(x, -0.015f, railZ), new Vector3(0.25f, 0.12f, 2.6f));
                MeshObject($"Track sleepers ({tag})", sl, sleeperMat, false).transform.SetParent(root, true);
                foreach (float s in new[] { -1f, 1f })
                {
                    var rail = Box($"Rail ({tag})", new Vector3(xc, 0.1425f, railZ + s * gauge / 2f), new Vector3(len, 0.195f, 0.07f), railMat, root, 0, false);
                    rail.isStatic = true;
                }
            }
        }

        /// <summary>Rail top above the ground (BuildTrack: rail centre 0.1425 m, height 0.195 m).</summary>
        const float RailTopY = 0.24f;

        /// <summary>
        /// T69: the crossing's trains — a suburban EMU and a freight train (Prefabs/Trains, tools/build_trains.py), in turn.
        /// Both wait hidden at <paramref name="startX"/>, heading west (−X).
        /// </summary>
        static void AddTrains(RailwayCrossingView view, Transform root, float railZ, float startX)
        {
            var emu = TrainKitBuilder.BuildConsist("Train / EMU (kinematic)", TrainKitBuilder.ElectricTrain, root, LayerProps, out float emuLength);
            var freight = TrainKitBuilder.BuildConsist("Train / freight (kinematic)", TrainKitBuilder.FreightTrain, root, LayerProps, out float freightLength);
            foreach (var rb in new[] { emu, freight })
                rb.transform.SetPositionAndRotation(new Vector3(startX, RailTopY, railZ), Quaternion.LookRotation(Vector3.left));
            view.train = emu; view.trainLength = emuLength;
            view.consists = new[] { emu, freight };
            view.consistLengths = new[] { emuLength, freightLength };
        }

        static void LampPost(Vector3 p, Quaternion rot)
        {
            var lamp = AssetDatabase.LoadAssetAtPath<GameObject>(Base + "/Prefabs/TrainingKit/TK_Lamp_7m.prefab");
            if (lamp == null) return;
            var o = (GameObject)PrefabUtility.InstantiatePrefab(lamp, props); o.transform.SetPositionAndRotation(p, rot);
            AddStreetLight(o.transform);
        }

        /// <summary>
        /// A TK_Lamp_7m post gets its lens and a sodium-coloured spot light under the housing, switched by daylight
        /// (StreetLampView). Also used for the lamps of the town district (T64).
        /// </summary>
        static void AddStreetLight(Transform post)
        {
            var rot = post.rotation;
            Vector3 lensPos = post.TransformPoint(new Vector3(0f, 6.78f, 1.3f));
            // The light sits at the end of the arm with a 120° cone, so the post itself is not lit at arm's length (T64).
            Vector3 lightPos = post.TransformPoint(new Vector3(0f, 6.75f, 1.9f));
            var l = Box("Lamp lens", lensPos + Vector3.up * 0.03f, new Vector3(0.34f, 0.02f, 0.56f), lampLens, post, 0, false);
            l.transform.rotation = rot; l.isStatic = false; var lens = l.GetComponent<Renderer>(); lens.shadowCastingMode = ShadowCastingMode.Off;
            var light = new GameObject("Street light").AddComponent<Light>(); light.transform.SetParent(post, true);
            light.transform.position = lightPos; light.type = LightType.Spot; light.spotAngle = 120f; light.innerSpotAngle = 70f;
            light.range = 40f; light.intensity = 120f; light.color = new Color(1f, 0.74f, 0.45f);
            light.transform.rotation = Quaternion.Euler(90, 0, 0); light.shadows = LightShadows.None;
            var view = post.gameObject.AddComponent<StreetLampView>(); view.lampLight = light; view.lens = lens;
        }

        static void ValidateRoadC(Transform railSpawn, Transform hillSpawn)
        {
            foreach (var p in new[] { railSpawn.position, hillSpawn.position })
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out _, 6f, 1 << LayerGround)) throw new Exception("No road under " + p);
            float top = HillHeight((HillStartZ + HillEndZ) / 2f);
            if (!Physics.Raycast(new Vector3(RoadCX + 2f, 5f, (HillStartZ + HillEndZ) / 2f), Vector3.down, out var h, 8f, 1 << LayerGround) || Mathf.Abs(h.point.y - top - 0.02f) > 0.05f)
                throw new Exception("Hill surface missing or at the wrong height (expected " + top.ToString("F2") + " m)");
            if (!Physics.Raycast(new Vector3(RoadCX + 2f, 3f, RailZ), Vector3.down, out var d, 6f, 1 << LayerGround) || d.point.y < 0.2f)
                throw new Exception("Railway crossing deck has no collider");
        }

        // ------------------------------------------------------------------ player

        static void Validate(GameObject player, Transform obstacle, Transform spawn, Transform crash)
        {
            string[] required = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "SteeringWheel_Pivot", "Socket_DriverEye" };
            foreach (var n in required)
                if (VehicleRigUtil.Find(player.transform, n) == null) throw new Exception(player.name + ": part missing: " + n);
            foreach (var p in new[] { spawn.position, crash.position, BumpRubberZ * Vector3.forward, new Vector3(CurveRadius * (1f - 0.7071f), 0, RoadAEndZ + CurveRadius * 0.7071f) /* curve midpoint (135°) */ })
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 6f, 1 << LayerGround) || hit.point.y < -0.01f)
                    throw new Exception("No road under " + p);
            if (!Physics.Raycast(new Vector3(0, 1f, BumpRubberZ), Vector3.down, out var bump, 2f, 1 << LayerGround) || bump.point.y < 0.03f)
                throw new Exception("Speed bump has no collider at road A");
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader")) throw new Exception("Missing material: " + r.name);
        }
    }
}
