using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>Regulator gestures available on DS_Pedestrian_Police (visual only, see docs/pedestrians.md).</summary>
    public enum RegulatorSignal { None = 0, ArmsSide = 1, RightArmForward = 2, ArmUp = 3 }

    /// <summary>
    /// Feeds a pedestrian Animator from the transform's planar motion: Idle/Walk/Run blend by speed,
    /// kerbside look-around and, for the police officer, regulator gestures.
    /// Movement itself belongs to the agent that moves the transform; the clips are in place.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class PedestrianAnimator : MonoBehaviour
    {
        public const string SpeedParameter = "Speed", LookAroundParameter = "LookAround", SignalParameter = "Signal";
        static readonly int SpeedId = Animator.StringToHash(SpeedParameter);
        static readonly int LookAroundId = Animator.StringToHash(LookAroundParameter);
        static readonly int SignalId = Animator.StringToHash(SignalParameter);
        // A planar jump faster than this is a teleport or respawn, not walking.
        const float TeleportSpeed = 12f;

        [SerializeField, Tooltip("Speed at which the Walk clip's feet do not slide, m/s. Set by PedestrianAssetBuilder.")]
        float walkSpeed = 1.3f;
        [SerializeField, Tooltip("Same for Run, m/s; 0 when the model has no Run clip.")]
        float runSpeed = 3f;
        [SerializeField, Tooltip("Damping time of the Speed parameter, s.")]
        float damping = .12f;
        [SerializeField] bool hasSignals;

        Animator animator;
        Vector3 lastPosition;
        bool hasLastPosition;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public bool HasSignals => hasSignals;
        /// <summary>Planar speed in m/s to show; null measures it from the transform's motion.</summary>
        public float? SpeedOverride { get; set; }
        /// <summary>Look left, right, left before crossing; plays only while standing.</summary>
        public bool LookAround { get; set; }
        public RegulatorSignal Signal { get; set; }

        public void Configure(float walk, float run, bool signals)
        {
            walkSpeed = walk; runSpeed = run; hasSignals = signals;
        }

        void Awake() => animator = GetComponent<Animator>();

        void OnEnable() => hasLastPosition = false;

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0) return;
            var position = transform.position;
            float measured = 0;
            if (hasLastPosition)
            {
                measured = new Vector2(position.x - lastPosition.x, position.z - lastPosition.z).magnitude / dt;
                if (measured > TeleportSpeed) measured = 0;
            }
            lastPosition = position; hasLastPosition = true;

            float speed = Mathf.Max(0, SpeedOverride ?? measured);
            float top = runSpeed > 0 ? runSpeed : walkSpeed;
            animator.SetFloat(SpeedId, Mathf.Min(speed, top), damping, dt);
            // Beyond the fastest clip play it faster rather than let the feet slide.
            animator.speed = speed > top ? speed / top : 1;
            animator.SetBool(LookAroundId, LookAround);
            if (hasSignals) animator.SetInteger(SignalId, (int)Signal);
        }
    }
}
