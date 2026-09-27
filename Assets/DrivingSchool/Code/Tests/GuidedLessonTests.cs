using System;
using DrivingSchool.Contracts;
using DrivingSchool.Learning;
using DrivingSchool.Rules;
using NUnit.Framework;

namespace DrivingSchool.Tests
{
    /// <summary>T49: пошаговый урок (docs/lessons.md) — порядок шагов, фильтр КПП, возврат при заглохании, зоны, проверка данных.</summary>
    public class GuidedLessonTests
    {
        static GuidedStep S(string id, string check, float value = 0, string only = null) =>
            new GuidedStep { id = id, text = "шаг " + id + " {clutch}", check = check, value = value, only = only };

        static GuidedLesson Lesson() => new GuidedLesson
        {
            id = "t", title = "Тест", doneText = "Готово",
            steps = new[]
            {
                new GuidedStep { id = "belt", text = "Ремень {belt}", check = "Seatbelt", keepUntil = "end", lostText = "Ремень отстёгнут" },
                S("clutch", "ClutchDown", only: "manual"),
                new GuidedStep { id = "engine", text = "Пуск {starter}", check = "EngineRunning", keepUntil = "stop", resumeAt = "gear", lostText = "Заглох {starter}" },
                S("gear", "Gear", 1, "manual"),
                new GuidedStep { id = "gear-a", text = "D {drive}", check = "Selector", selector = "D", only = "auto" },
                S("move", "MovingForward", 0.8f),
                new GuidedStep { id = "zone", text = "В рамку", check = "None", x = 10, z = 20, width = 4, length = 6, yaw = 90, headingTolerance = 30 },
                S("stop", "Stopped", 1),
                S("done", "Wait", 2),
            }
        };

        static LessonSnapshot Parked() => new LessonSnapshot { handbrake = true, gate = -1 };

        static void Run(GuidedLessonSession s, LessonSnapshot snap, double seconds = 0.1, double dt = 0.05)
        {
            for (double t = 0; t < seconds - 1e-9; t += dt) s.Tick(dt, snap);
        }

