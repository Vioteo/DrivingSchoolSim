using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Street lamp switched by daylight (WeatherController.Daylight01), like a photocell: the light and the glowing lens
    /// come on at dusk and go off in the morning. The lens is an emissive renderer; its material is instanced per lamp.
    /// </summary>
    public sealed class StreetLampView : MonoBehaviour
    {
        public Light lampLight;
        public Renderer lens;
        public Color lensColor = new Color(1f, 0.72f, 0.42f);
        public float lensEmission = 8f;
        [Tooltip("Daylight level below which the lamp is on.")]
        public float switchOnBelow = 0.35f;

        Material lensMat; float lightIntensity; bool? on;

        void Start()
        {
            if (lampLight != null) lightIntensity = lampLight.intensity;
            if (lens != null) { lensMat = lens.material; lensMat.EnableKeyword("_EMISSION"); }
        }

        void Update()
        {
            bool now = WeatherController.Daylight01 < switchOnBelow;
            if (on == now) return;
            on = now;
            if (lampLight != null) { lampLight.enabled = now; lampLight.intensity = lightIntensity; }
            if (lensMat != null)
            {
                lensMat.SetColor("_EmissionColor", now ? lensColor * lensEmission : Color.black);
                if (lensMat.HasProperty("_BaseColor")) lensMat.SetColor("_BaseColor", now ? lensColor : new Color(0.75f, 0.75f, 0.72f));
            }
        }

        void OnDestroy() { if (lensMat != null) Destroy(lensMat); }
    }
}
