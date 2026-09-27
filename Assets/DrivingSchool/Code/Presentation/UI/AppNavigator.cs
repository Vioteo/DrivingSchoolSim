using UnityEngine;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Координатор приложения (T46): переходы «главное меню ↔ поездка».
    /// Меню и пауза только генерируют намерения; сцены меняет только этот класс.
    /// В каждую сцену поездки, загруженную через него, добавляется меню паузы.
    /// </summary>
    public static class AppNavigator
    {
        public const string MainMenuScene = "MainMenu";

        static GameObject pausePrefab;
        static bool hooked;

        public static void Configure(GameObject pauseMenuPrefab)
        {
            pausePrefab = pauseMenuPrefab;
            if (hooked) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            hooked = true;
        }

        public static void StartDrive(string sceneName) { ResetTime(); Debug.Log($"[App] drive: {sceneName}"); SceneManager.LoadScene(sceneName); }
        public static void RestartDrive() { StartDrive(SceneManager.GetActiveScene().name); }
        public static void ToMainMenu() { ResetTime(); Debug.Log("[App] main menu"); SceneManager.LoadScene(MainMenuScene); }

        public static void Quit()
        {
            Debug.Log("[App] quit");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static void ResetTime() { Time.timeScale = 1f; AudioListener.pause = false; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || scene.name == MainMenuScene || pausePrefab == null) return;
            if (Object.FindAnyObjectByType<PauseMenuController>() != null) return;
            var go = Object.Instantiate(pausePrefab);
            go.name = pausePrefab.name;
            SceneManager.MoveGameObjectToScene(go, scene);
            var pause = go.GetComponent<PauseMenuController>();
            pause.OnRestartRequested.AddListener(RestartDrive);
            pause.OnExitToMenuRequested.AddListener(ToMainMenu);
        }
    }
}
