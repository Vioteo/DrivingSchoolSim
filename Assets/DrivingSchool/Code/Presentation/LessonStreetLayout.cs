using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Раскладка сцены «Учебная улица» (T49, docs/lessons.md): одна общая точка для генератора
    /// (LessonStreetBuilder) и тестов. Улица идёт по +Z вдоль X = 0, перекрёсток в начале координат,
    /// правостороннее движение: полоса на север — X 0…4, на юг — X −4…0. Модули — Road Kit (docs/road-kit.md).
    /// </summary>
    public static class LessonStreetLayout
    {
        public const string SceneName = "Lesson_Street";
        public const string ScenePath = "Assets/DrivingSchool/Scenes/Lesson_Street.unity";
        public const float RoadHalfWidth = 4f, SidewalkOuter = 6.2f;
        public const float JunctionHalf = 12f, StopLine = 10f;
        public const float StreetEnd = 132f, ArmEnd = 72f;
        public static readonly Vector3 Start = new Vector3(2.9f, 0f, -90f);

        /// <summary>Точка на проезжей части улицы (обе полосы, без перекрёстка и поперечных улиц).</summary>
        public static bool OnStreet(Vector3 p) => Mathf.Abs(p.x) <= RoadHalfWidth && Mathf.Abs(p.z) <= StreetEnd;
        public static bool InJunction(Vector3 p) => Mathf.Abs(p.x) <= JunctionHalf && Mathf.Abs(p.z) <= JunctionHalf;
        public static bool NorthboundLane(Vector3 p) => p.x >= 0f && p.x <= RoadHalfWidth && Mathf.Abs(p.z) <= StreetEnd;
    }
}
