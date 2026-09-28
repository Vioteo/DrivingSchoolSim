using System;
using System.Collections.Generic;

namespace DrivingSchool.Learning
{
    [Serializable] public sealed class CourseGate
    {
        public string instruction;
        public float x, z, yaw, width = 8, length = 8;
        public float holdSeconds, headingTolerance = 35;
        public int direction; // -1 reverse, +1 forward, 0 either; requires movement inside gate.
        public int minimumGear;
        public float minimumSpeed;
        public bool wholeVehicle;
        /// <summary>T68: distance from the gate centre to the stop line (or crosswalk edge) ahead along <see cref="yaw"/>, m; 0 — no line.
        /// The front bumper beyond the line before the stop is done is a fault, and the gate counts as passed.</summary>
        public float stopLine;
        /// <summary>T68: turn signal that must be on at some moment before this gate is reached: -1 left, 1 right, 0 not checked.</summary>
        public int signal;
        /// <summary>T68: allowed changes of travel direction against <see cref="direction"/> and back (corrections); 0 — not checked.</summary>
        public int maxCorrections;
    }
    [Serializable] public sealed class CourseLesson
    {
        public string id, title, briefing;
        public float startX, startZ, startYaw, timeLimit = 240;
        /// <summary>T68: rolling back more than <see cref="TrainingCourse.maxRollback"/> after the first gate ends the attempt (hill start).</summary>
        public bool noRollback;
        public CourseGate[] gates;
        public CourseGate[] transfer;
    }
    /// <summary>
    /// T68: a fault of the course and its points. The values are game settings of this autodrome, not the GIBDD
    /// method (that table is T37, ADR-013); <see cref="ruleId"/> links a DriveRuleMonitor event to the entry.
    /// </summary>
    [Serializable] public sealed class CoursePenalty
    {
        public string code, title, advice;
        public int points = 1;
        public string ruleId;
        public bool terminal;
    }
    [Serializable] public sealed class TrainingCourse
    {
        public int schemaVersion = 1;
        public string id = "training-ground-v1";
        public float halfWidth = 110, halfLength = 90, speedLimitKph = 30;
        public float stopSpeed = .12f, maxRollback = .3f, examTime = 1200;
        public int failPenalty = 5;
        public float vehicleWidth = 2.25f, vehicleLength = 4.5f;
        /// <summary>T68: how far the front bumper may pass a stop line before it is a fault, m (game tolerance).</summary>
        public float stopLineTolerance = .3f;
        public CoursePenalty[] penalties;
        public CourseLesson[] lessons;
    }
    public enum CoursePhase { Ready, Running, Passed, Failed, Cancelled }

    /// <summary>One recorded fault of an attempt (T68), for the HUD and the debrief.</summary>
    public sealed class CourseFault
    {
        public string code, title, advice, ruleId;
        public int points;
        public bool terminal, transfer;
        public int lessonIndex, gateIndex;
        public float elapsed;
    }

    /// <summary>What the course sees of the car in one step (T68).</summary>
    public struct CourseInput
    {
        public float x, z, yaw, signedSpeed;
        public int gear;
        public bool leftIndicator, rightIndicator, engineStalled;
    }

    /// <summary>Pure course evaluator; all authored thresholds are game rules, not legal standards.</summary>
    public sealed class CourseSession
    {
        public const string Contact = "contact", StopLine = "stop-line", NoSignal = "no-signal", Stall = "stall", Speed = "speed",
            Corrections = "corrections", Rollback = "rollback", Timeout = "timeout", Boundary = "boundary";

