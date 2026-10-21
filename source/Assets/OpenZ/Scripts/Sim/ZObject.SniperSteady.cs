using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV120 "the sniper stands and fires" (Zod has no such rule; the model is DV83 for the Electrobot,
    /// ZObject.ElectroSteady.cs). Snipers already never step aside from a shell or a grenade (b49, ZObject.Calm.cs CalmStandsFirm).
    /// <para>What still made them walk about in a fight without anything to dodge: (1) a walking robot never fires and never takes a
    /// new target (zobject.cpp:1615, ZObject.cs CheckPassiveEngage; snipers are not FiresOnMove): on an agro walk, a help walk (DV53) or
    /// the approach of an attack towards ONE enemy it walked on past every other enemy already in its 144 px; (2) an agro walk turns
    /// back to where it began when its target leaves the leash (zobject.cpp:2233-2328): there and back; (3) every walk stopped at the
    /// very edge of the range and every idle check (0.5 s) started a new one for an enemy just outside it (Zod's agro: range + 40 px),
    /// so the enemies' own small moves and each kill gave the next 3-20 px step: start, stop, start. Headshots (DV116) end fights about
    /// 1.7 times as fast, so these steps came 1.75 times as often, and the view turned each one into "get up, step, lie down"
    /// (View/BS01Stance.Steady.cs).</para>
    /// <para>DV120, for a sniper on foot (the fort's roof sniper has his own step and is not touched): a firing position with a margin on
    /// both sides (hysteresis), so the edge of the range no longer decides every step:
    /// (1) on an agro or help walk, or on the approach of an attack whose own target is not in range, an enemy it can kill from where
    /// it stands (a robot, or a vehicle or gun whose crew it can snipe) stops it once that enemy is <see cref="ZWorld.SniperStandBand"/>
    /// px inside its range, at once when it comes nearer by itself or is the one it already shoots at (standing, any enemy in range
    /// keeps it there): an agro / help walk ends there and it engages; an attack waits while it fires and goes on afterwards (its own
    /// target in range first, as Zod). Not an attack the player ordered himself: that one it does at once and until it is done (DV16:
    /// the player's order wins over every automatic behaviour). On an agro walk under way it also closes in that far on its own target before it
    /// stands (never into water, not when that target comes nearer);
    /// (2) an agro walk never turns back: it stays where the chase ended;
    /// (3) in position: while its squad has fired in the last <see cref="ZWorld.SniperHoldSecs"/> s and an enemy stands within
    /// <see cref="ZWorld.SniperEdgeBand"/> px outside its range, it keeps its spot (no new agro walk, no walk to a flag, a vehicle or
    /// a crate; a waiting attack waits on). An enemy farther out, or that long without a shot, and it moves on as one walk;
    /// (4) while it stands and fires, robots of its own team that do not are not allowed to push it about (the spacing layer's overlap
    /// push, ZSeparation.PushShare): they go round it. Vehicles of its team still get their room (vehicles first, ZYield.cs).
    /// A move, a force move, entering, a fort entry, a grenade crate, a repair and every step aside of the spacing layer (also the one
    /// for a vehicle of its team) are never held.</para>
    /// Deterministic: sim state and object order only, no random draw. Switch ZWorld.SniperSteadyDeviation: View/ZGame.cs sets it at
    /// every start (off in 'Zod exact'); off in headless worlds and in the counted checks unless they set it (OzV11Kit.GameSwitches).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV120 applies to this unit: a sniper on foot (not the fort's roof sniper), the switch on.</summary>
        public bool SniperSteady => (ZWorld.SniperSteadyDeviation && Kind == ZKind.Robot && Type == "sniper" && !IsFortDefender) || ToughSteady;   // DV123: the Tough on foot too (ZObject.ToughSteady.cs)

        bool snStood;

        /// <summary>DV120, ProcessServer in front of the waypoint step: true = it stands this tick (the waypoint step is skipped; the
        /// passive check and the weapon run as on every tick).</summary>
        bool SniperStand()
        {
            if (!SniperSteady) return false;
            if (SniperStandNow()) { snStood = true; return true; }
            if (snStood) { snStood = false; if (Waypoints.Count > 0) SetVelocity(); }        // nothing left to shoot from here: on with the order from where it stands
            return false;
        }

        bool SniperStandNow()
        {
            var wp = Waypoints[0];
            bool agro = wp.mode == WpMode.Agro;
            if (!agro && wp.mode != WpMode.Attack) return false;                            // the player's move, enter, crate, fort, repair, a step aside: never held
            if (!agro && HumanDirectOrder) return false;                                    // the player's own attack order: at once and until it is done (DV16)
            var own = World.Get(wp.refId);
            if (own == null || !CanAttackObject(own)) return false;                         // the waypoint ends itself
            bool ownIn = WithinAttackRadius(own);
            if (ownIn && (!agro || !SniperCloseIn(own))) return false;                      // its own target in range: the waypoint stands and fires (Zod)
            var t = SniperStopTarget(own, IsMoving && !snStood);
            if (t == null)
            {
                if (ownIn) return false;                                                    // agro: it closes in on its own target (ProcessAgroWP)
                if (agro || !snStood || !SniperKeepsPosition(World.Time)) return false;
                StopMove(); ZWorld.SniperKeptSpot++;                                       // attack: enemies at the edge and its squad fired a moment ago: it keeps its spot
            }
            else if (agro) { KillWP(); ZWorld.SniperAgroEnded++; Engage(t); }                // an agro / help walk ends here
            else { StopMove(); if (!snStood) ZWorld.SniperAttackHeld++; Engage(t); }        // an attack waits
            PS.lastProgress = World.Time; PS.stillX = double.NaN; PS.heldTicks = 0; PS.wait = ZPathWait.None;   // standing to fire is not "stuck"
            World.SniperStands++;
            return true;
        }

        /// <summary>DV120, CheckPassiveEngage, after the enemies in range (none) and before a new waypoint: an enemy is only within the
        /// agro reach (range + 40 px). In position (<see cref="SniperKeepsPosition"/>): no new waypoint this check.</summary>
        bool SniperInPosition(double time)
        {
            if (!SniperSteady || !SniperKeepsPosition(time)) return false;
            ZWorld.SniperHeldPosition++;
            return true;
        }

        /// <summary>DV120: its squad fired in the last SniperHoldSecs s (or one of them still holds a target) and an enemy it would stop for
        /// stands within SniperEdgeBand px outside its range, in sight.</summary>
        bool SniperKeepsPosition(double time)
        {
            var l = IsMinion && Leader != null ? Leader : this;
            bool fired = SniperFiredSince(l, time);
            foreach (var m in l.Minions) if (!fired && m != null && !m.Destroyed && m.KillAt < 0) fired = SniperFiredSince(m, time);
            if (!fired) return false;
            double reach = AttackRadius + ZWorld.SniperEdgeBand;
            foreach (var o in World.Objects)
                if (SniperStopsFor(o) && Dist(CenterX, CenterY, o.CenterX, o.CenterY) <= reach && CanSee(o)) return true;
            return false;
        }

        static bool SniperFiredSince(ZObject o, double time) => o.AttackObject != null || o.nextDamageTime - o.AttackSpeed > time - SteadyHoldSecs(o);   // DV123: a Tough's hold is ZWorld.ToughHoldSecs (ZObject.ToughSteady.cs)

        /// <summary>DV120, ProcessAgroWP: the agro target left the leash: a sniper stays where the chase ended (no walk back).</summary>
        bool SniperNoWalkBack()
        {
            if (!SniperSteady) return false;
            ZWorld.SniperNoReturn++;
            return true;
        }

        /// <summary>DV120, ProcessAgroWP: on an agro walk under way its target came into range but is still in the outer
        /// <see cref="ZWorld.SniperStandBand"/> px of it: it walks on (closes in) instead of stopping at the very edge. Not when it stands
        /// (a new agro or help walk on a target already in range: it fires from where it stands, Zod), not when the target comes nearer
        /// by itself, not into water.</summary>
        bool SniperCloseIn(ZObject t)
        {
            if (!SniperSteady || t == null || ZWorld.SniperStandBand <= 0 || !IsMoving) return false;
            double rx = t.CenterX - CenterX, ry = t.CenterY - CenterY, d = Math.Sqrt(rx * rx + ry * ry);
            if (d <= AttackRadius - ZWorld.SniperStandBand || d < 1) return false;
            if (SniperComes(t)) return false;                                               // it comes nearer: it walks into the band by itself
            int ax = CenterX + (int)(rx / d * ZWorld.SniperStandBand), ay = CenterY + (int)(ry / d * ZWorld.SniperStandBand);
            var wc = World.Map.ClassAt(ax >> 4, ay >> 4);
            if (wc == ZClass.Water || wc == ZClass.DeepWater) return false;                 // never into the water for it
            ZWorld.SniperCloseIns++;
            return true;
        }

        /// <summary>DV120: a sniper on foot that stands and fires: it does not walk and it holds a target or fired in the last
        /// SniperHoldSecs s (between two targets it is still in its firing position).</summary>
        public bool SniperFiring => SniperSteady && !IsMoving && (AttackObject != null || nextDamageTime - AttackSpeed > World.Time - SteadyHoldSecs(this));   // DV123: a Tough's hold (ZObject.ToughSteady.cs)

        /// <summary>DV120, the spacing layer's overlap push (ZSeparation.PushShare): a sniper that stands and fires takes no share of the
        /// push against a robot of its own team that does not (that one goes round it, as round a parked gun); vehicles first and every
        /// other pair are unchanged. Counter ZWorld.SniperPushesHeld.</summary>
        internal static bool SniperHoldsAgainst(ZObject a, ZObject b)
        {
            if (!ZWorld.SniperNoPush || !a.SniperFiring || b.Kind != ZKind.Robot || b.Owner != a.Owner || b.SniperFiring) return false;
            ZWorld.SniperPushesHeld++;
            return true;
        }

        /// <summary>DV120: o walks towards this unit.</summary>
        bool SniperComes(ZObject o) => o.IsMoving && o.Dx * (double)(CenterX - o.CenterX) + o.Dy * (double)(CenterY - o.CenterY) > 0;

        /// <summary>DV120: an enemy worth stopping for: a robot, or a vehicle / gun whose crew it can snipe now.</summary>
        bool SniperStopsFor(ZObject o) =>
            o != null && o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != Owner && CanAttackObject(o) && (o.Kind == ZKind.Robot || o.CanBeSniped() || ToughSteady);   // DV123: a Tough stops for any unit it can hit (ZObject.ToughSteady.cs)

        /// <summary>DV120: the enemy (not <paramref name="except"/>) it stops for (or, standing, does not walk on from): the one it already
        /// shoots at, if still in range; else the nearest in range with a clear shot (ties by object order) - while it walks, only one
        /// that is SniperStandBand px inside the range or comes nearer (it does not stop at the very edge; standing, it does not walk off
        /// from an enemy at the edge either).</summary>
        ZObject SniperStopTarget(ZObject except, bool walking)
        {
            if (AttackObject != null && AttackObject != except && SniperStopsFor(AttackObject) && WithinAttackRadius(AttackObject)) return AttackObject;
            ZObject best = null; double bd = double.MaxValue, deep = walking ? AttackRadius - ZWorld.SniperStandBand : AttackRadius;
            foreach (var o in World.Objects)
            {
                if (o == except || !SniperStopsFor(o)) continue;
                double d = Dist(CenterX, CenterY, o.CenterX, o.CenterY);
                if (d >= bd || d > AttackRadius || (d > deep && !SniperComes(o)) || !WithinAttackRadius(o)) continue;
                bd = d; best = o;
            }
            return best;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV120 switch (see ZObject.SniperSteady.cs). View/ZGame.cs sets it at every start (off in 'Zod exact'); headless
        /// default off.</summary>
        public static bool SniperSteadyDeviation = false;
        /// <summary>DV120: a sniper's walk stops for an enemy (and an agro walk closes in on its own target) this many px inside its range
        /// (one tile: 128 px from it, still outside a grunt's 120). The three numbers were chosen with the counted check (K248b kit,
        /// tools/headless.py band: 0-24 px, 0-32 px, 0-3 s tried): the fewest walks and short steps with the fights at most 20 % longer.</summary>
        public static int SniperStandBand = 16;
        /// <summary>DV120: in position, an enemy this many px outside its range does not start a new walk while its squad fires.</summary>
        public static int SniperEdgeBand = 16;
        /// <summary>DV120: "fires": a shot of the squad in the last this many s (or a target held).</summary>
        public static double SniperHoldSecs = 1.0;
        /// <summary>DV120 part (4): a firing sniper takes no share of the overlap push against robots of its team.</summary>
        public static bool SniperNoPush = true;
        /// <summary>DV120 statistics (checks): agro / help walks ended by an enemy in range, attacks held (each hold once), attack holds
        /// kept for an enemy at the edge (ticks), agro walk-backs not walked, close-in ticks, passive checks in which a squad kept its
        /// position, overlap pushes a firing sniper did not take; per world: ticks a sniper stood to fire in front of a waypoint.</summary>
        public static int SniperAgroEnded, SniperAttackHeld, SniperKeptSpot, SniperNoReturn, SniperCloseIns, SniperHeldPosition, SniperPushesHeld;
        public int SniperStands;
    }
}
