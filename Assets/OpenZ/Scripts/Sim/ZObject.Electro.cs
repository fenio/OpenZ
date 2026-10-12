using System;

namespace OpenZ.Sim
{
    /// <summary>DV45 tesla gatling state (ZObject.ElPhase): charged (ready), winding up to a bolt, recharging after one.</summary>
    public enum ElectroPhase { Charged = 0, Windup = 1, Recharging = 2 }

    /// <summary>
    /// Z had six robot types and no weapon that stuns (zobject.cpp:2102-2213 only removes health). The Electrobot is a
    /// seventh robot: squad of 1, the Tough's health (3378 HP), the Sniper's range (144 px), move 12 px/s (the Tough's), built after
    /// the Laser with the Laser's 179 s. Its tesla gatling is a direct hit (no missile, no splash, no hit roll: lightning never misses,
    /// but it needs Zod's engage rules and a clear line, DV23) of the Tough rocket's 1666 HP, on a tick-exact cycle: a 0.35 s windup,
    /// the bolt, a 3.65 s recharge (4.00 s = 400 ticks bolt to bolt). A bolt that does not kill stuns a robot or a vehicle for 15.00 s
    /// (1500 ticks): it cannot move (its orders wait) but it still fires at enemies in range. Cannons, the DV41 fort defender and other
    /// Electrobots (D4) are never stunned. He never picks up grenades, never snipes drivers, never damages buildings, rocks or bridges
    /// (no explosives, like the Laser) and never auto-enters a vehicle or cannon (D7).
    /// <para>Determinism (multiplayer rule): sim ticks only (<see cref="ZWorld.TickIndex"/>, never float time), no Zod rand() draw by the
    /// weapon or the stun, the view never writes any of it. The stun is an END TICK (not a countdown), so the tick a unit moves again
    /// does not depend on object order. A stunned unit draws nothing new either: no dodge, no scuffle nudge, no idle auto-grab.</para>
    /// <para>Events for the view (appended to ZEventType, existing ordinals unchanged): ElectroCharge (windup start), ElectroBolt
    /// (value = HP removed, radius = recharge ticks, flag = stunned, flag2 = killed), ElectroFizzle, ElectroReady, StunStart,
    /// StunRefresh, StunPulse (value = whole seconds left), StunEnd (flag = ended early). Per-frame state: ElPhase, ElectroCharge01,
    /// Stunned, StunTicksLeft, LastZapHit.</para>
    /// Switches (static like the other deviations; ZGame sets them at every start): ZWorld.ElectrobotDeviation (build lists, PlayerPrefs
    /// oz_electrobot), ZWorld.ElectrobotBotDeviation (DV46 CPU, ZBot.Electrobot.cs, oz_electrobot_cpu), both off in 'Zod exact'
    /// (oz_zod_exact = 1) and in every Zod row of the parity report. With both off no Electrobot can exist and the sim is the previous
    /// build bit for bit.
    /// </summary>
    public static class ZElectro
    {
        public const string Type = "electrobot";
        /// <summary>Balance knobs (listed in the DV45 row; set once before a game, identical on every lockstep peer).</summary>
        public static int WindupTicks = 35, CycleTicks = 400, StunTicks = 1500;
        /// <summary>Balance knob: ticks after a stun ends during which the unit cannot be stunned again (0 = off, the default). It only
        /// acts once a stun has ENDED: against one Electrobot refreshing one target it needs StunChainMaxTicks too.</summary>
        public static int StunGraceTicks = 0;
        /// <summary>Balance knob: the longest one continuous stun may last, counted from the bolt that started it; later bolts still
        /// refresh it but never push its end past that (0 = off, the default: every bolt gives 15.00 s from now, so one Electrobot
        /// firing every 4 s keeps one target frozen for as long as he lives). With StunGraceTicks it breaks such a stun lock.</summary>
        public static int StunChainMaxTicks = 0;
        /// <summary>Bolt to recharge end: the cycle minus the windup (365 ticks = 3.65 s).</summary>
        public static int RechargeTicks => Math.Max(0, CycleTicks - Math.Max(0, WindupTicks));
        /// <summary>View: a death within this many seconds of a bolt is an electrocution (ragdoll, arcs); a cannon's jolt (D5) lasts this long.</summary>
        public const double ZapDeathWindow = 1.5, CannonJoltSeconds = 1.5;
        public static double Seconds(long ticks) => ticks * ZConst.TICK;
    }

