using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using DrivingSchool.Learning;
using DrivingSchool.Simulation;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Presentation
{
    public sealed class TrainingGroundDirector : MonoBehaviour
    {
        public TextAsset courseFile;
        public VehicleController vehicle;
        public Camera view;
        public Transform marker;
        TrainingCourse course;
        CourseSession session;
        int selected, cameraMode, contacts;
        bool exam, saved;
        GUIStyle title, bodyStyle;
        string saveStatus="";
        public CourseSession Session => session;
        void Start() { course=JsonUtility.FromJson<TrainingCourse>(courseFile.text); CourseSession.Validate(course); PositionCar(0); }
        void PositionCar(int index)
        {
            var l=course.lessons[index];vehicle.ResetAt(new Vector3(l.startX,0,l.startZ),Quaternion.Euler(0,l.startYaw,0));
        }
        public void Begin(int lesson, bool fullExam)
        {
            session?.Cancel(); selected=fullExam?0:lesson; exam=fullExam; saved=false; saveStatus=""; contacts=0;
            PositionCar(selected);session=new CourseSession(course,selected,exam);session.Start();vehicle.inputEnabled=true;
        }
        void Update()
        {
            var k=Keyboard.current;
            if(k!=null && k.cKey.wasPressedThisFrame)cameraMode=(cameraMode+1)%3;
            if(k!=null && k.escapeKey.wasPressedThisFrame && session!=null) {session.Cancel();SaveResult();}
            vehicle.inputEnabled=session!=null && session.Phase==CoursePhase.Running;
            var gate=session?.CurrentGate;
            if(marker)
            {
                marker.gameObject.SetActive(gate!=null && (!exam || session.Transferring));
                if(gate!=null)
                {
                    float y=UnityEngine.Physics.Raycast(new Vector3(gate.x,5,gate.z),Vector3.down,out var hit,8,1<<9)?hit.point.y+.05f:.05f;
                    marker.SetPositionAndRotation(new Vector3(gate.x,y,gate.z),Quaternion.Euler(0,gate.yaw,0));
                    marker.localScale=new Vector3(gate.width,1,gate.length);
                }
            }
        }
        void FixedUpdate()
        {
            if(session==null || session.Phase!=CoursePhase.Running || (!Application.isFocused && !Application.isBatchMode))return;
            if(vehicle.CollisionCount>contacts) { session.Fault("Касание конуса или ограждения",2);contacts=vehicle.CollisionCount; }
            var p=vehicle.transform.position;
            var state = vehicle.Adapter.CurrentState;
            session.Tick(Time.fixedDeltaTime,p.x,p.z,vehicle.transform.eulerAngles.y,state.signedSpeedMps,state.gear);
            if(session.Phase!=CoursePhase.Running)SaveResult();
        }
        void LateUpdate()
        {
            if(!vehicle || !view)return;
            var t=vehicle.transform;
            if(cameraMode==2) { view.orthographic=true;view.orthographicSize=course.halfLength*1.08f;view.transform.SetPositionAndRotation(new Vector3(0,course.halfLength*3,0),Quaternion.Euler(90,0,0)); }
            else
            {
                view.orthographic=false;
                Vector3 desired=cameraMode==0?t.position-t.forward*10+Vector3.up*5.5f:t.position+Vector3.up*27-t.forward*7;
                view.transform.position=Vector3.Lerp(view.transform.position,desired,1-Mathf.Exp(-Time.deltaTime*7));
                view.transform.LookAt(t.position+t.forward*2+Vector3.up*.5f);
            }
        }
        [Serializable] class Result
        {
            public string courseId, utc, mode, lessonId, phase, reason;
            public int penalty;public float elapsedSeconds;
            public string controller="layout-prototype";
        }
        void SaveResult()
        {
            if(saved)return;saved=true;
            try
            {
                var result=new Result { courseId=course.id,utc=DateTime.UtcNow.ToString("O"),mode=exam?"exam":"lesson",
                    lessonId=course.lessons[session.LessonIndex].id,phase=session.Phase.ToString(),reason=session.Message,
                    penalty=session.Penalty,elapsedSeconds=session.Elapsed };
                var directory=Path.Combine(Application.persistentDataPath,"TrainingResults");Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".json"),JsonUtility.ToJson(result,true));
                saveStatus="Результат сохранён";
            }
            catch(Exception e) {saveStatus="Не удалось сохранить результат: "+e.Message;Debug.LogWarning(saveStatus);}
        }
        void OnGUI()
        {
            if(course==null)return;
            float scale=Mathf.Clamp(Screen.height/900f,.7f,1.5f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            if(title==null)
            {
                title=new GUIStyle(GUI.skin.label){fontSize=22,fontStyle=FontStyle.Bold,wordWrap=true};
                bodyStyle=new GUIStyle(GUI.skin.label){fontSize=15,wordWrap=true};
            }
            GUI.Box(new Rect(18,18,340,852),GUIContent.none);
            GUILayout.BeginArea(new Rect(34,30,308,822));
            GUILayout.Label("АВТОДРОМ / 01",title);
            GUILayout.Label("Учебная площадка · категория B",bodyStyle);
            GUILayout.Space(8);
            if(session==null || session.Phase!=CoursePhase.Running)
            {
                if(session!=null) {GUILayout.Label(session.Message,title);GUILayout.Label("Штраф: "+session.Penalty+" · "+session.Elapsed.ToString("F0")+" с\n"+saveStatus,bodyStyle);}
                for(int i=0;i<course.lessons.Length;i++)
                    if(GUILayout.Button((i+1).ToString("00")+"  "+course.lessons[i].title,GUILayout.Height(29))) {selected=i;PositionCar(i);}
                GUILayout.Space(8);GUILayout.Label(course.lessons[selected].briefing,bodyStyle);
                if(GUILayout.Button("Начать выбранный урок",GUILayout.Height(34)))Begin(selected,false);
                if(GUILayout.Button("Сдать всю площадку",GUILayout.Height(34)))Begin(0,true);
            }
            else
            {
                GUILayout.Label(exam?"ЭКЗАМЕН":"ПРАКТИКА",title);
                GUILayout.Label((session.LessonIndex+1)+" / "+course.lessons.Length+"  "+course.lessons[session.LessonIndex].title,bodyStyle);
                GUILayout.Label(session.Transferring?"Переезд к следующей зоне":session.CurrentGate.instruction,title);
                if(session.Transferring)GUILayout.Label(session.CurrentGate.instruction,bodyStyle);
                GUILayout.Label("Шаг "+(session.GateIndex+1)+"  ·  "+session.Elapsed.ToString("F0")+" с\nШтраф "+session.Penalty+" / "+course.failPenalty,bodyStyle);
                if(session.CurrentGate.holdSeconds>0)GUILayout.Label("Остановка: "+session.HoldProgress.ToString("F1")+" / "+session.CurrentGate.holdSeconds.ToString("F0")+" с",bodyStyle);
                GUILayout.Label(session.Message,bodyStyle);
                if(GUILayout.Button("Отменить заезд")){session.Cancel();SaveResult();}
            }
            GUILayout.Space(10);
            var state = vehicle.Adapter.CurrentState;
            GUILayout.Label(Mathf.Abs(state.signedSpeedMps*3.6f).ToString("F0")+" км/ч  ·  Передача "+(state.gear<0?"R":state.gear.ToString()),title);
            GUILayout.Label("W / ↑ — газ    S / ↓ — тормоз\nA D / ← → — руль    Пробел — ручник\nQ — D/R на остановке    E — 1/2\nC — камера / вид всей площадки",bodyStyle);
            GUILayout.Label("Тестовое вождение: полная физика (VehicleSolver).",bodyStyle);
            GUILayout.EndArea();GUI.matrix=Matrix4x4.identity;
        }
    }
}

