using System;
using System.Collections.Generic;

namespace DrivingSchool.Settings
{
    // Профиль ученика (T65): имя, выбранный автомобиль и настройки каждого автомобиля. Чистый C#, JsonUtility.
    // Файл — Application.persistentDataPath/profile.json (ProfileStore); настройки игры (settings.json) — отдельно.

    [Serializable]
    public sealed class CarSetup
    {
        public string carId = "";
        public int transmission = 0;              // МКПП / АКПП — то же значение, что gameplay.transmission для выбранной машины
        public bool abs = true;                   // антиблокировочная система
        public int paint = 0;                     // CarPaints: 0 — заводской цвет
        public CarSetup Clone() => (CarSetup)MemberwiseClone();
    }

    [Serializable]
    public sealed class PlayerProfile
    {
        public const int CurrentVersion = 1;
        public const string DefaultName = "Ученик";

        public int version = CurrentVersion;
        public string name = DefaultName;
        public string carId = "";                 // id из каталога (Data/Vehicles/vehicles.json); пусто — первый автомобиль
        public CarSetup[] cars = Array.Empty<CarSetup>();

        /// <summary>Настройки автомобиля; если их ещё нет — создаются со значениями по умолчанию.</summary>
        public CarSetup For(string id, int defaultTransmission = 0)
        {
            foreach (var c in cars) if (c != null && c.carId == id) return c;
            var created = new CarSetup { carId = id ?? "", transmission = defaultTransmission };
            var list = new List<CarSetup>(cars ?? Array.Empty<CarSetup>()) { created };
            cars = list.ToArray();
            return created;
        }

        public bool Has(string id)
        {
            foreach (var c in cars) if (c != null && c.carId == id) return true;
            return false;
        }

        public PlayerProfile Clone()
        {
            var p = (PlayerProfile)MemberwiseClone();
            p.cars = Array.ConvertAll(cars ?? Array.Empty<CarSetup>(), c => c?.Clone());
            return p;
        }

        /// <summary>Исправляет то, что могло прийти из битого или старого файла; возвращает число исправлений.</summary>
        public int Sanitize()
        {
            int fixes = 0;
            if (string.IsNullOrWhiteSpace(name)) { name = DefaultName; fixes++; }
            if (name.Length > 32) { name = name.Substring(0, 32); fixes++; }
            if (carId == null) { carId = ""; fixes++; }
            var list = new List<CarSetup>(); var seen = new HashSet<string>();
            foreach (var c in cars ?? Array.Empty<CarSetup>())
            {
                if (c == null || string.IsNullOrEmpty(c.carId) || !seen.Add(c.carId)) { fixes++; continue; }
                if (c.transmission < 0 || c.transmission > 1) { c.transmission = 0; fixes++; }
                if (c.paint < 0 || c.paint >= CarPaints.Count) { c.paint = 0; fixes++; }
                list.Add(c);
            }
            cars = list.ToArray();
            if (version < 1) { version = CurrentVersion; fixes++; }
            return fixes;
        }
    }

    /// <summary>Цвета кузова на выбор (игровая палитра, линейные RGB 0…1).</summary>
    public static class CarPaints
    {
        public static readonly string[] Names = { "Заводской", "Белый", "Серебристый", "Чёрный", "Красный", "Синий", "Зелёный" };
        static readonly float[][] Rgb =
        {
            null, new[] { .86f, .87f, .86f }, new[] { .55f, .57f, .6f }, new[] { .025f, .027f, .03f },
            new[] { .55f, .04f, .04f }, new[] { .05f, .14f, .42f }, new[] { .07f, .27f, .14f },
        };
        public static int Count => Names.Length;
        /// <summary>Цвет для индекса; false — заводской (не менять).</summary>
        public static bool TryGet(int index, out float r, out float g, out float b)
        {
            r = g = b = 0;
            if (index <= 0 || index >= Rgb.Length) return false;
            r = Rgb[index][0]; g = Rgb[index][1]; b = Rgb[index][2];
            return true;
        }
    }
}
