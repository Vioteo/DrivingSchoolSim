using UnityEngine;
using DrivingSchool.Contracts;
using DrivingSchool.Input;
using DrivingSchool.Presentation.Physics;

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
        public KeyboardInputSource Keyboard { get; } = new KeyboardInputSource();
        public IInputSource Source { get; set; }
        public int CollisionCount { get; private set; }
        public float LastImpactSpeedMps { get; private set; }
        public string LastImpactWith { get; private set; } = "";
        public float LastImpactTime { get; private set; } = -10f;
        long tick;

        void Awake()
        {
            Adapter = GetComponent<VehiclePhysicsAdapter>();
            Source = Keyboard;
            Keyboard.automatic = Adapter.transmission == TransmissionType.Automatic;
        }

        void Update()
        {
            Keyboard.automatic = Adapter.transmission == TransmissionType.Automatic;
            Keyboard.vehicleSpeedMps = Adapter.CurrentState.signedSpeedMps;
            if (inputEnabled && ReferenceEquals(Source, Keyboard)) Keyboard.Poll(Time.deltaTime);
        }

        void FixedUpdate()
        {
            var cmd = inputEnabled && Source != null && Source.IsConnected ? Source.Read(++tick) : Parked();
            Adapter.Step(cmd, Time.fixedDeltaTime);
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
        }
    }
}
