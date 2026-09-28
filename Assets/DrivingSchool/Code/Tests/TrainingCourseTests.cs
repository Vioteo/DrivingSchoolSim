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
            // ---------------- T68: faults of the exercises
        static CourseInput At(float x,float z,float yaw,float v,int gear=1,bool left=false,bool right=false,bool stalled=false)
            =>new CourseInput{x=x,z=z,yaw=yaw,signedSpeed=v,gear=gear,leftIndicator=left,rightIndicator=right,engineStalled=stalled};

        [Test] public void FrontPastStopLineIsAFaultAndTheStopCountsAsPassed()
        {
            var stop=Stop(); stop.stopLine=3.5f; var next=new CourseGate{instruction="Next",z=30};
            var s=Start(Course(stop,next));
            s.Tick(.1f,At(0,0,0,1));                           // front at 2.25 m: before the line
            Assert.That(s.Faults,Is.Empty);
            s.Tick(.1f,At(0,1.7f,0,1));                        // front at 3.95 m: 0.45 m past, tolerance 0.3
            Assert.That(s.Faults.Count,Is.EqualTo(1));
            Assert.That(s.Faults[0].code,Is.EqualTo(CourseSession.StopLine));
            Assert.That(s.GateIndex,Is.EqualTo(1),"stop gate is passed, the exercise goes on");
            Assert.That(s.Penalty,Is.EqualTo(3));
        }
        [Test] public void StopLineIgnoresCarsFacingAnotherWayOrBesideTheLane()
        {
            var stop=Stop(); stop.stopLine=3.5f;
            var s=Start(Course(stop));
            s.Tick(.1f,At(0,3,180,1));                         // facing back: its front is behind
            s.Tick(.1f,At(6,4,0,1));                           // 6 m to the side
            Assert.That(s.Faults,Is.Empty);
        }
        [Test] public void TurnSignalMustBeSeenBeforeTheGate()
        {
            var g=new CourseGate{instruction="Turn",signal=1};
            var s=Start(Course(g,Stop(30)));
            s.Tick(.1f,At(0,0,0,1));
            Assert.That(s.Faults.Count,Is.EqualTo(1));Assert.That(s.Faults[0].code,Is.EqualTo(CourseSession.NoSignal));
            var ok=Start(Course(new CourseGate{instruction="Turn",signal=1},Stop(30)));
            ok.Tick(.1f,At(0,-20,0,1,right:true));             // on while approaching, off after the turn
            ok.Tick(.1f,At(0,0,0,1));
            Assert.That(ok.Faults,Is.Empty);Assert.That(ok.GateIndex,Is.EqualTo(1));
            var hazard=Start(Course(new CourseGate{instruction="Turn",signal=-1},Stop(30)));
            hazard.Tick(.1f,At(0,-20,0,1,left:true,right:true)); // hazard lights are not a turn signal
            hazard.Tick(.1f,At(0,0,0,1));
            Assert.That(hazard.Faults.Count,Is.EqualTo(1));
        }
        [Test] public void StallIsCountedOncePerStall()
        {
            var s=Start(Course(Stop(30)));
            s.Tick(.1f,At(0,0,0,0,stalled:true));s.Tick(.1f,At(0,0,0,0,stalled:true));
            Assert.That(s.Faults.Count,Is.EqualTo(1));
            s.Tick(.1f,At(0,0,0,0));s.Tick(.1f,At(0,0,0,0,stalled:true));
            Assert.That(s.Faults.Count,Is.EqualTo(2));Assert.That(s.Faults[1].code,Is.EqualTo(CourseSession.Stall));
        }
        [Test] public void ReverseManoeuvreAllowsTwoCorrections()
        {
            var bay=Stop(30,-1); bay.maxCorrections=2;
            var s=Start(Course(bay));
            s.Tick(.1f,At(0,0,0,1));                            // still rolling forward after the previous gate: not a correction
            for(int i=0;i<2;i++) { s.Tick(.1f,At(0,0,0,-1,-1)); s.Tick(.1f,At(0,0,0,1)); }
            Assert.That(s.Faults,Is.Empty,"two corrections are allowed");
            s.Tick(.1f,At(0,0,0,-1,-1)); s.Tick(.1f,At(0,0,0,1));
            Assert.That(s.Faults.Count,Is.EqualTo(1));Assert.That(s.Faults[0].code,Is.EqualTo(CourseSession.Corrections));
            s.Tick(.1f,At(0,0,0,-1,-1)); s.Tick(.1f,At(0,0,0,1));
            Assert.That(s.Faults.Count,Is.EqualTo(1),"reported once per gate");
        }
        [Test] public void PenaltyTableSetsPointsAndMapsRuleEvents()
        {
            var c=Course(Stop(30));
            c.penalties=new[]{ new CoursePenalty{code=CourseSession.Contact,title="Конус",points=3},
                               new CoursePenalty{code="red-light",title="Красный",points=5,ruleId="PDD_6.2_RED_LIGHT"} };
            var s=Start(c);
            Assert.That(s.Penalize(CourseSession.Contact),Is.True);Assert.That(s.Penalty,Is.EqualTo(3));
            Assert.That(s.PenalizeRule("PDD_19.5_LOW_BEAM"),Is.False,"not in the table — not scored");
            Assert.That(s.PenalizeRule("PDD_6.2_RED_LIGHT"),Is.True);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Failed));Assert.That(s.Faults[1].ruleId,Is.EqualTo("PDD_6.2_RED_LIGHT"));
            Assert.That(s.Penalize(CourseSession.Contact),Is.False,"nothing is added after the end");
        }
        [Test] public void TerminalFaultsEndTheAttemptAndAreRecorded()
        {
            var c=Course(Stop());c.lessons[0].timeLimit=1;
            var s=Start(c);s.Tick(3,0,0,0,0,1);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Failed));
            Assert.That(s.Faults.Count,Is.EqualTo(1));Assert.That(s.Faults[0].terminal,Is.True);Assert.That(s.Faults[0].code,Is.EqualTo(CourseSession.Timeout));
        }
        [Test] public void NoRollbackFlagWorksForAnyLessonId()
        {
            var c=Course(new CourseGate {instruction="Entry"},Stop(20));c.lessons[0].noRollback=true;
            var s=Start(c);s.Tick(.1f,0,0,0,1,1);s.Tick(.1f,0,-.31f,0,-1,1);
            Assert.That(s.Phase,Is.EqualTo(CoursePhase.Failed));Assert.That(s.Faults[0].code,Is.EqualTo(CourseSession.Rollback));
        }
        [Test] public void ExamCountsCompletedExercisesAndKeepsGateIndexAtTheEnd()
        {
            var c=Course(Stop());c.lessons=new[]{c.lessons[0],new CourseLesson{id="second",title="Second",gates=new[]{Stop(15)}}};
            var s=Start(c,true);
            s.Tick(2.1f,0,0,0,0,1);Assert.That(s.LessonsCompleted,Is.EqualTo(1));Assert.That(s.LessonIndex,Is.EqualTo(1));
            s.Tick(2.1f,15,0,0,0,1);Assert.That(s.Phase,Is.EqualTo(CoursePhase.Passed));
            Assert.That(s.LessonsCompleted,Is.EqualTo(2));Assert.That(s.GateIndex,Is.EqualTo(1),"guided hints see the last gate done");
        }
        [Test] public void InvalidPenaltiesAndGateChecksAreRejected()
        {
            var c=Course(Stop());c.penalties=new[]{new CoursePenalty{code="a",title="A"},new CoursePenalty{code="a",title="B"}};
            Assert.Throws<ArgumentException>(()=>new CourseSession(c,0,false));
            c=Course(Stop());c.lessons[0].gates[0].signal=2;
            Assert.Throws<ArgumentException>(()=>new CourseSession(c,0,false));
            c=Course(Stop());c.lessons[0].gates[0].stopLine=-1;
            Assert.Throws<ArgumentException>(()=>new CourseSession(c,0,false));
        }
    }
}
