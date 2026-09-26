using UnityEngine;
using UnityEngine.InputSystem;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Sets up the three mirrors (MirrorSurface_L / _Centre / _R) and lets the driver adjust them:
    /// F1/F2/F3 select left/centre/right, numpad 8/2/4/6 (or Home/End/Delete/PageDown) tilt, numpad 5 resets.
    /// Side mirrors render every second frame, alternating (T23 time-slicing).
    /// </summary>
    [DefaultExecutionOrder(9000)]
    public sealed class VehicleMirrorRig : MonoBehaviour
    {
        public enum Slot { Left, Centre, Right }
        public Transform model;
        public Camera viewer;
        public Material templateMaterial;
        public bool enableTimeSlicing = true;
        public Vector2Int textureResolution = new Vector2Int(512, 256);
        public float adjustSpeedDeg = 12f;
        public Slot Selected { get; private set; } = Slot.Left;
        public float LastAdjustTime { get; private set; } = -10f;

        readonly VehicleMirror[] mirrors = new VehicleMirror[3];

        public VehicleMirror Get(Slot s) => mirrors[(int)s];

        void Start()
        {
            if (model == null) model = transform;
            if (viewer == null) viewer = Camera.main;
            string[] names = { "MirrorSurface_L", "MirrorSurface_Centre", "MirrorSurface_R" };
            for (int i = 0; i < 3; i++)
            {
                var t = VehicleRigUtil.Find(model, names[i]);
                if (t == null) continue;
                // Legacy PlanarMirror (ProjectBuilder showroom) would render the same surface twice.
                foreach (var old in t.GetComponents<PlanarMirror>()) old.enabled = false;
                var m = t.gameObject.AddComponent<VehicleMirror>();
                m.surface = t; m.viewer = viewer; m.car = transform; m.templateMaterial = templateMaterial;
                m.resolution = i == 1 ? new Vector2Int(textureResolution.x * 3 / 2, textureResolution.y) : textureResolution;
                m.convexity = i == 1 ? 0f : 0.35f;
                // Set up like a driver would: side mirrors show the lane behind with a sliver of the own car,
                // the centre mirror looks straight back through the rear window.
                m.aimDirection = i == 1 ? Quaternion.Euler(-2f, 0f, 0f) * Vector3.back
                                        : Quaternion.Euler(-1f, i == 0 ? 3f : -3f, 0f) * Vector3.back;
                m.renderEveryNthFrame = enableTimeSlicing && i != 1 ? 2 : 1;
                m.frameOffset = i == 2 ? 1 : 0;
                mirrors[i] = m;
            }
        }

        public void SetActiveMirrors(bool left, bool centre, bool right)
        {
            bool[] on = { left, centre, right };
            for (int i = 0; i < 3; i++) if (mirrors[i] != null) mirrors[i].enabled = on[i];
        }

        void Update()
        {
            var kb = Keyboard.current; if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) Selected = Slot.Left;
            if (kb.f2Key.wasPressedThisFrame) Selected = Slot.Centre;
            if (kb.f3Key.wasPressedThisFrame) Selected = Slot.Right;
            var m = mirrors[(int)Selected]; if (m == null) return;
            float d = adjustSpeedDeg * Time.deltaTime;
            float yaw = 0, pitch = 0;
            if (kb.numpad4Key.isPressed || kb.deleteKey.isPressed) yaw -= d;
            if (kb.numpad6Key.isPressed || kb.pageDownKey.isPressed) yaw += d;
            if (kb.numpad8Key.isPressed || kb.homeKey.isPressed) pitch += d;
            if (kb.numpad2Key.isPressed || kb.endKey.isPressed) pitch -= d;
            if (yaw != 0 || pitch != 0)
            {
                m.yawDeg = Mathf.Clamp(m.yawDeg + yaw, -15f, 15f); m.pitchDeg = Mathf.Clamp(m.pitchDeg + pitch, -15f, 15f);
                LastAdjustTime = Time.time;
            }
            if (kb.numpad5Key.wasPressedThisFrame) { m.yawDeg = 0; m.pitchDeg = 0; LastAdjustTime = Time.time; }
        }
    }
}
