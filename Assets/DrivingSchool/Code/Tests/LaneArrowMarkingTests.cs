using System.Collections.Generic;
using DrivingSchool.World;
using NUnit.Framework;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>Общая заготовка стрелок 1.18 (T68): размеры по ГОСТ, стрела не выходит из полосы, меш смотрит вверх.</summary>
    public sealed class LaneArrowMarkingTests
    {
        static IEnumerable<TestCaseData> All()
        {
            foreach (var kind in LaneArrowMarking.Standard)
                foreach (var size in new[] { LaneArrowSize.Upto60, LaneArrowSize.Above60 })
                    yield return new TestCaseData(kind, size).SetName("{m}(" + LaneArrowMarking.MeshName(kind, size) + ")");
        }

        [Test] public void CodesRoundTrip()
        {
            foreach (var kind in LaneArrowMarking.Standard)
                Assert.That(LaneArrowMarking.Parse(LaneArrowMarking.ToCode(kind)), Is.EqualTo(kind));
            Assert.That(LaneArrowMarking.Parse("sr"), Is.EqualTo(LaneArrow.Straight | LaneArrow.Right));
            Assert.Throws<System.ArgumentException>(() => LaneArrowMarking.Parse("SX"));
            Assert.Throws<System.ArgumentException>(() => LaneArrowMarking.Triangles(LaneArrow.None, LaneArrowSize.Upto60));
        }

        [Test] public void StraightArrowHasGostLength()
        {
            Assert.That(LaneArrowMarking.Length(LaneArrow.Straight, LaneArrowSize.Upto60), Is.EqualTo(5f).Within(.001f));
            Assert.That(LaneArrowMarking.Length(LaneArrow.Straight, LaneArrowSize.Above60), Is.EqualTo(7.5f).Within(.001f));
            Assert.That(LaneArrowMarking.Length(LaneArrow.Straight | LaneArrow.Right, LaneArrowSize.Upto60), Is.EqualTo(5f).Within(.001f));
        }

        [TestCaseSource(nameof(All))]
        public void StaysInsideTheLane(LaneArrow kind, LaneArrowSize size)
        {
            var f = LaneArrowMarking.Footprint(kind, size);
            Assert.That(f.xMin, Is.GreaterThanOrEqualTo(-LaneArrowMarking.MaxHalfWidth));
            Assert.That(f.xMax, Is.LessThanOrEqualTo(LaneArrowMarking.MaxHalfWidth));
            Assert.That(f.yMin, Is.GreaterThanOrEqualTo(-.01f));
            Assert.That(f.yMax, Is.LessThanOrEqualTo(size == LaneArrowSize.Above60 ? 7.5f : 5f) .And.GreaterThan(3f));
            // Ветви уходят в свою сторону.
            if ((kind & LaneArrow.Right) != 0) Assert.That(f.xMax, Is.GreaterThan(.6f));
            if ((kind & (LaneArrow.Left | LaneArrow.UTurn)) != 0) Assert.That(f.xMin, Is.LessThan(-.6f));
            if ((kind & (LaneArrow.Left | LaneArrow.UTurn)) == 0) Assert.That(f.xMin, Is.GreaterThan(-.35f));
        }

        [TestCaseSource(nameof(All))]
        public void TrianglesFaceUpAtAnyHeading(LaneArrow kind, LaneArrowSize size)
        {
            foreach (float heading in new[] { 0f, 90f, 180f, 270f, 33f })
            {
                var v = new List<Vector3>(); var t = new List<int>();
                LaneArrowMarking.Append(kind, size, new Vector3(10, .02f, -4), heading, v, t);
                Assert.That(t.Count, Is.GreaterThanOrEqualTo(9));
                for (int i = 0; i < t.Count; i += 3)
                {
                    var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    Assert.That(n.y, Is.GreaterThan(0f), "triangle " + i / 3 + " at heading " + heading);
                }
                foreach (var p in v) Assert.That(p.y, Is.EqualTo(.02f));
            }
        }

        [Test] public void TurnHeadsEndBelowTheStraightHead()
        {
            // «Прямо и направо»: оголовок ветви не заходит на оголовок прямой стрелы (было — стрелки налезали).
            var t = LaneArrowMarking.Triangles(LaneArrow.Straight | LaneArrow.Right, LaneArrowSize.Upto60);
            float branchTop = 0;
            foreach (var v in t) if (v.x > .31f) branchTop = Mathf.Max(branchTop, v.y);
            Assert.That(branchTop, Is.LessThanOrEqualTo(5f - 1.5f - .25f));
        }

        [Test] public void MeshIsHeadingPlusZFromTail()
        {
            var mesh = LaneArrowMarking.CreateMesh(LaneArrow.Straight, LaneArrowSize.Upto60);
            Assert.That(mesh.bounds.min.z, Is.EqualTo(0f).Within(.001f));
            Assert.That(mesh.bounds.max.z, Is.EqualTo(5f).Within(.001f));
            Assert.That(mesh.bounds.size.x, Is.EqualTo(.6f).Within(.001f));
            Object.DestroyImmediate(mesh);
        }
    }
}
