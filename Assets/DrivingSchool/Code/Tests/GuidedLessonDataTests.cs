using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Learning;
using DrivingSchool.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T49: настоящий пакет уроков (Resources/guided-lessons.json) — проходит проверку, урок 1 проходим до конца, зоны на площадке.</summary>
    public class GuidedLessonDataTests
    {
        static GuidedLessonPack Pack()
        {
            var asset = Resources.Load<TextAsset>(GuidedLessonRunner.PackResource);
            Assert.That(asset, Is.Not.Null, "Resources/guided-lessons.json");
            var pack = JsonUtility.FromJson<GuidedLessonPack>(asset.text);
            GuidedLessonPack.Validate(pack);
            return pack;
        }

        [Test] public void PackIsValidAndHasFirstLesson()
        {
            var p = Pack();
            Assert.That(p.Find(DrivingSchool.Presentation.UI.LessonLaunch.FirstLesson), Is.Not.Null);
            foreach (var l in p.lessons)
                foreach (var s in l.steps)
                    foreach (var k in GuidedText.Keys)
                        if (s.text.Contains("{" + k + "}")) Assert.That(LessonControls.KeyName(k), Is.Not.Null, k);
            Assert.That(GuidedText.Keys.All(k => LessonControls.KeyName(k) != null), "У каждой клавиши урока есть имя");
        }

        [Test] public void ExerciseHintsMatchCourseLessons()
        {
            var course = JsonUtility.FromJson<TrainingCourse>(File.ReadAllText("Assets/DrivingSchool/Data/Training/course-v2.json"));
            var ids = course.lessons.Select(l => l.id).ToList();
            foreach (var l in Pack().lessons.Where(l => !string.IsNullOrEmpty(l.courseLesson)))
            {
                Assert.That(ids, Does.Contain(l.courseLesson), l.id);
                var gates = course.lessons.First(c => c.id == l.courseLesson).gates.Length;
                foreach (var s in l.steps.Where(s => s.check == "Gate")) Assert.That(s.value, Is.LessThanOrEqualTo(gates), $"{l.id}/{s.id}");
            }
            Assert.That(course.lessons.All(c => Pack().FindForCourse(c.id) != null), "Подсказки есть для каждого упражнения площадки");
        }

        [Test] public void FirstLessonRunsOnTheStreetInTheRightLane()
        {
            var l = Pack().Find("start-moving");
            Assert.That(l.scene, Is.EqualTo(LessonStreetLayout.SceneName));
            Assert.That(l.scene, Is.EqualTo(DrivingSchool.Presentation.UI.LessonLaunch.FirstLessonScene));
            var start = new Vector3(l.startX, 0, l.startZ);
            Assert.That(LessonStreetLayout.NorthboundLane(start) && start.x > 2.5f, "Старт у правого края полосы на север");
            foreach (var s in l.steps.Where(s => s.HasZone))
            {
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(s.yaw, 0)), Is.LessThan(1), s.id + ": по ходу на север");
                Assert.That(s.x - s.width / 2, Is.GreaterThanOrEqualTo(0f), s.id + ": не на встречной");
                Assert.That(s.x + s.width / 2, Is.LessThanOrEqualTo(LessonStreetLayout.RoadHalfWidth), s.id + ": не на тротуаре");
                Assert.That(LessonStreetLayout.InJunction(new Vector3(s.x, 0, s.z)), Is.False, s.id + ": не внутри перекрёстка");
            }
            var zones = l.steps.Where(s => s.HasZone).Select(s => s.z).ToList();
            Assert.That(zones.Any(z => z < -LessonStreetLayout.StopLine) && zones.Any(z => z > LessonStreetLayout.JunctionHalf), "Маршрут проходит перекрёсток");
            Assert.That(l.steps.Where(s => s.HasZone && s.z < 0 && s.z > -LessonStreetLayout.StopLine - 30).All(s => s.z + s.length / 2 < -LessonStreetLayout.StopLine - 2.2f),
                "Зоны до перекрёстка кончаются до стоп-линии (центр машины за 2,2 м до бампера)");
        }

        /// <summary>«Ученик-робот» выполняет каждый шаг, как просит текст, — урок должен дойти до конца на обеих КПП.</summary>
        [TestCase(false)] [TestCase(true)]
        public void FirstLessonCanBeCompleted(bool automatic)
        {
            var s = new GuidedLessonSession(Pack().Find("start-moving"), automatic);
            var c = new LessonSnapshot { handbrake = true, gate = -1, x = 160, z = 252 };
            for (int i = 0; i < 5000 && s.Phase == GuidedPhase.Running; i++)
            {
                var st = s.Current;
                switch (st.check)
                {
                    case "Seatbelt": c.seatbelt = true; break;
                    case "Ignition": c.ignition = true; break;
                    case "ClutchDown": c.clutch = 1; break;
                    case "EngineRunning": c.engine = EnginePhase.Running; break;
                    case "LowBeam": c.lowBeam = true; break;
                    case "Gear": c.gear = (int)st.value; break;
                    case "Selector": c.selector = (AutomaticSelector)System.Enum.Parse(typeof(AutomaticSelector), st.selector); break;
                    case "IndicatorLeft": c.leftIndicator = true; c.rightIndicator = false; break;
                    case "IndicatorRight": c.rightIndicator = true; c.leftIndicator = false; break;
                    case "IndicatorsOff": c.leftIndicator = c.rightIndicator = false; break;
                    case "HandbrakeOff": c.handbrake = false; break;
                    case "HandbrakeOn": c.handbrake = true; break;
                    case "MovingForward": c.speedMps = st.value + 0.5f; c.clutch = 0; break;
                    case "SpeedBelow": c.speedMps = st.value * 0.5f; break;
                    case "Stopped": c.speedMps = 0; break;
                    case "Neutral": c.gear = 0; break;
                    case "IgnitionOff": c.ignition = false; c.engine = EnginePhase.Off; break;
                }
                if (st.HasZone) { c.x = st.x; c.z = st.z; c.yawDeg = st.yaw; }
                s.Tick(0.1, c);
            }
            Assert.That(s.Phase, Is.EqualTo(GuidedPhase.Done));
            Assert.That(s.Rewinds, Is.Zero);
        }
    }
}
