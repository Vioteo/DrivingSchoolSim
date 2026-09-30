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
        [Tooltip("Старое затемнение «Загрузка…» (до T70); если есть экран загрузки — не нужно")] public GameObject loadingOverlay;
        [Tooltip("T70: экран загрузки — фон меню при входе и сцена задания при запуске")] public LoadingScreen loading;
        public GameObject noticePanel;
        public TMP_Text notice;
        public float noticeSeconds = 3f;

        float noticeUntil;
        bool launching;
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
                catalog.LaunchRequested += id =>
                {
                    if (id == LessonLaunch.FirstLesson) StartFirstLesson();
                    else if (id == LessonCatalogController.FreeDrive) StartDrive();
                    else if (!string.IsNullOrEmpty(id)) StartAutodrome(id);   // упражнение площадки или экзамен (T68)
                };
            }
            menu.OnSelectLessonRequested.AddListener(OpenAssignments);
            menu.OnTheoryRequested.AddListener(() => ShowNotice("Теория ПДД — в разработке. Сейчас доступны урок, город, упражнения и экзамен на площадке."));
            menu.OnSettingsRequested.AddListener(OpenSettings);
            menu.OnGarageRequested.AddListener(OpenGarage);
            if (garage != null) { garage.gameObject.SetActive(false); garage.Initialize(); }
            menu.OnExitConfirmed.AddListener(AppNavigator.Quit);
        }

        void Update()
        {
            // Пока экран загрузки закрывает меню (грузится фон), клавиши меню не срабатывают.
            if (menu != null && !launching && loading != null) menu.enabled = !loading.Showing;
            if (noticePanel != null && noticePanel.activeSelf && Time.unscaledTime > noticeUntil) noticePanel.SetActive(false);
        }

        public void OpenAssignments()
        {
            if (launching) return;
            if (catalog == null) { ShowNotice("Задания пока недоступны."); return; }
            menu.gameObject.SetActive(false);
            catalog.Open(() => { menu.gameObject.SetActive(true); menu.Select(menu.lessonsButton); });
        }

        public void OpenGarage()
        {
            if (launching) return;
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

        /// <summary>Упражнение площадки (id подсказок, «ex-…») или экзамен (<see cref="LessonLaunch.AutodromeExam"/>), T68.</summary>
        public void StartAutodrome(string id) => Launch(id, LessonLaunch.AutodromeScene);

        void Launch(string lessonId, string scene)
        {
            if (launching) return;
            if (!Application.CanStreamedLevelBeLoaded(scene)) { ShowNotice("Задание пока недоступно. Выберите другое задание."); return; }
            LessonLaunch.LessonId = lessonId;
            sceneToLoad = scene;
            launching = true;
            if (catalog != null) catalog.gameObject.SetActive(false);
            if (loading != null) loading.Show("Загрузка задания…");
            else if (loadingOverlay != null) loadingOverlay.SetActive(true);
            if (menu != null) menu.enabled = false; // клавиши меню не срабатывают во время загрузки
            StartCoroutine(LoadNextFrame());
        }

        IEnumerator LoadNextFrame()
        {
            yield return null; // дать кадр на отрисовку «Загрузка…»
            if (loading == null) { AppNavigator.StartDrive(sceneToLoad); yield break; }
            // T70: асинхронно, с полосой прогресса; сцена меню (и этот экран) уходит, когда задание загрузилось.
            var op = AppNavigator.StartDriveAsync(sceneToLoad);
            while (op != null && !op.isDone) { loading.SetProgress(op.progress / 0.9f); yield return null; }
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
