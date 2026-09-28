using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Editor;
using DrivingSchool.Simulation.RoadGraph;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Tests
{
    /// <summary>
    /// T63: a car following any connection or lane of a junction module keeps its body on the carriageway — checked
    /// against the imported meshes (raycasts), not the numbers in the templates. With square kerb corners the right
    /// turns of RK_Road_Cross_24m ran over the sidewalk corner.
    /// </summary>
    public sealed class RoadKitClearanceTests
    {
        // Half the width of the widest traffic car (ambulance 2.0 m) plus what the rear axle cuts inside a tight turn.
        const double BodyHalfWidthM = 1.0, TurnCutM = 0.25, KerbHeightM = 0.05;

        static IEnumerable<TestCaseData> Junctions()
        {
            yield return new TestCaseData("Assets/DrivingSchool/Prefabs/RoadKit/RK_Road_Cross_24m.prefab", "RK_Road_Cross_24m").SetName("v1 cross 24 m");
            foreach (var id in new[] { RoadKitTemplatesV2.Cross4x4, RoadKitTemplatesV2.Cross4x2, RoadKitTemplatesV2.Roundabout, RoadKitTemplatesV2.RailCrossing })
                yield return new TestCaseData(RoadKitBuilder.PrefabsV2 + "/" + id + ".prefab", id).SetName("v2 " + id);
            foreach (var id in new[] { RoadKitTemplatesV2.CrossPlain, RoadKitTemplatesV2.Tee, RoadKitTemplatesV2.Curve4 })
                yield return new TestCaseData(RoadKitBuilder.PrefabsV3 + "/" + id + ".prefab", id).SetName("v3 " + id);
        }

        [TestCaseSource(nameof(Junctions))]
        public void EveryPathKeepsTheCarBodyOffTheKerbs(string prefabPath, string catalogId)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            Assert.That(new RoadKitCatalog().TryGet(catalogId, out var t), catalogId);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = new Vector3(0, -500, 0);   // away from anything in the open scene
            try
            {
                Physics.SyncTransforms();
                var bad = new List<string>();
                var paths = t.Fragment.connections.Select(c => (c.id, c.centerline, turn: c.maneuver != Contracts.LaneManeuver.Straight))
                    .Concat(t.Fragment.lanes.Select(l => (l.id, l.centerline, turn: false)));
                foreach (var (id, line, turn) in paths)
                {
                    var p = new Polyline(line);
                    double half = BodyHalfWidthM + (turn ? TurnCutM : 0);
                    for (double s = 0; s <= p.Length; s += 0.25)
                        foreach (var d in new[] { -half, half })
                        {
                            var q = p.OffsetPoint(s, d);
                            var from = new Vector3((float)q.x, -497f, (float)q.z);
                            if (!Physics.Raycast(from, Vector3.down, out var hit, 6f)) continue;   // beyond the module edge
                            float h = hit.point.y + 500f;
                            if (h > KerbHeightM) { bad.Add($"{id} at s={s:F1} side {(d > 0 ? "right" : "left")}: ground {h:F2} m ({hit.collider.name})"); break; }
                        }
                }
                Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(12)));
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