        readonly TrainingCourse course;
        readonly bool exam;
        readonly List<CourseFault> faults = new List<CourseFault>();
        float held, lessonElapsed, overspeed, previousX, previousZ, rollback;
        bool directionSeen, havePrevious, transferring;
        // T68: per-gate state
        bool signalSeen, stopLineReported, maneuverStarted, correctionsReported, wasStalled;
        int corrections, lastMotion;
        public CoursePhase Phase { get; private set; } = CoursePhase.Ready;
        public int LessonIndex { get; private set; }
        public int GateIndex { get; private set; }
        public int Penalty { get; private set; }
        public float Elapsed { get; private set; }
        public string Message { get; private set; } = "Готов к старту";
        public bool Transferring => transferring;
        public float HoldProgress => held;
        public bool Exam => exam;
        public TrainingCourse Course => course;
        public CourseLesson Lesson => course.lessons[LessonIndex];
        /// <summary>Exercises whose gates are all done (in the exam the transfer after them may still be driven).</summary>
        public int LessonsCompleted { get; private set; }
        public IReadOnlyList<CourseFault> Faults => faults;
        public CourseGate CurrentGate => Phase == CoursePhase.Running
            ? (transferring ? course.lessons[LessonIndex].transfer : course.lessons[LessonIndex].gates)[GateIndex] : null;
        public CourseSession(TrainingCourse definition, int lesson, bool fullExam)
        {
            Validate(definition);
            if (lesson < 0 || lesson >= definition.lessons.Length) throw new ArgumentOutOfRangeException(nameof(lesson));
            course = definition; exam = fullExam; LessonIndex = fullExam ? 0 : lesson;
        }
        public static void Validate(TrainingCourse c)
        {
            if(c == null || c.schemaVersion != 1 || c.lessons == null || c.lessons.Length == 0 ||
               !Positive(c.halfWidth) || !Positive(c.halfLength) || !Positive(c.speedLimitKph) ||
               !Positive(c.stopSpeed) || !Positive(c.maxRollback) || !Positive(c.examTime) || c.failPenalty < 1 ||
               !Positive(c.vehicleWidth) || !Positive(c.vehicleLength) || !Finite(c.stopLineTolerance) || c.stopLineTolerance < 0)
                throw new ArgumentException("Invalid course");
            if(c.penalties != null)
            {
                var codes = new HashSet<string>();
                foreach(var p in c.penalties)
                    if(p == null || string.IsNullOrEmpty(p.code) || !codes.Add(p.code) || string.IsNullOrEmpty(p.title) || p.points < 0)
                        throw new ArgumentException("Invalid penalty");
            }
            var ids = new HashSet<string>();
            foreach(var l in c.lessons)
            {
                if(l == null || string.IsNullOrEmpty(l.id) || !ids.Add(l.id) || !Positive(l.timeLimit) ||
                   !Finite(l.startX) || !Finite(l.startZ) || !Finite(l.startYaw) || l.gates == null || l.gates.Length == 0)
                    throw new ArgumentException("Invalid lesson");
                CheckGates(l.gates);
                if(l.transfer != null) CheckGates(l.transfer);
            }
        }
        static void CheckGates(CourseGate[] gates)
        {
            foreach(var g in gates)
                if(g == null || !Finite(g.x) || !Finite(g.z) || !Finite(g.yaw) || !Positive(g.width) ||
                   !Positive(g.length) || !Finite(g.holdSeconds) || g.holdSeconds < 0 || !Positive(g.headingTolerance) ||
                   g.headingTolerance > 180 || g.direction < -1 || g.direction > 1 ||
                   !Finite(g.minimumSpeed) || g.minimumSpeed < 0 || g.minimumGear < 0 ||
                   !Finite(g.stopLine) || g.stopLine < 0 || g.signal < -1 || g.signal > 1 || g.maxCorrections < 0)
                    throw new ArgumentException("Invalid gate");
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Positive(float f) => Finite(f) && f > 0;
        public void Start() { if(Phase != CoursePhase.Ready) throw new InvalidOperationException(); Phase=CoursePhase.Running; Message=CurrentGate.instruction; }
        public void Cancel() { if(Phase==CoursePhase.Running || Phase==CoursePhase.Ready) { Phase=CoursePhase.Cancelled; Message="Заезд отменён"; } }

        /// <summary>A fault with a free text and points (not from the table).</summary>
        public void Fault(string reason, int points = 1)
        {
            if(points < 1) throw new ArgumentOutOfRangeException(nameof(points));
            if(Phase != CoursePhase.Running)return;
            Record(null, reason, null, null, points, false);
        }

        /// <summary>A fault by its code in <see cref="TrainingCourse.penalties"/> (built-in title and points if the table has none).
        /// Returns false if the attempt is not running.</summary>
        public bool Penalize(string code)
        {
            if(Phase != CoursePhase.Running || string.IsNullOrEmpty(code)) return false;
            var p = Find(code);
            bool terminal = p != null ? p.terminal : DefaultTerminal(code);
            int points = p != null ? p.points : terminal ? course.failPenalty : DefaultPoints(code);
            Record(code, p != null ? p.title : DefaultTitle(code), p?.advice, p?.ruleId, points, terminal);
            return true;
        }

        /// <summary>A DriveRuleMonitor event (red light, seat belt…) scored by the entry with this ruleId; false — the table does not score it.</summary>
        public bool PenalizeRule(string ruleId)
        {
            if(course.penalties == null || string.IsNullOrEmpty(ruleId)) return false;
            foreach(var p in course.penalties) if(p.ruleId == ruleId) return Penalize(p.code);
            return false;
        }

        public CoursePenalty Find(string code)
        {
            if(course.penalties != null) foreach(var p in course.penalties) if(p.code == code) return p;
            return null;
        }

        static bool DefaultTerminal(string code) => code == Rollback || code == Timeout || code == Boundary;
        static int DefaultPoints(string code) => code == Contact ? 2 : code == StopLine ? 3 : 1;
        static string DefaultTitle(string code)
        {
            switch(code)
            {
                case Contact: return "Касание конуса или ограждения";
                case StopLine: return "Остановка за стоп-линией";
                case NoSignal: return "Не включён указатель поворота";
                case Stall: return "Двигатель заглох";
                case Speed: return "Превышение скорости";
                case Corrections: return "Слишком много корректировок";
                case Rollback: return "Откат на эстакаде более допуска";
                case Timeout: return "Время истекло";
                case Boundary: return "Выезд за границу площадки";
                default: return code;
            }
        }

        void Record(string code, string title, string advice, string ruleId, int points, bool terminal)
        {
            faults.Add(new CourseFault { code = code, title = title, advice = advice, ruleId = ruleId, points = points, terminal = terminal,
                transfer = transferring, lessonIndex = LessonIndex, gateIndex = GateIndex, elapsed = Elapsed });
            Penalty += points; Message = title;
            if(terminal) { Phase = CoursePhase.Failed; return; }
            if(Penalty >= course.failPenalty) { Phase=CoursePhase.Failed; Message="Не зачтено: " + title; }
        }

        /// <summary>Always ends the attempt (timeout, boundary, rollback), whatever the table says.</summary>
        void Terminate(string code)
        {
            Penalize(code);
            if(Phase == CoursePhase.Running) { Phase = CoursePhase.Failed; }
        }

        public static bool Contains(CourseGate g, float x, float z, float yaw, float width, float length)
        {
            double a=g.yaw*Math.PI/180, dx=x-g.x, dz=z-g.z;
            double lx=dx*Math.Cos(a)-dz*Math.Sin(a), lz=dx*Math.Sin(a)+dz*Math.Cos(a);
            double d=(yaw-g.yaw)*Math.PI/180;
            double ex=g.wholeVehicle ? (Math.Abs(Math.Cos(d))*width+Math.Abs(Math.Sin(d))*length)/2 : 0;
            double ez=g.wholeVehicle ? (Math.Abs(Math.Sin(d))*width+Math.Abs(Math.Cos(d))*length)/2 : 0;
            return Math.Abs(lx)+ex <= g.width/2 && Math.Abs(lz)+ez <= g.length/2;
        }

        /// <summary>Front bumper past the gate's stop line (T68): lateral and along-axis position in gate space.</summary>
        public static bool FrontPastStopLine(CourseGate g, float x, float z, float yaw, float length, float tolerance)
        {
            if(g.stopLine <= 0) return false;
            double yr=yaw*Math.PI/180, a=g.yaw*Math.PI/180;
            double dx=x+Math.Sin(yr)*length/2-g.x, dz=z+Math.Cos(yr)*length/2-g.z;
            double lx=dx*Math.Cos(a)-dz*Math.Sin(a), lz=dx*Math.Sin(a)+dz*Math.Cos(a);
            float deltaYaw=(float)Math.Abs(((yaw-g.yaw)%360+540)%360-180);
            return deltaYaw <= 60 && Math.Abs(lx) <= g.width/2 + 1 && lz > g.stopLine + tolerance && lz < g.stopLine + length + 3;
        }

        public void Tick(float dt, float x, float z, float yaw, float signedSpeed, int gear) =>
            Tick(dt, new CourseInput { x = x, z = z, yaw = yaw, signedSpeed = signedSpeed, gear = gear });

        public void Tick(float dt, CourseInput input)
        {
            float x=input.x, z=input.z, yaw=input.yaw, signedSpeed=input.signedSpeed; int gear=input.gear;
            if(!Positive(dt) || !Finite(x) || !Finite(z) || !Finite(yaw) || !Finite(signedSpeed)) throw new ArgumentOutOfRangeException();
            if(Phase != CoursePhase.Running) return;
            Elapsed += dt; lessonElapsed += dt;
            if(Elapsed >= (exam ? course.examTime : course.lessons[LessonIndex].timeLimit) ||
                (!transferring && lessonElapsed >= course.lessons[LessonIndex].timeLimit)) { Terminate(Timeout); return; }
            if(Math.Abs(x) > course.halfWidth - course.vehicleLength/2 || Math.Abs(z) > course.halfLength - course.vehicleLength/2)
            { Terminate(Boundary); return; }
            overspeed = Math.Abs(signedSpeed)*3.6f > course.speedLimitKph ? overspeed+dt : 0;
            if(overspeed >= 1) { Penalize(Speed); overspeed=0; if(Phase!=CoursePhase.Running)return; }
            if(input.engineStalled && !wasStalled) { Penalize(Stall); if(Phase!=CoursePhase.Running) { wasStalled=true; return; } }
            wasStalled = input.engineStalled;
            var gate=CurrentGate;
            if(gate.signal < 0 ? input.leftIndicator && !input.rightIndicator : gate.signal > 0 && input.rightIndicator && !input.leftIndicator) signalSeen=true;
            if(gate.maxCorrections > 0 && gate.direction != 0)
            {
                int motion = Math.Abs(signedSpeed) > .15f ? Math.Sign(signedSpeed) : 0;
                if(motion != 0)
                {
                    if(motion == gate.direction) maneuverStarted = true;
                    else if(maneuverStarted && lastMotion == gate.direction && ++corrections > gate.maxCorrections && !correctionsReported)
                    { correctionsReported = true; Penalize(Corrections); if(Phase!=CoursePhase.Running) return; }
                    lastMotion = motion;
                }
            }
            if(gate.stopLine > 0 && !stopLineReported && FrontPastStopLine(gate, x, z, yaw, course.vehicleLength, course.stopLineTolerance))
            {
                stopLineReported = true;
                Penalize(StopLine);
                if(Phase == CoursePhase.Running) CompleteGate(gate);
                return;
            }
            bool inside=Contains(gate,x,z,yaw,course.vehicleWidth,course.vehicleLength);
            float deltaYaw=(float)Math.Abs(((yaw-gate.yaw)%360+540)%360-180);
            bool aligned=deltaYaw <= gate.headingTolerance;
            if(inside && aligned && Math.Abs(signedSpeed)>.15f && Math.Sign(signedSpeed)==gate.direction) directionSeen=true;
            if(!inside || !aligned) { directionSeen=false; held=0; }
            var lesson=course.lessons[LessonIndex];
            if((lesson.noRollback || lesson.id=="hill") && !transferring && GateIndex>0 && havePrevious)
            {
                double hillYaw=lesson.startYaw*Math.PI/180;
                float dz=(float)((x-previousX)*Math.Sin(hillYaw)+(z-previousZ)*Math.Cos(hillYaw));
                rollback = dz<0 ? rollback-dz : 0;
                if(rollback > course.maxRollback) { Terminate(Rollback); return; }
            }
            previousX=x; previousZ=z; havePrevious=true;
            bool ready=inside && aligned && (gate.direction==0 || directionSeen) && (gate.minimumGear==0 || gear>=gate.minimumGear) &&
                Math.Abs(signedSpeed)>=gate.minimumSpeed;
            if(gate.holdSeconds>0)
            {
                held = ready && Math.Abs(signedSpeed)<=course.stopSpeed ? held+dt : 0;
                ready = held>=gate.holdSeconds;
            }
            if(ready) CompleteGate(gate);
        }

        void CompleteGate(CourseGate gate)
        {
            if(gate.signal != 0 && !signalSeen) { Penalize(NoSignal); if(Phase!=CoursePhase.Running) return; }
            GateIndex++; held=0; directionSeen=false; rollback=0;
            signalSeen=false; stopLineReported=false; maneuverStarted=false; correctionsReported=false; corrections=0; lastMotion=0;
            var gates=transferring ? course.lessons[LessonIndex].transfer : course.lessons[LessonIndex].gates;
            if(GateIndex<gates.Length) { Message=CurrentGate.instruction; return; }
            if(!transferring) LessonsCompleted++;
            if(!transferring && exam && course.lessons[LessonIndex].transfer?.Length>0) { transferring=true; GateIndex=0; }
            else if(exam && LessonIndex+1<course.lessons.Length) { LessonIndex++; transferring=false; lessonElapsed=0; GateIndex=0; }
            else { Phase=CoursePhase.Passed; Message=exam ? "Площадка сдана" : "Упражнение выполнено"; return; }
            Message=CurrentGate.instruction;
        }
    }
}
