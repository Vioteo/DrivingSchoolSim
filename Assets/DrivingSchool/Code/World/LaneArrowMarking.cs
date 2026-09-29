using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrivingSchool.World
{
    /// <summary>Направления стрелки разметки 1.18; сочетаются: «прямо и направо» = Straight | Right.</summary>
    [Flags]
    public enum LaneArrow { None = 0, Straight = 1, Left = 2, Right = 4, UTurn = 8 }

    /// <summary>
    /// Размер стрелы по ГОСТ Р 51256-2018, табл. Б.1: при разрешённой скорости 60 км/ч и меньше длина 5 м
    /// (оголовок 1,5 м), больше 60 км/ч — 7,5 м (оголовок 2,25 м).
    /// </summary>
    public enum LaneArrowSize { Upto60, Above60 }

    /// <summary>
    /// Общая заготовка стрелок 1.18 для автодрома и города (T68). Строит плоский меш в плоскости XZ:
    /// начало — середина хвоста стержня, стрела смотрит вдоль +Z (курс 0), ветви поворотов — вбок.
    ///
    /// Как на настоящей дороге: стержень прямой, ветвь поворота отходит от стержня плавной дугой,
    /// оголовок поворота смотрит вбок и немного вперёд. Ветви рисуются «глазами водителя» (продольный
    /// размер сжат в <c>stretch</c> раз) и растягиваются обратно — поэтому с места водителя боковые
    /// оголовки читаются как стрелки, а не как тонкие щепки. Вся стрела умещается в
    /// ±<see cref="MaxHalfWidth"/> м от оси стержня, т. е. в полосе 3–3,5 м не доходит до линий.
    /// Длины и оголовок прямой стрелы — из таблицы ГОСТ; остальные пропорции подобраны по виду.
    /// </summary>
    public static class LaneArrowMarking
    {
        public const float MaxHalfWidth = 1.0f;

        sealed class Spec
        {
            public float length, turnLength, head, headWidth, shaft, stretch, gap;
        }

        static readonly Spec Upto60 = new Spec { length = 5f, turnLength = 4f, head = 1.5f, headWidth = .6f, shaft = .15f, stretch = 2.2f, gap = .3f };
        static readonly Spec Above60 = new Spec { length = 7.5f, turnLength = 6f, head = 2.25f, headWidth = .6f, shaft = .15f, stretch = 3.3f, gap = .45f };

        // Ветвь поворота в «кадре водителя»: четверть эллипса с полуосями BranchA (вбок) и BranchB (вперёд)
        // до угла BranchEndDeg, затем треугольный оголовок.
        const float BranchA = .52f, BranchB = .42f, BranchEndDeg = 78f, BranchWidth = .14f;
        const float TurnHeadLength = .5f, TurnHeadWidth = .42f;
        // Разворот — полуэллипс влево, оголовок смотрит назад.
        const float UTurnRadius = .36f, UTurnB = .36f, UTurnHeadLength = .4f, UTurnHeadWidth = .4f;
        const int BranchSegments = 17, UTurnSegments = 27;

        // ------------------------------------------------------------------ codes
        /// <summary>«S», «L», «R», «U» и их сочетания («SR», «SLR», «LU»); регистр не важен.</summary>
        public static LaneArrow Parse(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Empty lane arrow code");
            var kind = LaneArrow.None;
            foreach (char ch in code.Trim().ToUpperInvariant())
            {
                switch (ch)
                {
                    case 'S': kind |= LaneArrow.Straight; break;
                    case 'L': kind |= LaneArrow.Left; break;
                    case 'R': kind |= LaneArrow.Right; break;
                    case 'U': kind |= LaneArrow.UTurn; break;
                    default: throw new ArgumentException("Lane arrow code '" + code + "': use S, L, R, U");
                }
            }
            return kind;
        }

        public static string ToCode(LaneArrow kind)
        {
            string s = "";
            if ((kind & LaneArrow.Straight) != 0) s += "S";
            if ((kind & LaneArrow.Left) != 0) s += "L";
            if ((kind & LaneArrow.Right) != 0) s += "R";
            if ((kind & LaneArrow.UTurn) != 0) s += "U";
            return s;
        }

        /// <summary>Все сочетания, для которых собираются префабы.</summary>
        public static readonly LaneArrow[] Standard =
        {
            LaneArrow.Straight, LaneArrow.Left, LaneArrow.Right,
            LaneArrow.Straight | LaneArrow.Left, LaneArrow.Straight | LaneArrow.Right, LaneArrow.Left | LaneArrow.Right,
            LaneArrow.Straight | LaneArrow.Left | LaneArrow.Right,
            LaneArrow.UTurn, LaneArrow.Straight | LaneArrow.UTurn, LaneArrow.Left | LaneArrow.UTurn,
        };

        // ------------------------------------------------------------------ geometry
        /// <summary>Треугольники стрелы в её собственной плоскости: x — вправо, y — вперёд, от хвоста (0, 0).</summary>
        public static List<Vector2> Triangles(LaneArrow kind, LaneArrowSize size)
        {
            if ((kind & (LaneArrow.Straight | LaneArrow.Left | LaneArrow.Right | LaneArrow.UTurn)) == 0)
                throw new ArgumentException("Lane arrow without a direction");
            var p = size == LaneArrowSize.Above60 ? Above60 : Upto60;
            var tris = new List<Vector2>();
            bool straight = (kind & LaneArrow.Straight) != 0;
            float top = straight ? p.length : p.turnLength;
            float w = p.shaft / 2;
            if (straight)
            {
                Quad(tris, new Vector2(-w, 0), new Vector2(w, 0), new Vector2(w, top - p.head + .02f), new Vector2(-w, top - p.head + .02f));
                tris.Add(new Vector2(-p.headWidth / 2, top - p.head)); tris.Add(new Vector2(p.headWidth / 2, top - p.head)); tris.Add(new Vector2(0, top));
            }
            float stem = -1f;
            foreach (var turn in new[] { LaneArrow.Left, LaneArrow.Right, LaneArrow.UTurn })
            {
                if ((kind & turn) == 0) continue;
                float limit = straight ? top - p.head - p.gap : top;
                if (turn == LaneArrow.UTurn && (kind & LaneArrow.Left) != 0) limit -= BranchTop(LaneArrow.Left, p) + p.gap;  // разворот — ниже левой ветви
                float start = limit - BranchTop(turn, p);
                stem = Mathf.Max(stem, start);
                Branch(tris, turn, p, start);
            }
            if (!straight && stem >= 0) Quad(tris, new Vector2(-w, 0), new Vector2(w, 0), new Vector2(w, stem + .05f), new Vector2(-w, stem + .05f));
            return tris;
        }

        /// <summary>Габарит стрелы: xMin/xMax — поперёк (вправо +), yMin/yMax — вдоль, от хвоста.</summary>
        public static Rect Footprint(LaneArrow kind, LaneArrowSize size)
        {
            var t = Triangles(kind, size);
            Vector2 lo = t[0], hi = t[0];
            foreach (var v in t) { lo = Vector2.Min(lo, v); hi = Vector2.Max(hi, v); }
            return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
        }

        /// <summary>Длина стрелы вдоль полосы.</summary>
        public static float Length(LaneArrow kind, LaneArrowSize size) => Footprint(kind, size).yMax;

        /// <summary>
        /// Добавляет стрелу в общий меш: <paramref name="tail"/> — середина хвоста на полосе (высота — y),
        /// <paramref name="headingDeg"/> — курс движения по полосе (0 — +Z, 90 — +X). Треугольники смотрят вверх.
        /// </summary>
        public static void Append(LaneArrow kind, LaneArrowSize size, Vector3 tail, float headingDeg, List<Vector3> vertices, List<int> triangles)
        {
            var rot = Quaternion.Euler(0, headingDeg, 0);
            var t = Triangles(kind, size);
            for (int i = 0; i < t.Count; i += 3)
            {
                var a = tail + rot * new Vector3(t[i].x, 0, t[i].y);
                var b = tail + rot * new Vector3(t[i + 1].x, 0, t[i + 1].y);
                var c = tail + rot * new Vector3(t[i + 2].x, 0, t[i + 2].y);
                float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                if (Mathf.Abs(cross) < 1e-7f) continue;   // вырожденный
                int n = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                // Unity: лицевая сторона — по часовой при взгляде сверху (отрицательное векторное произведение в XZ).
                if (cross < 0) { triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2); }
                else { triangles.Add(n); triangles.Add(n + 2); triangles.Add(n + 1); }
            }
        }

        /// <summary>Отдельный меш одной стрелы (хвост в начале координат, вдоль +Z) — для префабов.</summary>
        public static Mesh CreateMesh(LaneArrow kind, LaneArrowSize size, string name = null)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            Append(kind, size, Vector3.zero, 0, v, t);
            var mesh = new Mesh { name = name ?? MeshName(kind, size), indexFormat = IndexFormat.UInt16 };
            mesh.SetVertices(v);
            var uv = new Vector2[v.Count]; var normals = new Vector3[v.Count];
            for (int i = 0; i < v.Count; i++) { uv[i] = new Vector2(v[i].x, v[i].z); normals[i] = Vector3.up; }
            mesh.uv = uv; mesh.normals = normals;
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static string MeshName(LaneArrow kind, LaneArrowSize size) =>
            "LaneArrow_" + ToCode(kind) + (size == LaneArrowSize.Above60 ? "_90" : "_60");

        // ------------------------------------------------------------------ parts
        static void Quad(List<Vector2> t, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        /// <summary>Осевая ветви в «кадре водителя» (до растяжения), от точки на оси стержня.</summary>
        static Vector2[] Centreline(LaneArrow turn)
        {
            bool u = turn == LaneArrow.UTurn;
            int n = u ? UTurnSegments : BranchSegments;
            float end = u ? Mathf.PI : BranchEndDeg * Mathf.Deg2Rad;
            float a = u ? UTurnRadius : BranchA, b = u ? UTurnB : BranchB;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = end * i / n;
                pts[i] = new Vector2(a * (1 - Mathf.Cos(t)), b * Mathf.Sin(t));
            }
            return pts;
        }

        static float BranchTop(LaneArrow turn, Spec p)
        {
            var t = new List<Vector2>();
            Branch(t, turn, p, 0);
            float top = 0;
            foreach (var v in t) top = Mathf.Max(top, v.y);
            return top;
        }

        static void Branch(List<Vector2> tris, LaneArrow turn, Spec p, float start)
        {
            bool u = turn == LaneArrow.UTurn;
            float side = turn == LaneArrow.Right ? 1f : -1f;   // разворот — влево, как у нас принято
            var c = Centreline(turn);
            int n = c.Length;
            var left = new Vector2[n]; var right = new Vector2[n];
            Vector2 tangent = Vector2.up;
            for (int i = 0; i < n; i++)
            {
                // Как numpy.gradient: центральные разности внутри, односторонние на концах.
                var d = i == 0 ? c[1] - c[0] : i == n - 1 ? c[n - 1] - c[n - 2] : (c[i + 1] - c[i - 1]) * .5f;
                tangent = d.normalized;
                var normal = new Vector2(-tangent.y, tangent.x) * (BranchWidth / 2);
                left[i] = c[i] + normal; right[i] = c[i] - normal;
            }
            Vector2 Map(Vector2 v) => new Vector2(v.x * side, v.y * p.stretch + start);
            for (int i = 0; i + 1 < n; i++) Quad(tris, Map(left[i]), Map(right[i]), Map(right[i + 1]), Map(left[i + 1]));
            float hl = u ? UTurnHeadLength : TurnHeadLength, hw = u ? UTurnHeadWidth : TurnHeadWidth;
            var across = new Vector2(-tangent.y, tangent.x);
            var baseMid = c[n - 1];
            tris.Add(Map(baseMid + across * (hw / 2)));
            tris.Add(Map(baseMid - across * (hw / 2)));
            tris.Add(Map(baseMid + tangent * hl));
        }
    }
}
