using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Simulation.RoadGraph;

namespace DrivingSchool.Tests
{
    /// <summary>Layouts built with the current Road Kit v1 (straight 20 m, curve R14, cross 24 m).</summary>
    static class CityLayouts
    {
        public static readonly RoadKitTemplates Kit = new RoadKitTemplates();
        public const string Straight = "RK_Road_Urban_20m", Curve = "RK_Road_Curve90_R14", Cross = "RK_Road_Cross_24m";
        public static readonly string[] Arms = { "North", "South", "East", "West" };

        public static ModuleTemplate T(string id) { Kit.TryGet(id, out var t); return t; }

        /// <summary>Cross "c" at the origin with <paramref name="straights"/> straights on every arm, arm ends open.</summary>
        public static DistrictLayout CrossWithArms(int straights = 2)
        {
            var layout = new DistrictLayout { id = "test-cross", name = "Перекрёсток с рукавами" };
            var cross = new ModuleInstance { id = "c", catalogId = Cross };
            var instances = new List<ModuleInstance> { cross };
            var joins = new List<SocketJoin>(); var open = new List<SocketRef>();
            foreach (var arm in Arms)
            {
                ModuleInstance prev = cross; string prevSocket = "Socket_" + arm;
                for (int i = 0; i < straights; i++)
                {
                    var id = arm.ToLowerInvariant() + i;
                    var s = DistrictCompiler.Dock(id, T(Straight), "Socket_End", prev, T(prev.catalogId), prevSocket);
                    instances.Add(s);
                    joins.Add(new SocketJoin { instanceA = prev.id, socketA = prevSocket, instanceB = id, socketB = "Socket_End" });
                    prev = s; prevSocket = "Socket_Start";
                }
                open.Add(new SocketRef { instanceId = prev.id, socket = prevSocket });
            }
            layout.instances = instances.ToArray(); layout.joins = joins.ToArray(); layout.openSockets = open.ToArray();
            return layout;
        }

        /// <summary>n straights in a row along +Z from the origin, both ends open.</summary>
        public static DistrictLayout StraightChain(int n)
        {
            var first = new ModuleInstance { id = "s0", catalogId = Straight };
            var instances = new List<ModuleInstance> { first }; var joins = new List<SocketJoin>();
            for (int i = 1; i < n; i++)
            {
                var s = DistrictCompiler.Dock("s" + i, T(Straight), "Socket_Start", instances[i - 1], T(Straight), "Socket_End");
                instances.Add(s);
                joins.Add(new SocketJoin { instanceA = "s" + (i - 1), socketA = "Socket_End", instanceB = s.id, socketB = "Socket_Start" });
            }
            return new DistrictLayout
            {
                id = "chain", instances = instances.ToArray(), joins = joins.ToArray(),
                openSockets = new[] { new SocketRef { instanceId = "s0", socket = "Socket_Start" }, new SocketRef { instanceId = "s" + (n - 1), socket = "Socket_End" } },
            };
        }

        /// <summary>Forward lanes of a straight chain, s0/f .. s(n-1)/f.</summary>
        public static string[] ForwardRoute(int n) => Enumerable.Range(0, n).Select(i => "s" + i + "/f").ToArray();

        public static LayoutSignalPlan TwoPhasePlan(string instanceId = "c") => new LayoutSignalPlan
        {
            instanceId = instanceId,
            stages = new[]
            {
                new LayoutSignalStage { greenSockets = new[] { "Socket_North", "Socket_South" }, walkSockets = new[] { "Socket_East", "Socket_West" }, greenSeconds = 18, greenFlashSeconds = 3, amberSeconds = 3, allRedSeconds = 2, redAmberSeconds = 1 },
                new LayoutSignalStage { greenSockets = new[] { "Socket_East", "Socket_West" }, walkSockets = new[] { "Socket_North", "Socket_South" }, greenSeconds = 18, greenFlashSeconds = 3, amberSeconds = 3, allRedSeconds = 2, redAmberSeconds = 1 },
            },
        };

        public static WorldDocumentV2 Compile(DistrictLayout layout) => DistrictCompiler.Compile(layout, Kit);
    }
}
