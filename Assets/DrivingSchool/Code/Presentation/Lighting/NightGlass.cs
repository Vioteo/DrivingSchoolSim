using UnityEngine;

namespace DrivingSchool.Presentation
{
    /// <summary>
    /// Window glass that lights up at night (T75). The import step (Editor/NightGlassImport) gives every window of a house
    /// or a train car one constant UV0 — a texel of a small emission map — so a stock URP/Lit material with an emission map
    /// lights each window as a whole: house windows pick a random texel of <see cref="HousePattern"/> (lit warm, lit cool or
    /// dark), train windows pick the lit or the dark texel of <see cref="TrainPattern"/> (passenger saloon or driver's cab).
    /// The glass materials have no textures, so UV0 is free for this. The constants are shared by the import step and the
    /// run-time views.
    /// </summary>
    public static class NightGlass
    {
        /// <summary>Side of the house window pattern, texels.</summary>
        public const int HouseSize = 64;
        /// <summary>Hash step of a window centre, metres: the same window has the same texel in every LOD.</summary>
        public const float HashStep = 0.05f;
        /// <summary>UV0 of a train window: saloon (lit at night) and driver's cab (stays dark).</summary>
        public static readonly Vector2 TrainSaloonUv = new Vector2(0.25f, 0.5f), TrainCabUv = new Vector2(0.75f, 0.5f);
        /// <summary>Glass closer than this to the head lamps along the car is the driver's cab, metres.</summary>
        public const float CabLength = 3.2f;
        /// <summary>Share of house windows lit late in the evening (game value).</summary>
        public const float LitShare = 0.42f;

        /// <summary>UV0 of a house window whose centre (model space, metres) is <paramref name="centre"/>.</summary>
        public static Vector2 HouseUv(Vector3 centre)
        {
            int x = Mathf.RoundToInt(centre.x / HashStep), y = Mathf.RoundToInt(centre.y / HashStep), z = Mathf.RoundToInt(centre.z / HashStep);
            uint h = Hash((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u);
            int i = (int)(h % (HouseSize * HouseSize));
            return new Vector2((i % HouseSize + 0.5f) / HouseSize, (i / HouseSize + 0.5f) / HouseSize);
        }

        public static uint Hash(uint s)
        {
            s ^= s >> 16; s *= 0x7feb352du; s ^= s >> 15; s *= 0x846ca68bu; s ^= s >> 16;
            return s;
        }

        static Texture2D housePattern, trainPattern;

        /// <summary>
        /// 64×64 colours of house windows at night: ≈42 % lit — mostly warm incandescent, some warm-white LED, a few cool
        /// (a TV, a daylight lamp) and dim ones (curtains) — the rest dark. Point-sampled, no mips.
        /// </summary>
        public static Texture2D HousePattern
        {
            get
            {
                if (housePattern != null) return housePattern;
                var t = new Texture2D(HouseSize, HouseSize, TextureFormat.RGBA32, false, false)
                    { name = "NightWindows", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
                var px = new Color32[HouseSize * HouseSize];
                for (int i = 0; i < px.Length; i++)
                {
                    uint h = Hash((uint)i * 2654435761u + 12345u);
                    float lit = (h & 0xffff) / 65535f, kind = ((h >> 16) & 0xff) / 255f, level = ((h >> 24) & 0xff) / 255f;
                    Color c = Color.black;
                    if (lit < LitShare)
                    {
                        if (kind < 0.55f) c = new Color(1f, 0.66f, 0.34f);          // incandescent behind a curtain
                        else if (kind < 0.85f) c = new Color(1f, 0.86f, 0.66f);     // warm-white LED
                        else if (kind < 0.93f) c = new Color(0.78f, 0.85f, 1f) * 0.75f; // TV, cool lamp
                        else c = new Color(0.55f, 0.28f, 0.1f);                     // dim, thick curtain
                        c *= Mathf.Lerp(0.55f, 1f, level);
                    }
                    else c = new Color(0.012f, 0.012f, 0.014f);
                    c.a = 1f; px[i] = c;
                }
                t.SetPixels32(px); t.Apply(false, true);
                return housePattern = t;
            }
        }

        /// <summary>Two texels: the saloon light (cool fluorescent) and the dark cab.</summary>
        public static Texture2D TrainPattern
        {
            get
            {
                if (trainPattern != null) return trainPattern;
                var t = new Texture2D(2, 1, TextureFormat.RGBA32, false, false)
                    { name = "NightTrainWindows", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                t.SetPixels32(new[] { (Color32)new Color(0.86f, 0.94f, 1f), (Color32)Color.black });
                t.Apply(false, true);
                return trainPattern = t;
            }
        }

        /// <summary>A run-time copy of a glass material that glows through <paramref name="pattern"/>; emission starts black.</summary>
        public static Material MakeGlowing(Material glass, Texture pattern, Vector2 offset)
        {
            var m = new Material(glass) { name = glass.name + " (night)", hideFlags = HideFlags.DontSave };
            m.SetTexture("_EmissionMap", pattern);
            if (m.HasProperty("_BaseMap")) m.SetTextureOffset("_BaseMap", offset);   // URP/Lit samples the emission map with _BaseMap_ST
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", Color.black);
            return m;
        }
    }
}
