using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// own switch (off = Zod; 'Zod exact' turns them off, ZGame):
    /// <para>DV53 squad help.
    /// Zod has no retaliation: a robot only agros on an enemy inside its own attack radius + 40 px,
    /// so an attacker that out-ranges the squad (sniper 144, laser 136, heavy 144 vs a grunt's 120) kills it member by member.
    /// Now, when an enemy unit's shot or blast hurts a unit (hitscan hit, missile / grenade damage), every IDLE unit of that side
    /// (no waypoint, no target, not stunned) that can attack the shooter and sees it - its squad mates always, other units when the
    /// victim is inside their own agro radius (attack radius + agro_distance) - gets Zod's AGRO waypoint on the shooter with a leash
    /// long enough to reach it (+32 px): it closes in, fires, and walks back when the shooter leaves the leash (Zod's agro return).
    /// At most one call per victim per second. Orders, fights and moves in progress are never touched; cannons stay put.</para>
    /// <para>DV54 capture run. Zod only
    /// starts a run when the target is within move_speed x stamina and skips one run in five (zobject.cpp:2058-2079). A move onto a
    /// flag and an enter order now always start the run when the unit has stamina (>= 0.3 s); it runs for its max_run_time (Zod's
    /// stamina drain, run_unit_speed 1.8x), then walks; recharge unchanged. Zod's 1-in-5 roll is still drawn where Zod draws it.</para>
    /// <para>DV55 close grab.
    /// DV38 keeps a player-ordered unit where it was sent (no
    /// auto-grab); now a held unit still takes an empty vehicle within <see cref="ZWorld.CloseGrabPx"/> (5 tiles) / a flag within
    /// <see cref="ZWorld.CloseGrabFlagPx"/> (Zod's own flag distance) that it
    /// sees and can reach, and on a human team only the closest eligible idle unit (squad = one unit) goes for any auto-grab.</para>
    /// <para>DV56 CPU commits idle groups. The Zod bot orders only a
    /// 15-35 % wave per round, one unit per target (mutual nearest), so with few targets a big group stands by its fort. Every
    /// <see cref="ZBot.COMMIT_INTERVAL"/> s, when <see cref="ZBot.COMMIT_MIN"/>+ of its units (leaders / vehicles) have stood idle
    /// for <see cref="ZBot.COMMIT_IDLE"/> s, the Normal CPU keeps the <see cref="ZBot.COMMIT_KEEP"/> nearest its fort as a defence
    /// and sends the rest together (attack-move, Zod's bot waypoint) to the foreign flag nearest them, else into the enemy fort
    /// (robots) / at the nearest enemy (vehicles); same bot regions as MatchTargets_3. The Easy CPU keeps Zod's rounds.</para>
    /// Laser bots that did not shoot from a bridge were a DV23 fix, not a deviation: the bridge rail tiles (class Impassable along the
    /// deck) blocked the sight line like a cliff; ZSight now clears every bridge footprint tile and the map's side-face tiles one
    /// tile round it ("bridges never block").
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV53 squad help. PlayerPrefs oz_squad_help.</summary>
        public static bool HelpDeviation = true;
        /// <summary>DV54 capture run. PlayerPrefs oz_capture_run.</summary>
        public static bool CaptureRunDeviation = true;
        /// <summary>DV55 close grab. PlayerPrefs oz_close_grab.</summary>
        public static bool CloseGrabDeviation = true;
        /// <summary>DV56 CPU commits idle groups. PlayerPrefs oz_bot_commit.</summary>
        public static bool BotCommitDeviation = true;
        /// <summary>DV71 the CPU builds Lasers sometimes (ZBot.LaserPick). ZGame turns it on at every start (off in 'Zod exact');
        /// headless checks keep Zod's picks unless they switch it on.</summary>
        public static bool BotLaserDeviation = false;
        public const int HelpLeashExtra = 32, CloseGrabPx = 80;
        /// <summary>DV55: a held unit (DV38) still takes a FLAG it sees within this many px: Zod's own auto_grab_flag_distance
        /// (zsettings.cpp:277, default_settings.txt:244). Empty vehicles keep CloseGrabPx. One unit per flag, the closest, as before.</summary>
        public static int CloseGrabFlagPx = 220;
        public const double HelpCooldown = 1.0;
        /// <summary>Statistics (checks / proofs): help calls, AGRO waypoints they gave, capture runs started.</summary>
        public static int HelpCalls, HelpOrders, CaptureRuns;

        /// <summary>DV53: victim v was just hurt by enemy unit a.</summary>
        internal void HelpCall(ZObject v, ZObject a)
        {
            if (!HelpDeviation || v == null || a == null || !v.IsUnit || !a.IsUnit || a.Destroyed || a.KillAt >= 0) return;
            if (v.Owner == Team.Neutral || a.Owner == Team.Neutral || a.Owner == v.Owner) return;
            if (Time < v.helpNext) return;
            v.helpNext = Time + HelpCooldown;
            HelpCalls++;
            var sq = v.Leader ?? v;
            foreach (var u in Objects)
            {
                if (u.Owner != v.Owner || !u.IsMobile || u.Destroyed || u.KillAt >= 0 || u.IsFortDefender || u.Stunned || u.HoldPosition) continue;   // DV68: holding units stay
                if (CalmStart(u.Owner)) continue;                                                     // DV69 calm start: no walking off to help
                if (u.Waypoints.Count > 0 || u.AttackObject != null || u.MoveSpeed <= 0 || !u.CanAttack() || !u.CanAttackObject(a)) continue;
                bool mate = u == sq || u.Leader == sq;
                int agro = u.AttackRadius + S.agro_distance;
                if (!mate && !ZObject.Within(u.CenterX, u.CenterY, v.CenterX, v.CenterY, agro)) continue;
                if (!u.CanSee(a) || u.PathUnreachable(a)) continue;
                double dx = u.CenterX - a.CenterX, dy = u.CenterY - a.CenterY;
                int leash = Math.Max(0, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)) - agro) + HelpLeashExtra;
                u.Waypoints.Add(new ZWaypoint { mode = WpMode.Agro, refId = a.Id, x = a.CenterX, y = a.CenterY, leash = leash });
                HelpOrders++;
            }
        }
    }

    public partial class ZObject
    {
        /// <summary>
        /// back off, regroup, to a waiting bay: bridge decks, crowds; not in Zod) do not stop a robot from engaging an enemy in range
        /// (Zod's robots engage only when standing, and in Zod they would be standing there), nor does the server's own nudge
        /// (ScuffleUnits' unowned MOVE, zserver.cpp:974-1041: a few px in Zod, a long shuffle on a crowded bridge deck).</summary>
        public bool LayerShuffle
        {
            get
            {
                if (!ZWorld.HelpDeviation || Waypoints.Count == 0 || Waypoints[0].mode != WpMode.Move || Waypoints[0].playerGiven) return false;   // with DV53 ("never stand useless")
                int r = Waypoints[0].refId;
                return (r <= ZPathing.RefStepAside && r >= ZPathing.RefBay) || (r == -1 && !Waypoints[0].attackTo);
            }
        }

        /// <summary>DV53: no new help call for this victim before this time.</summary>
        internal double helpNext;

        /// <summary>DV54: the run toward a flag / an empty vehicle (off = Zod's AttemptStartRun(x, y)).</summary>
        void CaptureRun(int x, int y)
        {
            if (!ZWorld.CaptureRunDeviation || Owner == Team.Neutral) { AttemptStartRun(x, y); return; }
            if (IsRunning) return;
            if (Within(CenterX, CenterY, x, y, (int)(MoveSpeed * Stamina))) World.Rng.Next(5);   // Zod's 1-in-5 roll, drawn where Zod draws it
            if (Stamina < 0.3) return;
            IsRunning = true;
            ZWorld.CaptureRuns++;
        }

        /// <summary>DV55: t is VERY close (a held unit may still take it).</summary>
        bool CloseGrab(ZObject t) => ZWorld.CloseGrabDeviation && Within(CenterX, CenterY, t.CenterX, t.CenterY, ZWorld.CloseGrabPx);
        /// <summary>DV55: flag f is close enough for a held unit (ZWorld.CloseGrabFlagPx).</summary>
        bool CloseGrabFlag(ZObject f) => ZWorld.CloseGrabDeviation && Within(CenterX, CenterY, f.CenterX, f.CenterY, ZWorld.CloseGrabFlagPx);

        /// <summary>DV55: another idle unit of this team that would auto-grab t is closer to it (ties: the lower id goes).</summary>
        bool CloserGrabber(ZObject t, bool enter)
        {
            if (!ZWorld.CloseGrabDeviation) return false;
            double d = Dist(CenterX, CenterY, t.CenterX, t.CenterY);
            int lim = enter ? World.S.auto_grab_vehicle_distance : World.S.auto_grab_flag_distance;
            foreach (var o in World.Objects)
            {
                if (o == this || o.Owner != Owner || o.IsMinion || !o.IsMobile || o.Destroyed || o.KillAt >= 0 || o.IsFortDefender) continue;
                if (o.Waypoints.Count > 0 || o.AttackObject != null || !o.CanMove || !o.CanAttack() || o.Stunned || o.HoldPosition) continue;   // DV68
                if (enter && (!o.IsRobot || o.NoAutoEnter || (o.JustLeftCannon && t.Kind == ZKind.Cannon))) continue;
                double od = Dist(o.CenterX, o.CenterY, t.CenterX, t.CenterY);
                if (od > d || (od == d && o.Id > Id) || od > lim) continue;
                if (o.PlayerHold && ZWorld.GrabPolicyDeviation && od > (enter ? ZWorld.CloseGrabPx : ZWorld.CloseGrabFlagPx)) continue;   // held: only very close things
                if (!o.CanSee(t) || o.PathUnreachable(t)) continue;
                return true;
            }
            return false;
        }
    }

    public sealed partial class ZBot
    {
        /// <summary>DV56 knobs: idle time before a unit counts, the check interval, the smallest idle group, the defence kept.</summary>
        public const double COMMIT_IDLE = 30, COMMIT_INTERVAL = 20;
        public const int COMMIT_MIN = 4, COMMIT_KEEP = 2;
        readonly Dictionary<int, double> idleSince = new Dictionary<int, double>();
        double nextCommit;
        /// <summary>DV56 statistics: waves sent, units ordered by them.</summary>
        public int CommitWaves { get; private set; }
        public int CommitOrders { get; private set; }

        /// <summary>DV56 (see ZObject.AiCombat.cs), once per AI tick after Zod's order round.</summary>
        void CommitIdleGroups()
        {
            if (!ZWorld.BotCommitDeviation || Difficulty != ZBotDifficulty.Normal) return;
            var units = new List<ZObject>(); var targeted = new List<ZObject>();
            CollectOurUnits_3(units, targeted);
            var idle = new List<ZObject>(); var seen = new HashSet<int>();
            foreach (var u in units)
            {
                if (!u.CanMove || u.IsMoving || u.AttackObject != null || u.Waypoints.Count > 0 || u.Stunned) continue;   // the server's own waypoints (agro, grabs) = busy
                seen.Add(u.Id);
                if (!idleSince.TryGetValue(u.Id, out double t0)) { idleSince[u.Id] = W.Time; continue; }
                if (W.Time - t0 >= COMMIT_IDLE - TIME_EPS) idle.Add(u);
            }
            List<int> drop = null;
            foreach (var k in idleSince.Keys) if (!seen.Contains(k)) (drop ??= new List<int>()).Add(k);
            if (drop != null) foreach (var k in drop) idleSince.Remove(k);
            if (W.Time + TIME_EPS < nextCommit || idle.Count < COMMIT_MIN) return;
            nextCommit = W.Time + COMMIT_INTERVAL;
            ZObject fort = null;
            foreach (var o in W.Objects) if (o.IsFort && o.Owner == Team && !o.Destroyed) { fort = o; break; }
            if (fort != null) idle.Sort((a, b) => { int c = D(a, fort).CompareTo(D(b, fort)); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            var wave = idle.GetRange(COMMIT_KEEP, idle.Count - COMMIT_KEEP);
            double cx = 0, cy = 0; foreach (var u in wave) { cx += u.CenterX; cy += u.CenterY; }
            cx /= wave.Count; cy /= wave.Count;
            // targets: foreign flags nearest the wave first, then the enemy fort, then enemy units / cannons
            var flags = new List<ZObject>(); var hostile = new List<ZObject>();
            foreach (var t in W.Objects)
            {
                if (Gone(t) || !VisibleToBot(t)) continue;
                if (t.Kind == ZKind.MapItem && t.Type == "flag" && t.Owner != Team) flags.Add(t);
                else if (t.Owner != Team && t.Owner != Team.Neutral && !t.Destroyed && (t.IsUnit || t.IsFort)) hostile.Add(t);
            }
            int sent = 0;
            foreach (var u in wave)
            {
                ZObject best = null; double bd = double.MaxValue; int ux = u.LocX + 8, uy = u.LocY + 8; bool robot = u.Kind == ZKind.Robot;
                foreach (var pass in new[] { flags, hostile })
                {
                    foreach (var t in pass)
                    {
                        if (t.IsFort ? !robot : !u.CanAttackObject(t) && pass == hostile) continue;   // vehicles: units only; robots: the fort too
                        TargetPoint(t, out int tx, out int ty);
                        if (!InSameRegion(ux, uy, tx, ty, robot)) continue;
                        double d = Math.Sqrt((tx - cx) * (tx - cx) + (ty - cy) * (ty - cy));
                        if (t.IsFort) d -= 1e5;                                                   // the fort before any unit
                        if (d < bd) { bd = d; best = t; }
                    }
                    if (best != null) break;
                }
                if (best == null) continue;
                int before = Orders;
                Command(u, best);
                if (Orders > before) { sent++; idleSince.Remove(u.Id); }
            }
            if (sent == 0) return;
            CommitWaves++; CommitOrders += sent;
            Log.Add($"{W.Time:F0}s {Team} DV56 idle group: kept {COMMIT_KEEP} by the fort, sent {sent}/{wave.Count}");
        }
        static double D(ZObject a, ZObject b) { double dx = a.CenterX - b.CenterX, dy = a.CenterY - b.CenterY; return Math.Sqrt(dx * dx + dy * dy); }
    }
}
