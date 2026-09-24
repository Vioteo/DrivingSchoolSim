using System;
using NUnit.Framework;
using DrivingSchool.Learning;

namespace DrivingSchool.Tests
{
    public sealed class TrainingCourseTests
    {
        static CourseGate Stop(float x=0,int direction=0)=>new CourseGate {x=x,width=4,length=7,holdSeconds=2,wholeVehicle=true,direction=direction,instruction="Stop"};
        static TrainingCourse Course(params CourseGate[] gates)=>new TrainingCourse {lessons=new[]{new CourseLesson {id="test",gates=gates,title="Test"}}};
        static CourseSession Start(TrainingCourse c,bool exam=false){var s=new CourseSession(c,0,exam);s.Start();return s;}
        [Test] public void MustFitEntireVehicleInParkingBay()
        {
            var s=Start(Course(Stop()));s.Tick(3,1,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));
            s.Tick(2.1f,0,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Passed));
        }
        [Test] public void HoldMustBeContinuousAndAligned()
        {
            var s=Start(Course(Stop()));s.Tick(1.5f,0,0,0,0,1);s.Tick(.1f,0,0,0,1,1);s.Tick(1,0,0,0,0,1);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));s.Tick(2,0,0,180,0,1);Assert.That(s.HoldProgress,Is.Zero);
        }
        [Test] public void ReverseParkingCannotPassByDrivingForward()
        {
            var s=Start(Course(Stop(direction:-1)));s.Tick(.1f,0,0,0,1,1);s.Tick(3,0,0,0,0,1);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));
            s.Tick(.1f,0,0,0,-1,-1);s.Tick(2.1f,0,0,0,0,-1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Passed));
        }
        [Test] public void GatesCannotBeCompletedOutOfOrder()
        {
            var s=Start(Course(Stop(-20),Stop(20)));s.Tick(3,20,0,0,0,1);Assert.That(s.GateIndex,Is.Zero);
        }
        [Test] public void ReverseEntryLatchClearsOnExit()
        {
            var s=Start(Course(Stop(direction:-1)));s.Tick(.1f,0,0,0,-1,-1);s.Tick(.1f,10,0,0,-1,-1);
            s.Tick(3,0,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));
        }
        [Test] public void ExamIncludesTransfersAndFinalFinish()
        {
            var c=Course(Stop());c.lessons[0].transfer=new[]{Stop(15)};
            var s=Start(c,true);s.Tick(2.1f,0,0,0,0,1);Assert.That(s.Transferring,Is.True);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));s.Tick(2.1f,15,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Passed));
        }
        [Test] public void SingleLessonDoesNotRequireTransfer()
        {
            var c=Course(Stop());c.lessons[0].transfer=new[]{Stop(15)};
            var s=Start(c);s.Tick(2.1f,0,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Passed));
        }
        [Test] public void FaultThresholdAndTerminalStateAreStable()
        {
            var s=Start(Course(Stop()));s.Fault("cone",2);s.Fault("cone",2);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));
            s.Fault("speed");s.Tick(3,0,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Failed));
        }
        [Test] public void TimeoutCannotBeOverriddenByPassingStop()
        {
            var c=Course(Stop());c.lessons[0].timeLimit=1;
            var s=Start(c);s.Tick(3,0,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Failed));
        }
        [Test] public void RotatedBayUsesRotatedVehicleFootprint()
        {
            var g=Stop();g.yaw=90;
            Assert.That(CourseSession.Contains(g,0,0,90,2.25f,4.5f),Is.True);
            Assert.That(CourseSession.Contains(g,0,0,0,2.25f,4.5f),Is.False);
        }
        [Test] public void HillRollbackFailsAfterEntry()
        {
            var c=Course(new CourseGate {instruction="Entry"},Stop(20));c.lessons[0].id="hill";
            var s=Start(c);s.Tick(.1f,0,0,0,1,1);s.Tick(.1f,0,-.31f,0,-1,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Failed));
        }
        [TestCase(0)] [TestCase(90)] [TestCase(180)] [TestCase(270)]
        public void HillRollbackUsesAuthoredHeading(float yaw)
        {
            var entry=new CourseGate { instruction="Entry", yaw=yaw };
            var c=Course(entry,Stop(20));c.lessons[0].id="hill";c.lessons[0].startYaw=yaw;
            float dx=(float)Math.Sin(yaw*Math.PI/180), dz=(float)Math.Cos(yaw*Math.PI/180);
            var s=Start(c);s.Tick(.1f,0,0,yaw,1,1);
            s.Tick(.1f,dx,dz,yaw,1,1);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running),"Forward motion is not rollback");
            s.Tick(.1f,dx*.68f,dz*.68f,yaw,-1,-1);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Failed));
        }
        [Test] public void InvalidNumbersAreRejected()
        {
            var c=Course(Stop());c.lessons[0].gates[0].width=float.NaN;Assert.Throws<ArgumentException>(()=>new CourseSession(c,0,false));
            var s=Start(Course(Stop()));Assert.Throws<ArgumentOutOfRangeException>(()=>s.Tick(.1f,float.NaN,0,0,0,1));
        }
        [Test] public void ShiftGateRequiresGearAndMeasuredSpeed()
        {
            var g=new CourseGate {minimumGear=2,minimumSpeed=5};var s=Start(Course(g));
            s.Tick(.1f,0,0,0,6,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));
            s.Tick(.1f,0,0,0,4,2);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Running));
            s.Tick(.1f,0,0,0,5,2);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Passed));
        }
    }
}

