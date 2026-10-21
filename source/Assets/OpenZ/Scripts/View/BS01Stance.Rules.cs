using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV122 (Sim/ZObject.ProneTarget.cs): the sniper's stance is decided by the rules; while they decide it (<see cref="RulesDraw"/>)
    /// Drive() hands the sniper's frame to <see cref="RuleSniper"/>, which only DRAWS ZObject.Stance: down (kneel / prone clip) when
    /// the rules put it down, up when they stand it up (faster while the sim walks it), the aim loop and the fire clip (OnFire) in
    /// between, as DriveSniper played them. The rules already keep it out of water, up for a throw or a knockdown, one stance a fight
    /// (a throw the view starts on the very tick the rules stand it up for it is drawn standing at once).
    /// The view's own stance rules (BS01Stance.Steady.cs / SniperStanceGate) are used when the rules do not decide ('Zod exact').
    /// Play check counters: drawn lie-downs and get-ups; times the drawn stance (once it is down or up) differed from the rules' cover
    /// stance for more than a second (<see cref="RuleMismatches"/>; expected 0). K248b's own counters (SniperStanceGate, SteadyCounts)
    /// go on counting the drawn lie-downs and get-ups, so its report menu reads the same as before.
    /// </summary>
    public partial class BS01Stance
    {
        /// <summary>The rules decide this sniper's stance (DV122 on): the view has no decision of its own.</summary>
        bool RulesDraw => obj != null && obj.ProneTargetRule;

        /// <summary>Play report: drawn lie-downs and get-ups under the rules; mismatches of the drawn and the rules' stance longer than
        /// 1 s and their total seconds; per sniper (unit id): drawn lie-downs, get-ups, mismatches.</summary>
        public static int RuleDownsDrawn, RuleUpsDrawn, RuleMismatches;
        public static float RuleMismatchSecs;
        public static readonly Dictionary<int, Vector3Int> RuleCounts = new Dictionary<int, Vector3Int>();
        float ruleOff;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RuleStatics() { RuleCounts.Clear(); RuleDownsDrawn = RuleUpsDrawn = RuleMismatches = 0; RuleMismatchSecs = 0f; }

        bool RuleSniper(bool throwing, ref string viewState, float animMul, float dt)
        {
            var s = obj.Stance;
            string want = throwing ? "" : s == ZStance.Prone ? (Has("sniper_prone_down") ? "prone" : "kneel") : s == ZStance.Kneel ? "kneel" : "";   // a throw (the rules' own: they stand it up for it the same tick) is thrown standing
            RuleWatch(dt);
            switch (st)
            {
                case St.Up:
                    if (want.Length == 0) return false;
                    pose = want;
                    Play($"sniper_{pose}_down", 0.12f, Len($"sniper_{pose}_down"), animMul);
                    st = St.Down; viewState = "stance"; RuleCount(1, 0, 0);
                    return true;
                case St.Down:
                    if (want != pose) return RuleGetUp(throwing);
                    if (t >= len * 0.97f) { Play($"sniper_{pose}_aim", 0.10f, 99f, animMul); st = St.Aim; }
                    return true;
                case St.Aim:
                    if (want != pose) return RuleGetUp(throwing);
                    return true;
                case St.Fire:
                    if (want != pose) return RuleGetUp(throwing);
                    if (t >= len * 0.95f) { Play($"sniper_{pose}_aim", 0.08f, 99f, animMul); st = St.Aim; }
                    return true;
                case St.GetUp:
                    if (t >= len * 0.9f) { st = St.Up; viewState = null; anim.speed = animMul; return false; }
                    return true;
            }
            st = St.Up; return false;
        }

        bool RuleGetUp(bool throwing) { RuleCount(0, 1, 0, throwing); return GetUp(obj.IsMoving); }

        void RuleCount(int down, int up, int off, bool throwing = false)
        {
            RuleDownsDrawn += down; RuleUpsDrawn += up; RuleMismatches += off;
            RuleCounts.TryGetValue(obj.Id, out var c);
            RuleCounts[obj.Id] = new Vector3Int(c.x + down, c.y + up, c.z + off);
            if (down == 0 && up == 0) return;
            SteadyCounts.TryGetValue(obj.Id, out var k);                                          // K248b's report menu counts the drawn stance as before
            if (down > 0) { SniperStanceGate.LieDowns++; k.x++; }
            else if (throwing) SniperStanceGate.GetUpsThrow++;
            else if (obj.IsMoving) { SniperStanceGate.GetUpsWalk++; k.y++; if (SteadyEnemyInRange()) SniperStanceGate.GetUpsWalkInRange++; }
            else { SniperStanceGate.GetUpsLinger++; k.z++; }
            SteadyCounts[obj.Id] = k;
        }

        /// <summary>The drawn stance once it is down (aim / fire) or up, against the rules' cover stance (Stand while it is still going
        /// down); the down / up clips in between are not judged.</summary>
        void RuleWatch(float dt)
        {
            string drawn = st == St.Aim || st == St.Fire ? pose : st == St.Up ? "" : null;
            var c = obj.CoverStance;
            string rule = c == ZStance.Prone ? (Has("sniper_prone_down") ? "prone" : "kneel") : c == ZStance.Kneel ? "kneel" : "";
            if (drawn == null || drawn == rule) { ruleOff = 0f; return; }
            float before = ruleOff;
            ruleOff += dt;
            if (ruleOff >= 1f) { RuleMismatchSecs += before >= 1f ? dt : ruleOff; if (before < 1f) RuleCount(0, 0, 1); }
        }
    }
}
