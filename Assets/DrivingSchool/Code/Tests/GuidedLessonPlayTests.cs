using System.Collections;
using DrivingSchool.Contracts;
using DrivingSchool.Learning;
using DrivingSchool.Presentation;
using DrivingSchool.Presentation.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DrivingSchool.Tests
{
    /// <summary>
    /// T49: урок 1 целиком в настоящей сцене — главное меню → «Занятия» → учебная улица, настоящая физика машины.
    /// «Робот» делает то, что просит текущий шаг (как ученик), рулит к рамкам, на красный стоит у стоп-линии;
    /// урок должен дойти до конца без нарушений и столкновений.
    /// </summary>
    public sealed class GuidedLessonPlayTests
    {
        bool runInBackground;

        [UnityTest, Timeout(600000)] public IEnumerator FirstLessonFromMenuCanBeCompleted([Values(false, true)] bool automatic)
        {
            EditorSceneManager.OpenScene("Assets/DrivingSchool/Scenes/MainMenu.unity");
            yield return new EnterPlayMode();
            runInBackground = Application.runInBackground; Application.runInBackground = true;
            yield return null;
            var flow = Object.FindAnyObjectByType<MainMenuFlow>();
            Assert.That(flow, Is.Not.Null, "MainMenuFlow в сцене меню");
            flow.StartFirstLesson();

            DriveSession ds = null;
            float wait = Time.realtimeSinceStartup + 60;
            while ((ds == null || ds.Lesson == null) && Time.realtimeSinceStartup < wait)
            {
                yield return null;
                if (SceneManager.GetActiveScene().name != AppNavigator.MainMenuScene) ds = Object.FindAnyObjectByType<DriveSession>();
            }
            Assert.That(ds, Is.Not.Null, "Сессия поездки создана");
            Assert.That(ds.Lesson, Is.Not.Null, "Урок запущен из меню");
            Assert.That(ds.Lesson.Lesson.id, Is.EqualTo(LessonLaunch.FirstLesson));

            var car = Object.FindAnyObjectByType<VehicleTestRangeDirector>().player;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(LessonStreetLayout.SceneName));
            Assert.That(LessonStreetLayout.NorthboundLane(car.transform.position), "Машина у правого края улицы");
            var junction = Object.FindAnyObjectByType<SignalJunction>();
            Assert.That(junction, Is.Not.Null, "Регулируемый перекрёсток в сцене");
            car.Adapter.SetTransmission(automatic ? TransmissionType.Automatic : TransmissionType.Manual);
            var src = new ScriptedInputSource();
            car.Source = src;
            var c = new DriverCommand { handbrake = true, selector = AutomaticSelector.P };

            float deadline = Time.time + 300, nextLog = 0;
            var lesson = ds.Lesson;
            while (lesson.Session.Phase == GuidedPhase.Running && Time.time < deadline)
            {
                var st = car.Adapter.CurrentState;
                var step = lesson.Session.Current;
                float v = st.signedSpeedMps, dt = Mathf.Max(Time.deltaTime, 0.001f);
                bool manual = !automatic;
                c.starter = false;
                var zone = lesson.Session.NextZone;
                if (zone != null && Mathf.Abs(v) > 0.3f) c.steering = Steer(car.transform, zone);
                switch (step.check)
                {
                    case "Seatbelt": c.seatbelt = true; break;
                    case "Ignition": c.ignition = true; break;
                    case "ClutchDown": c.clutch = 1; break;
                    case "Selector":
                        c.brake = 0.5f; c.throttle = 0;
                        c.selector = (AutomaticSelector)System.Enum.Parse(typeof(AutomaticSelector), step.selector); break;
                    case "EngineRunning":
                        c.ignition = true; c.starter = true; c.brake = 0.5f; c.throttle = 0; c.steering = 0;
                        if (manual) { c.clutch = 1; c.requestedGear = 0; } else c.selector = AutomaticSelector.P;
                        break;
                    case "LowBeam": c.headlights = HeadlightMode.LowBeam; break;
                    case "Gear": c.clutch = 1; c.requestedGear = (int)step.value; break;
                    case "IndicatorLeft": c.turnSignal = TurnSignal.Left; break;
                    case "IndicatorRight": c.turnSignal = TurnSignal.Right; break;
                    case "IndicatorsOff": c.turnSignal = TurnSignal.Off; Cruise(ref c, v, manual, dt); break;
                    case "HandbrakeOff": c.handbrake = false; break;
                    case "HandbrakeOn": c.handbrake = true; break;
                    case "MovingForward":
                        c.brake = 0; c.throttle = 0.4f;
                        // Как в автошколе: быстро до точки схватывания, дальше медленно.
                        if (manual) c.clutch = c.clutch > 0.8f ? Mathf.MoveTowards(c.clutch, 0.8f, 2f * dt) : Mathf.MoveTowards(c.clutch, 0f, 0.25f * dt);
                        break;
                    case "SpeedBelow": c.throttle = 0; c.brake = 0.4f; if (manual && v < 2.5f) c.clutch = 1; break;
                    case "Stopped" when manual && v < 2.5f && !(step.HasZone && !GuidedLessonSession.InZone(step, lesson.Snapshot())):
                        c.throttle = 0; c.brake = 0.6f; c.clutch = 1; break;
                    case "Stopped":
                        if (step.HasZone && !GuidedLessonSession.InZone(step, lesson.Snapshot())) Cruise(ref c, v, manual, dt);
                        else { c.throttle = 0; c.brake = 0.6f; if (manual) c.clutch = 1; }
                        break;
                    case "None": Cruise(ref c, v, manual, dt); break;
                    case "Neutral": c.clutch = 1; c.requestedGear = 0; break;
                    case "IgnitionOff": c.ignition = false; break;
                }
                // Правила: на запрещающий сигнал — остановка перед стоп-линией.
                var fwd = car.transform.forward; var front = car.transform.position + fwd * 2.2f;
                if (junction.SignalFor(front, fwd) == SignalJunction.Signal.Stop && junction.Approach(front, fwd, out _, out float toLine) && toLine < 25f && toLine > -0.5f)
                {
                    c.throttle = 0; c.brake = toLine < 12f || v > 5f ? 0.7f : 0.3f; if (manual && v < 3f) c.clutch = 1;
                    if (toLine < 1.5f) c.brake = 1f;
                }
                src.Command = c;
                if (Time.time > nextLog)
                {
                    nextLog = Time.time + 10;
                    Debug.Log($"[Lesson] тест t={Time.time:0} шаг {step.id} x={car.transform.position.x:0.0} z={car.transform.position.z:0.0} v={v:0.0} gear={st.gear} engine={st.engine} clutch={c.clutch:0.00} rpm={st.engineRpm:0}");
                }
                yield return null;
            }
            var end = car.Adapter.CurrentState;
            Assert.That(lesson.Session.Phase, Is.EqualTo(GuidedPhase.Done),
                $"Урок застрял на шаге {lesson.Session.Current?.id} ({lesson.Progress}); x={car.transform.position.x:0.0} z={car.transform.position.z:0.0} yaw={car.transform.eulerAngles.y:0} v={end.signedSpeedMps:0.0} gear={end.gear} engine={end.engine}");
            Assert.That(car.CollisionCount, Is.Zero, "По пути урока ни во что не врезаемся");
            Assert.That(ds.Log.Events, Is.Empty, "Без нарушений: " + string.Join(", ", System.Linq.Enumerable.Select(ds.Log.Events, e => e.RuleId)));
            Debug.Log($"[Lesson] тест: урок пройден ({(automatic ? "АКПП" : "МКПП")}), возвратов {lesson.Session.Rewinds}, время {Time.time:0} с");
        }

        /// <summary>Держать шаг пешехода: ~3 м/с, на механике — первая передача без сцепления.</summary>
        static void Cruise(ref DriverCommand c, float v, bool manual, float dt)
        {
            c.throttle = v < 2.5f ? 0.3f : v < 3.5f ? 0.12f : 0f;
            c.brake = v > 4.5f ? 0.3f : 0f;
            // Трогание после остановки (красный) — так же плавно, как со старта.
            if (manual) c.clutch = c.clutch > 0.8f ? Mathf.MoveTowards(c.clutch, 0.8f, 2f * dt) : Mathf.MoveTowards(c.clutch, 0f, 0.25f * dt);
        }

        /// <summary>Руль на точку за центром рамки по её курсу — машина въезжает в рамку уже выровненной.</summary>
        static float Steer(Transform car, GuidedStep zone)
        {
            float a = zone.yaw * Mathf.Deg2Rad;
            var target = new Vector3(zone.x + Mathf.Sin(a) * 5f, 0, zone.z + Mathf.Cos(a) * 5f);
            var d = target - car.position;
            float desired = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return Mathf.Clamp(Mathf.DeltaAngle(car.eulerAngles.y, desired) / 25f, -1f, 1f);
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            LessonLaunch.LessonId = null;
            if (Application.isPlaying) { Time.timeScale = 1f; Application.runInBackground = runInBackground; yield return new ExitPlayMode(); }
        }
    }
}
