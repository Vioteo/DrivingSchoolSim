using DrivingSchool.Contracts;
using DrivingSchool.Learning;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Инструктор тестового полигона: подготовка к поездке (ремень, пуск, свет, ручник) и подсказки по зонам —
    /// неровности, слалом, поворот, горка, ж/д переезд, площадка (docs/ui-drive.md §2). Подсказки без клавиш:
    /// управление — в паузе (F4). Скорости в тексте не называем: игровые пороги ≠ нормы (CLAUDE.md, правило 5).
    /// </summary>
    public sealed class TestRangeInstructor
    {
        const float Moving = 1.0f;
        bool padShown;

        public void Update(InstructorHintQueue q, VehicleState s, Vector3 pos, Vector3 forward, RailwayCrossingView crossing)
        {
            float v = s.signedSpeedMps;
            bool moving = Mathf.Abs(v) > Moving;
            bool running = s.engine == EnginePhase.Running;
            bool lights = s.lowBeam || s.highBeam;
            Vector3 dir = forward * Mathf.Sign(v == 0 ? 1 : v);

            // --- подготовка: по одному шагу за раз, в порядке, как в автошколе ---
            Toggle(q, "prep-belt", !moving && !s.seatbelt, HintKind.Maneuver, "Пристегните ремень безопасности.");
            Toggle(q, "prep-engine", !moving && s.seatbelt && !running && s.engine != EnginePhase.Stalled, HintKind.Maneuver,
                s.transmission == TransmissionType.Automatic ? "Селектор в P — запустите двигатель." : "Выжмите сцепление и запустите двигатель.");
            Toggle(q, "stalled", s.engine == EnginePhase.Stalled, HintKind.Error, "Двигатель заглох. Выжмите сцепление, включите нейтраль и запустите двигатель снова.");
            Toggle(q, "prep-lights", !moving && running && s.seatbelt && !lights, HintKind.Maneuver, "Включите ближний свет — в движении он обязателен.");
            Toggle(q, "prep-handbrake", !moving && running && s.seatbelt && lights && s.handbrake, HintKind.Maneuver, "Снимите машину со стояночного тормоза и плавно трогайтесь.");
            Toggle(q, "handbrake-moving", moving && s.handbrake, HintKind.Error, "Машина едет на стояночном тормозе — снимите его.");

            // --- зоны ---
            bool bump = false, slalom = false, curve = false, hill = false, rail = false, railClosed = false;
            if (TestRangeLayout.OnRoadA(pos) && moving)
            {
                float ahead(float z) => (z - pos.z) * Mathf.Sign(dir.z);
                foreach (var bz in new[] { TestRangeLayout.BumpRubberZ, TestRangeLayout.BumpAsphaltZ })
                    if (ahead(bz) > 3f && ahead(bz) < 45f) bump = true;
                slalom = pos.z > TestRangeLayout.SlalomStartZ - 15f && pos.z < TestRangeLayout.SlalomEndZ;
                curve = dir.z > 0 && pos.z > TestRangeLayout.SlalomEndZ + 2f && pos.z < TestRangeLayout.RoadAEndZ;
            }
            if (TestRangeLayout.OnRoadC(pos))
            {
                float toRail = (TestRangeLayout.RailZ - pos.z) * Mathf.Sign(dir.z);
                bool approaching = toRail > TestRangeLayout.CrossingHalfLength && toRail < 90f;
                bool waiting = !moving && Mathf.Abs(TestRangeLayout.RailZ - pos.z) < 30f;
                railClosed = crossing != null && crossing.IsClosedForTraffic;
                rail = (moving && approaching) || (waiting && railClosed);
                float z = pos.z;
                hill = dir.z > 0 ? z > TestRangeLayout.HillStartZ - 35f && z < TestRangeLayout.HillStartZ + TestRangeLayout.HillRamp
                                 : z < TestRangeLayout.HillEndZ + 35f && z > TestRangeLayout.HillEndZ - TestRangeLayout.HillRamp;
            }
            Toggle(q, "zone-bump", bump, HintKind.Maneuver, "Впереди искусственная неровность — снизьте скорость и проезжайте её плавно.");
            Toggle(q, "zone-slalom", slalom, HintKind.Exercise, "Слалом: объезжайте конусы поочерёдно, не задевая их.");
            Toggle(q, "zone-curve", curve, HintKind.Navigation, "Впереди поворот направо — снизьте скорость до входа в поворот.");
            Toggle(q, "zone-hill", hill, HintKind.Exercise, "Подъём: остановитесь у стоп-линии, затем троньтесь без отката назад.");
            if (rail && railClosed) q.Post("zone-rail", HintKind.Safety, "Переезд закрыт — остановитесь у стоп-линии и дождитесь, пока поднимется шлагбаум.");
            else Toggle(q, "zone-rail", rail, HintKind.Maneuver, "Впереди железнодорожный переезд: снизьте скорость и убедитесь, что поезда нет.");

            if (TestRangeLayout.OnPad(pos) && !padShown)
            {
                padShown = true;
                q.Post("zone-pad", HintKind.Exercise, "Площадка для манёвров. Справа — лёд: на нём машина скользит, тормозите заранее.", 8);
            }
        }

        static void Toggle(InstructorHintQueue q, string key, bool on, HintKind kind, string text)
        {
            if (on) q.Post(key, kind, text); else q.Clear(key);
        }
    }
}
