using System;
using System.IO;
using DrivingSchool.Settings;
using UnityEngine;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>
    /// Файл профиля ученика (T65): Application.persistentDataPath/profile.json, JsonUtility — как у настроек (SettingsStore):
    /// нет файла или он битый → профиль по умолчанию и предупреждение; запись через временный файл и замену.
    /// </summary>
    public static class ProfileStore
    {
        static string overridePath;

        /// <summary>Путь к файлу. Тесты подменяют его на временный.</summary>
        public static string FilePath
        {
            get => overridePath ?? Path.Combine(Application.persistentDataPath, "profile.json");
            set => overridePath = value;
        }

        public static PlayerProfile Load()
        {
            string path = FilePath;
            if (!File.Exists(path)) return new PlayerProfile();
            try
            {
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] != '{') throw new FormatException("не JSON-объект");
                var p = new PlayerProfile();
                JsonUtility.FromJsonOverwrite(text, p);
                int fixes = p.Sanitize();
                if (fixes > 0) Debug.LogWarning($"[Profile] исправлено значений: {fixes}");
                return p;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Profile] Файл профиля повреждён ({e.Message}) — профиль по умолчанию: {path}");
                return new PlayerProfile();
            }
        }

        public static void Save(PlayerProfile profile)
        {
            string path = FilePath, tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, JsonUtility.ToJson(profile, true));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Profile] Не удалось сохранить {path}: {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ничего */ }
            }
        }
    }

    /// <summary>
    /// Текущий профиль (T65). Выбор автомобиля и его настройки сохраняются сразу. Коробка передач выбранной машины —
    /// то же значение, что «Коробка передач» в настройках (gameplay.transmission): меняется в любом из двух экранов,
    /// у каждой машины профиль помнит своё.
    /// </summary>
    public static class ProfileService
    {
        static PlayerProfile current;
        static bool hooked;

        public static PlayerProfile Current { get { Hook(); return current ?? (current = ProfileStore.Load()); } }
        public static event Action<PlayerProfile> Changed;

        /// <summary>Тесты: подменить профиль в памяти (без файла); null — перечитать из файла при следующем обращении.</summary>
        public static void Reset(PlayerProfile profile = null) { current = profile; }

        public static void Save()
        {
            ProfileStore.Save(Current);
            Changed?.Invoke(Current);
        }

        /// <summary>Настройки выбранной машины (новая машина берёт коробку из текущих настроек игры).</summary>
        public static CarSetup SetupOf(string carId) => Current.For(carId, SettingsService.Current.gameplay.transmission);

        /// <summary>Выбрать машину: профиль запоминает её, «Коробка передач» в настройках становится её коробкой.</summary>
        public static CarSetup SelectCar(string carId, bool save = true)
        {
            var p = Current;
            p.carId = carId ?? "";
            var setup = SetupOf(p.carId);
            SyncTransmissionToSettings(setup.transmission, save);
            if (save) Save();
            return setup;
        }

        /// <summary>Изменить настройки выбранной машины и сохранить.</summary>
        public static void UpdateSetup(string carId, Action<CarSetup> change)
        {
            var setup = SetupOf(carId);
            change(setup);
            if (carId == Current.carId) SyncTransmissionToSettings(setup.transmission, true);
            Save();
        }

        /// <summary>
        /// T70: законченная попытка задания (зачёт или незачёт) — в профиль и в файл. Прерванные попытки не записываются.
        /// </summary>
        public static AssignmentRecord RecordAssignment(string id, bool passed, int penalty, float seconds)
        {
            var record = Current.Record(id, passed, penalty, seconds, DateTime.UtcNow.ToString("O"));
            Save();
            Debug.Log($"[Profile] задание {id}: {(passed ? "зачёт" : "незачёт")}, попыток {record.attempts}, зачётов {record.passes}");
            return record;
        }

        static void SyncTransmissionToSettings(int transmission, bool save)
        {
            var s = SettingsService.Current;
            if (s.gameplay.transmission == transmission) return;
            var copy = s.Clone(); copy.gameplay.transmission = transmission;
            if (save) SettingsStore.Save(copy);
            SettingsService.Publish(copy);
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            // «Коробка передач» изменена в настройках — это коробка выбранной машины.
            SettingsService.Applied += s =>
            {
                if (current == null || string.IsNullOrEmpty(current.carId)) return;
                var setup = current.For(current.carId, s.gameplay.transmission);
                if (setup.transmission == s.gameplay.transmission) return;
                setup.transmission = s.gameplay.transmission;
                Save();
            };
        }
    }
}
