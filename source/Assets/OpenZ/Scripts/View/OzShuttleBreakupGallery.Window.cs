using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The break-up proof's fort-gun stages G19 and K3 (test only): their time window follows the rules' reload. The fort's big gun fires
    /// every 30 s of game time (DV25) after its lay, so a fixed 90 s of frames held two shells at most when the first shell came late, and
    /// K3's kill came about 13 s after the window (run 28: the gun was ready, but its first shell left 30-60 s into the window).
    /// <list type="bullet">
    /// <item>At the start, in game time: the time until the gun is ready again (its reload or lay still running) + the lay +
    /// (<see cref="FortShells"/> - 1) reloads + the lob's flight to the ship + <see cref="FortSpareSec"/>, never shorter than the old
    /// <see cref="FortMinSec"/> s.</item>
    /// <item>When the first shell leaves (however late): the window stays open at least (<see cref="FortShells"/> - 1) reloads + the
    /// lob's flight + <see cref="FortSpareSec"/> after it, so the rules' reload always has room for the shells counted.</item>
    /// <item><see cref="FortRealCapSec"/> s of real time is the last stop.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleBreakupGallery
    {
        /// <summary>Shells the window holds (G19 counts 3; K3 has 3 tries); spare game time (s); the real-time stop (s).</summary>
        public const int FortShells = 3;
        public const double FortSpareSec = 10.0, FortMinSec = 90.0, FortRealCapSec = 300.0;
        double fwReload = ZObject.FortMortarReload, fwFlight = 1.0, fwFirst = -1.0, fwEnd = -1.0; int fwShots0;

        static int FortShotsNow() => OzShuttleAim.Probes.TryGetValue("fort gun", out var p) && p != null ? p.shots : 0;

        /// <summary>The window (s of game time) for this fort's gun at the ship now, and its parts in words.</summary>
        double FortWindowSec(ZObject fort, out string why)
        {
            var hu = W != null && W.S != null ? W.S.Unit("howitzer") : null;
            bool mortar = ZWorld.FortMortarDeviation;
            double reload = mortar ? ZObject.FortMortarReload : (hu != null && hu.attack_speed > 0 ? hu.attack_speed : 5.0);
            double ready = fort != null && W != null ? System.Math.Max(0.0, fort.FortGunReadyAt - W.Time) : 0.0;
            double flight = 1.0;
            if (mortar && fort != null && s != null)
            {
                double dx = s.CenterX - fort.CenterX, dy = s.CenterY - fort.CenterY;
                flight = ZObject.FortMortarFlight(System.Math.Sqrt(dx * dx + dy * dy));
            }
            double sum = ready + ZObject.FortGunLay + (FortShells - 1) * reload + flight + FortSpareSec, win = System.Math.Max(FortMinSec, sum);
            fwReload = reload; fwFlight = flight; fwFirst = -1.0; fwEnd = -1.0; fwShots0 = FortShotsNow();
            why = $"{win:F1} s of game time: the gun ready in {ready:F1} s + its lay {ZObject.FortGunLay:F1} s + {FortShells - 1} reloads of {reload:F1} s + the lob's flight {flight:F1} s + {FortSpareSec:F0} s spare = {sum:F1} s (never under {FortMinSec:F0} s); once the first shell leaves, open at least {FortShells - 1} reloads + the lob + {FortSpareSec:F0} s spare after it = {(FortShells - 1) * reload + flight + FortSpareSec:F1} s; at most {FortRealCapSec:F0} s real";
            return win;
        }

        bool FortWindowOpen(double w0, double win, float u0)
        {
            if (W == null || Time.unscaledTime - u0 >= FortRealCapSec) return false;
            if (fwFirst < 0.0 && FortShotsNow() > fwShots0)
            {
                fwFirst = W.Time; fwEnd = System.Math.Max(w0 + win, fwFirst + (FortShells - 1) * fwReload + fwFlight + FortSpareSec);
                Log($"the fort's first shell left {fwFirst - w0:F1} s into the window: the window stays open until {fwEnd - w0:F1} s of game time");
            }
            return W.Time - w0 < win || (fwEnd > 0.0 && W.Time < fwEnd);
        }
    }
}
