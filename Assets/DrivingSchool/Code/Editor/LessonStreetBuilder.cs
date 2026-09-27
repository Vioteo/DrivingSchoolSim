using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using L = DrivingSchool.Presentation.LessonStreetLayout;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Сцена «Учебная улица» для урока 1 (T49, docs/lessons.md): городская улица из Road Kit с регулируемым
    /// перекрёстком, тротуарами, домами и фонарями. Машину игрока, камеру, погоду и директора берёт из собранной
    /// сцены полигона (VehicleTestRange): окружение полигона удаляется, строится улица, сцена сохраняется отдельно.
    /// Поэтому сначала соберите полигон, потом улицу. Сцена генерируется — руками не править.
    /// </summary>
    public static class LessonStreetBuilder
    {
        const string Source = "Assets/DrivingSchool/Scenes/VehicleTestRange.unity";
        const string RK = "Assets/DrivingSchool/Prefabs/RoadKit/";
        const string Traffic = "Assets/DrivingSchool/Prefabs/Traffic/";
        const string Houses = "Assets/DrivingSchool/Prefabs/Houses/";
        const string Kit = "Assets/DrivingSchool/Prefabs/TrainingKit/";
        const string MatDir = "Assets/DrivingSchool/Materials/LessonStreet";
        const int LayerGround = 9, LayerProps = 10;
        static readonly string[] KeepComponents =
            { "VehicleController", "WeatherController", "VehicleTestRangeDirector", "PlayerVehicleSelector", "DriverCameraRig" };

        static Transform env;
        static readonly List<Renderer> roadRenderers = new List<Renderer>();

        [MenuItem("Driving School/Lessons/Build lesson street scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(Source)) throw new InvalidOperationException("Нет " + Source + " — сначала соберите тестовый полигон.");
            roadRenderers.Clear();
            var scene = EditorSceneManager.OpenScene(Source, OpenSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                if (!Keep(root)) Object.DestroyImmediate(root);

            env = new GameObject("01 / LESSON STREET").transform;
            Ground();
            Roads();
            var junction = Signals();
            HousesAlongStreet();
            Lamps();

            var director = Object.FindAnyObjectByType<VehicleTestRangeDirector>();
            if (director == null) throw new InvalidOperationException("В сцене полигона нет директора");
            var spawn = new GameObject("SPAWN / lesson start").transform; spawn.SetParent(env);
            spawn.SetPositionAndRotation(L.Start + Vector3.up * 0.02f, Quaternion.identity);
            director.spawn = spawn; director.crashSpawn = null; director.railwaySpawn = null; director.hillSpawn = null;
            director.crossing = null; director.obstacleCar = null;
            EditorUtility.SetDirty(director);
            foreach (var car in Object.FindObjectsByType<VehicleController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                car.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var weather = Object.FindAnyObjectByType<WeatherController>();
            if (weather != null) { weather.roadRenderers.Clear(); weather.roadRenderers.AddRange(roadRenderers); EditorUtility.SetDirty(weather); }

            Physics.SyncTransforms();
            Validate(junction);
            EditorSceneManager.SaveScene(scene, L.ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == L.ScenePath)) { scenes.Add(new EditorBuildSettingsScene(L.ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
            AssetDatabase.SaveAssets();
            Debug.Log("LESSON_STREET_BUILD_PASS " + L.ScenePath);
        }

        static bool Keep(GameObject root)
        {
            if (root.GetComponent<Camera>() || root.GetComponent<Volume>()) return true;
            var light = root.GetComponent<Light>(); if (light && light.type == LightType.Directional) return true;
            return root.GetComponents<MonoBehaviour>().Any(c => c != null && KeepComponents.Contains(c.GetType().Name));
        }

        // ------------------------------------------------------------------ ground and roads

        static Material Grass()
        {
            Directory.CreateDirectory(MatDir);
            string path = MatDir + "/Grass.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", new Color(0.25f, 0.42f, 0.2f)); m.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Ground()
        {
            var g = Grass();
            const float far = 260f, top = 0.14f, s = L.SidewalkOuter;
            // Газон вровень с тротуаром — вне улиц; подложка ниже всего — на случай выезда за край.
            Slab("Ground / base", new Vector3(0, -0.4f, 0), new Vector3(2 * far, 0.1f, 2 * far), g);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    Box("Lawn", s, far, s, far, sx, sz, top, g);
            foreach (int sz in new[] { -1, 1 }) Slab("Lawn / street end", new Vector3(0, top / 2 - 0.05f, sz * (L.StreetEnd + far) / 2), new Vector3(2 * s, top + 0.1f, far - L.StreetEnd), g);
            foreach (int sx in new[] { -1, 1 }) Slab("Lawn / arm end", new Vector3(sx * (L.ArmEnd + far) / 2, top / 2 - 0.05f, 0), new Vector3(far - L.ArmEnd, top + 0.1f, 2 * s), g);
        }

        static void Box(string name, float x0, float x1, float z0, float z1, int sx, int sz, float top, Material m)
        {
            Slab(name, new Vector3(sx * (x0 + x1) / 2, top / 2 - 0.05f, sz * (z0 + z1) / 2), new Vector3(x1 - x0, top + 0.1f, z1 - z0), m);
        }

        static void Slab(string name, Vector3 centre, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(env); go.transform.position = centre; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m; go.layer = LayerGround; go.isStatic = true;
        }

        static void Roads()
        {
            var roads = new GameObject("Roads").transform; roads.SetParent(env);
            Road("RK_Road_Cross_24m", Vector3.zero, 0, roads, "Crossroad");
            for (float z = L.JunctionHalf; z < L.StreetEnd - 0.1f; z += 20f)
            {
                Road("RK_Road_Urban_20m", new Vector3(0, 0, z), 0, roads, "Street N");
                Road("RK_Road_Urban_20m", new Vector3(0, 0, -z), 180, roads, "Street S");
            }
            for (float x = L.JunctionHalf; x < L.ArmEnd - 0.1f; x += 20f)
            {
                Road("RK_Road_Urban_20m", new Vector3(x, 0, 0), 90, roads, "Street E");
                Road("RK_Road_Urban_20m", new Vector3(-x, 0, 0), 270, roads, "Street W");
            }
        }

        static void Road(string prefab, Vector3 pos, float yaw, Transform parent, string name)
        {
            var go = Place(RK + prefab + ".prefab", pos, yaw, parent);
            go.name = name + " / " + prefab;
            SetLayer(go, LayerGround); go.isStatic = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.enabled) roadRenderers.Add(r);
        }

        // ------------------------------------------------------------------ signals

        static SignalJunction Signals()
        {
            var root = new GameObject("Crossroad / signals").transform; root.SetParent(env); root.position = Vector3.zero;
            var cycle = root.gameObject.AddComponent<CrossroadSignalCycle>();
            cycle.green = 15; cycle.amber = 3; cycle.allRed = 2;
            var junction = root.gameObject.AddComponent<SignalJunction>();
            junction.signals = cycle; junction.stopLineOffset = L.StopLine; junction.armHalfWidth = L.RoadHalfWidth;
            // Столб — у правого края перед перекрёстком по ходу; головка смотрит на подъезжающих (heading + 180).
            const float a = 10.8f, b = 5.5f;
            var ns = new List<TrafficSignalView>(); var ew = new List<TrafficSignalView>();
            foreach (var (pos, heading) in new[] { (new Vector3(b, 0, -a), 0f), (new Vector3(-b, 0, a), 180f), (new Vector3(-a, 0, -b), 90f), (new Vector3(a, 0, b), 270f) })
            {
                var go = Place(Traffic + "DS_Signal_Vehicle.prefab", pos + Vector3.up * 0.15f, heading + 180f, root);
                go.name = "Signal / vehicle / " + heading.ToString("0");
                var view = go.GetComponentInChildren<TrafficSignalView>();
                if (view == null) throw new InvalidOperationException("DS_Signal_Vehicle без TrafficSignalView");
                (heading == 0f || heading == 180f ? ns : ew).Add(view);
            }
            cycle.northSouth = ns.ToArray(); cycle.eastWest = ew.ToArray();
            return junction;
        }

        // ------------------------------------------------------------------ houses and lamps

        static void HousesAlongStreet()
        {
            var names = new[] { "DS_House_Brick5", "DS_House_Townhouse", "DS_House_Modern8", "DS_House_Cottage" };
            var parent = new GameObject("Houses").transform; parent.SetParent(env);
            int k = 0;
            foreach (int side in new[] { -1, 1 })
                foreach (int dir in new[] { -1, 1 })
                {
                    float z = L.JunctionHalf + 10f;
                    while (true)
                    {
                        var go = Place(Houses + names[k++ % names.Length] + ".prefab", Vector3.zero, side > 0 ? 270f : 90f, parent);
                        var bounds = Bounds(go);
                        float len = bounds.size.z;
                        if (z + len > L.StreetEnd - 5f) { Object.DestroyImmediate(go); break; }
                        // Фасад в 4 м от тротуара, дом вдоль улицы.
                        float x = side * (L.SidewalkOuter + 4f + bounds.size.x / 2);
                        go.transform.position += new Vector3(x - bounds.center.x, 0.14f - bounds.min.y, dir * (z + len / 2) - bounds.center.z);
                        SetLayer(go, LayerProps); go.isStatic = true;
                        z += len + 6f;
                    }
                }
        }

        static void Lamps()
        {
            var parent = new GameObject("Lamps").transform; parent.SetParent(env);
            for (float z = L.JunctionHalf + 8f; z < L.StreetEnd; z += 30f)
                foreach (int dir in new[] { -1, 1 })
                    foreach (int side in new[] { -1, 1 })
                    {
                        var go = Place(Kit + "TK_Lamp_7m.prefab", new Vector3(side * (L.RoadHalfWidth + 1.4f), 0.15f, dir * z), side > 0 ? 270f : 90f, parent);
                        SetLayer(go, LayerProps);
                    }
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Place(string path, Vector3 pos, float yaw, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Нет префаба " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return go;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void SetLayer(GameObject o, int layer) { foreach (var t in o.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }

        static void Validate(SignalJunction junction)
        {
            // Под стартом, у стоп-линии и за перекрёстком — асфальт на высоте 0 (слой земли).
            foreach (var p in new[] { L.Start, new Vector3(2f, 0, -L.StopLine - 2f), new Vector3(2f, 0, 0), new Vector3(2f, 0, 60f), new Vector3(3f, 0, 62f) })
            {
                if (!Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 10f, 1 << LayerGround) || Mathf.Abs(hit.point.y) > 0.03f)
                    throw new InvalidOperationException($"Нет дороги под точкой {p}: {(hit.collider ? hit.collider.name + " y=" + hit.point.y : "пусто")}");
            }
            if (!junction.Approach(new Vector3(2f, 0, -30f), Vector3.forward, out _, out float d) || Mathf.Abs(d - 20f) > 0.01f)
                throw new InvalidOperationException("SignalJunction: неверное расстояние до стоп-линии");
            if (junction.signals.northSouth.Length != 2 || junction.signals.eastWest.Length != 2) throw new InvalidOperationException("Светофоры: нужно по 2 на направление");
        }
    }
}
