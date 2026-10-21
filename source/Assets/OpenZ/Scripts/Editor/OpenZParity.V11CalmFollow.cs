#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV109 "calm follow behind a slower unit" (the rule: Sim/ZPathing.CalmFollow.cs). Its probe runs two cases
    /// of the kit's counted check (Editor/OzKC1bFollowCount.cs) with the rule off and on: a mech that catches up with a heavy tank on the
    /// Desert road, and the Level 16 case by the red fort (the heavy tank ahead, the mech behind it, one order west along the road).</summary>
    public static partial class OpenZParity
    {
        /// <summary>The Deviations block's probes run without calm follow (a Play session leaves it on); the row's own probe sets it.</summary>
        static IDisposable CalmFollowOffScope()
        {
            bool was = ZWorld.CalmFollowDeviation;
            ZWorld.CalmFollowDeviation = false;
            return new FollowRestore(() => ZWorld.CalmFollowDeviation = was);
        }
        sealed class FollowRestore : IDisposable { readonly Action a; public FollowRestore(Action a) { this.a = a; } public void Dispose() => a(); }

        static void AddCalmFollowDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass = true;
            try
            {
                var parts = new List<string>();
                using (OzV11Kit.GameSwitches())
                {
                    foreach (var (lv, road, lx, ly, gx, gy, behind, delay) in new[] { ("L02", "Desert road", 52, 18, 52, 44, 8.0, 1.0), ("L16", "Jungle road by the red fort, westward", 51, 104, 34, 104, 5.0, 0.0) })
                    {
                        ZWorld.CalmMoveDeviation = true; ZWorld.OvertakeDeviation = false;
                        ZWorld.CalmFollowDeviation = false; var a = OzKC1bFollowCount.Case(lv, "heavy", "medium", lx, ly, gx, gy, behind, delay);
                        ZWorld.CalmFollowDeviation = true; var b = OzKC1bFollowCount.Case(lv, "heavy", "medium", lx, ly, gx, gy, behind, delay);
                        ZWorld.CalmFollowDeviation = false;
                        bool ok = b.Stops == 0 && b.SpeedChanges < a.SpeedChanges && b.Reversals <= a.Reversals && b.GapOk && b.TPair <= a.TPair * 1.03 && b.Arrived;
                        pass &= ok;
                        parts.Add($"{road}, a mech {behind:0} tiles behind a heavy tank: stops {a.Stops} -> {b.Stops}, speed changes {a.SpeedChanges} -> {b.SpeedChanges}, reversals {a.Reversals} -> {b.Reversals}, least centre gap {a.GapMin:0} -> {b.GapMin:0} px (spacing {b.Spacing:0}), the pair arrived {a.TPair:0.0} -> {b.TPair:0.0} s{(ok ? "" : " (FAIL)")}");
                    }
                }
                measured = string.Join("; ", parts);
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            bool exact = getInt("oz_zod_exact", 0) != 0;
            list.Add(new Deviation
            {
                id = "DV109", change = "Calm follow: a unit behind a slower one going its way eases to its pace at a steady gap",
                zod = "units have no collision (research 7.4): a faster unit drives through a slower one at its own speed",
                openz = "Sim/ZPathing.CalmFollow.cs (GateCore, first): a unit with a unit right ahead in its lane going its way that is slower by its own speed (another team too, never its own squad, a pair that may overlap, or a unit that itself waits or queues) " +
                        $"eases its speed to that unit's pace at the gate's spacing + {ZPathing.FollowMargin} px + {ZPathing.FollowBody} of its own body length of clear road (closing to the spacing over the last {ZPathing.FollowLeadRamp} px of the slower unit's way; in a crowd the calm movement rule's closer spacing): " +
                        $"the slower unit's speed + {ZPathing.FollowKp} per second of the gap error, at least {ZPathing.FollowFloor} of its pace while the gap opens, at most what it can brake from within the gap, " +
                        $"changing by at most {ZPathing.FollowAccel} px/s per second up and {ZPathing.FollowBrake} down; only its step gets shorter (heading, path and order untouched); not counted as pressed by its team; a unit ahead that stands {ZPathing.FollowStill} s is let go to the gate's own rules; " +
                        "with the option FAST UNITS PASS SLOW ONES on, a unit the passing rule may take goes round as that rule says. Before: full speed to the spacing, then the step cut at once, again and again (stop and go), pressed against the slower unit's back",
                measured = measured,
                toggle = "ZWorld.CalmFollowDeviation: View/ZGame.cs sets it at every level start (on unless 'Zod exact'); off in the parity rows and every counted check unless they set it",
                status = !pass ? DevCheck : !exact ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
