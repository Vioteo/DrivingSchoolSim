using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;

namespace DrivingSchool.Presentation
{
    /// <summary>Input source driven by a script (self-check, demos).</summary>
    public sealed class ScriptedInputSource : IInputSource
    {
        public DriverCommand Command;
        public bool IsConnected => true;
        public DriverCommand Read(long tick) { var c = Command; c.sequence = tick; c.Validate(); return c; }
    }

    /// <summary>
    /// Vehicle test range: technical HUD (IMGUI stand), hotkeys and an automated self-check of the car's
    /// systems. Launch with "--selfcheck" (player) to run the check at start and quit with exit code 0/1.
    /// F4 help, F7 МКПП/АКПП, F8 self-check, F9/Backspace respawn, F10 crash-test spawn, F5/F6 weather.
    /// </summary>
    public sealed class VehicleTestRangeDirector : MonoBehaviour
    {
        public VehicleController player;
        public WeatherController weather;
        public DriverCameraRig cameraRig;
        public VehicleMirrorRig mirrors;
        public DashboardView dashboard;
        public VehicleLightsView lights;
        public VehicleVisuals visuals;
        public WindshieldRainView windshield;
        public Transform spawn, crashSpawn;
        public Rigidbody obstacleCar;

        bool showHelp = true, running;
        readonly List<string> report = new List<string>();
        string reportSummary = "";
        GUIStyle label, small, box;
        Vector3 obstacleStart; Quaternion obstacleStartRot;

        void Awake()
        {
            // The tyre model is calibrated for a 100 Hz step (docs/architecture.md); project default is 50 Hz.
            Time.fixedDeltaTime = 0.01f;
        }

        void Start()
        {
            if (obstacleCar != null) { obstacleStart = obstacleCar.position; obstacleStartRot = obstacleCar.rotation; }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--selfcheck") >= 0) StartCoroutine(SelfCheck(true));
        }

        void Update()
        {
            var kb = Keyboard.current; if (kb == null || running) return;
            if (kb.f4Key.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.f7Key.wasPressedThisFrame) ToggleTransmission();
            if (kb.f9Key.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) Respawn(spawn);
            if (kb.f10Key.wasPressedThisFrame) Respawn(crashSpawn);
            if (kb.f8Key.wasPressedThisFrame) StartCoroutine(SelfCheck(false));
        }

        public void ToggleTransmission()
        {
            var a = player.Adapter;
            a.SetTransmission(a.transmission == TransmissionType.Manual ? TransmissionType.Automatic : TransmissionType.Manual);
            player.Keyboard.ResetToParked();
        }

        public void Respawn(Transform at)
        {
            if (at == null) return;
            player.ResetAt(at.position + Vector3.up * 0.05f, at.rotation);
            if (obstacleCar != null && at == crashSpawn)
            {
                obstacleCar.position = obstacleStart; obstacleCar.rotation = obstacleStartRot;
#if UNITY_6000_0_OR_NEWER
                obstacleCar.linearVelocity = Vector3.zero;
#else
                obstacleCar.velocity = Vector3.zero;
#endif
                obstacleCar.angularVelocity = Vector3.zero;
            }
        }

        // ------------------------------------------------------------------ self-check

        void Check(string name, bool ok, string detail = "")
        {
            string line = (ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " — " + detail : "");
            report.Add(line); Debug.Log("VEHICLE_SELFCHECK " + line);
        }

        IEnumerator Hold(ScriptedInputSource s, DriverCommand c, float seconds) { s.Command = c; yield return new WaitForSeconds(seconds); }

