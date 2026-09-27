using System;
using DrivingSchool.Contracts;
using DrivingSchool.Learning;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Обвязка пошагового урока (T49, docs/lessons.md): грузит пакет уроков, каждый кадр собирает снимок машины,
    /// ведёт <see cref="GuidedLessonSession"/>, рисует рамку ближайшей зоны на земле. Смена КПП (F7) — урок заново.
    /// </summary>
    public sealed class GuidedLessonRunner : IDisposable
    {
        public const string PackResource = "guided-lessons";
        static GuidedLessonPack cached;

        readonly VehicleController car;
        readonly bool drawZones;
        TransmissionType transmission;
        LessonZoneMarker marker;

        public GuidedLesson Lesson { get; }
        public GuidedLessonSession Session { get; private set; }
        public bool Overspeed { get; private set; }

        /// <summary>Пакет уроков из Resources; при ошибке — null и сообщение в консоль.</summary>
        public static GuidedLessonPack LoadPack()
        {
            if (cached != null) return cached;
            var asset = Resources.Load<TextAsset>(PackResource);
            if (asset == null) { Debug.LogWarning("[Lesson] нет Resources/" + PackResource + ".json"); return null; }
            try
            {
                var pack = JsonUtility.FromJson<GuidedLessonPack>(asset.text);
                GuidedLessonPack.Validate(pack);
                return cached = pack;
            }
            catch (Exception e) { Debug.LogError("[Lesson] пакет уроков не прошёл проверку: " + e.Message); return null; }
        }

        public GuidedLessonRunner(GuidedLesson lesson, VehicleController vehicle, bool zones)
        {
            Lesson = lesson ?? throw new ArgumentNullException(nameof(lesson));
            car = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
            drawZones = zones;
            Restart();
        }

        public void Restart()
        {
            transmission = car.Adapter.transmission;
            Session = new GuidedLessonSession(Lesson, transmission == TransmissionType.Automatic);
        }

        /// <summary>Поставить машину на старт урока (если он задан).</summary>
        public void PlaceCar()
        {
            if (!Lesson.hasStart) return;
            var p = new Vector3(Lesson.startX, 0f, Lesson.startZ);
            if (UnityEngine.Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f, LessonZoneMarker.GroundMask)) p.y = hit.point.y;
            car.ResetAt(p + Vector3.up * 0.05f, Quaternion.Euler(0f, Lesson.startYaw, 0f));
        }

        public LessonSnapshot Snapshot(int gate = -1, LessonSignal signal = LessonSignal.None)
        {
            var st = car.Adapter.CurrentState; var cmd = car.Adapter.LastCommand; var t = car.transform;
            return new LessonSnapshot
            {
                speedMps = st.signedSpeedMps, clutch = cmd.clutch, x = t.position.x, z = t.position.z, yawDeg = t.eulerAngles.y,
                gear = st.gear, selector = st.selector, engine = st.engine, ignition = cmd.ignition, seatbelt = st.seatbelt,
                leftIndicator = st.leftIndicator && !st.hazard, rightIndicator = st.rightIndicator && !st.hazard,
                lowBeam = st.lowBeam, handbrake = st.handbrake, gate = gate, signal = signal,
            };
        }

        public void Tick(float dt, int gate = -1, LessonSignal signal = LessonSignal.None)
        {
            if (car.Adapter.transmission != transmission) Restart();
            var s = Snapshot(gate, signal);
            Session.Tick(dt, s);
            Overspeed = Lesson.maxSpeedKph > 0 && Session.Phase == GuidedPhase.Running && Mathf.Abs(s.speedMps) * 3.6f > Lesson.maxSpeedKph;
            if (drawZones)
            {
                var zone = Session.NextZone;
                if (zone != null && marker == null) marker = LessonZoneMarker.Create();
                if (marker != null) marker.Show(zone, Session.Current == zone);
            }
        }

        public string Text => LessonControls.Format(Session.RawText);
        public string SpeedText => LessonControls.Format(Lesson.speedHint);
        public string Progress => Session.Phase == GuidedPhase.Done
            ? "ВЫПОЛНЕНО" : $"ШАГ {Session.StepIndex + 1} / {Session.StepCount}";

        public void Dispose()
        {
            if (marker != null) UnityEngine.Object.Destroy(marker.gameObject);
            marker = null;
        }
    }
}
