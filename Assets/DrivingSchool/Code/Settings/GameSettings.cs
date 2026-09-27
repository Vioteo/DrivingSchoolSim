using System;

namespace DrivingSchool.Settings
{
    // Настройки игрока (ADR-016, docs/ui-settings.md §4). Чистый C#: JsonUtility сериализует поля [Serializable]-классов.
    // Имена полей = вторая часть ключа из §4 (graphics.vSync → graphics.vSync). Значения по умолчанию — из §4.

    [Serializable]
    public sealed class GraphicsSection
    {
        public int displayMode = 0;               // Полный экран / Окно без рамки / Окно
        public string resolution = "1920x1080";
        public bool vSync = true;
        public int fpsLimit = 1;                  // 30 / 60 / 120 / без ограничения
        public int qualityPreset = 2;             // Низкое / Среднее / Высокое / Своё
        public int mirrorQuality = 1;             // Низкое / Среднее / Высокое
        public int shadows = 3;                   // Выкл / Низкие / Средние / Высокие
        public int antiAliasing = 2;              // Выкл / FXAA / SMAA / MSAA 4×
        public int drawDistance = 1200;           // м
        public GraphicsSection Clone() => (GraphicsSection)MemberwiseClone();
    }

    [Serializable]
    public sealed class ControlsSection
    {
        public int device = 0;                    // Клавиатура / Logitech G29
        public int steeringLock = 900;
        public int steeringDeadzone = 0;
        public int steeringLinearity = 0;
        public int keyboardSteerSpeed = 100;
        public bool invertPedals = false;
        public int pedalDeadzone = 3;
        public int ffbStrength = 60;
        public ControlsSection Clone() => (ControlsSection)MemberwiseClone();
    }

    [Serializable]
    public sealed class AudioSection
    {
        public int master = 80;
        public int engine = 80;
        public int environment = 70;
        public int instructor = 100;
        public int ui = 60;
        public bool muteInBackground = true;
        public AudioSection Clone() => (AudioSection)MemberwiseClone();
    }

    [Serializable]
    public sealed class GameplaySection
    {
        public int transmission = 0;              // МКПП / АКПП
        public bool antiStall = false;
        public bool autoClutch = false;
        public int instructorHints = 0;           // Все / Только ошибки / Выкл
        public bool subtitles = true;
        public int defaultCamera = 0;             // Из салона / Сзади
        public int fov = 75;
        public int hud = 0;                       // Полный / Минимальный / Выкл
        public int uiTheme = 0;                   // Асфальт / Графит / Знак / Бирюза
        public int uiScale = 100;
        public int language = 0;                  // Русский
        public GameplaySection Clone() => (GameplaySection)MemberwiseClone();
    }

    [Serializable]
    public sealed class GameSettings
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public GraphicsSection graphics = new GraphicsSection();
        public ControlsSection controls = new ControlsSection();
        public AudioSection audio = new AudioSection();
        public GameplaySection gameplay = new GameplaySection();

        public GameSettings Clone() => new GameSettings
        {
            version = version,
            graphics = (graphics ?? new GraphicsSection()).Clone(),
            controls = (controls ?? new ControlsSection()).Clone(),
            audio = (audio ?? new AudioSection()).Clone(),
            gameplay = (gameplay ?? new GameplaySection()).Clone(),
        };
    }
}
