using System;
namespace DrivingSchool.Contracts
{
    public enum SurfaceType { DryAsphalt, WetAsphalt, PackedSnow, BlackIce }
    public enum WeatherPreset { ClearDay, Overcast, Rain, HeavyRain, Fog, Snow, ClearNight, RainNight, FullMoonNight }

    // Game-side weather state; coefficients are gameplay calibration, not measured road data.
    [Serializable] public struct WeatherConditions
    {
        public WeatherPreset preset;
        public float rainIntensity01, snowIntensity01, fogDensity01;
        public float timeOfDayHours; // 0..24
        public SurfaceType surface;

        public bool IsNight => timeOfDayHours < 6f || timeOfDayHours >= 21f;

        public static WeatherConditions FromPreset(WeatherPreset p)
        {
            switch (p)
            {
                case WeatherPreset.Overcast:   return Make(p, 0f, 0f, .15f, 13f, SurfaceType.DryAsphalt);
                case WeatherPreset.Rain:       return Make(p, .45f, 0f, .25f, 13f, SurfaceType.WetAsphalt);
                case WeatherPreset.HeavyRain:  return Make(p, 1f, 0f, .45f, 13f, SurfaceType.WetAsphalt);
                case WeatherPreset.Fog:        return Make(p, 0f, 0f, 1f, 8f, SurfaceType.WetAsphalt);
                case WeatherPreset.Snow:       return Make(p, 0f, .7f, .35f, 12f, SurfaceType.PackedSnow);
                case WeatherPreset.ClearNight: return Make(p, 0f, 0f, .05f, 23f, SurfaceType.DryAsphalt);
                case WeatherPreset.RainNight:  return Make(p, .6f, 0f, .3f, 23f, SurfaceType.WetAsphalt);
                case WeatherPreset.FullMoonNight: return Make(p, 0f, 0f, .03f, 23.5f, SurfaceType.DryAsphalt);
                default:                       return Make(WeatherPreset.ClearDay, 0f, 0f, .02f, 13f, SurfaceType.DryAsphalt);
            }
        }

        static WeatherConditions Make(WeatherPreset p, float rain, float snow, float fog, float hours, SurfaceType s)
            => new WeatherConditions { preset = p, rainIntensity01 = rain, snowIntensity01 = snow, fogDensity01 = fog, timeOfDayHours = hours, surface = s };
    }
}
