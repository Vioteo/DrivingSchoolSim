using System.Linq;
using System.Reflection;
using DrivingSchool.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>T69: на переезде составы идут по очереди — электричка, затем грузовой; виден только идущий.</summary>
    public sealed class RailwayConsistTests
    {
        GameObject root;

        [TearDown] public void TearDown() { if (root != null) Object.DestroyImmediate(root); }

        static Rigidbody Consist(Transform parent, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(parent, false);
            return go.AddComponent<Rigidbody>();
        }

        static bool Visible(Rigidbody rb) => rb.GetComponentsInChildren<Renderer>(true).All(r => r.enabled);

        static void Invoke(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);

        [Test]
        public void ConsistsTakeTurnsAndOnlyTheRunningOneIsShown()
        {
            root = new GameObject("Crossing");
            var view = root.AddComponent<RailwayCrossingView>();
            view.barriers = new Transform[0]; view.signals = new Transform[0];
            var emu = Consist(root.transform, "EMU"); var freight = Consist(root.transform, "Freight");
            view.consists = new[] { emu, freight };
            view.consistLengths = new[] { 88f, 150f };
            Invoke(view, "Start");
            Assert.That(Visible(emu) || Visible(freight), Is.False, "до вызова составы спрятаны");

            view.CallTrain();
            Assert.That(view.ConsistIndex, Is.EqualTo(0), "первой идёт электричка");
            Assert.That(Visible(emu) && !Visible(freight), Is.True);
            Assert.That(view.trainLength, Is.EqualTo(88f));

            Invoke(view, "Enter", RailwayCrossingView.Phase.Open); // поезд ушёл, переезд открыт
            view.CallTrain();
            Assert.That(view.ConsistIndex, Is.EqualTo(1), "затем грузовой");
            Assert.That(Visible(freight) && !Visible(emu), Is.True, "прежний состав спрятан");
            Assert.That(view.trainLength, Is.EqualTo(150f));

            Invoke(view, "Enter", RailwayCrossingView.Phase.Open);
            view.CallTrain();
            Assert.That(view.ConsistIndex, Is.EqualTo(0), "и снова электричка");
        }
    }
}
