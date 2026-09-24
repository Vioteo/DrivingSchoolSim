using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Physically placed planar mirror (desktop, mono). A camera at the viewer's eye reflected in the mirror
    /// plane renders through an off-axis frustum whose screen is exactly the mirror rectangle, so the image
    /// maps 1:1 onto mirror UVs (rewritten at start) and moves correctly with head and mirror adjustment.
    /// The near plane lies on the mirror plane, so nothing behind the glass is rendered.
    /// Mirror orientation is adjusted with Yaw/Pitch around the glass centre.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class VehicleMirror : MonoBehaviour
    {
        public Transform surface;          // MirrorSurface_* renderer
        public Camera viewer;              // driver camera
        public Transform car;
        public Material templateMaterial;  // URP Unlit
        public Vector2Int resolution = new Vector2Int(512, 256);
        public float farClip = 300f;
        public int renderEveryNthFrame = 1, frameOffset;
        [Range(-15, 15)] public float yawDeg, pitchDeg;
        public float convexity = 0f;       // >0 widens the view (side mirrors), game setting

        Camera cam; RenderTexture rt; Material mat;
        Vector3 centreInCar, rightInCar, upInCar, normalInCar; // rest frame, car space
        float halfW, halfH; Quaternion surfaceRestInCar; Vector3 surfacePosRestInCar;
        bool ready;

        public RenderTexture Texture => rt;
        public Camera MirrorCamera => cam;

        void Start()
        {
            if (surface == null) surface = transform;
            if (car == null) car = surface.root;
            if (viewer == null) viewer = Camera.main;
            ready = Init();
        }

        bool Init()
        {
            var mf = surface.GetComponent<MeshFilter>(); var mr = surface.GetComponent<Renderer>();
            if (mf == null || mr == null || mf.sharedMesh == null || viewer == null) return false;
            var mesh = mf.mesh; // instance: UVs are rewritten below
            var verts = mesh.vertices; var normals = mesh.normals;
            // Plane normal: average of mesh normals, pointing at the viewer.
            Vector3 n = Vector3.zero; foreach (var v in normals) n += v;
            if (n.sqrMagnitude < 1e-6f) n = Vector3.back;
            n = car.InverseTransformDirection(surface.TransformDirection(n)).normalized;
            Vector3 c = Vector3.zero; foreach (var v in verts) c += car.InverseTransformPoint(surface.TransformPoint(v)); c /= Mathf.Max(1, verts.Length);
            Vector3 eye = car.InverseTransformPoint(viewer.transform.position);
            if (Vector3.Dot(n, eye - c) < 0) n = -n;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
            Vector3 right = Vector3.Cross(n, up); // viewer's right: viewer looks along −n, Unity right = up × forward
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            var pu = new float[verts.Length]; var pv = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = car.InverseTransformPoint(surface.TransformPoint(verts[i])) - c;
                pu[i] = Vector3.Dot(p, right); pv[i] = Vector3.Dot(p, up);
                minU = Mathf.Min(minU, pu[i]); maxU = Mathf.Max(maxU, pu[i]); minV = Mathf.Min(minV, pv[i]); maxV = Mathf.Max(maxV, pv[i]);
            }
            var uvs = new Vector2[verts.Length];
            // The reflected camera looks at the glass from behind, so its image is mirrored in u.
            for (int i = 0; i < verts.Length; i++) uvs[i] = new Vector2(1f - (pu[i] - minU) / (maxU - minU), (pv[i] - minV) / (maxV - minV));
            mesh.uv = uvs;
            centreInCar = c + right * (0.5f * (minU + maxU)) + up * (0.5f * (minV + maxV));
            halfW = 0.5f * (maxU - minU); halfH = 0.5f * (maxV - minV);
            rightInCar = right; upInCar = up; normalInCar = n;
            surfaceRestInCar = Quaternion.Inverse(car.rotation) * surface.rotation;
            surfacePosRestInCar = car.InverseTransformPoint(surface.position);

            rt = new RenderTexture(resolution.x, resolution.y, 24) { name = "Mirror_" + surface.name, antiAliasing = 2 };
            rt.Create();
            var go = new GameObject("MirrorCamera_" + surface.name); go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.CopyFrom(viewer);
            cam.targetTexture = rt; cam.depth = viewer.depth - 10; cam.useOcclusionCulling = false;
            cam.cullingMask = viewer.cullingMask & ~(1 << surface.gameObject.layer);
            var data = cam.GetUniversalAdditionalCameraData(); data.renderShadows = false; data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.None;
            if (templateMaterial != null) mat = new Material(templateMaterial);
            else { var sh = Shader.Find("Universal Render Pipeline/Unlit"); mat = sh != null ? new Material(sh) : new Material(mr.sharedMaterial); }
            mat.mainTexture = rt; if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", rt);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.92f, 0.93f, 0.95f));
            mr.sharedMaterial = mat;
            return true;
        }

        void LateUpdate()
        {
            if (!ready || viewer == null) return;
            // Mirror adjustment: rotate glass about its centre (car space).
            Quaternion adj = Quaternion.AngleAxis(yawDeg, upInCar) * Quaternion.AngleAxis(-pitchDeg, rightInCar);
            surface.SetPositionAndRotation(
                car.TransformPoint(centreInCar + adj * (surfacePosRestInCar - centreInCar)),
                car.rotation * adj * surfaceRestInCar);

            bool active = viewer.isActiveAndEnabled && (renderEveryNthFrame <= 1 || (Time.frameCount + frameOffset) % renderEveryNthFrame == 0);
            cam.enabled = active;
            if (!active) return;

            Vector3 n = car.TransformDirection(adj * normalInCar), r = car.TransformDirection(adj * rightInCar), u = car.TransformDirection(adj * upInCar);
            Vector3 centre = car.TransformPoint(centreInCar);
            Vector3 eye = viewer.transform.position;
            float dist = Vector3.Dot(eye - centre, n);
            if (dist <= 0.01f) { cam.enabled = false; return; }
            Vector3 pe = eye - 2f * dist * n;                      // reflected eye, behind the glass
            float w = halfW * (1f + convexity), h = halfH * (1f + convexity);
            // Screen corners as seen from behind: the viewer's right is the camera's left.
            Vector3 pa = centre + r * w - u * h, pb = centre - r * w - u * h, pc = centre + r * w + u * h;
            Vector3 vr = (pb - pa).normalized, vu = (pc - pa).normalized, vn = Vector3.Cross(vr, vu).normalized;
            Vector3 va = pa - pe, vb = pb - pe, vc = pc - pe;
            float d = Vector3.Dot(va, vn);
            if (d <= 0.001f) { cam.enabled = false; return; }
            float near = d; // near plane on the glass
            float l = Vector3.Dot(vr, va), rr = Vector3.Dot(vr, vb), b = Vector3.Dot(vu, va), t = Vector3.Dot(vu, vc);
            cam.transform.SetPositionAndRotation(pe, Quaternion.LookRotation(vn, vu));
            cam.nearClipPlane = near; cam.farClipPlane = farClip;
            cam.projectionMatrix = Matrix4x4.Frustum(l, rr, b, t, near, farClip);
        }

        void OnDestroy()
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (mat != null) Destroy(mat);
        }
    }
}
