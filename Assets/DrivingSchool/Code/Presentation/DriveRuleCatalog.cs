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
                case DriveRuleMonitor.RuleRedLight: return new Entry { Title = "Проезд на запрещающий сигнал светофора", Severe = true,
                    Advice = "На красный остановитесь перед стоп-линией и ждите зелёного. Жёлтый тоже запрещает движение, если можно остановиться без экстренного торможения.",
                    Reference = "ПДД РФ пп. 6.2, 6.13, 6.14 · редакция не сверена" };
                case DriveRuleMonitor.RuleCollision: return new Entry { Title = "Столкновение", Severe = true,
                    Advice = "Держите дистанцию и боковой интервал, снижайте скорость заранее.", Reference = "ДТП" };
                // Город (T65).
                case CityRuleMonitor.RuleSpeed: return new Entry { Title = "Превышение скорости",
                    Advice = "Следите за знаками: 3.24 ограничивает скорость до ближайшего перекрёстка или знака 3.25. В населённом пункте без знаков — не больше 60 км/ч.",
                    Reference = "ПДД РФ пп. 10.1, 10.2; знак 3.24 · редакция не сверена" };
                case CityRuleMonitor.RuleOncoming: return new Entry { Title = "Выезд на полосу встречного движения", Severe = true,
                    Advice = "Не пересекайте сплошную линию разметки и не выезжайте на встречную сторону дороги с четырьмя и более полосами.",
                    Reference = "ПДД РФ пп. 9.1(1), 9.2; разметка 1.1, 1.3 · редакция не сверена" };
                case CityRuleMonitor.RuleStartNoSignal: return new Entry { Title = "Начало движения без сигнала поворота",
                    Advice = "Перед тем как тронуться от края дороги, включите левый указатель поворота и убедитесь, что никому не мешаете.",
                    Reference = "ПДД РФ пп. 8.1, 8.2 · редакция не сверена" };
                case CityRuleMonitor.RuleLaneChangeNoSignal: return new Entry { Title = "Перестроение без сигнала поворота",
                    Advice = "Включите указатель поворота в сторону перестроения заранее и выключите его, когда займёте полосу.",
                    Reference = "ПДД РФ пп. 8.1, 8.2, 8.4 · редакция не сверена" };
                case CityRuleMonitor.RuleTurnNoSignal: return new Entry { Title = "Поворот без сигнала поворота",
                    Advice = "Включайте указатель поворота заблаговременно, до начала поворота, и не выключайте до его окончания.",
                    Reference = "ПДД РФ пп. 8.1, 8.2 · редакция не сверена" };
                case CityRuleMonitor.RuleRoundaboutEntryNoSignal: return new Entry { Title = "Въезд на кольцо без сигнала поворота",
                    Advice = "Въезд на перекрёсток с круговым движением — поворот направо: включите правый указатель поворота до въезда.",
                    Reference = "ПДД РФ пп. 8.1, 8.2 · редакция не сверена · трактовка въезда как поворота — игровая, сверить с разъяснениями" };
                case CityRuleMonitor.RuleRoundaboutExitNoSignal: return new Entry { Title = "Съезд с кольца без сигнала поворота",
                    Advice = "Перед съездом с кольца включите правый указатель поворота.",
                    Reference = "ПДД РФ пп. 8.1, 8.2 · редакция не сверена" };
                case CityRuleMonitor.RuleWrongLane: return new Entry { Title = "Поворот не из той полосы",
                    Advice = "Направо поворачивайте из крайней правой полосы, налево — из крайней левой, если знаки 5.15.1, 5.15.2 или разметка не разрешают иное.",
                    Reference = "ПДД РФ п. 8.5; знаки 5.15.1, 5.15.2 · редакция не сверена" };
                case CityRuleMonitor.RuleDistance: return new Entry { Title = "Несоблюдение дистанции",
                    Advice = "Держите дистанцию, которая позволит остановиться: в городе не меньше двух секунд до машины впереди.",
                    Reference = "ПДД РФ п. 9.10 · редакция не сверена · порог игровой" };
                case CityRuleMonitor.RuleStopOnCrosswalk: return new Entry { Title = "Остановка на пешеходном переходе", Severe = true,
                    Advice = "Не останавливайтесь на переходе: в заторе ждите перед ним, пока за переходом не освободится место.",
                    Reference = "ПДД РФ пп. 12.4, 13.2 · редакция не сверена" };
                case CityRuleMonitor.RuleStopNearCrosswalk: return new Entry { Title = "Остановка ближе 5 м перед переходом",
                    Advice = "Остановка запрещена на переходе и ближе 5 м перед ним — проедьте дальше.",
                    Reference = "ПДД РФ п. 12.4 · редакция не сверена" };
                case CityRuleMonitor.RuleNoStoppingZone: return new Entry { Title = "Остановка в зоне знака «Остановка запрещена»",
                    Advice = "Знак 3.27 запрещает остановку до ближайшего перекрёстка. Найдите место для остановки дальше.",
                    Reference = "ПДД РФ, знак 3.27 · редакция не сверена" };
                case CityRuleMonitor.RuleStopNotAtRightEdge: return new Entry { Title = "Остановка не у правого края дороги",
                    Advice = "Останавливайтесь только в крайней правой полосе, у правого края проезжей части.",
                    Reference = "ПДД РФ п. 12.1 · редакция не сверена" };
                default: return new Entry { Title = ruleId, Advice = "", Reference = ruleId };
            }
        }
    }
}