    public partial class ZObject
    {
        // ================================================================== DV45 tesla gatling (the robot, or an APC carrying one: D13)
        ElectroPhase elPhase = ElectroPhase.Charged; long elRelease = -1, elReady = -1; int elTarget = -1;
        /// <summary>DV45 weapon phase (view: cells, barrels, gauge needle).</summary>
        public ElectroPhase ElPhase => elPhase;
        /// <summary>Tick the current windup releases its bolt (-1 = not winding up).</summary>
        public long ElReleaseTick => elRelease;
        /// <summary>Tick the current recharge ends (-1 = not recharging).</summary>
        public long ElReadyTick => elReady;
        /// <summary>Target of the current windup / the last bolt (-1 = none).</summary>
        public int ElTargetId => elTarget;
        /// <summary>DV45 counters (checks, parity probe).</summary>
        public int ElBolts { get; private set; }
        public int ElFizzles { get; private set; }
        /// <summary>Sim time this object was last hit by a bolt (-100 = never). View: a death within ZElectro.ZapDeathWindow = electrocuted.</summary>
        public double LastZapHit = -100;

        public bool IsElectrobot => Kind == ZKind.Robot && Type == ZElectro.Type;
        /// <summary>This unit fires the tesla gatling: an Electrobot, or an APC whose crew weapon is his (Zod: the APC takes its crew's
        /// weapon, vapc.cpp:266-342; D13).</summary>
        public bool IsElectroWeapon => IsElectrobot || (Kind == ZKind.Vehicle && Type == "apc" && Drivers.Count > 0 && CrewWeaponType == ZElectro.Type);
        /// <summary>D7: he enters vehicles and cannons only on a direct order (no idle auto-grab, the CPU never sends him).</summary>
        public bool NoAutoEnter => ZWorld.ElectrobotNoAutoEnter && IsElectrobot;

        /// <summary>View: 0..1 over the recharge (1 = charged, also during the windup).</summary>
        public double ElectroCharge01
        {
            get
            {
                if (elPhase != ElectroPhase.Recharging || World == null) return 1.0;
                int rt = ZElectro.RechargeTicks; if (rt <= 0) return 1.0;
                return Math.Max(0.0, Math.Min(1.0, 1.0 - (elReady - World.TickIndex) / (double)rt));
            }
        }

        /// <summary>ProcessServer, every tick before the stun hold: the recharge ends on its tick (ElectroReady); a windup whose target
        /// was dropped (a robot that walks disengages, zobject.cpp:3519-3520) fizzles. One compare for every other object.</summary>
        void ElectroTick()
        {
            if (elPhase == ElectroPhase.Charged) return;
            if (!IsElectroWeapon) { elPhase = ElectroPhase.Charged; elRelease = elReady = -1; return; }   // the weapon left (an APC's Electrobot got out)
            if (elPhase == ElectroPhase.Recharging)
            {
                if (World.TickIndex >= elReady) { elPhase = ElectroPhase.Charged; elReady = -1; World.Emit(ZEventType.ElectroReady, this); }
                return;
            }
            if (AttackObject == null) Fizzle(null);
        }

        /// <summary>ProcessAttackDamage for the tesla gatling (instead of Zod's hitscan / missile code; after the no-target check).
        /// Charged: a new windup (CanAttackObject, DV23 line). Winding up: the target may change (the bolt goes to the current one);
        /// the release tick checks range + line again (fizzle = the charge is kept). The bolt: Damage (1666 x crew for an APC), no roll,
        /// no scatter, no dodge call (it is not a missile): nothing is drawn from Zod's rand().</summary>
        void ElectroFire(double time, bool playerGiven)
        {
            var t = AttackObject;
            long now = World.TickIndex;
            if (elPhase == ElectroPhase.Recharging) return;
            if (elPhase == ElectroPhase.Charged)
            {
                if (!CanAttackObject(t)) { Disengage(); return; }
                if (LosHoldShot(t)) return;                                                   // DV23: no line, no windup (disengages)
                elPhase = ElectroPhase.Windup; elRelease = now + Math.Max(0, ZElectro.WindupTicks); elTarget = t.Id;
                World.Emit(ZEventType.ElectroCharge, this, t, t.CenterX, t.CenterY, (float)ZElectro.Seconds(elRelease - now), (int)(elRelease - now));
                if (now < elRelease) return;
            }
            if (!CanAttackObject(t)) { Fizzle(t); Disengage(); return; }
            if (now < elRelease) { elTarget = t.Id; return; }
            if (!WithinAttackRadius(t)) { Fizzle(t); Disengage(); return; }                   // out of range / line on the release tick
            Bolt(t, time);
        }