        IEnumerator SelfCheck(bool quit)
        {
            running = true; report.Clear(); reportSummary = "выполняется…";
            var src = new ScriptedInputSource(); var prevSource = player.Source; player.Source = src;
            var a = player.Adapter; var prevWeather = weather != null ? weather.preset : WeatherPreset.ClearDay;
            if (a.transmission != TransmissionType.Manual) a.SetTransmission(TransmissionType.Manual);
            Respawn(spawn);
            yield return new WaitForSeconds(0.5f);

            var baseCmd = new DriverCommand { ignition = true, clutch = 1f, handbrake = false, seatbelt = true };
            // 1. Engine start (clutch pressed), bulb check ends.
            var c = baseCmd; c.starter = true; yield return Hold(src, c, 1.2f);
            yield return Hold(src, baseCmd, 2.2f);
            Check("Двигатель запускается со стартера", a.CurrentState.engine == EnginePhase.Running, $"{a.CurrentState.engineRpm:F0} об/мин");
            if (dashboard != null) Check("Приборы: лампы аккумулятора/масла гаснут после запуска", !dashboard.IsLit(DashboardView.Telltale.Battery) && !dashboard.IsLit(DashboardView.Telltale.Oil));

            // 2. Lights.
            c = baseCmd; c.headlights = HeadlightMode.Parking; yield return Hold(src, c, 0.3f);
            Check("Габаритные огни", a.CurrentState.parkingLights && !a.CurrentState.lowBeam);
            c.headlights = HeadlightMode.LowBeam; yield return Hold(src, c, 0.3f);
            Check("Ближний свет", a.CurrentState.lowBeam && (dashboard == null || dashboard.IsLit(DashboardView.Telltale.LowBeam)));
            c.highBeam = true; yield return Hold(src, c, 0.3f);
            Check("Дальний свет + синяя лампа на панели", a.CurrentState.highBeam && (dashboard == null || dashboard.IsLit(DashboardView.Telltale.HighBeam)));
            if (lights != null) Check("Найдены рассеиватели фар/фонарей/поворотников", lights.CountRole(VehicleLightsView.LampRole.Headlamp) > 0 && lights.CountRole(VehicleLightsView.LampRole.Tail) > 0 && lights.LampSlotCount >= 4, $"слотов: {lights.LampSlotCount}");

            // 3. Indicators flash and show on the dashboard.
            c = baseCmd; c.turnSignal = TurnSignal.Left; src.Command = c;
            int toggles = 0; bool prev = false, dashSeen = false, wrongSide = false;
            for (float t = 0; t < 2.1f; t += Time.deltaTime)
            {
                var st = a.CurrentState; bool on = st.indicatorLampOn && st.leftIndicator;
                if (on != prev) toggles++; prev = on; wrongSide |= st.rightIndicator;
                if (dashboard != null && dashboard.IsLit(DashboardView.Telltale.TurnLeft)) dashSeen = true;
                yield return null;
            }
            Check("Левый поворотник мигает ~1,5 Гц", toggles >= 5 && toggles <= 8 && !wrongSide, $"переключений за 2,1 с: {toggles}");
            Check("Стрелка поворотника на панели", dashboard == null || dashSeen);
            c = baseCmd; c.hazard = true; yield return Hold(src, c, 0.4f);
            Check("Аварийная сигнализация", a.CurrentState.leftIndicator && a.CurrentState.rightIndicator);

            // 4. Brake light.
            c = baseCmd; c.brake = 0.6f; yield return Hold(src, c, 0.2f);
            Check("Стоп-сигнал", a.CurrentState.brakeLight);

            // 5. Steering wheel and front wheels.
            c = baseCmd; c.steering = -1f; yield return Hold(src, c, 0.4f);
            float swDeg = a.Solver.SteeringWheelDeg;
            Check("Руль поворачивается (±450° до упора)", Mathf.Abs(swDeg + 450f) < 1f, $"{swDeg:F0}°");
            Check("Передние колёса поворачиваются (Аккерман)", a.Solver.Wheels[0].steerAngleRad < -0.4f && Mathf.Abs(a.Solver.Wheels[0].steerAngleRad) > Mathf.Abs(a.Solver.Wheels[1].steerAngleRad));

            // 6. Pull away in first gear, wheels spin, then brake.
            c = baseCmd; c.requestedGear = 1; yield return Hold(src, c, 0.3f);
            float spin0 = a.Solver.Wheels[2].spinAngleRad; float travelled = 0f;
            for (float t = 0; t < 3f; t += Time.fixedDeltaTime)
            {
                c.clutch = Mathf.Max(0f, 1f - t / 1.8f); c.throttle = 0.35f; src.Command = c;
                travelled += Mathf.Abs(a.Solver.Wheels[2].angularVelocityRadS) * Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            Check("Трогание на 1-й без заглохания", a.CurrentState.engine == EnginePhase.Running && a.CurrentState.signedSpeedMps > 1f, $"{a.CurrentState.signedSpeedMps * 3.6f:F1} км/ч");
            Check("Колёса вращаются", travelled > 3f, $"{travelled:F1} рад");
            c = baseCmd; c.requestedGear = 1; c.brake = 1f; yield return Hold(src, c, 2.5f);
            Check("Торможение до остановки", Mathf.Abs(a.CurrentState.signedSpeedMps) < 0.1f);

            // 7. Reverse light.
            c = baseCmd; c.requestedGear = -1; c.brake = 1f; yield return Hold(src, c, 0.3f);
            Check("Фонарь заднего хода", a.CurrentState.reverseLight);

            // 8. Stall on clutch dump.
            c = baseCmd; c.requestedGear = 1; yield return Hold(src, c, 0.3f);
            c.clutch = 0f; yield return Hold(src, c, 1.5f);
            Check("Двигатель глохнет при броске сцепления", a.CurrentState.engine == EnginePhase.Stalled);
            if (dashboard != null) Check("Лампа Check Engine при заглохшем двигателе", dashboard.IsLit(DashboardView.Telltale.CheckEngine));

            // 9. Rain and wipers.
            if (weather != null && windshield != null && windshield.Water != null)
            {
                weather.SetPreset(WeatherPreset.HeavyRain, true); windshield.Water.Clear();
                c = baseCmd; c.handbrake = true; yield return Hold(src, c, 4f);
                float wet = windshield.Water.Coverage;
                c.wipers = WiperMode.High; yield return Hold(src, c, 0.95f);
                float wiped = windshield.Water.Coverage;
                Check("Дождь намокает на стекле", wet > 0.05f, $"покрытие {wet:P0}");
                Check("Дворники убирают воду", wiped < wet * 0.75f && windshield.BladeCount > 0, $"{wet:P0} → {wiped:P0}, щёток: {windshield.BladeCount}");
                Check("Мокрая дорога снижает сцепление", a.surface == SurfaceType.WetAsphalt);
                c.wipers = WiperMode.Off; yield return Hold(src, c, 1.5f);
                Check("Дворники паркуются", a.CurrentState.wiperAngle01 == 0f);
                weather.SetPreset(WeatherPreset.ClearNight, true); yield return Hold(src, c, 0.3f);
                Check("Ночь: солнце почти выключено", weather.sun == null || weather.sun.intensity < 0.1f);
                weather.SetPreset(prevWeather, true);
            }

            // 10. Mirrors.
            if (mirrors != null)
            {
                int ok = 0;
                foreach (VehicleMirrorRig.Slot s in Enum.GetValues(typeof(VehicleMirrorRig.Slot)))
                {
                    var m = mirrors.Get(s); if (m != null && m.Texture != null && m.MirrorCamera != null) ok++;
                }
                Check("Три зеркала рендерятся в текстуры", ok == 3, $"{ok}/3");
            }

            // 11. Collision with the parked car.
            if (crashSpawn != null && obstacleCar != null)
            {
                Respawn(crashSpawn); int hits0 = player.CollisionCount; yield return new WaitForSeconds(0.3f);
                c = baseCmd; c.starter = true; yield return Hold(src, c, 1.2f);
                c = baseCmd; c.requestedGear = 1; yield return Hold(src, c, 0.3f);
                for (float t = 0; t < 8f && player.CollisionCount == hits0; t += Time.fixedDeltaTime)
                {
                    c.clutch = Mathf.Max(0f, 1f - t / 1.5f); c.throttle = 0.5f; src.Command = c; yield return new WaitForFixedUpdate();
                }
                c = baseCmd; c.brake = 1f; yield return Hold(src, c, 1.5f);
                float moved = Vector3.Distance(obstacleCar.position, obstacleStart);
                Check("Столкновение с машиной фиксируется", player.CollisionCount > hits0, $"удар {player.LastImpactSpeedMps * 3.6f:F1} км/ч о «{player.LastImpactWith}»");
                Check("Препятствие смещается от удара", moved > 0.02f, $"{moved:F2} м");
            }

            player.Source = prevSource; Respawn(spawn);
            int fails = report.FindAll(l => l.StartsWith("FAIL")).Count;
            reportSummary = fails == 0 ? $"PASS: {report.Count} проверок" : $"FAIL: {fails} из {report.Count}";
            Debug.Log("VEHICLE_SELFCHECK " + reportSummary);
            try
            {
                Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, "reports"));
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "reports", "vehicle-selfcheck.txt"), string.Join("\n", report) + "\n" + reportSummary + "\n");
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
            running = false;
            if (quit) Application.Quit(fails == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (player == null || player.Adapter == null || player.Adapter.Solver == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
                box = new GUIStyle(GUI.skin.box);
            }
            var a = player.Adapter; var st = a.CurrentState; var cmd = a.LastCommand; var s = a.Solver;
            GUI.Box(new Rect(12, 12, 340, 400), GUIContent.none, box);
            GUILayout.BeginArea(new Rect(22, 18, 322, 390));
            GUILayout.Label($"<b>{Mathf.Abs(st.signedSpeedMps) * 3.6f:F0} км/ч</b>   {st.engineRpm:F0} об/мин   <b>{(dashboard != null ? dashboard.GearText : st.gear.ToString())}</b>", label);
            GUILayout.Label($"КПП: {(st.transmission == TransmissionType.Manual ? "механика (F7 → автомат)" : "автомат (F7 → механика)")}", small);
            GUILayout.Label($"Двигатель: {Phase(st.engine)}   {(s.StarterInhibited && cmd.starter ? "<color=orange>стартер заблокирован: выжмите сцепление / N / P</color>" : "")}", small);
            GUILayout.Label($"Сцепление: педаль {cmd.clutch:P0}, {(s.ClutchLocked ? "замкнуто" : "пробуксовка")} {st.clutchTorqueNm:F0} Н·м", small);
            GUILayout.Label($"Газ {cmd.throttle:P0}  Тормоз {cmd.brake:P0}  Ручник {(cmd.handbrake ? "<color=red>ВКЛ</color>" : "выкл")}", small);
            GUILayout.Label($"Руль {s.SteeringWheelDeg:F0}°   колёса {s.Wheels[0].steerAngleRad * Mathf.Rad2Deg:F0}°/{s.Wheels[1].steerAngleRad * Mathf.Rad2Deg:F0}°", small);
            string ind = (st.leftIndicator ? (st.indicatorLampOn ? "<color=lime>◀</color>" : "◁") : "  ") + " " + (st.rightIndicator ? (st.indicatorLampOn ? "<color=lime>▶</color>" : "▷") : "  ");
            GUILayout.Label($"Поворотники {ind}  {(st.hazard ? "<color=red>аварийка</color>" : "")}", small);
            GUILayout.Label($"Свет: {(st.highBeam ? "<color=#5af>дальний</color>" : st.lowBeam ? "<color=lime>ближний</color>" : st.parkingLights ? "габариты" : "выкл")}   Стоп {(st.brakeLight ? "<color=red>●</color>" : "○")}  ЗХ {(st.reverseLight ? "●" : "○")}", small);
            GUILayout.Label($"Дворники: {Wipers(st.wipers)}  Ремень: {(cmd.seatbelt ? "пристёгнут" : "<color=red>не пристёгнут</color>")}", small);
            GUILayout.Label($"Погода: {WeatherController.Current.preset}  Покрытие: {a.surface}  μ={Simulation.SurfaceFrictionModel.GetFrictionCoefficient(a.surface):F2}", small);
            GUILayout.Label($"Столкновений: {player.CollisionCount}" + (Time.time - player.LastImpactTime < 4f ? $"  <color=orange>удар {player.LastImpactSpeedMps * 3.6f:F0} км/ч</color>" : ""), small);
            if (mirrors != null) GUILayout.Label($"Зеркало: {mirrors.Selected}  (F1/F2/F3, NumPad 8/2/4/6)", small);
            if (!string.IsNullOrEmpty(reportSummary)) GUILayout.Label("Самопроверка: " + reportSummary, small);
            GUILayout.EndArea();

            if (showHelp)
            {
                float w = 380, x = Screen.width - w - 12;
                GUI.Box(new Rect(x, 12, w, 400), GUIContent.none, box);
                GUILayout.BeginArea(new Rect(x + 10, 18, w - 20, 390));
                GUILayout.Label("<b>Управление</b> (F4 — скрыть)", label);
                GUILayout.Label(
                    "I — зажигание, Enter — стартер (сцепление выжато / N / P)\n" +
                    "W/S — газ/тормоз, A/D — руль, Shift — сцепление, Space — ручник\n" +
                    "1–6, R, N — передачи; в автомате 1–6 = D, R, N, P\n" +
                    "Q/E — поворотники (автоотключение после поворота), X — аварийка\n" +
                    "L — габариты → ближний → выкл, K — дальний, J — моргнуть, H — сигнал\n" +
                    "V — дворники (выкл/прерывистый/1/2), B — омыватель, T — ремень\n" +
                    "C — камера (салон/сзади/облёт), ПКМ или Z , . — осмотреться\n" +
                    "F1/F2/F3 + NumPad 8/2/4/6 (Home/End/Del/PgDn) — регулировка зеркал\n" +
                    "F5/F6 — погода, F7 — МКПП/АКПП, F8 — самопроверка\n" +
                    "F9/Backspace — на старт, F10 — к машине для столкновения", small);
                if (report.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var l in report) sb.AppendLine(l.StartsWith("PASS") ? "<color=lime>" + l + "</color>" : "<color=red>" + l + "</color>");
                    GUILayout.Label(sb.ToString(), small);
                }
                GUILayout.EndArea();
            }
        }

        static string Phase(EnginePhase p) => p == EnginePhase.Running ? "работает" : p == EnginePhase.Cranking ? "стартер" : p == EnginePhase.Stalled ? "<color=red>заглох</color>" : p == EnginePhase.Ignition ? "зажигание" : "выключен";
        static string Wipers(WiperMode m) => m == WiperMode.Off ? "выкл" : m == WiperMode.Interval ? "прерывистый" : m == WiperMode.Low ? "1 скорость" : "2 скорость";
    }
}
