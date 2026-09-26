using System;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Fuel tank and consumption of a petrol engine. Flow follows the combustion power at the crankshaft
    /// (gross torque × speed) times a specific consumption that is best at medium-high load and rises at part load,
    /// where throttling and friction dominate; above 85 % load the mixture is enriched. With the throttle closed
    /// above idle the engine torque is zero, so overrun costs no fuel (deceleration fuel cut-off).
    /// Calibration targets for the 1.6 l sedan: ≈0.8–1 l/h at idle, ≈6–8 l/100 km at a steady 90 km/h.
    /// These are gameplay figures, not measured data.
    /// </summary>
    public sealed class FuelModel
    {
        public const float DensityKgPerL = 0.745f;
        /// <summary>Best specific consumption referred to combustion (gross) power, g/kWh.</summary>
        public const float BestSfcGPerKWh = 235f;

        public float TankLitres { get; }
        public float Litres { get; private set; }
        public float FlowLitresPerHour { get; private set; }
        public bool IsEmpty => Litres <= 0f;
        /// <summary>Tank share 0…1 (what the gauge shows).</summary>
        public float Level01 => TankLitres > 0f ? Litres / TankLitres : 0f;

        public FuelModel(float tankLitres, float initialLitres)
        {
            if (!(tankLitres > 0f) || float.IsInfinity(tankLitres)) throw new ArgumentOutOfRangeException(nameof(tankLitres));
            if (float.IsNaN(initialLitres) || initialLitres < 0f) throw new ArgumentOutOfRangeException(nameof(initialLitres));
            TankLitres = tankLitres;
            Litres = Math.Min(initialLitres, tankLitres);
        }

        /// <summary>Adds fuel up to the tank capacity; returns how much went in.</summary>
        public float Refill(float litres)
        {
            if (float.IsNaN(litres) || litres < 0f) throw new ArgumentOutOfRangeException(nameof(litres));
            float before = Litres; Litres = Math.Min(TankLitres, Litres + litres); return Litres - before;
        }

        /// <summary>Specific consumption at a load fraction (gross torque / full-load torque at this speed), g/kWh.</summary>
        public static float SpecificConsumption(float load01)
        {
            float load = load01 < 0f ? 0f : load01 > 1f ? 1f : load01;
            float partLoad = (1f - load) * (1f - load) * (1f - load);
            float enrich = load > 0.85f ? 1f + (load - 0.85f) : 1f;
            return BestSfcGPerKWh * (1f + partLoad) * enrich;
        }

        /// <summary>Instantaneous flow for a running engine, litres per hour.</summary>
        public static float Flow(float grossTorqueNm, float rpm, float fullLoadTorqueNm)
        {
            if (!(grossTorqueNm > 0f) || !(rpm > 0f)) return 0f;
            float powerKw = grossTorqueNm * rpm * (2f * (float)Math.PI / 60f) / 1000f;
            float load = fullLoadTorqueNm > 0f ? grossTorqueNm / fullLoadTorqueNm : 1f;
            float gPerHour = powerKw * SpecificConsumption(load);
            return gPerHour / 1000f / DensityKgPerL;
        }

        public void Update(bool engineRunning, float grossTorqueNm, float rpm, float fullLoadTorqueNm, float dtSeconds)
        {
            if (float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
            if (float.IsNaN(grossTorqueNm) || float.IsNaN(rpm)) throw new ArgumentOutOfRangeException(nameof(grossTorqueNm));
            FlowLitresPerHour = engineRunning && !IsEmpty ? Flow(grossTorqueNm, rpm, fullLoadTorqueNm) : 0f;
            Litres = Math.Max(0f, Litres - FlowLitresPerHour * dtSeconds / 3600f);
        }

        /// <summary>Consumption per distance for the trip computer, l/100 km; infinity at standstill with the engine running.</summary>
        public static float PerHundredKm(float flowLitresPerHour, float speedMps)
        {
            float kmh = Math.Abs(speedMps) * 3.6f;
            if (kmh < 3f) return flowLitresPerHour > 0f ? float.PositiveInfinity : 0f;
            return flowLitresPerHour / kmh * 100f;
        }
    }
}
