using System;
using System.Collections.Generic;
using System.Linq;
using DrivingSchool.Contracts;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// Signs 5.15.1 «Направления движения по полосам» and 5.15.2 «Направления движения по полосе» (T65): which movements
    /// a lane of a junction approach allows. Value format (layout): letters per lane — L left, S straight, R right, U U-turn;
    /// 5.15.1 lists every lane of the approach from left to right separated by '|' ("L|SR"), 5.15.2 one lane ("LS").
    /// Without such a sign the default discipline applies (ПДД РФ 8.5, <see cref="RoadKitTemplatesV2.DefaultManeuvers"/>).
    /// Sign codes: ГОСТ Р 52290-2004 numbering, not verified against the text (see <see cref="SignCatalog.Verified"/>).
    /// </summary>
    public static class LaneDirectionSign
    {
        public const string Lanes = "5.15.1", Lane = "5.15.2";

        public static bool IsLaneDirection(string code) => code == Lanes || code == Lane;

        /// <summary>"LS" → Left | Straight. Throws <see cref="FormatException"/> on anything else.</summary>
        public static LaneManeuver ParseLane(string letters)
        {
            if (string.IsNullOrWhiteSpace(letters)) throw new FormatException("empty lane direction");
            var m = LaneManeuver.None;
            foreach (char c in letters.Trim())
            {
                switch (char.ToUpperInvariant(c))
                {
                    case 'L': m |= LaneManeuver.Left; break;
                    case 'S': m |= LaneManeuver.Straight; break;
                    case 'R': m |= LaneManeuver.Right; break;
                    case 'U': m |= LaneManeuver.UTurn; break;
                    default: throw new FormatException("unknown lane direction '" + c + "' in \"" + letters + "\" (use L, S, R, U)");
                }
            }
            return m;
        }

        /// <summary>Letters for a movement set, in the order L S R U ("LS").</summary>
        public static string Format(LaneManeuver m) =>
            ((m & LaneManeuver.Left) != 0 ? "L" : "") + ((m & LaneManeuver.Straight) != 0 ? "S" : "") +
            ((m & LaneManeuver.Right) != 0 ? "R" : "") + ((m & LaneManeuver.UTurn) != 0 ? "U" : "");

        /// <param name="laneId">Lane the sign is given for (5.15.2) or any lane of the approach (5.15.1).</param>
        /// <param name="approachLanes">Incoming lanes of the approach from the axis outwards (left to right for the driver).</param>
        public static IReadOnlyDictionary<string, LaneManeuver> Parse(string code, string value, string laneId, IReadOnlyList<string> approachLanes)
        {
            if (approachLanes == null || !approachLanes.Contains(laneId)) throw new FormatException("lane " + laneId + " is not on the approach");
            var result = new Dictionary<string, LaneManeuver>();
            if (code == Lane)
            {
                result[laneId] = ParseLane(value);
                return result;
            }
            if (code != Lanes) throw new FormatException("not a lane direction sign: " + code);
            var parts = (value ?? "").Split('|');
            if (parts.Length != approachLanes.Count)
                throw new FormatException("5.15.1 \"" + value + "\" lists " + parts.Length + " lanes, the approach has " + approachLanes.Count);
            for (int i = 0; i < parts.Length; i++) result[approachLanes[i]] = ParseLane(parts[i]);
            return result;
        }
    }
}
