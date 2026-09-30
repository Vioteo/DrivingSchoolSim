using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Audio;
using DrivingSchool.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Экран «Задания» (T70, раньше — T53/T68). Разделы вкладками: «Уроки», «Площадка», «Экзамены», «Город»; в разделе —
    /// список заданий, у каждого — отметка прогресса из профиля ученика (<see cref="ProfileService"/>). Все упражнения площадки
    /// видны списком (из guided-lessons.json, уроки с courseLesson).
    ///
    /// Управление одинаково с клавиатуры, геймпада и руля G29 (<see cref="MenuInput"/>): вверх/вниз (стрелки, крестовина) — задание,
    /// влево/вправо и лепестки (Q / E) — раздел, ✕ / Enter — начать выбранное, ○ / Esc — назад. Мышь: наведение и щелчок по
    /// строке — выбрать, «Начать задание» — запустить. Своё выделение (как в настройках): выделение EventSystem снимается каждый
    /// кадр, иначе Enter сработал бы дважды — Submit модуля ввода и наш.
    /// </summary>
    public sealed class LessonCatalogController : MonoBehaviour
    {
        public const string FreeDrive = "free-drive";
        public const string FreeDriveTitle = "Город";
        public const string AutodromeExamTitle = "Экзамен на площадке";
        public static readonly string[] SectionTitles = { "Уроки", "Площадка", "Экзамены", "Город" };
        public const int Lessons = 0, Autodrome = 1, Exams = 2, City = 3;

        [Tooltip("Строка списка: неактивный образец (Button + MenuItemView, дочерние Status и Dot)")] public Button rowTemplate;
        [Tooltip("Содержимое списка внутри окна с маской")] public RectTransform listContent;
        public RectTransform listViewport;
        public Button[] sectionButtons = Array.Empty<Button>();
        public TMP_Text[] sectionLabels = Array.Empty<TMP_Text>();
        public Button startButton, backButton;
        public TMP_Text title, description, mode, progress, overall, hint;
        public float rowHeight = 64f, rowGap = 8f;
        public event Action<string> LaunchRequested;

        public sealed class Item
        {
            public string id, title, mode, description;
            public bool tracked;       // прогресс ведётся (урок, упражнение, экзамен); город — нет
        }

        sealed class Row { public Item item; public Button button; public MenuItemView view; public TMP_Text status; public Image dot; }

        public int SectionIndex { get; private set; }
        public int ItemIndex { get; private set; }
        public Item SelectedItem => SectionItems(SectionIndex).Count > 0 ? SectionItems(SectionIndex)[ItemIndex] : null;
        public string SelectedId => SelectedItem?.id;
        public int ExerciseCount => sections[Autodrome].Count;
        public IReadOnlyList<Item> SectionItems(int section) => section >= 0 && section < sections.Length ? sections[section] : (IReadOnlyList<Item>)Array.Empty<Item>();
        public static bool ClosedThisFrame => closedFrame == Time.frameCount;
        static int closedFrame = -1;
        static int rememberedSection, rememberedItem;

        readonly List<Item>[] sections = { new List<Item>(), new List<Item>(), new List<Item>(), new List<Item>() };
        readonly List<Row> rows = new List<Row>();
        readonly DirectionRepeater repeater = new DirectionRepeater();
        Action onBack;
        bool initialized, wheel;
        float wheelCheckAt;

        [Serializable] sealed class PackLesson { public string id, title, briefing, courseLesson; }
        [Serializable] sealed class Pack { public PackLesson[] lessons; }

        void Awake() { Initialize(); }
        void OnDestroy() { ProfileService.Changed -= OnProfileChanged; }

        // Новая попытка записана (из поездки или тестом) — отметки обновляются. Подписка в Initialize: в EditMode OnEnable не зовётся.
        void OnProfileChanged(PlayerProfile _)
        {
            if (this == null) { ProfileService.Changed -= OnProfileChanged; return; }
            if (initialized) RefreshProgress();
        }

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            ProfileService.Changed += OnProfileChanged;
            BuildItems();
            for (int i = 0; i < sectionButtons.Length; i++)
            {
                int k = i;
                if (sectionButtons[i] == null) continue;
                sectionButtons[i].onClick.AddListener(() => ShowSection(k));
                sectionButtons[i].navigation = new Navigation { mode = Navigation.Mode.None };
            }
            if (startButton != null) { startButton.onClick.AddListener(Launch); startButton.navigation = new Navigation { mode = Navigation.Mode.None }; }
            if (backButton != null) { backButton.onClick.AddListener(Back); backButton.navigation = new Navigation { mode = Navigation.Mode.None }; }
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            SectionIndex = Mathf.Clamp(rememberedSection, 0, sections.Length - 1);
            ShowSection(SectionIndex, rememberedItem);
        }

        void BuildItems()
        {
            foreach (var s in sections) s.Clear();
            sections[Lessons].Add(new Item
            {
                id = LessonLaunch.FirstLesson, title = "Начало движения", mode = "УРОК 01 · С ПОШАГОВЫМИ ПОДСКАЗКАМИ", tracked = true,
                description = "Подготовьте автомобиль, запустите двигатель и плавно троньтесь. Пройдите маршрут по учебной улице и завершите поездку остановкой.\n\n" +
                              "Подсказки ведут по шагам. Доступны механическая и автоматическая коробки передач.",
            });
            var asset = Resources.Load<TextAsset>("guided-lessons");
            PackLesson[] exercises = Array.Empty<PackLesson>();
            if (asset != null)
            {
                try
                {
                    var pack = JsonUtility.FromJson<Pack>(asset.text);
                    if (pack?.lessons != null) exercises = pack.lessons.Where(l => l != null && !string.IsNullOrEmpty(l.courseLesson) && !string.IsNullOrEmpty(l.id)).ToArray();
                }
                catch (ArgumentException e) { Debug.LogWarning("[Catalog] упражнения площадки не прочитаны: " + e.Message); }
            }
            for (int i = 0; i < exercises.Length; i++)
            {
                var e = exercises[i];
                sections[Autodrome].Add(new Item
                {
                    id = e.id, title = e.title, tracked = true,
                    mode = $"УПРАЖНЕНИЕ НА ПЛОЩАДКЕ · {i + 1} ИЗ {exercises.Length} · С ПОДСКАЗКАМИ",
                    description = (string.IsNullOrEmpty(e.briefing) ? "" : e.briefing + "\n\n") +
                                  "Машина стоит у упражнения с заглушённым двигателем. Подсказки ведут по шагам: подготовка, въезд, манёвр, выезд. " +
                                  "Ошибки — игровые баллы площадки: с 5 баллов упражнение не зачтено.",
                });
            }
            sections[Exams].Add(new Item
            {
                id = LessonLaunch.AutodromeExam, title = AutodromeExamTitle, mode = "ПЛОЩАДКА · ВСЕ УПРАЖНЕНИЯ ПОДРЯД · БЕЗ ПОДСКАЗОК", tracked = true,
                description = "Все упражнения автодрома по маршруту — от старта до финиша. Между упражнениями едете сами, без перемещений: рамка на земле показывает путь.\n\n" +
                              "Ошибки — игровые баллы (касание конуса, стоп-линия, указатель поворота, заглохание, откат на эстакаде, красный свет…); с 5 баллов экзамен не сдан. В конце — разбор ошибок.",
            });
            sections[City].Add(new Item
            {
                id = FreeDrive, title = FreeDriveTitle, mode = "СВОБОДНАЯ ПРАКТИКА В ГОРОДЕ", tracked = false,
                description = "Поездка начинается в городе: перекрёстки со светофорами и знаками приоритета, полосы с направлениями движения, кольцо, переезд, пешеходный переход с лежачими полицейскими и разные ограничения скорости. Боты и пешеходы соблюдают ПДД.\n\n" +
                              "Инструктор отмечает нарушения: скорость, выезд на встречную, поворотники, полосу для поворота, дистанцию и остановки. F9 — на площадку полигона.",
            });
        }

        // ------------------------------------------------------------------ открытие / закрытие

        public void Open(Action back)
        {
            Initialize();
            onBack = back;
            gameObject.SetActive(true);
            ShowSection(SectionIndex, ItemIndex);
            UpdateHint(true);
        }

        public void Back()
        {
            closedFrame = Time.frameCount;
            gameObject.SetActive(false);
            onBack?.Invoke();
        }

        // ------------------------------------------------------------------ выбор

        /// <summary>Выбрать задание по id (раздел — тот, где оно есть). Неизвестный id — исключение, выбор не меняется.</summary>
        public void SelectAssignment(string id)
        {
            for (int s = 0; s < sections.Length; s++)
            {
                int i = sections[s].FindIndex(x => x.id == id);
                if (i < 0) continue;
                ShowSection(s, i);
                return;
            }
            throw new ArgumentException("Неизвестное задание: " + id, nameof(id));
        }

        /// <summary>Следующий (+1) или предыдущий (−1) раздел по кругу; в новом разделе выбрано последнее выбранное там задание.</summary>
        public void ShiftSection(int delta)
        {
            int n = sections.Length;
            ShowSection(((SectionIndex + delta) % n + n) % n);
        }

        readonly int[] lastItemOf = new int[4];

        public void ShowSection(int section) => ShowSection(section, lastItemOf[Mathf.Clamp(section, 0, 3)]);

        public void ShowSection(int section, int item)
        {
            section = Mathf.Clamp(section, 0, sections.Length - 1);
            bool rebuild = section != SectionIndex || rows.Count != sections[section].Count || rows.Count == 0;
            SectionIndex = section;
            rememberedSection = section;
            if (rebuild) BuildRows();
            SelectItem(item);
            for (int i = 0; i < sectionButtons.Length; i++)
            {
                var v = sectionButtons[i] != null ? sectionButtons[i].GetComponent<MenuItemView>() : null;
                if (v != null) v.SetFocused(i == SectionIndex);
            }
        }

        /// <summary>Выбрать задание в текущем разделе (индекс ограничивается списком).</summary>
        public void SelectItem(int index)
        {
            var list = sections[SectionIndex];
            ItemIndex = list.Count == 0 ? 0 : Mathf.Clamp(index, 0, list.Count - 1);
            lastItemOf[SectionIndex] = ItemIndex;
            rememberedItem = ItemIndex;
            ApplyFocus();
            ShowDetails();
            ScrollToFocus();
        }

        /// <summary>Вверх (−1) / вниз (+1) по списку раздела, по кругу.</summary>
        public void MoveFocus(int direction)
        {
            int n = sections[SectionIndex].Count;
            if (n == 0) return;
            SelectItem(((ItemIndex + direction) % n + n) % n);
        }

        /// <summary>Запустить выбранное задание (✕ / Enter на строке, кнопка «Начать задание»).</summary>
        public void Launch()
        {
            var item = SelectedItem;
            if (item == null) return;
            LaunchRequested?.Invoke(item.id);
        }

        // ------------------------------------------------------------------ строки и прогресс

        void BuildRows()
        {
            foreach (var r in rows) if (r.button != null) DestroyImmediateSafe(r.button.gameObject);
            rows.Clear();
            if (rowTemplate == null || listContent == null) return;
            var list = sections[SectionIndex];
            for (int i = 0; i < list.Count; i++)
            {
                var b = Instantiate(rowTemplate, listContent);
                b.gameObject.name = "Row_" + list[i].id;
                b.gameObject.SetActive(true);
                var rt = (RectTransform)b.transform;
                rt.anchoredPosition = new Vector2(0f, -i * (rowHeight + rowGap));
                var row = new Row
                {
                    item = list[i], button = b, view = b.GetComponent<MenuItemView>(),
                    status = b.transform.Find("Status")?.GetComponent<TMP_Text>(),
                    dot = b.transform.Find("Dot")?.GetComponent<Image>(),
                };
                if (row.view != null && row.view.label != null) row.view.label.text = list[i].title;
                b.navigation = new Navigation { mode = Navigation.Mode.None };
                int k = i;
                b.onClick.AddListener(() => SelectItem(k));
                if (row.view != null) row.view.Focused += _ => { if (ItemIndex != k) SelectItem(k); };
                rows.Add(row);
            }
            listContent.sizeDelta = new Vector2(listContent.sizeDelta.x, Mathf.Max(0f, list.Count * (rowHeight + rowGap) - rowGap));
            RefreshProgress();
        }

        static void DestroyImmediateSafe(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        void ApplyFocus()
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].view != null) rows[i].view.SetFocused(i == ItemIndex);
        }

        void ScrollToFocus()
        {
            if (listContent == null || listViewport == null || rows.Count == 0) return;
            float step = rowHeight + rowGap, view = listViewport.rect.height;
            float top = ItemIndex * step, bottom = top + rowHeight;
            float y = listContent.anchoredPosition.y;
            if (top < y) y = top;
            else if (bottom > y + view) y = bottom - view;
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, listContent.sizeDelta.y - view));
            listContent.anchoredPosition = new Vector2(listContent.anchoredPosition.x, y);
        }

        /// <summary>Короткая отметка строки: «сдано · 0 б.», «не сдано», «не начато»; для города — пусто.</summary>
        public static string StatusText(AssignmentRecord r, bool tracked)
        {
            if (!tracked) return "";
            if (r == null || r.attempts == 0) return "не начато";
            return r.Passed ? $"сдано · {r.bestPenalty} б." : "не сдано";
        }

        /// <summary>Строки прогресса в описании задания.</summary>
        public static string ProgressText(AssignmentRecord r, bool tracked)
        {
            if (!tracked) return "Свободная практика: прогресс не ведётся.";
            if (r == null || r.attempts == 0) return "Ещё не выполнялось.";
            string s = $"Попыток: {r.attempts} · зачётов: {r.passes}";
            if (r.Passed)
            {
                int t = Mathf.RoundToInt(r.bestSeconds);
                s += $"\nЛучший зачёт: {r.bestPenalty} б. · {t / 60}:{t % 60:00}";
            }
            s += "\nПоследняя попытка: " + (r.lastPassed ? "зачёт" : "незачёт");
            return s;
        }

        /// <summary>«Пройдено k из n» по разделу (только задания с прогрессом).</summary>
        public int PassedIn(int section, out int total)
        {
            var p = ProfileService.Current;
            var list = SectionItems(section).Where(x => x.tracked).ToList();
            total = list.Count;
            return list.Count(x => p.Progress(x.id)?.Passed == true);
        }

        void RefreshProgress()
        {
            var p = ProfileService.Current;
            var theme = UIThemeState.Current != null ? UIThemeState.Current : rowTemplate != null ? rowTemplate.GetComponent<MenuItemView>()?.fallbackTheme : null;
            foreach (var r in rows)
            {
                var rec = p.Progress(r.item.id);
                if (r.status != null) r.status.text = StatusText(rec, r.item.tracked);
                if (r.dot != null)
                {
                    r.dot.enabled = r.item.tracked;
                    if (theme != null) r.dot.color = rec == null || rec.attempts == 0 ? theme.line : rec.Passed ? theme.green : theme.red;
                }
            }
            int done = 0, all = 0;
            for (int s = 0; s < sections.Length; s++)
            {
                int k = PassedIn(s, out int n);
                done += k; all += n;
                if (s < sectionLabels.Length && sectionLabels[s] != null)
                    sectionLabels[s].text = n > 0 ? $"{SectionTitles[s]}  {k}/{n}" : SectionTitles[s];
            }
            if (overall != null) overall.text = $"Пройдено {done} из {all}";
            ShowDetails();
        }

        void ShowDetails()
        {
            var item = SelectedItem;
            if (item == null) return;
            if (title != null) title.text = item.title;
            if (mode != null) mode.text = item.mode;
            if (description != null) description.text = item.description;
            if (progress != null) progress.text = ProgressText(ProfileService.Current.Progress(item.id), item.tracked);
        }

        void UpdateHint(bool force)
        {
            bool w = WheelDetector.IsConnected;
            if (!force && w == wheel) return;
            wheel = w;
            if (hint == null) return;
            hint.text = wheel
                ? "Крестовина ↑↓ — задание    ←→ / лепестки — раздел    Крестик — начать    Кружок — назад"
                : "↑↓ — задание    ←→ / Q E — раздел    Enter — начать    Esc — назад";
        }

        // ------------------------------------------------------------------ ввод

        void Update()
        {
            if (Time.unscaledTime > wheelCheckAt) { wheelCheckAt = Time.unscaledTime + 1f; UpdateHint(false); }
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && !(Mouse.current != null && Mouse.current.leftButton.isPressed))
                es.SetSelectedGameObject(null);
            ApplyFocus();   // снятие выделения EventSystem гасит вид строки — вернуть

            if (MenuInput.Cancel) { UISound.Play(SoundClips.Ui.Back); Back(); return; }
            if (MenuInput.Submit) { UISound.Play(SoundClips.Ui.Confirm); Launch(); return; }
            if (MenuInput.TabPrev) { UISound.Play(SoundClips.Ui.Tab); ShiftSection(-1); return; }
            if (MenuInput.TabNext) { UISound.Play(SoundClips.Ui.Tab); ShiftSection(1); return; }
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame) { UISound.Play(SoundClips.Ui.Tab); ShiftSection(kb.shiftKey.isPressed ? -1 : 1); return; }
            var d = repeater.Next(MenuInput.HeldDirection);
            if (d.y != 0) { UISound.Play(SoundClips.Ui.Move); MoveFocus(-d.y); }
            else if (d.x != 0) { UISound.Play(SoundClips.Ui.Tab); ShiftSection(d.x); }
        }
    }
}
