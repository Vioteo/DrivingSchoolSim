using System;
using System.Collections.Generic;
using System.Linq;

namespace DrivingSchool.Settings
{
    public enum SettingKind { Cycle, Slider, Switch, Action }

    [Flags]
    public enum SettingFlags
    {
        None = 0,
        LockInDrive = 1,        // «только до поездки»: в паузе заблокирован
        NeedsWheel = 2,         // без руля заблокирован
        Stub = 4,               // подсистемы нет: бейдж «скоро», значение сохраняется
        KeyboardOnly = 8,       // только при управлении с клавиатуры
        ConfirmDisplay = 16,    // применяется с окном подтверждения 15 с
        PresetDriven = 32,      // задаётся пресетом «Общее качество»
        Preview = 64,           // виден сразу, без «Применить» откатывается
    }

    /// <summary>Почему пункт сейчас нельзя менять (или можно). Порядок = приоритет бейджа.</summary>
    public enum SettingAvailability { Enabled, NoWheel, AfterDrive, KeyboardOnly, DependsOff, Stub }

    public sealed class SettingItem
    {
        public string Key, Label, Help, Unit = "", ActionText = "Открыть →";
        public SettingKind Kind;
        public string[] Options = new string[0];
        public int Min, Max = 1, Step = 1, Default;
        public SettingFlags Flags;
        public string DisabledWhenOn;   // пункт недоступен, пока включён этот переключатель
        public string ApplyNote;   // «Применение» в справке; пусто — по флагам

        public bool Has(SettingFlags f) => (Flags & f) != 0;
        public bool IsValue => Kind != SettingKind.Action;
        public int Count => Kind == SettingKind.Cycle ? Options.Length : Kind == SettingKind.Switch ? 2 : (Max - Min) / Math.Max(1, Step) + 1;

        public int Clamp(int v)
        {
            switch (Kind)
            {
                case SettingKind.Cycle: return v >= 0 && v < Options.Length ? v : Default;
                case SettingKind.Switch: return v != 0 ? 1 : 0;
                case SettingKind.Slider:
                    if (v < Min) v = Min;
                    if (v > Max) v = Max;
                    int s = Math.Max(1, Step);
                    int snapped = Min + (int)Math.Round((v - Min) / (double)s, MidpointRounding.AwayFromZero) * s;
                    return snapped > Max ? Max : snapped;
                default: return 0;
            }
        }

        /// <summary>Следующее значение по ← →: циклично для списков, с упором для ползунков.</summary>
        public int Next(int v, int dir)
        {
            switch (Kind)
            {
                case SettingKind.Cycle: return Options.Length == 0 ? 0 : ((v + dir) % Options.Length + Options.Length) % Options.Length;
                case SettingKind.Switch: return v != 0 ? 0 : 1;
                case SettingKind.Slider: return Clamp(v + dir * Math.Max(1, Step));
                default: return v;
            }
        }

        public string Format(int v)
        {
            switch (Kind)
            {
                case SettingKind.Cycle: return v >= 0 && v < Options.Length ? Options[v] : "—";
                case SettingKind.Switch: return v != 0 ? "Вкл." : "Выкл.";
                case SettingKind.Slider: return v + Unit;
                default: return ActionText;
            }
        }
    }

    public sealed class SettingGroup
    {
        public string Title;
        public SettingItem[] Items;
    }

    public sealed class SettingTab
    {
        public string Id, Title;
        public SettingGroup[] Groups;
        public IEnumerable<SettingItem> Items => Groups.SelectMany(g => g.Items);
    }

    /// <summary>
    /// Схема экрана настроек: вкладки, группы, пункты, тексты справки — по docs/ui-settings.md §4 и макету
    /// artifacts/visual-review/settings/index.html. Ключи совпадают с полями <see cref="GameSettings"/>.
    /// </summary>
    public static class SettingsSchema
    {
        public const string Resolution = "graphics.resolution";
        public const string QualityPreset = "graphics.qualityPreset";
        public const int CustomPreset = 3;
        public static readonly string[] DefaultResolutions = { "1280 × 720", "1600 × 900", "1920 × 1080", "2560 × 1440" };

        /// <summary>Пресеты «Общего качества»: тени, сглаживание, дальность прорисовки.</summary>
        public static readonly (int shadows, int aa, int draw)[] Presets = { (1, 0, 400), (2, 1, 800), (3, 2, 1200) };