        [Test] public void ManualLessonGoesStepByStepToDone()
        {
            var s = new GuidedLessonSession(Lesson(), automatic: false);
            var c = Parked();
            Assert.That(s.Current.id, Is.EqualTo("belt"));
            Run(s, c); Assert.That(s.Current.id, Is.EqualTo("belt"), "Без действия шаг не засчитывается");
            c.seatbelt = true; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("clutch"));
            c.clutch = 1; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("engine"));
            c.engine = EnginePhase.Running; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("gear"));
            c.gear = 1; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("move"));
            c.speedMps = 0.5f; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("move"), "Порог скорости не достигнут");
            c.speedMps = 2f; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("zone"));
            c.x = 10; c.z = 20; c.yawDeg = 0; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("zone"), "Курс не тот");
            c.yawDeg = 80; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("stop"));
            c.speedMps = 0; Run(s, c, 0.5); Assert.That(s.Current.id, Is.EqualTo("stop"), "Стоять надо секунду");
            Run(s, c, 0.6); Assert.That(s.Current.id, Is.EqualTo("done"));
            Run(s, c, 2.1); Assert.That(s.Phase, Is.EqualTo(GuidedPhase.Done));
            Assert.That(s.RawText, Is.EqualTo("Готово"));
        }

        [Test] public void AutomaticSkipsManualOnlySteps()
        {
            var s = new GuidedLessonSession(Lesson(), automatic: true);
            Assert.That(s.StepCount, Is.EqualTo(7));
            var c = Parked(); c.seatbelt = true; c.engine = EnginePhase.Running;
            Run(s, c, 0.2); Assert.That(s.Current.id, Is.EqualTo("gear-a"));
            c.selector = AutomaticSelector.D; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("move"));
        }

        [Test] public void StallRewindsToEngineAndResumesAtGear()
        {
            var s = new GuidedLessonSession(Lesson(), automatic: false);
            var c = Parked(); c.seatbelt = true; c.clutch = 1; c.engine = EnginePhase.Running; c.gear = 1;
            Run(s, c, 0.3); Assert.That(s.Current.id, Is.EqualTo("move"));
            c.engine = EnginePhase.Stalled; c.gear = 0; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("engine")); Assert.That(s.Rewound, Is.True);
            Assert.That(s.RawText, Is.EqualTo("Заглох {starter}"));
            c.engine = EnginePhase.Running; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("gear"), "После пуска — снова передача, а не «трогайтесь» на нейтрали");
            c.gear = 1; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("move"), "Передача включена — назад туда, где заглохли");
            Assert.That(s.Rewinds, Is.EqualTo(1));
        }

        [Test] public void UnbuckledBeltReturnsToSameStepAfterFix()
        {
            var s = new GuidedLessonSession(Lesson(), automatic: false);
            var c = Parked(); c.seatbelt = true; c.clutch = 1; c.engine = EnginePhase.Running; c.gear = 1; c.speedMps = 2;
            Run(s, c, 0.4); Assert.That(s.Current.id, Is.EqualTo("zone"));
            c.seatbelt = false; c.clutch = 0; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("belt"));
            c.seatbelt = true; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("zone"), "Подготовку (сцепление) заново не требуем — возвращаемся туда, где были");
        }

        [Test] public void StallMidRouteDetoursOnlyThroughGearNotWholeStart()
        {
            var s = new GuidedLessonSession(Lesson(), automatic: false);
            var c = Parked(); c.seatbelt = true; c.clutch = 1; c.engine = EnginePhase.Running; c.gear = 1; c.speedMps = 2;
            Run(s, c, 0.4); Assert.That(s.Current.id, Is.EqualTo("zone"));
            c.engine = EnginePhase.Stalled; c.gear = 0; c.speedMps = 0; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("engine"));
            c.engine = EnginePhase.Running; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("gear"));
            c.gear = 1; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("zone"), "Шаг «трогайтесь» не повторяем — продолжаем маршрут");
        }

        [Test] public void KeepEndsAtItsStep()
        {
            var s = new GuidedLessonSession(Lesson(), automatic: false);
            var c = Parked(); c.seatbelt = true; c.clutch = 1; c.engine = EnginePhase.Running; c.gear = 1; c.speedMps = 2; c.x = 10; c.z = 20; c.yawDeg = 90;
            Run(s, c, 0.4); Assert.That(s.Current.id, Is.EqualTo("stop"));
            c.engine = EnginePhase.Stalled; c.speedMps = 0; Run(s, c);
            Assert.That(s.Current.id, Is.EqualTo("stop"), "На шаге остановки заглохнуть уже не страшно");
        }

        [Test] public void SkipAfterAndSkipAtGate()
        {
            var l = new GuidedLesson
            {
                id = "x", title = "x",
                steps = new[]
                {
                    new GuidedStep { id = "a", text = "a", check = "IndicatorsOff", skipAfter = 3 },
                    new GuidedStep { id = "b", text = "b", check = "HandbrakeOn", skipAtGate = 2 },
                    new GuidedStep { id = "c", text = "c", check = "Gate", value = 3 },
                }
            };
            var s = new GuidedLessonSession(l, false);
            var c = new LessonSnapshot { leftIndicator = true, gate = 0 };
            Run(s, c, 2.9); Assert.That(s.Current.id, Is.EqualTo("a"));
            Run(s, c, 0.2); Assert.That(s.Current.id, Is.EqualTo("b"));
            c.gate = 2; Run(s, c); Assert.That(s.Current.id, Is.EqualTo("c"), "Упражнение ушло дальше — шаг пропущен");
            c.gate = 3; Run(s, c); Assert.That(s.Phase, Is.EqualTo(GuidedPhase.Done));
        }

        [Test] public void NextZoneLooksAhead()
        {
            var s = new GuidedLessonSession(Lesson(), false);
            Assert.That(s.NextZone.id, Is.EqualTo("zone"), "Рамку видно заранее");
        }

        [Test] public void TextFormatReplacesKnownKeys()
        {
            Assert.That(GuidedText.Format("Выжмите {clutch} и {gas}", k => k == "clutch" ? "Shift" : k == "gas" ? "W" : null),
                Is.EqualTo("Выжмите Shift и W"));
            Assert.That(GuidedText.Format("без клавиш", null), Is.EqualTo("без клавиш"));
        }

        [Test] public void ValidateAcceptsGoodPackAndRejectsBad()
        {
            GuidedLessonPack Pack(GuidedLesson l) => new GuidedLessonPack { lessons = new[] { l } };
            Assert.DoesNotThrow(() => GuidedLessonPack.Validate(Pack(Lesson())));

            var bad = Lesson(); bad.steps[0].text = "Нажмите {unknown}";
            Assert.Throws<ArgumentException>(() => GuidedLessonPack.Validate(Pack(bad)), "неизвестная клавиша");
            bad = Lesson(); bad.steps[1].check = "Fly";
            Assert.Throws<ArgumentException>(() => GuidedLessonPack.Validate(Pack(bad)), "неизвестная проверка");
            bad = Lesson(); bad.steps[2].keepUntil = "nowhere";
            Assert.Throws<ArgumentException>(() => GuidedLessonPack.Validate(Pack(bad)), "keepUntil на несуществующий шаг");
            bad = Lesson(); bad.steps[5].keepUntil = "end";
            Assert.Throws<ArgumentException>(() => GuidedLessonPack.Validate(Pack(bad)), "keepUntil у проверки движения");
            bad = Lesson(); bad.steps[4].selector = "X";
            Assert.Throws<ArgumentException>(() => GuidedLessonPack.Validate(Pack(bad)), "селектор");
            bad = Lesson(); bad.steps[3].id = "belt";
            Assert.Throws<ArgumentException>(() => GuidedLessonPack.Validate(Pack(bad)), "повтор id");
        }

        [Test] public void StopTextWhileSignalProhibits()
        {
            var l = new GuidedLesson { id = "s", title = "s", steps = new[]
            { new GuidedStep { id = "cross", text = "Зелёный — едем", stopText = "Красный — стоим", check = "None", x = 0, z = 20, width = 4, length = 4 } } };
            var s = new GuidedLessonSession(l, false);
            var c = new LessonSnapshot { signal = LessonSignal.Stop };
            s.Tick(0.1, c); Assert.That(s.RawText, Is.EqualTo("Красный — стоим"));
            c.signal = LessonSignal.Go; s.Tick(0.1, c); Assert.That(s.RawText, Is.EqualTo("Зелёный — едем"));
            c.z = 20; s.Tick(0.1, c); Assert.That(s.Phase, Is.EqualTo(GuidedPhase.Done));
        }

        [Test] public void RedLightCrossingIsReportedAsSevereRule()
        {
            var m = new DriveRuleMonitor();
            Assert.That(m.Update(new DriveRuleInput { Seconds = 1, Seatbelt = true, LowOrHighBeam = true, SpeedMps = 5 }), Is.Empty);
            var ev = m.Update(new DriveRuleInput { Seconds = 2, Seatbelt = true, LowOrHighBeam = true, SpeedMps = 5, CrossedStopLineOnRed = true });
            Assert.That(ev.Count, Is.EqualTo(1)); Assert.That(ev[0].ruleId, Is.EqualTo(DriveRuleMonitor.RuleRedLight));
        }

        [Test] public void ForcedHintIgnoresHintMode()
        {
            var q = new InstructorHintQueue { Mode = HintMode.Off };
            q.Post("tip", HintKind.Maneuver, "совет");
            Assert.That(q.Current, Is.Null);
            q.Post("lesson", HintKind.Exercise, "шаг урока", 0, true);
            Assert.That(q.Current.Key, Is.EqualTo("lesson"));
            q.Tick(1); Assert.That(q.Current.Key, Is.EqualTo("lesson"), "Смена режима не убирает шаг урока");
        }
    }
}
