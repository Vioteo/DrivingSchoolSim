using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Выбор задания; загрузкой сцен управляет MainMenuFlow.</summary>
    public sealed class LessonCatalogController : MonoBehaviour
    {
        public const string FreeDrive = "free-drive";
        public Button lessonButton, rangeButton, startButton, backButton;
        public TMP_Text title, description, mode;
        public event Action<string> LaunchRequested;
        public string SelectedId { get; private set; } = LessonLaunch.FirstLesson;
        public static bool ClosedThisFrame => closedFrame == Time.frameCount;
        static int closedFrame = -1;
        Action onBack;
        bool initialized;
        Button[] chain;

        void Awake() { Initialize(); }

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            chain = new[] { lessonButton, rangeButton, startButton, backButton };
            lessonButton.onClick.AddListener(() => SelectAssignment(LessonLaunch.FirstLesson));
            rangeButton.onClick.AddListener(() => SelectAssignment(FreeDrive));
            lessonButton.GetComponent<MenuItemView>().Focused += _ => SelectAssignment(LessonLaunch.FirstLesson);
            rangeButton.GetComponent<MenuItemView>().Focused += _ => SelectAssignment(FreeDrive);
            startButton.onClick.AddListener(() => LaunchRequested?.Invoke(SelectedId));
            backButton.onClick.AddListener(Back);
            for (int i = 0; i < chain.Length; i++)
                chain[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = chain[(i + chain.Length - 1) % chain.Length],
                    selectOnDown = chain[(i + 1) % chain.Length],
                    selectOnLeft = chain[(i + chain.Length - 1) % chain.Length],
                    selectOnRight = chain[(i + 1) % chain.Length] };
            SelectAssignment(SelectedId);
        }

        public void Open(Action back)
        {
            Initialize();
            onBack = back;
            gameObject.SetActive(true);
            Focus(SelectedId == FreeDrive ? rangeButton : lessonButton);
        }

        public void SelectAssignment(string id)
        {
            if (id != LessonLaunch.FirstLesson && id != FreeDrive) throw new ArgumentException("Неизвестное задание", nameof(id));
            SelectedId = id;
            bool guided = id == LessonLaunch.FirstLesson;
            title.text = guided ? "Начало движения" : "Тестовая площадка";
            mode.text = guided ? "УРОК 01 · С ПОШАГОВЫМИ ПОДСКАЗКАМИ" : "СВОБОДНАЯ ПРАКТИКА";
            description.text = guided
                ? "Подготовьте автомобиль, запустите двигатель и плавно троньтесь. Пройдите маршрут по учебной улице и завершите поездку остановкой.\n\nПодсказки ведут по шагам. Доступны механическая и автоматическая коробки передач."
                : "Исследуйте тестовый район в своём темпе. Тренируйте управление, торможение и манёвры, проезжайте перекрёстки и железнодорожный переезд.\n\nБез последовательности урока. Проверка правил и журнал поездки работают во время движения.";
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
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        void Update()
        {
            var es = EventSystem.current;
            var module = es != null ? es.currentInputModule as InputSystemUIInputModule : null;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            var kb = Keyboard.current;
            if (cancel != null ? cancel.WasPerformedThisFrame() : kb != null && kb.escapeKey.wasPressedThisFrame) { Back(); return; }
            if (kb != null && kb.tabKey.wasPressedThisFrame) MoveFocus(kb.shiftKey.isPressed ? -1 : 1);
            if (es != null && Array.FindIndex(chain, b => b.gameObject == es.currentSelectedGameObject) < 0)
                Focus(SelectedId == FreeDrive ? rangeButton : lessonButton);
        }
    }
}
