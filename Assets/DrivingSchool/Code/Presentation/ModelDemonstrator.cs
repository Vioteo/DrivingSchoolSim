using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using DrivingSchool.Contracts;
using DrivingSchool.World;
using DrivingSchool.Learning;
namespace DrivingSchool.Presentation
{
    public sealed class ModelDemonstrator : MonoBehaviour
    {
        public Transform car; public Camera view;
        public float steer, pedals, rpm, wiper, turntable;
        readonly Dictionary<Transform,Quaternion> rotations=new Dictionary<Transform,Quaternion>();
        string status="Демонстрация модели. Физика автомобиля не подключена.";
        bool cockpit, isAutomatic, isNight;
        Vector3 eye; Quaternion initialCarRot; GUIStyle label;
        Transform transManual, transAuto; Light sun;
        void Start()
        {
            if(car!=null)
            {
                initialCarRot=car.localRotation;
                foreach(var t in car.GetComponentsInChildren<Transform>(true))
                {
                    rotations[t]=t.localRotation;
                    if(t.name=="Socket_DriverEye")eye=t.position;
                    else if(t.name=="Transmission_Manual")transManual=t;
                    else if(t.name=="Transmission_Automatic")transAuto=t;
                }
            }
            sun=FindFirstObjectByType<Light>();
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--smoke")>=0)StartCoroutine(Smoke());
        }
        void Update()
        {
            foreach(var kv in rotations)
            {
                if(kv.Key==null)continue;
                var n=kv.Key.name;float angle=0;Vector3 axis=Vector3.forward;
                if(n=="SteeringWheel_Pivot")angle=steer*450;
                else if(n.StartsWith("Wheel_F")){angle=steer*32;axis=Vector3.up;}
                else if(n.StartsWith("Needle_")){angle=-rpm*260;axis=Vector3.forward;}
                else if(n.StartsWith("Pedal_")){angle=pedals*20;axis=Vector3.right;}
                else if(n.StartsWith("Wiper_Pivot")){angle=wiper*70;axis=Vector3.up;}
                kv.Key.localRotation=kv.Value*Quaternion.AngleAxis(angle,axis);
            }
            if(car!=null)car.localRotation=initialCarRot*Quaternion.Euler(0,turntable,0);
            if(cockpit&&view!=null) {view.transform.position=eye;view.transform.rotation=Quaternion.Euler(5,0,0);}
        }
        void OnGUI()
        {
            if(label==null)label=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true};
            GUI.Box(new Rect(18,18,360,490),"DS / техническая демонстрация");
            GUILayout.BeginArea(new Rect(32,46,332,450));
            GUILayout.Label(status,label);
            GUILayout.Label("Поворот подиума");turntable=GUILayout.HorizontalSlider(turntable,-180,180);
            GUILayout.Label("Руль (угол выворота)");steer=GUILayout.HorizontalSlider(steer,-1,1);
            GUILayout.Label("Педали");pedals=GUILayout.HorizontalSlider(pedals,0,1);
            GUILayout.Label("Приборы (стрелки)");rpm=GUILayout.HorizontalSlider(rpm,0,1);
            GUILayout.Label("Дворники");wiper=GUILayout.HorizontalSlider(wiper,0,1);
            if(GUILayout.Button("Органы КПП: "+(isAutomatic?"АКПП (селектор P/R/N/D)":"МКПП (6 передач + сцепление)")))
            {
                isAutomatic=!isAutomatic;
                if(transManual!=null)transManual.gameObject.SetActive(!isAutomatic);
                if(transAuto!=null)transAuto.gameObject.SetActive(isAutomatic);
            }
            if(GUILayout.Button("Освещение: "+(isNight?"Ночь (подсветка приборов)":"День (естественный свет)")))
            {
                isNight=!isNight;
                if(sun!=null)sun.intensity=isNight?0.05f:2.5f;
                RenderSettings.ambientMode=AmbientMode.Flat;
                RenderSettings.ambientLight=isNight?new Color(.05f,.07f,.10f):new Color(.55f,.6f,.65f);
            }
            if(GUILayout.Button(cockpit?"Внешний вид":"Место водителя"))
            {
                cockpit=!cockpit;
                if(!cockpit&&view!=null){view.transform.position=new Vector3(-4,2.4f,5);view.transform.LookAt(new Vector3(0,.8f,0));}
            }
            if(GUILayout.Button("Проверить сохранение и занятие"))RunChecks();
            GUILayout.EndArea();
        }
        public void RunChecks()
        {
            try
            {
                var w=JsonUtility.FromJson<WorldDocument>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Examples/world.json")));
                var repo=new WorldRepository(Path.Combine(Application.persistentDataPath,"PrototypeWorlds"));repo.Save("smoke-map",w);var loaded=repo.Load("smoke-map");
                if(loaded.id!=w.id||loaded.lanes.Length!=w.lanes.Length)throw new Exception("World roundtrip mismatch");
                var l=JsonUtility.FromJson<LessonDefinition>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Examples/lesson.json")));
                var s=new LessonSession(l);s.Ready();s.Start();s.Tick(1,21,2);s.Tick(2.1f,22,0);
                if(s.Phase!=SessionPhase.Passed)throw new Exception("Lesson did not pass");
                status="PASS: карта сохранена и загружена; занятие завершено. Это программная проверка, не поездка.";
                Debug.Log("DS_PLAYER_SMOKE_PASS "+Application.persistentDataPath);
            }
            catch(Exception e){status="FAIL: "+e.Message;Debug.LogException(e);}
        }
        IEnumerator Smoke()
        {
            RunChecks();yield return new WaitForSeconds(3);
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--capture");
            if(i>=0&&i+1<args.Length) {ScreenCapture.CaptureScreenshot(args[i+1]);yield return new WaitForSeconds(2);}
            Application.Quit(status.StartsWith("PASS")?0:1);
        }
    }
}
