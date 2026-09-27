using System;
using System.Collections.Generic;
using System.Linq;

namespace DrivingSchool.Learning
{
    /// <summary>Тип подсказки инструктора = приоритет (docs/ui-drive.md §2): меньше — важнее.</summary>
    public enum HintKind { Safety = 0, Error = 1, Maneuver = 2, Navigation = 3, Exercise = 4 }

    /// <summary>gameplay.instructorHints: Все / Только ошибки / Выкл.</summary>
    public enum HintMode { All = 0, ErrorsOnly = 1, Off = 2 }

    public sealed class InstructorHint
    {
        public string Key, Text;
        public HintKind Kind;
        /// <summary>Сколько секунд ещё показывать; &lt;= 0 — пока условие актуально (снимается <see cref="InstructorHintQueue.Clear"/>).</summary>
        public double RemainingSeconds;
        public bool Timed;
        /// <summary>Показывается при любом режиме подсказок: шаги пошагового урока — это и есть урок (docs/lessons.md §3).</summary>
        public bool Forced;
        internal long Order;
    }

    /// <summary>
    /// Очередь подсказок (docs/ui-drive.md §2). Видна одна — самая важная; при равной важности — раньше пришедшая.
    /// Более важная вытесняет текущую сразу, вытесненная остаётся в очереди, пока актуальна. Время показа
    /// у подсказок с таймером идёт только пока они на экране. Режим фильтрует, что вообще попадает в очередь.
    /// </summary>
    public sealed class InstructorHintQueue
    {
        readonly List<InstructorHint> active = new List<InstructorHint>();
        long order;

        public HintMode Mode { get; set; } = HintMode.All;
        public InstructorHint Current { get; private set; }
        /// <summary>Сколько подсказок ждут, кроме видимой («ещё N в очереди»).</summary>
        public int Waiting => Math.Max(0, active.Count - (Current != null ? 1 : 0));

        public bool Accepts(HintKind kind)
        {
            switch (Mode)
            {
                case HintMode.Off: return false;
                // «Только ошибки»: замечания после нарушения; предупреждения об опасности тоже не прячем.
                case HintMode.ErrorsOnly: return kind == HintKind.Error || kind == HintKind.Safety;
                default: return true;
            }
        }

        /// <summary>
        /// Показать или обновить подсказку. <paramref name="seconds"/> &lt;= 0 — пока не снимут.
        /// <paramref name="force"/> — не фильтровать по режиму (шаги урока).
        /// </summary>
        public void Post(string key, HintKind kind, string text, double seconds = 0, bool force = false)
        {
            if (string.IsNullOrEmpty(key) || (!force && !Accepts(kind))) { Clear(key); return; }
            var h = active.FirstOrDefault(x => x.Key == key);
            if (h == null) { h = new InstructorHint { Key = key, Order = ++order }; active.Add(h); }
            else if (h.Timed && seconds > 0) { h.Text = text; h.Kind = kind; Pick(); return; }   // повтор той же временной подсказки не продлевает её
            h.Kind = kind; h.Text = text; h.Timed = seconds > 0; h.RemainingSeconds = seconds; h.Forced = force;
            Pick();
        }

        public bool Has(string key) => active.Any(x => x.Key == key);

        public void Clear(string key)
        {
            if (active.RemoveAll(x => x.Key == key) > 0) Pick();
        }

        public void ClearAll() { active.Clear(); Current = null; }

        /// <summary>Шаг времени: тикают только видимые подсказки с таймером.</summary>
        public void Tick(double dt)
        {
            if (Current != null && Current.Timed)
            {
                Current.RemainingSeconds -= dt;
                if (Current.RemainingSeconds <= 0) active.Remove(Current);
            }
            // Режим могли поменять в настройках: убираем то, что теперь не показывается.
            active.RemoveAll(x => !x.Forced && !Accepts(x.Kind));
            Pick();
        }

        void Pick() => Current = active.OrderBy(x => (int)x.Kind).ThenBy(x => x.Order).FirstOrDefault();
    }

    /// <summary>Событие поездки для разбора (docs/ui-drive.md §5).</summary>
    public sealed class DriveLogEvent
    {
        public double Seconds;
        public string RuleId, Title, Advice, Reference;
        public bool Severe;
        public double X, Z;
    }

    /// <summary>Журнал поездки: время, дистанция, максимальная скорость, замечания.</summary>
    public sealed class DriveLog
    {
        readonly List<DriveLogEvent> events = new List<DriveLogEvent>();
        public IReadOnlyList<DriveLogEvent> Events => events;
        public double ElapsedSeconds { get; private set; }
        public double DistanceM { get; private set; }
        public double MaxSpeedMps { get; private set; }
        public int SevereCount => events.Count(e => e.Severe);

        public void Sample(double speedMps, double dt)
        {
            if (dt <= 0 || double.IsNaN(speedMps) || double.IsNaN(dt)) return;
            ElapsedSeconds += dt;
            double v = Math.Abs(speedMps);
            DistanceM += v * dt;
            if (v > MaxSpeedMps) MaxSpeedMps = v;
        }

        public void Add(DriveLogEvent e) { if (e != null) { e.Seconds = ElapsedSeconds; events.Add(e); } }
    }
}
