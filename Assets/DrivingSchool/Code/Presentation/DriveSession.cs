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

        public DriveLog Log => log;
        public InstructorHintQueue Hints => hints;

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
            CreateMinimap();
            Debug.Log("[Drive] сессия полигона: HUD и инструктор включены");
        }

        void OnDestroy()
        {
            if (pause != null) pause.FinishRequested -= ShowDebrief;
            if (debrief != null) { debrief.RestartRequested -= AppNavigator.RestartDrive; debrief.MenuRequested -= AppNavigator.ToMainMenu; }
            if (minimapRt != null) { minimapRt.Release(); Destroy(minimapRt); }
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
            wasOnCrossing = onCrossing;
            foreach (var ev in rules.Update(input)) Report(ev, pos);
            instructor.Update(hints, state, pos, player.transform.forward, director.crossing);
        }

        void Report(RuleEvent ev, Vector3 pos)
        {
            var e = DriveRuleCatalog.Get(ev.ruleId);
            log.Add(new DriveLogEvent { RuleId = ev.ruleId, Title = e.Title, Advice = e.Advice, Reference = e.Reference, Severe = e.Severe, X = pos.x, Z = pos.z });
            hud.ShowCard(e.Title, e.Reference, e.Advice, e.Severe);   // код правила — в разборе
            hints.Post("err-" + ev.ruleId, HintKind.Error, e.Advice, DriveHudView.CardSeconds);   // замечание инструктора после нарушения
            Debug.Log($"[Drive] нарушение {ev.ruleId} в {TestRangeLayout.ZoneName(pos)}");
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
                    place = $"{TestRangeLayout.ZoneName(new Vector3((float)e.X, 0, (float)e.Z))} · x {e.X:0}, z {e.Z:0} м",
                });
            }
            return m;
        }

        void ShowDebrief()
        {
            if (debrief == null) { AppNavigator.ToMainMenu(); return; }
            debrief.Show(BuildDebrief());
        }
    }
}
