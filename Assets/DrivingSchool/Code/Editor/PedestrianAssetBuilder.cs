using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrivingSchool.Editor
{
    /// <summary>Imports only the dedicated pedestrian assets; never rebuilds the existing levels.</summary>
    public static class PedestrianAssetBuilder
    {
        const string Root = "Assets/DrivingSchool";
        const string Output = Root + "/Prefabs/Pedestrians";
        const string Materials = Root + "/Materials/Pedestrians";

        sealed class Spec
        {
            public readonly string Name; public readonly float MinHeight, MaxHeight, Radius; public readonly bool Police;
            public Spec(string name, float minHeight, float maxHeight, float radius, bool police = false)
            { Name = name; MinHeight = minHeight; MaxHeight = maxHeight; Radius = radius; Police = police; }
            public string[] Clips => Police ? PoliceClips : CivilClips;
        }

        // Must match CHARACTERS and the clip lists in tools/build_pedestrians.py.
        static readonly Spec[] Specs =
        {
            new Spec("DS_Pedestrian_A", 1.65f, 1.9f, .24f),
            new Spec("DS_Pedestrian_B", 1.65f, 1.9f, .24f),
            new Spec("DS_Pedestrian_C", 1.65f, 1.9f, .24f),
            new Spec("DS_Pedestrian_Child_A", 1.1f, 1.4f, .18f),
            new Spec("DS_Pedestrian_Child_B", 1.1f, 1.4f, .18f),
            new Spec("DS_Pedestrian_Police", 1.7f, 1.95f, .24f, police: true),
        };
        static readonly string[] CivilClips = { "Idle", "Walk", "Run", "LookAround" };
        static readonly string[] PoliceClips = { "Idle", "Walk", "LookAround", "Signal_ArmsSide", "Signal_RightArmForward", "Signal_ArmUp" };

        [MenuItem("Driving School/Pedestrians/Import models and create showroom")]
        public static void Build()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory("artifacts/reports");
            Directory.CreateDirectory("artifacts/visual-review/pedestrians");
            AssetDatabase.Refresh();
            var report = new System.Text.StringBuilder();
            var prefabs = new GameObject[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
                prefabs[i] = BuildOne(Specs[i], report);
            AssetDatabase.SaveAssets();
            CreateShowroom(prefabs);
            File.WriteAllText("artifacts/reports/pedestrians-unity.txt", report +
                "PASS: Unity import, URP materials, rig, clips, natural clip speeds and sampled ground contact of every clip.\n" +
                "No navigation or traffic AI verified.\n");
            Debug.Log("PEDESTRIAN_IMPORT_PASS");
        }

        static GameObject BuildOne(Spec spec, System.Text.StringBuilder report)
        {
            string name = spec.Name;
            string path = Root + "/Art/Pedestrians/" + name + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new FileNotFoundException(path);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            // Drop remaps from earlier builds (material names changed when the generator stopped sharing them).
            foreach (var old in importer.GetExternalObjectMap().Keys.ToArray()) importer.RemoveRemap(old);
            importer.SaveAndReimport();
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = c.takeName.Split('|').Last();
                c.loopTime = true;
                c.loopPose = true;
                c.lockRootPositionXZ = true;
                c.lockRootRotation = true;
                c.keepOriginalPositionY = true;
            }
            var names = clips.Select(c => c.name).OrderBy(n => n).ToArray();
            if (!names.SequenceEqual(spec.Clips.OrderBy(n => n)))
                throw new InvalidOperationException(name + ": clips " + string.Join(",", names) + ", expected " + string.Join(",", spec.Clips));
            importer.clipAnimations = clips;
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                string materialPath = Materials + "/" + name + "_" + source.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.color : Color.gray);
                // Reflective vest bands and the cap badge read as satin, the rest as cloth and skin.
                material.SetFloat("_Smoothness", source.name.Contains("Reflect") || source.name.Contains("Badge") ? .55f : .22f);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), material);
                EditorUtility.SetDirty(material);
            }
            importer.SaveAndReimport();
            var animations = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);

            var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel);
            instance.name = name;
            try
            {
                var skin = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                if (skin.Length == 0) throw new InvalidOperationException(name + ": no skinned mesh");
                var bounds = skin[0].bounds;
                foreach (var r in skin) bounds.Encapsulate(r.bounds);
                if (bounds.size.y < spec.MinHeight || bounds.size.y > spec.MaxHeight)
                    throw new InvalidOperationException(name + ": unexpected imported height " + bounds.size);
                foreach (var r in skin)
                {
                    // Arm swing and raised-arm gestures extend beyond bind-pose bounds. Keep a conservative local bound.
                    var b = r.localBounds; b.Expand(1.2f); r.localBounds = b;
                    if (r.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                        throw new InvalidOperationException(name + ": invalid material remap");
                }
                report.AppendLine(name + ": bounds=" + bounds.size.ToString("F3") + "; renderers=" + skin.Length);

                float walkSpeed = NaturalSpeed(instance, animations["Walk"]);
                float runSpeed = animations.TryGetValue("Run", out var run) ? NaturalSpeed(instance, run) : 0;
                report.AppendLine("  natural speed: Walk " + walkSpeed.ToString("F2") + " m/s" +
                    (runSpeed > 0 ? ", Run " + runSpeed.ToString("F2") + " m/s" : ""));
                if (walkSpeed < .6f || walkSpeed > 2.2f || (runSpeed > 0 && runSpeed < walkSpeed * 1.5f))
                    throw new InvalidOperationException(name + ": implausible clip speeds");
                foreach (var clip in spec.Clips)
                    CheckGround(name, instance, skin, animations[clip], report);

                var controller = BuildController(name, spec, animations, walkSpeed, runSpeed);
                var animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                var driver = instance.GetComponent<PedestrianAnimator>();
                if (driver == null) driver = instance.AddComponent<PedestrianAnimator>();
                driver.Configure(walkSpeed, runSpeed, spec.Police);
                var capsule = instance.GetComponent<CapsuleCollider>();
                if (capsule == null) capsule = instance.AddComponent<CapsuleCollider>();
                capsule.height = bounds.size.y; capsule.radius = spec.Radius;
                capsule.center = new Vector3(0, bounds.size.y / 2, 0);
                // Sampling moved the bones; save the prefab in its bind pose.
                animations["Idle"].SampleAnimation(instance, 0);
                return PrefabUtility.SaveAsPrefabAsset(instance, Output + "/" + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        static AnimatorController BuildController(string name, Spec spec, Dictionary<string, AnimationClip> clips,
            float walkSpeed, float runSpeed)
        {
            // Recreated each build so no orphaned blend trees or states accumulate inside the asset.
            string controllerPath = Output + "/" + name + ".controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter(PedestrianAnimator.SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(PedestrianAnimator.LookAroundParameter, AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;

            // Speed thresholds equal the clips' natural speeds, so feet do not slide inside the blend.
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = PedestrianAnimator.SpeedParameter;
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Idle"], 0);
            tree.AddChild(clips["Walk"], walkSpeed);
            if (runSpeed > 0) tree.AddChild(clips["Run"], runSpeed);
            machine.defaultState = locomotion;

            var look = machine.AddState("LookAround");
            look.motion = clips["LookAround"];
            var toLook = locomotion.AddTransition(look);
            toLook.hasExitTime = false; toLook.duration = .25f;
            toLook.AddCondition(AnimatorConditionMode.If, 0, PedestrianAnimator.LookAroundParameter);
            toLook.AddCondition(AnimatorConditionMode.Less, .1f, PedestrianAnimator.SpeedParameter);
            var lookEnd = look.AddTransition(locomotion);
            lookEnd.hasExitTime = false; lookEnd.duration = .25f;
            lookEnd.AddCondition(AnimatorConditionMode.IfNot, 0, PedestrianAnimator.LookAroundParameter);
            var lookWalk = look.AddTransition(locomotion);
            lookWalk.hasExitTime = false; lookWalk.duration = .2f;
            lookWalk.AddCondition(AnimatorConditionMode.Greater, .1f, PedestrianAnimator.SpeedParameter);

            if (spec.Police)
            {
                controller.AddParameter(PedestrianAnimator.SignalParameter, AnimatorControllerParameterType.Int);
                foreach (RegulatorSignal signal in Enum.GetValues(typeof(RegulatorSignal)))
                {
                    if (signal == RegulatorSignal.None) continue;
                    var state = machine.AddState("Signal_" + signal);
                    state.motion = clips["Signal_" + signal];
                    var enter = machine.AddAnyStateTransition(state);
                    enter.canTransitionToSelf = false; enter.hasExitTime = false; enter.duration = .35f;
                    enter.AddCondition(AnimatorConditionMode.Equals, (int)signal, PedestrianAnimator.SignalParameter);
                    var leave = state.AddTransition(locomotion);
                    leave.hasExitTime = false; leave.duration = .35f;
                    leave.AddCondition(AnimatorConditionMode.Equals, (int)RegulatorSignal.None, PedestrianAnimator.SignalParameter);
                }
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static Transform Bone(GameObject instance, string bone) =>
            instance.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == bone)
            ?? throw new InvalidOperationException(instance.name + ": bone " + bone + " not found");

        /// <summary>
        /// In-place cycle: a planted foot slides back (-Z) at the speed the body would travel. Median over
        /// contact intervals (lower ankle within 3 cm of its lowest height), as in tools/build_pedestrians.py.
        /// </summary>
        static float NaturalSpeed(GameObject instance, AnimationClip clip)
        {
            var feet = new[] { Bone(instance, "Foot_L"), Bone(instance, "Foot_R") };
            const int samples = 60;
            var track = new List<Vector3[]>();
            for (int f = 0; f <= samples; f++)
            {
                clip.SampleAnimation(instance, clip.length * f / samples);
                track.Add(feet.Select(t => t.position).ToArray());
            }
            float floor = track.SelectMany(p => p).Min(p => p.y);
            var rates = new List<float>();
            for (int f = 1; f < track.Count; f++)
            {
                var a = track[f - 1]; var b = track[f];
                int planted = a[0].y < a[1].y ? 0 : 1;
                if (a[planted].y < floor + .03f && b[planted].y < floor + .03f)
                    rates.Add((a[planted].z - b[planted].z) * samples / clip.length);
            }
            if (rates.Count == 0) throw new InvalidOperationException(instance.name + ": no foot contact in " + clip.name);
            rates.Sort();
            return rates[rates.Count / 2];
        }

        /// <summary>Samples a clip and verifies every animated vertex stays finite with the lowest one on the floor.</summary>
        static void CheckGround(string name, GameObject instance, SkinnedMeshRenderer[] skin, AnimationClip clip,
            System.Text.StringBuilder report)
        {
            float minGround = float.PositiveInfinity, maxGround = float.NegativeInfinity;
            var mesh = new Mesh();
            try
            {
                for (int f = 0; f <= 30; f++)
                {
                    clip.SampleAnimation(instance, clip.length * f / 30);
                    float ground = float.PositiveInfinity;
                    foreach (var r in skin)
                    {
                        r.BakeMesh(mesh);
                        foreach (var v in mesh.vertices)
                        {
                            var world = r.transform.TransformPoint(v);
                            if (float.IsNaN(world.y) || float.IsInfinity(world.y))
                                throw new InvalidOperationException(name + ": invalid animated vertex in " + clip.name);
                            ground = Mathf.Min(ground, world.y);
                        }
                    }
                    minGround = Mathf.Min(minGround, ground); maxGround = Mathf.Max(maxGround, ground);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            report.AppendLine("  " + clip.name + " " + clip.length.ToString("F2") + "s floor range, 31 samples: " +
                minGround.ToString("F4") + " .. " + maxGround.ToString("F4") + " m");
            // Run has a short flight phase after each toe-off.
            if (minGround < -.025f || maxGround > (clip.name == "Run" ? .08f : .04f))
                throw new InvalidOperationException(name + ": " + clip.name + " ground contact outside tolerance");
        }

        static void CreateShowroom(GameObject[] prefabs)
        {
            // Additive scene keeps any currently open user scene untouched.
            var previous = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);
            try
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    var o = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                    o.transform.position = new Vector3((i - (prefabs.Length - 1) / 2f) * .95f, 0, 0);
                }
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 2.2f;
                sun.transform.rotation = Quaternion.Euler(40, -35, 0); sun.shadows = LightShadows.Soft;
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f,.6f,.65f);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Pedestrian display floor";
                string floorPath = Materials + "/Stage.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(floorPath);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.SetColor("_BaseColor",new Color(.14f,.19f,.21f)); AssetDatabase.CreateAsset(mat,floorPath);
                }
                floor.GetComponent<Renderer>().sharedMaterial = mat;
                var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
                camera.tag = "MainCamera"; camera.transform.position = new Vector3(2.5f,2.4f,8.5f);
                camera.transform.LookAt(new Vector3(0,.85f,0)); camera.fieldOfView = 36;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.14f,.19f,.21f);
                EditorSceneManager.SaveScene(scene, Root + "/Scenes/PedestrianShowroom.unity");
                var target = new RenderTexture(960,720,24);
                var pixels = new Texture2D(960,720,TextureFormat.RGB24,false);
                var old = RenderTexture.active;
                try
                {
                    camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0,0,960,720),0,0); pixels.Apply();
                    File.WriteAllBytes("artifacts/visual-review/pedestrians/unity-lineup.png",pixels.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null; RenderTexture.active = old;
                    target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
            }
        }
    }
}
