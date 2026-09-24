using System;
using DrivingSchool.Contracts;
namespace DrivingSchool.Learning
{
    public sealed class LessonSession
    {
        readonly LessonDefinition definition;
        float elapsed, stopped; bool reached;
        public SessionPhase Phase { get; private set; } = SessionPhase.Briefing;
        public SessionResult Result { get; private set; }
        public LessonSession(LessonDefinition value)
        {
            if (value==null||value.timeLimitSeconds<=0||value.targetDistanceM<=0||value.requiredStopSeconds<=0||value.stopSpeedMps<0)
                throw new ArgumentException("Invalid lesson");
            definition=value;
        }
        public void Ready() { if (Phase!=SessionPhase.Briefing)throw new InvalidOperationException(); Phase=SessionPhase.Ready; }
        public void Start() { if (Phase!=SessionPhase.Ready)throw new InvalidOperationException(); Phase=SessionPhase.Running; }
        public void Tick(float dt,float forwardDisplacementM,float speedMps)
        {
            if (dt<=0||float.IsNaN(dt)||float.IsInfinity(dt)||float.IsNaN(speedMps)||float.IsInfinity(speedMps)||float.IsNaN(forwardDisplacementM)||float.IsInfinity(forwardDisplacementM))throw new ArgumentOutOfRangeException();
            if (Phase!=SessionPhase.Running)return;
            elapsed+=dt;
            reached|=forwardDisplacementM>=definition.targetDistanceM;
            stopped=reached&&Math.Abs(speedMps)<=definition.stopSpeedMps?stopped+dt:0;
            if(elapsed>=definition.timeLimitSeconds)Finish(SessionPhase.Failed,"timeout");
            else if(stopped>=definition.requiredStopSeconds)Finish(SessionPhase.Passed,"stopped-after-target");
        }
        public void Cancel() { if(Phase==SessionPhase.Passed||Phase==SessionPhase.Failed||Phase==SessionPhase.Cancelled)return; Finish(SessionPhase.Cancelled,"cancelled"); }
        void Finish(SessionPhase phase,string reason) { Phase=phase;Result=new SessionResult{lessonId=definition.id,phase=phase,reason=reason,elapsedSeconds=elapsed}; }
    }
}
