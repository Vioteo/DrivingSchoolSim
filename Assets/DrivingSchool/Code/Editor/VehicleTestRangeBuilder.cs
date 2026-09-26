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
    /// for night driving. The player sedan gets the full vehicle stack (physics, visuals, lights, dashboard,
    /// mirrors, windshield rain) and the scene gets weather and the test director.
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
        public const float RoadWidth = 8f, RoadAStartZ = -30f, RoadAEndZ = 260f, CurveRadius = 30f, RoadBEndX = 150f;
        public const float BumpRubberZ = 80f, BumpAsphaltZ = 140f;
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

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.35f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(45, -35, 0);

            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera"; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1200f; cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true; // bloom on lamps, tone mapping for night contrast
            var volume = new GameObject("00 / POST FX").AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = postFx;

            var player = CreatePlayer(cam, telltaleMat, waterMat, glassWaterMat, mirrorMat, out var controller, out var visual);
            var rig = cam.gameObject.AddComponent<DriverCameraRig>(); rig.car = player.transform; rig.model = visual.transform;

            var weather = new GameObject("03 / WEATHER").AddComponent<WeatherController>();
            weather.sun = sun; weather.viewCamera = cam; weather.precipitationMaterial = precipMat; weather.skyMaterial = skyMat;
            RenderSettings.skybox = skyMat;
            weather.vehicles.Add(controller.GetComponent<VehiclePhysicsAdapter>());
            weather.roadRenderers.AddRange(roadRenderers);

            var spawn = new GameObject("SPAWN / start of road A").transform; spawn.SetParent(env);
            spawn.SetPositionAndRotation(new Vector3(-2f, 0.02f, RoadAStartZ + 10f), Quaternion.identity);
            var crash = new GameObject("SPAWN / crash test").transform; crash.SetParent(env);
            crash.SetPositionAndRotation(new Vector3(obstacle.position.x - 28f, 0.02f, obstacle.position.z), Quaternion.Euler(0, 90, 0));
            player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

            var director = new GameObject("04 / TEST DIRECTOR").AddComponent<VehicleTestRangeDirector>();
            director.player = controller; director.weather = weather; director.cameraRig = rig; director.spawn = spawn; director.crashSpawn = crash;
            director.obstacleCar = obstacle.GetComponent<Rigidbody>();
            director.mirrors = player.GetComponent<VehicleMirrorRig>(); director.dashboard = player.GetComponent<DashboardView>();
            director.lights = player.GetComponent<VehicleLightsView>(); director.visuals = player.GetComponent<VehicleVisuals>();
            director.windshield = player.GetComponent<WindshieldRainView>();

            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.0015f;
            Physics.SyncTransforms();
            Validate(player, obstacle, spawn, crash);

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
            Box("Ground", new Vector3(100f, -0.1f, 150f), new Vector3(700f, 0.2f, 700f), grass, env, LayerGround, true);

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
                Transform post; Vector3 lightPos; Renderer lens = null;
                if (lamp != null)
                {
                    var o = (GameObject)PrefabUtility.InstantiatePrefab(lamp, props); o.transform.SetPositionAndRotation(p, rot); post = o.transform;
                    lightPos = post.TransformPoint(new Vector3(0f, 6.78f, 1.3f)); // under the lamp housing
                    var l = Box("Lamp lens", lightPos + Vector3.up * 0.03f, new Vector3(0.34f, 0.02f, 0.56f), lampLens, post, 0, false);
                    l.transform.rotation = rot; l.isStatic = false; lens = l.GetComponent<Renderer>();
                    lens.shadowCastingMode = ShadowCastingMode.Off;
                }
                else { post = Box("Lamp post", p + Vector3.up * 3.5f, new Vector3(0.15f, 7f, 0.15f), concrete, props, 0, false).transform; lightPos = p + Vector3.up * 6.8f; }
                var light = new GameObject("Street light").AddComponent<Light>(); light.transform.SetParent(post, true);
                light.transform.position = lightPos; light.type = LightType.Spot; light.spotAngle = 135f; light.innerSpotAngle = 70f;
                light.range = 32f; light.intensity = 45f; light.color = new Color(1f, 0.74f, 0.45f); // high-pressure sodium look
                light.transform.rotation = Quaternion.Euler(90, 0, 0); light.shadows = LightShadows.None;
                var view = post.gameObject.AddComponent<StreetLampView>(); view.lampLight = light; view.lens = lens;
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

        // ------------------------------------------------------------------ player

        static GameObject CreatePlayer(Camera cam, Material telltale, Material water, Material glassWater, Material mirror, out VehicleController controller, out GameObject visual)
        {
            var player = new GameObject("05 / PLAYER / DS_Sedan_A");
            visual = Sedan(player.transform);
            SetLayer(player, LayerPlayer);
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("MirrorSurface")) t.gameObject.layer = LayerMirror;

            var b = LocalBounds(player.transform, visual);
            var hull = player.AddComponent<BoxCollider>();
            const float clearance = 0.28f; // wheels are raycasts; the hull must clear speed bumps
            hull.center = new Vector3(0f, (clearance + b.max.y) / 2f, b.center.z);
            hull.size = new Vector3(b.size.x * 0.88f, b.max.y - clearance, b.size.z * 0.98f);

            var body = player.AddComponent<Rigidbody>(); body.mass = 1350f; body.interpolation = RigidbodyInterpolation.Interpolate;
            var adapter = player.AddComponent<VehiclePhysicsAdapter>();
            adapter.vehicleJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/StreamingAssets/Examples/vehicle.json");
            adapter.groundMask = ~((1 << LayerPlayer) | (1 << LayerMirror) | (1 << 2));
            controller = player.AddComponent<VehicleController>();
            var visuals = player.AddComponent<VehicleVisuals>(); visuals.adapter = adapter; visuals.model = visual.transform;
            var lights = player.AddComponent<VehicleLightsView>(); lights.adapter = adapter; lights.model = visual.transform;
            var dash = player.AddComponent<DashboardView>(); dash.adapter = adapter; dash.model = visual.transform; dash.templateMaterial = telltale; dash.dialMaterial = water;
            var rain = player.AddComponent<WindshieldRainView>(); rain.adapter = adapter; rain.visuals = visuals; rain.model = visual.transform; rain.templateMaterial = glassWater;
            var mirrors = player.AddComponent<VehicleMirrorRig>(); mirrors.model = visual.transform; mirrors.viewer = cam; mirrors.templateMaterial = mirror;
            return player;
        }

        static void Validate(GameObject player, Transform obstacle, Transform spawn, Transform crash)
        {
            string[] required = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "SteeringWheel_Pivot", "Socket_DriverEye" };
            foreach (var n in required)
                if (VehicleRigUtil.Find(player.transform, n) == null) throw new Exception("Sedan part missing: " + n);
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
