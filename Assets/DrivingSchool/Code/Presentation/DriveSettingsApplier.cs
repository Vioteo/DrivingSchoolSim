using DrivingSchool.Contracts;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Настройки поездки (ADR-016, docs/ui-settings.md §4) → компоненты машины и камеры:
    /// FOV из салона и дальность прорисовки (DriverCameraRig), вид при старте, качество зеркал (VehicleMirrorRig),
    /// скорость руления с клавиатуры (KeyboardInputSource), коробка передач (VehiclePhysicsAdapter).
    /// Вид при старте и КПП — только при загрузке сцены («только до поездки»); остальное — сразу после «Применить».
    /// </summary>
    public static class DriveSettingsApplier
    {
        public const float BaseSteeringRate = 2.5f, BaseReturnRate = 3.5f;   // значения KeyboardInputSource при 100 %
        static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            SceneManager.sceneLoaded += (scene, mode) => Apply(SettingsService.Current, atDriveStart: true);
            SettingsService.Applied += s => Apply(s, atDriveStart: false);
        }

        /// <summary>Разрешение текстуры бокового зеркала и прореживание кадров для «Низкое / Среднее / Высокое».</summary>
        public static (Vector2Int resolution, bool timeSlicing) MirrorQuality(int level)
        {
            switch (level)
            {
                case 0: return (new Vector2Int(256, 128), true);
                case 2: return (new Vector2Int(1024, 512), false);
                default: return (new Vector2Int(512, 256), true);
            }
        }

        public static void Apply(GameSettings s, bool atDriveStart)
        {
            foreach (var rig in Object.FindObjectsByType<DriverCameraRig>(FindObjectsSortMode.None))
            {
                rig.cockpitFov = Mathf.Clamp(s.gameplay.fov, 30, 120);
                var cam = rig.GetComponent<Camera>();
                if (cam != null) cam.farClipPlane = Mathf.Max(100, s.graphics.drawDistance);
                if (atDriveStart) rig.mode = s.gameplay.defaultCamera == 1 ? DriverCameraRig.Mode.Chase : DriverCameraRig.Mode.Cockpit;
            }
            var mq = MirrorQuality(s.graphics.mirrorQuality);
            foreach (var mirrors in Object.FindObjectsByType<VehicleMirrorRig>(FindObjectsSortMode.None))
                mirrors.ApplyQuality(mq.resolution, mq.timeSlicing);
            float k = Mathf.Clamp(s.controls.keyboardSteerSpeed, 10, 300) / 100f;
            foreach (var vc in Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            {
                vc.ApplyControls(s.controls);
                vc.Adapter?.SetSteeringWheelDegrees(s.controls.device == 1 ? s.controls.steeringLock : 900f);
                vc.Keyboard.steeringRate = BaseSteeringRate * k;
                vc.Keyboard.returnRate = BaseReturnRate * k;
                if (atDriveStart && vc.Adapter != null)
                {
                    var want = s.gameplay.transmission == 1 ? TransmissionType.Automatic : TransmissionType.Manual;
                    if (vc.Adapter.transmission != want) { vc.Adapter.SetTransmission(want); vc.Keyboard.ResetToParked(); }
                }
            }
        }
    }
}
