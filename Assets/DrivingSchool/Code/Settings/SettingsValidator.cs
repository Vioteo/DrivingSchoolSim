using System.Collections.Generic;

namespace DrivingSchool.Settings
{
    /// <summary>
    /// Приводит настройки к допустимым значениям (ADR-016): недостающие разделы — по умолчанию, числа — в диапазон
    /// и на шаг ползунка, неизвестные индексы списков — по умолчанию. Возвращает список исправленных ключей для лога.
    /// </summary>
    public static class SettingsValidator
    {
        public static GameSettings Sanitize(GameSettings s, List<string> fixedKeys = null)
        {
            if (s == null) { fixedKeys?.Add("*"); return new GameSettings(); }
            if (s.graphics == null) { s.graphics = new GraphicsSection(); fixedKeys?.Add("graphics"); }
            if (s.controls == null) { s.controls = new ControlsSection(); fixedKeys?.Add("controls"); }
            if (s.audio == null) { s.audio = new AudioSection(); fixedKeys?.Add("audio"); }
            if (s.gameplay == null) { s.gameplay = new GameplaySection(); fixedKeys?.Add("gameplay"); }

            // Разрешение хранится строкой «1920x1080» и не обязано быть в списке монитора (файл мог прийти с другого ПК).
            if (!SettingsSchema.TryParseResolution(s.graphics.resolution, out _, out _))
            {
                s.graphics.resolution = new GraphicsSection().resolution;
                fixedKeys?.Add(SettingsSchema.Resolution);
            }
            foreach (var item in SettingsSchema.All)
            {
                if (!item.IsValue || item.Key == SettingsSchema.Resolution) continue;
                int v = SettingsSchema.Get(s, item.Key);
                int c = item.Clamp(v);
                if (c != v) { SettingsSchema.Set(s, item.Key, c); fixedKeys?.Add(item.Key); }
            }
            return s;
        }
    }

    /// <summary>Цепочка миграций версий файла v → v+1. Файл новее игры читается как есть (незнакомые поля JsonUtility пропускает).</summary>
    public static class SettingsMigrator
    {
        public static GameSettings Migrate(GameSettings s, out bool fromNewerVersion)
        {
            fromNewerVersion = false;
            if (s == null) return new GameSettings();
            if (s.version > GameSettings.CurrentVersion) { fromNewerVersion = true; return s; }
            if (s.version < 1) s.version = 1;   // v0 — файл без поля version: поля те же
            // Пример на будущее: if (s.version == 1) { …перенос полей…; s.version = 2; }
            s.version = GameSettings.CurrentVersion;
            return s;
        }
    }
}
