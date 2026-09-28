namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Какой пошаговый урок запустить в следующей сцене поездки (T49). Меню ставит id перед загрузкой сцены,
    /// сессия поездки читает его при старте. «Пройти снова» оставляет урок тем же. null — свободная поездка.
    /// Автодром (T68): id упражнения — id подсказок из guided-lessons.json (например «ex-slalom»),
    /// экзамен по всей площадке — <see cref="AutodromeExam"/>; сцена — <see cref="AutodromeScene"/>.
    /// </summary>
    public static class LessonLaunch
    {
        /// <summary>Урок 1 «Начало движения» (Data/Lessons/Resources/guided-lessons.json).</summary>
        public const string FirstLesson = "start-moving";
        /// <summary>Сцена урока 1 — «Учебная улица» (генератор Driving School/Lessons/Build lesson street scene).</summary>
        public const string FirstLessonScene = "Lesson_Street";
        /// <summary>Сцена автодрома (генератор Driving School/Training Ground/Build scene and models).</summary>
        public const string AutodromeScene = "Autodrome_Training";
        /// <summary>Экзамен на площадке: все упражнения подряд по маршруту, без подсказок и телепортов.</summary>
        public const string AutodromeExam = "autodrome-exam";

        public static string LessonId { get; set; }
    }
}
