using UnityEngine;
using UnityEngine.InputSystem;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Camera modes: cockpit (Socket_DriverEye, look around with the right mouse button, Z or ',' left, '.' right, glance back
    /// with the mouse wheel click), chase and free orbit around the car. C cycles modes.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class DriverCameraRig : MonoBehaviour
    {
        public enum Mode { Cockpit, Chase, Orbit }
        public Transform car;
        public Transform model;
        public Mode mode = Mode.Cockpit;
        public float cockpitFov = 68f, chaseFov = 60f;
        public Vector3 fallbackEyeInCar = new Vector3(-0.37f, 1.12f, -0.25f);
        public float mouseSensitivity = 0.12f;

        Camera cam; Transform eye;
        float lookYaw, lookPitch, orbitYaw = 200f, orbitPitch = 18f, orbitDist = 7f;
        Vector3 chaseVel; Vector3 chasePos;

        public Camera Camera => cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (model == null && car != null) model = car;
            if (model != null) eye = VehicleRigUtil.Find(model, "Socket_DriverEye");
            if (car != null) chasePos = car.position - car.forward * 7f + Vector3.up * 2.5f;
            cam.nearClipPlane = 0.05f;
        }

        void LateUpdate()
        {
            if (car == null) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb != null && kb.cKey.wasPressedThisFrame) mode = (Mode)(((int)mode + 1) % 3);
            Vector2 delta = mouse != null && mouse.rightButton.isPressed ? mouse.delta.ReadValue() * mouseSensitivity : Vector2.zero;

            switch (mode)
            {
                case Mode.Cockpit:
                {
                    cam.fieldOfView = cockpitFov; cam.nearClipPlane = 0.05f;
                    float keyYaw = 0f;
                    if (kb != null && (kb.commaKey.isPressed || kb.zKey.isPressed)) keyYaw = -1f;
                    if (kb != null && kb.periodKey.isPressed) keyYaw = 1f;
                    if (delta != Vector2.zero) { lookYaw += delta.x; lookPitch -= delta.y; }
                    else if (keyYaw != 0f) lookYaw = Mathf.MoveTowards(lookYaw, keyYaw * 75f, 240f * Time.deltaTime);
                    else if (mouse == null || !mouse.rightButton.isPressed) { lookYaw = Mathf.MoveTowards(lookYaw, 0f, 120f * Time.deltaTime); lookPitch = Mathf.MoveTowards(lookPitch, 0f, 60f * Time.deltaTime); }
                    if (mouse != null && mouse.middleButton.isPressed) lookYaw = 160f;
                    lookYaw = Mathf.Clamp(lookYaw, -170f, 170f); lookPitch = Mathf.Clamp(lookPitch, -60f, 40f);
                    Vector3 p = eye != null ? eye.position : car.TransformPoint(fallbackEyeInCar);
                    // Lean a little towards the side window when looking far sideways.
                    p += car.right * Mathf.Sin(lookYaw * Mathf.Deg2Rad) * 0.08f;
                    transform.SetPositionAndRotation(p, car.rotation * Quaternion.Euler(lookPitch + 4f, lookYaw, 0f));
                    break;
                }
                case Mode.Chase:
                {
                    cam.fieldOfView = chaseFov; cam.nearClipPlane = 0.2f;
                    Vector3 target = car.position - car.forward * 6.5f + Vector3.up * 2.4f;
                    chasePos = Vector3.SmoothDamp(chasePos, target, ref chaseVel, 0.25f);
                    transform.position = chasePos;
                    transform.rotation = Quaternion.LookRotation(car.position + Vector3.up * 1.0f - chasePos, Vector3.up);
                    break;
                }
                default:
                {
                    cam.fieldOfView = chaseFov; cam.nearClipPlane = 0.2f;
                    orbitYaw += delta.x * 3f; orbitPitch = Mathf.Clamp(orbitPitch - delta.y * 3f, 3f, 80f);
                    if (mouse != null) orbitDist = Mathf.Clamp(orbitDist - mouse.scroll.ReadValue().y * 0.01f, 3f, 25f);
                    var rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
                    transform.position = car.position + Vector3.up * 0.8f + rot * (Vector3.back * orbitDist);
                    transform.rotation = Quaternion.LookRotation(car.position + Vector3.up * 0.8f - transform.position, Vector3.up);
                    break;
                }
            }
        }
    }
}
