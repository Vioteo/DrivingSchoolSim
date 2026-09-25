using System.Collections;
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
        [UnityTest] public IEnumerator ActualSceneKeyboardDriveCanPassStartStop()
        {
            EditorSceneManager.OpenScene(TrainingGroundBuilder.ScenePath);
            yield return new EnterPlayMode();
            var keyboard=InputSystem.AddDevice<Keyboard>();
            yield return null;
            var director=Object.FindFirstObjectByType<TrainingGroundDirector>();
            Assert.That(director,Is.Not.Null);director.Begin(0,false);
            var vehicle=director.vehicle;
            vehicle.Keyboard.KeyboardDevice=keyboard;
            float stopZ=TrainingGroundBuilder.CreateCourse().lessons[0].gates[1].z;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
            float deadline=Time.realtimeSinceStartup+30;
            while(vehicle.transform.position.z < stopZ-1.3f && Time.realtimeSinceStartup<deadline)
            { InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));yield return null; }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.S));
            yield return new WaitForSeconds(3);
            Assert.That(vehicle.CollisionCount,Is.Zero,"Straight corridor must be unobstructed");
            Assert.That(director.Session.Phase,Is.EqualTo(CoursePhase.Passed),"Actual keyboard drive: "+director.Session.Message+" z="+vehicle.transform.position.z);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            InputSystem.RemoveDevice(keyboard);
            yield return new ExitPlayMode();
        }
    }
}
