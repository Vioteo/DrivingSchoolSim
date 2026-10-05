using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Imports the environment kit (EN_*, tools/build_env_kit.py, docs/env-kit.md): URP materials from
    /// EN_Materials.json (textures, alpha cutout for foliage, two-sided cards), prefabs (identity root +
    /// "Model" child turned by Socket_Front/Socket_Up), LOD groups from *_LOD0..2, colliders from COL_*,
    /// a demo scene and captures in day / dusk / night light (art-pipeline.md, acceptance item 5).
    /// Writes only its own materials, prefabs, demo scene and reports.
    /// </summary>
    public static class EnvKitBuilder
    {
        const string Art = "Assets/DrivingSchool/Art/EnvKit";
        const string Prefabs = "Assets/DrivingSchool/Prefabs/EnvKit";
        const string Materials = "Assets/DrivingSchool/Materials/EnvKit";
        const string Demo = "Assets/DrivingSchool/Scenes/EnvKit_Demo.unity";
        const string GroundMesh = "Assets/DrivingSchool/Scenes/EnvKit_Demo_Ground.asset";
        const string Manifest = "artifacts/reports/envkit-manifest.json";
        const string Report = "artifacts/reports/envkit-unity.json";
        const string Evidence = "artifacts/visual-review/envkit/unity";

        [Serializable] class MaterialEntry
        {
            public string name; public float[] color; public float metallic; public float roughness; public float emission;
            public string texture; public float[] tint; public bool alphaClip; public bool doubleSided;
        }
        [Serializable] class MaterialList { public MaterialEntry[] materials; }
        [Serializable] class ManifestAsset { public string id; public string kind; public float[] size; public float[] bounds_min; }
        [Serializable] class ManifestFile { public ManifestAsset[] assets; }

        [Serializable] class ImportResult
        {
            public string name; public Vector3 boundsMetres; public Vector3 expectedMetres; public int lods;
            public int colliders; public bool pass; public string note;
        }
        [Serializable] class ImportReport { public string unityVersion; public string checkedUtc; public bool passed; public ImportResult[] assets; }

        [MenuItem("Driving School/Env Kit/Import and build demo")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var dir in new[] { Prefabs, Materials, Evidence, "artifacts/reports" }) Directory.CreateDirectory(dir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var manifest = JsonUtility.FromJson<ManifestFile>(File.ReadAllText(Manifest)).assets.ToDictionary(a => a.id);
            ImportTextures();
            CreateMaterials();
            var results = new List<ImportResult>();
            var report = new ImportReport { unityVersion = Application.unityVersion, checkedUtc = DateTime.UtcNow.ToString("O") };
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                foreach (var path in Directory.GetFiles(Art, "EN_*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (!manifest.TryGetValue(name, out var entry)) throw new InvalidOperationException(name + " missing in " + Manifest);
                    Import(path, name);
                    results.Add(BuildPrefab(path, name, entry));
                }
                CreateDemo();
                report.assets = results.ToArray();
                report.passed = results.All(r => r.pass);
                File.WriteAllText(Report, JsonUtility.ToJson(report, true));
                EditorSceneManager.SaveScene(scene, Demo);
                AssetDatabase.SaveAssets();
                if (!report.passed) throw new InvalidOperationException("Env kit validation failed. See " + Report);
                Debug.Log("ENVKIT_UNITY_PASS: " + results.Count + " prefabs; axes, bounds, LOD groups and colliders verified.");
            }
            catch
            {
                report.assets = results.ToArray();
                File.WriteAllText(Report, JsonUtility.ToJson(report, true));
                throw;
            }
        }

        static void ImportTextures()
        {
            foreach (var path in Directory.GetFiles(Art + "/Textures", "T_*.png").Select(p => p.Replace('\\', '/')))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) continue;
                string n = Path.GetFileNameWithoutExtension(path);
                bool alpha = n.StartsWith("T_Leaves") || n.StartsWith("T_Needles") || n.StartsWith("T_GrassBlades");
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = alpha;
                ti.mipmapEnabled = true;
                ti.mipMapsPreserveCoverage = alpha;      // cutout foliage keeps its density in the distance
                ti.alphaTestReferenceValue = .5f;
                ti.wrapMode = n.StartsWith("T_Sign") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.anisoLevel = n.StartsWith("T_Asphalt") || n.StartsWith("T_Sign") ? 8 : 2;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
        }

        static void CreateMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader unavailable.");
            var list = JsonUtility.FromJson<MaterialList>(File.ReadAllText(Art + "/EN_Materials.json"));
            foreach (var m in list.materials)
            {
                string path = Materials + "/" + m.name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(shader) { name = m.name }; AssetDatabase.CreateAsset(mat, path); }
                // Palette colours are linear (Blender); URP material colours are authored in gamma space.
                var color = new Color(m.color[0], m.color[1], m.color[2], m.color[3]).gamma;
                color.a = m.color[3];
                Texture2D tex = string.IsNullOrEmpty(m.texture) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/" + m.texture);
                if (!string.IsNullOrEmpty(m.texture) && tex == null) throw new InvalidOperationException("Missing texture " + m.texture);
                mat.SetTexture("_BaseMap", tex);
                if (tex != null)
                {
                    var t = m.tint != null && m.tint.Length >= 3 ? new Color(m.tint[0], m.tint[1], m.tint[2]) : Color.white;
                    mat.SetColor("_BaseColor", new Color(Mathf.Clamp01(t.r), Mathf.Clamp01(t.g), Mathf.Clamp01(t.b), 1));
                }
                else mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", m.metallic);
                mat.SetFloat("_Smoothness", 1 - m.roughness);
                mat.SetFloat("_AlphaClip", m.alphaClip ? 1 : 0);
                mat.SetFloat("_Cutoff", .5f);
                if (m.alphaClip) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", m.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
                mat.doubleSidedGI = m.doubleSided;
                if (m.alphaClip) mat.renderQueue = (int)RenderQueue.AlphaTest;
                if (m.emission > 0)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    mat.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * m.emission);
                }
                else mat.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        static void Import(string path, string name)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importNormals = ModelImporterNormals.Import;          // foliage cards carry crown normals
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();                                     // scale/axes: EnvKitImportSettings
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                var target = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/" + source.name + ".mat");
                if (target == null) throw new InvalidOperationException("Missing env kit material: " + source.name);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), target);
            }
            importer.SaveAndReimport();
        }

        static ImportResult BuildPrefab(string path, string name, ManifestAsset entry)
        {
            var root = new GameObject(name);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
                model.name = "Model";
                var nodes = model.GetComponentsInChildren<Transform>(true);
                Transform Node(string n) => nodes.First(t => t.name == n || t.name.StartsWith(n + ".", StringComparison.Ordinal));
                Vector3 forward = Node("Socket_Front").position - model.transform.position;
                Vector3 up = Node("Socket_Up").position - model.transform.position;
                if (Mathf.Abs(forward.magnitude - 1) > .001f || Mathf.Abs(up.magnitude - 1) > .001f)
                    throw new InvalidOperationException(name + ": FBX is not in metres (Socket_Front/Up not 1 m from the root).");
                // The front must be +Z and up +Y; only the Model child is turned, the root stays identity.
                model.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * model.transform.rotation;
                var notes = new List<string>();
                if (Vector3.Dot((Node("Socket_Front").position - root.transform.position).normalized, Vector3.forward) < .999f) notes.Add("front is not +Z");

                bool moving = false;
                int drive = LayerMask.NameToLayer("DriveSurface"), solid = LayerMask.NameToLayer("SolidProp");
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    filter.gameObject.isStatic = !moving;
                    if (!filter.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                    filter.GetComponent<MeshRenderer>().enabled = false;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    int layer = filter.name.StartsWith("COL_DriveSurface", StringComparison.Ordinal) ? drive : solid;
                    if (layer >= 0) filter.gameObject.layer = layer;
                }
                // shadows: foliage cards cast, grass and markings do not
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name.EndsWith("_Markings", StringComparison.Ordinal) || r.name.EndsWith("_Patches", StringComparison.Ordinal) ||
                        name.StartsWith("EN_Grass_Clump", StringComparison.Ordinal))
                        r.shadowCastingMode = ShadowCastingMode.Off;
                }
                // LOD group from *_LOD0.._LOD2
                var lodRenderers = Enumerable.Range(0, 3)
                    .Select(i => model.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.EndsWith("_LOD" + i, StringComparison.Ordinal)).ToArray())
                    .Where(a => a.Length > 0).ToArray();
                if (lodRenderers.Length > 1)
                {
                    bool tree = name.StartsWith("EN_Tree", StringComparison.Ordinal);
                    float[] heights = tree ? new[] { .30f, .10f, .015f } : new[] { .25f, .06f, .01f };
                    if (lodRenderers.Length == 2) heights = new[] { heights[0], heights[2] };
                    var lods = lodRenderers.Select((rs, i) => new LOD(heights[i], rs)).ToArray();
                    var group = root.AddComponent<LODGroup>();
                    group.SetLODs(lods);
                    group.fadeMode = LODFadeMode.CrossFade;
                    group.animateCrossFading = true;
                    group.RecalculateBounds();
                }
                var renderers = root.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && !r.name.Contains("_LOD1") && !r.name.Contains("_LOD2")).ToArray();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                var expected = new Vector3(entry.size[0], entry.size[2], entry.size[1]);
                var result = new ImportResult
                {
                    name = name, boundsMetres = bounds.size, expectedMetres = expected, lods = lodRenderers.Length,
                    colliders = root.GetComponentsInChildren<Collider>().Length,
                };
                if (Vector3.Distance(bounds.size, expected) > .02f) notes.Add("bounds differ from the Blender manifest");
                bool needsCollider = !(name.StartsWith("EN_Bush") || name.StartsWith("EN_Grass") || name.StartsWith("EN_Yard_TireFlowerbed"));
                if (needsCollider && result.colliders == 0) notes.Add("no collider");
                result.pass = notes.Count == 0;
                result.note = string.Join("; ", notes);
                PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + name + ".prefab");
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------ demo scene
        static GameObject Place(string asset, string label, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + asset + ".prefab");
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = label;
            go.transform.SetPositionAndRotation(position, rotation);
            if (parent) go.transform.SetParent(parent, true);
            return go;
        }

        static Transform Socket(GameObject go, string n) =>
            go.GetComponentsInChildren<Transform>(true).First(t => t.name == n || t.name.StartsWith(n + ".", StringComparison.Ordinal));

        static void CreateDemo()
        {
            var roads = new GameObject("Country Road").transform;
            var pos = new Vector3(0, 0, -96);
            var rot = Quaternion.identity;
            var chain = new[] { "EN_Road_Country2_24m", "EN_Road_Country2_24m", "EN_Road_Country2_24m_Approach", "EN_Road_Country2_24m_Approach",
                                "EN_Road_Country2_Curve90_R50", "EN_Road_Country2_24m_Solid", "EN_Road_Country2_24m",
                                "EN_Road_Country2_Curve30_R160", "EN_Road_Country2_24m" };
            int k = 0;
            foreach (var id in chain)
            {
                var go = Place(id, $"{k++:00}_{id}", pos, rot, roads);
                if (go == null) throw new InvalidOperationException("Missing prefab " + id);
                if (id == "EN_Road_Country2_Curve90_R50") Place(id + "_Furniture", "Curve_Furniture", pos, rot, roads);
                Physics.SyncTransforms();
                pos = Socket(go, "Socket_End").position;
                // exit heading: right-hand curves turn clockwise seen from above (+Y rotation in Unity)
                float turn = id.Contains("Curve90") ? 90f : id.Contains("Curve30") ? 30f : 0f;
                rot = rot * Quaternion.Euler(0, turn, 0);
            }
            Physics.SyncTransforms();
            Place("EN_Sign_1_11_1_CurveRight", "Sign_1_11_1", new Vector3(6.9f, 0, -85), Quaternion.Euler(0, 180, 0), roads);
            Place("EN_Sign_3_20_NoOvertaking", "Sign_3_20", new Vector3(6.9f, 0, -55), Quaternion.Euler(0, 180, 0), roads);
            // forest on both sides
            var rnd = new System.Random(7);
            var forest = new GameObject("Forest").transform;
            string[] trees = { "EN_Tree_Spruce_A", "EN_Tree_Spruce_A", "EN_Tree_Spruce_B", "EN_Tree_Birch_A", "EN_Tree_Birch_B", "EN_Tree_Pine_A", "EN_Tree_Pine_B", "EN_Tree_Spruce_Young", "EN_Bush_A" };
            for (int i = 0; i < 900; i++)
            {
                var p = new Vector3((float)rnd.NextDouble() * 360 - 120, 0, (float)rnd.NextDouble() * 320 - 110);
                if (NearRoad(p, 3f)) continue;
                Place(trees[rnd.Next(trees.Length)], "Tree", p, Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0), forest);
            }
            CreateGround();
            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft;
            var cam = new GameObject("Demo Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera"; cam.nearClipPlane = .1f; cam.farClipPlane = 800; cam.fieldOfView = 55;
            cam.GetUniversalAdditionalCameraData();
            ApplyPreset("day");
        }

        /// <summary>
        /// Flat grass at Y = -0.03 around the road. The road modules carry their own shoulders, ditches and
        /// verges out to 12.5 m, so the ground leaves them out: a 1 m grid near the road skips every cell whose
        /// corners all lie over COL_DriveSurface (a plain plane would cover the shoulders with grass).
        /// </summary>
        static void CreateGround()
        {
            Physics.SyncTransforms();
            var roads = UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
                .Where(c => c.name.StartsWith("COL_DriveSurface", StringComparison.Ordinal)).ToArray();
            var b = roads[0].bounds;
            foreach (var c in roads.Skip(1)) b.Encapsulate(c.bounds);
            b.Expand(new Vector3(80, 0, 80));
            const float cell = 1f, y = -.03f, far = 600f, tile = 2f;     // EN_Grass texture: 2 m tile
            int nx = Mathf.CeilToInt(b.size.x / cell), nz = Mathf.CeilToInt(b.size.z / cell);
            float x0 = b.min.x, z0 = b.min.z, x1 = x0 + nx * cell, z1 = z0 + nz * cell;
            var verts = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            var inside = new bool[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int k = 0; k <= nz; k++)
                {
                    var p = new Vector3(x0 + i * cell, y, z0 + k * cell);
                    verts.Add(p); uv.Add(new Vector2(p.x, p.z) / tile);
                    inside[i, k] = Physics.RaycastAll(p + Vector3.up * 60, Vector3.down, 120)
                        .Any(h => h.collider.name.StartsWith("COL_DriveSurface", StringComparison.Ordinal));
                }
            int V(int i, int k) => i * (nz + 1) + k;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    if (inside[i, k] && inside[i + 1, k] && inside[i, k + 1] && inside[i + 1, k + 1]) continue;
                    tris.AddRange(new[] { V(i, k), V(i, k + 1), V(i + 1, k + 1), V(i, k), V(i + 1, k + 1), V(i + 1, k) });
                }
            void Quad(float ax, float az, float bx, float bz)
            {
                int o = verts.Count;
                foreach (var c in new[] { new Vector2(ax, az), new Vector2(ax, bz), new Vector2(bx, bz), new Vector2(bx, az) })
                { verts.Add(new Vector3(c.x, y, c.y)); uv.Add(c / tile); }
                tris.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
            }
            Quad(-far, -far, far, z0); Quad(-far, z1, far, far); Quad(-far, z0, x0, z1); Quad(x1, z0, far, z1);
            var mesh = new Mesh { name = "EN_DemoGround", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, GroundMesh);
            var ground = new GameObject("Ground", typeof(MeshFilter), typeof(MeshRenderer));
            ground.GetComponent<MeshFilter>().sharedMesh = mesh;
            ground.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/EN_Grass.mat");
            ground.isStatic = true;
        }

        /// <summary>True if p or a point within `margin` of it is over a road module (25 m wide incl. ditches).</summary>
        static bool NearRoad(Vector3 p, float margin)
        {
            foreach (var o in new[] { Vector3.zero, Vector3.right * margin, Vector3.left * margin, Vector3.forward * margin, Vector3.back * margin })
                foreach (var hit in Physics.RaycastAll(p + o + Vector3.up * 50, Vector3.down, 100))
                    if (hit.collider.name.StartsWith("COL_DriveSurface", StringComparison.Ordinal)) return true;
            return false;
        }

        // ------------------------------------------------------------ lighting presets and captures
        static void ApplyPreset(string preset)
        {
            var sun = GameObject.Find("Sun").GetComponent<Light>();
            var cam = Camera.main;
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.name.StartsWith("Artificial")))
                l.enabled = preset != "day";
            RenderSettings.ambientMode = AmbientMode.Trilight;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // reflections come from the default (daylight) sky: dim them with the light,
            // otherwise metal and foliage glow white at night
            RenderSettings.reflectionIntensity = preset == "day" ? 1f : preset == "dusk" ? .3f : .04f;
            switch (preset)
            {
                case "day":
                    sun.intensity = 1.6f; sun.color = new Color(1f, .96f, .9f); sun.transform.rotation = Quaternion.Euler(38, 150, 0);
                    RenderSettings.ambientSkyColor = new Color(.55f, .65f, .8f); RenderSettings.ambientEquatorColor = new Color(.45f, .5f, .5f);
                    RenderSettings.ambientGroundColor = new Color(.2f, .22f, .18f); cam.backgroundColor = new Color(.62f, .74f, .88f);
                    break;
                case "dusk":
                    sun.intensity = .55f; sun.color = new Color(1f, .55f, .3f); sun.transform.rotation = Quaternion.Euler(3, 150, 0);
                    RenderSettings.ambientSkyColor = new Color(.22f, .22f, .32f); RenderSettings.ambientEquatorColor = new Color(.3f, .2f, .18f);
                    RenderSettings.ambientGroundColor = new Color(.05f, .05f, .05f); cam.backgroundColor = new Color(.42f, .32f, .36f);
                    break;
                default:
                    sun.intensity = .08f; sun.color = new Color(.62f, .72f, 1f); sun.transform.rotation = Quaternion.Euler(35, 220, 0);
                    RenderSettings.ambientSkyColor = new Color(.02f, .025f, .045f); RenderSettings.ambientEquatorColor = new Color(.012f, .014f, .02f);
                    RenderSettings.ambientGroundColor = new Color(.004f, .004f, .005f); cam.backgroundColor = new Color(.01f, .012f, .025f);
                    break;
            }
        }

        static void Headlights(Vector3 carPos, Vector3 forward)
        {
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            foreach (int s in new[] { -1, 1 })
            {
                var l = new GameObject("Artificial_Headlight", typeof(Light)).GetComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 55; l.innerSpotAngle = 25; l.range = 70; l.intensity = 60;
                l.color = new Color(1f, .95f, .88f); l.shadows = LightShadows.Soft;
                l.transform.position = carPos + forward * 2f + right * s * .62f + Vector3.up * .68f;
                l.transform.rotation = Quaternion.LookRotation(forward + Vector3.down * .06f);
            }
        }

        /// <summary>Screenshots of the demo scene in day, dusk and night light (run without -nographics).</summary>
        [MenuItem("Driving School/Env Kit/Capture day, dusk, night")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene(Demo);
            var cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("Env kit demo camera missing.");
            Directory.CreateDirectory(Evidence);
            // own car before the curve, dipped beams
            Headlights(new Vector3(1.75f, 0, -24), Vector3.forward);
            var shots = new (string name, Vector3 pos, Vector3 look)[]
            {
                ("curve-driver", new Vector3(1.75f, 1.3f, -22), new Vector3(8, .8f, 30)),
                ("curve-aerial", new Vector3(-48, 46, -52), new Vector3(24, 0, 26)),
                ("forest-edge", new Vector3(14, 3, -60), new Vector3(30, 7, -40)),
            };
            foreach (var preset in new[] { "day", "dusk", "night" })
            {
                ApplyPreset(preset);
                foreach (var s in shots)
                {
                    cam.transform.position = s.pos; cam.transform.LookAt(s.look);
                    Shot(cam, $"{Evidence}/{s.name}-{preset}.png");
                }
            }
            Debug.Log("ENVKIT_UNITY_CAPTURED");
        }

        static void Shot(Camera camera, string file)
        {
            var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
                File.WriteAllBytes(file, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
