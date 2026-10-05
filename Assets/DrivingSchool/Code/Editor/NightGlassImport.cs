using System.Collections.Generic;
using DrivingSchool.Presentation;
using UnityEditor;
using UnityEngine;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// T75: marks the windows of houses (Art/Houses) and train cars (Art/Trains) for night lighting. Every window — a
    /// connected piece of the glass submesh — gets one constant UV0 (see <see cref="NightGlass"/>): a house window a texel
    /// hashed from its centre (the same in every LOD), a train window the saloon or the cab texel (glass within
    /// <see cref="NightGlass.CabLength"/> of the head lamps is the cab). Only UV0 of glass vertices changes; the glass
    /// materials have no textures, so the day look is the same. Runs on every (re)import, nothing to set by hand.
    /// </summary>
    internal sealed class NightGlassImport : AssetPostprocessor
    {
        const string Houses = "Assets/DrivingSchool/Art/Houses/", Trains = "Assets/DrivingSchool/Art/Trains/";

        public override uint GetVersion() => 1;

        void OnPostprocessModel(GameObject root)
        {
            bool house = assetPath.StartsWith(Houses), train = assetPath.StartsWith(Trains);
            if (!house && !train) return;
            Vector3? lamp = null;
            if (train)
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && mf.name.Contains("Lamp_White")) { lamp = mf.transform.localToWorldMatrix.MultiplyPoint3x4(mf.sharedMesh.bounds.center); break; }
            int windows = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh; if (mesh == null) continue;
                var r = mf.GetComponent<Renderer>();
                var glassSubs = new List<int>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = r != null && s < r.sharedMaterials.Length ? r.sharedMaterials[s] : null;
                    bool glass = mat != null ? mat.name.Contains("Glass") : mf.name.EndsWith("_Glass");
                    if (glass) glassSubs.Add(s);
                }
                if (glassSubs.Count == 0) continue;
                windows += Mark(mesh, glassSubs, mf.transform.localToWorldMatrix, house, lamp);
            }
            if (windows == 0) Debug.LogWarning("NightGlassImport: no windows in " + assetPath);
        }

        static int Mark(Mesh mesh, List<int> glassSubs, Matrix4x4 toModel, bool house, Vector3? lamp)
        {
            var verts = mesh.vertices;
            var uv = mesh.uv; if (uv == null || uv.Length != verts.Length) uv = new Vector2[verts.Length];
            var world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) world[i] = toModel.MultiplyPoint3x4(verts[i]);
            // Vertices also used by other materials keep their UV.
            var shared = new bool[verts.Length];
            var isGlass = new bool[verts.Length];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                bool g = glassSubs.Contains(s);
                foreach (int i in mesh.GetTriangles(s)) { if (g) isGlass[i] = true; else shared[i] = true; }
            }
            // Union-find: a triangle joins its corners, coincident vertices (split normals) join each other.
            var parent = new int[verts.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
            foreach (int s in glassSubs)
            {
                var tri = mesh.GetTriangles(s);
                for (int t = 0; t < tri.Length; t += 3) { Union(tri[t], tri[t + 1]); Union(tri[t], tri[t + 2]); }
            }
            var byPos = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Length; i++)
            {
                if (!isGlass[i]) continue;
                var key = Vector3Int.RoundToInt(world[i] * 1000f);
                if (byPos.TryGetValue(key, out int other)) Union(other, i); else byPos[key] = i;
            }
            var sum = new Dictionary<int, Vector3>(); var count = new Dictionary<int, int>();
            for (int i = 0; i < verts.Length; i++)
            {
                if (!isGlass[i]) continue;
                int c = Find(i);
                sum[c] = (sum.TryGetValue(c, out var v) ? v : Vector3.zero) + world[i];
                count[c] = (count.TryGetValue(c, out int n) ? n : 0) + 1;
            }
            // Train: the car runs along its longest horizontal axis; the cab is the glass next to the head lamps.
            var b = mesh.bounds; var size = toModel.MultiplyVector(b.size);
            bool alongX = Mathf.Abs(size.x) > Mathf.Abs(size.z);
            var codes = new Dictionary<int, Vector2>();
            foreach (var kv in sum)
            {
                Vector3 centre = kv.Value / count[kv.Key];
                Vector2 code;
                if (house) code = NightGlass.HouseUv(centre);
                else
                {
                    bool cab = lamp.HasValue && Mathf.Abs(alongX ? centre.x - lamp.Value.x : centre.z - lamp.Value.z) < NightGlass.CabLength;
                    code = cab ? NightGlass.TrainCabUv : NightGlass.TrainSaloonUv;
                }
                codes[kv.Key] = code;
            }
            for (int i = 0; i < verts.Length; i++)
                if (isGlass[i] && !shared[i]) uv[i] = codes[Find(i)];
            mesh.uv = uv;
            return codes.Count;
        }
    }
}
