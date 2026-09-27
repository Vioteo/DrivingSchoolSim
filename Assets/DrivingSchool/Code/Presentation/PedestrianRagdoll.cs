using System.Collections.Generic;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Ragdoll of a pedestrian (T53), built at runtime from the shared 20-bone skeleton (docs/pedestrians.md: Hips, Spine, Chest,
    /// Head, UpperArm/Forearm/Hand, UpperLeg/LowerLeg/Foot _L/_R). Bodies stay kinematic and colliders off until
    /// <see cref="Knock"/>: then the Animator stops, physics takes the bones and the impact velocity is handed over.
    /// </summary>
    public sealed class PedestrianRagdoll : MonoBehaviour
    {
        public const float MassKg = 70f;

        sealed class Part { public Transform bone; public Rigidbody body; public Collider collider; }

        readonly List<Part> parts = new List<Part>();
        public bool IsDown { get; private set; }
        public bool Built => parts.Count > 0;
        public Rigidbody Pelvis => parts.Count > 0 ? parts[0].body : null;

        // bone, child that ends it (for the capsule), parent body bone, share of the total mass, radius as a share of the length
        static readonly (string bone, string end, string parent, float mass, float radius)[] Layout =
        {
            ("Hips", "Spine", null, 0.15f, 0.9f),
            ("Spine", "Neck", "Hips", 0.25f, 0.55f),
            ("Head", null, "Spine", 0.08f, 0.5f),
            ("UpperArm_L", "Forearm_L", "Spine", 0.035f, 0.2f), ("UpperArm_R", "Forearm_R", "Spine", 0.035f, 0.2f),
            ("Forearm_L", "Hand_L", "UpperArm_L", 0.03f, 0.17f), ("Forearm_R", "Hand_R", "UpperArm_R", 0.03f, 0.17f),
            ("UpperLeg_L", "LowerLeg_L", "Hips", 0.12f, 0.2f), ("UpperLeg_R", "LowerLeg_R", "Hips", 0.12f, 0.2f),
            ("LowerLeg_L", "Foot_L", "UpperLeg_L", 0.07f, 0.15f), ("LowerLeg_R", "Foot_R", "UpperLeg_R", 0.07f, 0.15f),
        };

        public void Build()
        {
            if (Built) return;
            var bones = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            var byBone = new Dictionary<string, Part>();
            foreach (var (name, end, parent, share, radius) in Layout)
            {
                if (!bones.TryGetValue(name, out var bone)) continue;
                var part = new Part { bone = bone };
                part.body = bone.gameObject.AddComponent<Rigidbody>();
                part.body.mass = MassKg * share; part.body.isKinematic = true; part.body.interpolation = RigidbodyInterpolation.Interpolate;
                part.body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                Vector3 local = end != null && bones.TryGetValue(end, out var e) ? bone.InverseTransformPoint(e.position) : bone.InverseTransformDirection(Vector3.up) * 0.22f / Mathf.Max(0.01f, bone.lossyScale.y);
                if (name == "Head")
                {
                    var sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = local.magnitude * 0.5f; sphere.center = local * 0.5f; part.collider = sphere;
                }
                else
                {
                    var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                    int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                    capsule.direction = axis; capsule.center = local * 0.5f;
                    float length = local.magnitude;
                    capsule.radius = Mathf.Max(0.02f, length * radius * 0.5f);
                    capsule.height = length + capsule.radius;
                    part.collider = capsule;
                }
                part.collider.enabled = false;
                if (parent != null && byBone.TryGetValue(parent, out var p))
                {
                    var joint = bone.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = p.body; joint.enableProjection = true;
                    joint.lowTwistLimit = new SoftJointLimit { limit = -25f }; joint.highTwistLimit = new SoftJointLimit { limit = 60f };
                    joint.swing1Limit = new SoftJointLimit { limit = name.StartsWith("Upper") ? 70f : 35f };
                    joint.swing2Limit = new SoftJointLimit { limit = name.StartsWith("Upper") ? 45f : 20f };
                }
                parts.Add(part); byBone[name] = part;
            }
        }

        /// <summary>Hands the body over to physics with the velocity of the impact (m/s, world).</summary>
        public void Knock(Vector3 velocity, Vector3 hitPoint)
        {
            if (IsDown) return;
            Build();
            IsDown = true;
            foreach (var a in GetComponentsInChildren<Animator>()) a.enabled = false;
            foreach (var d in GetComponentsInChildren<PedestrianAnimator>()) d.enabled = false;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            foreach (var p in parts)
            {
                p.body.isKinematic = false; p.collider.enabled = true;
                // The body is thrown along the car, lower parts harder (the bumper hits the legs), with a little lift.
                float low = Mathf.Clamp01(1.2f - (p.bone.position.y - transform.position.y));
                p.body.linearVelocity = velocity * (0.55f + 0.35f * low) + Vector3.up * velocity.magnitude * 0.12f;
            }
            // Parts of one body do not collide with each other (set once the colliders are active).
            foreach (var a in parts) foreach (var b in parts) if (a != b) UnityEngine.Physics.IgnoreCollision(a.collider, b.collider);
        }
    }
}
