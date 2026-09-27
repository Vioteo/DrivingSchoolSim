using System;
using System.Collections.Generic;
using System.IO;
using DrivingSchool.Settings;
using UnityEngine;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Файл настроек (ADR-016, docs/ui-settings.md §5): Application.persistentDataPath/settings.json, JsonUtility.
    /// Отсутствующие поля берут значения по умолчанию, неизвестные игнорируются, значения вне диапазона обрезаются.
    /// Битый файл → значения по умолчанию и предупреждение; игра не падает. Запись — через временный файл и замену.
    /// </summary>
    public static class SettingsStore
    {
        static string overridePath;

        /// <summary>Путь к файлу. Тесты подменяют его на временный.</summary>
        public static string FilePath
        {
            get => overridePath ?? Path.Combine(Application.persistentDataPath, "settings.json");
            set => overridePath = value;
        }

        public static GameSettings Load() => Load(out _);

        public static GameSettings Load(out string warning)
        {
            warning = null;
            string path = FilePath;
            if (!File.Exists(path)) return new GameSettings();
            GameSettings s;
            try
            {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] != '{') throw new FormatException("не JSON-объект");
                s = new GameSettings();
                JsonUtility.FromJsonOverwrite(text, s);
            }
            catch (Exception e)
            {
                warning = $"Файл настроек повреждён ({e.Message}) — используются значения по умолчанию: {path}";
                Debug.LogWarning("[Settings] " + warning);
                return new GameSettings();
            }
            s = SettingsMigrator.Migrate(s, out bool newer);
            if (newer) Debug.LogWarning($"[Settings] Файл настроек новее игры (version {s.version}) — читаются знакомые поля");
            var fixedKeys = new List<string>();
            s = SettingsValidator.Sanitize(s, fixedKeys);
            if (fixedKeys.Count > 0)
            {
                warning = "Исправлены значения вне диапазона: " + string.Join(", ", fixedKeys);
                Debug.LogWarning("[Settings] " + warning);
            }
            return s;
        }

        public static void Save(GameSettings settings)
        {
            string path = FilePath, tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, JsonUtility.ToJson(settings, true));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Settings] Не удалось сохранить {path}: {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ничего */ }
            }
        }
    }
}