        static SettingItem Cycle(string key, string label, string[] options, int def, string help, SettingFlags flags = SettingFlags.None) =>
            new SettingItem { Key = key, Label = label, Kind = SettingKind.Cycle, Options = options, Max = options.Length - 1, Default = def, Help = help, Flags = flags };
        static SettingItem Slider(string key, string label, int min, int max, int step, string unit, int def, string help, SettingFlags flags = SettingFlags.None) =>
            new SettingItem { Key = key, Label = label, Kind = SettingKind.Slider, Min = min, Max = max, Step = step, Unit = unit, Default = def, Help = help, Flags = flags };
        static SettingItem Switch(string key, string label, bool def, string help, SettingFlags flags = SettingFlags.None) =>
            new SettingItem { Key = key, Label = label, Kind = SettingKind.Switch, Max = 1, Default = def ? 1 : 0, Help = help, Flags = flags };
        static SettingItem Action(string key, string label, string help, SettingFlags flags) =>
            new SettingItem { Key = key, Label = label, Kind = SettingKind.Action, Help = help, Flags = flags };

        public static readonly SettingTab[] Tabs = Build();

        static SettingTab[] Build()
        {
            const SettingFlags wheelStub = SettingFlags.NeedsWheel | SettingFlags.Stub;
            var res = Cycle(Resolution, "Разрешение", DefaultResolutions, 2,
                "Целевое разрешение проекта — 1920 × 1080. Интерфейс проверяется на 1920 × 1080 и 1280 × 720.", SettingFlags.ConfirmDisplay);
            var fps = Cycle("graphics.fpsLimit", "Ограничение FPS", new[] { "30", "60", "120", "Без ограничения" }, 1,
                "Не влияет на физику: шаг симуляции фиксированный и не зависит от FPS.");
            fps.DisabledWhenOn = "graphics.vSync";
            return new[]
            {
                new SettingTab { Id = "graphics", Title = "Графика", Groups = new[]
                {
                    new SettingGroup { Title = "Экран", Items = new[]
                    {
                        Cycle("graphics.displayMode", "Режим экрана", new[] { "Полный экран", "Окно без рамки", "Окно" }, 0,
                            "Полный экран даёт наименьшую задержку. Окно без рамки удобно, если рядом открыт другой монитор.", SettingFlags.ConfirmDisplay),
                        res,
                        Switch("graphics.vSync", "Вертикальная синхронизация", true, "Убирает разрывы кадра. При включении ограничение FPS не действует."),
                        fps,
                    }},
                    new SettingGroup { Title = "Качество", Items = new[]
                    {
                        Cycle(QualityPreset, "Общее качество", new[] { "Низкое", "Среднее", "Высокое", "Своё" }, 2,
                            "Пресет задаёт тени, сглаживание и дальность прорисовки. Если изменить любой из них вручную, пресет станет «Своё»."),
                        Cycle("graphics.mirrorQuality", "Качество зеркал", new[] { "Низкое", "Среднее", "Высокое" }, 1,
                            "Разрешение и частота обновления трёх зеркал заднего вида. Зеркала — главный потребитель GPU в салоне."),
                        Cycle("graphics.shadows", "Тени", new[] { "Выкл.", "Низкие", "Средние", "Высокие" }, 3, "Качество и дальность теней от солнца.", SettingFlags.PresetDriven),
                        Cycle("graphics.antiAliasing", "Сглаживание", new[] { "Выкл.", "FXAA", "SMAA", "MSAA 4×" }, 2,
                            "MSAA даёт самый чистый край, но дороже по производительности.", SettingFlags.PresetDriven),
                        Slider("graphics.drawDistance", "Дальность прорисовки", 200, 1500, 50, " м", 1200,
                            "Дальность отрисовки из машины. На площадке автодрома почти не влияет.", SettingFlags.PresetDriven),
                    }},
                }},
                new SettingTab { Id = "controls", Title = "Управление", Groups = new[]
                {
                    new SettingGroup { Title = "Устройство", Items = new[]
                    {
                        Cycle("controls.device", "Устройство ввода", new[] { "Клавиатура", "Logitech G29" }, 0,
                            "Руль G29 выбирается, только когда он подключён. Управление рулём в поездке подключается в задаче T42; сейчас поездка идёт с клавиатуры.",
                            SettingFlags.LockInDrive | wheelStub),
                        Action("controls.calibrate", "Калибровка руля и педалей", "Отдельный экран: проверка осей, крайних положений педалей и передач H-шифтера.", wheelStub),
                    }},
                    new SettingGroup { Title = "Руль", Items = new[]
                    {
                        Slider("controls.steeringLock", "Угол поворота руля", 180, 900, 10, "°", 900,
                            "У G29 900° — от упора до упора. Угол должен совпадать с настройкой драйвера Logitech, иначе руль и колёса разойдутся.", wheelStub),
                        Slider("controls.steeringDeadzone", "Мёртвая зона руля", 0, 10, 1, " %", 0,
                            "Для исправного G29 не нужна. Увеличивайте, только если машину тянет в сторону при отпущенном руле.", wheelStub),
                        Slider("controls.steeringLinearity", "Линейность", 0, 100, 5, " %", 0, "0 % — отклик линейный. Больше — точнее в центре и резче у краёв.", wheelStub),
                        Slider("controls.keyboardSteerSpeed", "Скорость руления с клавиатуры", 20, 200, 10, " %", 100,
                            "С какой скоростью колёса поворачиваются и возвращаются в центр, пока клавиша нажата.", SettingFlags.KeyboardOnly),
                    }},
                    new SettingGroup { Title = "Педали", Items = new[]
                    {
                        Switch("controls.invertPedals", "Инвертировать педали", false, "Включите, если отпущенная педаль в калибровке показывает 100 %.", wheelStub),
                        Slider("controls.pedalDeadzone", "Мёртвая зона педалей", 0, 15, 1, " %", 3, "Защищает от самопроизвольного газа и тормоза из-за изношенных потенциометров.", wheelStub),
                    }},
                    new SettingGroup { Title = "Обратная связь", Items = new[]
                    {
                        Slider("controls.ffbStrength", "Сила обратной связи (FFB)", 0, 100, 5, " %", 60,
                            "Force feedback пока не реализован. Когда появится, первое включение будет со слабым усилием.", wheelStub),
                        Action("controls.rebind", "Переназначение кнопок", "Пока не реализовано. Раскладка фиксированная (docs/vehicle-test-range.md).", SettingFlags.Stub),
                    }},
                }},
                new SettingTab { Id = "audio", Title = "Звук", Groups = new[]
                {
                    new SettingGroup { Title = "Громкость", Items = new[]
                    {
                        Slider("audio.master", "Общая", 0, 100, 5, " %", 80, "Громкость всех звуков игры."),
                        Slider("audio.engine", "Двигатель", 0, 100, 5, " %", 80, "Звук двигателя помогает трогаться без тахометра — это часть навыка. Звука двигателя пока нет.", SettingFlags.Stub),
                        Slider("audio.environment", "Окружение и трафик", 0, 100, 5, " %", 70, "Другие машины, пешеходы, погода. Раздельной громкости пока нет.", SettingFlags.Stub),
                        Slider("audio.instructor", "Голос инструктора", 0, 100, 5, " %", 100, "Подсказки и замечания инструктора. Голоса пока нет.", SettingFlags.Stub),
                        Slider("audio.ui", "Интерфейс", 0, 100, 5, " %", 60, "Щелчки меню и уведомления. Звуков интерфейса пока нет.", SettingFlags.Stub),
                    }},
                    new SettingGroup { Title = "Поведение", Items = new[]
                    {
                        Switch("audio.muteInBackground", "Без звука в свёрнутом окне", true, "Отключать звук, когда окно игры не в фокусе."),
                    }},
                }},
                new SettingTab { Id = "gameplay", Title = "Обучение", Groups = new[]
                {
                    new SettingGroup { Title = "Автомобиль", Items = new[]
                    {
                        Cycle("gameplay.transmission", "Коробка передач", new[] { "МКПП", "АКПП" }, 0, "Тип коробки учебного автомобиля для новых поездок.", SettingFlags.LockInDrive),
                        Switch("gameplay.antiStall", "Помощник: двигатель не глохнет", false,
                            "Для первых занятий. Включённый помощник будет отмечаться в разборе поездки (ADR-007).", SettingFlags.LockInDrive | SettingFlags.Stub),
                        Switch("gameplay.autoClutch", "Помощник: автосцепление", false,
                            "Сцепление выжимается само при переключении передач. Будет отмечаться в разборе поездки.", SettingFlags.LockInDrive | SettingFlags.Stub),
                    }},
                    new SettingGroup { Title = "Инструктор", Items = new[]
                    {
                        Cycle("gameplay.instructorHints", "Подсказки инструктора", new[] { "Все", "Только ошибки", "Выкл." }, 0,
                            "«Все» — предупреждения перед манёвром. «Только ошибки» — замечания после нарушения и предупреждения об опасности. Нарушения фиксируются всегда."),
                        Switch("gameplay.subtitles", "Субтитры", true, "Текст подсказок внизу экрана.", SettingFlags.Stub),
                    }},
                    new SettingGroup { Title = "Камера и HUD", Items = new[]
                    {
                        Cycle("gameplay.defaultCamera", "Камера при старте", new[] { "Из салона", "Сзади" }, 0,
                            "Основной вид для обучения — из салона: так видны зеркала и приборы. В поездке камеру переключает C."),
                        Slider("gameplay.fov", "Угол обзора (FOV)", 50, 100, 1, "°", 75,
                            "Вертикальный угол обзора из салона. Меньше — ближе к реальной перспективе на одном мониторе."),
                        Cycle("gameplay.hud", "Показывать HUD", new[] { "Полный", "Минимальный", "Выкл." }, 0,
                            "Минимальный HUD — скорость и передача. Подсказки инструктора и замечания видны всегда. Из салона приборы HUD скрыты: их показывает панель машины."),
                    }},
                    new SettingGroup { Title = "Интерфейс", Items = new[]
                    {
                        Cycle("gameplay.uiTheme", "Тема интерфейса", new[] { "Асфальт", "Графит", "Знак", "Бирюза" }, 0,
                            "Цвета меню и HUD. Изменение видно сразу; без «Применить» тема вернётся к прежней.", SettingFlags.Preview),
                        Slider("gameplay.uiScale", "Масштаб интерфейса", 80, 130, 5, " %", 100, "Размер меню и HUD. Полезно на больших мониторах и при слабом зрении."),
                        Cycle("gameplay.language", "Язык интерфейса", new[] { "Русский" }, 0, "Пока доступен только русский."),
                    }},
                }},
            };
        }

