using System;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>Visual state only; traffic timing and right of way belong to simulation.</summary>
    [ExecuteAlways]
    public sealed class TrafficSignalView : MonoBehaviour
    {
        // New values are appended so aspects already serialized in scenes keep their meaning.
        public enum Aspect { Off, Red, RedAmber, Amber, Green, GreenFlashing, AmberFlashing }

        /// <summary>Blink period of flashing aspects, seconds (visual only).</summary>
        public const float BlinkPeriod = 1f;

        [SerializeField] bool pedestrian;
        [SerializeField] Aspect aspect = Aspect.Red;
        [SerializeField] bool arrow;
        [SerializeField] Renderer[] redLamps = Array.Empty<Renderer>();
        [SerializeField] Renderer[] amberLamps = Array.Empty<Renderer>();
        [SerializeField] Renderer[] greenLamps = Array.Empty<Renderer>();
        [SerializeField] Renderer[] arrowLamps = Array.Empty<Renderer>();

        public Aspect CurrentAspect => aspect;
        public bool IsPedestrian => pedestrian;
        public bool HasArrow => arrowLamps.Length > 0;

        public void Configure(bool isPedestrian, Renderer[] red, Renderer[] amber,
            Renderer[] green, Renderer[] additionalArrow)
        {
            pedestrian = isPedestrian;
            redLamps = red ?? Array.Empty<Renderer>();
            amberLamps = amber ?? Array.Empty<Renderer>();
            greenLamps = green ?? Array.Empty<Renderer>();
            arrowLamps = additionalArrow ?? Array.Empty<Renderer>();
            SetAspect(Aspect.Red);
        }

        public void SetAspect(Aspect value, bool additionalArrow = false)
        {
            if (!Enum.IsDefined(typeof(Aspect), value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (pedestrian && (value == Aspect.Amber || value == Aspect.RedAmber || value == Aspect.AmberFlashing))
                throw new ArgumentException("Pedestrian signals have red and green lamps only.", nameof(value));
            aspect = value;
            arrow = additionalArrow && HasArrow;
            Apply();
        }

        /// <summary>Maps the simulation aspect (T32) to this view.</summary>
        public void SetAspect(Contracts.SignalAspect value, bool additionalArrow = false) => SetAspect((Aspect)(int)value, additionalArrow);

        void OnEnable() => Apply();
        void Update()
        {
            if (aspect == Aspect.GreenFlashing || aspect == Aspect.AmberFlashing) Apply();
        }

        void OnValidate()
        {
            if (pedestrian && (aspect == Aspect.Amber || aspect == Aspect.RedAmber || aspect == Aspect.AmberFlashing)) aspect = Aspect.Red;
            arrow &= HasArrow;
            Apply();
        }

        void Apply()
        {
            Set(redLamps, aspect == Aspect.Red || aspect == Aspect.RedAmber);
            bool blinkOn = Mathf.Repeat(Time.realtimeSinceStartup, BlinkPeriod) < BlinkPeriod / 2;
            Set(amberLamps, aspect == Aspect.Amber || aspect == Aspect.RedAmber || (aspect == Aspect.AmberFlashing && blinkOn));
            Set(greenLamps, aspect == Aspect.Green || (aspect == Aspect.GreenFlashing && blinkOn));
            Set(arrowLamps, arrow);
        }

        static void Set(Renderer[] lamps, bool visible)
        {
            if (lamps == null) return;
            foreach (var lamp in lamps) if (lamp != null) lamp.enabled = visible;
        }
    }
}
