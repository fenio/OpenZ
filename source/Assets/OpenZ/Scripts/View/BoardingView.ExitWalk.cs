using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// A figure's walk out of a carrier is never cut short by its robot's fight.
    /// Before: StepGhost squeezed whatever was left of an exit walk into <see cref="catchUp"/> (0.2 s) the moment its robot fired or was shot
    /// at (Acted: every Hitscan / MissileFired by or at it sets hurry) or moved (IsMoving). Under fire that is at once: the shuttle's figures
    /// (bench, aisle, doorway, ramp, the walk to the back slot: 2.5 s + the walk) crossed the bay and the ramp in 0.2 s, the APC's file
    /// (up to 3.2 s each), a tank crew climbing out of its hatch (1.1 s), a gunner off his seat (0.6-1.1 s) the same. Same code for every
    /// carrier and robot type, the heroes too.
    /// Now (switch <see cref="ExitWalkKeepOn"/>, on):
    ///  * the walk plays at its own pace to its end, whatever its robot does (no squeeze). The shuttle and the APC: rule DV132
    ///    (Sim/ZObject.ExitWalk.cs) keeps the robot out of the fight until its figure stands on its spot (no shot, no step, not a target);
    ///    a walk that would end after the rules' out moment (the APC's walk round its hull) plays the rest of its way evenly faster
    ///    (at most <see cref="ExitDuePaceMax"/> x), so the figure is there first;
    ///  * the last stretch, the walk on to the robot, arrives on time but follows the robot if it moved meanwhile (Zod's own crews may), at
    ///    most <see cref="ExitChaseMps"/> (the running clip at the pace drawn), and ends ON it: the robot's own model takes over with no gap;
    ///    over the walk's last <see cref="ExitFaceBlendSec"/> the figure turns to the facing that model has (its heading or its aim), so the
    ///    hand-over has no turn either, and the fight's own pose follows from there;
    ///  * a shot its robot fires while the figure still walks (Zod's own crews: the rules let them fire at once; a few hundredths at the end
    ///    of a DV132 walk; a figure saved off a destroyed shuttle's ramp walking on to its robot, K418) is held and drawn from the robot's own model once the walk is over (at most <see cref="ExitHeldMax"/>,
    ///    <see cref="ExitReleaseGapSec"/> apart, with its sound; the rules dealt the damage at the time, as for a knocked-down robot's held
    ///    shots); a killing shot is drawn at once (its target's death plays now).
    /// The ramp foot's step clank, the doorway timing (K418), the ramp's speeds (K422) and the boarding walks are unchanged.
    /// the robots acted in the rules 0.6-1.3 s before their figures stood there): the shuttle's walk from the ramp foot to the robot was
    /// measured when the squad is let out, in BoardingView.Update (order 70), where the ship's transform still holds UnitView.Sync's own
    /// vehicle pose (order 60, the hull's steering yaw); OzPlayableShuttle (order 200) then draws the ship at the rules' heading, and the walk
    /// is drawn on that ship (LateUpdate). After a landing turn the two differ (about 105 deg in that proof): the foot was measured 7.3 m from
    /// where it is drawn and every walk on was keyed 2.0-2.2 s too long. Now (switch <see cref="ExitReplanOn"/>, on) that walk is planned
    /// again on the ship as drawn, as the figure steps off the ramp's tip (<see cref="ExitWalkReplan"/>), at rule DV132's own pace
    /// (ZWorld.ExitWalkMps, the last leg at least ZWorld.ExitWalkMinSec): the walk the rule counts. The pace-up lead is the rule's hand-over
    /// (<see cref="ExitDueLeadSec"/> = ZWorld.ExitHandSec): each figure stands on its spot at least that long before its robot may act.
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>On: exit walks play out (off: squeezed into catchUp when the robot acts, as before).</summary>
        public static bool ExitWalkKeepOn = true;
        /// <summary>K441b, on: the shuttle's walk from the ramp foot to the robot is planned again on the ship as drawn, at rule DV132's pace
        /// (off: as K441 keyed it when the squad was let out).</summary>
        public static bool ExitReplanOn = true;
        /// <summary>The fastest the figure walks on to a robot that moved meanwhile (m/s; the running clip x1.4); the walk then ends later, on it.</summary>
        public static float ExitChaseMps = 4.5f;
        /// <summary>Over the walk's last seconds the figure turns to its robot's own facing.</summary>
        public static float ExitFaceBlendSec = 0.35f;
        /// <summary>Safety: this long past the walk's own end the figure is handed over wherever it is (s).</summary>
        public static float ExitChaseMaxSec = 6f;
        /// <summary>Held shots per robot while its figure walks out, and the gap between them when they are drawn (s, game time).</summary>
        public static int ExitHeldMax = 3;
        public static float ExitReleaseGapSec = 0.12f;
        /// <summary>Rule DV132 holds the robot until a moment the rules fix (ZObject.ExitOutAt). A walk that would end later than that (the APC's
        /// walk round its hull may take up to detourTime more than the rules' 3.2 s; a shuttle slot off the back line) plays the rest of its
        /// way evenly faster, at most this many times its own pace, so the figure stands on its robot <see cref="ExitDueLeadSec"/> before the
        /// robot may act (K441b: the rule's own hand-over, ZWorld.ExitHandSec, was 0.05 s). The shuttle's walk is paced only from its ramp foot
        /// on (the bay, the doorway and the ramp keep the rules' moments).</summary>
        public static float ExitDuePaceMax = 1.4f, ExitDueLeadSec = (float)ZWorld.ExitHandSec;

        /// <summary>Proof counters (all walks): squeezes the old rule would have made, walks that followed a robot past their own end, walks
        /// ended on their robot; shots held / drawn after the walk / dropped (over the cap, or the walk aborted) / killing shots drawn at once;
        /// walks paced up to be out by the rules' moment.</summary>
        public static int ExitSqueezesSkipped, ExitChased, ExitWalksEnded, ExitShotsHeld, ExitShotsReleased, ExitShotsDropped, ExitShotsNotHeld, ExitPacedUp;
        /// <summary>Proof: the largest step of an exit figure in one frame (m, flat) and as a pace (m/s of game time); the gap and the turn
        /// between the figure and its robot's model at the last hand-over and the worst so far; the highest pace factor given to be out on time.</summary>
        public static float ExitMaxStepM, ExitMaxMps, ExitLastHandGapM, ExitLastHandTurnDeg, ExitWorstHandGapM, ExitWorstHandTurnDeg, ExitPaceMaxSeen;
        /// <summary>K441b proof: shuttle walks from the ramp foot planned again on the ship as drawn, the most time that took off such a walk
        /// (s, as keyed at the let-out less as walked) and the most it added, and one line per walk (newest last, at most 32).</summary>
        public static int ExitReplanned;
        public static float ExitReplanCutMaxS, ExitReplanAddMaxS;
        public static readonly List<string> ExitReplanLog = new List<string>();

        readonly HashSet<Ghost> exitSqueezeSeen = new HashSet<Ghost>(), exitChased = new HashSet<Ghost>(), exitReplanned = new HashSet<Ghost>();
        readonly Dictionary<Ghost, double> exitDue = new Dictionary<Ghost, double>();
        readonly Dictionary<int, List<ZEvent>> exitHeld = new Dictionary<int, List<ZEvent>>();
        readonly Dictionary<int, GameObject> exitDoorWalkers = new Dictionary<int, GameObject>();
        CombatView exitCombat;

        /// <summary>Proof: every K441 / K441b counter back to 0.</summary>
        public static void ExitWalkStatsReset()
        {
            ExitSqueezesSkipped = ExitChased = ExitWalksEnded = ExitShotsHeld = ExitShotsReleased = ExitShotsDropped = ExitShotsNotHeld = ExitPacedUp = 0;
            ExitMaxStepM = ExitMaxMps = ExitLastHandGapM = ExitLastHandTurnDeg = ExitWorstHandGapM = ExitWorstHandTurnDeg = ExitPaceMaxSeen = 0f;
            ExitReplanned = 0; ExitReplanCutMaxS = ExitReplanAddMaxS = 0f; ExitReplanLog.Clear();
        }

        /// <summary>StepGhost: does an exit walk get squeezed this frame? acted = the old test (its robot fired, was shot at or moved).</summary>
        bool ExitWalkActs(Ghost g, bool acted)
        {
            if (!ExitWalkKeepOn || g.demo) return acted;
            if (acted && exitSqueezeSeen.Add(g)) ExitSqueezesSkipped++;
            ExitWalkReplan(g);
            ExitWalkDuePace(g);
            return false;
        }

        /// <summary>K441b: the shuttle's walk from the ramp foot to its robot planned again on the ship AS DRAWN, once, as the figure steps off
        /// the ramp's tip (StepGhost runs in LateUpdate: the ship stands where OzPlayableShuttle draws it, parked by then). BuildShuttleExit keyed
        /// it when the squad was let out, in Update, on UnitView.Sync's own vehicle pose of the ship (see the class summary). The legs after the
        /// ramp foot are walked again (WalkTo: round the hull only when the way to the robot crosses it) and timed at rule DV132's own pace,
        /// ZWorld.ExitWalkMps, the last leg at least ZWorld.ExitWalkMinSec (OzExitWalkPace.LegSec): the walk the rule counts for its out moment.
        /// Everything up to the ramp foot (the rules' moment, K418's doorway, K422's paces) is not touched.</summary>
        void ExitWalkReplan(Ghost g)
        {
            if (!ExitReplanOn || g.enter || g.demo || g.footT <= 0f || !g.live || g.keys.Count < 3 || exitReplanned.Contains(g)) return;
            int footK = -1; float acc = 0f;
            for (int i = 1; i < g.keys.Count; i++)
            {
                acc += g.keys[i].dur;
                if (Mathf.Abs(acc - g.footT) <= 1e-3f) { footK = i; break; }
                if (acc > g.footT + 1e-3f) break;
            }
            if (footK < 1 || footK >= g.keys.Count - 1) { exitReplanned.Add(g); return; }                   // not a shuttle exit as BuildShuttleExit keys it: left alone
            if (g.time < g.footT - g.keys[footK].dur) return;                                              // still in the bay or on the ramp: planned as it steps off the tip
            exitReplanned.Add(g);
            System.Func<Vector3> foot = g.keys[footK].at, to = g.keys[g.keys.Count - 1].at;
            if (foot == null || to == null) return;
            float before = g.total - g.footT, detour = g.detour;
            g.keys.RemoveRange(footK + 1, g.keys.Count - footK - 1);
            WalkTo(g, foot, to, minLast: (float)ZWorld.ExitWalkMinSec);
            g.detour = detour;                                                                              // the shuttle's walk has no detour budget (no Finish)
            float mps = (float)ZWorld.ExitWalkMps, path = 0f;
            for (int i = footK + 1; i < g.keys.Count; i++)
            {
                float len = Dist(g.keys[i - 1].at, g.keys[i].at); path += len;
                g.keys[i].dur = Mathf.Max(0.02f, (float)OzExitWalkPace.LegSec(len, mps, i == g.keys.Count - 1 ? ZWorld.ExitWalkMinSec : 0.02));
            }
            g.total = Sum(g.keys);
            float after = g.total - g.footT;
            ExitReplanned++;
            ExitReplanCutMaxS = Mathf.Max(ExitReplanCutMaxS, before - after); ExitReplanAddMaxS = Mathf.Max(ExitReplanAddMaxS, after - before);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            ExitReplanLog.Add($"#{g.robotId}: from the ramp foot {path.ToString("0.00", inv)} m in {g.keys.Count - footK - 1} leg(s) at {mps.ToString("0.0", inv)} m/s: {after.ToString("0.00", inv)} s (keyed at the let-out {before.ToString("0.00", inv)} s)");
            if (ExitReplanLog.Count > 32) ExitReplanLog.RemoveAt(0);
        }

        /// <summary>Rule DV132's out moment (read once, while the rules still hold the robot): the rest of the walk evenly faster when its own
        /// pace would end it later than the lead before that moment (at most ExitDuePaceMax; never a squeeze; OzExitWalkPace.DueSpeedMul).</summary>
        void ExitWalkDuePace(Ghost g)
        {
            if (g.enter || !g.live || !game || game.World == null) return;
            if (!exitDue.TryGetValue(g, out double due))
            {
                var o = g.live.Obj;
                exitDue[g] = due = o != null && ZWorld.ExitWalkHoldOn && o.ExitOutAt >= 0 ? o.ExitOutAt : -1;
            }
            if (due < 0 || (g.footT > 0f && g.time < g.footT)) return;                                    // no DV132 hold; the shuttle: from its ramp foot on
            float rest = g.total - g.time, left = (float)(due - ExitDueLeadSec - game.World.Time);
            float need = (float)OzExitWalkPace.DueSpeedMul(rest, left, g.speedMul, ExitDuePaceMax);
            if (need <= g.speedMul) return;
            if (g.speedMul <= 1f) ExitPacedUp++;
            g.speedMul = need; ExitPaceMaxSeen = Mathf.Max(ExitPaceMaxSeen, need);
        }

        /// <summary>StepGhost, right after Place: the walk on to the robot follows it and ends on it, the figure turning to the robot's own
        /// facing at the end; `from` = where the figure stood last frame, dt = game time.</summary>
        void ExitWalkSmooth(Ghost g, Vector3 from, float dt)
        {
            if (!ExitWalkKeepOn || g.enter || g.demo || g.done || !g.go || !g.live || g.keys.Count < 2) return;
            var tr = g.go.transform;
            var last = g.keys[g.keys.Count - 1];
            float left = g.total - g.time, leftT = left / Mathf.Max(1f, g.speedMul);                      // walk time left; the same in game time (paced up: ExitWalkDuePace)
            if (dt > 0f && left <= last.dur)
            {   // the walk on to the robot (BuildExit / BuildShuttleExit / BuildApcExit end with WalkTo to the robot's model)
                var goal = Ground(g.live.transform.position);
                var flat = goal - from; flat.y = 0f; float d = flat.magnitude;
                float step = (float)OzExitWalkPace.LastStep(d, Mathf.Max(0f, leftT), dt, ExitChaseMps);
                var p = Ground(d > 1e-5f ? from + flat / d * step : goal);
                float after = Mathf.Max(0f, d - step), v = step / dt;
                if (left < 0f && after > (float)OzExitWalkPace.ReachM && exitChased.Add(g)) ExitChased++;
                if (d > 0.05f) g.rot = Quaternion.Slerp(g.rot, Quaternion.LookRotation(-flat / d, Vector3.up), 1f - Mathf.Exp(-dt * 14f));   // robot front = -z
                float w = (float)OzExitWalkPace.FaceBlend(Mathf.Max(leftT, after / Mathf.Max(0.5f, ExitChaseMps)), ExitFaceBlendSec);
                tr.SetPositionAndRotation(p, Quaternion.Slerp(g.rot, g.live.transform.rotation, w));
                // the clip Place chose for this key (the same name: no second cross-fade in the frame), its speed from the pace drawn
                float vKey = (last.at() - g.keys[g.keys.Count - 2].at()).magnitude / Mathf.Max(0.05f, last.dur);
                bool run = vKey > 2.4f;
                float spd = Mathf.Clamp(v / (run ? RunClipSpeed : WalkClipSpeed), 0.6f, 2.5f);
                string now = g.clipNow; Play(g.anim, ref now, run ? "run_forward" : "walking", spd * SimRate); g.clipNow = now;
                Band(g.skin, tr, g.hips, tr.localScale.x);
            }
            if (dt > 0f && g.go.activeInHierarchy)
            {
                var mv = tr.position - from; mv.y = 0f; float m = mv.magnitude;
                if (m > ExitMaxStepM) ExitMaxStepM = m;
                if (m / dt > ExitMaxMps) ExitMaxMps = m / dt;
            }
        }

        /// <summary>StepGhost: the walk's time is up; an exit walk ends only on its robot (or ExitChaseMaxSec past its end).</summary>
        bool ExitWalkReached(Ghost g)
        {
            if (!ExitWalkKeepOn || g.enter || g.demo || !g.go || !g.live) return true;
            var a = g.go.transform.position; var b = g.live.transform.position; a.y = 0f; b.y = 0f;
            return OzExitWalkPace.Reached(Vector3.Distance(a, b), g.time - g.total, ExitChaseMaxSec);
        }

        /// <summary>EndGhost, right after the robot's own model is shown again: the hand-over measured, the shots held meanwhile drawn.</summary>
        void ExitWalkEnded(Ghost g, bool abort)
        {
            exitSqueezeSeen.Remove(g); exitChased.Remove(g); exitDue.Remove(g); exitReplanned.Remove(g);
            if (g.enter || g.demo) return;
            if (!abort && g.go && g.live)
            {
                var a = g.go.transform.position; var b = g.live.transform.position; a.y = 0f; b.y = 0f;
                ExitLastHandGapM = Vector3.Distance(a, b); ExitLastHandTurnDeg = Quaternion.Angle(g.go.transform.rotation, g.live.transform.rotation);
                ExitWorstHandGapM = Mathf.Max(ExitWorstHandGapM, ExitLastHandGapM); ExitWorstHandTurnDeg = Mathf.Max(ExitWorstHandTurnDeg, ExitLastHandTurnDeg);
                ExitWalksEnded++;
            }
            if (g.robotId >= 0 && exitHeld.TryGetValue(g.robotId, out var list))
            {
                exitHeld.Remove(g.robotId);
                if (!abort && g.live && !g.live.Dying && list.Count > 0) StartCoroutine(ExitRelease(list));
                else ExitShotsDropped += list.Count;
            }
        }

        IEnumerator ExitRelease(List<ZEvent> list)
        {
            foreach (var e in list)
            {
                if (!exitCombat) exitCombat = FindFirstObjectByType<CombatView>();
                var a = game && game.World != null ? game.World.GetAny(e.objId) : null;
                if (exitCombat && a != null && !a.Destroyed && a.KillAt < 0) { exitCombat.DrawHeld(e); ExitShotsReleased++; }
                else ExitShotsDropped++;
                float t = 0f;
                while (t < ExitReleaseGapSec) { t += Time.deltaTime * SimRate; yield return null; }
            }
        }

        /// <summary>CombatView.DrawShot: a robot whose figure is still walking out of a carrier fired; true = the shot is held and drawn from
        /// its own model when the walk is over (a killing shot cannot wait: false, drawn now).</summary>
        public static bool HoldShotWhileExiting(ZEvent e, ZObject a, ZObject t)
        {
            var bv = I;
            if (!ExitWalkKeepOn || !bv || a == null || a.Kind != ZKind.Robot) return false;
            Ghost g = null;
            foreach (var x in bv.ghosts) if (!x.enter && !x.demo && !x.done && x.go && x.live && x.robotId == a.Id) { g = x; break; }
            if (g == null && !(bv.exitDoorWalkers.TryGetValue(a.Id, out var walker) && walker)) return false;   // no exit walk, no K418 walker (a figure saved off a destroyed shuttle's ramp)
            if (t == null || WeaponsV2.KillRoll(e, t)) { ExitShotsNotHeld++; return false; }               // the killing roll (a death, a sniped driver) plays now
            if (!bv.exitHeld.TryGetValue(a.Id, out var list)) bv.exitHeld[a.Id] = list = new List<ZEvent>();
            if (list.Count < ExitHeldMax) { list.Add(e); ExitShotsHeld++; } else ExitShotsDropped++;
            return true;
        }

        /// <summary>K418's DoorwayWalkOn hook: a figure saved off a destroyed shuttle's ramp walks on to its robot (its robot's model hidden until
        /// it arrives): its robot's shots wait for it too, and are drawn when it is there.</summary>
        void ExitWalkDoorWalker(int robotId, GameObject go)
        {
            if (!ExitWalkKeepOn || robotId < 0 || !go) return;
            exitDoorWalkers[robotId] = go;
            StartCoroutine(ExitDoorWalkerWatch(robotId, go));
        }

        IEnumerator ExitDoorWalkerWatch(int robotId, GameObject go)
        {
            while (go) yield return null;                                                                     // the walk destroys its figure when it is there
            if (exitDoorWalkers.TryGetValue(robotId, out var x) && x == go) exitDoorWalkers.Remove(robotId);
            if (!exitHeld.TryGetValue(robotId, out var list)) yield break;
            exitHeld.Remove(robotId);
            var v = View(robotId);
            if (v && !v.Dying && list.Count > 0) StartCoroutine(ExitRelease(list)); else ExitShotsDropped += list.Count;
        }

        /// <summary>Proof: 0 = no exit walk for that robot, 1 = its walk waits with the figure not drawn (a hatch / seat / door figure
        /// waiting its turn), 2 = its figure is drawn at pos / rot.</summary>
        public static int ExitWalkFigure(int robotId, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity; var bv = I; if (!bv) return 0;
            foreach (var x in bv.ghosts)
            {
                if (x.enter || x.demo || x.done || !x.go || !x.live || x.robotId != robotId) continue;
                if (!x.go.activeInHierarchy) return 1;
                pos = x.go.transform.position; rot = x.go.transform.rotation; return 2;
            }
            return 0;
        }

        /// <summary>Proof: the robots that have an exit walk now.</summary>
        public void ExitWalkIds(List<int> into)
        {
            into.Clear();
            foreach (var x in ghosts) if (!x.enter && !x.demo && !x.done && x.go && x.live && x.robotId >= 0) into.Add(x.robotId);
        }
    }
}
