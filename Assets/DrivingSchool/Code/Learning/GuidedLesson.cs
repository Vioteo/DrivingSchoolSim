using System;
using System.Collections.Generic;
using System.Text;
using DrivingSchool.Contracts;

namespace DrivingSchool.Learning
{
    /// <summary>Что проверяет шаг пошагового урока (docs/lessons.md §2). Имя в JSON — поле <c>check</c>.</summary>
    public enum StepCheck
    {
        None,            // только зона/курс (если заданы), иначе сразу выполнен
        Seatbelt, Ignition, IgnitionOff, ClutchDown, EngineRunning, LowBeam,
        Gear,            // value — номер передачи (-1 R)
        Neutral, Selector,
        IndicatorLeft, IndicatorRight, IndicatorsOff,
        HandbrakeOn, HandbrakeOff,
        MovingForward,   // value — порог скорости, м/с (0 → 0,5)
        MovingBackward,
        SpeedBelow,      // value — м/с
        Stopped,         // value — сколько секунд стоять
        Distance,        // value — метров с начала шага
        Gate,            // value — номер шага упражнения автодрома (CourseSession.GateIndex) не меньше value
        Wait,            // value — секунд показа
    }

    /// <summary>Шаг урока. Все пороги — игровые, не правовые нормы (CLAUDE.md, правило 5).</summary>
    [Serializable] public sealed class GuidedStep
    {
        public string id;
        /// <summary>Текст подсказки; {clutch}, {gas}… заменяются на клавиши (<see cref="GuidedText"/>).</summary>
        public string text;
        /// <summary>Текст, если урок вернулся к этому шагу (например, двигатель заглох).</summary>
        public string lostText;
        /// <summary>Текст, пока светофор впереди запрещает движение (<see cref="LessonSignal.Stop"/>).</summary>
        public string stopText;
        public string check;
        public float value;
        /// <summary>Для <see cref="StepCheck.Selector"/>: P, R, N или D.</summary>
        public string selector;
        /// <summary>"" — всегда, "manual" — только МКПП, "auto" — только АКПП.</summary>
        public string only;
        /// <summary>Зона: центр машины внутри прямоугольника (x, z, ширина поперёк, длина вдоль yaw). width = 0 — зоны нет.</summary>
        public float x, z, width, length, yaw;
        /// <summary>Допуск курса машины к <see cref="yaw"/>, градусы; 0 — курс не проверяется.</summary>
        public float headingTolerance;
        /// <summary>Условие шага должно держаться до шага с этим id ("end" — до конца урока); нарушено — урок возвращается сюда.</summary>
        public string keepUntil;
        /// <summary>После возврата к шагу и его исправления выполнить ещё этот шаг (например, снова включить передачу), затем вернуться туда, где были.</summary>
        public string resumeAt;
        /// <summary>Шаг пропускается, если за столько секунд не выполнен (0 — ждать сколько угодно).</summary>
        public float skipAfter;
        /// <summary>Шаг пропускается, если упражнение автодрома уже дошло до этого номера шага (0 — не используется).</summary>
        public int skipAtGate;

        public bool HasZone => width > 0 && length > 0;
    }

    [Serializable] public sealed class GuidedLesson
    {
        public string id, title, briefing, doneText;
        /// <summary>Где проходит урок — для разбора («учебная улица»).</summary>
        public string place;
        /// <summary>Сцена урока (имя из Build Settings).</summary>
        public string scene;
        /// <summary>Для подсказок к упражнению автодрома — id урока из course-v2.json.</summary>
        public string courseLesson;
        public bool hasStart;
        public float startX, startZ, startYaw;
        /// <summary>Игровой порог скорости для замечания «не разгоняйтесь» (0 — нет).</summary>
        public float maxSpeedKph;
        public string speedHint;
        public GuidedStep[] steps;
    }

