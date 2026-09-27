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
        const string Straight = "RK_Road_Urban_20m", Curve = "RK_Road_Curve90_R14", Cross = "RK_Road_Cross_24m";

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
            X42 = RoadKitTemplatesV2.Cross4x2, Ring = RoadKitTemplatesV2.Roundabout, Rail = RoadKitTemplatesV2.RailCrossing;

        /// <summary>Instance of the level crossing and the lane that enters the district from road A.</summary>
        public const string RailInstance = "rs1", EntryLane = "re1/f";

        /// <summary>
        /// The district (T55/T56) with the signalled 2+2 junction X1 at <paramref name="origin"/>:
        /// arterial 2+2 north–south through X1 and X2 (2+2 × 1+1, main road N–S), X1's west and east arms 2+2,
        /// lane-change stretches between junctions; east of X2 a 1+1 street to a roundabout, whose east arm leads to road A
        /// and whose south arm crosses the railway (track west–east).
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

            // Arterial and X1's arms (2+2).
            Open(Arm("n", x1, "Socket_North", LC, S4, S4));
            Open(Arm("w", x1, "Socket_West", S4, S4, S4));
            Open(Arm("e", x1, "Socket_East", LC, S4, S4));
            var s = Arm("s", x1, "Socket_South", LC, S4);
            // X2: its main road (local E–W) continues the arterial, so it is turned: local West faces north.
            var x2 = Dock("X2", X42, "Socket_West", s, "Socket_Start");
            Open(Arm("x", x2, "Socket_East", S4));                                  // arterial south of X2
            Open(Arm("xw", x2, "Socket_South", Straight, Straight, Straight));      // 1+1 street west
            var xe = Arm("xe", x2, "Socket_North", Straight);                        // 1+1 street east to the roundabout
            var ring = Dock("R", Ring, "Socket_West", xe, "Socket_Start");
            Open(Arm("re", ring, "Socket_East", Straight, Straight));               // to road A
            Open(Arm("rn", ring, "Socket_North", Straight, Straight));
            Open(Arm("rs", ring, "Socket_South", Straight, Rail, Straight));         // over the railway

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
                Sign("main-x2-south", "2.1", null, "x0", "f2", 8),
                Sign("yield-x2-east", "2.4", null, "xe0", "f", 8),
                Sign("stop-x2-west", "2.5", null, "xw0", "f", 8),
                Sign("zebra-x2-east", "5.19.1", null, "xe0", "f", 14),
                Sign("zebra-x2-west", "5.19.1", null, "xw0", "f", 14),
                // Level crossing with barriers: warning signs on both approaches.
                Sign("rail-north", "1.1", null, "rs0", "b", 6),
                Sign("rail-south", "1.1", null, "rs2", "f", 4),
                // Odds and ends: 60 on the arterial into town, no parking on the west arm, parking on the west street.
                Sign("speed-60-north", "3.24", "60", "n2", "f2", 4),
                Sign("no-stopping-north", "3.27", null, "n0", "f2b", 1, untilJunction: true),
                Sign("no-parking-west", "3.28", null, "w1", "b2", 4),
                Sign("parking-west", "6.4", null, "xw1", "b", 10),
            };
            var approaches = new List<LayoutApproach>
            {
                new LayoutApproach { instanceId = "X2", socket = "Socket_West", priority = ApproachPriority.Main, signIds = new[] { "main-x2-north" } },
                new LayoutApproach { instanceId = "X2", socket = "Socket_East", priority = ApproachPriority.Main, signIds = new[] { "main-x2-south" } },
                new LayoutApproach { instanceId = "X2", socket = "Socket_North", priority = ApproachPriority.Secondary, signIds = new[] { "yield-x2-east" } },
                new LayoutApproach { instanceId = "X2", socket = "Socket_South", priority = ApproachPriority.Secondary, signIds = new[] { "stop-x2-west" } },
            };
            foreach (var arm in new[] { "East", "North", "West", "South" })
                approaches.Add(new LayoutApproach { instanceId = "R", socket = "Socket_" + arm, priority = ApproachPriority.Secondary, signIds = new[] { "ring-yield-" + arm.ToLowerInvariant(), "ring-" + arm.ToLowerInvariant() } });
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
            return new DistrictLayout
            {
                id = "test-range-district", name = "Городской район полигона", revision = "v2",
                instances = list.ToArray(), joins = joins.ToArray(), openSockets = open.ToArray(),
                signs = signs.ToArray(), approaches = approaches.ToArray(), signalPlans = new[] { plan },
            };
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
                foreach (var (mesh, dz) in RoadKitTemplatesV2.Meshes(m.catalogId))
                {
                    var rot = Quaternion.Euler(0, m.yawDeg, 0);
                    var dir = mesh.StartsWith("RK2_", StringComparison.Ordinal) ? RoadKitBuilder.PrefabsV2 + "/" : RK;
                    var go = Place(dir + mesh + ".prefab", new Vector3((float)m.x, (float)m.y, (float)m.z) + rot * new Vector3(0, 0, (float)dz), m.yawDeg, roads);
                    go.name = m.id + " / " + mesh + (dz > 0 ? " (2)" : "");
                    SetLayer(go, layerGround); go.isStatic = true;
                    foreach (var ren in go.GetComponentsInChildren<Renderer>()) if (ren.enabled) r.roadRenderers.Add(ren);
                    roadBounds.Add(BoundsOf(go));
                    r.modules++;
                }
            // Signals and signs on shared poles and masts (T54).
            var furniture = StreetFurniture.Place(world, signs, heads, layerProps);
            r.signs = world.signs.Length; r.heads = furniture.heads;
            // The railway corridor (T56): track across the level crossing module, west to east over the whole district.
            r.rail = layout.instances.First(m => m.id == RailInstance);
            var railCentre = RailPoint(r.rail, 0);
            roadBounds.Add(new Bounds(new Vector3(railCentre.x, 0, railCentre.z), new Vector3(600f, 1f, 9f)));
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
            so.FindProperty("maxVehicles").intValue = 10;
            so.FindProperty("maxPedestrians").intValue = 12;
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
            foreach (var (m, dz) in StraightPieces(layout))
            {
                float outer = m.catalogId == Straight ? 6.2f : 10.7f;   // outer edge of the sidewalk
                var inst = new Vector3((float)m.x, (float)m.y, (float)m.z) + Quaternion.Euler(0, m.yawDeg, 0) * new Vector3(0, 0, (float)dz);
                var rot = Quaternion.Euler(0, m.yawDeg, 0);
                foreach (int side in new[] { -1, 1 })
                {
                    var prefab = Houses + names[k++ % names.Length] + ".prefab";
                    var go = Place(prefab, Vector3.zero, m.yawDeg + (side > 0 ? 270f : 90f), parent);
                    var b = BoundsOf(go);
                    var across = rot * Vector3.right * side;
                    float halfDepth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(across.x), 0, Mathf.Abs(across.z))));
                    var target = inst + rot * new Vector3(0, 0, 10f) + across * (outer + 4f + halfDepth);
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

        /// <summary>Every 20 m straight piece (1+1 or 2+2) with its local z offset: houses and lamps stand along them.</summary>
        static IEnumerable<(ModuleInstance m, double dz)> StraightPieces(DistrictLayout layout) =>
            layout.instances.Where(x => x.catalogId == Straight || x.catalogId == S4 || x.catalogId == LC)
                .SelectMany(x => RoadKitTemplatesV2.Meshes(x.catalogId).Select(mesh => (x, mesh.z)));

        static Bounds Flat(Bounds b) => new Bounds(new Vector3(b.center.x, 0, b.center.z), new Vector3(b.size.x, 1, b.size.z));

        static void PlaceLamps(DistrictLayout layout, Transform parent, int layer)
        {
            int i = 0;
            foreach (var (m, dz) in StraightPieces(layout))
            {
                if (i++ % 2 == 1) continue;
                var rot = Quaternion.Euler(0, m.yawDeg, 0);
                float kerb = m.catalogId == Straight ? 5.4f : 8.1f;
                var pos = new Vector3((float)m.x, (float)m.y + SidewalkTop, (float)m.z) + rot * new Vector3(kerb, 0f, 10f + (float)dz);
                // The arm of TK_Lamp_7m points along local +Z: turn it over the road (towards −X of the module).
                var go = Place(Kit + "TK_Lamp_7m.prefab", pos, m.yawDeg - 90f, parent);
                SetLayer(go, layer); go.isStatic = true;
            }
            // Junctions and the roundabout (T64): a lamp on every corner, its arm towards the centre.
            foreach (var m in layout.instances)
            {
                IEnumerable<Vector2> corners =
                    m.catalogId == X4 ? new[] { new Vector2(13.5f, 13.5f) } :
                    m.catalogId == X42 ? new[] { new Vector2(10.5f, 13.5f) } :
                    m.catalogId == Ring ? new[] { new Vector2(10.7f, 10.7f) } : new Vector2[0];
                var rot = Quaternion.Euler(0, m.yawDeg, 0);
                var centre = new Vector3((float)m.x, (float)m.y + SidewalkTop, (float)m.z);
                foreach (var c in corners)
                    foreach (var (sx, sz) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) })
                    {
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
