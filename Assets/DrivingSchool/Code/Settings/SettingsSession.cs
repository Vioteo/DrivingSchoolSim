using System;
using System.Collections.Generic;
using System.Linq;

namespace DrivingSchool.Settings
{
    /// <summary>
    /// Черновик экрана настроек (docs/ui-settings.md §3): правки идут в черновик; «Применить» фиксирует,
    /// «Отменить» возвращает сохранённое, «По умолчанию» сбрасывает вкладку в черновик. Пресет качества
    /// задаёт зависимые пункты; ручная правка зависимого пункта переводит пресет в «Своё».
    /// </summary>
    public sealed class SettingsSession
    {
        public GameSettings Saved { get; private set; }
        public GameSettings Draft { get; private set; }

        public SettingsSession(GameSettings saved)
        {
            Saved = SettingsValidator.Sanitize((saved ?? new GameSettings()).Clone());
            Draft = Saved.Clone();
        }

        public int Get(string key) => SettingsSchema.Get(Draft, key);
        public int GetSaved(string key) => SettingsSchema.Get(Saved, key);

        public void Set(string key, int value)
        {
            var item = SettingsSchema.Find(key);
            if (item == null || !item.IsValue) return;
            value = item.Clamp(value);
            SettingsSchema.Set(Draft, key, value);
            if (key == SettingsSchema.QualityPreset)
            {
                if (value < SettingsSchema.Presets.Length)
                {
                    var p = SettingsSchema.Presets[value];
                    Draft.graphics.shadows = p.shadows; Draft.graphics.antiAliasing = p.aa; Draft.graphics.drawDistance = p.draw;
                }
            }
            else if (item.Has(SettingFlags.PresetDriven))
                Draft.graphics.qualityPreset = SettingsSchema.MatchPreset(Draft);
        }

        public bool IsChanged(string key)
        {
            if (key == SettingsSchema.Resolution) return Draft.graphics.resolution != Saved.graphics.resolution;
            var item = SettingsSchema.Find(key);
            return item != null && item.IsValue && Get(key) != GetSaved(key);
        }

        public IEnumerable<string> ChangedKeys => SettingsSchema.All.Where(i => i.IsValue && IsChanged(i.Key)).Select(i => i.Key);
        public int ChangedCount => ChangedKeys.Count();
        public bool IsDirty => ChangedCount > 0;

        /// <summary>Сброс одного пункта в значение по умолчанию (R).</summary>
        public void ResetItem(string key)
        {
            var item = SettingsSchema.Find(key);
            if (item != null && item.IsValue) Set(key, item.Default);
        }

        /// <summary>Сброс вкладки в черновик. <paramref name="canChange"/> отсекает заблокированные пункты (в паузе).</summary>
        public void ResetTab(SettingTab tab, Func<SettingItem, bool> canChange = null)
        {
            // Пресет первым: иначе он перезапишет зависимые пункты после их сброса.
            foreach (var item in tab.Items.Where(i => i.IsValue).OrderBy(i => i.Key == SettingsSchema.QualityPreset ? 0 : 1))
                if (canChange == null || canChange(item)) Set(item.Key, item.Default);
        }

        public void Revert() => Draft = Saved.Clone();

        /// <summary>Фиксирует черновик. Возвращает изменённые ключи.</summary>
        public List<string> Commit()
        {
            var changed = ChangedKeys.ToList();
            Saved = Draft.Clone();
            return changed;
        }

        /// <summary>Откат только режима экрана и разрешения — после «Вернуть прежний» или истечения 15 с.</summary>
        public void RestoreDisplay(int displayMode, string resolution)
        {
            Saved.graphics.displayMode = Draft.graphics.displayMode = displayMode;
            Saved.graphics.resolution = Draft.graphics.resolution = resolution;
        }

        public static SettingAvailability Availability(SettingItem item, GameSettings draft, bool inDrive, bool wheelConnected)
        {
            if (item.Has(SettingFlags.NeedsWheel) && !wheelConnected) return SettingAvailability.NoWheel;
            if (item.Has(SettingFlags.LockInDrive) && inDrive) return SettingAvailability.AfterDrive;
            if (item.Has(SettingFlags.KeyboardOnly) && draft.controls.device != 0) return SettingAvailability.KeyboardOnly;
            if (!string.IsNullOrEmpty(item.DisabledWhenOn) && SettingsSchema.Get(draft, item.DisabledWhenOn) != 0) return SettingAvailability.DependsOff;
            if (item.Has(SettingFlags.Stub)) return SettingAvailability.Stub;
            return SettingAvailability.Enabled;
        }

        /// <summary>Можно ли менять значение: заглушки меняются (значение сохраняется заранее), заблокированные — нет.</summary>
        public static bool CanChange(SettingAvailability a) => a == SettingAvailability.Enabled || a == SettingAvailability.Stub;
    }

    /// <summary>Применённые настройки процесса. Адаптеры (экран, камеры, ввод) подписываются на <see cref="Applied"/>.</summary>
    public static class SettingsService
    {
        static GameSettings current;
        public static GameSettings Current => current ?? (current = new GameSettings());
        public static bool IsLoaded => current != null;
        public static event Action<GameSettings> Applied;

        public static void Publish(GameSettings settings)
        {
            current = SettingsValidator.Sanitize((settings ?? new GameSettings()).Clone());
            Applied?.Invoke(current);
        }
    }
}
