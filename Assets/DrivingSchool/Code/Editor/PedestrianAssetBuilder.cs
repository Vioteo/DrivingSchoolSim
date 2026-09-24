using System;
using System.IO;
using System.Linq;
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

        [MenuItem("Driving School/Pedestrians/Import models and create showroom")]
        public static void Build()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Materials);
            Directory.CreateDirectory("artifacts/reports");
            Directory.CreateDirectory("artifacts/visual-review/pedestrians");
            AssetDatabase.Refresh();
            var report = new System.Text.StringBuilder();
            var prefabs = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                string name = "DS_Pedestrian_" + (char)('A' + i);
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
                importer.SaveAndReimport();
                var clips = importer.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = c.name.Contains("Walk") ? "Walk" : c.name.Contains("Idle") ? "Idle" : c.name;
                    c.loopTime = true;
                    c.loopPose = true;
                    c.lockRootPositionXZ = true;
                    c.lockRootRotation = true;
                    c.keepOriginalPositionY = true;
                }
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
                    material.SetFloat("_Smoothness", .22f);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(source), material);
                    EditorUtility.SetDirty(material);
                }
                importer.SaveAndReimport();
                var animations = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).ToArray();
                var idle = animations.Single(c => c.name == "Idle");
                var walk = animations.Single(c => c.name == "Walk");
                string controllerPath = Output + "/" + name + ".controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                    controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                controller.parameters = new[] { new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float } };
                if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
                var machine = controller.layers[0].stateMachine;
                foreach (var s in machine.states) machine.RemoveState(s.state);
                var idleState = machine.AddState("Idle"); idleState.motion = idle;
                var walkState = machine.AddState("Walk"); walkState.motion = walk;
                machine.defaultState = idleState;
                var start = idleState.AddTransition(walkState); start.hasExitTime = false; start.duration = .18f;
                start.AddCondition(AnimatorConditionMode.Greater, .1f, "Speed");
                var stop = walkState.AddTransition(idleState); stop.hasExitTime = false; stop.duration = .18f;
                stop.AddCondition(AnimatorConditionMode.Less, .1f, "Speed");
                EditorUtility.SetDirty(controller);

                var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceModel);
                instance.name = name;
                try
                {
                    var animator = instance.GetComponent<Animator>();
                    if (animator == null) animator = instance.AddComponent<Animator>();
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    var skin = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
                    if (skin.Length == 0) throw new InvalidOperationException(name + ": no skinned mesh");
                    var bounds = skin[0].bounds;
                    foreach (var r in skin) bounds.Encapsulate(r.bounds);
                    if (bounds.size.y < 1.65f || bounds.size.y > 1.9f)
                        throw new InvalidOperationException(name + ": unexpected imported height " + bounds.size);
                    foreach (var r in skin)
                    {
                        // Arm swing extends beyond bind-pose depth. Keep a conservative local bound.
                        var b = r.localBounds; b.Expand(.8f); r.localBounds = b;
                        if (r.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                            throw new InvalidOperationException(name + ": invalid material remap");
                    }
                    var capsule = instance.GetComponent<CapsuleCollider>();
                    if (capsule == null) capsule = instance.AddComponent<CapsuleCollider>();
                    capsule.height = bounds.size.y; capsule.radius = .24f;
                    capsule.center = new Vector3(0, bounds.size.y / 2, 0);
                    prefabs[i] = PrefabUtility.SaveAsPrefabAsset(instance, Output + "/" + name + ".prefab");
                    report.AppendLine(name + ": bounds=" + bounds.size.ToString("F3") +
                        "; renderers=" + skin.Length + "; clips=" + idle.name + " " + idle.length + "s, " + walk.name + " " + walk.length + "s");
                    // Sample imported animation and verify every sampled mesh remains finite and near ground.
                    float minGround = float.PositiveInfinity, maxGround = float.NegativeInfinity;
                    for (int f = 0; f <= 30; f++)
                    {
                        walk.SampleAnimation(instance, walk.length * f / 30);
                        float ground = float.PositiveInfinity;
                        foreach (var r in skin)
                        {
                            var mesh = new Mesh();
                            try
                            {
                                r.BakeMesh(mesh);
                                foreach (var v in mesh.vertices)
                                {
                                    var world = r.transform.TransformPoint(v);
                                    if (float.IsNaN(world.y) || float.IsInfinity(world.y))
                                        throw new InvalidOperationException("Invalid animated vertex");
                                    ground = Mathf.Min(ground, world.y);
                                }
                            }
                            finally { UnityEngine.Object.DestroyImmediate(mesh); }
                        }
                        minGround = Mathf.Min(minGround, ground); maxGround = Mathf.Max(maxGround, ground);
                    }
                    report.AppendLine("  Walk floor range, 31 samples: " + minGround.ToString("F4") + " .. " + maxGround.ToString("F4") + " m");
                    if (minGround < -.025f || maxGround > .04f)
                        throw new InvalidOperationException(name + ": walk ground contact outside tolerance");
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
            AssetDatabase.SaveAssets();
            CreateShowroom(prefabs);
            File.WriteAllText("artifacts/reports/pedestrians-unity.txt", report + "PASS: Unity import, URP materials, rig, clips and sampled walk ground contact.\nNo navigation or traffic AI verified.\n");
            Debug.Log("PEDESTRIAN_IMPORT_PASS");
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
                    o.transform.position = new Vector3((i - 1) * .95f, 0, 0);
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
                camera.tag = "MainCamera"; camera.transform.position = new Vector3(2,2.3f,6);
                camera.transform.LookAt(new Vector3(0,.9f,0)); camera.fieldOfView = 32;
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
