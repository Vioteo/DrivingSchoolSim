using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Раскладка тестового полигона (docs/vehicle-test-range.md), метры. Единый источник для генератора сцены
    /// (VehicleTestRangeBuilder) и рантайма (подсказки инструктора, проверка переезда).
    /// Дорога A идёт по +Z вдоль X = 0, поворот направо R30, дорога B по +X к площадке; дорога C — вдоль X = RoadCX.
    /// </summary>
    public static class TestRangeLayout
    {
        public const float RoadWidth = 8f, RoadAStartZ = -30f, RoadAEndZ = 260f, CurveRadius = 30f, RoadBEndX = 150f;
        public const float BumpRubberZ = 80f, BumpAsphaltZ = 140f;
        public const float SlalomStartZ = 180f, SlalomEndZ = 228f;
        public const float RoadCX = 195f, RoadCEndZ = 10f, RailZ = 185f, HillStartZ = 75f, HillEndZ = 123f;
        public const float HillGrade = 0.14f, HillRamp = 14f;
        public const float CrossingHalfLength = 4.8f;
        public static readonly Vector3 PadCentre = new Vector3(195f, 0f, 290f);
        public const float PadSize = 90f;
        /// <summary>
        /// Городской район (T51, T55/T56): центр регулируемого перекрёстка X1 (2+2 × 2+2); район занимает X −500…−12, Z −205…265 (западная часть и эстакада — T65, кольцевые дороги — T66),
        /// въезд с дороги A — улица 1+1 на Z = DistrictEntryZ (восточный рукав кольца), ж/д переезд на южном рукаве кольца.
        /// </summary>
        public static readonly Vector3 DistrictOrigin = new Vector3(-150f, 0.02f, 110f);
        public const float DistrictEntryZ = 22f;
        public const float DistrictMinX = -500f, DistrictMaxX = -12f, DistrictMinZ = -205f, DistrictMaxZ = 265f;   // west part and overpass since T65, loops since T66

        public static bool OnRoadA(Vector3 p) => Mathf.Abs(p.x) < RoadWidth / 2 + 2f && p.z > RoadAStartZ - 5f && p.z < RoadAEndZ + 2f;
        public static bool OnRoadC(Vector3 p) => Mathf.Abs(p.x - RoadCX) < RoadWidth / 2 + 2f && p.z > RoadCEndZ - 25f && p.z < PadCentre.z - PadSize / 2;
        public static bool OnPad(Vector3 p) => Mathf.Abs(p.x - PadCentre.x) < PadSize / 2 && Mathf.Abs(p.z - PadCentre.z) < PadSize / 2;
        /// <summary>Машина на настиле переезда (между рельсами и пандусами).</summary>
        public static bool OnRailwayCrossing(Vector3 p) => Mathf.Abs(p.x - RoadCX) < RoadWidth / 2 + 1f && Mathf.Abs(p.z - RailZ) < CrossingHalfLength;

        public static bool InDistrict(Vector3 p) => p.x > DistrictMinX && p.x < DistrictMaxX && p.z > DistrictMinZ && p.z < DistrictMaxZ;

        public static string ZoneName(Vector3 p)
        {
            if (InDistrict(p)) return "городской район";
            if (OnRailwayCrossing(p)) return "ж/д переезд";
            if (OnPad(p)) return "площадка";
            if (OnRoadC(p)) return p.z > HillStartZ - 5 && p.z < HillEndZ + 5 ? "горка" : "дорога C";
            if (OnRoadA(p))
            {
                if (Mathf.Abs(p.z - BumpRubberZ) < 8) return "резиновая неровность";
                if (Mathf.Abs(p.z - BumpAsphaltZ) < 8) return "асфальтовая неровность";
                if (p.z > SlalomStartZ - 5 && p.z < SlalomEndZ + 5) return "слалом";
                return "дорога A";
            }
            return "полигон";
        }
    }
}