        void Bolt(ZObject t, double time)
        {
            int hp0 = t.Health;
            t.DamageHealth(Damage);
            t.LastEnemyHit = time; t.LastZapHit = time;                                       // combat_nb_v1 NB31 + the view's zap death
            UpdatedAttackObjectHealth = true;                                                 // Zod's side-effect pass relays the death (ZWorld.Tick)
            bool killed = t.Destroyed, stunned = false;
            if (!killed && t.CanBeStunned()) stunned = t.ApplyStun(this);
            elPhase = ElectroPhase.Recharging; elRelease = -1; elReady = World.TickIndex + ZElectro.RechargeTicks; elTarget = t.Id;
            ElBolts++; World.ElectroBolts++; if (killed) World.ElectroKills++;
            World.Emit(ZEventType.ElectroBolt, this, t, t.CenterX, t.CenterY, hp0 - t.Health, ZElectro.RechargeTicks, stunned, killed);
        }

        void Fizzle(ZObject t)
        {
            elPhase = ElectroPhase.Charged; elRelease = -1; ElFizzles++;
            World.Emit(ZEventType.ElectroFizzle, this, t);
        }

        // ================================================================== DV45 stun (a sim status of the target)
        long stunEnd = -1, stunLastEnd = long.MinValue / 4, stunChainStart = -1; int stunBy = -1;
        /// <summary>Tick the stun ends (-1 = none). The unit moves again on this tick.</summary>
        public long StunEndTick => stunEnd;
        /// <summary>The Electrobot (or APC) whose bolt set the current stun.</summary>
        public int StunById => stunBy;
        /// <summary>Fresh stuns this unit took (statistics).</summary>
        public int StunCount { get; private set; }
        public bool Stunned => stunEnd >= 0 && World != null && World.TickIndex < stunEnd;
        public long StunTicksLeft => Stunned ? stunEnd - World.TickIndex : 0;

        /// <summary>A bolt that does not kill stuns robots (every type but the Electrobot, D4) and vehicles (jeep, tanks, mech, APC, MLRS,
        /// crane) of a team. Never: cannons and the DV41 fort defender (they cannot move anyway: D5, a view-only jolt), neutral units,
        /// units in a building stage (crane repair inside, repair bay or its queue, fort entry past the approach) so no stage machine
        /// freezes halfway, and anything about to be deleted.</summary>
        public bool CanBeStunned()
        {
            if (World == null || Destroyed || KillAt >= 0 || Owner == Team.Neutral || IsFortDefender) return false;
            if (PathInBuildingStage || PathWaitingForRepair) return false;
            if (Kind == ZKind.Robot) return !(IsElectrobot && ZWorld.ElectrobotImmune);
            return Kind == ZKind.Vehicle;
        }

