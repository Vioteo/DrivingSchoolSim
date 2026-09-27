using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Применение настроек оболочки (ADR-016): экран, частота кадров, тени и сглаживание URP, камеры, тема, звук.
    /// Настройки поездки (FOV, вид, зеркала, руление, КПП) применяет DriveSettingsApplier в Presentation.
    /// Изменения URP-ассета в редакторе откатываются при выходе из Play Mode — ассет на диске не меняется.
    /// </summary>
    public static class SettingsApplier
    {
        public static readonly FullScreenMode[] DisplayModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        static readonly int[] FpsLimits = { 30, 60, 120, -1 };
        // Тени: дальность (м), разрешение карты, число каскадов. Средние = прежние значения URP.asset.
        static readonly (float distance, int resolution, int cascades)[] ShadowLevels = { (0f, 1024, 1), (35f, 1024, 1), (50f, 2048, 1), (120f, 4096, 4) };

        struct UrpSnapshot { public UniversalRenderPipelineAsset asset; public float distance; public int resolution, cascades, msaa; }
        static UrpSnapshot? original;
        static bool mutedByFocus;

        /// <summary>Разрешения монитора для пункта «Разрешение», от меньшего к большему.</summary>
        public static List<string> MonitorResolutions()
        {
            var list = new List<string>();
            foreach (var r in Screen.resolutions.OrderBy(r => r.width).ThenBy(r => r.height))
            {
                if (r.width < 1024 || r.height < 600) continue;
                string label = $"{r.width} × {r.height}";
                if (!list.Contains(label)) list.Add(label);
            }
            return list;
        }

        public static void ApplyAll(GameSettings s)
        {
            ApplyFrameRate(s);
            ApplyUrp(s);
            ApplyCameras(s);
            ApplyTheme(s.gameplay.uiTheme);
            ApplyAudio(s);
        }

        /// <summary>Режим экрана и разрешение. В редакторе окно Game не переключается — только лог.</summary>
        public static void ApplyDisplay(GameSettings s)
        {
            var mode = DisplayModes[Mathf.Clamp(s.graphics.displayMode, 0, DisplayModes.Length - 1)];
            if (!SettingsSchema.TryParseResolution(s.graphics.resolution, out int w, out int h)) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
            if (Application.isEditor) { Debug.Log($"[Settings] экран: {mode} {w}×{h} (в редакторе не применяется)"); return; }
            if (Screen.fullScreenMode == mode && Screen.width == w && Screen.height == h) return;
            Screen.SetResolution(w, h, mode);
        }

        public static void ApplyFrameRate(GameSettings s)
        {
            QualitySettings.vSyncCount = s.graphics.vSync ? 1 : 0;
            Application.targetFrameRate = s.graphics.vSync ? -1 : FpsLimits[Mathf.Clamp(s.graphics.fpsLimit, 0, FpsLimits.Length - 1)];
        }

        public static void ApplyUrp(GameSettings s)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            if (original == null)
            {
                original = new UrpSnapshot { asset = urp, distance = urp.shadowDistance, resolution = urp.mainLightShadowmapResolution, cascades = urp.shadowCascadeCount, msaa = urp.msaaSampleCount };
                Application.quitting += RestoreUrp;
            }
            var lvl = ShadowLevels[Mathf.Clamp(s.graphics.shadows, 0, ShadowLevels.Length - 1)];
            urp.shadowDistance = lvl.distance;
            urp.mainLightShadowmapResolution = lvl.resolution;
            urp.shadowCascadeCount = lvl.cascades;
            urp.msaaSampleCount = s.graphics.antiAliasing == 3 ? 4 : 1;
        }

        static void RestoreUrp()
        {
            Application.quitting -= RestoreUrp;
            if (original == null) return;
            var o = original.Value;
            if (o.asset != null)
            {
                o.asset.shadowDistance = o.distance; o.asset.mainLightShadowmapResolution = o.resolution;
                o.asset.shadowCascadeCount = o.cascades; o.asset.msaaSampleCount = o.msaa;
            }
            original = null;
        }

        /// <summary>Сглаживание и тени на камерах, которые рисуют на экран (зеркала с RenderTexture не трогаем).</summary>
        public static void ApplyCameras(GameSettings s)
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.targetTexture != null) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                data.antialiasing = s.graphics.antiAliasing == 1 ? AntialiasingMode.FastApproximateAntialiasing
                                  : s.graphics.antiAliasing == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                cam.allowMSAA = s.graphics.antiAliasing == 3;
                data.renderShadows = s.graphics.shadows > 0;
            }
        }

        public static void ApplyTheme(int index)
        {
            var catalog = UIThemeCatalog.Instance;
            var theme = catalog != null ? catalog.Get(index) : null;
            if (theme != null) UIThemeState.Set(theme);
        }

        public static void ApplyAudio(GameSettings s)
        {
            AudioListener.volume = mutedByFocus && s.audio.muteInBackground ? 0f : Mathf.Clamp01(s.audio.master / 100f);
        }

        public static void OnFocusChanged(bool focused, GameSettings s)
        {
            mutedByFocus = !focused;
            ApplyAudio(s);
        }
    }

    /// <summary>
    /// Загрузка и применение настроек при старте игры (до первой сцены), повторное применение к камерам новых сцен,
    /// звук в свёрнутом окне. Живёт весь процесс (DontDestroyOnLoad).
    /// </summary>
    public sealed class SettingsRuntime : MonoBehaviour
    {
        static SettingsRuntime instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            SettingsSchema.SetResolutionOptions(SettingsApplier.MonitorResolutions());
            var loaded = SettingsStore.Load();
            var go = new GameObject("SettingsRuntime") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SettingsRuntime>();
            SettingsService.Publish(loaded);                 // до подписки: экран применяем один раз ниже
            SettingsApplier.ApplyDisplay(SettingsService.Current);
            SettingsApplier.ApplyAll(SettingsService.Current);
        }

        void OnEnable() { SettingsService.Applied += OnApplied; SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { SettingsService.Applied -= OnApplied; SceneManager.sceneLoaded -= OnSceneLoaded; }
        void OnDestroy() { if (instance == this) instance = null; }

        void OnApplied(GameSettings s) => SettingsApplier.ApplyAll(s);
        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SettingsApplier.ApplyCameras(SettingsService.Current);
        void OnApplicationFocus(bool focus) => SettingsApplier.OnFocusChanged(focus, SettingsService.Current);
    }
}
