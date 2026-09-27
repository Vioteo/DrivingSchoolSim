using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// What a car model must contain for the shared vehicle stack to work on it (T48, docs/vehicle-models.md).
    /// Every view finds its parts by name, so a new model gets a feature for free when it has the parts, and loses
    /// only that feature (not the car) when a part is missing. <see cref="Audit"/> says which is which.
    /// </summary>
    public static class VehicleModelContract
    {
        public enum Level { Required, Player, Optional }

        public sealed class Feature
        {
            public string Id, Title; public Level Level; public string[] Parts, AnyOf;
            public Feature(string id, string title, Level level, string[] parts, string[] anyOf = null)
            { Id = id; Title = title; Level = level; Parts = parts; AnyOf = anyOf ?? Array.Empty<string>(); }
        }

        /// <summary>Parts are exact object names; AnyOf entries ending with '*' are prefixes.</summary>
        public static readonly IReadOnlyList<Feature> Features = new[]
        {
            new Feature("wheels", "Колёса: подвеска, вращение, поворот", Level.Required, new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" }),
            new Feature("lamps", "Фары, фонари, поворотники (по имени или материалу Lamp_*)", Level.Required, Array.Empty<string>(), new[] { "Headlight*", "Taillight*", "TurnSignal*" }),
            new Feature("eye", "Вид из салона (точка глаз водителя)", Level.Player, new[] { "Socket_DriverEye" }),
            new Feature("steering", "Руль вращается (900°)", Level.Player, new[] { "SteeringWheel_Pivot" }),
            new Feature("pedals", "Педали", Level.Player, new[] { "Pedal_Clutch", "Pedal_Brake", "Pedal_Throttle" }),
            new Feature("mirrors", "Зеркала: левое, салонное, правое", Level.Player, new[] { "MirrorSurface_L", "MirrorSurface_Centre", "MirrorSurface_R" }),
            new Feature("glass", "Стёкла: дождь, дворники чистят лобовое", Level.Player, new[] { "Glass_Windshield" }, new[] { "Glass_*" }),
            new Feature("wipers", "Дворники", Level.Player, Array.Empty<string>(), new[] { "Wiper_Pivot*" }),
            new Feature("cluster", "Щиток приборов: контрольные лампы", Level.Player, Array.Empty<string>(), new[] { "Cluster_Display", "Instrument_Hood" }),
            new Feature("needles", "Стрелки тахометра и спидометра", Level.Optional, new[] { "Needle_RPM", "Needle_Speed" }),
            new Feature("gearLever", "Рычаг КПП", Level.Optional, new[] { "GearLever_Pivot" }),
            new Feature("handbrake", "Рычаг ручника", Level.Optional, new[] { "Handbrake_Pivot" }),
            new Feature("screen", "Мультимедиа (экран)", Level.Optional, new[] { "Infotainment_Glass" }),
            new Feature("transmission", "Салон МКПП/АКПП (переключается с КПП)", Level.Optional, new[] { "Transmission_Manual", "Transmission_Automatic" }),
        };

        public sealed class Result
        {
            public Feature Feature; public bool Ok; public string Detail;
        }

        public sealed class Report
        {
            public string ModelName;
            public readonly List<Result> Results = new List<Result>();
            public readonly List<string> Problems = new List<string>();
            public Vector3 SizeM; public float WheelbaseM, TrackM, WheelRadiusM;

            /// <summary>True when the car can be driven at all (required parts, sane geometry).</summary>
            public bool UsableForTraffic => Problems.Count == 0 && Results.Where(r => r.Feature.Level == Level.Required).All(r => r.Ok);
            /// <summary>True when every player feature is present too.</summary>
            public bool UsableForPlayer => UsableForTraffic && Results.Where(r => r.Feature.Level == Level.Player).All(r => r.Ok);

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Проверка модели {ModelName}").AppendLine();
                sb.AppendLine($"Габариты {SizeM.z:F2} × {SizeM.x:F2} × {SizeM.y:F2} м (Д×Ш×В), база {WheelbaseM:F2} м, колея {TrackM:F2} м, радиус колеса {WheelRadiusM:F3} м.").AppendLine();
                sb.AppendLine($"Игрок: **{(UsableForPlayer ? "PASS" : "FAIL")}**, трафик: **{(UsableForTraffic ? "PASS" : "FAIL")}**").AppendLine();
                sb.AppendLine("| Функция | Уровень | Итог | Подробности |").AppendLine("|---|---|---|---|");
                foreach (var r in Results)
                    sb.AppendLine($"| {r.Feature.Title} | {LevelName(r.Feature.Level)} | {(r.Ok ? "есть" : "**нет**")} | {r.Detail} |");
                if (Problems.Count > 0) { sb.AppendLine().AppendLine("Ошибки геометрии:"); foreach (var p in Problems) sb.AppendLine("- " + p); }
                return sb.ToString();
            }

            static string LevelName(Level l) => l == Level.Required ? "обязательно" : l == Level.Player ? "машина игрока" : "желательно";
        }

        /// <summary>Checks <paramref name="model"/> as it sits under <paramref name="car"/> (car space: X right, Y up, Z forward).</summary>
        public static Report Audit(Transform car, Transform model)
        {
            var report = new Report { ModelName = model.name };
            var all = model.GetComponentsInChildren<Transform>(true);
            var names = new HashSet<string>(all.Select(t => t.name));
            bool Has(string pattern) => pattern.EndsWith("*") ? all.Any(t => t.name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)) : names.Contains(pattern);

            foreach (var f in Features)
            {
                var missing = f.Parts.Where(p => !names.Contains(p)).ToList();
                bool anyOk = f.AnyOf.Length == 0 || f.AnyOf.Any(Has);
                string detail = missing.Count > 0 ? "нет: " + string.Join(", ", missing) : "";
                if (!anyOk) detail += (detail.Length > 0 ? "; " : "") + "нужно одно из: " + string.Join(", ", f.AnyOf);
                if (f.Id == "lamps")
                {
                    int white = 0, red = 0, amber = 0;
                    foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            if (m.name.StartsWith("Lamp_White", StringComparison.Ordinal)) white++;
                            else if (m.name.StartsWith("Lamp_Red", StringComparison.Ordinal)) red++;
                            else if (m.name.StartsWith("Lamp_Amber", StringComparison.Ordinal)) amber++;
                        }
                    anyOk = anyOk || (white > 0 && red > 0 && amber > 0);
                    detail = $"материалы Lamp_White/Red/Amber: {white}/{red}/{amber}" + (anyOk ? "" : "; нет рассеивателей");
                }
                report.Results.Add(new Result { Feature = f, Ok = missing.Count == 0 && anyOk, Detail = detail });
            }

            // Geometry: the physics measures wheelbase, track and radius from the wheels; they must sit where a car's wheels are.
            var bounds = new Bounds(); bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var b = VehicleRigUtil.CarSpaceBounds(car, r);
                if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
            }
            report.SizeM = bounds.size;
            var wheel = new Vector3[4]; float radius = 0f; int found = 0;
            string[] wn = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int k = 0; k < 4; k++)
            {
                var t = all.FirstOrDefault(x => x.name == wn[k]); if (t == null) continue;
                var rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) { report.Problems.Add(wn[k] + " без меша"); continue; }
                var tyre = rs.Select(r => VehicleRigUtil.CarSpaceBounds(car, r)).OrderByDescending(b => b.size.y).First();
                wheel[k] = tyre.center; radius += tyre.size.y / 2f; found++;
            }
            if (found == 4)
            {
                report.WheelRadiusM = radius / 4f;
                report.WheelbaseM = (wheel[0].z + wheel[1].z - wheel[2].z - wheel[3].z) / 2f;
                report.TrackM = (wheel[1].x - wheel[0].x + wheel[3].x - wheel[2].x) / 2f;
                if (wheel[0].z < wheel[2].z || wheel[0].x > wheel[1].x) report.Problems.Add("Wheel_FL не спереди слева: модель повёрнута под корнем машины (проверь modelEulerDeg)");
                if (report.WheelbaseM < 1.8f || report.WheelbaseM > 4.5f) report.Problems.Add($"база {report.WheelbaseM:F2} м вне 1,8–4,5 м");
                if (report.TrackM < 1.2f || report.TrackM > 2.2f) report.Problems.Add($"колея {report.TrackM:F2} м вне 1,2–2,2 м");
                if (report.WheelRadiusM < 0.22f || report.WheelRadiusM > 0.5f) report.Problems.Add($"радиус колеса {report.WheelRadiusM:F3} м вне 0,22–0,5 м");
                float lowest = wheel.Min(w => w.y) - report.WheelRadiusM;
                if (Mathf.Abs(lowest - bounds.min.y) > 0.05f) report.Problems.Add($"шины не стоят на земле: низ шины {lowest:F2} м, низ модели {bounds.min.y:F2} м");
            }
            if (bounds.size.z < bounds.size.x) report.Problems.Add("модель длиннее поперёк, чем вдоль: ось вперёд не +Z");
            var eye = all.FirstOrDefault(x => x.name == "Socket_DriverEye");
            if (eye != null)
            {
                var e = car.InverseTransformPoint(eye.position);
                if (!bounds.Contains(e)) report.Problems.Add("точка глаз вне кузова");
                else if (e.x > 0f) report.Problems.Add("точка глаз справа: руль должен быть слева (правостороннее движение)");
            }
            return report;
        }
    }
}
