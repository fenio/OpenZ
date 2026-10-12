using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// electrobot_v1, OpenZ deviation DV46 "the CPU builds one Electrobot at a time". Zod's bot picks the first unit of its preferred
    /// list a building can make (medium, light, jeep, pyro, tough, sniper, tough, psycho; zbot.cpp:20-27, :1372-1432), so it never
    /// builds a Laser and would never build the Electrobot (DV45): only the player would ever field one.
    /// <para>With DV46 (needs DV45), in every production round before Zod's pick: when the team has no living Electrobot (on foot, as a
    /// driver, as an APC passenger, in a repair bay) and no producer claimed for one yet, its first producer that can make one at its
    /// build level claims it (robot factories before forts, then object order: a fort keeps Zod's tanks when a factory can do the job).
    /// The claimed producer builds the Electrobot through the same STOP_BUILDING + START_BUILDING commands; once he exists it goes back
    /// to Zod's pick. With DV71 a producer that is building its own pick when it is claimed finishes that unit first (the claim never
    /// throws away a started build); the Electrobot is its next unit. The claim ends when the producer is lost, destroyed or can no longer make him. The CPU never orders its
    /// Electrobot into a vehicle or cannon (ZBot.MatchTargets_3, DV37's car-park pass; D7); he takes flags, forts and fights like any
    /// squad. Normal and Easy. Deterministic: the bot's own object list in creation order, no random draw, one command path.</para>
    /// </summary>
    public sealed partial class ZBot
    {
        int elbProducer = -1;
        /// <summary>DV71: start time (BInit) of the build the claimed producer was running when it was claimed; the Electrobot waits
        /// for that one unit (-1 = nothing to wait for).</summary>
        double elbWaitBInit = -1;
        /// <summary>DV46: the producer claimed for an Electrobot (-1 = none).</summary>
        public int ElectrobotProducerId => elbProducer;
        /// <summary>DV46 statistics (checks): producers claimed.</summary>
        public int ElectrobotClaims { get; private set; }
        /// <summary>DV71 statistics (checks): production rounds in which a claimed producer went on with its running build.</summary>
        public int ElectrobotWaits { get; private set; }

        /// <summary>DV46, start of ChooseBuildOrders: keep, end or make the claim.</summary>
        void ElectrobotRound()
        {
            if (!ZWorld.ElectrobotDeviation || !ZWorld.ElectrobotBotDeviation || TeamHasElectrobot()) { elbProducer = -1; elbWaitBInit = -1; return; }
            if (elbProducer >= 0)
            {
                var c = W.Get(elbProducer);
                if (c != null && c.Owner == Team && !c.Destroyed && c.IsProducer && ZBuildLists.InList(c.Type, c.BuildLevel, ZElectro.Type)) return;
                elbProducer = -1; elbWaitBInit = -1;
            }
            for (int pass = 0; pass < 2 && elbProducer < 0; pass++)
                foreach (var b in W.Objects)
                {
                    if (b.Owner != Team || b.Kind != ZKind.Building || !b.IsProducer || b.Destroyed) continue;
                    if ((b.Type == "robot_factory") != (pass == 0)) continue;                    // robot factories first, then forts
                    if (!ZBuildLists.InList(b.Type, b.BuildLevel, ZElectro.Type)) continue;
                    elbProducer = b.Id; ElectrobotClaims++;
                    elbWaitBInit = ZWorld.BotLaserDeviation && b.Building && b.CurUnit != null && b.CurUnit != ZElectro.Type ? b.BInit : -1;
                    Log.Add($"{W.Time:F0}s {Team} {b.Type}({b.LocX / 16},{b.LocY / 16}) L{b.Level} claimed for an Electrobot (DV46)");
                    break;
                }
        }

        /// <summary>DV46: Zod's pick of this producer, or the Electrobot when it holds the claim. A producer still making Electrobots
        /// after its claim ended goes back to Zod's pick; where Zod's bot has no pick for it at all (a level-0 robot factory with the D1
        /// all-levels toggle on: grunt, electrobot, gatling, none of them preferred) it goes back to its list's entry 0, the building's
        /// default production, as Zod's bot leaves such a factory. Without that it repeated Electrobots (harness, L02 with the toggle:
        /// three alive per CPU). With the toggle off every producer that can make him has a Zod pick, so nothing changes there.</summary>
        string ElectrobotPick(ZObject b, string zodPick)
        {
            if (b.Id == elbProducer)
            {   // DV71: the unit this producer was already building as its own pick is finished first (same build = same start time)
                if (elbWaitBInit >= 0 && b.Building && b.CurUnit != null && b.CurUnit == zodPick && b.BInit == elbWaitBInit) { ElectrobotWaits++; return zodPick; }
                elbWaitBInit = -1;
                return ZElectro.Type;
            }
            if (zodPick == null && b.CurUnit == ZElectro.Type)
            {
                var l = ZBuildLists.For(b.Type, b.BuildLevel);
                return l.Count > 0 ? l[0] : null;
            }
            return zodPick;
        }

        // ------------------------------------------------------------------ DV72 the Electrobot leaves with another unit
        /// <summary>DV72 knobs: how near (px between top-left corners, the Zod bot's distance) an ordered unit must stand to take the
        /// Electrobot along, and how long (game s) he waits for company before he gets Zod's own order alone.</summary>
        public const int ELB_JOIN_PX = 320;
        public const double ELB_WAIT = 240;
        readonly Dictionary<int, double> elbWaitSince = new Dictionary<int, double>();
        /// <summary>DV72 statistics (checks): Electrobot orders given together with another unit's order / alone after the wait.</summary>
        public int ElectrobotEscorted { get; private set; }
        public int ElectrobotAlone { get; private set; }

        /// <summary>OpenZ deviation DV72 "the CPU's Electrobot comes with support" (needs DV46). The Zod bot pairs every idle unit with
        /// its own nearest target (zbot.cpp:222-414), so the single Electrobot always walked alone. With DV72 the idle Electrobot is
        /// taken out of Zod's wave (here, before ReduceUnitsToPercent) and ElectrobotEscort gives him his order at the end of the
        /// round. Normal CPU only (the Easy CPU keeps its rounds). Returns the held-back Electrobots, null when there is none.</summary>
        List<ZObject> ElectrobotHoldBack(List<ZObject> units)
        {
            if (!ZWorld.ElectrobotEscortDeviation || Difficulty != ZBotDifficulty.Normal) return null;
            List<ZObject> held = null;
            for (int i = 0; i < units.Count;)
            {
                if (units[i].Kind == ZKind.Robot && units[i].Type == ZElectro.Type) { (held ??= new List<ZObject>()).Add(units[i]); units.RemoveAt(i); }
                else i++;
            }
            if (elbWaitSince.Count > 0)
            {   // the wait ends with a real CPU order (Command) or when he is gone: a chase or a flag walk of his own does not restart it
                List<int> drop = null;
                foreach (var k in elbWaitSince.Keys) { var o = W.Get(k); if (o == null || Gone(o) || o.Owner != Team) (drop ??= new List<int>()).Add(k); }
                if (drop != null) foreach (var k in drop) elbWaitSince.Remove(k);
            }
            return held;
        }

        /// <summary>DV72, end of the order round: each held-back Electrobot takes the target of the nearest unit of ours that was
        /// ordered in this round to a foreign flag, into the enemy fort or to attack and stands within ELB_JOIN_PX of him. When no
        /// such unit was ordered but an idle armed unit of ours stands that near, that unit gets its Zod order now (a one-unit
        /// round, Zod's matching) and he takes its target. After ELB_WAIT s without company he gets Zod's own order, alone.
        /// Deterministic: list order, no random draw.</summary>
        void ElectrobotEscort(List<ZObject> escorts, List<ZObject> idle, List<ZObject> targets)
        {
            foreach (var e in escorts)
            {
                if (!elbWaitSince.ContainsKey(e.Id)) elbWaitSince[e.Id] = W.Time;
                var t = EscortTarget(e, out var mate);
                if (t == null)
                {
                    ZObject near = null; double nd = double.MaxValue;
                    foreach (var u in idle)
                    {
                        if (!u.CanAttack() || Gone(u) || LastRoundOrders.Exists(o => o.unitId == u.Id)) continue;
                        double d = Dist(e, u);
                        if (d <= ELB_JOIN_PX && d < nd) { nd = d; near = u; }
                    }
                    if (near != null)
                    {
                        var one = new List<ZObject> { near };
                        MatchTargets_3(one, targets); GiveOutOrders_3(one, targets);
                        t = EscortTarget(e, out mate);
                    }
                }
                int before = Orders;
                if (t != null) Command(e, t);
                else if (W.Time - elbWaitSince[e.Id] >= ELB_WAIT - TIME_EPS)
                {
                    var one = new List<ZObject> { e };
                    MatchTargets_3(one, targets); GiveOutOrders_3(one, targets);
                }
                if (Orders == before) continue;                                            // nothing given: he keeps waiting
                elbWaitSince.Remove(e.Id);
                if (t != null) { ElectrobotEscorted++; Log.Add($"{W.Time:F0}s {Team} electrobot#{e.Id} goes with {mate.Type}#{mate.Id} -> {t.Type}#{t.Id} (DV72)"); }
                else { ElectrobotAlone++; Log.Add($"{W.Time:F0}s {Team} electrobot#{e.Id} goes alone after {ELB_WAIT:F0} s without company (DV72)"); }
            }
        }

        /// <summary>DV72: the target of the nearest unit of ours ordered this round (flag, fort entry, attack) within ELB_JOIN_PX of e
        /// that e may take too (same robot region; an attack target he can attack). Null when there is none.</summary>
        ZObject EscortTarget(ZObject e, out ZObject mate)
        {
            mate = null; ZObject best = null; double bd = double.MaxValue;
            foreach (var o in LastRoundOrders)
            {
                if (o.unitId == e.Id || (o.mode != WpMode.Move && o.mode != WpMode.EnterFort && o.mode != WpMode.Attack)) continue;
                var u = W.Get(o.unitId); var t = BotObject(o.targetId);
                if (u == null || t == null || Gone(u) || (u.Kind == ZKind.Robot && u.Type == ZElectro.Type)) continue;
                if (o.mode == WpMode.Attack && !e.CanAttackObject(t)) continue;
                TargetPoint(t, out int tx, out int ty);
                if (!InSameRegion(e.LocX + 8, e.LocY + 8, tx, ty, true)) continue;
                double d = Dist(e, u);
                if (d > ELB_JOIN_PX || d >= bd) continue;
                bd = d; best = t; mate = u;
            }
            return best;
        }

        /// <summary>DV46: an Electrobot of ours is alive somewhere: on foot, driving a vehicle / cannon, riding an APC, or in a repair bay
        /// with the vehicle he drives or rides.</summary>
        bool TeamHasElectrobot()
        {
            foreach (var o in W.Objects)
            {
                if (o.Owner != Team || o.Destroyed) continue;
                if (o.Kind == ZKind.Robot) { if (o.Type == ZElectro.Type && o.KillAt < 0) return true; continue; }
                if (o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon)
                {
                    if (o.DriverType == ZElectro.Type && o.Drivers.Count > 0) return true;
                    if (o.PassengerType == ZElectro.Type && o.Passengers.Count > 0) return true;
                    continue;
                }
                if (o.Kind == ZKind.Building && o.Type == "repair" && o.RepairingUnit)
                {
                    if (o.RepairDriverType == ZElectro.Type && o.RepairDrivers.Count > 0) return true;
                    if (o.RepairPassengerType == ZElectro.Type && o.RepairPassengers.Count > 0) return true;
                }
            }
            return false;
        }
    }
}
