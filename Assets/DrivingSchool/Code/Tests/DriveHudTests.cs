using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Editor;
using DrivingSchool.Learning;
using DrivingSchool.Presentation;
using DrivingSchool.Presentation.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T47: HUD поездки, разбор, сценарий инструктора полигона.</summary>
    public class DriveHudTests
    {
        const string HudPrefab = "Assets/DrivingSchool/Prefabs/UI/HUD.prefab";

        sealed class Hud : System.IDisposable
        {
            public readonly GameObject Root; public readonly DriveHudView V; public readonly DebriefView D;
            public Hud()
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
                Assert.That(prefab, Is.Not.Null, "Нет HUD.prefab — Driving School/Build Drive HUD");
                Root = Object.Instantiate(prefab);
                V = Root.GetComponent<DriveHudView>();
                D = Root.GetComponentInChildren<DebriefView>(true);
                Assert.That(V, Is.Not.Null); Assert.That(D, Is.Not.Null);
            }
            public void Dispose() => Object.DestroyImmediate(Root);
        }

        [Test] public void HudUsesUIFontsWithAllGlyphs()
        {
            using (var h = new Hud())
                foreach (var t in h.Root.GetComponentsInChildren<TMP_Text>(true))
                {
                    Assert.That(t.font.name, Does.StartWith("GolosText").Or.StartWith("RobotoMono"), t.name);
                    Assert.That(t.font.HasCharacters(t.text, out var missing), Is.True, $"{t.name}: {string.Join(" ", missing ?? new System.Collections.Generic.List<char>())}");
                }
        }

        [Test] public void HudModesFollowSpec()
        {
            using (var h = new Hud())
            {
                var m = new HudModel { hudMode = 0, hintText = "Пристегните ремень", hintKind = (int)HintKind.Maneuver, minimapAvailable = true, minimap = Texture2D.whiteTexture };
                h.V.Render(m);
                Assert.That(h.V.instruments.activeSelf && h.V.telltalesRow.activeSelf && h.V.minimapPanel.activeSelf, Is.True, "Полный");
                Assert.That(h.V.hintPanel.activeSelf, Is.True);
                m.hudMode = 1; h.V.Render(m);
                Assert.That(h.V.instruments.activeSelf, Is.True); Assert.That(h.V.telltalesRow.activeSelf, Is.False, "Минимальный — скорость и КПП");
                Assert.That(h.V.minimapPanel.activeSelf, Is.False);
                m.hudMode = 2; h.V.Render(m);
                Assert.That(h.V.instruments.activeSelf, Is.False, "Выкл.");
                Assert.That(h.V.hintPanel.activeSelf && h.V.remarksPanel.activeSelf, Is.True, "Подсказки и замечания видны всегда");
                m.hudMode = 0; m.cockpit = true; h.V.Render(m);
                Assert.That(h.V.instruments.activeSelf, Is.False, "Из салона приборы показывает панель машины");
                m.hintText = null; h.V.Render(m);
                Assert.That(h.V.hintPanel.activeSelf, Is.False);
            }
        }

        [Test] public void GearboxShowsManualPatternOrSelector()
        {
            using (var h = new Hud())
            {
                var m = new HudModel { manual = true, gear = 3, gearCount = 5 };
                h.V.Render(m);
                Assert.That(h.V.manualBox.activeSelf, Is.True);
                Assert.That(h.V.gearLabels[5].gameObject.activeSelf, Is.False, "Шестой передачи у 5-ступенчатой коробки нет");
                Assert.That(h.V.gearLabels[2].color, Is.Not.EqualTo(h.V.gearLabels[0].color), "Включённая передача подсвечена");
                m.manual = false; m.selector = 3; h.V.Render(m);
                Assert.That(h.V.autoBox.activeSelf, Is.True); Assert.That(h.V.manualBox.activeSelf, Is.False);
            }
        }

        [Test] public void AtMostTwoViolationCards()
        {
            using (var h = new Hud())
            {
                h.V.ShowCard("1", "r", "a", false); h.V.ShowCard("2", "r", "a", true); h.V.ShowCard("3", "r", "a", false);
                Assert.That(h.V.CardCount, Is.EqualTo(2));
            }
        }

        [Test] public void DebriefListsEventsOrSaysClean()
        {
            using (var h = new Hud())
            {
                var m = new DebriefModel();
                m.summary.Add(("Время", "1:00"));
                m.events.Add(new DebriefEvent { time = "00:10", title = "Столкновение", advice = "…", reference = "COLLISION", place = "площадка", severe = true });
                h.D.Show(m);
                Assert.That(DebriefView.IsOpen, Is.True);
                Assert.That(h.D.list.GetComponentsInChildren<UnityEngine.UI.Button>(false).Length, Is.EqualTo(1));
                Assert.That(h.D.emptyText.gameObject.activeSelf, Is.False);
                Assert.That(h.D.detailTitle.text, Is.EqualTo("Столкновение"));
                h.D.Hide();
                h.D.Show(new DebriefModel());
                Assert.That(h.D.emptyText.gameObject.activeSelf, Is.True, "Нет замечаний — так и написано");
                h.D.Hide();
                Assert.That(DebriefView.IsOpen, Is.False);
            }
        }

        static VehicleState Parked(bool belt = false, EnginePhase engine = EnginePhase.Off) =>
            new VehicleState { seatbelt = belt, engine = engine, handbrake = true, transmission = TransmissionType.Manual };

        [Test] public void InstructorWalksThroughPreparationInOrder()
        {
            var q = new InstructorHintQueue(); var ins = new TestRangeInstructor();
            var start = new Vector3(-2, 0, -20);
            ins.Update(q, Parked(), start, Vector3.forward, null);
            Assert.That(q.Current.Key, Is.EqualTo("prep-belt"));
            ins.Update(q, Parked(belt: true), start, Vector3.forward, null);
            Assert.That(q.Current.Key, Is.EqualTo("prep-engine"));
            var running = Parked(belt: true, engine: EnginePhase.Running);
            ins.Update(q, running, start, Vector3.forward, null);
            Assert.That(q.Current.Key, Is.EqualTo("prep-lights"));
            running.lowBeam = true;
            ins.Update(q, running, start, Vector3.forward, null);
            Assert.That(q.Current.Key, Is.EqualTo("prep-handbrake"));
            running.handbrake = false;
            ins.Update(q, running, start, Vector3.forward, null);
            Assert.That(q.Current, Is.Null, "Готов — подсказок нет");
        }

        [Test] public void InstructorWarnsAboutZonesAhead()
        {
            var q = new InstructorHintQueue(); var ins = new TestRangeInstructor();
            var st = Parked(belt: true, engine: EnginePhase.Running); st.handbrake = false; st.lowBeam = true; st.signedSpeedMps = 8;
            ins.Update(q, st, new Vector3(1, 0, TestRangeLayout.BumpRubberZ - 30), Vector3.forward, null);
            Assert.That(q.Current.Key, Is.EqualTo("zone-bump"));
            ins.Update(q, st, new Vector3(1, 0, TestRangeLayout.BumpRubberZ + 10), Vector3.forward, null);
            Assert.That(q.Has("zone-bump"), Is.False, "Проехали — подсказка снята");
            ins.Update(q, st, new Vector3(TestRangeLayout.RoadCX + 2, 0, TestRangeLayout.RailZ - 50), Vector3.forward, null);
            Assert.That(q.Current.Key, Is.EqualTo("zone-rail"));
            Assert.That(q.Current.Kind, Is.EqualTo(HintKind.Maneuver), "Без закрытого переезда — подготовка, не опасность");
            st.signedSpeedMps = -3;   // задним ходом от переезда
            ins.Update(q, st, new Vector3(TestRangeLayout.RoadCX + 2, 0, TestRangeLayout.RailZ - 50), Vector3.forward, null);
            Assert.That(q.Has("zone-rail"), Is.False);
        }

        [Test] public void LayoutZonesMatchGeneratorConstants()
        {
            Assert.That(VehicleTestRangeBuilder.RailZ, Is.EqualTo(TestRangeLayout.RailZ));
            Assert.That(VehicleTestRangeBuilder.BumpRubberZ, Is.EqualTo(TestRangeLayout.BumpRubberZ));
            Assert.That(TestRangeLayout.OnRailwayCrossing(new Vector3(TestRangeLayout.RoadCX, 0, TestRangeLayout.RailZ)), Is.True);
            Assert.That(TestRangeLayout.OnRailwayCrossing(new Vector3(TestRangeLayout.RoadCX, 0, TestRangeLayout.RailZ - 20)), Is.False);
            Assert.That(TestRangeLayout.ZoneName(new Vector3(0, 0, 200)), Is.EqualTo("слалом"));
        }

        [Test] public void PauseHasFinishDriveItem()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DrivingSchool/Prefabs/UI/PauseMenu.prefab");
            Assert.That(prefab.GetComponent<PauseMenuController>().finishButton, Is.Not.Null);
        }
    }
}
