using System.IO;
using DrivingSchool.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrivingSchool.Editor
{
    /// <summary>
    /// Префабы стрелок 1.18 из общей заготовки <see cref="LaneArrowMarking"/> (T68) — для города и любых сцен:
    /// <c>DS_LaneArrow_{S|L|R|SL|SR|LR|SLR|U|SU|LU}_{60|90}.prefab</c>. Хвост стрелы — в начале координат, стрела
    /// смотрит вдоль +Z; ставить на середину полосы, поворот — курс движения. Высота над покрытием — как у остальной
    /// разметки (сам префаб на y = 0, поднимайте на 6–9 мм, как RK_Marking_*). Материал — белая краска дорожного набора.
    /// Автодром берёт ту же геометрию напрямую (<see cref="LaneArrowMarking.Append"/>) и сливает в один меш.
    /// </summary>
    public static class LaneArrowBuilder
    {
        public const string Folder = "Assets/DrivingSchool/Art/Markings/LaneArrows";
        public const string MaterialPath = "Assets/DrivingSchool/Materials/RoadKit/RK_White.mat";
        public const float Lift = .008f;

        public static string PrefabPath(LaneArrow kind, LaneArrowSize size) =>
            Folder + "/DS_" + LaneArrowMarking.MeshName(kind, size) + ".prefab";

        [MenuItem("Driving School/Road Kit/Build lane arrows (1.18)")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material) throw new FileNotFoundException(MaterialPath + " — сначала Driving School/Road Kit/Import and build demo");
            int count = 0;
            foreach (var kind in LaneArrowMarking.Standard)
                foreach (var size in new[] { LaneArrowSize.Upto60, LaneArrowSize.Above60 })
                {
                    var mesh = MeshAsset(kind, size);
                    var go = new GameObject("DS_" + LaneArrowMarking.MeshName(kind, size));
                    var body = new GameObject("Marking", typeof(MeshFilter), typeof(MeshRenderer));
                    body.transform.SetParent(go.transform, false);
                    body.transform.localPosition = new Vector3(0, Lift, 0);
                    body.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var r = body.GetComponent<MeshRenderer>();
                    r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
                    go.isStatic = body.isStatic = true;
                    PrefabUtility.SaveAsPrefabAsset(go, PrefabPath(kind, size));
                    Object.DestroyImmediate(go);
                    count++;
                }
            AssetDatabase.SaveAssets();
            Debug.Log($"[LaneArrows] {count} префабов стрелок 1.18 в {Folder}");
        }

        /// <summary>Меш-ассет стрелы; существующий переписывается на месте, чтобы ссылки из сцен не терялись.</summary>
        static Mesh MeshAsset(LaneArrow kind, LaneArrowSize size)
        {
            string path = Folder + "/" + LaneArrowMarking.MeshName(kind, size) + ".asset";
            var fresh = LaneArrowMarking.CreateMesh(kind, size);
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!old) { AssetDatabase.CreateAsset(fresh, path); return fresh; }
            old.Clear();
            old.SetVertices(fresh.vertices); old.normals = fresh.normals; old.uv = fresh.uv;
            old.SetTriangles(fresh.triangles, 0);
            old.RecalculateBounds(); old.UploadMeshData(false);
            EditorUtility.SetDirty(old);
            Object.DestroyImmediate(fresh);
            return old;
        }
    }
}
