using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Пауза в поездке (T46, docs/ui-settings.md §1). Esc / Start геймпада открывает и закрывает.
    /// На паузе: Time.timeScale = 0, звук на паузе, скрипты сцены DrivingSchool.* (кроме UI) выключены,
    /// чтобы клавиши поездки (Enter — стартер, F-клавиши полигона) не срабатывали за меню.
    /// Сценами не управляет — только генерирует намерения для AppNavigator.
    /// </summary>
    public sealed class PauseMenuController : MonoBehaviour
    {
        public GameObject panel;
        public Button resumeButton;
        public Button restartButton;
        public Button exitToMenuButton;
        public Button settingsButton;
        public Button finishButton;
        public UITheme defaultTheme;

        [Header("Намерения")]
        public UnityEvent OnRestartRequested = new UnityEvent();
        public UnityEvent OnExitToMenuRequested = new UnityEvent();
        public UnityEvent OnSettingsRequested = new UnityEvent();

        /// <summary>«Завершить поездку»: обработчик показывает разбор (DriveSession). Без обработчика — выход в меню.</summary>
        public event System.Action FinishRequested;

        /// <summary>Экран настроек, который открывает пункт «Настройки» (добавляет AppNavigator).</summary>
        public SettingsScreenController settings;

        readonly List<Selectable> chain = new List<Selectable>();
        readonly List<Behaviour> frozen = new List<Behaviour>();
        float savedTimeScale = 1f;
        bool initialized;

        public bool IsPaused { get; private set; }
        public IReadOnlyList<Behaviour> Frozen => frozen;

        void Awake() { Initialize(); }

        /// <summary>Идемпотентно. Вызывается из Awake; в EditMode-тестах — вручную.</summary>
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (UIThemeState.Current == null) UIThemeState.Set(defaultTheme);
            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (restartButton != null) restartButton.onClick.AddListener(() => LeaveWith(OnRestartRequested, "restart"));
            if (exitToMenuButton != null) exitToMenuButton.onClick.AddListener(() => LeaveWith(OnExitToMenuRequested, "exit-to-menu"));
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (finishButton != null) finishButton.onClick.AddListener(Finish);

            chain.Clear();
            foreach (var b in new[] { resumeButton, finishButton, restartButton, settingsButton, exitToMenuButton })
                if (b != null) chain.Add(b);
            for (int i = 0; i < chain.Count; i++)
                chain[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = chain[(i - 1 + chain.Count) % chain.Count],
                    selectOnDown = chain[(i + 1) % chain.Count],
                };
            if (panel != null) panel.SetActive(false);
        }

        void Start() { if (Application.isPlaying) EnsureEventSystem(); }

        /// <summary>Настройки поверх паузы: время стоит, пункты «только до поездки» заблокированы.</summary>
        public void OpenSettings()
        {
            OnSettingsRequested.Invoke();
            if (settings == null || !IsPaused) return;
            if (panel != null) panel.SetActive(false);
            settings.Open(true, () => { if (panel != null && IsPaused) { panel.SetActive(true); if (chain.Count > 0) Select(chain[0]); } });
        }

        /// <summary>Завершить поездку: пауза остаётся (время стоит), панель паузы уступает место разбору.</summary>
        public void Finish()
        {
            if (FinishRequested == null) { LeaveWith(OnExitToMenuRequested, "exit-to-menu"); return; }
            if (panel != null) panel.SetActive(false);
            Debug.Log("[PauseMenu] intent: finish");
            FinishRequested.Invoke();
        }

        void Update()
        {
            if (SettingsScreenController.IsAnyOpen || SettingsScreenController.ClosedThisFrame) return;   // Esc обрабатывают настройки
            if (DebriefView.IsOpen) return;                                                                // и разбор поездки
            if (TogglePressed()) { if (IsPaused) Resume(); else Pause(); return; }
            if (!IsPaused) return;

            var es = EventSystem.current;
            if (es == null || chain.Count == 0) return;
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) MoveFocus(kb.shiftKey.isPressed ? -1 : 1);
            var current = es.currentSelectedGameObject;
            var sel = current != null ? current.GetComponent<Selectable>() : null;
            if (sel == null || !chain.Contains(sel)) Select(chain[0]);
        }

        public void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            FreezeScene();
            if (panel != null) panel.SetActive(true);
            foreach (var view in GetComponentsInChildren<MenuItemView>(true)) view.Refresh();
            if (chain.Count > 0) Select(chain[0]);
            Debug.Log("[PauseMenu] paused");
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
            foreach (var b in frozen) if (b != null) b.enabled = true;
            frozen.Clear();
            if (panel != null) panel.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Debug.Log("[PauseMenu] resumed");
        }

        void LeaveWith(UnityEvent intent, string name)
        {
            // Время и звук возвращаем до смены сцены, иначе следующая сцена стартует замороженной.
            Resume();
            Debug.Log($"[PauseMenu] intent: {name}");
            intent.Invoke();
        }

        void OnDestroy()
        {
            if (!IsPaused) return;
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
        }

        void FreezeScene()
        {
            frozen.Clear();
            foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!b.enabled || b.transform.IsChildOf(transform)) continue;
                var ns = b.GetType().Namespace ?? "";
                if (!ns.StartsWith("DrivingSchool") || ns.StartsWith("DrivingSchool.Presentation.UI")) continue;
                b.enabled = false;
                frozen.Add(b);
            }
        }

        public void MoveFocus(int direction)
        {
            if (chain.Count == 0) return;
            var es = EventSystem.current;
            var current = es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.GetComponent<Selectable>() : null;
            int i = current != null ? chain.IndexOf(current) : -1;
            int next = i < 0 ? 0 : ((i + (direction >= 0 ? 1 : -1)) % chain.Count + chain.Count) % chain.Count;
            Select(chain[next]);
        }

        void Select(Selectable target)
        {
            var es = EventSystem.current;
            if (es != null) { es.SetSelectedGameObject(target.gameObject); return; }
            foreach (var view in GetComponentsInChildren<MenuItemView>(true)) view.SetFocused(view.Selectable == target);
        }

        static bool TogglePressed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            return pad != null && pad.startButton.wasPressedThisFrame;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