    [Serializable] public sealed class GuidedLessonPack
    {
        public int schemaVersion = 1;
        public GuidedLesson[] lessons;

        public GuidedLesson Find(string id)
        {
            if (lessons != null) foreach (var l in lessons) if (l != null && l.id == id) return l;
            return null;
        }

        public GuidedLesson FindForCourse(string courseLessonId)
        {
            if (lessons != null && !string.IsNullOrEmpty(courseLessonId))
                foreach (var l in lessons) if (l != null && l.courseLesson == courseLessonId) return l;
            return null;
        }

        static readonly HashSet<StepCheck> Keepable = new HashSet<StepCheck>
        {
            StepCheck.Seatbelt, StepCheck.Ignition, StepCheck.EngineRunning, StepCheck.LowBeam, StepCheck.HandbrakeOff,
            StepCheck.HandbrakeOn, StepCheck.ClutchDown, StepCheck.Gear, StepCheck.Neutral, StepCheck.Selector,
        };

        /// <summary>Проверка пакета; бросает <see cref="ArgumentException"/> с понятной причиной.</summary>
        public static void Validate(GuidedLessonPack p)
        {
            if (p == null || p.schemaVersion != 1 || p.lessons == null || p.lessons.Length == 0) throw new ArgumentException("Пакет уроков пуст или не той версии");
            var ids = new HashSet<string>();
            foreach (var l in p.lessons)
            {
                if (l == null || string.IsNullOrEmpty(l.id) || !ids.Add(l.id)) throw new ArgumentException("Урок без id или id повторяется");
                string where = "урок " + l.id;
                if (string.IsNullOrEmpty(l.title)) throw new ArgumentException(where + ": нет названия");
                if (l.steps == null || l.steps.Length == 0) throw new ArgumentException(where + ": нет шагов");
                if (!Finite(l.startX) || !Finite(l.startZ) || !Finite(l.startYaw) || !Finite(l.maxSpeedKph) || l.maxSpeedKph < 0)
                    throw new ArgumentException(where + ": неверный старт или порог скорости");
                GuidedText.CheckPlaceholders(l.briefing, where); GuidedText.CheckPlaceholders(l.doneText, where); GuidedText.CheckPlaceholders(l.speedHint, where);
                var stepIds = new HashSet<string>();
                foreach (var s in l.steps)
                    if (s == null || string.IsNullOrEmpty(s.id) || !stepIds.Add(s.id)) throw new ArgumentException(where + ": шаг без id или id повторяется");
                foreach (var s in l.steps)
                {
                    string at = where + ", шаг " + s.id;
                    if (string.IsNullOrEmpty(s.text)) throw new ArgumentException(at + ": нет текста");
                    GuidedText.CheckPlaceholders(s.text, at); GuidedText.CheckPlaceholders(s.lostText, at); GuidedText.CheckPlaceholders(s.stopText, at);
                    if (!TryParseCheck(s.check, out var c)) throw new ArgumentException(at + ": неизвестная проверка «" + s.check + "»");
                    if (c == StepCheck.Selector && !TryParseSelector(s.selector, out _)) throw new ArgumentException(at + ": селектор должен быть P, R, N или D");
                    if (!string.IsNullOrEmpty(s.only) && s.only != "manual" && s.only != "auto") throw new ArgumentException(at + ": only — manual, auto или пусто");
                    if (!Finite(s.value) || !Finite(s.x) || !Finite(s.z) || !Finite(s.yaw) || s.width < 0 || s.length < 0 || !Finite(s.width) || !Finite(s.length) ||
                        s.headingTolerance < 0 || s.headingTolerance > 180 || s.skipAfter < 0 || !Finite(s.skipAfter) || s.skipAtGate < 0)
                        throw new ArgumentException(at + ": неверные числа");
                    if ((s.width > 0) != (s.length > 0)) throw new ArgumentException(at + ": у зоны нужны и ширина, и длина");
                    if (!string.IsNullOrEmpty(s.keepUntil))
                    {
                        if (!Keepable.Contains(c)) throw new ArgumentException(at + ": keepUntil только для проверок состояния");
                        if (s.keepUntil != "end" && !stepIds.Contains(s.keepUntil)) throw new ArgumentException(at + ": keepUntil ссылается на неизвестный шаг");
                    }
                    if (!string.IsNullOrEmpty(s.resumeAt) && !stepIds.Contains(s.resumeAt)) throw new ArgumentException(at + ": resumeAt ссылается на неизвестный шаг");
                }
            }
        }

        internal static bool TryParseCheck(string s, out StepCheck c)
        {
            c = StepCheck.None;
            if (string.IsNullOrEmpty(s)) return true;
            return Enum.TryParse(s, false, out c) && Enum.IsDefined(typeof(StepCheck), c);
        }

        internal static bool TryParseSelector(string s, out AutomaticSelector sel)
        {
            sel = AutomaticSelector.P;
            return !string.IsNullOrEmpty(s) && s.Length == 1 && Enum.TryParse(s, false, out sel);
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    /// <summary>Светофор впереди по ходу: нет / разрешает / запрещает.</summary>
    public enum LessonSignal { None, Go, Stop }

    /// <summary>Снимок машины для урока. Собирает Presentation из VehicleState и DriverCommand.</summary>
    public struct LessonSnapshot
    {
        public float speedMps;          // со знаком: + вперёд
        public float clutch;            // 1 = выжато
        public float x, z, yawDeg;
        public int gear;
        public AutomaticSelector selector;
        public EnginePhase engine;
        public bool ignition, seatbelt, leftIndicator, rightIndicator, lowBeam, handbrake;
        /// <summary>CourseSession.GateIndex для упражнений автодрома; -1 — нет упражнения.</summary>
        public int gate;
        public LessonSignal signal;
    }

    public enum GuidedPhase { Running, Done }

    /// <summary>
    /// Пошаговый урок (как первые уроки City Car Driving): одна задача за раз, шаг засчитывается по состоянию машины.
    /// Шаги с keepUntil должны держаться дальше (ремень, работающий двигатель): нарушено — урок возвращается
    /// к шагу, показывает lostText, после исправления просит ещё шаг resumeAt (если он раньше места возврата) и
    /// продолжает с того места, где был.
    /// Чистый C#: ни Unity, ни времени кадра — только Tick(dt, снимок).
    /// </summary>
    public sealed class GuidedLessonSession
    {
        public const float StopSpeedMps = 0.15f;
        readonly GuidedStep[] steps;
        readonly StepCheck[] checks;
        readonly int[] keepEnd, resumeIndex;
        int resumeTo = -1, detour = -1;
        bool inDetour;
        double stepTime, stepDistance, held;
        LessonSignal signal;

        public GuidedLesson Lesson { get; }
        public GuidedPhase Phase { get; private set; } = GuidedPhase.Running;
        public int StepIndex { get; private set; }
        public int StepCount => steps.Length;
        public GuidedStep Current => Phase == GuidedPhase.Running ? steps[StepIndex] : null;
        /// <summary>Урок вернулся к этому шагу (условие нарушено) — показываем lostText.</summary>
        public bool Rewound { get; private set; }
        /// <summary>Сколько раз урок возвращался назад (заглох, отстегнул ремень…) — для разбора.</summary>
        public int Rewinds { get; private set; }
        public string RawText
        {
            get
            {
                var s = Current;
                if (s == null) return Lesson.doneText;
                if (Rewound && !string.IsNullOrEmpty(s.lostText)) return s.lostText;
                return signal == LessonSignal.Stop && !string.IsNullOrEmpty(s.stopText) ? s.stopText : s.text;
            }
        }

        /// <summary>Зона ближайшего шага, у которого она есть (текущий или следующие) — для рамки на земле.</summary>
        public GuidedStep NextZone
        {
            get
            {
                if (Phase != GuidedPhase.Running) return null;
                for (int i = StepIndex; i < steps.Length; i++) if (steps[i].HasZone) return steps[i];
                return null;
            }
        }

        public GuidedLessonSession(GuidedLesson lesson, bool automatic)
        {
            if (lesson == null || lesson.steps == null) throw new ArgumentNullException(nameof(lesson));
            Lesson = lesson;
            var list = new List<GuidedStep>();
            foreach (var s in lesson.steps)
                if (string.IsNullOrEmpty(s.only) || s.only == (automatic ? "auto" : "manual")) list.Add(s);
            if (list.Count == 0) throw new ArgumentException("Нет шагов для этой коробки передач");
            steps = list.ToArray();
            checks = new StepCheck[steps.Length]; keepEnd = new int[steps.Length]; resumeIndex = new int[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                if (!GuidedLessonPack.TryParseCheck(steps[i].check, out checks[i])) throw new ArgumentException("Неизвестная проверка " + steps[i].check);
                keepEnd[i] = string.IsNullOrEmpty(steps[i].keepUntil) ? -1 : steps[i].keepUntil == "end" ? steps.Length : IndexOf(steps[i].keepUntil, steps.Length);
                resumeIndex[i] = string.IsNullOrEmpty(steps[i].resumeAt) ? -1 : IndexOf(steps[i].resumeAt, -1);
            }
        }

        // Шаг другой коробки передач отфильтрован — берём следующий существующий после него в исходном порядке.
        int IndexOf(string id, int fallback)
        {
            int orig = Array.FindIndex(Lesson.steps, s => s.id == id);
            if (orig < 0) return fallback;
            for (int k = orig; k < Lesson.steps.Length; k++)
            {
                int i = Array.IndexOf(steps, Lesson.steps[k]);
                if (i >= 0) return i;
            }
            return steps.Length;
        }

        public void Tick(double dt, LessonSnapshot s)
        {
            if (Phase != GuidedPhase.Running || !(dt > 0) || double.IsInfinity(dt)) return;
            signal = s.signal;
            stepTime += dt;
            stepDistance += Math.Abs(s.speedMps) * dt;
            held = Math.Abs(s.speedMps) <= StopSpeedMps ? held + dt : 0;

            // Условия, которые должны держаться (ремень, двигатель): нарушено — вернуться к шагу.
            for (int i = 0; i < StepIndex; i++)
                if (keepEnd[i] > StepIndex && !Holds(i, s)) { Rewind(i); return; }

            var step = steps[StepIndex];
            bool skip = (step.skipAfter > 0 && stepTime >= step.skipAfter) || (step.skipAtGate > 0 && s.gate >= step.skipAtGate);
            if (skip || Done(StepIndex, s)) Advance();
        }

        void Rewind(int i)
        {
            if (resumeTo < 0) resumeTo = StepIndex;
            detour = resumeIndex[i] > i && resumeIndex[i] < resumeTo ? resumeIndex[i] : -1;
            StepIndex = i; Rewound = true; inDetour = false; Rewinds++; ResetStep();
        }

        void Advance()
        {
            if (Rewound && detour >= 0) { StepIndex = detour; detour = -1; inDetour = true; }
            else if (Rewound || inDetour) { StepIndex = Math.Max(resumeTo, StepIndex + 1); resumeTo = -1; inDetour = false; }
            else StepIndex++;
            Rewound = false; ResetStep();
            if (StepIndex >= steps.Length) { StepIndex = steps.Length - 1; Phase = GuidedPhase.Done; }
        }

        void ResetStep() { stepTime = 0; stepDistance = 0; held = 0; }

        bool Done(int i, LessonSnapshot s)
        {
            var st = steps[i];
            if (st.HasZone && !InZone(st, s)) return false;
            if (st.headingTolerance > 0 && HeadingError(s.yawDeg, st.yaw) > st.headingTolerance) return false;
            switch (checks[i])
            {
                case StepCheck.MovingForward: return s.speedMps >= (st.value > 0 ? st.value : 0.5f);
                case StepCheck.MovingBackward: return -s.speedMps >= (st.value > 0 ? st.value : 0.5f);
                case StepCheck.SpeedBelow: return Math.Abs(s.speedMps) <= st.value;
                case StepCheck.Stopped: return held >= Math.Max(st.value, 0.01f);
                case StepCheck.Distance: return stepDistance >= st.value;
                case StepCheck.Wait: return stepTime >= st.value;
                case StepCheck.IndicatorsOff: return !s.leftIndicator && !s.rightIndicator;
                case StepCheck.IgnitionOff: return !s.ignition && s.engine != EnginePhase.Running;
                case StepCheck.Gate: return s.gate >= st.value;
                default: return Holds(i, s);
            }
        }

        /// <summary>Мгновенные условия состояния (их можно «держать» через keepUntil).</summary>
        bool Holds(int i, LessonSnapshot s)
        {
            var st = steps[i];
            switch (checks[i])
            {
                case StepCheck.None: return true;
                case StepCheck.Seatbelt: return s.seatbelt;
                case StepCheck.Ignition: return s.ignition || s.engine == EnginePhase.Running;
                case StepCheck.ClutchDown: return s.clutch >= 0.9f;
                case StepCheck.EngineRunning: return s.engine == EnginePhase.Running;
                case StepCheck.LowBeam: return s.lowBeam;
                case StepCheck.Gear: return s.gear == (int)Math.Round(st.value);
                case StepCheck.Neutral: return s.gear == 0;
                case StepCheck.Selector: return GuidedLessonPack.TryParseSelector(st.selector, out var sel) && s.selector == sel;
                case StepCheck.IndicatorLeft: return s.leftIndicator && !s.rightIndicator;
                case StepCheck.IndicatorRight: return s.rightIndicator && !s.leftIndicator;
                case StepCheck.HandbrakeOn: return s.handbrake;
                case StepCheck.HandbrakeOff: return !s.handbrake;
                default: return Done(i, s);
            }
        }

        public static bool InZone(GuidedStep g, LessonSnapshot s)
        {
            double a = g.yaw * Math.PI / 180, dx = s.x - g.x, dz = s.z - g.z;
            double lx = dx * Math.Cos(a) - dz * Math.Sin(a), lz = dx * Math.Sin(a) + dz * Math.Cos(a);
            return Math.Abs(lx) <= g.width / 2 && Math.Abs(lz) <= g.length / 2;
        }

        public static float HeadingError(float yaw, float target) => Math.Abs(((yaw - target) % 360 + 540) % 360 - 180);
    }

    /// <summary>
    /// Подстановка клавиш в текст урока: {clutch} → «Shift» и т. п. Имена клавиш даёт Presentation (клавиатура/руль),
    /// здесь — только список допустимых ключей и сама подстановка.
    /// </summary>
    public static class GuidedText
    {
        public static readonly string[] Keys =
        {
            "belt", "ignition", "starter", "clutch", "gas", "brake", "handbrake", "left", "right", "lights",
            "gear1", "gear2", "reverse", "neutral", "drive", "park", "steer", "steer_left", "steer_right", "horn", "hazard",
        };

        public static bool IsKnown(string key) => Array.IndexOf(Keys, key) >= 0;

        public static void CheckPlaceholders(string text, string where)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', i + 1))
            {
                int j = text.IndexOf('}', i);
                if (j < 0) throw new ArgumentException(where + ": незакрытая { в тексте");
                string key = text.Substring(i + 1, j - i - 1);
                if (!IsKnown(key)) throw new ArgumentException(where + ": неизвестная клавиша {" + key + "}");
            }
        }

        public static string Format(string text, Func<string, string> keyName)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            var sb = new StringBuilder(text.Length + 16);
            int pos = 0;
            for (int i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', pos))
            {
                int j = text.IndexOf('}', i);
                if (j < 0) break;
                sb.Append(text, pos, i - pos);
                string key = text.Substring(i + 1, j - i - 1);
                string name = keyName?.Invoke(key);
                sb.Append(string.IsNullOrEmpty(name) ? key : name);
                pos = j + 1;
            }
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }
    }
}
