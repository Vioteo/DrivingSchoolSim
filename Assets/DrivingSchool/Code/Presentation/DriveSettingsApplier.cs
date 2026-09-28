using DrivingSchool.Contracts;
using DrivingSchool.Input;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Settings;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Настройки поездки (ADR-016, docs/ui-settings.md §4) → компоненты машины и камеры:
    /// FOV из салона и дальность прорисовки (DriverCameraRig), вид при старте, качество зеркал (VehicleMirrorRig),
    /// скорость руления с клавиатуры (KeyboardInputSource), коробка передач (VehiclePhysicsAdapter),
    /// устройство ввода: руль G29 поверх клавиатуры (WheelInputSource, T42) и его мёртвые зоны, линейность, инверсия педалей.
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

        /// <summary>Настройки «Управление» → пересчёт осей руля (проценты → доли).</summary>
        public static void ApplyWheel(WheelTuning t, ControlsSection c)
        {
            t.steeringLockDeg = Mathf.Clamp(c.steeringLock, 180, 900);
            t.steeringDeadzone = Mathf.Clamp(c.steeringDeadzone, 0, 10) / 100f;
            t.steeringLinearity = Mathf.Clamp(c.steeringLinearity, 0, 100) / 100f;
            t.pedalDeadzone = Mathf.Clamp(c.pedalDeadzone, 0, 15) / 100f;
            t.invertPedals = c.invertPedals;
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
            WheelDevice.Enabled = s.controls.device == 1;
            foreach (var vc in Object.FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            {
                vc.Keyboard.steeringRate = BaseSteeringRate * k;
                vc.Keyboard.returnRate = BaseReturnRate * k;
                if (WheelDevice.Enabled)
                {
                    var wheel = vc.Keyboard.Overlay as WheelInputSource ?? new WheelInputSource();
                    ApplyWheel(wheel.Tuning, s.controls);
                    vc.Keyboard.Overlay = wheel;
                }
                else vc.Keyboard.Overlay = null;
                if (atDriveStart && vc.Adapter != null)
                {
                    var want = s.gameplay.transmission == 1 ? TransmissionType.Automatic : TransmissionType.Manual;
                    if (vc.Adapter.transmission != want) { vc.Adapter.SetTransmission(want); vc.Keyboard.ResetToParked(); }
                }
            }
        }
    }
}
