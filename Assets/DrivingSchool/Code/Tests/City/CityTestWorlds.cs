using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Tests
{
    /// <summary>Shared fixtures for the city tests. Mirrors StreamingAssets/Examples/world.json in code.</summary>
    static class CityTestWorlds
    {
        public static WorldDocument ExampleV1()
        {
            var w = new WorldDocument { id = "training-district", name = "Учебный квартал" };
            w.nodes = new[]
            {
                Node("south", 0, -250), Node("centre", 0, 0), Node("north", 0, 250), Node("west", -250, 0), Node("east", 250, 0),
            };
            var arms = new[] { "south", "north", "west", "east" };
            w.segments = arms.Select(a => new RoadSegment { id = a + "-centre", fromNode = a, toNode = "centre", widthM = 14, laneCount = 4, speedLimitKph = 60 }).ToArray();
            var lanes = new List<Lane>();
            foreach (var a in arms)
            {
                var others = arms.Where(o => o != a).ToArray();
                for (int i = 0; i < 2; i++)
                    lanes.Add(new Lane { id = a + "-centre:0:" + i, segmentId = a + "-centre", fromNode = a, toNode = "centre", widthM = 3.5f, index = i,
                        successors = others.Select(o => o + "-centre:1:" + i).ToArray() });
                for (int i = 0; i < 2; i++)
                    lanes.Add(new Lane { id = a + "-centre:1:" + i, segmentId = a + "-centre", fromNode = "centre", toNode = a, widthM = 3.5f, index = i });
            }
            w.lanes = lanes.ToArray();
            w.objects = new[] { new WorldObject { id = "spawn", catalogId = "spawn-car", x = -1.75, y = 0.1, z = -40 } };
            w.districts = new[] { new District { id = "demo", minX = -250, minZ = -250, sizeM = 500 } };
            return w;
        }

        public static WorldDocument SingleStraightV1(double length = 100)
        {
            return new WorldDocument
            {
                id = "straight",
                nodes = new[] { Node("a", 0, 0), Node("b", 0, length) },
                segments = new[] { new RoadSegment { id = "ab", fromNode = "a", toNode = "b", widthM = 7, laneCount = 2, speedLimitKph = 60 } },
                lanes = new[]
                {
                    new Lane { id = "ab:f", segmentId = "ab", fromNode = "a", toNode = "b", widthM = 3.5f, index = 0 },
                    new Lane { id = "ab:b", segmentId = "ab", fromNode = "b", toNode = "a", widthM = 3.5f, index = 1 },
                },
            };
        }

        public static WorldDocumentV2 ExampleV2() => WorldMigration.MigrateV1ToV2(ExampleV1());

        public static RoadNode Node(string id, double x, double z) => new RoadNode { id = id, x = x, z = z };

        public static LaneConnection Connection(WorldDocumentV2 w, string from, string to) =>
            w.connections.Single(c => c.fromLaneId == from && c.toLaneId == to);

        public static T[] Append<T>(T[] array, T item) => array.Concat(new[] { item }).ToArray();
    }
}
