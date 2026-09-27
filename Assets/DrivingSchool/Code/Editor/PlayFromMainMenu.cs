using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// T46: Play в редакторе стартует с главного меню, как собранная игра, — из какой бы сцены ни нажали Play.
    /// Выключается пунктом «Driving School/Play from Main Menu» (запоминается для этого компьютера).
    /// В batchmode (проверки, генераторы) и во время прогона тестов не действует: PlayMode-тесты входят в Play
    /// в своей сцене (например TrainingSceneTests на автодроме).
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromMainMenu
    {
        const string PrefKey = "DrivingSchool.PlayFromMainMenu";
        const string MenuPath = "Driving School/Play from Main Menu";

        static bool testsRunning;

        static PlayFromMainMenu()
        {
            EditorApplication.delayCall += Apply;
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new TestRunGuard());
        }

        sealed class TestRunGuard : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { testsRunning = true; Apply(); }
            public void RunFinished(ITestResultAdaptor result) { testsRunning = false; Apply(); }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set { EditorPrefs.SetBool(PrefKey, value); Apply(); }
        }

        public static void Apply()
        {
            SceneAsset start = null;
            if (Enabled && !Application.isBatchMode && !testsRunning)
            {
                start = AssetDatabase.LoadAssetAtPath<SceneAsset>(UIBuilder.MenuScenePath);
                if (start == null) Debug.LogWarning($"[PlayFromMainMenu] Нет {UIBuilder.MenuScenePath} — Driving School/Build Main Menu scene");
            }
            EditorSceneManager.playModeStartScene = start;
        }

        [MenuItem(MenuPath, priority = 0)]
        static void Toggle() { Enabled = !Enabled; }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate() { Menu.SetChecked(MenuPath, Enabled); return true; }
    }
}
