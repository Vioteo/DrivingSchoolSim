using DrivingSchool.Simulation.Traffic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// A pedestrian of the traffic director (T51): placed from the snapshot on the ground (sidewalk, kerb or road),
    /// animated by <see cref="PedestrianAnimator"/> at the simulated speed. No decisions here.
    /// Being hit (T53): the body capsule is a trigger; a car that drives into it knocks the person down — the
    /// <see cref="PedestrianRagdoll"/> takes over with the car's velocity and the director is told (it stops simulating the
    /// person and keeps cars away from where they lie).
    /// </summary>
    public sealed class PedestrianView : MonoBehaviour
    {
        public LayerMask groundMask = 1 << 9;
        public const float KnockSpeedMps = 1.2f;
        PedestrianAnimator animator;
        PedestrianRagdoll ragdoll;
        Rigidbody body;
        TrafficDirectorHost host;
        string id;
        bool placed;

        public bool IsDown => ragdoll != null && ragdoll.IsDown;
        public string PedestrianId => id;

        void Awake()
        {
            animator = GetComponentInChildren<PedestrianAnimator>();
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate;
            foreach (var c in GetComponents<Collider>()) c.isTrigger = true;   // walking body: a sensor, not a wall
            ragdoll = GetComponent<PedestrianRagdoll>();
            if (ragdoll == null) ragdoll = gameObject.AddComponent<PedestrianRagdoll>();
            ragdoll.Build();
        }

        internal void Bind(TrafficDirectorHost owner, string pedestrianId) { host = owner; id = pedestrianId; }

        internal void Apply(ParticipantState p)
        {
            if (IsDown) return;   // lying where the car left them; the simulation no longer moves them
            var target = new Vector3((float)p.Position.x, (float)p.Position.y, (float)p.Position.z);
            if (UnityEngine.Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out var hit, 4f, groundMask, QueryTriggerInteraction.Ignore)) target.y = hit.point.y;
            var rotation = Quaternion.Euler(0f, (float)(p.HeadingRad * Mathf.Rad2Deg), 0f);
            if (!placed) { transform.SetPositionAndRotation(target, rotation); placed = true; }
            else { body.MovePosition(target); body.MoveRotation(Quaternion.RotateTowards(transform.rotation, rotation, 540f * Time.fixedDeltaTime)); }
            if (animator != null)
            {
                animator.SpeedOverride = (float)p.SpeedMps;
                animator.LookAround = p.Decision != null && p.Decision.StartsWith("wait", System.StringComparison.Ordinal);
            }
        }

        void OnTriggerEnter(Collider other) => TryKnock(other);
        void OnTriggerStay(Collider other) => TryKnock(other);

        void TryKnock(Collider other)
        {
            if (IsDown) return;
            var rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic || rb.transform.IsChildOf(transform)) return;   // AI cars are kinematic and stop for people
            var v = rb.GetPointVelocity(transform.position + Vector3.up);
            if (v.magnitude < KnockSpeedMps) return;
            ragdoll.Knock(v, other.ClosestPoint(transform.position + Vector3.up));
            body.detectCollisions = false;
            if (host != null) host.ReportPedestrianHit(id, v.magnitude, host.IsPlayer(rb.transform));
        }
    }
}
