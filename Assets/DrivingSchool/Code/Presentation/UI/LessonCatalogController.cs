using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Выбор задания; загрузкой сцен управляет MainMenuFlow. Задания: урок 1, город, упражнения на площадке (T68: выбор
    /// упражнения — Q / E, лепестки руля или кнопки &lt; &gt;) и экзамен на площадке. Список упражнений — из подсказок
    /// guided-lessons.json (уроки с courseLesson), в их порядке.
    /// </summary>
    public sealed class LessonCatalogController : MonoBehaviour
    {
        public const string FreeDrive = "free-drive";
        public const string FreeDriveTitle = "Город";
        /// <summary>Пункт «Упражнения на площадке»; запускается выбранное упражнение (<see cref="SelectedExerciseId"/>).</summary>
        public const string AutodromePractice = "autodrome-practice";
        public const string AutodromePracticeTitle = "Упражнения на площадке";
        public const string AutodromeExamTitle = "Экзамен на площадке";
        public Button lessonButton, rangeButton, startButton, backButton;
        [Tooltip("T68: упражнения и экзамен на площадке; кнопки выбора упражнения мышью")]
        public Button autodromeButton, examButton, prevExerciseButton, nextExerciseButton;
        public TMP_Text title, description, mode;
        public event Action<string> LaunchRequested;
        public string SelectedId { get; private set; } = LessonLaunch.FirstLesson;
        public static bool ClosedThisFrame => closedFrame == Time.frameCount;
        static int closedFrame = -1;
        static int rememberedExercise;
        Action onBack;
        bool initialized;
        Button[] chain;

        [Serializable] sealed class PackLesson { public string id, title, briefing, courseLesson; }
        [Serializable] sealed class Pack { public PackLesson[] lessons; }
        PackLesson[] exercises = new PackLesson[0];

        public int ExerciseCount => exercises.Length;
        public int ExerciseIndex { get; private set; }
        /// <summary>id подсказок выбранного упражнения площадки (например «ex-slalom»); null — упражнений нет.</summary>
        public string SelectedExerciseId => exercises.Length > 0 ? exercises[ExerciseIndex].id : null;

        void Awake() { Initialize(); }

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            LoadExercises();
            chain = new[] { lessonButton, rangeButton, autodromeButton, examButton, startButton, backButton }.Where(b => b != null).ToArray();
            Bind(lessonButton, LessonLaunch.FirstLesson);
            Bind(rangeButton, FreeDrive);
            if (exercises.Length > 0) Bind(autodromeButton, AutodromePractice);
            else if (autodromeButton != null) autodromeButton.interactable = false;
            Bind(examButton, LessonLaunch.AutodromeExam);
            if (prevExerciseButton != null) { prevExerciseButton.onClick.AddListener(() => ShiftExercise(-1)); prevExerciseButton.navigation = new Navigation { mode = Navigation.Mode.None }; }
            if (nextExerciseButton != null) { nextExerciseButton.onClick.AddListener(() => ShiftExercise(1)); nextExerciseButton.navigation = new Navigation { mode = Navigation.Mode.None }; }
            startButton.onClick.AddListener(() => LaunchRequested?.Invoke(SelectedId == AutodromePractice ? SelectedExerciseId : SelectedId));
            backButton.onClick.AddListener(Back);
            for (int i = 0; i < chain.Length; i++)
                chain[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = chain[(i + chain.Length - 1) % chain.Length],
                    selectOnDown = chain[(i + 1) % chain.Length],
                    selectOnLeft = chain[(i + chain.Length - 1) % chain.Length],
                    selectOnRight = chain[(i + 1) % chain.Length] };
            ExerciseIndex = exercises.Length > 0 ? Mathf.Clamp(rememberedExercise, 0, exercises.Length - 1) : 0;
            SelectAssignment(SelectedId);
        }

        void Bind(Button button, string id)
        {
            if (button == null) return;
            button.onClick.AddListener(() => SelectAssignment(id));
            var view = button.GetComponent<MenuItemView>();
            if (view != null) view.Focused += _ => SelectAssignment(id);
        }

        void LoadExercises()
        {
            var asset = Resources.Load<TextAsset>("guided-lessons");
            if (asset == null) return;
            try
            {
                var pack = JsonUtility.FromJson<Pack>(asset.text);
                if (pack?.lessons != null) exercises = pack.lessons.Where(l => l != null && !string.IsNullOrEmpty(l.courseLesson) && !string.IsNullOrEmpty(l.id)).ToArray();
            }
            catch (ArgumentException e) { Debug.LogWarning("[Catalog] упражнения площадки не прочитаны: " + e.Message); }
        }

        public void Open(Action back)
        {
            Initialize();
            onBack = back;
            gameObject.SetActive(true);
            Focus(ButtonFor(SelectedId));
        }

        Button ButtonFor(string id)
        {
            Button b = id == FreeDrive ? rangeButton : id == AutodromePractice ? autodromeButton : id == LessonLaunch.AutodromeExam ? examButton : lessonButton;
            return b != null ? b : lessonButton;
        }

        public void SelectAssignment(string id)
        {
            bool known = id == LessonLaunch.FirstLesson || id == FreeDrive || id == LessonLaunch.AutodromeExam || (id == AutodromePractice && exercises.Length > 0);
            if (!known) throw new ArgumentException("Неизвестное задание", nameof(id));
            SelectedId = id;
            if (id == AutodromePractice) { ShowExercise(); return; }
            if (id == LessonLaunch.AutodromeExam)
            {
                title.text = AutodromeExamTitle;
                mode.text = "ПЛОЩАДКА · ВСЕ УПРАЖНЕНИЯ ПОДРЯД · БЕЗ ПОДСКАЗОК";
                description.text = "Все упражнения автодрома по маршруту — от старта до финиша. Между упражнениями едете сами, без перемещений: рамка на земле показывает путь.\n\n" +
                                   "Ошибки — игровые баллы (касание конуса, стоп-линия, указатель поворота, заглохание, откат на эстакаде, красный свет…); с 5 баллов экзамен не сдан. В конце — разбор ошибок.";
                return;
            }
            bool guided = id == LessonLaunch.FirstLesson;
            title.text = guided ? "Начало движения" : FreeDriveTitle;
            mode.text = guided ? "УРОК 01 · С ПОШАГОВЫМИ ПОДСКАЗКАМИ" : "СВОБОДНАЯ ПРАКТИКА В ГОРОДЕ";
            description.text = guided
                ? "Подготовьте автомобиль, запустите двигатель и плавно троньтесь. Пройдите маршрут по учебной улице и завершите поездку остановкой.\n\nПодсказки ведут по шагам. Доступны механическая и автоматическая коробки передач."
                : "Поездка начинается в городе: перекрёстки со светофорами и знаками приоритета, полосы с направлениями движения, кольцо, переезд, пешеходный переход с лежачими полицейскими и разные ограничения скорости. Боты и пешеходы соблюдают ПДД.\n\nИнструктор отмечает нарушения: скорость, выезд на встречную, поворотники, полосу для поворота, дистанцию и остановки. F9 — на площадку полигона.";
        }

        /// <summary>Следующее (+1) или предыдущее (−1) упражнение площадки; выбирает пункт «Упражнения на площадке».</summary>
        public void ShiftExercise(int delta)
        {
            if (exercises.Length == 0) return;
            ExerciseIndex = ((ExerciseIndex + delta) % exercises.Length + exercises.Length) % exercises.Length;
            rememberedExercise = ExerciseIndex;
            SelectAssignment(AutodromePractice);
            if (autodromeButton != null) Focus(autodromeButton);
        }

        void ShowExercise()
        {
            var e = exercises[ExerciseIndex];
            title.text = e.title;
            mode.text = $"ПЛОЩАДКА · УПРАЖНЕНИЕ {ExerciseIndex + 1} ИЗ {exercises.Length} · Q / E — ДРУГОЕ";
            description.text = (string.IsNullOrEmpty(e.briefing) ? "" : e.briefing + "\n\n") +
                               "Машина стоит у упражнения с заглушённым двигателем. Подсказки ведут по шагам: подготовка, въезд, манёвр, выезд. " +
                               "Ошибки — игровые баллы: с 5 баллов упражнение не зачтено.";
        }

        public void Back()
        {
            closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            onBack?.Invoke();
        }

        public void MoveFocus(int direction)
        {
            var es = EventSystem.current;
            int i = es == null ? -1 : Array.FindIndex(chain, b => b.gameObject == es.currentSelectedGameObject);
            int next = i < 0 ? (direction < 0 ? chain.Length - 1 : 0) : (i + (direction < 0 ? chain.Length - 1 : 1)) % chain.Length;
            Focus(chain[next]);
        }

        static void Focus(Button button)
        {
            if (EventSystem.current != null && button != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        void Update()
        {
            var es = EventSystem.current;
            var module = es != null ? es.currentInputModule as InputSystemUIInputModule : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            var kb = Keyboard.current;
            if (cancel != null ? cancel.WasPerformedThisFrame() : kb != null && kb.escapeKey.wasPressedThisFrame) { Back(); return; }
            if (kb != null && kb.tabKey.wasPressedThisFrame) MoveFocus(kb.shiftKey.isPressed ? -1 : 1);
            if (exercises.Length > 0 && (SelectedId == AutodromePractice || (autodromeButton != null && es != null && es.currentSelectedGameObject == autodromeButton.gameObject)))
            {
                if (MenuInput.TabPrev) ShiftExercise(-1);
                else if (MenuInput.TabNext) ShiftExercise(1);
            }
            if (es != null && Array.FindIndex(chain, b => b.gameObject == es.currentSelectedGameObject) < 0)
                Focus(ButtonFor(SelectedId));
        }
    }
}
