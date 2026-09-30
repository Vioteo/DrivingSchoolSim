using DrivingSchool.Contracts;
using DrivingSchool.Learning;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Rules;
using DrivingSchool.Settings;
using DrivingSchool.Simulation.Traffic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Поездка на тестовом полигоне (T47): собирает состояние машины, ведёт журнал, проверяет правила
    /// (DriveRuleMonitor), ведёт инструктора (TestRangeInstructor → InstructorHintQueue) и рисует HUD.
    /// Создаётся сам, если в загруженной сцене есть директор полигона; работает, только если меню положило HUD
    /// (сцена, открытая напрямую, остаётся со старой панелью F4). «Завершить поездку» в паузе → разбор.
    /// Если меню выбрало пошаговый урок (<see cref="LessonLaunch"/>, T49) — вместо советов полигона ведёт урок:
    /// машина на старт урока, шаги в подсказке инструктора, рамка цели на земле, в конце — разбор урока.
    /// На автодроме (T68, <see cref="TrainingGroundDirector"/>) ведёт упражнение с пошаговыми подсказками или экзамен по
    /// всей площадке без подсказок и телепортов: ошибки упражнений — карточки и журнал, в конце — разбор с баллами.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class DriveSession : MonoBehaviour
    {
        public const int MinimapSize = 384;
        // Картинка в HUD на 42 % больше видимого круга (запас на поворот по курсу): видимый круг = 100 м в диаметре.
        public const float MinimapHalfExtentM = 50f * 1.42f;

        VehicleTestRangeDirector director;
        VehicleController player;
        DriverCameraRig rig;
        DriveHudView hud;
        DebriefView debrief;
        PauseMenuController pause;
        Camera minimapCam; RenderTexture minimapRt;

        readonly InstructorHintQueue hints = new InstructorHintQueue();
        readonly TestRangeInstructor instructor = new TestRangeInstructor();
        readonly DriveRuleMonitor rules = new DriveRuleMonitor();
        readonly DriveLog log = new DriveLog();
        readonly HudModel model = new HudModel();
        bool wasOnCrossing; float lastImpactTime; int gearCount = 6; float redline = 6500f;
        GuidedLessonRunner lesson; float lessonIntroLeft; bool lessonFinished;
        SignalJunction junction; readonly SignalJunction.StopLineWatch stopLine = new SignalJunction.StopLineWatch();
        bool shiftLockShown;
        bool testRange;   // советы TestRangeInstructor и названия зон — только на самом полигоне
        // Город (T65): правила по графу района — скорость по знакам, встречная, поворотники, полосы, дистанция, остановки.
        TrafficDirectorHost traffic; PlayerRoadMonitor road; readonly CityRuleMonitor cityRules = new CityRuleMonitor();
        double lastRoadSeconds = -1; float speedLimitKph; float shownLimitKph = -1;
        const float LessonIntroSeconds = 5f;
        // Автодром (T68): упражнение или экзамен.
        TrainingGroundDirector autodrome; bool autodromeRun; int faultsShown;

        public DriveLog Log => log;
        public InstructorHintQueue Hints => hints;
        /// <summary>Пошаговый урок этой поездки; null — свободная поездка.</summary>
        public GuidedLessonRunner Lesson => lesson;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;   // фон главного меню грузится аддитивно
            var dir = Object.FindAnyObjectByType<VehicleTestRangeDirector>();
            if (dir == null || dir.player == null) return;
            var go = new GameObject("DriveSession");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<DriveSession>().director = dir;
        }

        void Start()
        {
            hud = FindAnyObjectByType<DriveHudView>();
            if (hud == null || director == null) { Destroy(gameObject); return; }
            player = director.player;
            rig = FindAnyObjectByType<DriverCameraRig>();
            debrief = FindAnyObjectByType<DebriefView>(FindObjectsInactive.Include);
            pause = FindAnyObjectByType<PauseMenuController>();
            director.ShowHelp = false; director.ShowTelemetry = false;   // клавиши — в паузе (F4); в поездке подсказок клавиш нет (ui-drive §1)
            if (pause != null) pause.FinishRequested += ShowDebrief;
            if (debrief != null) { debrief.RestartRequested += AppNavigator.RestartDrive; debrief.MenuRequested += AppNavigator.ToMainMenu; }
            if (player.Adapter != null)
            {
                var spec = player.Adapter.BuildSpec();
                if (spec.gearRatios != null && spec.gearRatios.Length > 0) gearCount = spec.gearRatios.Length;
                redline = spec.redlineRpm;
            }
            lastImpactTime = player.LastImpactTime;
            testRange = gameObject.scene.name == "VehicleTestRange";
            junction = FindAnyObjectByType<SignalJunction>();
            traffic = FindAnyObjectByType<TrafficDirectorHost>();
            autodrome = FindAnyObjectByType<TrainingGroundDirector>();
            if (traffic != null && traffic.Director != null) road = new PlayerRoadMonitor(traffic.Director);
            StartLesson();
            CreateMinimap();
            Debug.Log("[Drive] сессия полигона: HUD и инструктор включены");
        }

        void OnDestroy()
        {
            if (pause != null) pause.FinishRequested -= ShowDebrief;
            if (debrief != null) { debrief.RestartRequested -= AppNavigator.RestartDrive; debrief.MenuRequested -= AppNavigator.ToMainMenu; }
            if (minimapRt != null) { minimapRt.Release(); Destroy(minimapRt); }
            lesson?.Dispose();
        }

        void StartLesson()
        {
            string id = LessonLaunch.LessonId;
            if (autodrome != null) { StartAutodrome(id); return; }
            if (string.IsNullOrEmpty(id)) return;
            var def = GuidedLessonRunner.LoadPack()?.Find(id);
            if (def == null) { Debug.LogWarning($"[Lesson] урок {id} не найден — свободная поездка"); return; }
            lesson = new GuidedLessonRunner(def, player, true);
            lesson.PlaceCar();
            lessonIntroLeft = LessonIntroSeconds;
            Debug.Log($"[Lesson] {def.title}: {lesson.Session.StepCount} шагов");
        }

        void StepLesson(float dt, LessonSignal signal)
        {
            if (lessonIntroLeft > 0f)
            {
                lessonIntroLeft -= dt;
                hints.Post("lesson", HintKind.Exercise, LessonControls.Format(lesson.Lesson.briefing), 0, true);
                return;
            }
            lesson.Tick(dt, -1, signal);
            hints.Post("lesson", HintKind.Exercise, lesson.Text, 0, true);
            if (lesson.Overspeed) hints.Post("lesson-speed", HintKind.Maneuver, lesson.SpeedText); else hints.Clear("lesson-speed");
            if (lesson.Session.Phase == GuidedPhase.Done && !lessonFinished)
            {
                lessonFinished = true;
                Debug.Log($"[Lesson] выполнен: {lesson.Lesson.id}, повторов шагов {lesson.Session.Rewinds}");
                ShowDebrief();
            }
        }

        // ------------------------------------------------------------------ автодром (T68)

        void StartAutodrome(string id)
        {
            if (string.IsNullOrEmpty(id)) return;   // сцена без задания — свободная езда по площадке
            bool exam = id == LessonLaunch.AutodromeExam;
            if (exam) autodrome.Begin(0, true);
            else
            {
                var def = GuidedLessonRunner.LoadPack()?.Find(id);
                int index = def != null ? autodrome.IndexOf(def.courseLesson) : -1;
                if (index < 0) { Debug.LogWarning($"[Autodrome] упражнение {id} не найдено — свободная езда по площадке"); return; }
                autodrome.Begin(index, false);
                lesson = new GuidedLessonRunner(def, player, false);
            }
            autodromeRun = true;
            director.TeleportsLocked = true;
            director.TransmissionLocked = exam;
            lessonIntroLeft = LessonIntroSeconds;
            Debug.Log(exam ? "[Autodrome] экзамен на площадке" : $"[Autodrome] упражнение {autodrome.Session.Lesson.title}");
        }

        void StepAutodrome(float dt, LessonSignal signal)
        {
            var s = autodrome.Session;
            if (s == null) return;
            while (faultsShown < s.Faults.Count) ReportFault(s.Faults[faultsShown++]);
            if (s.Phase != CoursePhase.Running)
            {
                if (!lessonFinished) { hints.Clear("lesson"); hints.Clear("lesson-speed"); ShowDebrief(); }
                return;
            }
            if (lessonIntroLeft > 0f)
            {
                lessonIntroLeft -= dt;
                hints.Post("lesson", HintKind.Exercise, LessonControls.Format(AutodromeIntro(s)), 0, true);
                return;
            }
            if (lesson != null)
            {
                lesson.Tick(dt, s.Transferring ? -1 : s.GateIndex, signal);
                hints.Post("lesson", HintKind.Exercise, lesson.Text, 0, true);
            }
            else hints.Post("lesson", HintKind.Exercise, ExamText(s), 0, true);
        }

        string AutodromeIntro(CourseSession s)
        {
            if (!s.Exam)
            {
                string b = lesson != null && !string.IsNullOrEmpty(lesson.Lesson.briefing) ? lesson.Lesson.briefing : s.Lesson.briefing;
                return b + " Ошибки — игровые баллы площадки; с " + s.Course.failPenalty + " баллов упражнение не зачтено.";
            }
            return $"Экзамен на площадке: {s.Course.lessons.Length} упражнений подряд по маршруту, без подсказок. Ошибки — игровые баллы, " +
                   $"с {s.Course.failPenalty} баллов экзамен не сдан. Подготовьте машину: ремень, двигатель, передача — и троньтесь с левым указателем.";
        }

        static string ExamText(CourseSession s)
        {
            var g = s.CurrentGate;
            if (s.Transferring) return "Следующее упражнение: " + s.Course.lessons[System.Math.Min(s.LessonIndex + 1, s.Course.lessons.Length - 1)].title +
                                       ". Маршрут: " + (g != null ? g.instruction.ToLowerInvariant() : "") + " — следуйте рамке на земле.";
            return s.Lesson.title + ". " + s.Lesson.briefing;
        }

        string AutodromeTitle()
        {
            var s = autodrome.Session;
            string points = $"БАЛЛЫ {s.Penalty} ИЗ {s.Course.failPenalty}";
            // Номер упражнения — в его названии (У1…У9 по порядку маршрута), а не порядковый индекс со стартом и финишем.
            if (s.Exam) return lessonIntroLeft > 0f ? "ЭКЗАМЕН НА ПЛОЩАДКЕ" : $"ЭКЗАМЕН · {s.Lesson.title.ToUpperInvariant()} · {points}";
            string title = s.Lesson.title.ToUpperInvariant();
            if (lessonIntroLeft > 0f || lesson == null) return title;
            return title + " · " + lesson.Progress + " · " + points;
        }

        void ReportFault(CourseFault f)
        {
            var s = autodrome.Session;
            string title = f.title, advice = f.advice ?? "", reference;
            string where = "Упражнение: " + s.Course.lessons[f.lessonIndex].title + (f.transfer ? " (переезд)" : "");
            if (!string.IsNullOrEmpty(f.ruleId))
            {
                var e = DriveRuleCatalog.Get(f.ruleId);
                if (string.IsNullOrEmpty(advice)) advice = e.Advice;
                reference = e.Reference + "\n" + where;
            }
            else reference = where;
            reference += $"\n{f.points} б. — игровые баллы площадки, не методика ГИБДД (T37)" + (f.terminal ? "; попытка окончена" : "");
            bool severe = f.terminal || f.points >= 3;
            var pos = player.transform.position;
            log.Add(new DriveLogEvent { RuleId = "AUTODROME_" + (f.code ?? "FAULT").ToUpperInvariant(), Title = title, Advice = advice, Reference = reference, Severe = severe, X = pos.x, Z = pos.z });
            hud.ShowCard(title, f.points + " б. · " + s.Course.lessons[f.lessonIndex].title, advice, severe);
            if (!string.IsNullOrEmpty(advice)) hints.Post("err-" + (f.code ?? "fault"), HintKind.Error, advice, DriveHudView.CardSeconds);
            Debug.Log($"[Autodrome] ошибка {f.code}: {title}, {f.points} б., всего {s.Penalty}");
        }

        DebriefModel BuildAutodromeDebrief(CourseSession s)
        {
            var c = s.Course;
            var m = new DebriefModel();
            bool passed = s.Phase == CoursePhase.Passed, failed = s.Phase == CoursePhase.Failed;
            m.title = s.Exam ? "Экзамен на площадке" : s.Lesson.title;
            m.subtitle = passed ? (s.Exam ? "Сдано" : "Упражнение выполнено")
                       : failed ? (s.Exam ? "Не сдано" : "Упражнение не выполнено") + " — " + (s.Faults.Count > 0 ? s.Faults[s.Faults.Count - 1].title : s.Message)
                       : "Прервано";
            m.subtitle += " · баллы игровые, не методика ГИБДД (T37)";
            m.summary.Add(("Результат", passed ? "зачёт" : failed ? "незачёт" : "прервано"));
            m.summary.Add(("Баллы", $"{s.Penalty} (незачёт с {c.failPenalty})"));
            if (s.Exam) m.summary.Add(("Упражнения", $"{System.Math.Min(s.LessonsCompleted, c.lessons.Length)} из {c.lessons.Length}"));
            else if (lesson != null && lesson.Session.Rewinds > 0) m.summary.Add(("Возвраты к шагам", lesson.Session.Rewinds.ToString()));
            int t = (int)s.Elapsed;
            m.summary.Add(("Время", $"{t / 60}:{t % 60:00}"));
            m.summary.Add(("Ошибки", s.Faults.Count.ToString()));
            m.summary.Add(("Коробка передач", player != null && player.Adapter != null && player.Adapter.transmission == TransmissionType.Automatic ? "АКПП" : "МКПП"));
            foreach (var e in log.Events)
            {
                int et = (int)e.Seconds;
                m.events.Add(new DebriefEvent
                {
                    time = $"{et / 60:00}:{et % 60:00}", title = e.Title, advice = e.Advice, severe = e.Severe,
                    reference = e.Reference, place = $"автодром · x {e.X:0}, z {e.Z:0} м",
                });
            }
            return m;
        }

        void CreateMinimap()
        {
            minimapRt = new RenderTexture(MinimapSize, MinimapSize, 16) { name = "Minimap" };
            var go = new GameObject("MinimapCamera");
            go.transform.SetParent(transform, false);
            minimapCam = go.AddComponent<Camera>();
            minimapCam.orthographic = true; minimapCam.orthographicSize = MinimapHalfExtentM;
            minimapCam.clearFlags = CameraClearFlags.SolidColor; minimapCam.backgroundColor = new Color(0.12f, 0.16f, 0.12f);
            minimapCam.nearClipPlane = 1f; minimapCam.farClipPlane = 400f;
            minimapCam.targetTexture = minimapRt; minimapCam.depth = -20; minimapCam.useOcclusionCulling = false;
            var data = minimapCam.GetUniversalAdditionalCameraData();
            data.renderShadows = false; data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
        }

        void Update()
        {
            if (player == null || hud == null) return;
            var s = SettingsService.Current;
            float dt = Time.deltaTime;
            var state = player.Adapter != null ? player.Adapter.CurrentState : default;
            var pos = player.transform.position;
            if (dt > 0f) Step(state, pos, dt);

            hints.Mode = (HintMode)Mathf.Clamp(s.gameplay.instructorHints, 0, 2);
            hints.Tick(dt);
            Fill(state, s);
            hud.Render(model);

            if (minimapCam != null)
            {
                bool on = model.minimapAvailable && s.gameplay.hud == 0 && Time.timeScale > 0f;
                minimapCam.enabled = on;
                minimapCam.transform.SetPositionAndRotation(pos + Vector3.up * 150f, Quaternion.Euler(90f, 0f, 0f));   // север вверху
            }
        }

        void Step(VehicleState state, Vector3 pos, float dt)
        {
            log.Sample(state.signedSpeedMps, dt);
            bool onCrossing = TestRangeLayout.OnRailwayCrossing(pos);
            float impact = 0f;
            if (player.LastImpactTime != lastImpactTime) { lastImpactTime = player.LastImpactTime; impact = player.LastImpactSpeedMps; }
            var input = new DriveRuleInput
            {
                Seconds = log.ElapsedSeconds, SpeedMps = state.signedSpeedMps, Seatbelt = state.seatbelt,
                LowOrHighBeam = state.lowBeam || state.highBeam,
                EnteredRailwayCrossing = onCrossing && !wasOnCrossing,
                RailwayClosed = director.crossing != null && director.crossing.IsClosedForTraffic,
                ImpactSpeedMps = impact, X = pos.x, Z = pos.z,
            };
            var fwd = player.transform.forward; var front = pos + fwd * 2.2f;
            if (stopLine.Update(junction, front, fwd, out var aspect) && SignalJunction.Prohibits(aspect)) input.CrossedStopLineOnRed = true;
            var signal = junction == null ? SignalJunction.Signal.None : junction.SignalFor(front, fwd);
            wasOnCrossing = onCrossing;
            if (player.Keyboard.ShiftLockRefused && !hints.Has("shift-lock") && !shiftLockShown)
                hints.Post("shift-lock", HintKind.Error, "Селектор не сдвинулся: из P он выходит только с нажатым тормозом. Держите " + LessonControls.Format("{brake}") + " и переключайте.", 5, true);
            shiftLockShown = player.Keyboard.ShiftLockRefused;
            foreach (var ev in rules.Update(input)) Report(ev, pos);
            StepCity(pos);
            if (autodrome != null) speedLimitKph = autodrome.Course.speedLimitKph;   // T68: ограничение площадки для прибора HUD
            var lessonSignal = signal == SignalJunction.Signal.Stop ? LessonSignal.Stop : signal == SignalJunction.Signal.Go ? LessonSignal.Go : LessonSignal.None;
            if (autodromeRun) StepAutodrome(dt, lessonSignal);
            else if (lesson != null) StepLesson(dt, lessonSignal);
            else if (testRange) instructor.Update(hints, state, pos, player.transform.forward, director.crossing);
        }

        /// <summary>Правила города по фактам графа (T65): один раз на тик диспетчера трафика.</summary>
        void StepCity(Vector3 pos)
        {
            speedLimitKph = 0f;
            if (road == null || traffic == null || traffic.Director == null) return;
            if (traffic.Director.SimSeconds == lastRoadSeconds) { speedLimitKph = shownLimitKph > 0 ? shownLimitKph : 0f; return; }
            lastRoadSeconds = traffic.Director.SimSeconds;
            var facts = road.Update();
            speedLimitKph = facts.OnRoad ? facts.SpeedLimitKph : 0f;
            foreach (var ev in cityRules.Update(facts)) Report(ev, pos);
            // Новое ограничение скорости — инструктор напомнит (советы, не ошибки).
            if (facts.OnRoad && Mathf.Abs(speedLimitKph - shownLimitKph) > 0.5f)
            {
                if (shownLimitKph > 0f) hints.Post("speed-limit", HintKind.Maneuver, $"Ограничение скорости {speedLimitKph:0} км/ч.", 4);
                shownLimitKph = speedLimitKph;
            }
        }

        void Report(RuleEvent ev, Vector3 pos)
        {
            if (autodromeRun && autodrome.Session != null && autodrome.Session.Phase == CoursePhase.Running)
            {
                // Касания считает площадка (конусы), свет на закрытой площадке не требуется;
                // красный, ремень и др. из таблицы площадки становятся ошибкой упражнения (ReportFault).
                if (ev.ruleId == DriveRuleMonitor.RuleCollision || ev.ruleId == DriveRuleMonitor.RuleLowBeam) return;
                if (autodrome.Session.PenalizeRule(ev.ruleId)) return;
            }
            var e = DriveRuleCatalog.Get(ev.ruleId);
            log.Add(new DriveLogEvent { RuleId = ev.ruleId, Title = e.Title, Advice = e.Advice, Reference = e.Reference, Severe = e.Severe, X = pos.x, Z = pos.z });
            hud.ShowCard(e.Title, e.Reference, e.Advice, e.Severe);   // код правила — в разборе
            hints.Post("err-" + ev.ruleId, HintKind.Error, e.Advice, DriveHudView.CardSeconds);   // замечание инструктора после нарушения
            Debug.Log($"[Drive] нарушение {ev.ruleId} в {PlaceName(pos)}");
        }

        void Fill(VehicleState st, GameSettings s)
        {
            model.hudMode = Mathf.Clamp(s.gameplay.hud, 0, 2);
            model.cockpit = rig != null && rig.mode == DriverCameraRig.Mode.Cockpit;
            model.speedKph = Mathf.Abs(st.signedSpeedMps) * 3.6f;
            // Ограничение — из знаков района (T65); на полигоне вне района ограничений нет.
            model.overLimit = speedLimitKph > 0f && model.speedKph > speedLimitKph + CityRuleMonitor.SpeedToleranceKph;
            model.manual = st.transmission == TransmissionType.Manual;
            model.gear = st.gear; model.gearCount = gearCount;
            model.selector = st.selector == AutomaticSelector.P ? 0 : st.selector == AutomaticSelector.R ? 1 : st.selector == AutomaticSelector.N ? 2 : 3;
            model.rpm = st.engineRpm; model.redlineRpm = redline;
            model.leftIndicator = st.leftIndicator; model.rightIndicator = st.rightIndicator; model.indicatorLamp = st.indicatorLampOn;
            model.lowBeam = st.lowBeam; model.highBeam = st.highBeam; model.handbrake = st.handbrake; model.seatbelt = st.seatbelt;
            model.engineRunning = st.engine == EnginePhase.Running; model.stalled = st.engine == EnginePhase.Stalled;
            var h = hints.Current;
            model.hintText = h?.Text; model.hintKind = h != null ? (int)h.Kind : -1; model.hintWaiting = hints.Waiting;
            model.hintTitle = autodromeRun && h != null && h.Key == "lesson" ? AutodromeTitle()
                : lesson != null && h != null && h.Key == "lesson"
                ? (lessonIntroLeft > 0f ? lesson.Lesson.title.ToUpperInvariant() : lesson.Lesson.title.ToUpperInvariant() + " · " + lesson.Progress) : null;
            model.remarks = log.Events.Count; model.severe = log.SevereCount;
            model.minimap = minimapRt; model.minimapAvailable = minimapRt != null;
            model.carYawDeg = player.transform.eulerAngles.y;
        }

        /// <summary>Разбор поездки из журнала (docs/ui-drive.md §5).</summary>
        public DebriefModel BuildDebrief()
        {
            if (autodromeRun && autodrome.Session != null) return BuildAutodromeDebrief(autodrome.Session);
            var s = SettingsService.Current;
            var m = new DebriefModel
            {
                title = "Разбор поездки",
                subtitle = "Тренировка · тестовый полигон · баллы по методике ГИБДД появятся с таблицей ошибок (T37)",
            };
            int t = (int)log.ElapsedSeconds;
            if (lesson != null)
            {
                bool done = lesson.Session.Phase == GuidedPhase.Done;
                m.title = lesson.Lesson.title;
                string where = string.IsNullOrEmpty(lesson.Lesson.place) ? "" : " · " + lesson.Lesson.place;
                m.subtitle = (done ? "Урок выполнен" : "Урок не завершён") + where;
                m.summary.Add(("Урок", done ? "выполнен" : $"шаг {lesson.Session.StepIndex + 1} из {lesson.Session.StepCount}"));
                if (lesson.Session.Rewinds > 0) m.summary.Add(("Возвраты к шагам", lesson.Session.Rewinds.ToString()));
            }
            m.summary.Add(("Время", $"{t / 60}:{t % 60:00}"));
            m.summary.Add(("Дистанция", log.DistanceM >= 1000 ? $"{log.DistanceM / 1000:0.00} км" : $"{log.DistanceM:0} м"));
            m.summary.Add(("Макс. скорость", $"{log.MaxSpeedMps * 3.6:0} км/ч"));
            m.summary.Add(("Замечания", log.SevereCount > 0 ? $"{log.Events.Count}, грубых {log.SevereCount}" : log.Events.Count.ToString()));
            m.summary.Add(("Коробка передач", player != null && player.Adapter != null && player.Adapter.transmission == TransmissionType.Automatic ? "АКПП" : "МКПП"));
            m.summary.Add(("Помощники", s.gameplay.antiStall || s.gameplay.autoClutch ? "включены в настройках (пока не работают)" : "нет"));
            foreach (var e in log.Events)
            {
                int et = (int)e.Seconds;
                m.events.Add(new DebriefEvent
                {
                    time = $"{et / 60:00}:{et % 60:00}", title = e.Title, advice = e.Advice, severe = e.Severe,
                    reference = $"{e.RuleId}\n{e.Reference}\nВес: не сверен (T37)",
                    place = $"{PlaceName(new Vector3((float)e.X, 0, (float)e.Z))} · x {e.X:0}, z {e.Z:0} м",
                });
            }
            return m;
        }

        string PlaceName(Vector3 p)
        {
            if (autodrome != null) return "автодром";
            if (testRange) return TestRangeLayout.ZoneName(p);
            if (junction != null && Vector3.Distance(new Vector3(p.x, 0, p.z), junction.transform.position) < LessonStreetLayout.JunctionHalf + 4f) return "перекрёсток";
            return "улица";
        }

        void ShowDebrief()
        {
            if (autodromeRun)
            {
                lessonFinished = true;
                if (autodrome.Session != null && autodrome.Session.Phase == CoursePhase.Running) autodrome.Cancel();   // «Завершить поездку» в паузе
            }
            RecordProgress();
            if (debrief == null) { AppNavigator.ToMainMenu(); return; }
            debrief.Show(BuildDebrief());
        }

        bool progressRecorded;

        /// <summary>
        /// T70: законченная попытка задания — в профиль ученика (прогресс на экране «Задания»). Упражнение и экзамен
        /// площадки — зачёт/незачёт с баллами площадки; урок — только выполненный (баллы — число замечаний).
        /// Прерванная попытка («Завершить поездку» до конца) не записывается; свободная езда — тоже.
        /// </summary>
        void RecordProgress()
        {
            if (progressRecorded) return;
            string id = LessonLaunch.LessonId;
            if (string.IsNullOrEmpty(id)) return;
            if (autodromeRun && autodrome != null && autodrome.Session != null)
            {
                var s = autodrome.Session;
                if (s.Phase != CoursePhase.Passed && s.Phase != CoursePhase.Failed) return;
                progressRecorded = true;
                ProfileService.RecordAssignment(id, s.Phase == CoursePhase.Passed, s.Penalty, s.Elapsed);
                return;
            }
            if (lesson != null && lesson.Session.Phase == GuidedPhase.Done)
            {
                progressRecorded = true;
                ProfileService.RecordAssignment(id, true, log.Events.Count, (float)log.ElapsedSeconds);
            }
        }
    }
}
