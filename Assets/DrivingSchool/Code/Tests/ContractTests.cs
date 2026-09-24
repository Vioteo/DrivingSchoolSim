using System;
using System.IO;
using NUnit.Framework;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation;
using DrivingSchool.Learning;
using DrivingSchool.World;
using UnityEngine;
namespace DrivingSchool.Tests
{
    public class ContractTests
    {
        LessonDefinition Lesson() { return new LessonDefinition{id="test",timeLimitSeconds=10,targetDistanceM=20,requiredStopSeconds=2,stopSpeedMps=.14f}; }
        [Test] public void NeutralCannotTransmitTorque() { Assert.That(DrivetrainMath.AxleTorque(180,0,4.1,.9),Is.Zero); }
        [Test] public void ReverseAndFinalDriveAffectTorque() { Assert.That(DrivetrainMath.AxleTorque(100,-3.5,4,.9),Is.EqualTo(-1260).Within(.001)); }
        [Test] public void DisengagedClutchCannotTransmitTorque() { Assert.That(DrivetrainMath.ClutchTorque(200,0,1,240,4),Is.Zero); }
        [Test] public void ClutchCannotExceedCapacity() { Assert.That(DrivetrainMath.ClutchTorque(900,0,.5,240,10),Is.EqualTo(120)); }
        [Test] public void AxleTorqueRejectsNanAndInfinity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.AxleTorque(double.NaN, 3.5, 4.1, 0.9));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.AxleTorque(100, double.NaN, 4.1, 0.9));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.AxleTorque(100, 3.5, double.NaN, 0.9));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.AxleTorque(100, 3.5, 4.1, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.AxleTorque(100, 3.5, double.PositiveInfinity, 0.9));
        }
        [Test] public void ClutchTorqueRejectsNanAndInfinity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.ClutchTorque(double.NaN, 0, 0, 350, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.ClutchTorque(100, double.NaN, 0, 350, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.ClutchTorque(100, 0, double.NaN, 350, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.ClutchTorque(100, 0, 0, double.NaN, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.ClutchTorque(100, 0, 0, 350, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => DrivetrainMath.ClutchTorque(100, 0, double.PositiveInfinity, 350, 50));
        }
        [Test] public void CommandsRejectNan() { var c=new DriverCommand{throttle=float.NaN};Assert.Throws<ArgumentOutOfRangeException>(()=>c.Validate()); }
        [Test] public void HoldingStillAtSpawnDoesNotPass() { var s=new LessonSession(Lesson());s.Ready();s.Start();s.Tick(3,0,0);Assert.That(s.Phase,Is.EqualTo(SessionPhase.Running)); }
        [Test] public void ReachingTargetThenStoppingPassesOnce() { var s=new LessonSession(Lesson());s.Ready();s.Start();s.Tick(1,21,3);s.Tick(2,22,0);var r=s.Result;s.Tick(2,22,0);Assert.That(s.Phase,Is.EqualTo(SessionPhase.Passed));Assert.That(s.Result,Is.SameAs(r)); }
        [Test] public void TimeoutAndCancellationAreTerminal() { var s=new LessonSession(Lesson());s.Ready();s.Start();s.Tick(10,0,0);s.Cancel();Assert.That(s.Phase,Is.EqualTo(SessionPhase.Failed)); }
        [Test] public void InvalidStartIsRejected() { Assert.Throws<InvalidOperationException>(()=>new LessonSession(Lesson()).Start()); }
        [Test] public void WorldRoundTripAndBackup()
        {
            var folder=Path.Combine(Application.temporaryCachePath,"ds-test-"+Guid.NewGuid().ToString("N"));
            try { var w=JsonUtility.FromJson<WorldDocument>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Examples/world.json")));var repo=new WorldRepository(folder);repo.Save("map",w);w.name="changed";repo.Save("map",w);Assert.That(repo.Load("map").name,Is.EqualTo("changed"));Assert.That(File.Exists(Path.Combine(folder,"map.json.bak")),Is.True);Assert.Throws<ArgumentException>(()=>repo.Load("../escape")); }
            finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
        }
        [Test] public void BrokenLaneSuccessorIsRejected() { var w=JsonUtility.FromJson<WorldDocument>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Examples/world.json")));w.lanes[0].successors=new[]{"missing"};Assert.Throws<InvalidDataException>(()=>WorldValidator.Validate(w)); }
        [Test] public void FutureSchemaIsNotSilentlyLoaded() { Assert.Throws<InvalidDataException>(()=>WorldValidator.Validate(new WorldDocument{schemaVersion=99})); }
        [Test] public void WorldLoadFallsBackToBackupOnCorruptPrimary()
        {
            var folder=Path.Combine(Application.temporaryCachePath,"ds-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                var w=JsonUtility.FromJson<WorldDocument>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Examples/world.json")));
                var repo=new WorldRepository(folder);
                repo.Save("map",w);
                var originalName=w.name;
                w.name="revision2";
                repo.Save("map",w);
                File.WriteAllText(Path.Combine(folder,"map.json"),"");
                var recovered=repo.Load("map");
                Assert.That(recovered,Is.Not.Null);
                Assert.That(recovered.name,Is.EqualTo(originalName));
            }
            finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
        }
        [Test] public void WorldValidatorRejectsInvalidDistrict()
        {
            var w=JsonUtility.FromJson<WorldDocument>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Examples/world.json")));
            w.districts=new[]{new District{id="",minX=0,minZ=0,sizeM=100}};
            Assert.Throws<InvalidDataException>(()=>WorldValidator.Validate(w));
            w.districts=new[]{new District{id="d1",minX=double.NaN,minZ=0,sizeM=100}};
            Assert.Throws<InvalidDataException>(()=>WorldValidator.Validate(w));
            w.districts=new[]{new District{id="d2",minX=0,minZ=0,sizeM=-10}};
            Assert.Throws<InvalidDataException>(()=>WorldValidator.Validate(w));
        }
        [Test] public void KeyboardInputDefaultProducesValidCommand() { var src = new DrivingSchool.Input.KeyboardInputSource(); var cmd = src.Read(1); Assert.DoesNotThrow(() => cmd.Validate()); Assert.That(cmd.sequence, Is.EqualTo(1)); }
        [Test] public void KeyboardInputResetClearsPedals() { var src = new DrivingSchool.Input.KeyboardInputSource(); src.Reset(); var cmd = src.Read(2); Assert.That(cmd.throttle, Is.Zero); Assert.That(cmd.brake, Is.Zero); }
        [Test] public void SpeedLimitUnderLimitDoesNotTrigger() { var eval = new DrivingSchool.Rules.SpeedLimitEvaluator(); bool viol = eval.Evaluate(1.0, 0, 0, 0, 15f, 60f, out var ev); Assert.That(viol, Is.False); Assert.That(ev, Is.Null); }
        [Test] public void SpeedLimitExceededGeneratesSingleRuleEvent() { var eval = new DrivingSchool.Rules.SpeedLimitEvaluator { GraceKph = 0f }; bool first = eval.Evaluate(1.0, 10, 0, 20, 25f, 60f, out var ev); Assert.That(first, Is.True); Assert.That(ev, Is.Not.Null); Assert.That(ev.ruleId, Is.EqualTo("pdd-10.2")); bool second = eval.Evaluate(2.0, 15, 0, 30, 25f, 60f, out var ev2); Assert.That(second, Is.False); }
        [Test] public void TheoryPackAuthorValidationSucceeds() { var t = JsonUtility.FromJson<TheoryContentPack>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Examples/theory.json"))); Assert.DoesNotThrow(() => TheoryPackageValidator.Validate(t)); }
        [Test] public void TheoryPackInvalidCorrectIndexRejected() { var t = JsonUtility.FromJson<TheoryContentPack>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Examples/theory.json"))); t.questions[0].correctIndex = 99; Assert.Throws<InvalidDataException>(() => TheoryPackageValidator.Validate(t)); }
        [Test] public void TheoryPackOfficialWithoutSourceRejected() { var t = JsonUtility.FromJson<TheoryContentPack>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Examples/theory.json"))); t.isOfficial = true; t.source = "Unknown unverified source"; Assert.Throws<InvalidDataException>(() => TheoryPackageValidator.Validate(t)); }
    }
}
