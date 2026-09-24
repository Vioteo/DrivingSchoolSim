using System;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation
{
    /// <summary>
    /// Wiper motor: a cycle is park → full sweep → park. Interval pauses between cycles, washer adds three sweeps.
    /// A running cycle always completes (self-park) unless power is lost. Rates are game settings.
    /// </summary>
    public sealed class WiperModel
    {
        public const float IntervalPauseS = 4f, LowCyclesPerMin = 45f, HighCyclesPerMin = 65f;
        public const int WasherSweeps = 3;

        float phase;        // 0..2 while moving: 0→1 outward, 1→2 return
        float pause;        // interval timer
        bool washerWas;
        WiperMode runMode = WiperMode.Low;

        public float Angle01 { get; private set; }
        public bool Moving { get; private set; }
        public int WashSweepsRemaining { get; private set; }
        public int CompletedCycles { get; private set; }

        public void Update(WiperMode mode, bool washer, bool powered, float dtSeconds)
        {
            if (float.IsNaN(dtSeconds) || float.IsInfinity(dtSeconds) || dtSeconds < 0) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
            if (washer && !washerWas && powered) WashSweepsRemaining = WasherSweeps;
            washerWas = washer;
            if (!powered) return; // blades stop where they are

            if (!Moving)
            {
                bool start = false;
                if (mode == WiperMode.Low || mode == WiperMode.High) start = true;
                else if (mode == WiperMode.Interval) { pause += dtSeconds; if (pause >= IntervalPauseS) start = true; }
                if (WashSweepsRemaining > 0) { start = true; WashSweepsRemaining--; }
                if (start) { Moving = true; phase = 0f; pause = 0f; runMode = mode == WiperMode.High ? WiperMode.High : WiperMode.Low; }
            }
            if (Moving)
            {
                float cpm = runMode == WiperMode.High ? HighCyclesPerMin : LowCyclesPerMin;
                phase += dtSeconds * 2f * cpm / 60f;
                if (phase >= 2f) { phase = 0f; Moving = false; CompletedCycles++; }
            }
            float p = phase <= 1f ? phase : 2f - phase;
            Angle01 = Moving ? 0.5f - 0.5f * (float)Math.Cos(Math.PI * p) : 0f;
        }
    }
}
