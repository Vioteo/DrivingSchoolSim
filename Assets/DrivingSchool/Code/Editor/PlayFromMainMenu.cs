using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// T46: Play в редакторе стартует с главного меню, как собранная игра, — из какой бы сцены ни нажали Play.
    /// Выключается пунктом «Driving School/Play from Main Menu» (запоминается для этого компьютера).
    /// В batchmode (проверки, генераторы) не действует.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromMainMenu
    {
        const string PrefKey = "DrivingSchool.PlayFromMainMenu";
        const string MenuPath = "Driving School/Play from Main Menu";

        static PlayFromMainMenu() { EditorApplication.delayCall += Apply; }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set { EditorPrefs.SetBool(PrefKey, value); Apply(); }
        }

        public static void Apply()
        {
            SceneAsset start = null;
            if (Enabled && !Application.isBatchMode)
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
