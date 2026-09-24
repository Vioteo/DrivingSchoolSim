using System;

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
    }
    [Serializable] public sealed class CourseLesson
    {
        public string id, title, briefing;
        public float startX, startZ, startYaw, timeLimit = 240;
        public CourseGate[] gates;
        public CourseGate[] transfer;
    }
    [Serializable] public sealed class TrainingCourse
    {
        public int schemaVersion = 1;
        public string id = "training-ground-v1";
        public float halfWidth = 110, halfLength = 90, speedLimitKph = 30;
        public float stopSpeed = .12f, maxRollback = .3f, examTime = 1200;
        public int failPenalty = 5;
        public float vehicleWidth = 2.25f, vehicleLength = 4.5f;
        public CourseLesson[] lessons;
    }
    public enum CoursePhase { Ready, Running, Passed, Failed, Cancelled }

    /// <summary>Pure course evaluator; all authored thresholds are game rules, not legal standards.</summary>
    public sealed class CourseSession
    {
        readonly TrainingCourse course;
        readonly bool exam;
        float held, lessonElapsed, overspeed, previousX, previousZ, rollback;
        bool directionSeen, havePrevious, transferring;
        public CoursePhase Phase { get; private set; } = CoursePhase.Ready;
        public int LessonIndex { get; private set; }
        public int GateIndex { get; private set; }
        public int Penalty { get; private set; }
        public float Elapsed { get; private set; }
        public string Message { get; private set; } = "Готов к старту";
        public bool Transferring => transferring;
        public float HoldProgress => held;
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
               !Positive(c.vehicleWidth) || !Positive(c.vehicleLength)) throw new ArgumentException("Invalid course");
            var ids = new System.Collections.Generic.HashSet<string>();
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
                   !Finite(g.minimumSpeed) || g.minimumSpeed < 0 || g.minimumGear < 0)
                    throw new ArgumentException("Invalid gate");
        }
        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Positive(float f) => Finite(f) && f > 0;
        public void Start() { if(Phase != CoursePhase.Ready) throw new InvalidOperationException(); Phase=CoursePhase.Running; Message=CurrentGate.instruction; }
        public void Cancel() { if(Phase==CoursePhase.Running || Phase==CoursePhase.Ready) { Phase=CoursePhase.Cancelled; Message="Заезд отменён"; } }
        public void Fault(string reason, int points = 1)
        {
            if(points < 1) throw new ArgumentOutOfRangeException(nameof(points));
            if(Phase != CoursePhase.Running)return;
            Penalty += points; Message = reason;
            if(Penalty >= course.failPenalty) { Phase=CoursePhase.Failed; Message="Не зачтено: " + reason; }
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
        public void Tick(float dt, float x, float z, float yaw, float signedSpeed, int gear)
        {
            if(!Positive(dt) || !Finite(x) || !Finite(z) || !Finite(yaw) || !Finite(signedSpeed)) throw new ArgumentOutOfRangeException();
            if(Phase != CoursePhase.Running) return;
            Elapsed += dt; lessonElapsed += dt;
            if(Elapsed >= (exam ? course.examTime : course.lessons[LessonIndex].timeLimit) ||
                (!transferring && lessonElapsed >= course.lessons[LessonIndex].timeLimit)) { Fail("Время истекло"); return; }
            if(Math.Abs(x) > course.halfWidth - course.vehicleLength/2 || Math.Abs(z) > course.halfLength - course.vehicleLength/2)
            { Fail("Выезд за границу площадки"); return; }
            overspeed = Math.Abs(signedSpeed)*3.6f > course.speedLimitKph ? overspeed+dt : 0;
            if(overspeed >= 1) { Fault("Превышение скорости",1); overspeed=0; if(Phase!=CoursePhase.Running)return; }
            var gate=CurrentGate;
            bool inside=Contains(gate,x,z,yaw,course.vehicleWidth,course.vehicleLength);
            float deltaYaw=(float)Math.Abs(((yaw-gate.yaw)%360+540)%360-180);
            bool aligned=deltaYaw <= gate.headingTolerance;
            if(inside && aligned && Math.Abs(signedSpeed)>.15f && Math.Sign(signedSpeed)==gate.direction) directionSeen=true;
            if(!inside || !aligned) { directionSeen=false; held=0; }
            if(course.lessons[LessonIndex].id=="hill" && !transferring && GateIndex>0 && havePrevious)
            {
                double hillYaw=course.lessons[LessonIndex].startYaw*Math.PI/180;
                float dz=(float)((x-previousX)*Math.Sin(hillYaw)+(z-previousZ)*Math.Cos(hillYaw));
                rollback = dz<0 ? rollback-dz : 0;
                if(rollback > course.maxRollback) { Fail("Откат на эстакаде более допуска"); return; }
            }
            previousX=x; previousZ=z; havePrevious=true;
            bool ready=inside && aligned && (gate.direction==0 || directionSeen) && (gate.minimumGear==0 || gear>=gate.minimumGear) &&
                Math.Abs(signedSpeed)>=gate.minimumSpeed;
            if(gate.holdSeconds>0)
            {
                held = ready && Math.Abs(signedSpeed)<=course.stopSpeed ? held+dt : 0;
                ready = held>=gate.holdSeconds;
            }
            if(!ready)return;
            GateIndex++; held=0; directionSeen=false; rollback=0;
            var gates=transferring ? course.lessons[LessonIndex].transfer : course.lessons[LessonIndex].gates;
            if(GateIndex>=gates.Length)
            {
                GateIndex=0;
                if(!transferring && exam && course.lessons[LessonIndex].transfer?.Length>0) transferring=true;
                else if(exam && LessonIndex+1<course.lessons.Length)
                { LessonIndex++; transferring=false; lessonElapsed=0; }
                else { Phase=CoursePhase.Passed; Message=exam ? "Площадка сдана" : "Урок выполнен"; return; }
            }
            Message=CurrentGate.instruction;
        }
        void Fail(string reason) { Phase=CoursePhase.Failed; Message=reason; }
    }
}

