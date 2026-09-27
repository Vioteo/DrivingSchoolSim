using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Регулируемый перекрёсток для проверок поездки (T49): центр — позиция объекта, стоп-линии — на расстоянии
    /// <see cref="stopLineOffset"/> от центра по каждому въезду, светофоры — <see cref="CrossroadSignalCycle"/>.
    /// Даёт сигнал для подъезжающей машины и ловит пересечение стоп-линии.
    /// </summary>
    public sealed class SignalJunction : MonoBehaviour
    {
        public CrossroadSignalCycle signals;
        [Tooltip("Расстояние от центра до стоп-линии, м")] public float stopLineOffset = 10f;
        [Tooltip("Половина ширины въезда (дорога), м")] public float armHalfWidth = 4f;
        [Tooltip("С какого расстояния до стоп-линии сигнал считается «для вас», м")] public float approachDistance = 60f;

        public enum Signal { None, Go, Stop }

        /// <summary>Въезд, по которому едет машина: единичный вектор к центру (по ближайшей стороне света) и расстояние от переднего бампера до стоп-линии.</summary>
        public bool Approach(Vector3 front, Vector3 forward, out Vector3 dir, out float toStopLine)
        {
            dir = Cardinal(forward);
            var rel = transform.position - front; rel.y = 0;
            float along = Vector3.Dot(rel, dir);
            float lateral = Vector3.Dot(rel, new Vector3(dir.z, 0, -dir.x));
            toStopLine = along - stopLineOffset;
            return Mathf.Abs(lateral) <= armHalfWidth + 0.5f && along > -stopLineOffset;
        }

        /// <summary>Сигнал для машины, подъезжающей к перекрёстку; None — не подъезжает (уже на перекрёстке или далеко).</summary>
        public Signal SignalFor(Vector3 front, Vector3 forward)
        {
            if (signals == null || !Approach(front, forward, out var dir, out float d) || d < -0.5f || d > approachDistance) return Signal.None;
            var a = AspectFor(dir);
            return a == TrafficSignalView.Aspect.Green || a == TrafficSignalView.Aspect.GreenFlashing ? Signal.Go : Signal.Stop;
        }

        public TrafficSignalView.Aspect AspectFor(Vector3 dir)
        {
            var heads = Mathf.Abs(dir.z) > 0.5f ? signals.northSouth : signals.eastWest;
            foreach (var h in heads) if (h != null) return h.CurrentAspect;
            return TrafficSignalView.Aspect.Off;
        }

        /// <summary>Красный или красный с жёлтым: пересекать стоп-линию нельзя (жёлтый — по п. 6.14 можно, если не успеть остановиться).</summary>
        public static bool Prohibits(TrafficSignalView.Aspect a) => a == TrafficSignalView.Aspect.Red || a == TrafficSignalView.Aspect.RedAmber;

        static Vector3 Cardinal(Vector3 f)
        {
            f.y = 0;
            return Mathf.Abs(f.x) > Mathf.Abs(f.z) ? new Vector3(Mathf.Sign(f.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(f.z));
        }

        /// <summary>
        /// Следит за одной машиной: в каком кадре передний бампер пересёк стоп-линию и какой был сигнал.
        /// </summary>
        public sealed class StopLineWatch
        {
            float last = float.NaN;
            public bool Update(SignalJunction j, Vector3 front, Vector3 forward, out TrafficSignalView.Aspect aspect)
            {
                aspect = TrafficSignalView.Aspect.Off;
                if (j == null || !j.Approach(front, forward, out var dir, out float d)) { last = float.NaN; return false; }
                bool crossed = !float.IsNaN(last) && last > 0f && d <= 0f;
                last = d;
                if (crossed && j.signals != null) aspect = j.AspectFor(dir);
                return crossed;
            }
        }
    }
}