        public static IEnumerable<SettingItem> All => Tabs.SelectMany(t => t.Items);
        public static SettingItem Find(string key) => All.FirstOrDefault(i => i.Key == key);

        /// <summary>Список разрешений монитора («1920 × 1080»). Пустой — остаётся список по умолчанию.</summary>
        public static void SetResolutionOptions(IList<string> labels)
        {
            var item = Find(Resolution);
            var list = labels != null && labels.Count > 0 ? labels.Distinct().ToArray() : DefaultResolutions;
            item.Options = list;
            item.Max = list.Length - 1;
            int d = Array.IndexOf(list, "1920 × 1080");
            item.Default = d >= 0 ? d : list.Length - 1;
        }

        public static string ResolutionKey(string label) => label.Replace(" × ", "x").Replace(" ", "");
        public static string ResolutionLabel(string key) => (key ?? "").Replace("x", " × ");

        public static bool TryParseResolution(string key, out int width, out int height)
        {
            width = height = 0;
            var p = (key ?? "").Split('x');
            return p.Length == 2 && int.TryParse(p[0], out width) && int.TryParse(p[1], out height) && width >= 320 && height >= 240 && width <= 16384 && height <= 16384;
        }

        static int B(bool b) => b ? 1 : 0;

        /// <summary>Значение пункта как int (индекс списка, 0/1 переключателя, число ползунка). Ручное сопоставление ключей (ADR-016).</summary>
        public static int Get(GameSettings s, string key)
        {
            var g = s.graphics; var c = s.controls; var a = s.audio; var p = s.gameplay;
            switch (key)
            {
                case "graphics.displayMode": return g.displayMode;
                case Resolution:
                {
                    var item = Find(Resolution);
                    int i = Array.IndexOf(item.Options, ResolutionLabel(g.resolution));
                    return i >= 0 ? i : item.Default;
                }
                case "graphics.vSync": return B(g.vSync);
                case "graphics.fpsLimit": return g.fpsLimit;
                case QualityPreset: return g.qualityPreset;
                case "graphics.mirrorQuality": return g.mirrorQuality;
                case "graphics.shadows": return g.shadows;
                case "graphics.antiAliasing": return g.antiAliasing;
                case "graphics.drawDistance": return g.drawDistance;
                case "controls.device": return c.device;
                case "controls.steeringLock": return c.steeringLock;
                case "controls.steeringDeadzone": return c.steeringDeadzone;
                case "controls.steeringLinearity": return c.steeringLinearity;
                case "controls.keyboardSteerSpeed": return c.keyboardSteerSpeed;
                case "controls.invertPedals": return B(c.invertPedals);
                case "controls.pedalDeadzone": return c.pedalDeadzone;
                case "controls.ffbStrength": return c.ffbStrength;
                case "audio.master": return a.master;
                case "audio.engine": return a.engine;
                case "audio.environment": return a.environment;
                case "audio.instructor": return a.instructor;
                case "audio.ui": return a.ui;
                case "audio.muteInBackground": return B(a.muteInBackground);
                case "gameplay.transmission": return p.transmission;
                case "gameplay.antiStall": return B(p.antiStall);
                case "gameplay.autoClutch": return B(p.autoClutch);
                case "gameplay.instructorHints": return p.instructorHints;
                case "gameplay.subtitles": return B(p.subtitles);
                case "gameplay.defaultCamera": return p.defaultCamera;
                case "gameplay.fov": return p.fov;
                case "gameplay.hud": return p.hud;
                case "gameplay.uiTheme": return p.uiTheme;
                case "gameplay.uiScale": return p.uiScale;
                case "gameplay.language": return p.language;
                default: return 0; // действия (калибровка, переназначение) значения не имеют
            }
        }

