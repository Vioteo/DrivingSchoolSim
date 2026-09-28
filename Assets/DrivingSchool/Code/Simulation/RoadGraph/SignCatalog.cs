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
            E("1.17", "DS_Sign_Bump", "Искусственная неровность", SignKind.Warning),
            E("2.1", "DS_Sign_Priority", "Главная дорога", SignKind.Priority),
            E("2.4", "DS_Sign_Yield", "Уступите дорогу", SignKind.Priority),
            E("2.5", "DS_Sign_Stop", "Движение без остановки запрещено", SignKind.Priority),
            E("3.1", "DS_Sign_NoEntry", "Въезд запрещён", SignKind.Prohibition),
            E("3.24", "DS_Sign_Speed60", "Ограничение максимальной скорости", SignKind.Prohibition, needsValue: true),
            E("3.25", "DS_Sign_SpeedEnd20", "Конец зоны ограничения максимальной скорости", SignKind.Prohibition, needsValue: true),
            E("3.27", "DS_Sign_NoStopping", "Остановка запрещена", SignKind.Prohibition),
            E("3.28", "DS_Sign_NoParking", "Стоянка запрещена", SignKind.Prohibition),
            E("4.1.1", "DS_Sign_Straight", "Движение прямо", SignKind.Mandatory),
            E("4.1.2", "DS_Sign_Right", "Движение направо", SignKind.Mandatory),
            E("4.3", "DS_Sign_Roundabout", "Круговое движение", SignKind.Mandatory),
            E("5.15.1", "DS_Sign_LaneDir_L_SR", "Направления движения по полосам", SignKind.Information, needsValue: true),
            E("5.15.2", "DS_Sign_LaneDir1_LS", "Направления движения по полосе", SignKind.Information, needsValue: true),
            E("5.19.1", "DS_Sign_Crossing", "Пешеходный переход", SignKind.Information),
            E("5.20", "DS_Sign_BumpInfo", "Искусственная неровность", SignKind.Information),
            E("5.23.1", "DS_Sign_TownEntry", "Начало населённого пункта", SignKind.Settlement),
            E("5.24.1", "DS_Sign_TownExit", "Конец населённого пункта", SignKind.Settlement),
            E("6.4", "DS_Sign_Parking", "Парковка (парковочное место)", SignKind.Service),
        };

        static readonly Dictionary<string, SignCatalogEntry> ByCode = Entries.ToDictionary(e => e.Code);

        public static ICollection<string> Codes => ByCode.Keys;

        public static bool TryGet(string code, out SignCatalogEntry entry) => ByCode.TryGetValue(code, out entry);

        /// <summary>Speed values drawn in the kit for 3.24 (tools/build_traffic_assets.py).</summary>
        public static readonly string[] SpeedValues = { "20", "30", "40", "60" };
        /// <summary>Values drawn for 3.25 «Конец зоны ограничения максимальной скорости».</summary>
        public static readonly string[] SpeedEndValues = { "20" };
        /// <summary>Lane direction faces drawn in the kit (5.15.1: lanes left to right separated by '|'; 5.15.2: one lane).</summary>
        public static readonly string[] LaneDirectionValues = { "L|SR", "L|LSR" }, LaneDirectionSingleValues = { "LS", "SR" };

        /// <summary>
        /// Signs whose zone runs from the sign to the next intersection (ПДД РФ, Приложение 1, раздел 3 — редакцию сверить):
        /// they act on every lane of their direction.
        /// </summary>
        public static bool IsZoneSign(string code) => code == "3.24" || code == "3.25" || code == "3.27" || code == "3.28";

        /// <summary>Prefab for a sign; signs with a value pick the variant that exists in the kit (null if none).</summary>
        public static string PrefabFor(string code, string value)
        {
            if (!ByCode.TryGetValue(code, out var e)) return null;
            if (code == "3.24") return System.Array.IndexOf(SpeedValues, value) >= 0 ? "DS_Sign_Speed" + value : null;
            if (code == "3.25") return System.Array.IndexOf(SpeedEndValues, value) >= 0 ? "DS_Sign_SpeedEnd" + value : null;
            if (code == "5.15.1") return System.Array.IndexOf(LaneDirectionValues, value) >= 0 ? "DS_Sign_LaneDir_" + value.Replace('|', '_') : null;
            if (code == "5.15.2") return System.Array.IndexOf(LaneDirectionSingleValues, value) >= 0 ? "DS_Sign_LaneDir1_" + value : null;
            return e.NeedsValue ? null : e.CatalogId;
        }

        static SignCatalogEntry E(string code, string prefab, string title, SignKind kind, bool needsValue = false) =>
            new SignCatalogEntry { Code = code, CatalogId = prefab, Title = title, Kind = kind, NeedsValue = needsValue };
    }
}
