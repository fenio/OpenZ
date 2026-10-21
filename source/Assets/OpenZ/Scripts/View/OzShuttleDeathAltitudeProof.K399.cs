using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256h Play proof's lines for the pieces' own throws (K399, test only; OzShuttleBreakup.ThrowVary.cs): every piece of every
    /// break got its own outward / upward factor, turn and spin from the death's seed, no two pieces leave with the same speed or at the
    /// same planned moment, the launch speeds are spread, the pieces leave the blast over at least <see cref="VarySpreadMinMs"/> ms (the
    /// heaviest first) and the crew's bodies got their own throw factors too.
    /// </summary>
    public sealed partial class OzShuttleDeathAltitudeProof
    {
        /// <summary>The pieces leave over at least this (ms, measured on the physics steps); the stdev of the pieces' launch speed against
        /// their unvaried launch at least this (x); the stdev of the pieces' launch speeds at least (m/s) for an air burst or split, the crack,
        /// the ground death or the drop (the kit's twin: the least of 200 deaths 1.93 / 1.05 / 0.69, x 0.047); two pieces count as alike when
        /// their out, up and spin factors all lie within <see cref="VaryAlike"/> of each other.</summary>
        public static float VarySpreadMinMs = 40f, VaryRatioSdMin = 0.035f, VarySpeedSdBurst = 1.5f, VarySpeedSdCrack = 0.8f, VarySpeedSdGround = 0.4f, VaryAlike = 0.001f;

        static float Sd(float[] v, int n)
        {
            if (n < 2) return 0f;
            float m = 0f; for (int i = 0; i < n; i++) m += v[i]; m /= n;
            float s = 0f; for (int i = 0; i < n; i++) s += (v[i] - m) * (v[i] - m);
            return Mathf.Sqrt(s / (n - 1));
        }

        /// <summary>BreakChecks, 2 s after the break (every piece has left the blast): the table and the lines.</summary>
        void VaryLines(Kill k, string tag, bool air)
        {
            var rows = OzShuttleBreakup.LastVary; int n = rows.Count;
            bool thisBreak = OzShuttleBreakup.LastVaryShip >= 0 && OzShuttleBreakup.LastVaryHow == OzShuttleBreakup.LastHow && n == OzShuttleBreakup.LastPieces.Count;
            var sb = new StringBuilder();
            sb.Append($"{tag} K399 the pieces' own throws (seed {OzShuttleBreakup.LastVarySeed}, '{OzShuttleBreakup.LastVaryHow}', {n} pieces): name kind kg | out x, up x, turn deg, spin x | planned / left ms | launch m/s unvaried -> varied | spin rad/s");
            var speeds = new float[Mathf.Max(1, n)]; var ratios = new float[Mathf.Max(1, n)];
            float minDv = 99f, minDt = 99f, minDf = 99f, firstLeft = 99f, lastLeft = -1f, heaviestLeft = -1f, lightestLeft = -1f; int left = 0, inRange = 0;
            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                sb.Append($"\n   {r.name} {r.kind} {r.mass:F0} | {r.outK:F3} {r.upK:F3} {r.turnDeg:F1} {r.spinK:F3} | {r.delayMs:F1} / {r.leftMs:F0} | {r.speed0:F2} -> {r.speed:F2}{(r.floored ? " (floor)" : "")} | {r.spin:F2}");
                speeds[i] = r.speed; ratios[i] = r.speed0 > 1e-3f ? r.speed / r.speed0 : 1f;
                if (r.outK >= OzShuttleBreakup.VaryOut0 - 1e-4f && r.outK <= OzShuttleBreakup.VaryOut1 + 1e-4f && r.upK >= OzShuttleBreakup.VaryUp0 - 1e-4f && r.upK <= OzShuttleBreakup.VaryUp1 + 1e-4f
                    && r.turnDeg <= OzShuttleBreakup.VaryTurnDeg + 1e-3f && r.spinK >= OzShuttleBreakup.VarySpin0 - 1e-4f && r.spinK <= OzShuttleBreakup.VarySpin1 + 1e-4f) inRange++;
                if (r.leftMs >= 0f) { left++; firstLeft = Mathf.Min(firstLeft, r.leftMs); lastLeft = Mathf.Max(lastLeft, r.leftMs); }
                if (r.rank == 0) heaviestLeft = r.leftMs;
                if (r.rank == n - 1) lightestLeft = r.leftMs;
                for (int j = 0; j < i; j++)
                {
                    var q = rows[j];
                    minDv = Mathf.Min(minDv, Mathf.Abs(q.speed - r.speed)); minDt = Mathf.Min(minDt, Mathf.Abs(q.delayMs - r.delayMs));
                    minDf = Mathf.Min(minDf, Mathf.Max(Mathf.Abs(q.outK - r.outK), Mathf.Max(Mathf.Abs(q.upK - r.upK), Mathf.Abs(q.spinK - r.spinK))));
                }
            }
            Log(sb.ToString());
            float sdV = Sd(speeds, n), sdR = Sd(ratios, n), spread = left > 0 ? lastLeft - firstLeft : -1f;
            string how = OzShuttleBreakup.LastVaryHow;
            float wantSd = how == OzShuttleDeathPlan.Crack ? VarySpeedSdCrack : how == OzShuttleDeathPlan.Burst || how == OzShuttleDeathPlan.Split ? VarySpeedSdBurst : VarySpeedSdGround;
            var unv = new float[Mathf.Max(1, n)]; for (int i = 0; i < n; i++) unv[i] = rows[i].speed0; System.Array.Sort(unv, 0, n);
            var srt = new float[Mathf.Max(1, n)]; System.Array.Copy(speeds, srt, n); System.Array.Sort(srt, 0, n);
            var crew = OzShuttleBreakup.LastCrewVary; float cMin = 9f, cMax = 0f, cDiff = 9f;
            for (int i = 0; i < crew.Count; i++) { cMin = Mathf.Min(cMin, crew[i]); cMax = Mathf.Max(cMax, crew[i]); for (int j = 0; j < i; j++) cDiff = Mathf.Min(cDiff, Mathf.Abs(crew[i] - crew[j])); }
            Log($"{tag} K399 spread: launch speeds stdev {sdV:F2} m/s, against the unvaried launch stdev x{sdR:F3}; the median {srt[n / 2]:F2} m/s (unvaried {unv[n / 2]:F2}: K396's launch floor is meant for it), the slowest {srt[0]:F2} (unvaried {unv[0]:F2}); closest factors {minDf:F4}, closest speeds {minDv:F4} m/s, closest planned moments {minDt:F2} ms; left over {spread:F0} ms ({firstLeft:F0} .. {lastLeft:F0}, the heaviest at {heaviestLeft:F0}, the lightest at {lightestLeft:F0}); held {OzShuttleBreakup.VaryHeld}, left {OzShuttleBreakup.VaryLeft} in all; crew factors {(crew.Count > 0 ? string.Join(" ", crew.ConvertAll(c => c.ToString("F3"))) : "-")}");
            Check($"{tag} K399 every piece its own throw: {inRange} of {n} pieces drawn within out x{OzShuttleBreakup.VaryOut0:F2}-{OzShuttleBreakup.VaryOut1:F2}, up x{OzShuttleBreakup.VaryUp0:F2}-{OzShuttleBreakup.VaryUp1:F2}, turn <= {OzShuttleBreakup.VaryTurnDeg:F0} deg, spin x{OzShuttleBreakup.VarySpin0:F2}-{OzShuttleBreakup.VarySpin1:F2}; no two alike: the closest pair's factors differ by {minDf:F4} (>= {VaryAlike:F3}), the closest planned moments by {minDt:F2} ms (>= 0.1)",
                  thisBreak && n >= 2 && inRange == n && minDf >= VaryAlike && minDt >= 0.1f);
            Check($"{tag} K399 launch speeds spread: the stdev of the pieces' launch speeds {sdV:F2} m/s (>= {wantSd:F1}), against their unvaried launch x{sdR:F3} (>= {VaryRatioSdMin:F3})", thisBreak && sdV >= wantSd && sdR >= VaryRatioSdMin);
            Check($"{tag} K399 the pieces leave the blast over a few frames: {left} of {n} left, over {spread:F0} ms (>= {VarySpreadMinMs:F0}), the heaviest first ({heaviestLeft:F0} ms <= the lightest's {lightestLeft:F0} ms)",
                  thisBreak && left == n && spread >= VarySpreadMinMs - 0.5f && heaviestLeft >= 0f && heaviestLeft <= lightestLeft);
            Check($"{tag} K399 the bodies get their own throw too: {crew.Count} factors x{cMin:F3}-{cMax:F3} (within x{OzShuttleBreakup.VaryCrew0:F2}-{OzShuttleBreakup.VaryCrew1:F2}), all different (closest {cDiff:F4})",
                  crew.Count >= 1 && cMin >= OzShuttleBreakup.VaryCrew0 - 1e-4f && cMax <= OzShuttleBreakup.VaryCrew1 + 1e-4f && (crew.Count < 2 || cDiff > 1e-4f));
        }
    }
}