        public static void Set(GameSettings s, string key, int v)
        {
            var g = s.graphics; var c = s.controls; var a = s.audio; var p = s.gameplay;
            bool b = v != 0;
            switch (key)
            {
                case "graphics.displayMode": g.displayMode = v; break;
                case Resolution:
                {
                    var item = Find(Resolution);
                    if (v >= 0 && v < item.Options.Length) g.resolution = ResolutionKey(item.Options[v]);
                    break;
                }
                case "graphics.vSync": g.vSync = b; break;
                case "graphics.fpsLimit": g.fpsLimit = v; break;
                case QualityPreset: g.qualityPreset = v; break;
                case "graphics.mirrorQuality": g.mirrorQuality = v; break;
                case "graphics.shadows": g.shadows = v; break;
                case "graphics.antiAliasing": g.antiAliasing = v; break;
                case "graphics.drawDistance": g.drawDistance = v; break;
                case "controls.device": c.device = v; break;
                case "controls.steeringLock": c.steeringLock = v; break;
                case "controls.steeringDeadzone": c.steeringDeadzone = v; break;
                case "controls.steeringLinearity": c.steeringLinearity = v; break;
                case "controls.keyboardSteerSpeed": c.keyboardSteerSpeed = v; break;
                case "controls.invertPedals": c.invertPedals = b; break;
                case "controls.pedalDeadzone": c.pedalDeadzone = v; break;
                case "controls.ffbStrength": c.ffbStrength = v; break;
                case "audio.master": a.master = v; break;
                case "audio.engine": a.engine = v; break;
                case "audio.environment": a.environment = v; break;
                case "audio.instructor": a.instructor = v; break;
                case "audio.ui": a.ui = v; break;
                case "audio.muteInBackground": a.muteInBackground = b; break;
                case "gameplay.transmission": p.transmission = v; break;
                case "gameplay.antiStall": p.antiStall = b; break;
                case "gameplay.autoClutch": p.autoClutch = b; break;
                case "gameplay.instructorHints": p.instructorHints = v; break;
                case "gameplay.subtitles": p.subtitles = b; break;
                case "gameplay.defaultCamera": p.defaultCamera = v; break;
                case "gameplay.fov": p.fov = v; break;
                case "gameplay.hud": p.hud = v; break;
                case "gameplay.uiTheme": p.uiTheme = v; break;
                case "gameplay.uiScale": p.uiScale = v; break;
                case "gameplay.language": p.language = v; break;
            }
        }

        /// <summary>Какой пресет соответствует текущим теням/сглаживанию/дальности; «Своё», если ни один.</summary>
        public static int MatchPreset(GameSettings s)
        {
            for (int i = 0; i < Presets.Length; i++)
                if (Presets[i].shadows == s.graphics.shadows && Presets[i].aa == s.graphics.antiAliasing && Presets[i].draw == s.graphics.drawDistance) return i;
            return CustomPreset;
        }
    }
}
