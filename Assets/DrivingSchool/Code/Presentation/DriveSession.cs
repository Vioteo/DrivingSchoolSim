using DrivingSchool.Contracts;
using DrivingSchool.Learning;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Rules;
using DrivingSchool.Settings;
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
        const float LessonIntroSeconds = 5f;

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
            if (lesson != null) StepLesson(dt, signal == SignalJunction.Signal.Stop ? LessonSignal.Stop : signal == SignalJunction.Signal.Go ? LessonSignal.Go : LessonSignal.None);
            else if (testRange) instructor.Update(hints, state, pos, player.transform.forward, director.crossing);
        }

        void Report(RuleEvent ev, Vector3 pos)
        {
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
            model.overLimit = false;   // ограничений скорости на полигоне нет (нет знаков и данных дороги)
            model.manual = st.transmission == TransmissionType.Manual;
            model.gear = st.gear; model.gearCount = gearCount;
            model.selector = st.selector == AutomaticSelector.P ? 0 : st.selector == AutomaticSelector.R ? 1 : st.selector == AutomaticSelector.N ? 2 : 3;
            model.rpm = st.engineRpm; model.redlineRpm = redline;
            model.leftIndicator = st.leftIndicator; model.rightIndicator = st.rightIndicator; model.indicatorLamp = st.indicatorLampOn;
            model.lowBeam = st.lowBeam; model.highBeam = st.highBeam; model.handbrake = st.handbrake; model.seatbelt = st.seatbelt;
            model.engineRunning = st.engine == EnginePhase.Running; model.stalled = st.engine == EnginePhase.Stalled;
            var h = hints.Current;
            model.hintText = h?.Text; model.hintKind = h != null ? (int)h.Kind : -1; model.hintWaiting = hints.Waiting;
            model.hintTitle = lesson != null && h != null && h.Key == "lesson"
                ? (lessonIntroLeft > 0f ? lesson.Lesson.title.ToUpperInvariant() : lesson.Lesson.title.ToUpperInvariant() + " · " + lesson.Progress) : null;
            model.remarks = log.Events.Count; model.severe = log.SevereCount;
            model.minimap = minimapRt; model.minimapAvailable = minimapRt != null;
            model.carYawDeg = player.transform.eulerAngles.y;
        }

        /// <summary>Разбор поездки из журнала (docs/ui-drive.md §5).</summary>
        public DebriefModel BuildDebrief()
        {
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
            if (testRange) return TestRangeLayout.ZoneName(p);
            if (junction != null && Vector3.Distance(new Vector3(p.x, 0, p.z), junction.transform.position) < LessonStreetLayout.JunctionHalf + 4f) return "перекрёсток";
            return "улица";
        }

        void ShowDebrief()
        {
            if (debrief == null) { AppNavigator.ToMainMenu(); return; }
            debrief.Show(BuildDebrief());
        }
    }
}
