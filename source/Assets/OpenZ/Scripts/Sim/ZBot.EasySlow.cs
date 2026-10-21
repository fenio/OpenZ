using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// difficulty is Easy (ZBotDifficulty.Easy: human vs computer on EASY; MEDIUM and HARD play the Zod computer, ZBotDifficulty.Normal,
    /// which never reaches a line below), every timer x <see cref="ZWorld.EasyDecisionSlow"/>:
    /// <list type="bullet">
    /// <item>THE ORDER ROUNDS (zbot.cpp's planner: which idle units leave the factories and the fort, for which flag, crate, empty vehicle,
    /// repair station or enemy fort) run on a clock of their own: one look every AI_INTERVAL x EASY_AI_INTERVAL_SCALE x 1.3 = 2.6 s (was
    /// 2 s), the round delays x 1.3 (13 / 16.25 / 26 / 39 s by the flag tier, was 10 / 12.5 / 20 / 30 s), the first round no sooner than
    /// EASY_START_DELAY x 1.3 = 58.5 s (was 45 s). On the 2.6 s clock every gap between two rounds is exactly 1.3 x today's (13 / 18.2 /
    /// 26 / 39 s, was 10 / 14 / 20 / 30 s), the first round 59.8 s (was 46 s): new units stand longer at their factory before they are sent.</item>
    /// <item>THE FORT FINISH (DV60) is looked at on the same slower clock (every 2.6 s, was 2 s).</item>
    /// <item>
    /// window given up (was 2:00). A group that may leave stands at its fort 30 % longer: it leaves no sooner than its window's opening +
    /// 1.3 x the time it took to get ready (all standing, or the end of its production cycle); nobody joins in that extra time (the units
    /// built then are the planner's, as after a group left today); when the time is up it leaves as today's rule says (all standing, else
    /// at its cycle's end on the slower clock; with too few members left the window is given up). A marching group looks at its target
    /// every 6.5 s (was 5 s; on the rule's 1 s step that is every 7 s).</item>
    /// <item>THE IDLE SWEEP (DV70): every 9:06 (was 7:00), a unit counts as forgotten after 117 s standing (was 90 s).</item>
    /// </list>
    /// NOT changed: production (what a factory builds and when: the 2 s production look and its 45 s start stay as they were), build times,
    /// unit stats, every unit-level rule (targeting, firing, dodging, a unit's own AI, a strike group's march and pace), the Easy limits (2
    /// units a round, 2 on hostile orders), the human's side, MEDIUM / HARD, computer vs computer. Deterministic: game time only, no random
    /// draw. Switch <see cref="ZWorld.EasyCpuSlowerOn"/> (code switch, on): off = today's Easy computer tick for tick. Deviation row DV127.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>K434: the switch (code switch, on). Off = today's Easy computer, tick for tick. MEDIUM / HARD never read it.</summary>
        public static bool EasyCpuSlowerOn = true;
        /// <summary>K434: how much slower the Easy computer takes its big decisions (every delay x this, the decision rate / this).</summary>
        public const double EasyDecisionSlow = 1.3;

        /// <summary>K434: the idle sweep's (DV70) time scale: <see cref="EasyDecisionSlow"/> while an Easy computer plays here (of this team
        /// when one is named), else exactly 1.</summary>
        internal double IdleSweepSlow(Team? team = null)
        {
            foreach (var b in Bots) if (!b.Ignored && b.EasySlowActive && (team == null || b.Team == team.Value)) return EasyDecisionSlow;
            return 1.0;
        }
    }

    public sealed partial class ZBot
    {
        /// <summary>K434: at work for this computer player (the switch on and the player Easy).</summary>
        public bool EasySlowActive => ZWorld.EasyCpuSlowerOn && Difficulty == ZBotDifficulty.Easy;
        /// <summary>K434: the factor on this computer player's big-decision timers: 1.3 while at work, else exactly 1.</summary>
        public double EasySlowScale => EasySlowActive ? ZWorld.EasyDecisionSlow : 1.0;

        double nextPlanTime;                                                                  // K434: the Easy planner's own clock
        int sgHoldGroup = -1; double sgHoldUntil; string sgHoldHow;                          // K434: the strike group that stands longer
        /// <summary>K434 statistics (checks): looks of the slower planner that passed the start; strike groups that stood longer, and per
        /// group (window opened, ready to leave, left) in game seconds (left = -1 while it still stands or when it was given up).</summary>
        public int EasyPlanLooks { get; private set; }
        /// <summary>The game time of this computer player's latest order round (zbot.cpp's last_order_time; 0 before the first). Checks and the Play watch.</summary>
        public double LastRoundAt => lastOrderTime;
        public int EasyStrikeHolds { get; private set; }
        public readonly List<(int group, double opened, double ready, double left)> EasyStrikeHoldLog = new List<(int, double, double, double)>();

        /// <summary>K434, ZBot.Tick before the planner's own tick: false = not at work (today's tick below runs, untouched). At work: the
        /// production look keeps its 2 s clock and its 45 s start; the order round, the idle groups (Normal only, returns at once) and the fort
        /// finish run on the 1.3 x slower clock with the 1.3 x later start; both in today's order when they fall on the same tick.</summary>
        bool EasySlowTick()
        {
            if (!EasySlowActive) return false;
            bool prod = W.Time + TIME_EPS >= nextAiTime, plan = W.Time + TIME_EPS >= nextPlanTime;
            if (!prod && !plan) return true;
            double tick = AI_INTERVAL * EASY_AI_INTERVAL_SCALE;
            if (prod) nextAiTime = W.Time + tick;
            if (plan) nextPlanTime = W.Time + tick * ZWorld.EasyDecisionSlow;
            Purge();
            if (Ignored) return true;
            if (plan && W.Time + TIME_EPS >= EASY_START_DELAY * ZWorld.EasyDecisionSlow)
            {
                EasyPlanLooks++;
                Stage1AI_3();                                                                 // the round's delay x 1.3 is read inside (EasySlowScale)
                CommitIdleGroups();
                FinishGame();
            }
            if (prod && W.Time + TIME_EPS >= EASY_START_DELAY) ChooseBuildOrders();           // production: today's clock and start
            return true;
        }

        // ------------------------------------------------------------------ strike groups (ZBot.StrikeGroup.cs hooks)
        /// <summary>K434: the forming group stands its extra time (nobody joins it meanwhile). Only while the rule is at work: a difficulty
        /// changed during the game away from EASY ends the extra time at once (today's rule from that step on).</summary>
        bool SgEasyHeld => EasySlowActive && sgHoldGroup == sgGroup && sgPhase == StrikePhase.Forming;

        /// <summary>K434, StrikeFormingStep: the group may leave now. Every computer but an Easy one leaves at once (today's rule). The Easy one
        /// first stands until its window's opening + 1.3 x the time it took to get ready.</summary>
        void StrikeGoEasy(string how)
        {
            if (!EasySlowActive) { StrikeGo(how); return; }
            double until = sgOpened + (W.Time - sgOpened) * ZWorld.EasyDecisionSlow;
            if (W.Time + TIME_EPS >= until)
            {
                if (sgMembers.Count >= ZWorld.StrikeMinMembers) StrikeGo(how);
                else StrikeEnd(StrikeEventKind.GivenUp, "the cycle is over with fewer than " + ZWorld.StrikeMinMembers + " members", true);
                return;
            }
            sgHoldGroup = sgGroup; sgHoldUntil = until; sgHoldHow = how; EasyStrikeHolds++;
            if (EasyStrikeHoldLog.Count < 400) EasyStrikeHoldLog.Add((sgGroup, sgOpened, W.Time, -1));
            Log.Add($"{W.Time:F0}s {Team} DV107 strike group {sgGroup}: ready ({how}); the Easy computer stands {until - W.Time:F0} s longer (K434)");
        }

        /// <summary>K434, StrikeFormingStep: true while the extra time runs (the step ends there). When it is up the group leaves as today's
        /// rule says: every building delivered and all standing, else at the end of its cycle on the slower clock; with fewer than
        /// StrikeMinMembers members then the window is given up.</summary>
        bool SgEasyHolding(bool enough, bool delivered, bool allStand)
        {
            if (!SgEasyHeld) return false;
            double end = sgOpened + Math.Max(sgDeadline - sgOpened, 0) * ZWorld.EasyDecisionSlow;
            bool timeUp = W.Time + TIME_EPS >= sgHoldUntil;
            if (timeUp && enough && delivered && allStand) { SgEasyLeft(true); StrikeGo(sgHoldHow); return true; }
            if (!timeUp || W.Time + TIME_EPS < end) return true;
            SgEasyLeft(enough);
            if (enough) StrikeGo("the cycle is over");
            else StrikeEnd(StrikeEventKind.GivenUp, "the cycle is over with fewer than " + ZWorld.StrikeMinMembers + " members", true);
            return true;
        }

        void SgEasyLeft(bool leaves)
        {
            sgHoldGroup = -1;
            if (!leaves) return;
            for (int i = EasyStrikeHoldLog.Count - 1; i >= 0; i--)
                if (EasyStrikeHoldLog[i].group == sgGroup) { var e = EasyStrikeHoldLog[i]; EasyStrikeHoldLog[i] = (e.group, e.opened, e.ready, W.Time); break; }
        }
    }
}
