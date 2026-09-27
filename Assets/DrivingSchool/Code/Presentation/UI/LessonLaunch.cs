namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Какой пошаговый урок запустить в следующей сцене поездки (T49). Меню ставит id перед загрузкой сцены,
    /// сессия поездки читает его при старте. «Пройти снова» оставляет урок тем же. null — свободная поездка.
    /// </summary>
    public static class LessonLaunch
    {
        /// <summary>Урок 1 «Начало движения» (Data/Lessons/Resources/guided-lessons.json).</summary>
        public const string FirstLesson = "start-moving";
        /// <summary>Сцена урока 1 — «Учебная улица» (генератор Driving School/Lessons/Build lesson street scene).</summary>
        public const string FirstLessonScene = "Lesson_Street";

        public static string LessonId { get; set; }
    }
}
