using System.Collections;
using TMPro;
using UnityEngine;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Главное меню и каталог заданий передают запуск сцены координатору AppNavigator.
    /// </summary>
    public sealed class MainMenuFlow : MonoBehaviour
    {
        public MainMenuController menu;
        public LessonCatalogController catalog;
        [Tooltip("Экран «Автомобиль» (T65)")] public GarageController garage;
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
            if (catalog != null)
            {
                catalog.gameObject.SetActive(false);
                catalog.Initialize();
                catalog.LaunchRequested += id => { if (id == LessonLaunch.FirstLesson) StartFirstLesson(); else if (id == LessonCatalogController.FreeDrive) StartDrive(); };
            }
            menu.OnSelectLessonRequested.AddListener(OpenAssignments);
            menu.OnTheoryRequested.AddListener(() => ShowNotice("Теория ПДД — в разработке. Сейчас доступны город и тестовый полигон."));
            menu.OnSettingsRequested.AddListener(OpenSettings);
            menu.OnGarageRequested.AddListener(OpenGarage);
            if (garage != null) { garage.gameObject.SetActive(false); garage.Initialize(); }
            menu.OnExitConfirmed.AddListener(AppNavigator.Quit);
        }

        void Update()
        {
            if (noticePanel != null && noticePanel.activeSelf && Time.unscaledTime > noticeUntil) noticePanel.SetActive(false);
        }

        public void OpenAssignments()
        {
            if (loading) return;
            if (catalog == null) { ShowNotice("Задания пока недоступны."); return; }
            menu.gameObject.SetActive(false);
            catalog.Open(() => { menu.gameObject.SetActive(true); menu.Select(menu.lessonsButton); });
        }

        public void OpenGarage()
        {
            if (loading) return;
            if (garage == null) { ShowNotice("Экран «Автомобиль» не собран — Driving School/Build UI Prefabs"); return; }
            menu.gameObject.SetActive(false);
            garage.Open(() => { menu.gameObject.SetActive(true); menu.Select(menu.garageButton); });
        }

        public void StartDrive() => Launch(null, driveScene);

        public void StartFirstLesson()
        {
            if (!Application.CanStreamedLevelBeLoaded(LessonLaunch.FirstLessonScene))
            { ShowNotice("Урок пока недоступен. Выберите другое задание."); return; }
            Launch(LessonLaunch.FirstLesson, LessonLaunch.FirstLessonScene);
        }

        void Launch(string lessonId, string scene)
        {
            if (loading) return;
            if (!Application.CanStreamedLevelBeLoaded(scene)) { ShowNotice("Задание пока недоступно. Выберите другое задание."); return; }
            LessonLaunch.LessonId = lessonId;
            sceneToLoad = scene;
            loading = true;
            if (catalog != null) catalog.gameObject.SetActive(false);
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
