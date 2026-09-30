using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Лёгкие сцены фона главного меню (T70). Раньше фон грузил целую сцену поездки (город с трафиком — 5 МБ, автодром с
    /// машинами игрока и директорами). Теперь из сцен поездки вырезается только то, что видит камера меню:
    ///  • MenuBackdrop_Garage — студия и копии машин игрока (только модели); видна машина из профиля (<see cref="MenuBackdropCars"/>);
    ///  • MenuBackdrop_Railway — переезд у площадки с поездами и мир в радиусе <see cref="RailwayRadiusM"/> м (дома — все, как горизонт);
    ///  • MenuBackdrop_Autodrome — площадка без машины, директоров и камеры.
    /// Везде убраны коллайдеры, физика (кроме поездов переезда), камеры, трафик и управление. Порядок сборки: полигон →
    /// автодром → фоны (Driving School/Build menu backdrops). Результат не правится руками.
    /// </summary>
    public static class MenuBackdropBuilder
    {
        public const string GarageScene = "MenuBackdrop_Garage", RailwayScene = "MenuBackdrop_Railway", AutodromeScene = "MenuBackdrop_Autodrome";
        public static readonly Color GarageBackground = new Color(0.055f, 0.06f, 0.07f);
        public const float RailwayRadiusM = 320f;
        const string Scenes = "Assets/DrivingSchool/Scenes/";
        const string Mats = "Assets/DrivingSchool/Materials/MenuBackdrop";

        // Корни сцен поездки, которые фону не нужны (машины игрока — по компоненту VehicleController).
        static readonly string[] DropRoots = { "Main Camera", "04 / TEST DIRECTOR", "05 / PLAYER SELECTOR (M)", "05 / LESSONS AND EXAM" };
        static readonly string[] DropChildren = { "TRAFFIC HOST", "RAILWAY (town)", "SPAWN / town district" };
        static readonly string[] DropBehaviours = { "VehicleTestRangeDirector", "TrainingGroundDirector", "PlayerVehicleSelector", "TrafficDirectorHost", "DriverCameraRig", "TestRangeInstructor" };

        [MenuItem("Driving School/Build menu backdrops")]
        public static void BuildMenu() { Build(); }

        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Mats);
            var report = new List<string>();
            report.Add(BuildGarage());
            report.Add(Cut(UIBuilder.DriveScenePath, RailwayScene, new Vector3(VehicleTestRangeBuilder.RoadCX, 0f, VehicleTestRangeBuilder.RailZ), RailwayRadiusM, "06 / RAILWAY CROSSING"));
            report.Add(Cut(Scenes + "Autodrome_Training.unity", AutodromeScene, Vector3.zero, float.PositiveInfinity, null));
            AddToBuild(GarageScene, RailwayScene, AutodromeScene);
            Directory.CreateDirectory("artifacts/reports");
            File.WriteAllText("artifacts/reports/menu-backdrops.txt", string.Join("\n", report) + "\n");
            Debug.Log("MENU_BACKDROPS_BUILT\n" + string.Join("\n", report));
        }

        // ------------------------------------------------------------------ гараж

        static string BuildGarage()
        {
            var source = EditorSceneManager.OpenScene(UIBuilder.DriveScenePath, OpenSceneMode.Single);
            var selector = Object.FindAnyObjectByType<PlayerVehicleSelector>();
            if (selector == null || selector.cars.Length == 0) throw new InvalidOperationException("В сцене полигона нет машин игрока — сначала Vehicle Test Range/Build scene");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var root = new GameObject("CARS");
            SceneManager.MoveGameObjectToScene(root, scene);
            var shown = root.AddComponent<MenuBackdropCars>();
            var cars = new List<GameObject>(); var ids = new List<string>();
            for (int i = 0; i < selector.cars.Length; i++)
            {
                if (selector.cars[i] == null) continue;
                var copy = Object.Instantiate(selector.cars[i].gameObject);
                copy.name = i < selector.ids.Length ? selector.ids[i] : selector.cars[i].name;
                copy.SetActive(true);
                StripToModel(copy);
                copy.transform.SetParent(root.transform, false);
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var b = BoundsOf(copy);
                copy.transform.position = new Vector3(-b.center.x, -b.min.y, -b.center.z);   // колёса на полу, машина по центру подиума
                cars.Add(copy); ids.Add(i < selector.ids.Length ? selector.ids[i] : "");
            }
            shown.cars = cars.ToArray(); shown.ids = ids.ToArray();
            EditorSceneManager.CloseScene(source, true);
            SceneManager.SetActiveScene(scene);

            // Студия: пол, подиум, три источника света, мягкий эмбиент, без неба.
            var floor = Prim(PrimitiveType.Cylinder, "Floor", new Vector3(0f, -0.05f, 0f), new Vector3(40f, 0.05f, 40f), Mat("Floor", new Color(0.11f, 0.115f, 0.125f), 0.55f));
            var stage = Prim(PrimitiveType.Cylinder, "Turntable", new Vector3(0f, -0.01f, 0f), new Vector3(7.5f, 0.012f, 7.5f), Mat("Turntable", new Color(0.19f, 0.2f, 0.215f), 0.7f));
            var ring = Prim(PrimitiveType.Cylinder, "Turntable edge", new Vector3(0f, -0.02f, 0f), new Vector3(7.7f, 0.01f, 7.7f), Mat("Edge", new Color(0.8f, 0.55f, 0.1f), 0.4f, 1.2f));
            Light("Key", LightType.Directional, new Vector3(50f, -140f, 0f), Vector3.zero, 1.6f, new Color(1f, 0.96f, 0.9f), true);
            Light("Fill", LightType.Directional, new Vector3(25f, 60f, 0f), Vector3.zero, 0.45f, new Color(0.75f, 0.85f, 1f), false);
            Light("Rim", LightType.Spot, new Vector3(35f, 180f, 0f), new Vector3(0f, 5f, 7f), 60f, new Color(1f, 0.85f, 0.6f), false, 60f, 20f);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.34f, 0.38f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.21f, 0.23f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.09f);
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            foreach (var go in new[] { floor, stage, ring }) go.isStatic = true;
            Save(scene, GarageScene);
            return $"{GarageScene}: машин {cars.Count} ({string.Join(", ", ids)})";
        }

        /// <summary>Только модель: Transform, меши и LODGroup; колёса, салон и фары — как в поездке, но без скриптов и физики.</summary>
        static void StripToModel(GameObject go)
        {
            var keep = new HashSet<Type> { typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer), typeof(SkinnedMeshRenderer), typeof(LODGroup) };
            // Удалять в обратном порядке: зависимые компоненты (UniversalAdditionalCameraData → Camera) раньше.
            foreach (var c in go.GetComponentsInChildren<Component>(true).Reverse())
                if (c != null && !keep.Contains(c.GetType())) Object.DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.StartsWith("MirrorSurface", StringComparison.Ordinal)) r.enabled = false;   // зеркала без камер — чёрные
        }

        // ------------------------------------------------------------------ вырезка из сцены поездки

        static string Cut(string sourcePath, string target, Vector3 pivot, float radius, string keepWhole)
        {
            var scene = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Single);
            int before = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Renderer>(true).Length);
            foreach (var r in scene.GetRootGameObjects())
            {
                if (DropRoots.Contains(r.name) || r.GetComponent<VehicleController>() != null) { Object.DestroyImmediate(r); continue; }
                foreach (var t in r.GetComponentsInChildren<Transform>(true).Where(t => DropChildren.Contains(t.name)).ToList())
                    if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            // Дальше радиуса — долой (дома остаются горизонтом), кроме целого корня keepWhole (пути и поезда длинные).
            if (!float.IsInfinity(radius))
                foreach (var r in scene.GetRootGameObjects())
                {
                    if (r.name == keepWhole) continue;
                    Prune(r.transform, pivot, radius);
                }
            foreach (var r in scene.GetRootGameObjects())
            {
                bool trains = r.name == keepWhole;
                foreach (var b in r.GetComponentsInChildren<MonoBehaviour>(true))
                    if (b != null && DropBehaviours.Contains(b.GetType().Name)) Object.DestroyImmediate(b);
                foreach (var c in r.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                if (!trains) foreach (var rb in r.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                foreach (var cam in r.GetComponentsInChildren<Camera>(true)) { var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); if (data != null) Object.DestroyImmediate(data); Object.DestroyImmediate(cam); }
                foreach (var l in r.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(l);
            }
            int after = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Renderer>(true).Length);
            Save(scene, target);
            return $"{target}: из {Path.GetFileNameWithoutExtension(sourcePath)}, рендереров {after} из {before}";
        }

        /// <summary>Удалить поддеревья, которые целиком дальше <paramref name="radius"/> по горизонтали; дома не трогать.</summary>
        static void Prune(Transform t, Vector3 pivot, float radius)
        {
            if (t.name == "Houses") return;
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var p = new Vector3(pivot.x, b.center.y, pivot.z);
            if (b.SqrDistance(p) > radius * radius && t.parent != null) { Object.DestroyImmediate(t.gameObject); return; }
            if (b.SqrDistance(p) <= 0.01f && renderers.Length == 1) return;   // сам объект у точки
            foreach (Transform c in t.Cast<Transform>().ToList()) Prune(c, pivot, radius);
        }

        // ------------------------------------------------------------------ helpers

        static void Save(Scene scene, string name)
        {
            string path = Scenes + name + ".unity";
            EditorSceneManager.SaveScene(scene, path);
        }

        static void AddToBuild(params string[] names)
        {
            var list = EditorBuildSettings.scenes.ToList();
            foreach (var n in names)
            {
                string path = Scenes + n + ".unity";
                int i = list.FindIndex(s => s.path == path);
                if (i < 0) list.Add(new EditorBuildSettingsScene(path, true));
                else if (!list[i].enabled) list[i] = new EditorBuildSettingsScene(path, true);
            }
            EditorBuildSettings.scenes = list.ToArray();
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = pos; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static Material Mat(string name, Color color, float smoothness, float emission = 0f)
        {
            string path = Mats + "/Garage_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * emission); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Light(string name, LightType type, Vector3 euler, Vector3 pos, float intensity, Color color, bool shadows, float range = 10f, float spot = 30f)
        {
            var go = new GameObject("Light / " + name);
            var l = go.AddComponent<Light>();
            l.type = type; l.intensity = intensity; l.color = color;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (type == LightType.Spot) { l.range = range; l.spotAngle = spot; }
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
        }
    }
}
