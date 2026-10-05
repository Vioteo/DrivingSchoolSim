using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Street lamp switched by daylight (WeatherController.Daylight01), like a photocell: the light and the glowing lens
    /// come on at dusk and go off in the morning. The lens is an emissive renderer; its material is instanced per lamp.
    /// T75: the beam is shaped like a road luminaire — a wide spot pointing down with a light cookie that spreads the light
    /// along the road and over the carriageway (more intensity at steep angles, the "batwing"), so neighbouring lamps
    /// overlap instead of drawing round pools under each post. The arm of the post (its local +Z) points over the road.
    /// Photometry values are a game calibration, not a lighting standard.
    /// </summary>
    public sealed class StreetLampView : MonoBehaviour
    {
        public Light lampLight;
        public Renderer lens;
        public Color lensColor = new Color(1f, 0.72f, 0.42f);
        public float lensEmission = 8f;
        [Tooltip("Daylight level below which the lamp is on.")]
        public float switchOnBelow = 0.35f;

        [Header("Beam (T75)")]
        [Tooltip("Shape the beam at start: spot straight down, cookie, intensity and range below.")]
        public bool shapeBeam = true;
        public float beamIntensity = 520f;
        public float beamRange = 46f;
        public Color beamColor = new Color(1f, 0.76f, 0.48f);

        // Footprint of the cookie in tan(angle from vertical): ×6.75 m (lamp height) gives metres on the ground.
        public const float SpotAngle = 146f, AlongRoad = 2.9f, OverRoad = 2.0f, BehindPost = 1.0f;

        Material lensMat; float lightIntensity; bool? on;
        static Texture2D cookie;

        void Start()
        {
            if (lampLight != null)
            {
                if (shapeBeam) ShapeBeam();
                lightIntensity = lampLight.intensity;
            }
            if (lens != null) { lensMat = lens.material; lensMat.EnableKeyword("_EMISSION"); }
        }

        void ShapeBeam()
        {
            Vector3 arm = transform.forward; arm.y = 0f;
            if (arm.sqrMagnitude < 1e-4f) arm = Vector3.forward;
            // Light's local Y points over the road, local X along it (the cookie's v and u axes).
            lampLight.transform.rotation = Quaternion.LookRotation(Vector3.down, arm.normalized);
            lampLight.type = LightType.Spot;
            lampLight.spotAngle = SpotAngle; lampLight.innerSpotAngle = SpotAngle - 4f;
            lampLight.range = beamRange; lampLight.intensity = beamIntensity; lampLight.color = beamColor;
            lampLight.cookie = Cookie;
        }

        /// <summary>
        /// Shared cookie: an ellipse on the ground, long along the road, reaching over the carriageway and a little behind
        /// the post; inside it the intensity grows with the angle from vertical (≈(1+tan²)^1.2) to even out the light on
        /// the ground, and fades to zero at the edge.
        /// </summary>
        public static Texture2D Cookie
        {
            get
            {
                if (cookie != null) return cookie;
                const int n = 128;
                float T = Mathf.Tan(SpotAngle * 0.5f * Mathf.Deg2Rad);
                var v = new float[n * n]; float max = 0f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float tx = ((x + 0.5f) / n * 2f - 1f) * T, ty = ((y + 0.5f) / n * 2f - 1f) * T;
                        float ay = ty >= 0f ? OverRoad : BehindPost;
                        float e = Mathf.Sqrt(tx * tx / (AlongRoad * AlongRoad) + ty * ty / (ay * ay));
                        float window = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, e));
                        float bat = Mathf.Pow(1f + tx * tx + ty * ty, 1.2f);
                        float val = window * bat;
                        if (x == 0 || y == 0 || x == n - 1 || y == n - 1) val = 0f;
                        v[y * n + x] = val; if (val > max) max = val;
                    }
                var px = new Color32[n * n];
                for (int i = 0; i < px.Length; i++) { byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v[i] / max) * 255f); px[i] = new Color32(b, b, b, b); }
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false, true)
                    { name = "StreetLampCookie", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
                t.SetPixels32(px); t.Apply(false, false);
                return cookie = t;
            }
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
