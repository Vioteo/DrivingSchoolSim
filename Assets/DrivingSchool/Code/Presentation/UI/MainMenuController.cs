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
    /// Главное меню (T03). Только генерирует намерения для координатора приложения;
    /// физикой, уроком и выходом из программы не управляет.
    /// Навигация: стрелки/крестовина, Tab/Shift+Tab, Enter; Escape — диалог выхода, а не выход.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Пункты меню (сверху вниз)")]
        public Button continueButton;
        public Button lessonsButton;
        public Button theoryButton;
        public Button examButton;
        public Button settingsButton;
        public Button exitButton;

        [Header("Диалог выхода")]
        public GameObject exitDialog;
        public Button exitCancelButton;
        public Button exitConfirmButton;

        public UITheme defaultTheme;

        [Header("Намерения")]
        public UnityEvent OnStartLessonRequested = new UnityEvent();
        public UnityEvent OnSelectLessonRequested = new UnityEvent();
        public UnityEvent OnTheoryRequested = new UnityEvent();
        public UnityEvent OnSettingsRequested = new UnityEvent();
        public UnityEvent OnExitConfirmed = new UnityEvent();

        readonly List<Selectable> menuChain = new List<Selectable>();
        readonly List<Selectable> dialogChain = new List<Selectable>();
        Selectable focused;
        bool initialized;

        public bool IsExitDialogOpen => exitDialog != null && exitDialog.activeSelf;
        public Selectable Focused => focused;

        void Awake() { Initialize(); }

        void OnEnable()
        {
            if (Application.isPlaying) EnsureEventSystem();
            Select(DefaultSelection());
        }

        /// <summary>Идемпотентно. Вызывается из Awake; в EditMode-тестах — вручную.</summary>
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            if (UIThemeState.Current == null) UIThemeState.Set(defaultTheme);

            Wire(continueButton, () => Raise(OnStartLessonRequested, "start-lesson"));
            Wire(lessonsButton, () => Raise(OnSelectLessonRequested, "select-lesson"));
            Wire(theoryButton, () => Raise(OnTheoryRequested, "theory"));
            Wire(settingsButton, () => Raise(OnSettingsRequested, "settings"));
            Wire(exitButton, RequestExit);
            Wire(exitCancelButton, CancelExit);
            Wire(exitConfirmButton, ConfirmExit);

            // Экзаменационный маршрут ещё не реализован: пункт виден, подписан «скоро», фокус его пропускает.
            if (examButton != null) { examButton.interactable = false; examButton.navigation = new Navigation { mode = Navigation.Mode.None }; }

            menuChain.Clear();
            foreach (var b in new[] { continueButton, lessonsButton, theoryButton, examButton, settingsButton, exitButton })
                if (b != null && b.interactable) menuChain.Add(b);
            dialogChain.Clear();
            foreach (var b in new[] { exitCancelButton, exitConfirmButton })
                if (b != null) dialogChain.Add(b);

            LinkChain(menuChain, vertical: true);
            LinkChain(dialogChain, vertical: false);
            if (exitDialog != null) exitDialog.SetActive(false);
            foreach (var view in GetComponentsInChildren<MenuItemView>(true)) view.Refresh();
        }

        void Update()
        {
            var es = EventSystem.current;
            if (es == null) return;
            if (CancelPressed(es)) { HandleCancel(); return; }

            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) MoveFocus(kb.shiftKey.isPressed ? -1 : 1);

            // Фокус потерян (клик в пустоту, отключённый объект) — возвращаем, чтобы навигация не умирала.
            var current = es.currentSelectedGameObject;
            var selectable = current != null && current.activeInHierarchy ? current.GetComponent<Selectable>() : null;
            if (selectable == null || !ActiveChain.Contains(selectable))
                Select(focused != null && ActiveChain.Contains(focused) ? focused : DefaultSelection());
            else
                focused = selectable;
        }

        List<Selectable> ActiveChain => IsExitDialogOpen ? dialogChain : menuChain;

        Selectable DefaultSelection()
        {
            if (IsExitDialogOpen) return exitCancelButton; // безопасный вариант по умолчанию
            return menuChain.Count > 0 ? menuChain[0] : null;
        }

        /// <summary>Escape / ○ на руле / Cancel геймпада.</summary>
        public void HandleCancel()
        {
            if (IsExitDialogOpen) CancelExit();
            else RequestExit();
        }

        public void RequestExit()
        {
            if (exitDialog == null) return;
            exitDialog.SetActive(true);
            Select(exitCancelButton);
        }

        public void CancelExit()
        {
            if (exitDialog != null) exitDialog.SetActive(false);
            Select(exitButton);
        }

        void ConfirmExit()
        {
            if (exitDialog != null) exitDialog.SetActive(false);
            Raise(OnExitConfirmed, "exit");
        }

        /// <summary>Tab / Shift+Tab: по кругу внутри активной цепочки. Без текущего фокуса — на первый/последний пункт.</summary>
        public void MoveFocus(int direction)
        {
            var chain = ActiveChain;
            if (chain.Count == 0) return;
            int i = focused != null ? chain.IndexOf(focused) : -1;
            int next = i < 0 ? (direction >= 0 ? 0 : chain.Count - 1) : ((i + (direction >= 0 ? 1 : -1)) % chain.Count + chain.Count) % chain.Count;
            Select(chain[next]);
        }

        public void Select(Selectable target)
        {
            if (target == null) return;
            focused = target;
            var es = EventSystem.current;
            if (es != null) { es.SetSelectedGameObject(target.gameObject); return; }
            // Без EventSystem (EditMode) обновляем вид вручную.
            foreach (var view in GetComponentsInChildren<MenuItemView>(true)) view.SetFocused(view.Selectable == target);
        }

        static void Raise(UnityEvent e, string intent)
        {
            Debug.Log($"[MainMenu] intent: {intent}");
            e.Invoke();
        }

        static void Wire(Button b, UnityAction action) { if (b != null) b.onClick.AddListener(action); }

        static void LinkChain(List<Selectable> chain, bool vertical)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                var prev = chain[(i - 1 + chain.Count) % chain.Count];
                var next = chain[(i + 1) % chain.Count];
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                if (vertical) { nav.selectOnUp = prev; nav.selectOnDown = next; }
                else { nav.selectOnLeft = prev; nav.selectOnRight = next; }
                chain[i].navigation = nav;
            }
        }

        static bool CancelPressed(EventSystem es)
        {
            var module = es.currentInputModule as InputSystemUIInputModule;
            var action = module != null && module.cancel != null ? module.cancel.action : null;
            if (action != null) return action.WasPerformedThisFrame();
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
