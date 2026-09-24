using UnityEngine;
using DrivingSchool.Contracts;

namespace DrivingSchool.Presentation.Physics
{
    /// <summary>Optional per-collider surface override (ice patch, grass, gravel).</summary>
    public sealed class SurfaceTag : MonoBehaviour
    {
        public SurfaceType surface = SurfaceType.DryAsphalt;
        [Range(0.1f, 1.5f)] public float frictionScale = 1f;
        public bool followWeather = true; // wet/snow from weather overrides a dry tag
    }
}
