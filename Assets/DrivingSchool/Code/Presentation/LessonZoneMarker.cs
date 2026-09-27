using DrivingSchool.Learning;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>Рамка зоны урока на земле: четыре полосы, без коллайдеров. Текущая цель — ярче.</summary>
    public sealed class LessonZoneMarker : MonoBehaviour
    {
        public const int GroundLayer = 9;
        public static int GroundMask => 1 << GroundLayer;
        const float Stripe = 0.3f;
        readonly Transform[] sides = new Transform[4];
        Material mat;
        GuidedStep shown;

        public static LessonZoneMarker Create()
        {
            var go = new GameObject("LessonZone");
            var m = go.AddComponent<LessonZoneMarker>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            m.mat = new Material(sh) { name = "LessonZone" };
            for (int i = 0; i < 4; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(s.GetComponent<Collider>());
                s.name = "Side" + i; s.transform.SetParent(go.transform, false);
                var r = s.GetComponent<MeshRenderer>(); r.sharedMaterial = m.mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                m.sides[i] = s.transform;
            }
            return m;
        }

        public void Show(GuidedStep zone, bool current)
        {
            gameObject.SetActive(zone != null);
            if (zone == null) return;
            var c = current ? new Color(1f, 0.82f, 0.1f) : new Color(1f, 0.82f, 0.1f) * 0.55f;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c;
            if (zone == shown) return;
            shown = zone;
            var p = new Vector3(zone.x, 0f, zone.z);
            float y = UnityEngine.Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f, GroundMask) ? hit.point.y : 0f;
            transform.SetPositionAndRotation(new Vector3(zone.x, y + 0.03f, zone.z), Quaternion.Euler(0f, zone.yaw, 0f));
            float w = zone.width, l = zone.length;
            Place(0, new Vector3(-w / 2, 0, 0), new Vector3(Stripe, 0.02f, l));
            Place(1, new Vector3(w / 2, 0, 0), new Vector3(Stripe, 0.02f, l));
            Place(2, new Vector3(0, 0, -l / 2), new Vector3(w + Stripe, 0.02f, Stripe));
            Place(3, new Vector3(0, 0, l / 2), new Vector3(w + Stripe, 0.02f, Stripe));
        }

        void Place(int i, Vector3 local, Vector3 size) { sides[i].localPosition = local; sides[i].localScale = size; }

        void OnDestroy() { if (mat != null) Destroy(mat); }
    }
}
