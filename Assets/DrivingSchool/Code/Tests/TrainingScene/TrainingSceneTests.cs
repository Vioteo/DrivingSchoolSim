using System.Collections;
using DrivingSchool.Contracts;
using DrivingSchool.Editor;
using DrivingSchool.Learning;
using DrivingSchool.Presentation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace DrivingSchool.Tests
{
    public sealed class TrainingSceneTests
    {
        [Test] public void EveryAuthoredLessonAndWholeExamCanCompleteWithValidGateSamples()
        {
            var c=TrainingGroundBuilder.CreateCourse();
            for(int i=0;i<c.lessons.Length;i++) Replay(c,i,false);
            Replay(c,0,true);
        }
        static void Replay(TrainingCourse c,int index,bool exam)
        {
            var s=new CourseSession(c,index,exam);s.Start();int count=0;
            while(s.Phase==CoursePhase.Running && count++<100)
            {
                var g=s.CurrentGate;int gear=g.direction<0?-1:Mathf.Max(1,g.minimumGear);
                // Gate telemetry replay validates content/state transitions, not vehicle manoeuvrability.
                float speed=g.direction<0?-1:Mathf.Max(1,g.minimumSpeed);
                s.Tick(.02f,g.x,g.z,g.yaw,speed,gear);
                if(g.holdSeconds>0 && ReferenceEquals(s.CurrentGate,g))s.Tick(g.holdSeconds+.02f,g.x,g.z,g.yaw,0,gear);
            }
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Passed),"Replay "+index+" exam="+exam+" "+s.Message);
        }
        Keyboard keyboard;
        VehicleController vehicle;
        bool runInBackground;

        [UnityTest] public IEnumerator ActualSceneKeyboardDriveCanPassStartStop()
        {
            EditorSceneManager.OpenScene(TrainingGroundBuilder.ScenePath);
            yield return new EnterPlayMode();
            // Тесты часто идут в несфокусированном редакторе (MCP, фон): без этого Play Mode почти не тикает.
            // Ставим уже в Play Mode: вход перезагружает домен, а настройка проекта не должна меняться.
            runInBackground=Application.runInBackground; Application.runInBackground=true;
            keyboard=InputSystem.AddDevice<Keyboard>();
            yield return null;
            var director=Object.FindFirstObjectByType<TrainingGroundDirector>();
            Assert.That(director,Is.Not.Null);
            vehicle=director.vehicle;
            // Автомат: трогание со сцеплением с клавиатуры нестабильно для теста; механику проверяет самопроверка полигона (F8).
            vehicle.Adapter.SetTransmission(TransmissionType.Automatic);
            director.Begin(0,false);
            vehicle.Keyboard.KeyboardDevice=keyboard;

            yield return Tap(Key.I);                             // зажигание
            yield return Hold(1.5f,Key.Enter,Key.S);             // стартер в P, нога на тормозе
            Assert.That(vehicle.Adapter.CurrentState.engine,Is.EqualTo(EnginePhase.Running),"Двигатель не завёлся");
            yield return Tap(Key.Digit1,Key.S);                  // селектор D
            yield return Tap(Key.Space,Key.S);                   // снять с ручника

            // Все ожидания — в игровом времени: в несфокусированном редакторе кадров мало, и WaitForSeconds
            // (время редактора) заканчивается раньше, чем машина успевает что-то сделать.
            float stopZ=TrainingGroundBuilder.CreateCourse().lessons[0].gates[1].z;
            float deadline=Time.time+30;
            // Трогание «накатом» на D без газа, торможение за метр до центра стоп-зоны.
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            while(vehicle.transform.position.z<stopZ-1f && Time.time<deadline) yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S));
            float until=Time.time+4;
            while(Time.time<until) yield return null;
            Assert.That(vehicle.CollisionCount,Is.Zero,"Straight corridor must be unobstructed");
            Assert.That(director.Session.Phase,Is.EqualTo(CoursePhase.Passed),"Actual keyboard drive: "+director.Session.Message+" x="+vehicle.transform.position.x+" z="+vehicle.transform.position.z+
                " yaw="+vehicle.transform.eulerAngles.y+" v="+vehicle.Adapter.CurrentState.signedSpeedMps+" gear="+vehicle.Adapter.CurrentState.gear+" hold="+director.Session.HoldProgress);
        }

        // Очистка обязательна даже при упавшей проверке: иначе виртуальная клавиатура с зажатой клавишей
        // остаётся Keyboard.current и ломает следующие тесты (так падал KeyboardInputResetClearsPedals).
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(vehicle!=null) { vehicle.Keyboard.KeyboardDevice=null; vehicle=null; }   // иначе машина опрашивает удалённое устройство
            if(keyboard!=null)
            { InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.RemoveDevice(keyboard); keyboard=null; }
            if(Application.isPlaying) { Application.runInBackground=runInBackground; yield return new ExitPlayMode(); }
        }

        IEnumerator Tap(Key key,params Key[] held)
        {
            var down=new Key[held.Length+1]; held.CopyTo(down,0); down[held.Length]=key;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(down)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(held)); yield return null; yield return null;
        }

        IEnumerator Hold(float seconds,params Key[] keys)
        {
            float until=Time.time+seconds;
            while(Time.time<until) { InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys)); yield return null; }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
        }
    }
}
