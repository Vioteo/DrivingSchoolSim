using System.Collections.Generic;
using DrivingSchool.Contracts;

namespace DrivingSchool.Rules
{
    /// <summary>Что видно в кадре для проверки правил поездки (полигон, город).</summary>
    public struct DriveRuleInput
    {
        public double Seconds;
        public float SpeedMps;
        public bool Seatbelt, LowOrHighBeam;
        /// <summary>Машина в этом кадре въехала в зону переезда (между стоп-линиями).</summary>
        public bool EnteredRailwayCrossing;
        /// <summary>Переезд закрыт для движения: сигнал, опускание или опущенный шлагбаум.</summary>
        public bool RailwayClosed;
        /// <summary>Удар в этом кадре (скорость соударения, м/с); 0 — удара нет.</summary>
        public float ImpactSpeedMps;
        public double X, Z;
    }

    /// <summary>
    /// Нарушения, которые игра умеет засечь уже сейчас: движение без ремня (ПДД 2.1.2), без ближнего света (ПДД 19.5),
    /// выезд на закрытый переезд (ПДД 15.3), столкновение. Каждое засчитывается один раз за эпизод: ремень —
    /// пока не пристегнулись, свет — пока не включили. Баллы — 0: вес задаёт таблица методики ГИБДД (T37), её ещё нет.
    /// </summary>
    public sealed class DriveRuleMonitor
    {
        public const string RuleSeatbelt = "PDD_2.1.2_SEATBELT";
        public const string RuleLowBeam = "PDD_19.5_LOW_BEAM";
        public const string RuleRailwayClosed = "PDD_15.3_RAILWAY_CLOSED";
        public const string RuleCollision = "COLLISION";

        public const float MovingMps = 1.4f;             // ~5 км/ч: машина едет
        public const double SeatbeltGraceSeconds = 2.0;  // игровые пороги, не правовые нормы (CLAUDE.md, правило 5)
        public const double LightsGraceSeconds = 5.0;
        public const float MinImpactMps = 1.5f;
        public const double CollisionCooldownSeconds = 2.0;

        double movingNoBeltSince = -1, movingNoLightsSince = -1, lastCollision = double.NegativeInfinity;
        bool beltReported, lightsReported;
        int seq;

        public List<RuleEvent> Update(DriveRuleInput i)
        {
            var result = new List<RuleEvent>();
            bool moving = System.Math.Abs(i.SpeedMps) > MovingMps;

            if (i.Seatbelt) { beltReported = false; movingNoBeltSince = -1; }
            else if (moving && !beltReported)
            {
                if (movingNoBeltSince < 0) movingNoBeltSince = i.Seconds;
                if (i.Seconds - movingNoBeltSince >= SeatbeltGraceSeconds) { beltReported = true; result.Add(Make(RuleSeatbelt, i)); }
            }
            else if (!moving) movingNoBeltSince = -1;

            if (i.LowOrHighBeam) { lightsReported = false; movingNoLightsSince = -1; }
            else if (moving && !lightsReported)
            {
                if (movingNoLightsSince < 0) movingNoLightsSince = i.Seconds;
                if (i.Seconds - movingNoLightsSince >= LightsGraceSeconds) { lightsReported = true; result.Add(Make(RuleLowBeam, i)); }
            }
            else if (!moving) movingNoLightsSince = -1;

            if (i.EnteredRailwayCrossing && i.RailwayClosed) result.Add(Make(RuleRailwayClosed, i));

            if (i.ImpactSpeedMps >= MinImpactMps && i.Seconds - lastCollision >= CollisionCooldownSeconds)
            {
                lastCollision = i.Seconds;
                result.Add(Make(RuleCollision, i));
            }
            return result;
        }

        RuleEvent Make(string ruleId, DriveRuleInput i) => new RuleEvent
        {
            id = $"drive-{++seq}", ruleId = ruleId, ruleRevision = "не сверена", participantId = "player",
            explanationKey = ruleId, simulationSeconds = i.Seconds, x = i.X, z = i.Z, penalty = 0,
        };
    }
}
