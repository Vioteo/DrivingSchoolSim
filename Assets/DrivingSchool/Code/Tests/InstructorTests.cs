using System.Linq;
using DrivingSchool.Learning;
using DrivingSchool.Rules;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T47: очередь подсказок инструктора (docs/ui-drive.md §2), журнал поездки, монитор нарушений.</summary>
    public class InstructorTests
    {
        [Test] public void HigherPriorityPreemptsAndDisplacedReturns()
        {
            var q = new InstructorHintQueue();
            q.Post("nav", HintKind.Navigation, "Поворот направо", 8);
            Assert.That(q.Current.Key, Is.EqualTo("nav"));
            q.Post("train", HintKind.Safety, "Переезд закрыт");
            Assert.That(q.Current.Key, Is.EqualTo("train"), "Безопасность вытесняет сразу");
            Assert.That(q.Waiting, Is.EqualTo(1));
            q.Tick(20);   // навигация ждёт — её время не идёт
            Assert.That(q.Has("nav"), Is.True);
            q.Clear("train");
            Assert.That(q.Current.Key, Is.EqualTo("nav"), "Вытесненная возвращается, пока актуальна");
            q.Tick(5); Assert.That(q.Current, Is.Not.Null);
            q.Tick(3.1); Assert.That(q.Current, Is.Null, "8 с показа истекли");
        }

        [Test] public void SamePriorityKeepsArrivalOrder()
        {
            var q = new InstructorHintQueue();
            q.Post("a", HintKind.Maneuver, "первая");
            q.Post("b", HintKind.Maneuver, "вторая");
            Assert.That(q.Current.Key, Is.EqualTo("a"));
            q.Post("a", HintKind.Maneuver, "первая, новый текст");
            Assert.That(q.Current.Text, Is.EqualTo("первая, новый текст"), "Обновление не меняет очередь");
        }

        [Test] public void RepostingTimedHintDoesNotExtendIt()
        {
            var q = new InstructorHintQueue();
            q.Post("err", HintKind.Error, "Пристегнитесь", 7);
            q.Tick(6);
            q.Post("err", HintKind.Error, "Пристегнитесь", 7);
            q.Tick(1.5);
            Assert.That(q.Current, Is.Null);
        }

        [Test] public void ModeFiltersKinds()
        {
            var q = new InstructorHintQueue { Mode = HintMode.ErrorsOnly };
            q.Post("m", HintKind.Maneuver, "манёвр");
            q.Post("e", HintKind.Error, "ошибка");
            q.Post("s", HintKind.Safety, "опасность");
            Assert.That(q.Has("m"), Is.False);
            Assert.That(q.Has("e") && q.Has("s"), Is.True);
            q.Mode = HintMode.Off; q.Tick(0);
            Assert.That(q.Current, Is.Null, "Выкл. — ничего");
            q.Post("s2", HintKind.Safety, "опасность");
            Assert.That(q.Current, Is.Null);
        }

        [Test] public void LogAccumulatesDistanceTimeAndMaxSpeed()
        {
            var log = new DriveLog();
            log.Sample(10, 2); log.Sample(-5, 1); log.Sample(double.NaN, 1);
            Assert.That(log.DistanceM, Is.EqualTo(25).Within(1e-9));
            Assert.That(log.ElapsedSeconds, Is.EqualTo(3).Within(1e-9));
            Assert.That(log.MaxSpeedMps, Is.EqualTo(10));
            log.Add(new DriveLogEvent { RuleId = "X", Severe = true });
            Assert.That(log.Events[0].Seconds, Is.EqualTo(3));
            Assert.That(log.SevereCount, Is.EqualTo(1));
        }

        static DriveRuleInput At(double t, float v, bool belt = true, bool lights = true) =>
            new DriveRuleInput { Seconds = t, SpeedMps = v, Seatbelt = belt, LowOrHighBeam = lights };

        [Test] public void SeatbeltReportedOncePerEpisodeAfterGrace()
        {
            var m = new DriveRuleMonitor();
            Assert.That(m.Update(At(0, 0, belt: false)), Is.Empty, "Стоя без ремня — не нарушение");
            Assert.That(m.Update(At(1, 5, belt: false)), Is.Empty);
            Assert.That(m.Update(At(2.9, 5, belt: false)), Is.Empty, "Ещё в пределах игрового допуска");
            var ev = m.Update(At(3.1, 5, belt: false));
            Assert.That(ev.Select(e => e.ruleId), Is.EquivalentTo(new[] { DriveRuleMonitor.RuleSeatbelt }));
            Assert.That(ev[0].penalty, Is.Zero, "Баллов нет до таблицы T37");
            Assert.That(m.Update(At(10, 5, belt: false)), Is.Empty, "Один раз за эпизод");
            m.Update(At(11, 5, belt: true));
            m.Update(At(12, 5, belt: false));
            Assert.That(m.Update(At(14.5, 5, belt: false)), Has.Count.EqualTo(1), "Отстегнулся снова — новый эпизод");
        }

        [Test] public void LightsAndRailwayAndCollision()
        {
            var m = new DriveRuleMonitor();
            m.Update(At(0, 5, lights: false));
            Assert.That(m.Update(At(4, 5, lights: false)), Is.Empty);
            Assert.That(m.Update(At(5.5, 5, lights: false)).Single().ruleId, Is.EqualTo(DriveRuleMonitor.RuleLowBeam));

            var open = new DriveRuleInput { Seconds = 20, SpeedMps = 5, Seatbelt = true, LowOrHighBeam = true, EnteredRailwayCrossing = true, RailwayClosed = false };
            Assert.That(m.Update(open), Is.Empty, "Открытый переезд проезжать можно");
            var closed = open; closed.Seconds = 21; closed.RailwayClosed = true;
            Assert.That(m.Update(closed).Single().ruleId, Is.EqualTo(DriveRuleMonitor.RuleRailwayClosed));

            var hit = At(30, 3); hit.ImpactSpeedMps = 4;
            Assert.That(m.Update(hit).Single().ruleId, Is.EqualTo(DriveRuleMonitor.RuleCollision));
            hit.Seconds = 31;
            Assert.That(m.Update(hit), Is.Empty, "Дребезг одного удара не считается дважды");
            var tap = At(40, 1); tap.ImpactSpeedMps = 0.5f;
            Assert.That(m.Update(tap), Is.Empty, "Касание — не столкновение");
        }
    }
}
