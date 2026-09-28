using System;
using DrivingSchool.Presentation.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace DrivingSchool.Tests
{
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
            events = new GameObject("AssignmentTestEvents", typeof(TestEvents));
            events.GetComponent<TestEvents>().Register(); // EditMode не вызывает OnEnable.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DrivingSchool/Prefabs/UI/LessonCatalog.prefab");
            Assert.That(prefab, Is.Not.Null);
            root = Object.Instantiate(prefab);
            catalog = root.GetComponent<LessonCatalogController>();
            Assert.That(catalog, Is.Not.Null, "Пересоберите UIBuilder.BuildAssignmentsUI");
            catalog.Initialize();
        }

        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root);
            if (events != null) events.GetComponent<TestEvents>().Unregister();
            Object.DestroyImmediate(events);
        }

        [Test] public void SelectionDoesNotLaunchAndStartUsesCurrentChoiceOnce()
        {
            int launches = 0;
            string requested = null;
            catalog.LaunchRequested += id => { requested = id; launches++; };
            catalog.Initialize(); // повторная инициализация не удваивает подписки
            catalog.Open(null);
            catalog.rangeButton.onClick.Invoke();
            Assert.That(launches, Is.Zero);
            Assert.That(catalog.title.text, Is.EqualTo("Город"), "свободная поездка начинается в городе (T65)");
            catalog.startButton.onClick.Invoke();
            Assert.That(requested, Is.EqualTo(LessonCatalogController.FreeDrive));
            Assert.That(launches, Is.EqualTo(1));
            catalog.lessonButton.onClick.Invoke();
            Assert.That(catalog.title.text, Is.EqualTo("Начало движения"));
            catalog.startButton.onClick.Invoke();
            Assert.That(requested, Is.EqualTo(LessonLaunch.FirstLesson));
            Assert.That(launches, Is.EqualTo(2));
        }

        [Test] public void InvalidChoiceIsRejectedWithoutLosingSelection()
        {
            catalog.SelectAssignment(LessonCatalogController.FreeDrive);
            Assert.Throws<ArgumentException>(() => catalog.SelectAssignment("unknown"));
            Assert.That(catalog.SelectedId, Is.EqualTo(LessonCatalogController.FreeDrive));
        }

        [Test] public void TabWrapsBackClosesAndReopenRestoresChoice()
        {
            int backs = 0;
            catalog.Open(() => backs++);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(catalog.lessonButton.gameObject));
            catalog.MoveFocus(-1);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(catalog.backButton.gameObject));
            catalog.MoveFocus(1);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(catalog.lessonButton.gameObject));
            catalog.MoveFocus(1);
            Assert.That(catalog.SelectedId, Is.EqualTo(LessonCatalogController.FreeDrive), "Фокус карточки обновляет описание");
            catalog.backButton.onClick.Invoke();
            Assert.That(backs, Is.EqualTo(1));
            Assert.That(root.activeSelf, Is.False);
            Assert.That(LessonCatalogController.ClosedThisFrame, Is.True);
            catalog.Open(null);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(catalog.rangeButton.gameObject));
        }
    }
}
