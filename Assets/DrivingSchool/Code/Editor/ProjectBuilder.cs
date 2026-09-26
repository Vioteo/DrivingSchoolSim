using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DrivingSchool.Presentation;
using DrivingSchool.Contracts;
using DrivingSchool.World;
namespace DrivingSchool.Editor
{
    public static class ProjectBuilder
    {
        const string Base="Assets/DrivingSchool";
        static void Folders(){foreach(var p in new[]{"Scenes","Materials","Settings","Prefabs"})Directory.CreateDirectory(Base+"/"+p);Directory.CreateDirectory("artifacts/reports");AssetDatabase.Refresh();}
        [MenuItem("Driving School/Refresh sedan showroom")]
        public static void RefreshSedan()
        {
            Folders();Import("DS_Sedan_A");CreateShowroom();AssetDatabase.SaveAssets();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Base+"/Prefabs/DS_Sedan_A.prefab");
            var parts=prefab.GetComponentsInChildren<Transform>(true);
            foreach(var name in new[]{"Seat_RearBench","Socket_DriverEye","Wheel_FL","Wheel_FR","Wheel_RL","Wheel_RR","SteeringWheel_Pivot","Pedal_Clutch","Pedal_Brake","Pedal_Throttle","MirrorSurface_L","MirrorSurface_R","MirrorSurface_Centre"})
                if(!parts.Any(t=>t.name==name))throw new InvalidOperationException("Missing sedan part: "+name);
            var renderers=prefab.GetComponentsInChildren<Renderer>(true);
            if(renderers.Any(r=>r.sharedMaterials.Any(m=>m==null)))throw new InvalidOperationException("Missing sedan material");
            File.WriteAllText("artifacts/reports/cabin-v3-unity-import.txt","PASS: cabin_v3 prefab saved; required parts and renderer materials present.\n"+File.ReadAllText("artifacts/reports/unity-import.txt"));
            Debug.Log("DS_SEDAN_REFRESH_PASS");
        }
        [MenuItem("Driving School/Capture sedan review")]
        public static void CaptureSedanReview()
        {
            EditorSceneManager.OpenScene(Base+"/Scenes/Showroom.unity");
            var cam=Camera.main;
            var model=GameObject.Find("DS_Sedan_A");
            var parts=model.GetComponentsInChildren<Transform>(true);
            var eye=parts.First(t=>t.name=="Socket_DriverEye");
            var front=parts.First(t=>t.name=="Wheel_FL").position;
            var rear=parts.First(t=>t.name=="Wheel_RL").position;
            var output="artifacts/visual-review/cabin-v3";
            Directory.CreateDirectory(output);
            foreach(var cockpit in new[]{false,true})
            {
                if(cockpit){cam.transform.position=eye.position;cam.transform.rotation=Quaternion.LookRotation((front-rear).normalized+Vector3.down*.08f,Vector3.up);cam.fieldOfView=75;}
                var target=new RenderTexture(960,720,24);target.Create();cam.targetTexture=target;
                cam.Render();var previous=RenderTexture.active;RenderTexture.active=target;
                var capture=new Texture2D(960,720,TextureFormat.RGB24,false);
                capture.ReadPixels(new Rect(0,0,960,720),0,0);capture.Apply();
                File.WriteAllBytes(output+(cockpit?"/unity-driver.png":"/unity-exterior.png"),capture.EncodeToPNG());
                RenderTexture.active=previous;cam.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(capture);
            }
            Debug.Log("DS_SEDAN_CAPTURE_PASS");
        }
        [MenuItem("Driving School/Prepare prototype")]
        public static void Prepare()
        {
            Folders();Time.fixedDeltaTime=.01f;
            PlayerSettings.companyName="DrivingSchool";PlayerSettings.productName="DrivingSchoolSim Prototype";
            PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Base+"/Settings/Renderer.asset");
            if(renderer==null){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,Base+"/Settings/Renderer.asset");}
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Base+"/Settings/URP.asset");
            if(pipeline==null){pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,Base+"/Settings/URP.asset");}
            pipeline.msaaSampleCount=4;pipeline.shadowDistance=100;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            ApplyLightingSettings();
            var project=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=project.FindProperty("activeInputHandler");if(input!=null){input.intValue=2;project.ApplyModifiedPropertiesWithoutUndo();}
            foreach(var name in new[]{"DS_Sedan_A","DS_District","DS_Autodrome"})Import(name);
            CreateShowroom();CreateEnvironment("District","DS_District",new Vector3(-130,120,-170));CreateEnvironment("Autodrome","DS_Autodrome",new Vector3(-100,130,-140));
            EditorBuildSettings.scenes=new[]{"Showroom","District","Autodrome"}.Select(n=>new EditorBuildSettingsScene(Base+"/Scenes/"+n+".unity",true)).ToArray();
            AssetDatabase.SaveAssets();
            var doc=JsonUtility.FromJson<WorldDocument>(File.ReadAllText("Assets/StreamingAssets/Examples/world.json"));WorldValidator.Validate(doc);
            Debug.Log("DS_PREPARE_PASS");
        }
        /// <summary>
        /// Night driving needs many local lights on one object (street lamps, both headlamp beams, other cars' lamps on one long
        /// road mesh): Forward's limit of 4 lights per object silently dropped the headlamps. Forward+ has no per-object limit;
        /// headlamp shadows need additional-light shadows.
        /// </summary>
        [MenuItem("Driving School/Apply lighting settings (Forward+)")]
        public static void ApplyLightingSettings()
        {
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Base+"/Settings/Renderer.asset");
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Base+"/Settings/URP.asset");
            if(renderer==null||pipeline==null)throw new FileNotFoundException("URP settings missing: run Prepare prototype");
            renderer.renderingMode=UnityEngine.Rendering.Universal.RenderingMode.ForwardPlus;EditorUtility.SetDirty(renderer);
            var so=new SerializedObject(pipeline);
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue=(int)UnityEngine.Rendering.Universal.LightRenderingMode.PerPixel;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue=8;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true;
            so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue=2048;
            so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("DS_LIGHTING_SETTINGS_PASS");
        }
        static void Import(string name)
        {
            var path=Base+"/Art/"+name+".fbx";
            if(!File.Exists(path))throw new FileNotFoundException(path);
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;importer.importCameras=false;importer.importLights=false;importer.isReadable=true;
            importer.SaveAndReimport();
            foreach(var old in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                string matPath=Base+"/Materials/"+old.name+".mat";
                var m=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if(m==null)
                {
                    m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=old.name;
                    m.SetColor("_BaseColor",old.HasProperty("_Color")?old.color:Color.gray);
                    m.SetFloat("_Smoothness",old.name.Contains("Paint")?.65f:.25f);
                    if(old.name.Contains("Aluminium")||old.name=="Chrome"||old.name=="Mirror")m.SetFloat("_Metallic",.8f);
                    if(old.name=="Glass")
                    {
                        m.SetColor("_BaseColor",new Color(.25f,.45f,.5f,.13f));m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;
                    }
                    if(old.name.StartsWith("Lamp_")||old.name=="Ink") {m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",m.GetColor("_BaseColor")*1.5f);}
                    AssetDatabase.CreateAsset(m,matPath);
                }
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(old),m);
            }
            importer.SaveAndReimport();
        }
        static GameObject Model(string name)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Base+"/Art/"+name+".fbx");var obj=(GameObject)PrefabUtility.InstantiatePrefab(source);obj.name=name;
            if(name=="DS_Sedan_A")
            {
                var all=obj.GetComponentsInChildren<Transform>();
                obj.transform.rotation = Quaternion.Euler(-90, 180, 0);
                foreach(var t in all)if(t.name=="Transmission_Automatic")t.gameObject.SetActive(false);
            }
            return obj;
        }
        static Camera CameraAt(Vector3 pos,Vector3 target)
        {
            var cam=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();cam.tag="MainCamera";cam.transform.position=pos;cam.transform.LookAt(target);cam.fieldOfView=60;cam.farClipPlane=1600;cam.nearClipPlane=.03f;cam.backgroundColor=new Color(.11f,.15f,.18f);cam.clearFlags=CameraClearFlags.SolidColor;cam.GetUniversalAdditionalCameraData();return cam;
        }
        static void Light()
        {
            var light=new GameObject("Sun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=2.5f;light.transform.rotation=Quaternion.Euler(45,-30,0);light.shadows=LightShadows.Soft;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.55f,.6f,.65f);
        }
        static void CreateShowroom()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var model=Model("DS_Sedan_A");Light();
            var stageMat=AssetDatabase.LoadAssetAtPath<Material>(Base+"/Materials/Stage.mat");
            if(stageMat==null)
            {
                stageMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));stageMat.name="Stage";
                stageMat.SetColor("_BaseColor",new Color(.12f,.15f,.18f));stageMat.SetFloat("_Smoothness",.35f);stageMat.SetFloat("_Metallic",.1f);
                AssetDatabase.CreateAsset(stageMat,Base+"/Materials/Stage.mat");
            }
            var mirrorMat=AssetDatabase.LoadAssetAtPath<Material>(Base+"/Materials/MirrorReflection.mat");
            if(mirrorMat==null)
            {
                mirrorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mirrorMat.name="MirrorReflection";
                mirrorMat.SetFloat("_Smoothness",.9f);mirrorMat.SetFloat("_Metallic",.2f);
                AssetDatabase.CreateAsset(mirrorMat,Base+"/Materials/MirrorReflection.mat");
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Stage";floor.transform.localScale=Vector3.one*10;floor.GetComponent<Renderer>().sharedMaterial=stageMat;
            var cam=CameraAt(new Vector3(-4,2.4f,5),new Vector3(0,.8f,0));
            var demo=new GameObject("Model Demonstrator",typeof(ModelDemonstrator)).GetComponent<ModelDemonstrator>();demo.car=model.transform;demo.view=cam;
            foreach(var t in model.GetComponentsInChildren<Transform>())if(t.name.StartsWith("MirrorSurface")) {t.gameObject.layer=8;var mirror=t.gameObject.AddComponent<PlanarMirror>();mirror.surface=t;mirror.source=cam;mirror.templateMaterial=mirrorMat;}
            var b=new Bounds(model.transform.position,Vector3.zero);foreach(var rr in model.GetComponentsInChildren<Renderer>())b.Encapsulate(rr.bounds);
            File.WriteAllText("artifacts/reports/unity-import.txt","Imported bounds metres: "+b.size+"\nParts: "+model.GetComponentsInChildren<Transform>().Length+"\n");
            PrefabUtility.SaveAsPrefabAsset(model,Base+"/Prefabs/DS_Sedan_A.prefab");EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Base+"/Scenes/Showroom.unity");
        }
        static void CreateEnvironment(string scene,string model,Vector3 position)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var obj=Model(model);Light();CameraAt(position,Vector3.zero);
            foreach(var filter in obj.GetComponentsInChildren<MeshFilter>())if(filter.name=="RoadSurface"||filter.name=="IntersectionSurface"||filter.name=="HillRamp")filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Base+"/Scenes/"+scene+".unity");
        }
        public static void Build()
        {
            Directory.CreateDirectory("Builds/Windows");var report=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes,"Builds/Windows/DrivingSchoolSim.exe",BuildTarget.StandaloneWindows64,BuildOptions.Development);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Build failed: "+report.summary.result);
            File.WriteAllText("artifacts/reports/build.txt",report.summary.result+"\nBytes: "+report.summary.totalSize+"\nTime: "+report.summary.totalTime);Debug.Log("DS_BUILD_PASS");
        }
    }
}
