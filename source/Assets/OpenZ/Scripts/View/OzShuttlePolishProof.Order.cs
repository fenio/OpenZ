using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256b Play Proof A, the door sounds' order (test only).
    /// <list type="bullet">
    /// <item>S2 (boarding): the door and its hydraulic start together at an opening (the ramp fires both in the same frame, as the Rules
    /// Proof's R1 reads it: the hydraulic within 0.3 s of the door, never before it); then the close, the latch, the crew in, All aboard!</item>
    /// <item>S8 (the exit): no latch while the ramp stands open (a lock clank with the door open is the wrong sound: the lock belongs to
    /// the shut) and no jump-landing thud (the crew walks down the ramp; the ramp foot's step clank is their sound).</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttlePolishProof
    {
        /// <summary>The latches played after t0 while the ramp stood open (after its opening began, before its closing began), and the two
        /// moments (unscaled time; -1 = not in the log).</summary>
        static int LatchesWhileOpen(float t0, out float openAt, out float closeAt)
        {
            openAt = closeAt = -1f; var latches = new List<float>();
            foreach (var e in OzSoundLog.Since(t0))
            {
                if (e.outcome == null) continue;
                if (e.src == "DOOR" && e.key == "shuttle_ramp")
                {
                    if (e.outcome.StartsWith("ramp opening") && openAt < 0f) openAt = e.t;
                    else if (e.outcome.StartsWith("ramp closing") && openAt >= 0f && closeAt < 0f) closeAt = e.t;
                }
                else if (e.key == "latch" && e.src != "DOOR" && e.outcome.StartsWith("PLAY")) latches.Add(e.t);
            }
            int n = 0;
            foreach (var t in latches) if (openAt >= 0f && t > openAt && (closeAt < 0f || t < closeAt)) n++;
            return n;
        }

        /// <summary>S8: the latches while the ramp stood open and the robots' jump-landing thuds since t0 (both must be 0), logged and checked.</summary>
        void ExitOrder(float t0)
        {
            int open = LatchesWhileOpen(t0, out float openAt, out float closeAt), land = Played(t0, "rob_land");
            Log($"S8 the ramp opened at {openAt - T0:F2}, began to close at {closeAt - T0:F2}; latches while it stood open {open}, jump-landing thuds {land}; the exit cue {Played(t0, "exit")}, ramp steps {Played(t0, "crew_step")}");
            Check("S8 the exit's sounds in their order: no latch while the ramp stands open (the lock only at the shut), no jump-landing thud (they walk down the ramp)", openAt > 0f && closeAt > openAt && open == 0 && land == 0);
        }
    }
}
