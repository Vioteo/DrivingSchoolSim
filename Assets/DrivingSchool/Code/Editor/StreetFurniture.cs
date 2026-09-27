using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrivingSchool.Contracts;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Poles for signals and signs placed from the graph (T54). Real junctions share poles, and so does this:
    ///  • at the stop line of every signalled approach — a Г-shaped mast: the head for the approach at eye level and a
    ///    repeater hanging over the lane (&lt;id&gt;#over);
    ///  • at every corner — one pole with the pedestrian heads of both crossings (two directions) and the far-side repeater
    ///    for the approach that faces this corner across the junction (&lt;id&gt;#far);
    ///  • signs next to a pole are mounted on it; signs standing close together share one post.
    /// Heads and plates are the Traffic Kit models without their own post ("mounted" prefabs made here from the kit prefabs).
    /// Every head keeps its TrafficSignalView; the traffic host drives all views named &lt;attachment id&gt;[#suffix].
    /// </summary>
    public static class StreetFurniture
    {
        const string Traffic = "Assets/DrivingSchool/Prefabs/Traffic/";
        const string Mounted = "Assets/DrivingSchool/Prefabs/Traffic/Mounted/";
        const string SteelPath = "Assets/DrivingSchool/Materials/Traffic/Traffic_Steel.mat";
        // Heights above the sidewalk: pedestrian heads at eye level, vehicle heads above them, repeaters higher still; plates below.
        const float SidewalkTop = 0.15f, PoleRadius = 0.07f, BracketM = 0.22f, MastHeight = 6.6f, CornerHeight = 4.8f;
        const float VehicleHeadY = 3.4f, FarHeadY = 4.1f, OverheadHeadY = 5.45f, PedestrianHeadY = 2.9f, MastPlateY = 2.3f, CornerPlateY = 2.0f;
        const float CornerClusterM = 5.5f, SignToPoleM = 3.0f, SignGroupM = 1.6f;
        static readonly string[] PostParts = { "Post", "PostCap", "BasePlate", "AnchorBolt" };

        public sealed class Stats { public int poles, masts, heads, plates, posts; }

        sealed class Pole { public Vector3 at; public Transform root; public float nextPlateY = 2.15f; }

        public static Stats Place(WorldDocumentV2 world, Transform signsParent, Transform signalsParent, int layer)
        {
            var st = new Stats();
            var steel = AssetDatabase.LoadAssetAtPath<Material>(SteelPath);
            var poles = new List<Pole>();
            var nodes = world.nodes.ToDictionary(n => n.id);
            var junctionCentre = world.junctions.ToDictionary(j => j.id, j => V(nodes[j.nodeId].x, nodes[j.nodeId].y, nodes[j.nodeId].z));
            var groupJunction = world.signalGroups.ToDictionary(g => g.id, g => g.junctionId);

            // 1. Corner poles: pedestrian heads grouped by corner.
            var pedHeads = world.signals.Where(h => h.catalogId == "DS_Signal_Pedestrian").ToList();
            var clusters = new List<List<TrafficSignalAttachment>>();
            foreach (var h in pedHeads)
            {
                var c = clusters.FirstOrDefault(cl => cl.Any(o => Vector3.Distance(P(o), P(h)) < CornerClusterM));
                if (c == null) clusters.Add(c = new List<TrafficSignalAttachment>());
                c.Add(h);
            }
            foreach (var cl in clusters)
            {
                var centre = cl.Aggregate(Vector3.zero, (acc, h) => acc + P(h)) / cl.Count;
                var pole = NewPole(centre, CornerHeight, "Pole / corner", signalsParent, steel, layer); pole.nextPlateY = CornerPlateY; poles.Add(pole); st.poles++;
                foreach (var h in cl) { Head(pole, h.catalogId, h.id, h.yawDeg, PedestrianHeadY, 0f, layer); st.heads++; }
            }

            // 2. Masts at the stop lines, repeaters over the lane and at the far-left corner.
            foreach (var h in world.signals.Where(x => x.catalogId != "DS_Signal_Pedestrian"))
            {
                var at = P(h);
                float travel = h.yawDeg + 180f;   // the head faces the drivers: they travel the other way
                var fwd = Quaternion.Euler(0, travel, 0) * Vector3.forward; var left = Quaternion.Euler(0, travel - 90f, 0) * Vector3.forward;
                var mast = NewPole(at, MastHeight, "Mast / " + h.id, signalsParent, steel, layer); mast.nextPlateY = MastPlateY; poles.Add(mast); st.masts++;
                Head(mast, h.catalogId, h.id, h.yawDeg, VehicleHeadY, 0f, layer); st.heads++;
                // Arm over the lanes: from the kerb side (3.3–3.4 m right of the outer lane centre) to just past the inner
                // lane centre, a repeater over every lane of the approach (T55: 2+2 has two).
                int lanes = Math.Max(1, world.connections.Where(c => c.signalGroupId == h.signalGroupId).Select(c => c.fromLaneId).Distinct().Count());
                float reach = 3.9f + (lanes - 1) * 3.5f;
                Arm(mast.root, at + Vector3.up * (MastHeight - 0.35f), left, reach, steel, layer);
                for (int k = 0; k < lanes; k++)
                {
                    var over = Head(null, h.catalogId, h.id + "#over" + (k == 0 ? "" : (k + 1).ToString()), h.yawDeg, 0f, 0f, layer);
                    over.SetParent(mast.root, true);
                    over.position = at + left * (3.45f + k * 3.5f) + Vector3.up * OverheadHeadY - fwd * 0.05f;
                    st.heads++;
                }
                // Far side: the corner diagonally ahead-left of the driver, across the junction.
                if (groupJunction.TryGetValue(h.signalGroupId, out var jid) && junctionCentre.TryGetValue(jid, out var centre) && poles.Count > 0)
                {
                    float diag = lanes > 1 ? 9.5f : 7f;
                    var target = centre + fwd * diag + left * diag;
                    var corner = poles.Where(p => p.root.name.StartsWith("Pole / corner")).OrderBy(p => Vector3.Distance(Flat(p.at), Flat(target))).FirstOrDefault();
                    if (corner != null && Vector3.Distance(Flat(corner.at), Flat(target)) < 5.5f)
                    { Head(corner, h.catalogId, h.id + "#far", h.yawDeg, FarHeadY, 0f, layer); st.heads++; }
                }
            }

            // 3. Signs: on a nearby pole, else grouped on shared posts.
            var free = new List<SignPlacement>();
            foreach (var s in world.signs)
            {
                var pole = poles.OrderBy(p => Vector3.Distance(Flat(p.at), Flat(P(s)))).FirstOrDefault();
                if (pole != null && Vector3.Distance(Flat(pole.at), Flat(P(s))) < SignToPoleM && pole.nextPlateY > 1.9f)
                {
                    Plate(pole, s, layer); st.plates++;
                }
                else free.Add(s);
            }
            var groups = new List<List<SignPlacement>>();
            foreach (var s in free)
            {
                var g = groups.FirstOrDefault(gr => gr.Any(o => Vector3.Distance(Flat(P(o)), Flat(P(s))) < SignGroupM && Mathf.Abs(Mathf.DeltaAngle(o.yawDeg, s.yawDeg)) < 30f));
                if (g == null) groups.Add(g = new List<SignPlacement>());
                g.Add(s);
            }
            foreach (var g in groups)
            {
                if (g.Count == 1)
                {
                    var s = g[0];
                    var go = Place(Traffic + s.catalogId + ".prefab", P(s) + Vector3.up * SidewalkTop, s.yawDeg, signsParent);
                    go.name = SignName(s); SetLayer(go, layer); st.posts++;
                    continue;
                }
                // Priority first (top), then the others in layout order.
                var ordered = g.OrderBy(x => x.code.StartsWith("2.") ? 0 : 1).ToList();
                var centre = ordered.Aggregate(Vector3.zero, (acc, x) => acc + P(x)) / ordered.Count;
                var post = NewPole(centre, 2.35f + 0.72f * ordered.Count, "Post / " + string.Join(" + ", ordered.Select(x => x.code)), signsParent, steel, layer, 0.04f);
                post.nextPlateY = post.root.GetChild(0).localScale.y * 2f - 0.4f;
                foreach (var s in ordered) { Plate(post, s, layer); st.plates++; }
                st.posts++;
            }
            return st;
        }

        static string SignName(SignPlacement s) => s.id + " / " + s.code + (string.IsNullOrEmpty(s.value) ? "" : " " + s.value);

        // ------------------------------------------------------------------ mounting

        static Transform Head(Pole pole, string catalogId, string name, float yaw, float y, float side, int layer)
        {
            var prefab = MountedPrefab(catalogId);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            var view = go.GetComponentInChildren<TrafficSignalView>();
            if (view != null && view.gameObject != go) view.gameObject.name = name;
            var rot = Quaternion.Euler(0, yaw, 0);
            if (pole != null)
            {
                go.transform.SetParent(pole.root, false);
                // On a short bracket: heads facing different ways on one pole do not touch.
                go.transform.SetPositionAndRotation(pole.at + rot * new Vector3(side, 0, PoleRadius + BracketM) + Vector3.up * y, rot);
                var bracket = Cylinder("Bracket", pole.root, AssetDatabase.LoadAssetAtPath<Material>(SteelPath), layer);
                bracket.position = pole.at + rot * new Vector3(side, 0, (PoleRadius + BracketM) / 2) + Vector3.up * y;
                bracket.rotation = rot * Quaternion.Euler(90, 0, 0);
                bracket.localScale = new Vector3(0.05f, (PoleRadius + BracketM) / 2, 0.05f);
                Object.DestroyImmediate(bracket.GetComponent<Collider>());
            }
            else go.transform.rotation = rot;
            SetLayer(go, layer);
            return go.transform;
        }

        static void Plate(Pole pole, SignPlacement s, int layer)
        {
            var prefab = MountedPrefab(s.catalogId);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pole.root);
            go.name = SignName(s);
            var rot = Quaternion.Euler(0, s.yawDeg, 0);
            go.transform.SetPositionAndRotation(pole.at + rot * new Vector3(0, 0, PoleRadius - 0.03f) + Vector3.up * pole.nextPlateY, rot);
            pole.nextPlateY -= 0.72f;
            SetLayer(go, layer);
        }

        /// <summary>Kit prefab without its post, root at the mounting point (back of the housing / post axis). Made once.</summary>
        public static GameObject MountedPrefab(string catalogId)
        {
            string path = Mounted + catalogId + "_Mounted.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Traffic + catalogId + ".prefab");
            if (source == null) throw new FileNotFoundException(Traffic + catalogId + ".prefab");
            Directory.CreateDirectory(Mounted);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                go.name = catalogId + "_Mounted";
                var postAxis = Vector3.zero; bool found = false;
                foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => PostParts.Any(p => t.name == p || t.name.StartsWith(p + "."))).ToList())
                {
                    if (!found && t.name.StartsWith("Post") && !t.name.StartsWith("PostCap") && t.GetComponent<Renderer>() != null) { postAxis = t.GetComponent<Renderer>().bounds.center; found = true; }
                    Object.DestroyImmediate(t.gameObject);
                }
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                var rs = go.GetComponentsInChildren<Renderer>(true);
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                // Heads: the back of the housing; plates: the old post axis at the plate centre height.
                bool signal = go.GetComponent<TrafficSignalView>() != null;
                var mount = signal ? new Vector3(b.center.x, b.center.y, b.min.z) : new Vector3(postAxis.x, b.center.y, postAxis.z);
                var model = go.transform.Find("Model");
                (model != null ? model : go.transform.GetChild(0)).position -= mount;
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------ poles

        static Pole NewPole(Vector3 at, float height, string name, Transform parent, Material steel, int layer, float radius = PoleRadius)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            var basePos = at + Vector3.up * SidewalkTop;
            root.position = basePos;
            var shaft = Cylinder("Shaft", root, steel, layer);
            shaft.localScale = new Vector3(radius * 2, height / 2, radius * 2); shaft.localPosition = Vector3.up * height / 2;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; plate.name = "BasePlate"; plate.SetParent(root, false);
            plate.localScale = new Vector3(0.3f, 0.03f, 0.3f); plate.localPosition = Vector3.up * 0.015f;
            Object.DestroyImmediate(plate.GetComponent<Collider>()); plate.GetComponent<Renderer>().sharedMaterial = steel; plate.gameObject.layer = layer;
            var cap = Cylinder("Cap", root, steel, layer); cap.localScale = new Vector3(radius * 2.3f, 0.02f, radius * 2.3f); cap.localPosition = Vector3.up * (height + 0.02f);
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            root.gameObject.isStatic = true;
            return new Pole { at = basePos, root = root, nextPlateY = Mathf.Min(2.15f, height - 0.4f) };
        }

        static void Arm(Transform pole, Vector3 from, Vector3 dir, float length, Material steel, int layer)
        {
            var arm = Cylinder("Arm", pole, steel, layer);
            arm.position = from + dir * length / 2;
            arm.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            arm.localScale = new Vector3(0.1f, length / 2, 0.1f);
            Object.DestroyImmediate(arm.GetComponent<Collider>());
            // A diagonal brace, as on real mast arms.
            var brace = Cylinder("Brace", pole, steel, layer);
            var a = from - Vector3.up * 0.9f; var b2 = from + dir * length * 0.45f;
            brace.position = (a + b2) / 2; brace.rotation = Quaternion.FromToRotation(Vector3.up, (b2 - a).normalized);
            brace.localScale = new Vector3(0.05f, Vector3.Distance(a, b2) / 2, 0.05f);
            Object.DestroyImmediate(brace.GetComponent<Collider>());
        }

        static Transform Cylinder(string name, Transform parent, Material m, int layer)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform; t.name = name; t.SetParent(parent, false);
            t.GetComponent<Renderer>().sharedMaterial = m; t.gameObject.layer = layer; t.gameObject.isStatic = true;
            return t;
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 V(double x, double y, double z) => new Vector3((float)x, (float)y, (float)z);
        static Vector3 P(TrafficSignalAttachment h) => V(h.x, h.y, h.z);
        static Vector3 P(SignPlacement s) => V(s.x, s.y, s.z);
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        static GameObject Place(string path, Vector3 pos, float yaw, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException("No prefab " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return go;
        }

        static void SetLayer(GameObject o, int layer) { foreach (var t in o.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }
    }
}