        /// <summary>A bolt hits: stun until 15.00 s from now. Refresh, never stack (max of the end ticks, one attacker id). A fresh stun
        /// stops the unit (no run, a pending path answer dropped: it re-plans when the stun ends). Returns false only while the
        /// optional grace (ZElectro.StunGraceTicks) protects it. The optional chain cap (ZElectro.StunChainMaxTicks) keeps the end
        /// within that many ticks of the bolt that started the stun. StunStart / StunRefresh carry the real time left (value s, radius
        /// ticks): 15.00 s / 1500 with both knobs off.</summary>
        internal bool ApplyStun(ZObject by)
        {
            long now = World.TickIndex;
            if (stunEnd >= 0 && now >= stunEnd) EndStun(false);                              // ran out this tick before its own turn: end it first (event order)
            bool was = stunEnd >= 0;
            if (!was && ZElectro.StunGraceTicks > 0 && now - stunLastEnd < ZElectro.StunGraceTicks) return false;
            if (!was) stunChainStart = now;
            long end = Math.Max(stunEnd, now + ZElectro.StunTicks);
            if (ZElectro.StunChainMaxTicks > 0) end = Math.Min(end, stunChainStart + ZElectro.StunChainMaxTicks);   // balance knob (off by default)
            stunEnd = end; stunBy = by != null ? by.Id : -1;
            long left = stunEnd - now; float secs = (float)ZElectro.Seconds(left);
            if (was) { World.Emit(ZEventType.StunRefresh, this, by, CenterX, CenterY, secs, (int)left); return true; }
            StunCount++; World.ElectroStuns++;
            StopMove(); IsRunning = false;
            cur.pathFindingId = 0;                                                            // a path answer still in flight is dropped
            if (formOn) { formOn = false; formRetry = World.Time + ZFormation.ReenterDelay; }  // DV24: out of the V (never a moving slot)
            World.Emit(ZEventType.StunStart, this, by, CenterX, CenterY, secs, (int)left, true);
            return true;
        }

        /// <summary>ProcessServer after the fort-defender branch, before the lid and the pathing layer's PreProcess. While stunned: no
        /// movement, no waypoint step (orders wait, new ones are accepted and wait too), no pathing layer, no crate, no attack-move; it
        /// engages its ordered target when that is in range, else Zod's passive engage picks the first enemy in range (engage only: no
        /// agro, enter, flag or crate waypoint, no rand() draw), and it fires (Zod's weapon code, the Tough's prepare, the heavy's second
        /// barrel, the crane's DV31 rifle, an APC's crew weapon). On the end tick it falls through: it moves on this very tick.</summary>
        bool StunHold(double time)
        {
            if (stunEnd < 0) return false;
            if (World.TickIndex >= stunEnd) { EndStun(false); return false; }
            if (Owner == Team.Neutral || KillAt >= 0 || PathInBuildingStage) { EndStun(true); return false; }
            StopMove(); IsRunning = false; fortRush = false;
            PS.wait = ZPathWait.None; PS.heldTicks = 0;
            ProcessServerLid(time);
            LastAttackPlayerGiven = false;
            if (Waypoints.Count > 0 && (Waypoints[0].mode == WpMode.Attack || Waypoints[0].mode == WpMode.Agro))
            {
                var wp = Waypoints[0]; var t = World.Get(wp.refId);                           // the ordered target first
                if (CanAttackObject(t) && WithinAttackRadius(t)) { Engage(t); LastAttackPlayerGiven = wp.mode == WpMode.Attack && wp.playerGiven; }
            }
            CheckPassiveEngage(time, true);
            ProcessAttackDamage(time, LastAttackPlayerGiven);
            CraneGunStep(time);
            long left = stunEnd - World.TickIndex;
            if (left % 100 == 0) World.Emit(ZEventType.StunPulse, this, null, CenterX, CenterY, left / 100);
            return true;
        }

        /// <summary>The stun ends (its tick, or early: owner change, deleted). The unit resumes its orders as a fresh start of the same
        /// waypoint from where it stands (Zod's new-waypoint path, zobject.cpp:2709-2733), and the pathing layer's stuck clocks restart,
        /// so 15 s of standing never count as stuck, frozen or jammed.</summary>
        void EndStun(bool early)
        {
            if (stunEnd < 0) return;
            stunEnd = -1; stunLastEnd = World.TickIndex; stunBy = -1; stunChainStart = -1;
            World.Emit(ZEventType.StunEnd, this, null, CenterX, CenterY, 0, 0, early);
            var s = PS; double now = World.Time;
            s.lastProgress = now; s.stillX = double.NaN; s.rebase = true; s.trafficSince = -1; s.heldTicks = 0; s.wait = ZPathWait.None;
            s.sideSince = -1; s.bridge.jamSince = now; s.bridge.jamX = CenterX; s.bridge.jamY = CenterY;
            if (Waypoints.Count > 0 && !PathInBuildingStage) PathReissue();
        }

