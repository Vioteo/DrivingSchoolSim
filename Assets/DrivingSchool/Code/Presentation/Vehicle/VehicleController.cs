using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Input;
using DrivingSchool.Presentation.Physics;
using DrivingSchool.Settings;
using DrivingSchool.Presentation.UI;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Player car coordinator: polls keyboard input every frame, steps the physics adapter every FixedUpdate,
    /// counts collisions. Visual/lights/dashboard components read the adapter state.
    /// </summary>
    [RequireComponent(typeof(VehiclePhysicsAdapter))]
    [DefaultExecutionOrder(-40)]
    public sealed class VehicleController : MonoBehaviour
    {
        public bool inputEnabled = true;
        public VehiclePhysicsAdapter Adapter { get; private set; }
        VehicleAudio vehicleAudio;
        public KeyboardInputSource Keyboard { get; } = new KeyboardInputSource();
        public G29InputSource Wheel { get; private set; }
        public IInputSource Source { get; set; }
        public int CollisionCount { get; private set; }
        public float LastImpactSpeedMps { get; private set; }
        public string LastImpactWith { get; private set; } = "";
        public float LastImpactTime { get; private set; } = -10f;
        long tick;
        bool wheelWasConnected;

        void Awake()
        {
            Adapter = GetComponent<VehiclePhysicsAdapter>();
            vehicleAudio = GetComponent<VehicleAudio>();
            if (vehicleAudio == null) vehicleAudio = gameObject.AddComponent<VehicleAudio>();
            Source = Keyboard;
            Wheel = new G29InputSource(Keyboard);
            ApplyControls(SettingsService.Current.controls);
            Keyboard.automatic = Adapter.transmission == TransmissionType.Automatic;
        }

        public void ApplyControls(ControlsSection controls)
        {
            Keyboard.Bindings = controls;
            Wheel.Settings = controls;
            if (Source == null || ReferenceEquals(Source, Keyboard) || ReferenceEquals(Source, Wheel))
                Source = controls != null && controls.device == 1 && Wheel.IsConnected ? Wheel : Keyboard;
        }

        void Update()
        {
            if (ReferenceEquals(Source, Keyboard) && SettingsService.Current.controls.device == 1 && Wheel.IsConnected)
                Source = Wheel;
            if (ReferenceEquals(Source, Wheel))
            {
                bool connected = Wheel.IsConnected;
                if (wheelWasConnected && !connected)
                {
                    var pause = Object.FindFirstObjectByType<PauseMenuController>();
                    pause?.Pause();
                    Debug.LogWarning("G29 отключён: подача газа сброшена, поездка поставлена на паузу.", this);
                }
                wheelWasConnected = connected;
            }
            else wheelWasConnected = false;
            Keyboard.automatic = Adapter.transmission == TransmissionType.Automatic;
            Keyboard.vehicleSpeedMps = Adapter.CurrentState.signedSpeedMps;
            Keyboard.wheelbaseM = Adapter.WheelbaseM; Keyboard.maxSteerDeg = Adapter.MaxSteerDeg;
            if (inputEnabled) Keyboard.Poll(Time.deltaTime);
        }

        void FixedUpdate()
        {
            var focused = !ReferenceEquals(Source, Wheel) || Application.isFocused || Application.isBatchMode;
            var cmd = inputEnabled && focused && Source != null && Source.IsConnected ? Source.Read(++tick) : Parked();
            Adapter.Step(cmd, Time.fixedDeltaTime);
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused || !ReferenceEquals(Source, Wheel)) return;
            Object.FindFirstObjectByType<PauseMenuController>()?.Pause();
        }

        DriverCommand Parked() => new DriverCommand { sequence = ++tick, handbrake = true, ignition = Adapter.LastCommand.ignition, selector = AutomaticSelector.P };

        public void ResetAt(Vector3 position, Quaternion rotation)
        {
            Keyboard.ResetToParked();
            Adapter.ResetAt(position, rotation);
        }

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v < 0.3f) return;
            CollisionCount++; LastImpactSpeedMps = v; LastImpactWith = c.collider.name; LastImpactTime = Time.time;
            vehicleAudio?.PlayImpact(v);
        }
    }
}
