using System.Collections.Generic;
using System.Linq;

namespace DrivingSchool.Simulation.RoadGraph
{
    public enum SignKind { Warning, Priority, Prohibition, Mandatory, Information, Service, Settlement }

    public sealed class SignCatalogEntry
    {
        public string Code, CatalogId, Title;
        public SignKind Kind;
        public bool NeedsValue;
    }

    /// <summary>
    /// Sign code (ГОСТ Р 52290-2004 numbering) → Traffic Kit v1 prefab (T28).
    /// Status: codes assigned by the developer from the standard's numbering and NOT yet checked against the text
    /// of the standard (<see cref="Verified"/> = false). Plaques (8.x) are referenced by prefab id only until verified.
    /// </summary>
    public static class SignCatalog
    {
        public const string Source = "ГОСТ Р 52290-2004 «Знаки дорожные. Общие технические требования», нумерация знаков";
        public const bool Verified = false;

        public static readonly IReadOnlyList<SignCatalogEntry> Entries = new[]
        {
            E("1.1", "DS_Sign_RailwayBarrier", "Железнодорожный переезд со шлагбаумом", SignKind.Warning),
            E("1.2", "DS_Sign_RailwayNoBarrier", "Железнодорожный переезд без шлагбаума", SignKind.Warning),
            E("1.3.1", "DS_Sign_SingleTrack", "Однопутная железная дорога", SignKind.Warning),
            E("1.3.2", "DS_Sign_MultiTrack", "Многопутная железная дорога", SignKind.Warning),
            E("1.4.1", "DS_Sign_RailwayDist3_R", "Приближение к железнодорожному переезду (3 полосы, справа)", SignKind.Warning),
            E("1.4.2", "DS_Sign_RailwayDist2_R", "Приближение к железнодорожному переезду (2 полосы, справа)", SignKind.Warning),
            E("1.4.3", "DS_Sign_RailwayDist1_R", "Приближение к железнодорожному переезду (1 полоса, справа)", SignKind.Warning),
            E("1.4.4", "DS_Sign_RailwayDist3_L", "Приближение к железнодорожному переезду (3 полосы, слева)", SignKind.Warning),
            E("1.4.5", "DS_Sign_RailwayDist2_L", "Приближение к железнодорожному переезду (2 полосы, слева)", SignKind.Warning),
            E("1.4.6", "DS_Sign_RailwayDist1_L", "Приближение к железнодорожному переезду (1 полоса, слева)", SignKind.Warning),
            E("2.1", "DS_Sign_Priority", "Главная дорога", SignKind.Priority),
            E("2.4", "DS_Sign_Yield", "Уступите дорогу", SignKind.Priority),
            E("2.5", "DS_Sign_Stop", "Движение без остановки запрещено", SignKind.Priority),
            E("3.1", "DS_Sign_NoEntry", "Въезд запрещён", SignKind.Prohibition),
            E("3.24", "DS_Sign_Speed60", "Ограничение максимальной скорости", SignKind.Prohibition, needsValue: true),
            E("3.27", "DS_Sign_NoStopping", "Остановка запрещена", SignKind.Prohibition),
            E("3.28", "DS_Sign_NoParking", "Стоянка запрещена", SignKind.Prohibition),
            E("4.1.1", "DS_Sign_Straight", "Движение прямо", SignKind.Mandatory),
            E("4.1.2", "DS_Sign_Right", "Движение направо", SignKind.Mandatory),
            E("4.3", "DS_Sign_Roundabout", "Круговое движение", SignKind.Mandatory),
            E("5.19.1", "DS_Sign_Crossing", "Пешеходный переход", SignKind.Information),
            E("5.23.1", "DS_Sign_TownEntry", "Начало населённого пункта", SignKind.Settlement),
            E("5.24.1", "DS_Sign_TownExit", "Конец населённого пункта", SignKind.Settlement),
            E("6.4", "DS_Sign_Parking", "Парковка (парковочное место)", SignKind.Service),
        };

        static readonly Dictionary<string, SignCatalogEntry> ByCode = Entries.ToDictionary(e => e.Code);

        public static ICollection<string> Codes => ByCode.Keys;

        public static bool TryGet(string code, out SignCatalogEntry entry) => ByCode.TryGetValue(code, out entry);

        /// <summary>Prefab for a sign; 3.24 picks the speed variant that exists in the kit.</summary>
        public static string PrefabFor(string code, string value)
        {
            if (!ByCode.TryGetValue(code, out var e)) return null;
            if (code == "3.24" && (value == "30" || value == "40" || value == "60")) return "DS_Sign_Speed" + value;
            return e.NeedsValue && code == "3.24" ? null : e.CatalogId;
        }

        static SignCatalogEntry E(string code, string prefab, string title, SignKind kind, bool needsValue = false) =>
            new SignCatalogEntry { Code = code, CatalogId = prefab, Title = title, Kind = kind, NeedsValue = needsValue };
    }
}
