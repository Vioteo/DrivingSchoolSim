using System;
using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>Visual state only; traffic timing and right of way belong to simulation.</summary>
    [ExecuteAlways]
    public sealed class TrafficSignalView : MonoBehaviour
    {
        public enum Aspect { Off, Red, RedAmber, Amber, Green }

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
            if (pedestrian && (value == Aspect.Amber || value == Aspect.RedAmber))
                throw new ArgumentException("Pedestrian signals have red and green lamps only.", nameof(value));
            aspect = value;
            arrow = additionalArrow && HasArrow;
            Apply();
        }

        void OnEnable() => Apply();
        void OnValidate()
        {
            if (pedestrian && (aspect == Aspect.Amber || aspect == Aspect.RedAmber)) aspect = Aspect.Red;
            arrow &= HasArrow;
            Apply();
        }

        void Apply()
        {
            Set(redLamps, aspect == Aspect.Red || aspect == Aspect.RedAmber);
            Set(amberLamps, aspect == Aspect.Amber || aspect == Aspect.RedAmber);
            Set(greenLamps, aspect == Aspect.Green);
            Set(arrowLamps, arrow);
        }

        static void Set(Renderer[] lamps, bool visible)
        {
            if (lamps == null) return;
            foreach (var lamp in lamps) if (lamp != null) lamp.enabled = visible;
        }
    }
}
