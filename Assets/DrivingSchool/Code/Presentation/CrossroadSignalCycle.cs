using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Fixed two-phase cycle for the training crossroad (exercise 8). Visual only: the course
    /// evaluator does not score red-light violations yet.
    /// </summary>
    public sealed class CrossroadSignalCycle : MonoBehaviour
    {
        public TrafficSignalView[] northSouth = new TrafficSignalView[0];
        public TrafficSignalView[] eastWest = new TrafficSignalView[0];
        [Tooltip("Pedestrian heads that cross the east-west arms; they walk while north-south traffic has green.")]
        public TrafficSignalView[] walkWithNorthSouth = new TrafficSignalView[0];
        [Tooltip("Pedestrian heads that cross the north-south arms.")]
        public TrafficSignalView[] walkWithEastWest = new TrafficSignalView[0];
        public float green = 18, amber = 3, allRed = 2;

        float clock;

        void OnEnable() { clock = 0; Apply(); }

        void Update()
        {
            clock += Time.deltaTime;
            Apply();
        }

        void Apply()
        {
            float half = green + amber + allRed;
            float t = Mathf.Repeat(clock, half * 2);
            bool nsPhase = t < half;
            float p = nsPhase ? t : t - half;
            var go = p < green ? TrafficSignalView.Aspect.Green
                : p < green + amber ? TrafficSignalView.Aspect.Amber : TrafficSignalView.Aspect.Red;
            bool preparing = p >= half - allRed;
            Set(nsPhase ? northSouth : eastWest, go);
            Set(nsPhase ? eastWest : northSouth, preparing ? TrafficSignalView.Aspect.RedAmber : TrafficSignalView.Aspect.Red);
            bool walk = p < green;
            Set(nsPhase ? walkWithNorthSouth : walkWithEastWest, walk ? TrafficSignalView.Aspect.Green : TrafficSignalView.Aspect.Red);
            Set(nsPhase ? walkWithEastWest : walkWithNorthSouth, TrafficSignalView.Aspect.Red);
        }

        static void Set(TrafficSignalView[] heads, TrafficSignalView.Aspect aspect)
        {
            if (heads == null) return;
            foreach (var h in heads)
                if (h != null && h.CurrentAspect != aspect) h.SetAspect(aspect);
        }
    }
}
