using System;
using System.Linq;
using DrivingSchool.Presentation.UI;
using DrivingSchool.Settings;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace DrivingSchool.Tests
{
    /// <summary>Экран «Задания» (T70): разделы, все упражнения площадки списком, запуск выбранного, прогресс из профиля.</summary>
    public sealed class AssignmentUITests
    {
        GameObject root, events;
        sealed class TestEvents : EventSystem
        {
            public void Register() { base.OnEnable(); }
            public void Unregister() { base.OnDisable(); }
        }
        LessonCatalogController catalog;

        [SetUp] public void Setup()
        {
            ProfileStore.FilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ds-assignments-" + Guid.NewGuid().ToString("N") + ".json");
            ProfileService.Reset(new PlayerProfile());   // прогресс — только из теста, не из файла ученика
            events = new GameObject("AssignmentTestEvents", typeof(TestEvents));
            events.GetComponent<TestEvents>().Register(); // EditMode не вызывает OnEnable.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DrivingSchool/Prefabs/UI/LessonCatalog.prefab");
            Assert.That(prefab, Is.Not.Null);
            root = Object.Instantiate(prefab);
            catalog = root.GetComponent<LessonCatalogController>();
            Assert.That(catalog, Is.Not.Null, "Пересоберите UIBuilder.BuildAssignmentsUI");
            Assert.That(catalog.rowTemplate, Is.Not.Null, "Пересоберите UIBuilder.BuildAssignmentsUI (T70)");
            catalog.Initialize();
        }

        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root);
            if (events != null) events.GetComponent<TestEvents>().Unregister();
            Object.DestroyImmediate(events);
            if (System.IO.File.Exists(ProfileStore.FilePath)) System.IO.File.Delete(ProfileStore.FilePath);
            ProfileStore.FilePath = null;
            ProfileService.Reset();
        }

        TMP_Text[] RowStatuses() => catalog.listContent.GetComponentsInChildren<Transform>(false)
            .Where(t => t.name == "Status").Select(t => t.GetComponent<TMP_Text>()).ToArray();

        [Test] public void EveryAutodromeExerciseIsARowOfItsSection()
        {
            catalog.Open(null);
            catalog.ShowSection(LessonCatalogController.Autodrome, 0);
            Assert.That(catalog.ExerciseCount, Is.GreaterThanOrEqualTo(9), "упражнения площадки — из guided-lessons.json");
            var rows = catalog.listContent.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToList();
            Assert.That(rows.Count, Is.EqualTo(catalog.ExerciseCount), "каждое упражнение — отдельная строка, не спрятано за Q / E");
            Assert.That(catalog.SectionItems(LessonCatalogController.Autodrome).All(i => i.id.StartsWith("ex-")), Is.True);
            Assert.That(rows.Select(r => r.GetComponent<MenuItemView>().label.text), Is.EqualTo(catalog.SectionItems(LessonCatalogController.Autodrome).Select(i => i.title)));
        }

        [Test] public void SelectingDoesNotLaunchAndStartLaunchesTheSelectedItemOnce()
        {
            int launches = 0; string requested = null;
            catalog.LaunchRequested += id => { requested = id; launches++; };
            catalog.Initialize(); // повторная инициализация не удваивает подписки
            catalog.Open(null);
            catalog.SelectAssignment(LessonCatalogController.FreeDrive);
            Assert.That(launches, Is.Zero);
            Assert.That(catalog.title.text, Is.EqualTo("Город"), "свободная поездка начинается в городе (T65)");
            catalog.startButton.onClick.Invoke();
            Assert.That(requested, Is.EqualTo(LessonCatalogController.FreeDrive));
            Assert.That(launches, Is.EqualTo(1));

            catalog.ShowSection(LessonCatalogController.Autodrome, 0);
            catalog.MoveFocus(1);
            var second = catalog.SectionItems(LessonCatalogController.Autodrome)[1];
            Assert.That(catalog.SelectedId, Is.EqualTo(second.id));
            catalog.listContent.Cast<Transform>().First(t => t.gameObject.activeSelf).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.That(catalog.ItemIndex, Is.Zero, "щелчок мышью по строке выбирает её, но не запускает");
            Assert.That(launches, Is.EqualTo(1));
            catalog.Launch();   // ✕ / Enter
            Assert.That(requested, Is.EqualTo(catalog.SectionItems(LessonCatalogController.Autodrome)[0].id), "запускается выбранное упражнение");
            catalog.SelectAssignment(LessonLaunch.AutodromeExam);
            Assert.That(catalog.title.text, Is.EqualTo(LessonCatalogController.AutodromeExamTitle));
            catalog.Launch();
            Assert.That(requested, Is.EqualTo(LessonLaunch.AutodromeExam));
        }

        [Test] public void SectionsAndItemsWrapAndReopenRestoresTheChoice()
        {
            int backs = 0;
            catalog.Open(() => backs++);
            catalog.ShowSection(LessonCatalogController.Lessons, 0);
            catalog.ShiftSection(-1);
            Assert.That(catalog.SectionIndex, Is.EqualTo(LessonCatalogController.City), "разделы по кругу");
            catalog.ShiftSection(1); catalog.ShiftSection(1);
            Assert.That(catalog.SectionIndex, Is.EqualTo(LessonCatalogController.Autodrome));
            int n = catalog.ExerciseCount;
            catalog.MoveFocus(-1);
            Assert.That(catalog.ItemIndex, Is.EqualTo(n - 1), "список по кругу");
            string chosen = catalog.SelectedId;
            catalog.ShiftSection(1);
            catalog.ShiftSection(-1);
            Assert.That(catalog.SelectedId, Is.EqualTo(chosen), "в разделе помнится выбранное задание");
            catalog.backButton.onClick.Invoke();
            Assert.That(backs, Is.EqualTo(1));
            Assert.That(root.activeSelf, Is.False);
            Assert.That(LessonCatalogController.ClosedThisFrame, Is.True);
            catalog.Open(null);
            Assert.That(catalog.SelectedId, Is.EqualTo(chosen));
        }

        [Test] public void InvalidChoiceIsRejectedWithoutLosingSelection()
        {
            catalog.SelectAssignment(LessonCatalogController.FreeDrive);
            Assert.Throws<ArgumentException>(() => catalog.SelectAssignment("unknown"));
            Assert.That(catalog.SelectedId, Is.EqualTo(LessonCatalogController.FreeDrive));
        }

        [Test] public void ProgressFromTheProfileIsShownOnRowsSectionsAndDetails()
        {
            var ex = catalog.SectionItems(LessonCatalogController.Autodrome);
            var p = ProfileService.Current;
            p.Record(ex[0].id, false, 6, 200, "2026-09-30T10:00:00Z");
            p.Record(ex[0].id, true, 2, 190, "2026-09-30T10:10:00Z");
            p.Record(ex[1].id, false, 5, 90, "2026-09-30T10:20:00Z");
            catalog.Open(null);
            catalog.ShowSection(LessonCatalogController.Autodrome, 0);
            var statuses = RowStatuses();
            Assert.That(statuses[0].text, Is.EqualTo("сдано · 2 б."));
            Assert.That(statuses[1].text, Is.EqualTo("не сдано"));
            Assert.That(statuses[2].text, Is.EqualTo("не начато"));
            Assert.That(catalog.sectionLabels[LessonCatalogController.Autodrome].text, Is.EqualTo($"Площадка  1/{ex.Count}"));
            Assert.That(catalog.progress.text, Does.Contain("Попыток: 2 · зачётов: 1").And.Contain("Лучший зачёт: 2 б. · 3:10"));
            Assert.That(catalog.overall.text, Is.EqualTo($"Пройдено 1 из {ex.Count + 2}"), "урок, упражнения и экзамен; город без прогресса");

            // Новая попытка из поездки — экран обновляется по событию профиля.
            ProfileService.RecordAssignment(ex[1].id, true, 0, 80);
            Assert.That(RowStatuses()[1].text, Is.EqualTo("сдано · 0 б."));
        }

        [Test] public void RecordKeepsTheBestPassAndCountsAttempts()
        {
            var p = new PlayerProfile();
            p.Record("ex-a", true, 3, 100, "");
            p.Record("ex-a", true, 1, 150, "");
            p.Record("ex-a", true, 1, 120, "");
            var r = p.Record("ex-a", false, 7, 50, "t");
            Assert.That((r.attempts, r.passes, r.bestPenalty, r.bestSeconds, r.lastPassed, r.lastUtc), Is.EqualTo((4, 3, 1, 120f, false, "t")));
            Assert.That(p.Progress("ex-b"), Is.Null);
            Assert.Throws<ArgumentException>(() => p.Record("", true, 0, 0, ""));
            p.assignments = new[] { r, new AssignmentRecord { id = "ex-a" }, null, new AssignmentRecord { id = "x", attempts = 1, passes = 3, bestPenalty = 2, bestSeconds = 5 } };
            Assert.That(p.Sanitize(), Is.GreaterThanOrEqualTo(3));
            Assert.That(p.assignments.Select(a => a.id), Is.EqualTo(new[] { "ex-a", "x" }));
            Assert.That(p.Progress("x").passes, Is.EqualTo(1), "зачётов не больше попыток");
        }
    }
}
