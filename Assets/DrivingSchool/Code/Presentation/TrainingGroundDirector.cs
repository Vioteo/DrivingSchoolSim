using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Learning;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Упражнения и экзамен автодрома (T68, docs/training-ground.md). Курс — course-v2.json; <see cref="CourseSession"/>
    /// шагает в FixedUpdate по состоянию машины игрока (позиция, скорость, передача, указатели, заглохание); касание
    /// конуса или ограждения — ошибка «contact»; рамка текущей цели лежит на земле (на экзамене — только на переездах).
    /// Подсказки, HUD, карточки ошибок и разбор ведёт <see cref="DriveSession"/>; сцена, открытая напрямую
    /// (PlayMode-тесты), работает через <see cref="Begin"/> без интерфейса. Результат попытки пишется в
    /// persistentDataPath/TrainingResults.
    /// </summary>
    public sealed class TrainingGroundDirector : MonoBehaviour
    {
        public TextAsset courseFile;
        public VehicleController vehicle;
        public Transform marker;
        TrainingCourse course;
        CourseSession session;
        int contacts;
        float lastContact = -10f;
        bool saved;
        const float ContactCooldown = 1.5f;   // один удар о конус — одна ошибка (игровой порог)

        public CourseSession Session => session;
        public TrainingCourse Course { get { Load(); return course; } }
        /// <summary>Что стало с сохранением результата: путь к файлу или причина ошибки; "" — ещё не сохраняли.</summary>
        public string SaveStatus { get; private set; } = "";

        void Start()
        {
            ResolveVehicle();
            Load();
            if (session == null && vehicle != null) PositionCar(0);
        }

        void ResolveVehicle()
        {
            // Машину выбирает PlayerVehicleSelector (гараж, M) и передаёт директору полигона.
            var range = FindAnyObjectByType<VehicleTestRangeDirector>();
            if (range != null && range.player != null) vehicle = range.player;
        }

        void Load()
        {
            if (course != null) return;
            course = JsonUtility.FromJson<TrainingCourse>(courseFile.text);
            CourseSession.Validate(course);
        }

        /// <summary>Номер упражнения по id курса; -1 — нет такого.</summary>
        public int IndexOf(string lessonId)
        {
            Load();
            for (int i = 0; i < course.lessons.Length; i++) if (course.lessons[i].id == lessonId) return i;
            return -1;
        }

        void PositionCar(int index)
        {
            var l = course.lessons[index];
            vehicle.ResetAt(new Vector3(l.startX, .05f, l.startZ), Quaternion.Euler(0, l.startYaw, 0));
        }

        /// <summary>Начать упражнение (<paramref name="fullExam"/> = false) или экзамен по всей площадке с первого упражнения.</summary>
        public void Begin(int lesson, bool fullExam)
        {
            Load();
            if (vehicle == null) ResolveVehicle();
            session?.Cancel();
            saved = false; SaveStatus = "";
            int index = fullExam ? 0 : lesson;
            PositionCar(index);
            session = new CourseSession(course, index, fullExam);
            session.Start();
            contacts = vehicle.CollisionCount;
            vehicle.inputEnabled = true;
        }

        /// <summary>Прервать попытку (пауза → «Завершить поездку»); результат сохраняется как отменённый.</summary>
        public void Cancel()
        {
            if (session == null) return;
            session.Cancel();
            SaveResult();
        }

        void Update()
        {
            if (session == null || vehicle == null) return;
            vehicle.inputEnabled = session.Phase == CoursePhase.Running;
            var gate = session.CurrentGate;
            if (!marker) return;
            // Остановка у стоп-линии (stopZone) — без рамки: ориентир — сама линия.
            marker.gameObject.SetActive(gate != null && gate.stopZone <= 0 && (!session.Exam || session.Transferring));
            if (gate == null) return;
            float y = UnityEngine.Physics.Raycast(new Vector3(gate.x, 5, gate.z), Vector3.down, out var hit, 8, 1 << 9) ? hit.point.y + .05f : .05f;
            marker.SetPositionAndRotation(new Vector3(gate.x, y, gate.z), Quaternion.Euler(0, gate.yaw, 0));
            marker.localScale = new Vector3(gate.width, 1, gate.length);
        }

        void FixedUpdate()
        {
            // Пауза при потере фокуса (безопасность руля/FFB); в пакетном режиме и с runInBackground (автотесты) не нужна.
            if (session == null || session.Phase != CoursePhase.Running || vehicle == null ||
                (!Application.isFocused && !Application.isBatchMode && !Application.runInBackground)) return;
            if (vehicle.CollisionCount > contacts)
            {
                contacts = vehicle.CollisionCount;
                // Удар о землю (слой 9: край эстакады, покрытие) — не касание препятствия.
                if (vehicle.LastImpactLayer != 9 && Time.time - lastContact > ContactCooldown)
                {
                    lastContact = Time.time;
                    session.Penalize(CourseSession.Contact);
                }
            }
            var t = vehicle.transform;
            var st = vehicle.Adapter.CurrentState;
            if (session.Phase == CoursePhase.Running)
                session.Tick(Time.fixedDeltaTime, new CourseInput
                {
                    x = t.position.x, z = t.position.z, yaw = t.eulerAngles.y, signedSpeed = st.signedSpeedMps, gear = st.gear,
                    leftIndicator = st.leftIndicator && !st.hazard, rightIndicator = st.rightIndicator && !st.hazard,
                    engineStalled = st.engine == EnginePhase.Stalled,
                    redLight = RedLightAhead(t),
                });
            if (session.Phase != CoursePhase.Running) SaveResult();
        }

        SignalJunction junction; bool junctionSearched;

        /// <summary>Сигнал регулируемого перекрёстка впереди запрещает пересекать стоп-линию (красный, красный с жёлтым).</summary>
        bool RedLightAhead(Transform car)
        {
            if (!junctionSearched) { junction = FindAnyObjectByType<SignalJunction>(); junctionSearched = true; }
            if (junction == null || junction.signals == null) return false;
            var forward = car.forward;
            var front = car.position + forward * (course.vehicleLength / 2);
            return junction.Approach(front, forward, out var dir, out float toLine) && toLine > -1f &&
                   SignalJunction.Prohibits(junction.AspectFor(dir));
        }

        [Serializable] class ResultFault { public string code, title, lesson; public int points; public bool terminal; public float seconds; }
        [Serializable] class Result
        {
            public string courseId, utc, mode, lessonId, phase, reason;
            public int penalty, failPenalty, lessonsCompleted; public float elapsedSeconds;
            public ResultFault[] faults;
            public string controller = "vehicle-solver";
        }

        void SaveResult()
        {
            if (saved || session == null) return;
            saved = true;
            try
            {
                var faults = new List<ResultFault>();
                foreach (var f in session.Faults)
                    faults.Add(new ResultFault { code = f.code, title = f.title, lesson = course.lessons[f.lessonIndex].id, points = f.points, terminal = f.terminal, seconds = f.elapsed });
                var result = new Result
                {
                    courseId = course.id, utc = DateTime.UtcNow.ToString("O"), mode = session.Exam ? "exam" : "lesson",
                    lessonId = course.lessons[session.LessonIndex].id, phase = session.Phase.ToString(), reason = session.Message,
                    penalty = session.Penalty, failPenalty = course.failPenalty, lessonsCompleted = session.LessonsCompleted,
                    elapsedSeconds = session.Elapsed, faults = faults.ToArray(),
                };
                var directory = Path.Combine(Application.persistentDataPath, "TrainingResults"); Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                File.WriteAllText(file, JsonUtility.ToJson(result, true));
                SaveStatus = file;
            }
            catch (Exception e) { SaveStatus = "Не удалось сохранить результат: " + e.Message; Debug.LogWarning(SaveStatus); }
        }
    }
}
