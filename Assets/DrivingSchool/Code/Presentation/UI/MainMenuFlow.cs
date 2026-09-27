using System.Collections;
using TMPro;
using UnityEngine;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Связка главного меню со сценами (T46). Живёт в сцене MainMenu, её собирает UIBuilder (Driving School/Build Main Menu scene).
    /// «Продолжить занятие» — свободная поездка по тестовому полигону, «Занятия» — пошаговый урок 1 «Начало движения»
    /// на учебной улице (T49; экрана выбора урока пока нет); «Теория ПДД» — «в разработке»; «Настройки» — экран настроек (T43).
    /// </summary>
    public sealed class MainMenuFlow : MonoBehaviour
    {
        public MainMenuController menu;
        public GameObject pauseMenuPrefab;
        public GameObject settingsPrefab;
        public GameObject hudPrefab;
        [Tooltip("Экран настроек в сцене меню")] public SettingsScreenController settings;
        [Tooltip("Имя сцены из Build Settings")] public string driveScene = "VehicleTestRange";
        public GameObject loadingOverlay;
        public GameObject noticePanel;
        public TMP_Text notice;
        public float noticeSeconds = 3f;

        float noticeUntil;
        bool loading;
        string sceneToLoad;

        void Awake()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            AppNavigator.Configure(pauseMenuPrefab, settingsPrefab, hudPrefab);
            if (loadingOverlay != null) loadingOverlay.SetActive(false);
            if (noticePanel != null) noticePanel.SetActive(false);
            if (menu == null) return;
            menu.Initialize();
            menu.OnStartLessonRequested.AddListener(StartDrive);
            menu.OnSelectLessonRequested.AddListener(StartFirstLesson);
            menu.OnTheoryRequested.AddListener(() => ShowNotice("Теория ПДД — в разработке. Сейчас доступен тестовый полигон."));
            menu.OnSettingsRequested.AddListener(OpenSettings);
            menu.OnExitConfirmed.AddListener(AppNavigator.Quit);
        }

        void Update()
        {
            if (noticePanel != null && noticePanel.activeSelf && Time.unscaledTime > noticeUntil) noticePanel.SetActive(false);
        }

        public void StartDrive() => Launch(null, driveScene);

        public void StartFirstLesson()
        {
            if (!Application.CanStreamedLevelBeLoaded(LessonLaunch.FirstLessonScene))
            { ShowNotice("Сцена урока не собрана: Driving School → Lessons → Build lesson street scene"); return; }
            Launch(LessonLaunch.FirstLesson, LessonLaunch.FirstLessonScene);
        }

        void Launch(string lessonId, string scene)
        {
            if (loading) return;
            LessonLaunch.LessonId = lessonId;
            sceneToLoad = scene;
            loading = true;
            if (loadingOverlay != null) loadingOverlay.SetActive(true);
            if (menu != null) menu.enabled = false; // клавиши меню не срабатывают во время загрузки
            StartCoroutine(LoadNextFrame());
        }

        IEnumerator LoadNextFrame()
        {
            yield return null; // дать кадр на отрисовку «Загрузка…»
            AppNavigator.StartDrive(sceneToLoad);
        }

        public void OpenSettings()
        {
            if (settings == null) { ShowNotice("Экран настроек не собран — Driving School/Build UI Prefabs"); return; }
            menu.gameObject.SetActive(false);   // меню не слушает клавиши, пока открыты настройки
            settings.Open(false, () => menu.gameObject.SetActive(true));
        }

        void ShowNotice(string text)
        {
            if (notice == null || noticePanel == null) return;
            notice.text = text;
            noticePanel.SetActive(true);
            noticeUntil = Time.unscaledTime + noticeSeconds;
        }
    }
}