        /// <summary>ZWorld.ResetObjectTeam (driver sniped, crew ejected, capture): a unit that changes hands is no longer stunned.</summary>
        internal void ClearStun(bool early) => EndStun(early);

        bool stunWait;
        /// <summary>ProcessMoveWP, before the DV24 V (and DV44's trail): a minion whose squad leader is stunned waits where it stands
        /// while it holds the leader's own cloned MOVE (Zod's CloneMinionWayPoints), so the squad never walks on without him. Without it
        /// a minion that was not in the V (lagging, or the V not formed yet) walked its own path to the goal alone and took the leader's
        /// spot there (the harness: a pyro squad's leader then ended its order 22 px short). An attack-move still engages first
        /// (CheckAttackTo runs before). The wait counts as progress for the stuck guard (no give-up); when the leader moves again the
        /// minion re-plans from where it stands (like the leader, EndStun) and the V / trail takes over. Only with a stunned leader,
        /// so with DV45 off it never acts; no rand() draw.</summary>
        bool StunWaitForLeader()
        {
            var l = Leader;
            bool wait = IsMinion && l != null && !l.Destroyed && l.KillAt < 0 && l.Stunned && Kind == ZKind.Robot && Waypoints.Count > 0
                        && l.Waypoints.Count > 0 && Waypoints[0].mode == WpMode.Move && l.Waypoints[0].Equals(Waypoints[0]);
            var s = PS;
            if (wait)
            {
                StopMove(); IsRunning = false;
                s.lastProgress = World.Time; s.stillX = double.NaN; s.heldTicks = 0; s.wait = ZPathWait.None;
                stunWait = true;
                return true;
            }
            if (!stunWait) return false;
            stunWait = false;                                                                 // the leader moves again (or the order changed): re-plan from here
            s.lastProgress = World.Time; s.rebase = true;
            if (Waypoints.Count > 0) PathReissue();
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV45: ticks since the world was created, incremented first thing in Tick: the stun and the tesla cycle count in it
        /// (no float drift, the same index for every object within a tick).</summary>
        public long TickIndex { get; private set; }

        /// <summary>DV45 (electrobot_v1): the Electrobot is in the fort / robot factory build lists (levels 4-5, after the Laser). OFF =
        /// Zod's build lists (ZBuildLists), no Electrobot. PlayerPrefs oz_electrobot; off in 'Zod exact' and in the parity Zod rows.</summary>
        public static bool ElectrobotDeviation = true;
        /// <summary>DV46 (electrobot_v1): the CPU builds one Electrobot at a time (ZBot.Electrobot.cs). Needs DV45. PlayerPrefs
        /// oz_electrobot_cpu; off in 'Zod exact'.</summary>
        public static bool ElectrobotBotDeviation = true;
        /// <summary>DV72: the CPU's Electrobot leaves with another unit of his team (ZBot.Electrobot.cs, ElectrobotEscort). Needs DV46.
        /// ZGame turns it on at every start (off in 'Zod exact'); headless checks keep the previous rounds unless they switch it on.</summary>
        public static bool ElectrobotEscortDeviation = false;
        /// <summary>
        /// in the ROBOT FACTORY lists at every level (after the Laser at 4-5, after the last robot below), so he can be built on L02
        /// (whose producers are levels 0-1) on every difficulty; forts keep levels 4-5. Needs DV45. PlayerPrefs oz_electrobot_all_levels;
        /// off in 'Zod exact' and in the parity Zod rows (DV45 off there = Zod's lists whatever this says).</summary>
        public static bool ElectrobotAllLevels = false;
        /// <summary>
        /// false = stunnable like everyone (checked by the harness).</summary>
        public static bool ElectrobotImmune = true;
        /// <summary>
        /// false = Zod's idle auto-grab and the CPU's enter orders apply to him too (checked by the harness).</summary>
        public static bool ElectrobotNoAutoEnter = true;

        /// <summary>DV45 statistics of this world (checks, harness, probe).</summary>
        public int ElectroBolts, ElectroStuns, ElectroKills;
    }
}
