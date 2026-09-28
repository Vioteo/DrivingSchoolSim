using System.Linq;
using DrivingSchool.Presentation.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Editor
{
    public static partial class UIBuilder
    {
        [MenuItem("Driving School/Build garage UI")]
        public static void BuildGarageUI()
        {
            BuildMainMenu();
            BuildGarage();
            BuildMenuScene();
            Debug.Log("GARAGE_UI_BUILT");
        }

        /// <summary>Экран «Автомобиль» (T65): машины игрока из каталога, их описание и настройки профиля.</summary>
        static void BuildGarage()
        {
            var theme = BuildThemes();
            var canvas = CreateCanvas("Garage");
            canvas.GetComponent<Canvas>().sortingOrder = 10;
            var garage = canvas.AddComponent<GarageController>();
            var bg = CreateFill("Background", canvas.transform);
            AddThemedImage(bg.gameObject, ThemeRole.BgDark, theme);
            var content = CreateFixed("Content", bg, new Vector2(.5f, .5f), new Vector2(1600, 860), Vector2.zero);
            var heading = CreateFixed("Heading", content, new Vector2(0, 1), new Vector2(1000, 80), Vector2.zero);
            AddThemedText(heading.gameObject, "Автомобиль", 64, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var profile = CreateFixed("Profile", content, new Vector2(1, 1), new Vector2(560, 50), new Vector2(0, -20), new Vector2(1, 1));
            garage.profile = AddThemedText(profile.gameObject, "Профиль: Ученик", 26, ThemeRole.Muted, theme, TextAlignmentOptions.Right);
            var subtitle = CreateFixed("Subtitle", content, new Vector2(0, 1), new Vector2(1500, 50), new Vector2(0, -90));
            AddThemedText(subtitle.gameObject, "Учебная машина и её настройки сохраняются в профиле и действуют со следующей поездки", 26, ThemeRole.Muted, theme, TextAlignmentOptions.Left);

            var catalog = VehicleAssembler.LoadCatalog();
            var cars = catalog.Players().ToList();
            garage.cars = cars.Select(v => new GarageController.Car
            {
                id = v.id, title = v.title, defaultTransmission = (int)v.spec.transmission, description = Describe(v),
            }).ToArray();
            garage.carButtons = cars.Select((v, i) => CreateMenuItem("Car_" + v.id, content, new Vector2(0, -200 - i * 116), new Vector2(590, 100), v.title, 32, theme, null)).ToArray();

            var panel = CreateFixed("Details", content, new Vector2(1, 1), new Vector2(950, 600), new Vector2(0, -200));
            AddThemedImage(panel.gameObject, ThemeRole.BgPanel, theme);
            var accent = CreateFixed("Accent", panel, new Vector2(0, 1), new Vector2(950, 5), Vector2.zero);
            AddThemedImage(accent.gameObject, ThemeRole.Accent, theme);
            var title = CreateFixed("Title", panel, new Vector2(0, 1), new Vector2(854, 70), new Vector2(48, -40));
            garage.title = AddThemedText(title.gameObject, "", 44, ThemeRole.Text, theme, TextAlignmentOptions.Left, FontWeight.Bold);
            var body = CreateFixed("Description", panel, new Vector2(0, 1), new Vector2(854, 150), new Vector2(48, -120));
            garage.description = AddThemedText(body.gameObject, "", 26, ThemeRole.Text2, theme, TextAlignmentOptions.TopLeft);
            garage.transmissionButton = CreateMenuItem("Transmission", panel, new Vector2(48, -300), new Vector2(854, 76), "Коробка передач", 28, theme, null, new Vector2(0, 1));
            garage.absButton = CreateMenuItem("Abs", panel, new Vector2(48, -388), new Vector2(854, 76), "ABS", 28, theme, null, new Vector2(0, 1));
            garage.paintButton = CreateMenuItem("Paint", panel, new Vector2(48, -476), new Vector2(854, 76), "Цвет кузова", 28, theme, null, new Vector2(0, 1));
            garage.backButton = CreateMenuItem("Back", content, Vector2.zero, new Vector2(340, 72), "Назад", 28, theme, null, new Vector2(0, 0));
            var hint = CreateFixed("Hint", content, new Vector2(1, 0), new Vector2(1140, 40), Vector2.zero);
            AddThemedText(hint.gameObject, "↑↓ Tab — пункт     ← → Enter — изменить     Esc — назад", 22, ThemeRole.Muted, theme, TextAlignmentOptions.Right);
            SavePrefab(canvas, "Garage");
        }

        static string Describe(DrivingSchool.Simulation.VehicleEntry v)
        {
            var s = v.spec;
            string drive = s.drive.ToString().StartsWith("Front") ? "передний" : s.drive.ToString().StartsWith("Rear") ? "задний" : "полный";
            string gearbox = s.transmission.ToString() == "Automatic" ? "АКПП" : "МКПП";
            string cabin = v.interior == "modern" ? "современный" : v.interior == "classic" ? "классический" : "простой";
            return $"Масса {s.massKg:0} кг · привод {drive} · {s.gearRatios.Length} передач\nКоробка в каталоге: {gearbox} · салон {cabin}";
        }
    }
}
