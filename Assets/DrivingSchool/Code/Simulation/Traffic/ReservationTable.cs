using System;
using System.Collections.Generic;
using System.Linq;

namespace DrivingSchool.Simulation.Traffic
{
    /// <summary>
    /// The one table of exclusive claims in a district (T40). Invariant: two owners never hold overlapping claims
    /// (same key or overlapping span) at the same time. Leases expire; the director decides what happens then.
    /// </summary>
    public sealed class ReservationTable
    {
        public sealed class Entry
        {
            public string OwnerId; public ReservationClaim Claim; public double UntilSeconds;
        }

        readonly List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public bool TryReserve(string ownerId, ReservationClaim claim, double untilSeconds, out string conflictOwnerId)
        {
            conflictOwnerId = null;
            foreach (var e in entries)
            {
                if (e.OwnerId == ownerId) continue;
                if (Conflicts(e.Claim, claim)) { conflictOwnerId = e.OwnerId; return false; }
            }
            entries.RemoveAll(e => e.OwnerId == ownerId && e.Claim.Id == claim.Id);
            entries.Add(new Entry { OwnerId = ownerId, Claim = claim, UntilSeconds = untilSeconds });
            return true;
        }

        /// <summary>Owner of any claim that conflicts with <paramref name="claim"/>, ignoring <paramref name="exceptOwner"/>; null if free.</summary>
        public string ConflictingOwner(ReservationClaim claim, string exceptOwner)
        {
            foreach (var e in entries)
                if (e.OwnerId != exceptOwner && Conflicts(e.Claim, claim)) return e.OwnerId;
            return null;
        }

        public bool Holds(string ownerId, string claimId) => entries.Any(e => e.OwnerId == ownerId && e.Claim.Id == claimId);

        public void Extend(string ownerId, string claimId, double untilSeconds)
        {
            foreach (var e in entries) if (e.OwnerId == ownerId && e.Claim.Id == claimId) e.UntilSeconds = Math.Max(e.UntilSeconds, untilSeconds);
        }

        public void Release(string ownerId, string claimId) => entries.RemoveAll(e => e.OwnerId == ownerId && e.Claim.Id == claimId);

        public void ReleaseAll(string ownerId) => entries.RemoveAll(e => e.OwnerId == ownerId);

        /// <summary>Removes and returns leases that ended at or before <paramref name="simSeconds"/>.</summary>
        public List<Entry> ExpireUntil(double simSeconds)
        {
            var expired = entries.Where(e => e.UntilSeconds <= simSeconds).ToList();
            entries.RemoveAll(e => e.UntilSeconds <= simSeconds);
            return expired;
        }

        /// <summary>Throws if the invariant is broken (used by tests and debug builds).</summary>
        public void AssertConsistent()
        {
            for (int i = 0; i < entries.Count; i++)
                for (int j = i + 1; j < entries.Count; j++)
                    if (entries[i].OwnerId != entries[j].OwnerId && Conflicts(entries[i].Claim, entries[j].Claim))
                        throw new InvalidOperationException("Overlapping reservations: " + entries[i].OwnerId + " / " + entries[j].OwnerId);
        }

        /// <summary>
        /// Plain keys conflict when equal. Side keys "zone#A" / "zone#B" conflict only with the other side of the same
        /// zone, so vehicles following each other through one connection do not block each other.
        /// </summary>
        public static bool KeysConflict(string x, string y)
        {
            int ix = x.LastIndexOf('#'), iy = y.LastIndexOf('#');
            if (ix < 0 || iy < 0) return x == y;
            return string.CompareOrdinal(x, 0, y, 0, Math.Max(ix, iy)) == 0 && ix == iy && x.Substring(ix) != y.Substring(iy);
        }

        static bool Conflicts(ReservationClaim a, ReservationClaim b)
        {
            foreach (var k in a.Keys) foreach (var m in b.Keys) if (KeysConflict(k, m)) return true;
            foreach (var s in a.Spans) foreach (var t in b.Spans) if (s.Overlaps(t)) return true;
            return false;
        }
    }
}
