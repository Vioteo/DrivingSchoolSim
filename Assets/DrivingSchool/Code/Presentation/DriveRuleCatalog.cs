using DrivingSchool.Rules;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Тексты нарушений для HUD и разбора: название, «как правильно», пункт ПДД. Пункты — ПДД РФ
    /// (постановление Правительства РФ № 1090); редакция не сверена (CLAUDE.md, правило 5). Баллов нет до T37.
    /// </summary>
    public static class DriveRuleCatalog
    {
        public sealed class Entry { public string Title, Advice, Reference; public bool Severe; }

        public static Entry Get(string ruleId)
        {
            switch (ruleId)
            {
                case DriveRuleMonitor.RuleSeatbelt: return new Entry { Title = "Движение без ремня безопасности",
                    Advice = "Пристегнитесь до начала движения и не отстёгивайтесь, пока машина едет.", Reference = "ПДД РФ п. 2.1.2 · редакция не сверена" };
                case DriveRuleMonitor.RuleLowBeam: return new Entry { Title = "Движение без ближнего света",
                    Advice = "В движении днём и ночью включайте ближний свет или дневные ходовые огни.", Reference = "ПДД РФ п. 19.5 · редакция не сверена" };
                case DriveRuleMonitor.RuleRailwayClosed: return new Entry { Title = "Выезд на закрытый переезд", Severe = true,
                    Advice = "Если шлагбаум опускается или горит красный сигнал, остановитесь у стоп-линии и дождитесь открытия.", Reference = "ПДД РФ п. 15.3 · редакция не сверена" };
                case DriveRuleMonitor.RuleCollision: return new Entry { Title = "Столкновение", Severe = true,
                    Advice = "Держите дистанцию и боковой интервал, снижайте скорость заранее.", Reference = "ДТП" };
                default: return new Entry { Title = ruleId, Advice = "", Reference = ruleId };
            }
        }
    }
}
