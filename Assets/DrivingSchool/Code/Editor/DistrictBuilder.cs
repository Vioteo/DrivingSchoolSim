using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation;
using DrivingSchool.Simulation.RoadGraph;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Town district of the vehicle test range (T51, first cut of T30): a layout of Road Kit modules → DistrictCompiler →
    /// graph (Data/City/test-range-district.world.json) → scene. Everything that has rules in it is placed from the graph:
    /// signs from SignPlacement, signal heads from the attachments (named by id, the traffic host finds them by name).
    /// Two junctions: A — traffic lights with pedestrian signals; B — main road E–W (2.1) and secondary N–S (2.4 / 2.5),
    /// zebra crossings (5.19.1). Arms end open: traffic enters and leaves there, the east arm is the entrance from road A.
    /// Called by VehicleTestRangeBuilder; does not save scenes itself.
    /// </summary>
    public static class DistrictBuilder
    {
        public const string DataDir = "Assets/DrivingSchool/Data/City";
        public const string LayoutPath = DataDir + "/test-range-district.layout.json";
        public const string WorldPath = DataDir + "/test-range-district.world.json";
        const string RK = "Assets/DrivingSchool/Prefabs/RoadKit/";
        const string Traffic = "Assets/DrivingSchool/Prefabs/Traffic/";
        const string Houses = "Assets/DrivingSchool/Prefabs/Houses/";
        const string Kit = "Assets/DrivingSchool/Prefabs/TrainingKit/";
        const string Pedestrians = "Assets/DrivingSchool/Prefabs/Pedestrians/";
        const float SidewalkTop = 0.15f;
        const string Straight = "RK_Road_Urban_20m", Curve = "RK_Road_Curve90_R14", Cross = "RK_Road_Cross_24m", Zebra = RoadKitTemplates.CrosswalkId,
            ZebraPlain = RoadKitTemplates.ZebraPlainId;
        const string SpeedBump = "Assets/DrivingSchool/Art/SpeedBumps/DS_SpeedBump_Rubber_7m.fbx";
        const string Ramp = RoadKitTemplates.RampId, Bridge = RoadKitTemplates.BridgeId;

        public sealed class Result
        {
            public Transform root; public TrafficDirectorHost traffic; public Transform entrySpawn;
            public WorldDocumentV2 world; public readonly List<Renderer> roadRenderers = new List<Renderer>();
            public int modules, signs, heads, houses;
            public ModuleInstance rail;
        }

        // ------------------------------------------------------------------ layout

        static readonly RoadKitCatalog Kit_ = new RoadKitCatalog();
        static ModuleTemplate T(string id) { Kit_.TryGet(id, out var t); return t; }
        const string S4 = RoadKitTemplatesV2.Straight4, LC = RoadKitTemplatesV2.LaneChange4, X4 = RoadKitTemplatesV2.Cross4x4,
            X42 = RoadKitTemplatesV2.Cross4x2, Ring = RoadKitTemplatesV2.Roundabout, Rail = RoadKitTemplatesV2.RailCrossing,
            Plain = RoadKitTemplatesV2.CrossPlain, Tee = RoadKitTemplatesV2.Tee, Curve4 = RoadKitTemplatesV2.Curve4, Zebra4 = RoadKitTemplatesV2.Crosswalk4;

        /// <summary>Instance of the level crossing and the lane that enters the district from road A.</summary>
        public const string RailInstance = "rs1", EntryLane = "re1/f";

        /// <summary>
        /// The district (T55/T56, T65; closed into loops in T66) with the signalled 2+2 junction X1 at <paramref name="origin"/>.
        /// Light-controlled X1 and X3 (2+2 × 2+2, zebras with pedestrian lights at the junction); unregulated X2, X4, X10
        /// (2+2 × 1+1, main road 2.1, the side streets 2.4/2.5) and T junctions X5, X6, X7 have no zebras — the streets have
        /// mid-block zebras instead (5.19.1). Loops: X1–X3–X5–X2; X4–X6 over the top U; the outer 2+2 loop X7 → north → west
        /// → X10 (on the bump street) → X3; the bottom U X5–X2 just north of the railway; the south road from the foot of the
        /// overpass east to the level crossing south of the roundabout. The only open end is the entrance from road A.
        /// </summary>
        public static DistrictLayout Layout(Vector3 origin)
        {
            var list = new List<ModuleInstance>(); var joins = new List<SocketJoin>(); var open = new List<SocketRef>();
            var x1 = new ModuleInstance { id = "X1", catalogId = X4, x = origin.x, y = origin.y, z = origin.z };
            list.Add(x1);
            ModuleInstance Dock(string id, string catalog, string socket, ModuleInstance prev, string prevSocket)
            {
                var m = DistrictCompiler.Dock(id, T(catalog), socket, prev, T(prev.catalogId), prevSocket);
                list.Add(m); joins.Add(new SocketJoin { instanceA = prev.id, socketA = prevSocket, instanceB = id, socketB = socket });
                return m;
            }
            // A chain of road pieces: each docks its Socket_End to the previous module and leaves through Socket_Start,
            // so the "f" lanes of every piece run towards the junction the chain starts from.
            ModuleInstance Arm(string prefix, ModuleInstance from, string socket, params string[] pieces)
            {
                var prev = from; string s = socket;
                for (int i = 0; i < pieces.Length; i++) { prev = Dock(prefix + i, pieces[i], "Socket_End", prev, s); s = "Socket_Start"; }
                return prev;
            }
            void Open(ModuleInstance m, string socket = "Socket_Start") => open.Add(new SocketRef { instanceId = m.id, socket = socket });
            void Join(ModuleInstance a, string sa, ModuleInstance b, string sb) => joins.Add(new SocketJoin { instanceA = a.id, socketA = sa, instanceB = b.id, socketB = sb });
            Vec3d At(ModuleInstance m, string socket) => DistrictCompiler.SocketPosition(m, T(m.catalogId).Socket(socket));
            // Distance from a socket straight ahead (along its out heading) to a point; the point must lie on that line.
            double Ahead(ModuleInstance m, string socket, Vec3d target)
            {
                var p = At(m, socket); double h = DistrictCompiler.SocketHeading(m, T(m.catalogId).Socket(socket)) * Math.PI / 180;
                double dx = target.x - p.x, dz = target.z - p.z, along = dx * Math.Sin(h) + dz * Math.Cos(h), off = Math.Abs(dx * Math.Cos(h) - dz * Math.Sin(h));
                if (off > 0.05) throw new InvalidOperationException($"District layout: {m.id}:{socket} is {off:F2} m off the line to ({target.x:F1}, {target.z:F1})");
                return along;
            }
            // A walk along a road: pieces dock by Socket_End and leave by Socket_Start (a curve taken this way turns left).
            ModuleInstance piece = null; string leave = null;
            void From(ModuleInstance m, string socket) { piece = m; leave = socket; }
            ModuleInstance Next(string id, string catalog, string dockBy = "Socket_End", string leaveBy = "Socket_Start")
            { piece = Dock(id, catalog, dockBy, piece, leave); leave = leaveBy; return piece; }
            // A straight up to a point straight ahead (stretched unless a whole 20 m), then optionally joined to a socket there.
            ModuleInstance Reach(string id, string baseId, Vec3d target)
            {
                double len = Ahead(piece, leave, target);
                return Next(id, Math.Abs(len - 20) < 1e-6 ? baseId : RoadKitTemplates.Stretched(baseId, len));
            }
            void Close(ModuleInstance m, string socket) => Join(piece, leave, m, socket);
            void Fill(string id, string baseId, ModuleInstance m, string socket) { Reach(id, baseId, At(m, socket)); Close(m, socket); }
            const double R4 = RoadKitTemplatesV2.Curve4RadiusM, R1 = RoadKitTemplates.CurveRadiusM;

            // Arterial and X1's arms (2+2).
            var n = Arm("n", x1, "Socket_North", LC);                                  // to X6
            var w = Arm("w", x1, "Socket_West", S4, S4, S4);                           // to X3
            var s = Arm("s", x1, "Socket_South", LC, S4);
            // X2: its main road (local E–W) continues the arterial, so it is turned: local West faces north.
            var x2 = Dock("X2", Plain, "Socket_West", s, "Socket_Start");
            var xw = Arm("xw", x2, "Socket_South", Straight, ZebraPlain, Straight);   // 1+1 street west to X5, a zebra mid-block
            var xe = Arm("xe", x2, "Socket_North", Straight);                        // 1+1 street east to the roundabout
            var ring = Dock("R", Ring, "Socket_West", xe, "Socket_Start");
            Open(Arm("re", ring, "Socket_East", Straight, Straight));               // to road A: the only open end
            var rs = Arm("rs", ring, "Socket_South", Straight, Rail, Straight);      // over the railway, on to the south road

            // West part (T65). X3 92 m west of X1; X4 68 m north of X3, X5 88 m south of it (like X2: local West faces north).
            var x3 = Dock("X3", X4, "Socket_East", w, "Socket_Start");
            var x4 = Dock("X4", Plain, "Socket_East", Arm("m", x3, "Socket_North", S4, S4), "Socket_Start");
            var x5 = Dock("X5", Tee, "Socket_West", Arm("q", x3, "Socket_South", S4, Zebra4, S4), "Socket_Start");
            Join(xw, "Socket_Start", x5, "Socket_North");
            // X6 (T66): a T on the arterial 68 m north of X1, level with X4; the short street from X4 ends there.
            var x6 = Dock("X6", Tee, "Socket_West", n, "Socket_Start");
            Join(Arm("h", x4, "Socket_North", Straight, ZebraPlain, Straight), "Socket_Start", x6, "Socket_North");
            // Top U (T66): X4's north arm turns east, runs 40 m and turns south into X6 (walked with the "f" lanes).
            From(x4, "Socket_West");
            Next("k0", Curve4, "Socket_Start", "Socket_End"); Next("k1", S4, "Socket_Start", "Socket_End");
            Next("k2", S4, "Socket_Start", "Socket_End"); Next("k3", Curve4, "Socket_Start", "Socket_End");
            Close(x6, "Socket_East");
            // X7 (T66): a T on X1's east arm right above the roundabout; the ring's north arm comes up to it.
            From(x1, "Socket_East");
            var e = Reach("e0", S4, new Vec3d(ring.x - 12, 0, x1.z));                 // X7's west socket is 12 m from its centre
            var x7 = Dock("X7", Tee, "Socket_East", e, "Socket_Start");
            From(ring, "Socket_North"); Fill("rn0", Straight, x7, "Socket_North");

            // Bump street (T65) west of X4, crossed by the outer loop at X10 (T66); its zebra between the bumps is b4.
            var x10 = Dock("X10", Plain, "Socket_North", Arm("b", x4, "Socket_South", Straight, Straight), "Socket_Start");  // local North faces east
            From(x10, "Socket_South");
            for (int i = 2; i <= 8; i++) Next("b" + i, i == 4 ? Zebra : Straight);
            // Overpass over the railway (T65): a left turn to the south, four ramps up (8 %, 6.4 m), three spans on columns
            // over the track, four ramps down.
            Next("o0", Curve);
            for (int i = 1; i <= 5; i++) Next("o" + i, Straight);
            for (int i = 0; i < 4; i++) Next("up" + i, Ramp, "Socket_Start", "Socket_End");
            for (int i = 0; i < 3; i++) Next("br" + i, Bridge, "Socket_Start", "Socket_End");
            for (int i = 0; i < 4; i++) Next("dn" + i, Ramp, "Socket_End", "Socket_Start");
            Next("o6", Straight);
            // South road (T66): from the foot of the overpass east, a zebra half way, then north to the level crossing.
            Next("so0", Curve);                                                        // left, to the east
            var southEnd = new Vec3d(ring.x - R1, 0, At(piece, leave).z);
            double south = Ahead(piece, leave, southEnd);
            Next("so1", RoadKitTemplates.Stretched(Straight, (south - 20) / 2)); Next("so2", ZebraPlain); Reach("so3", Straight, southEnd);
            Next("so4", Curve);                                                        // left, to the north
            Fill("so5", Straight, rs, "Socket_Start");

            // Outer loop (T66), 2+2 at 40 km/h: from X7 east, north, west over the top of the town, south through X10, east into X3.
            From(x7, "Socket_West");
            Next("ol0", Curve4);                                                       // left, to the north
            double top = x4.z + 72;
            Reach("ol1", S4, new Vec3d(At(piece, leave).x, 0, top - R4));
            Next("ol2", Curve4);                                                       // left, to the west
            var westEnd = new Vec3d(x10.x + R4, 0, At(piece, leave).z);
            double across = Ahead(piece, leave, westEnd);
            Next("ol3", RoadKitTemplates.Stretched(S4, (across - 20) / 2)); Next("ol4", Zebra4); Reach("ol5", S4, westEnd);
            Next("ol6", Curve4);                                                       // left, to the south
            Fill("ol7", S4, x10, "Socket_West");
            From(x10, "Socket_East");
            Reach("ol8", S4, new Vec3d(x10.x, 0, x3.z + R4));
            Next("ol9", Curve4);                                                       // left, to the east
            Fill("ol10", S4, x3, "Socket_West");

            // Bottom U (T66): X5's south arm turns east just north of the railway, a zebra, and comes up into X2.
            From(x5, "Socket_East");
            Next("u0", Curve4); Next("u1", Zebra4);                                   // left, to the east
            Reach("u2", S4, new Vec3d(x2.x - R4, 0, At(piece, leave).z));
            Next("u3", Curve4); Close(x2, "Socket_East");                             // left, to the north

            var byId = list.ToDictionary(m => m.id);
            float Before(string id, double metres) => (float)(LengthOf(byId[id].catalogId) - metres);   // s on the lane running to the far end
            var signs = new List<LayoutSign>
            {
                // Entrance from road A (east arm of the roundabout, far end): town, speed limit.
                Sign("town-entry", "5.23.1", null, "re1", "f", 2),
                Sign("speed-40-east", "3.24", "40", "re1", "f", 7),
                Sign("town-exit", "5.24.1", null, "re1", "b", 17),
                // Roundabout: on every entry 4.3 with 2.4 (the ring goes first) and a zebra.
                Sign("ring-yield-east", "2.4", null, "R", "East.in", 2), Sign("ring-east", "4.3", null, "R", "East.in", 2.4f),
                Sign("ring-yield-north", "2.4", null, "R", "North.in", 2), Sign("ring-north", "4.3", null, "R", "North.in", 2.4f),
                Sign("ring-yield-west", "2.4", null, "R", "West.in", 2), Sign("ring-west", "4.3", null, "R", "West.in", 2.4f),
                Sign("ring-yield-south", "2.4", null, "R", "South.in", 2), Sign("ring-south", "4.3", null, "R", "South.in", 2.4f),
                Sign("zebra-ring-east", "5.19.1", null, "re0", "f", 17),
                Sign("zebra-ring-south", "5.19.1", null, "rs0", "f", 17),
                // X2: main road N–S (2.1), the 1+1 street gives way (2.4 from the east, 2.5 from the west).
                Sign("main-x2-north", "2.1", null, "s1", "b2", 8),
                Sign("main-x2-south", "2.1", null, "u3", "b2", 40),
                Sign("yield-x2-east", "2.4", null, "xe0", "f", 8),
                Sign("stop-x2-west", "2.5", null, "xw0", "f", 8),
                // Level crossing with barriers: warning signs on both approaches.
                Sign("rail-north", "1.1", null, "rs0", "b", 6),
                Sign("rail-south", "1.1", null, "rs2", "f", 4),
                // Odds and ends: 60 on the arterial into town, no stopping on it, no parking on the west arm, parking on the west street.
                Sign("speed-60-north", "3.24", "60", "n0", "f2a", 2),
                Sign("no-stopping-north", "3.27", null, "n0", "f2b", 1, untilJunction: true),
                Sign("no-parking-west", "3.28", null, "w1", "b2", 4),
                Sign("parking-west", "6.4", null, "xw0", "b", 4),

                // West part (T65). X3: lane directions — from the east only left from the inner lane, from the west left from both.
                Sign("lanes-x3-east", "5.15.1", "L|SR", "X3", "East.in2", 0.5f),
                Sign("lanes-x3-west", "5.15.1", "L|LSR", "X3", "West.in2", 0.5f),
                // Top U between X4 and X6: 30 km/h both ways.
                Sign("north-30", "3.24", "30", "k1", "f2", 3), Sign("north-30-back", "3.24", "30", "k2", "b2", 3),
                // X4, X10 (main road N–S) and the T junctions X5, X6 (main road N–S), X7 (main road E–W): 2.1, side streets 2.4.
                Sign("main-x4-south", "2.1", null, "m1", "b2", 8), Sign("main-x4-north", "2.1", null, "k0", "b2", 40),
                Sign("yield-x4-west", "2.4", null, "b0", "f", 8), Sign("yield-x4-east", "2.4", null, "h0", "f", 8),
                Sign("main-x5-north", "2.1", null, "q2", "b2", 8), Sign("main-x5-south", "2.1", null, "u0", "f2", 24),
                Sign("yield-x5-east", "2.4", null, "xw2", "b", 8),
                Sign("main-x6-south", "2.1", null, "n0", "b2b", 2), Sign("main-x6-north", "2.1", null, "k3", "f2", 24),
                Sign("yield-x6-west", "2.4", null, "h2", "b", 12),
                Sign("main-x7-west", "2.1", null, "e0", "b2", Before("e0", 8)), Sign("main-x7-east", "2.1", null, "ol0", "f2", 24),
                Sign("yield-x7-south", "2.4", null, "rn0", "b", Before("rn0", 8)),
                Sign("main-x10-north", "2.1", null, "ol7", "b2", Before("ol7", 8)), Sign("main-x10-south", "2.1", null, "ol8", "f2", Before("ol8", 8)),
                Sign("yield-x10-east", "2.4", null, "b1", "b", 12), Sign("yield-x10-west", "2.4", null, "b2", "f", 12),
                // Outer loop: 40 km/h from X7 and from X10 (a zone ends at the next junction).
                Sign("outer-40-north", "3.24", "40", "ol1", "b2", 4), Sign("outer-40-south", "3.24", "40", "ol7", "f2", 4),
                // Bump street: a zebra away from junctions (b4) between two bumps; 45 m before it 1.17 and 20 km/h,
                // at the bumps 5.20, at the crossing 5.19.1, 10 m after the second bump the end of the zone (3.25).
                Sign("bump-warn-w", "1.17", null, "b2", "b", 5), Sign("bump-20-w", "3.24", "20", "b2", "b", 5.4f),
                Sign("bump-info-w", "5.20", null, "b4", "b", 1.5f), Sign("zebra-bump-w", "5.19.1", null, "b4", "b", 8),
                Sign("bump-end-w", "3.25", "20", "b5", "b", 8),
                Sign("bump-warn-e", "1.17", null, "b6", "f", 5), Sign("bump-20-e", "3.24", "20", "b6", "f", 5.4f),
                Sign("bump-info-e", "5.20", null, "b4", "f", 1.5f), Sign("zebra-bump-e", "5.19.1", null, "b4", "f", 8),
                Sign("bump-end-e", "3.25", "20", "b3", "f", 8),
                // Overpass: 40 km/h up and down both ways.
                Sign("overpass-40-s", "3.24", "40", "o4", "b", 5), Sign("overpass-40-n", "3.24", "40", "o6", "f", 5),
            };
            // Mid-block zebras (T66): 5.19.1 at the crossing on both sides of the street.
            foreach (var (id, f, b) in new[] { ("xw1", "f", "b"), ("h1", "f", "b"), ("so2", "f", "b"), ("q1", "f2", "b2"), ("ol4", "f2", "b2"), ("u1", "f2", "b2") })
            {
                signs.Add(Sign("zebra-" + id + "-f", "5.19.1", null, id, f, 8));
                signs.Add(Sign("zebra-" + id + "-b", "5.19.1", null, id, b, 8));
            }
            var approaches = new List<LayoutApproach>();
            void Priority(string junction, string socket, ApproachPriority p, params string[] signIds) =>
                approaches.Add(new LayoutApproach { instanceId = junction, socket = socket, priority = p, signIds = signIds });
            // Junctions 2+2 × 1+1: local West/East is the main road, North/South the side street.
            foreach (var (x, west, east, north, southSign) in new[]
            {
                ("X2", "main-x2-north", "main-x2-south", "yield-x2-east", "stop-x2-west"),
                ("X4", "main-x4-north", "main-x4-south", "yield-x4-east", "yield-x4-west"),
                ("X5", "main-x5-north", "main-x5-south", "yield-x5-east", null),
                ("X6", "main-x6-south", "main-x6-north", "yield-x6-west", null),
                ("X7", "main-x7-east", "main-x7-west", "yield-x7-south", null),
                ("X10", "main-x10-north", "main-x10-south", "yield-x10-east", "yield-x10-west"),
            })
            {
                Priority(x, "Socket_West", ApproachPriority.Main, west);
                Priority(x, "Socket_East", ApproachPriority.Main, east);
                Priority(x, "Socket_North", ApproachPriority.Secondary, north);
                if (southSign != null) Priority(x, "Socket_South", ApproachPriority.Secondary, southSign);
            }
            foreach (var arm in new[] { "East", "North", "West", "South" })
                Priority("R", "Socket_" + arm, ApproachPriority.Secondary, "ring-yield-" + arm.ToLowerInvariant(), "ring-" + arm.ToLowerInvariant());
            // X1: two phases, pedestrians cross the arms parallel to the green flow.
            var plan = new LayoutSignalPlan
            {
                instanceId = "X1",
                stages = new[]
                {
                    new LayoutSignalStage { greenSockets = new[] { "Socket_North", "Socket_South" }, walkSockets = new[] { "Socket_East", "Socket_West" }, greenSeconds = 22, greenFlashSeconds = 3, amberSeconds = 3, allRedSeconds = 2, redAmberSeconds = 1 },
                    new LayoutSignalStage { greenSockets = new[] { "Socket_East", "Socket_West" }, walkSockets = new[] { "Socket_North", "Socket_South" }, greenSeconds = 18, greenFlashSeconds = 3, amberSeconds = 3, allRedSeconds = 2, redAmberSeconds = 1 },
                },
            };
            // X3 (T65): the same two phases, shifted so the two light-controlled junctions do not switch together.
            var planX3 = new LayoutSignalPlan
            {
                instanceId = "X3", offsetSeconds = 12,
                stages = plan.stages.Select(st => new LayoutSignalStage
                {
                    greenSockets = st.greenSockets, walkSockets = st.walkSockets, greenSeconds = st.greenSeconds, greenFlashSeconds = st.greenFlashSeconds,
                    amberSeconds = st.amberSeconds, allRedSeconds = st.allRedSeconds, redAmberSeconds = st.redAmberSeconds,
                }).ToArray(),
            };
            return new DistrictLayout
            {
                id = "test-range-district", name = "Городской район полигона", revision = "v4",
                instances = list.ToArray(), joins = joins.ToArray(), openSockets = open.ToArray(),
                signs = signs.ToArray(), approaches = approaches.ToArray(), signalPlans = new[] { plan, planX3 },
            };
        }

        /// <summary>Length of a straight-like module along its road: stretched straights carry it in the id, the rest are 20 m.</summary>
        static double LengthOf(string catalogId)
        {
            foreach (var b in new[] { S4, Straight })
                if (RoadKitTemplates.TryStretch(catalogId, b, out double length)) return length;
            return 20;
        }

        static LayoutSign Sign(string id, string code, string value, string instance, string lane, float atS, bool untilJunction = false) =>
            new LayoutSign { id = id, code = code, value = value, instanceId = instance, laneId = lane, atS = atS, untilNextJunction = untilJunction };

        // ------------------------------------------------------------------ scene

        /// <summary>Compiles the layout, writes layout and graph JSON, builds the district under <paramref name="parent"/>.</summary>
        public static Result Build(Vector3 origin, Transform parent, int layerGround, int layerProps, GameObject[] trafficPrefabs)
        {
            var layout = Layout(origin);
            var world = DistrictCompiler.Compile(layout, Kit_);   // throws before anything is written
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(LayoutPath, JsonUtility.ToJson(layout, true));
            File.WriteAllText(WorldPath, JsonUtility.ToJson(world, true));
            AssetDatabase.ImportAsset(LayoutPath); AssetDatabase.ImportAsset(WorldPath);

            var r = new Result { world = world };
            r.root = new GameObject("06 / TOWN DISTRICT (U)").transform; r.root.SetParent(parent, false);
            var roads = Child(r.root, "Roads"); var signs = Child(r.root, "Signs"); var heads = Child(r.root, "Signals");
            var houses = Child(r.root, "Houses"); var lamps = Child(r.root, "Lamps");

            var roadBounds = new List<Bounds>();
            foreach (var m in layout.instances)
                foreach (var (mesh, dz, scale) in RoadKitTemplatesV2.MeshPieces(m.catalogId))
                {
                    var rot = Quaternion.Euler(0, m.yawDeg, 0);
                    var dir = mesh.StartsWith("RK2_", StringComparison.Ordinal) ? RoadKitBuilder.PrefabsV2 + "/"
                        : mesh.StartsWith("RK3_", StringComparison.Ordinal) ? RoadKitBuilder.PrefabsV3 + "/"
                        : mesh == Ramp || mesh == Bridge ? RoadKitBuilder.PrefabsOverpass + "/" : RK;
                    var go = Place(dir + mesh + ".prefab", new Vector3((float)m.x, (float)m.y, (float)m.z) + rot * new Vector3(0, 0, (float)dz), m.yawDeg, roads);
                    // A stretched straight (T66): the last piece is scaled along the road to close the loop exactly.
                    if (Math.Abs(scale - 1) > 1e-6) go.transform.localScale = new Vector3(1f, 1f, (float)scale);
                    go.name = m.id + " / " + mesh + (dz > 0 ? " (" + (int)(dz / 20 + 1) + ")" : "") + (Math.Abs(scale - 1) > 1e-6 ? " ×" + scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "");
                    SetLayer(go, layerGround); go.isStatic = true;
                    foreach (var ren in go.GetComponentsInChildren<Renderer>()) if (ren.enabled) r.roadRenderers.Add(ren);
                    roadBounds.Add(BoundsOf(go));
                    r.modules++;
                }
            // Mid-block zebra modules (T65): the crossing marking and a speed bump on each side, on the road layer (wheels feel it).
            foreach (var m in layout.instances.Where(x => x.catalogId == Zebra || x.catalogId == ZebraPlain))
            {
                var rot = Quaternion.Euler(0, m.yawDeg, 0); var at = new Vector3((float)m.x, (float)m.y, (float)m.z);
                var zebra = Place(RK + "RK_Marking_Zebra_8x3m.prefab", at + rot * new Vector3(0, 0, (float)(RoadKitTemplates.CrosswalkZ - 1.5)), m.yawDeg, roads);
                zebra.name = m.id + " / zebra"; SetLayer(zebra, layerGround); zebra.isStatic = true;
                if (m.catalogId != Zebra) continue;   // the plain zebra (T66) has no bumps
                foreach (var dz in new[] { -RoadKitTemplates.BumpOffsetM, RoadKitTemplates.BumpOffsetM })
                {
                    var bump = PlaceBump(at + rot * new Vector3(0, 0, (float)(RoadKitTemplates.CrosswalkZ + dz)), m.yawDeg, roads, layerGround);
                    bump.name = m.id + " / speed bump " + (dz < 0 ? "1" : "2");
                    foreach (var ren in bump.GetComponentsInChildren<Renderer>()) if (ren.enabled) r.roadRenderers.Add(ren);
                }
            }
            // Signals and signs on shared poles and masts (T54).
            var furniture = StreetFurniture.Place(world, signs, heads, layerProps);
            r.signs = world.signs.Length; r.heads = furniture.heads;
            // The railway corridor (T56): track across the level crossing module, west to east over the whole district.
            r.rail = layout.instances.First(m => m.id == RailInstance);
            var railCentre = RailPoint(r.rail, 0);
            roadBounds.Add(new Bounds(new Vector3(railCentre.x, 0, railCentre.z), new Vector3(1400f, 1f, 24f)));   // no houses within 12 m of the track
            r.houses = PlaceHouses(layout, roadBounds, houses, layerProps);
            PlaceLamps(layout, lamps, layerProps);

            // Entrance from road A: the start of the east arm's inbound lane, heading into the district.
            var entryLane = world.lanes.First(l => l.id == EntryLane);
            var line = new Polyline(entryLane.centerline);
            var p = line.PointAt(4);
            r.entrySpawn = new GameObject("SPAWN / town district").transform; r.entrySpawn.SetParent(r.root, false);
            r.entrySpawn.SetPositionAndRotation(new Vector3((float)p.x, (float)p.y + 0.02f, (float)p.z), Quaternion.Euler(0f, (float)(line.HeadingAt(4) * Mathf.Rad2Deg), 0f));

            var host = new GameObject("TRAFFIC HOST").AddComponent<TrafficDirectorHost>(); host.transform.SetParent(r.root, false);
            var so = new SerializedObject(host);
            so.FindProperty("worldJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(WorldPath);
            SetArray(so.FindProperty("vehiclePrefabs"), trafficPrefabs);
            var peds = new[] { "DS_Pedestrian_A", "DS_Pedestrian_B", "DS_Pedestrian_C", "DS_Pedestrian_Child_A", "DS_Pedestrian_Child_B" }
                .Select(x => AssetDatabase.LoadAssetAtPath<GameObject>(Pedestrians + x + ".prefab")).Where(x => x != null).ToArray();
            SetArray(so.FindProperty("pedestrianPrefabs"), peds);
            so.FindProperty("maxVehicles").intValue = 12;
            so.FindProperty("maxPedestrians").intValue = 16;
            so.FindProperty("seed").intValue = 7;
            so.FindProperty("groundMask").intValue = 1 << layerGround;
            so.ApplyModifiedPropertiesWithoutUndo();
            r.traffic = host;
            Validate(r, layerGround);
            return r;
        }

        static void SetArray(SerializedProperty prop, GameObject[] items)
        {
            prop.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        /// <summary>Houses along the straights, facade 4 m behind the sidewalk, never on another road or house.</summary>
        static int PlaceHouses(DistrictLayout layout, List<Bounds> roads, Transform parent, int layer)
        {
            var names = new[] { "DS_House_Brick5", "DS_House_Townhouse", "DS_House_Modern8", "DS_House_Cottage" };
            var taken = new List<Bounds>();
            int k = 0, placed = 0;
            foreach (var (m, dz, mid) in StraightPieces(layout))
            {
                float outer = IsNarrow(m.catalogId) ? 6.2f : 10.7f;   // outer edge of the sidewalk
                var inst = new Vector3((float)m.x, (float)m.y, (float)m.z) + Quaternion.Euler(0, m.yawDeg, 0) * new Vector3(0, 0, (float)dz);
                var rot = Quaternion.Euler(0, m.yawDeg, 0);
                foreach (int side in new[] { -1, 1 })
                {
                    var prefab = Houses + names[k++ % names.Length] + ".prefab";
                    var go = Place(prefab, Vector3.zero, m.yawDeg + (side > 0 ? 270f : 90f), parent);
                    var b = BoundsOf(go);
                    var across = rot * Vector3.right * side;
                    float halfDepth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(across.x), 0, Mathf.Abs(across.z))));
                    var target = inst + rot * new Vector3(0, 0, (float)mid) + across * (outer + 4f + halfDepth);
                    go.transform.position += new Vector3(target.x - b.center.x, inst.y + SidewalkTop - b.min.y, target.z - b.center.z);
                    var nb = BoundsOf(go); var probe = nb; probe.Expand(new Vector3(2f, 0f, 2f));
                    bool clash = roads.Any(rb => Flat(rb).Intersects(Flat(probe))) || taken.Any(tb => tb.Intersects(Flat(probe)));
                    if (clash) { Object.DestroyImmediate(go); continue; }
                    taken.Add(Flat(nb));
                    go.name = m.id + (side > 0 ? " R / " : " L / ") + go.name;
                    SetLayer(go, layer); go.isStatic = true; placed++;
                }
            }
            return placed;
        }

        /// <summary>World point of the level crossing module (local: x across the road, z along it; the rails lie at z = 10).</summary>
        public static Vector3 RailPoint(ModuleInstance rail, double localX, double localZ = 10)
        {
            var p = DistrictCompiler.ToWorld(rail, new Vec3d(localX, 0, localZ));
            return new Vector3((float)p.x, (float)p.y, (float)p.z);
        }

        /// <summary>
        /// Every straight mesh (1+1 or 2+2, stretched ones too, T66) with its local z offset and the middle of the piece:
        /// houses and lamps stand along them.
        /// </summary>
        static IEnumerable<(ModuleInstance m, double dz, double mid)> StraightPieces(DistrictLayout layout) =>
            layout.instances.Where(x => x.catalogId == Straight || x.catalogId == Zebra || x.catalogId == ZebraPlain || x.catalogId == S4 || x.catalogId == LC
                                        || x.catalogId == Zebra4 || RoadKitTemplates.TryStretch(x.catalogId, S4, out _) || RoadKitTemplates.TryStretch(x.catalogId, Straight, out _))
                .SelectMany(x => RoadKitTemplatesV2.MeshPieces(x.catalogId).Select(mesh => (x, mesh.z, mesh.z + 10 * mesh.scaleZ)));

        static bool IsNarrow(string catalogId) => catalogId == Straight || catalogId == Zebra || catalogId == ZebraPlain || RoadKitTemplates.TryStretch(catalogId, Straight, out _);

        static Bounds Flat(Bounds b) => new Bounds(new Vector3(b.center.x, 0, b.center.z), new Vector3(b.size.x, 1, b.size.z));

        static void PlaceLamps(DistrictLayout layout, Transform parent, int layer)
        {
            int i = 0;
            foreach (var (m, dz, mid) in StraightPieces(layout))
            {
                // A lamp on every 20 m piece, sides alternating (T64: one side every 40 m left dark stretches).
                int side = i++ % 2 == 0 ? 1 : -1;
                var rot = Quaternion.Euler(0, m.yawDeg, 0);
                float kerb = IsNarrow(m.catalogId) ? 5.4f : 8.1f;
                // Not on the zebra itself (T66): the lamp moves 4 m along.
                double at = m.catalogId == Zebra || m.catalogId == ZebraPlain || m.catalogId == Zebra4 ? mid + 4 : mid;
                var pos = new Vector3((float)m.x, (float)m.y + SidewalkTop, (float)m.z) + rot * new Vector3(side * kerb, 0f, (float)at);
                // The arm of TK_Lamp_7m points along local +Z: turn it over the road.
                var go = Place(Kit + "TK_Lamp_7m.prefab", pos, m.yawDeg - 90f * side, parent);
                SetLayer(go, layer); go.isStatic = true;
            }
            // Junctions and the roundabout (T64): a lamp on every corner, its arm towards the centre.
            foreach (var m in layout.instances)
            {
                IEnumerable<Vector2> corners =
                    m.catalogId == X4 ? new[] { new Vector2(13.5f, 13.5f) } :
                    m.catalogId == X42 || m.catalogId == Plain || m.catalogId == Tee ? new[] { new Vector2(10.5f, 13.5f) } :
                    m.catalogId == Ring ? new[] { new Vector2(10.7f, 10.7f) } : new Vector2[0];
                var rot = Quaternion.Euler(0, m.yawDeg, 0);
                var centre = new Vector3((float)m.x, (float)m.y + SidewalkTop, (float)m.z);
                foreach (var c in corners)
                    foreach (var (sx, sz) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) })
                    {
                        if (m.catalogId == Tee && sz < 0) continue;   // the T has no southern corners
                        var local = new Vector3(c.x * sx, 0f, c.y * sz);
                        var pos = centre + rot * local;
                        float yaw = Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg + m.yawDeg;
                        var go = Place(Kit + "TK_Lamp_7m.prefab", pos, yaw, parent);
                        go.name = m.id + " corner lamp";
                        SetLayer(go, layer); go.isStatic = true;
                    }
            }
        }

        static void Validate(Result r, int layerGround)
        {
            Physics.SyncTransforms();
            // Lanes lie on the road meshes' colliders (graph ↔ mesh), within 5 cm.
            foreach (var lane in r.world.lanes.Where((l, i) => i % 3 == 0))
            {
                var line = new Polyline(lane.centerline);
                var p = line.PointAt(line.Length / 2);
                var from = new Vector3((float)p.x, (float)p.y + 3f, (float)p.z);
                if (!Physics.Raycast(from, Vector3.down, out var hit, 6f, 1 << layerGround) || Mathf.Abs(hit.point.y - (float)p.y) > 0.05f)
                    throw new InvalidOperationException($"District: no road under lane {lane.id} at {p.x:F1}, {p.z:F1}" + (hit.collider ? $" (hit {hit.collider.name} at y={hit.point.y:F3})" : ""));
            }
            var named = r.root.GetComponentsInChildren<TrafficSignalView>(true).Select(v => v.gameObject.name.Split('#')[0]).ToHashSet();
            foreach (var h in r.world.signals) if (!named.Contains(h.id)) throw new InvalidOperationException("District: no signal head for " + h.id);
            int signObjects = r.root.GetComponentsInChildren<Transform>(true).Count(t => r.world.signs.Any(sg => t.name.StartsWith(sg.id + " / ", StringComparison.Ordinal)));
            if (signObjects != r.world.signs.Length) throw new InvalidOperationException($"District: {signObjects} sign objects for {r.world.signs.Length} signs in the graph");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Speed bump across a 1+1 road (the art kit's 7 m rubber bump), with colliders so the car's wheels ride over it.</summary>
        static GameObject PlaceBump(Vector3 pos, float yaw, Transform parent, int layer)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SpeedBump);
            if (src == null) throw new FileNotFoundException(SpeedBump);
            var o = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            // Keep the model's own axis correction (the kit's FBX root is turned −90° about X), then turn it with the road.
            var own = o.transform.rotation;
            o.transform.rotation = Quaternion.Euler(0, yaw, 0) * own;
            // Long axis across the road; sits on the road surface.
            var b = VehicleRigUtil.WorldBounds(o.transform);
            var across = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            if (Mathf.Abs(Vector3.Dot(b.size, new Vector3(Mathf.Abs(across.x), 0, Mathf.Abs(across.z)))) < Mathf.Max(b.size.x, b.size.z) * 0.9f)
            { o.transform.rotation = Quaternion.Euler(0, yaw + 90f, 0) * own; b = VehicleRigUtil.WorldBounds(o.transform); }
            if (b.size.y > 0.12f) throw new InvalidOperationException("Speed bump stands up: " + b.size + " (" + SpeedBump + ")");
            o.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
            foreach (var mf in o.GetComponentsInChildren<MeshFilter>()) if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            SetLayer(o, layer); o.isStatic = true;
            return o;
        }

        static Transform Child(Transform parent, string name) { var t = new GameObject(name).transform; t.SetParent(parent, false); return t; }

        static GameObject Place(string path, Vector3 pos, float yaw, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("No prefab " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return go;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            return b;
        }

        static void SetLayer(GameObject o, int layer) { foreach (var t in o.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }
    }
}
