using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV121 "the jeep runs robots over". A jeep is too weak to win a fair gun fight against a strong robot, so it drives at him a
    /// little faster than it drives anyway and runs him over (the run-over rule DV65, Sim/ZWorld.RunOver.cs: the hull's hit knocks him
    /// down and takes a quarter of his health, driving on over him squashes him). Weak robots (Grunts, Psychos, Pyros) are shot as
    /// before, unless one is CLOSE: that one is run over too. When the charge is over (the robot dead, lost or out of reach) the jeep
    /// drives back to the spot where it began, unless it has an order to go on with or was given a new one. The Zod engine has no
    /// run-over and no charge (refs/zod_engine/src: no crush code), so this is an OpenZ deviation; every number below is OpenZ's.
    /// <para>WHO: a crewed jeep of any team, player or computer (never the APC, the tanks, the crane or the missile launcher). WHOM: an
    /// enemy robot ON FOOT on the map (never a robot inside a vehicle, gun, fort or building, a fort's roof defender, nor one walking into
    /// a building or through the fort door). STRONG = <see cref="ChargeStrong"/>: Toughs, Lasers, Electrobots, Snipers and every hero
    /// squad member (the robots that win the gun fight against a jeep: Tough rockets of 1666 HP against its 1756, the Laser's 136 px and
    /// the Sniper's 144 px reach beyond its 120 px gun, the Sniper also shoots its driver out of the open seat, the Electrobot's bolt
    /// stuns it, the heroes' three times a grunt's health); weak = Grunts, Psychos, Pyros: only when the robot is within
    /// <see cref="ZWorld.ChargeClosePx"/> of the jeep (a charge on a weak robot goes on while he stays within
    /// <see cref="ZWorld.ChargeCloseKeepPx"/>). A Pyro is weak although its flame wins the duel: it reaches exactly as far as the jeep's gun
    /// and burns 700 HP a second, so a drive at it from afar only takes the jeep into the flame sooner (counted: lost either way).</para>
    /// <para>WHEN: (a) the jeep has no order and fights the robot from where it stands (its own target, CheckPassiveEngage): an AGRO
    /// waypoint on him is put in front, as Zod's own chase does; while it shoots a robot it does not run over (a weak one farther off),
    /// the closest one in range it does run over (a strong one, or a weak one that has come close) goes first; (b) its own chase or a
    /// squad's call for help (an AGRO waypoint; a call for help reaches as far as the chase it gave); (c) an ATTACK order on that robot
    /// (the player's named target, or the computer's); (d) an attack on the way of the computer's order. NOT on the way of the PLAYER's
    /// own move or order (his order first, as the calm rules have it: it fires from its way as before), except with Ctrl held
    /// (ZWaypoint.fightOnWay: the attack-move as it always was); not on HOLD POSITION, not in the calm start of a human team (DV69), not
    /// while a crocodile makes it wary, not after the game is over.</para>
    /// <para>HOW: inside the waypoint, where Zod stops dead and fires (ZObject.Duel.cs DuelStep), the jeep drives straight at the robot
    /// and on past him (<see cref="ZWorld.ChargeOverPx"/> beyond his centre, re-aimed every tick, so it follows a robot that steps aside;
    /// over him it keeps its heading, so the hull passes over him) at <see cref="ZWorld.ChargeSpeedMul"/> x its speed (RunSpeed, on top
    /// of the ground's speed and its damage slow-down), and keeps firing its gun all the way (a vehicle fires while it moves). It does
    /// not step aside for shells while it drives at him (<see cref="ZWorld.ChargeNoDodge"/>).</para>
    /// <para>NEVER INTO TROUBLE (checked before it starts and every <see cref="ZWorld.ChargeCheckSec"/> s while it drives): the jeep's
    /// box swept along the line to just past the robot stays on free vehicle tiles (water, deep water, cliffs, rocks, trees that close a
    /// tile, buildings, the map edge are walls) and out of the lanes' zones (bridge decks, one-lane chokepoints and their approaches);
    /// no unit stands in that lane (its own team's units, vehicles and guns of any team, empty vehicles: enemy robots in the lane are run
    /// over as well); the robot does not stand within reach of an enemy crewed gun (its attack radius) or an enemy fort (the fort gun's
    /// range from its centre) + <see cref="ZWorld.ChargeDangerMarginPx"/>; the robot is within <see cref="ChargeLeash"/> (plus a call for
    /// help's extra reach) of the spot where the charge began. A failed
    /// check: it stops and fires as before and looks again <see cref="ZWorld.ChargeRetrySec"/> s later. Held by traffic for
    /// <see cref="ZWorld.ChargeHeldSec"/> s, blocked by the ground, or driving longer than <see cref="ZWorld.ChargeMaxSec"/> s: the same.</para>
    /// <para>AFTERWARDS: the robot dead or lost and the waypoint over: another robot it wants within range (the rest of a squad) is
    /// charged at once, the first spot kept; else, when the jeep has no waypoint left, it drives back to that spot (a plain MOVE
    /// waypoint, no attack-move, normal speed; it fires at what it meets on the way and charges again from the way back if a robot it
    /// wants comes in reach, the first spot kept), then goes on as before. A waypoint left in its list (the order it had) = it goes on
    /// with that order. A new order (player or computer, ZWorld.SendWaypoints) ends the charge or the drive back at once. This step
    /// runs in the jeep's own server pass right after its waypoint step and before its passive check, so the drive back is in its list
    /// before an idle unit's own grab of a flag, an empty vehicle or a crate nearby would look (from its spot it grabs as before).</para>
    /// <para>FAIR CHANCES (the robot it drives at fights back): a Laser or a hero on foot that a jeep drives at aims at its driver once
    /// it is within <see cref="ZWorld.ChargeSnipePx"/>: each of his hits on it is the shot that snipes the driver with
    /// <see cref="ZWorld.ChargeSnipeLaser"/> / <see cref="ZWorld.ChargeSnipeHero"/> (the game's own driver-shot roll, DC2 / DV27:
    /// ZObject.CombatNB.cs SnipeChanceAt, and ZWorld.DriverHitDamage, here the driver's whole pool), else it hits the hull; a Tough
    /// stands his ground against a jeep driving at him (<see cref="ChargeToughWait"/>): he aims at it and lets it come to a distance drawn
    /// when it begins (about 2 to 3 tiles of open ground; on a road, where it drives 1.689 x as fast, farther out: <see cref="ChargeToughFar"/>),
    /// then fires at once, his rocket aimed from where the jeep will be when it bursts with
    /// <see cref="ZWorld.ChargeRocketScatter"/> of Zod's scatter (ZObject.cs ProcessAttackDamage, after Zod's lead and scatter: the same
    /// draws), and his next rocket follows <see cref="ZWorld.ChargeToughReload"/> s later: fired early enough, the second rocket destroys
    /// the jeep; let it come too close, the hull knocks him over first (about half and half from any distance). Only while that jeep
    /// drives at that robot (<see cref="ChargingAt"/>); every other shot, rocket, robot and vehicle as before. The light tank does not charge
    /// (<see cref="ZWorld.ChargeLightTank"/> off: standing it already wins against every lone strong robot, counted in the check's part I).</para>
    /// <para>Deterministic: sim positions, sim time, object order, Zod's rand() stream; one draw of its own: how close a Tough lets a jeep
    /// that drives at him come (World.Rng, once when it begins to drive at him; nowhere else). The step aside it does not take draws none;
    /// the driver shot uses the roll the game draws for every such hit anyway, the rocket the scatter Zod draws anyway. Switch
    /// <see cref="ZWorld.JeepChargeDeviation"/>: off by default headless (parity rows, counted checks); View/ZGame.cs sets it with the
    /// run-over rule (off in 'Zod exact' and with the run-over option off). Off: not a field of any unit changes, every match is the
    /// game as it was, unit for unit.</para>
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV121: one jeep's charge, from its first drive until it is back at its spot (null = none).</summary>
        sealed class JeepCharge
        {
            public int targetId = -1, startX, startY, leash;
            public Team owner;
            public ZWaypoint wp, back;
            public bool hasWp, returning, driving, drove, wpDrove, inserted;
            public double insertedAt, hx = 1, hy, driveFrom = -100, lastDrive = -100, retryAt = -100, nextCheck = -100, movedAt, movedX, movedY;
            public long drivenTick = -1;
        }
        JeepCharge charge;
        bool chargeMoving;
        double chargeLook;

        /// <summary>DV121: the jeep drove at its robot in this tick (the checks, the view).</summary>
        public bool ChargeDriving => charge != null && World != null && charge.drivenTick == World.TickIndex;
        /// <summary>DV121: the jeep is on its way back to the spot where its charge began.</summary>
        public bool ChargeReturning => charge != null && charge.returning;
        /// <summary>DV121: a charge (or its drive back) is under way.</summary>
        public bool ChargeOn => charge != null;
        /// <summary>DV121: the robot it charges (-1 = none).</summary>
        public int ChargeTargetId => charge != null && !charge.returning ? charge.targetId : -1;
        /// <summary>DV121: the spot the charge began at (the jeep's centre when none).</summary>
        public int ChargeStartX => charge != null ? charge.startX : CenterX;
        public int ChargeStartY => charge != null ? charge.startY : CenterY;

        /// <summary>DV121: the robots a jeep drives at wherever it fights them: the tough ones (see the class notes).</summary>
        public static bool ChargeStrong(ZObject t) => t != null && t.Kind == ZKind.Robot && (t.IsHero || t.Type == "tough" || t.Type == "laser" || t.Type == ZElectro.Type || t.Type == "sniper");

        /// <summary>DV121: never farther than this from the spot where the charge began (px): its range + Zod's agro distance.</summary>
        int ChargeLeash => AttackRadius + World.S.agro_distance;

        bool ChargeUnit() => ZWorld.RunOverDeviation && ChargeType && Owner != Team.Neutral && Drivers.Count > 0 && !Destroyed && KillAt < 0 && MoveSpeed > 0;

        /// <summary>DV121: a vehicle of a type that charges (the jeep; the light tank when <see cref="ZWorld.ChargeLightTank"/>).</summary>
        bool ChargeType => Kind == ZKind.Vehicle && (Type == "jeep" || (ZWorld.ChargeLightTank && Type == "light"));

        /// <summary>RunSpeed: the speed-up of a charge (only inside its own drive step).</summary>
        bool ChargeHurry(out double speed) { speed = ZWorld.ChargeSpeedMul; return chargeMoving; }

        /// <summary>DodgeMissile: no step aside while it drives at its robot (before any random draw).</summary>
        bool ChargeNoDodgeNow() => ZWorld.ChargeNoDodge && charge != null && !charge.returning && charge.driving && World != null && World.Time - charge.lastDrive < 0.05;

        /// <summary>ZWorld.SendWaypoints: a new order ends the charge or the drive back at once.</summary>
        internal void ChargeOrdered()
        {
            if (charge == null) return;
            charge = null;
            World.JeepChargeOrdersTaken++;
        }

        /// <summary>Its waypoint list holds w (a dodge, a step aside may stand in front of it).</summary>
        int ChargeWpIndex(ZWaypoint w) { for (int i = 0; i < Waypoints.Count; i++) if (Waypoints[i].Equals(w)) return i; return -1; }

        /// <summary>The attack in front is one the jeep may charge from: not on the way of the player's own order (his order first).</summary>
        bool ChargeOrderAllows(ZWaypoint wp)
        {
            if (wp.mode == WpMode.Agro || wp.playerGiven || !World.IsHumanTeam(Owner)) return true;
            for (int i = 1; i < Waypoints.Count; i++) if (Waypoints[i].playerGiven && !Waypoints[i].fightOnWay) return false;
            return true;
        }

        /// <summary>A robot this jeep runs over: on foot, an enemy, strong or close; within the leash of the spot (sx, sy) (plus the extra
        /// reach of a squad's call for help, <paramref name="extra"/>: the chase the call gave it reaches that far, ProcessAgroWP).</summary>
        bool ChargeWants(ZObject t, int sx, int sy, bool keep, int extra = 0)
        {
            if (t == null || t.Kind != ZKind.Robot || t.Destroyed || t.KillAt >= 0 || t.Health <= 0) return false;
            if (t.Owner == Team.Neutral || t.Owner == Owner || t.IsFortDefender || t.PathInBuildingStage || t.FortRushOn) return false;
            if (!ChargeStrong(t) && !Within(CenterX, CenterY, t.CenterX, t.CenterY, keep ? ZWorld.ChargeCloseKeepPx : ZWorld.ChargeClosePx)) return false;
            return Within(sx, sy, t.CenterX, t.CenterY, ChargeLeash + extra);
        }

        /// <summary>The way to t is free and t does not stand under an enemy gun or fort (see the class notes).</summary>
        bool ChargeWayOk(ZObject t, out bool danger)
        {
            danger = ChargeUnderFire(t);
            return !danger && ChargeLaneClear(t);
        }

        bool ChargeLaneClear(ZObject t)
        {
            double cx = PosX + (WidthPix >> 1), cy = PosY + (HeightPix >> 1), dx = t.CenterX - cx, dy = t.CenterY - cy, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return true;
            double ux = dx / len, uy = dy / len, end = len + ZWorld.ChargeOverPx * 0.5;
            var lanes = World.Pathing != null && World.Pathing.Enabled ? World.Pathing : null;
            for (double s = 4; ; s += 4)
            {
                if (s > end) s = end;
                double px = cx + ux * s, py = cy + uy * s;
                int bx = (int)Math.Floor(px) - (WidthPix >> 1), by = (int)Math.Floor(py) - (HeightPix >> 1);
                if (!DuelBoxFree(bx, by) || !ChargeBoxDry(bx, by)) return false;
                if (lanes != null && ChargeLaneZoneAt(lanes, (int)px, (int)py)) return false;
                if (s >= end) break;
            }
            foreach (var o in World.Objects)
            {
                if (o == this || o == t || !ZPathing.Separable(o)) continue;
                if (o.Kind == ZKind.Robot && o.Owner != Owner && o.Owner != Team.Neutral) continue;   // an enemy robot in the lane is run over as well
                double ox = o.CenterX - cx, oy = o.CenterY - cy, along = ox * ux + oy * uy;
                if (along <= 0.5 || along > end) continue;                                   // behind it (or exactly beside it): not in its way
                if (Math.Abs(ox * uy - oy * ux) < ZPathing.MinSpacing(this, o) + 2) return false;
            }
            return true;
        }

        /// <summary>(x, y) lies in a lane's zone (a bridge deck or a one-lane chokepoint with its approach at both ends, ZLanes.cs,
        /// where the traffic waits its turn): no charge through it. The margin round it where nobody parks (the keep-clear area) may be
        /// driven across: the jeep does not park there, after the run-over it drives back to its spot.</summary>
        static bool ChargeLaneZoneAt(ZPathing lanes, int x, int y)
        {
            foreach (var L in lanes.Lanes) if (x >= L.ZX0 && x < L.ZX1 && y >= L.ZY0 && y < L.ZY1) return true;
            return false;
        }

        /// <summary>No water under the jeep's box at (x, y) (the box as DuelBoxFree takes it).</summary>
        bool ChargeBoxDry(int x, int y)
        {
            int w = WidthPix - 2, h = HeightPix - 2; x += 1; y += 1;
            for (int j = y >> 4; j <= (y + h - 1) >> 4; j++)
                for (int i = x >> 4; i <= (x + w - 1) >> 4; i++)
                {
                    var k = World.Map.ClassAt(i, j);
                    if (k == ZClass.Water || k == ZClass.DeepWater) return false;
                }
            return true;
        }

        /// <summary>t stands within reach of an enemy crewed gun or an enemy fort (its gun's range from the fort's centre).</summary>
        bool ChargeUnderFire(ZObject t)
        {
            foreach (var o in World.Objects)
            {
                if (o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner) continue;
                int reach;
                if (o.Kind == ZKind.Cannon) { if (o.Drivers.Count == 0 || !o.CanAttack()) continue; reach = o.AttackRadius; }
                else if (o.Kind == ZKind.Building && (o.Type == "fort_front" || o.Type == "fort_back") && ZWorld.FortGunDeviation)
                { var hw = World.S.Unit("howitzer"); if (hw == null) continue; reach = FortGunRange(hw); }
                else continue;
                if (Within(o.CenterX, o.CenterY, t.CenterX, t.CenterY, reach + ZWorld.ChargeDangerMarginPx)) return true;
            }
            return false;
        }

        /// <summary>ZObject.Duel.cs DuelStep, first: an ATTACK / AGRO waypoint whose target is in range (where Zod stops and fires). True =
        /// the charge drove this tick (result ZDuel.Manoeuvre); false = no charge now, Zod's (or the duel's) own step goes on.</summary>
        internal bool ChargeStep(ZObject t, double dt, bool agro, out int result)
        {
            result = ZDuel.Stand;
            if (!ZWorld.JeepChargeDeviation || Waypoints.Count == 0 || t == null) return false;
            if (!ChargeUnit() || World.GameOver) { if (charge != null) charge.driving = false; return false; }
            var wp = Waypoints[0];
            if ((wp.mode != WpMode.Attack && wp.mode != WpMode.Agro) || wp.refId != t.Id) return false;
            var c = charge;
            if (c != null && c.owner != Owner) charge = c = null;                            // emptied and taken by the other side: a new jeep
            double now = World.Time;
            if (c == null || c.returning || !c.hasWp || !c.wp.Equals(wp) || c.targetId != t.Id)
            {                                                                                // a charge begins inside an order / chase it already has
                if (!ChargeOrderAllows(wp)) return false;
                int sx = CenterX, sy = CenterY, ex = 0;
                if (c != null) { sx = c.startX; sy = c.startY; ex = c.leash; }               // a chain: the first spot is kept
                else if (wp.mode == WpMode.Agro && Waypoints.Count == 1) { sx = cur.agroX; sy = cur.agroY; ex = wp.leash; }   // its own chase (or a call for help, with its extra reach): back to where the chase began
                if (!ChargeWants(t, sx, sy, false, ex) || now < chargeLook) return false;
                chargeLook = now + ZWorld.ChargeCheckSec;                                    // (the way is looked at every ChargeCheckSec s)
                if (!ChargeWayOk(t, out bool danger0)) { if (danger0) World.JeepChargeRefusedDanger++; else World.JeepChargeRefusedWay++; return false; }
                c = charge ??= new JeepCharge();                                             // a charge begins only when it can drive at him now
                c.owner = Owner; c.startX = sx; c.startY = sy; c.leash = ex; c.returning = false; c.hasWp = true; c.wp = wp; c.targetId = t.Id; c.inserted = false; c.wpDrove = false; c.driving = false; c.retryAt = -100;
                ChargeDriveStart(c, t, now);
            }
            else if (!c.driving)
            {
                if (now < c.retryAt) return false;
                if (!ChargeWants(t, c.startX, c.startY, false, c.leash)) { c.retryAt = now + ZWorld.ChargeRetrySec; return false; }
                if (!ChargeWayOk(t, out bool danger))
                {
                    c.retryAt = now + ZWorld.ChargeRetrySec;
                    if (danger) World.JeepChargeRefusedDanger++; else World.JeepChargeRefusedWay++;
                    return false;
                }
                ChargeDriveStart(c, t, now);
            }
            else
            {
                if (now - c.lastDrive > 0.05) { c.movedAt = now; c.movedX = PosX; c.movedY = PosY; }   // back from a pause (a step aside): the held clock starts again
                string stop = null;
                if (now - c.driveFrom > ZWorld.ChargeMaxSec) stop = "time";
                else if (now >= c.nextCheck)
                {
                    c.nextCheck = now + ZWorld.ChargeCheckSec;
                    if (!ChargeWants(t, c.startX, c.startY, true, c.leash)) stop = "reach";
                    else if (!ChargeWayOk(t, out _)) stop = "way";
                }
                if (stop != null) { ChargeStop(c, now, stop); return false; }
            }
            Engage(t);                                                                       // it keeps firing (Zod's ProcessAttackDamage, its own cooldown)
            double cx = PosX + (WidthPix >> 1), cy = PosY + (HeightPix >> 1), dx = t.CenterX - cx, dy = t.CenterY - cy, d = Math.Sqrt(dx * dx + dy * dy);
            int ax, ay;
            if (d > ZWorld.ChargeOverPx)
            {                                                                                // at him and on past him, re-aimed every tick
                c.hx = dx / d; c.hy = dy / d;
                ax = (int)Math.Round(t.CenterX + c.hx * ZWorld.ChargeOverPx); ay = (int)Math.Round(t.CenterY + c.hy * ZWorld.ChargeOverPx);
            }
            else
            {                                                                                // on him: straight on, so the hull passes over him
                ax = (int)Math.Round(cx + c.hx * (ZWorld.ChargeOverPx + 8)); ay = (int)Math.Round(cy + c.hy * (ZWorld.ChargeOverPx + 8));
            }
            SetTarget(ax, ay); SetVelocity();
            chargeMoving = true;
            bool moved;
            try { moved = ProcessMove(dt, true, out _, out _); }
            finally { chargeMoving = false; }
            c.drivenTick = World.TickIndex; c.lastDrive = now;
            if (!moved) { ChargeStop(c, now, "blocked"); StopMove(); result = ZDuel.Manoeuvre; return true; }
            if (Math.Abs(PosX - c.movedX) + Math.Abs(PosY - c.movedY) >= ZWorld.ChargeHeldPx) { c.movedX = PosX; c.movedY = PosY; c.movedAt = now; }
            else if (now - c.movedAt > ZWorld.ChargeHeldSec) ChargeStop(c, now, "held");
            result = ZDuel.Manoeuvre;
            return true;
        }

        void ChargeDriveStart(JeepCharge c, ZObject t, double now)
        {
            c.driving = true; c.drove = true; c.wpDrove = true; c.driveFrom = now; c.nextCheck = now + ZWorld.ChargeCheckSec; c.movedAt = now; c.movedX = PosX; c.movedY = PosY;
            cur.pfPoints = null; cur.pathFindingId = 0; cur.gotPfResponse = true;          // the approach path is spent (as a duel manoeuvre)
            World.JeepCharges++;
            if (ChargeStrong(t)) World.JeepChargesStrong++; else World.JeepChargesClose++;
        }

        void ChargeStop(JeepCharge c, double now, string why)
        {
            c.driving = false; c.retryAt = now + ZWorld.ChargeRetrySec;
            switch (why)
            {
                case "time": World.JeepChargeStopsTime++; break;
                case "reach": World.JeepChargeStopsReach++; break;
                case "way": World.JeepChargeStopsWay++; break;
                case "blocked": World.JeepChargeStopsBlocked++; break;
                default: World.JeepChargeStopsHeld++; break;
            }
        }

        /// <summary>The next robot it wants in range (the closest; the first spot kept), or null.</summary>
        ZObject ChargeNext(JeepCharge c) => ChargeNearest(c.startX, c.startY, c.leash, null);

        /// <summary>The closest robot in range it runs over from the spot (sx, sy) (a strong one, or a weak one that is close) with a free
        /// way, other than <paramref name="skip"/>, or null.</summary>
        ZObject ChargeNearest(int sx, int sy, int extra, ZObject skip)
        {
            ZObject best = null; double bd = double.MaxValue;
            foreach (var o in World.Objects)
            {
                if (o == skip || o.Kind != ZKind.Robot || !ChargeWants(o, sx, sy, false, extra) || !CanAttackObject(o) || !WithinAttackRadius(o)) continue;
                double d = DistanceFrom(o);
                if (d >= bd || !ChargeWayOk(o, out _)) continue;
                best = o; bd = d;
            }
            return best;
        }

        /// <summary>The first n waypoints are its own steps aside from shells (Zod's DodgeMissile), nothing else (n = 0: true).</summary>
        bool ChargeOnlyDodges(int n)
        {
            for (int i = 0; i < n && i < Waypoints.Count; i++) if (Waypoints[i].mode != WpMode.Dodge) return false;
            return true;
        }

        /// <summary>Steps aside stand in front of the charge's waypoint (taken before the drive began, or in a pause): once it may drive
        /// at its robot again (the pause over, he is still one it wants, the way is free) they are dropped and it drives at him.</summary>
        void ChargeDropDodges(JeepCharge c, int k)
        {
            if (!ChargeOnlyDodges(k) || World.Time < c.retryAt) return;
            var t = World.Get(c.targetId);
            if (!ChargeWants(t, c.startX, c.startY, true, c.leash) || World.Time < chargeLook) return;
            chargeLook = World.Time + ZWorld.ChargeCheckSec;
            if (!CanAttackObject(t) || !WithinAttackRadius(t) || !ChargeWayOk(t, out _)) return;
            Waypoints.RemoveRange(0, k);
            World.JeepChargeDodgesDropped += k;
        }

        void ChargeAt(ZObject t, JeepCharge c)
        {
            var wp = new ZWaypoint { mode = WpMode.Agro, refId = t.Id, x = t.CenterX, y = t.CenterY };
            Waypoints.Insert(0, wp);
            c.wp = wp; c.hasWp = true; c.targetId = t.Id; c.returning = false; c.driving = false; c.retryAt = -100; c.inserted = true; c.wpDrove = false; c.insertedAt = World.Time;
            Engage(t);
        }

        /// <summary>ZObject.cs ProcessServer, right after its waypoint step and before its passive check (so the drive back is in its list
        /// before an idle unit's own flag / vehicle / crate grab would look): a jeep's own fight, the next robot of a squad, the drive back.</summary>
        internal void ChargeAfterOrders()
        {
            if (!ChargeType) return;
            if (!ZWorld.JeepChargeDeviation) { charge = null; return; }
            if (!ChargeUnit() || World.GameOver || (charge != null && charge.owner != Owner)) { charge = null; return; }   // (emptied and taken by the other side: a new jeep)
            var c = charge;
            if (c != null && c.returning)
            {
                int k = ChargeWpIndex(c.back);
                if (k < 0)
                {                                                                            // the way back is over (arrived, or it gave up on the way)
                    if (Within(CenterX, CenterY, c.startX, c.startY, ZWorld.ChargeHomePx * 2)) World.JeepChargeHome++;
                    charge = null;
                    return;
                }
                var a = AttackObject;
                if (k != 0 || a == null || !ChargeWants(a, c.startX, c.startY, false, c.leash) || World.Time < chargeLook) return;
                chargeLook = World.Time + ZWorld.ChargeCheckSec;
                if (CanAttackObject(a) && WithinAttackRadius(a) && ChargeWayOk(a, out _))
                {                                                                            // a robot it wants comes in reach on the way back: it goes for him
                    Waypoints.RemoveAt(0);
                    ChargeAt(a, c);
                    World.JeepChargeChains++;
                }
                return;
            }
            if (c != null)
            {
                if (c.hasWp)
                {
                    int k = ChargeWpIndex(c.wp);
                    if (k == 0 && c.inserted && !c.wpDrove && World.Time - c.insertedAt >= 0.05)
                    {                                                                        // the chase the rule put in front did not get to drive at him: taken out
                        KillWP(); c.hasWp = false;                                           // again, the jeep fights from where it is as before; the episode ends below
                        if (!c.drove) { charge = null; return; }
                    }
                    else if (k == 0) return;                                                 // still at it
                    if (k > 0) { ChargeDropDodges(c, k); return; }                           // steps aside in front of it: no more of them once it can drive at him
                }
                if (Waypoints.Count > 0 || !c.drove) { charge = null; return; }              // its waypoint is over and it has an order left: on with that order (or it never drove)
                var next = ChargeNext(c);
                if (next != null) { ChargeAt(next, c); World.JeepChargeChains++; return; }    // the rest of the squad, the first spot kept
                if (Within(CenterX, CenterY, c.startX, c.startY, ZWorld.ChargeHomePx)) { World.JeepChargeHome++; charge = null; return; }
                c.back = ZWaypoint.Move(c.startX, c.startY, false);
                Waypoints.Add(c.back);
                c.returning = true; c.hasWp = false; c.driving = false;
                World.JeepChargeReturns++;
                return;
            }
            if (!ChargeOnlyDodges(Waypoints.Count) || HoldPosition || CrocWary || World.CalmStart(Owner)) return;   // its own fight only (an idle jeep, or one only stepping aside, as Zod's own chase)
            var fought = AttackObject;
            if (fought == null || World.Time < chargeLook) return;
            bool wants = ChargeWants(fought, CenterX, CenterY, false);
            if (!wants && fought.Kind != ZKind.Robot) return;                                   // a vehicle, a gun or a building: fought as before
            chargeLook = World.Time + ZWorld.ChargeCheckSec;                                 // it looks at the robots and the way every ChargeCheckSec s
            ZObject t = null;
            if (wants && CanAttackObject(fought) && WithinAttackRadius(fought))
            {
                if (ChargeWayOk(fought, out bool danger)) t = fought;
                else if (danger) World.JeepChargeRefusedDanger++; else World.JeepChargeRefusedWay++;
            }
            if (t == null) t = ChargeNearest(CenterX, CenterY, 0, fought);                      // it shoots a robot it does not run over (a weak one farther off): one in range
            if (t == null) return;                                                           // it does run over (a strong one, or a weak one that is close) goes first
            if (t != fought) World.JeepChargeSwitched++;
            if (Waypoints.Count > 0) { World.JeepChargeDodgesDropped += Waypoints.Count; Waypoints.Clear(); }   // its own steps aside: it drives at him instead
            charge = new JeepCharge { owner = Owner, startX = CenterX, startY = CenterY };
            ChargeAt(t, charge);
        }

        // ------------------------------------------------------------------ the robot it drives at fights back (fair chances)
        /// <summary>DV121: this vehicle drives at robot r in a charge right now (its charge is on r and it drove this tick or the tick
        /// before: the robot's own step may come before or after the vehicle's in the tick).</summary>
        internal bool ChargingAt(ZObject r) => charge != null && !charge.returning && charge.driving && r != null && charge.targetId == r.Id
                                               && World != null && World.Time - charge.lastDrive <= ZWorld.ChargeRecentSec;

        /// <summary>DV121: this robot (a Laser or a hero on foot) shoots at vehicle v that drives at HIM in a charge and is within
        /// <see cref="ZWorld.ChargeSnipePx"/>: he aims at its driver (the windscreen comes straight at him).</summary>
        bool ChargeDriverShot(ZObject v) =>
            ZWorld.JeepChargeDeviation && Kind == ZKind.Robot && (IsHero || Type == "laser") && v != null && v.Kind == ZKind.Vehicle && v.charge != null
            && v.ChargingAt(this) && Within(CenterX, CenterY, v.CenterX, v.CenterY, ZWorld.ChargeSnipePx);

        /// <summary>DV121: the chance that a hit of his on such a vehicle snipes its driver (Laser / hero).</summary>
        double ChargeSnipeOdds => IsHero ? ZWorld.ChargeSnipeHero : ZWorld.ChargeSnipeLaser;

        /// <summary>ZObject.CombatNB.cs SnipeChanceAt: the game's own driver-shot roll (DC2 / DV27, drawn exactly as before: one roll per
        /// hit) decides with <see cref="ChargeSnipeOdds"/> whether his hit on a vehicle that drives at him from close is the shot that
        /// snipes its driver (else it hits the hull). A veteran crew stays buttoned up here too (DV12, ZVeterancy.cs: x VeteranSnipeFactor,
        /// as in SnipeChanceAgainst), so the charge does not take that perk away.</summary>
        internal double ChargeSnipeChance(ZObject t, double sc) =>
            ChargeDriverShot(t) ? ChargeSnipeOdds * (t.VeteranActive ? ZWorld.VeteranSnipeFactor : 1.0) : sc;

        /// <summary>ZWorld.cs DriverHitDamage: such a driver hit takes the driver's whole pool: the driver is sniped (the vehicle stops,
        /// empty, as after any driver snipe, DamageDriverHealth).</summary>
        internal int ChargeDriverHit(ZObject target, int d)
        {
            if (!ChargeDriverShot(target)) return d;
            World.JeepChargeDriverHits++;
            int pool = target.Drivers.Count > 0 ? target.Drivers[0] : 0;
            return Math.Max(d, pool);
        }

        /// <summary>ZObject.cs ProcessAttackDamage, a rocket right after Zod's lead and scatter: a Tough on foot whose target drives at
        /// him in a charge aims true. The lead is worked out from where the vehicle will be when the rocket bursts (it drives straight at
        /// him at its speed now), and only <see cref="ZWorld.ChargeRocketScatter"/> of the scatter Zod drew is kept (the same draws as
        /// before, nothing more drawn). Every other rocket: untouched.</summary>
        internal void ChargeRocketAim(ZObject t, ref int tx, ref int ty)
        {
            if (!ZWorld.JeepChargeDeviation || Kind != ZKind.Robot || Type != "tough" || t == null || t.Kind != ZKind.Vehicle || !t.ChargingAt(this)) return;
            if (!EstimateMissileTarget(t, MissileSpeed, out int ex, out int ey)) { ex = t.CenterX; ey = t.CenterY; }   // Zod's point before the scatter (the same numbers again, no draw)
            int ox = tx - ex, oy = ty - ey;                                                                          // the scatter it drew
            double px = t.CenterX, py = t.CenterY;
            if (ZWorld.ChargeRocketLead && MissileSpeed > 0)
                for (int i = 0; i < 12; i++)
                {                                                                                                    // where it is when the rocket bursts
                    double fl = Math.Sqrt((px - CenterX) * (px - CenterX) + (py - CenterY) * (py - CenterY)) / MissileSpeed;
                    px = t.CenterX + t.Dx * fl; py = t.CenterY + t.Dy * fl;
                }
            else { px = ex; py = ey; }
            tx = (int)Math.Round(px + ox * ZWorld.ChargeRocketScatter); ty = (int)Math.Round(py + oy * ZWorld.ChargeRocketScatter);
            World.JeepChargeAimedRockets++;
            if (ZWorld.ChargeToughReload > 0 && t.Type == "jeep")
            {                                                                                                        // his next rocket at a jeep: as soon as his aim is steady again
                double r = ZWorld.ChargeToughReload;
                if (ZWorld.ChargeToughReloadSpread > 0) r += World.Rng.Roll() * ZWorld.ChargeToughReloadSpread;      // (decision sweep only: 0 in the game, no draw)
                nextDamageTime = World.Time + r;
                World.JeepChargeQuickReloads++;
            }
        }

        int ctHoldJeep = -1; double ctHoldPx = -1;

        /// <summary>DV121: how close (px) this Tough lets the jeep that drives at him come before his first rocket (drawn when it began to
        /// drive at him), -1 = none (the Play stage's report).</summary>
        public double ChargeToughFirePx => ctHoldJeep >= 0 ? ctHoldPx : -1;

        /// <summary>ZObject.cs ProcessAttackDamage, right before DV11's prepare (ZObject.ToughPrepare.cs): a Tough on foot whose target
        /// drives at him in a charge (a jeep: the owner's case; the light tank never charges in the game) stands his ground. He aims at it and lets it come: his rocket waits until the jeep is within a
        /// distance drawn once when it begins to drive at him (<see cref="ZWorld.ChargeToughHoldPx"/> + 0..<see cref="ZWorld.ChargeToughHoldSpreadPx"/>,
        /// one World.Rng draw, Zod's rand(); counted as open ground along its way, <see cref="ChargeToughFar"/>), then it leaves at once
        /// (no DV11 aim-up: he has been aiming at it all along), aimed true
        /// (<see cref="ChargeRocketAim"/>), and his next one follows <see cref="ZWorld.ChargeToughReload"/> s later. True = he holds his rocket
        /// this tick (no missile, no cooldown used). Every other target, every other robot, a grenade throw: as before.</summary>
        internal bool ChargeToughWait(double time)
        {
            if (!ZWorld.JeepChargeDeviation || Kind != ZKind.Robot || Type != "tough") return false;
            var t = AttackObject;
            if (t == null || t.Kind != ZKind.Vehicle || t.Type != "jeep" || !t.ChargingAt(this)) { ctHoldJeep = -1; return false; }
            if (Grenades > 0 || (Leader != null && Leader.Grenades > 0)) return false;                          // a grenade throw: as before (zobject.cpp:2118)
            if (ZWorld.ChargeToughHoldPx > 0)
            {
                if (ctHoldJeep != t.Id)
                {                                                                                                // the jeep began to drive at him: how close he lets it come
                    ctHoldJeep = t.Id; ctHoldPx = ZWorld.ChargeToughHoldPx;
                    if (ZWorld.ChargeToughHoldSpreadPx > 0) ctHoldPx += World.Rng.Next(ZWorld.ChargeToughHoldSpreadPx + 1);
                    World.JeepChargeToughStands++;
                }
                if (ChargeToughFar(t)) { tpLastEngaged = time; tpReadyAt = -1; World.JeepChargeToughHeldTicks++; return true; }   // aiming, waiting (the tube stays up)
            }
            if (ZWorld.ChargeToughNoAimUp) { tpLastEngaged = time; tpReadyAt = -1; }                            // no aim-up: DV11's prepare would hold 0.8 s more
            return false;
        }

        /// <summary>DV121 (the Tough stands his ground): the jeep t is still farther than the distance he lets it come (ctHoldPx), counted as
        /// open ground. The race is one of time (his second rocket ChargeToughReload s after the first, or the hull first), and on a road
        /// the jeep drives 1.689 x as fast (ZConst.ROAD_SPEED): so its straight way to him is looked at tile by tile, from where the hull
        /// meets him (ZWorld.ChargeToughMeetPx) to the jeep, every road tile counting 1/1.689 of its length (a ford 1/0.7); he fires once
        /// that open-ground length is within ctHoldPx. On a way over open ground only: exactly the plain distance test, as before.
        /// No random draw, sim positions and the map only.</summary>
        bool ChargeToughFar(ZObject t)
        {
            if (ZWorld.ChargeToughGround)
            {
                double dx = t.CenterX - CenterX, dy = t.CenterY - CenterY, d = Math.Sqrt(dx * dx + dy * dy), meet = ZWorld.ChargeToughMeetPx;
                if (d > meet)
                {
                    int n = (int)Math.Ceiling((d - meet) / 4.0);
                    double step = (d - meet) / n, open = meet;
                    bool other = false;
                    for (int i = 0; i < n; i++)
                    {                                                                                                // every 4 px of its way, from him out to the jeep
                        double s = meet + (i + 0.5) * step;
                        double tile = World.Map.TileWalkSpeed((int)Math.Round(CenterX + dx / d * s), (int)Math.Round(CenterY + dy / d * s));
                        if (tile <= 0) tile = 1.0;                                                                   // (a rock corner beside its way: counted as open ground)
                        if (tile != 1.0) other = true;
                        open += step / tile;
                    }
                    if (other) return open > ctHoldPx;
                }
            }
            return !Within(CenterX, CenterY, t.CenterX, t.CenterY, ctHoldPx);                                        // open ground all the way: the plain distance
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV121 (Sim/ZObject.JeepCharge.cs). Off by default headless; View/ZGame.cs sets it at every start with the run-over rule.</summary>
        public static bool JeepChargeDeviation;
        /// <summary>DV121: the jeep's speed while it drives at a robot, x its normal speed (on top of the ground and damage factors).</summary>
        public static double ChargeSpeedMul = 1.15;
        /// <summary>DV121: a weak robot (Grunt, Psycho, Pyro) is run over when it is this close to the jeep (centre to centre,
        /// px; 48 = 3 tiles, about 6 m: about 3 m of open ground between the jeep's nose and the robot); the charge on him goes on while
        /// he stays within ChargeCloseKeepPx.</summary>
        public const int ChargeClosePx = 48, ChargeCloseKeepPx = 64;
        /// <summary>DV121: the aim point lies this far past the robot's centre (px), so the hull drives on over him.</summary>
        public const int ChargeOverPx = 24;
        /// <summary>DV121: a robot standing this far (px) inside the reach of an enemy crewed gun or fort is not charged.</summary>
        public const int ChargeDangerMarginPx = 16;
        /// <summary>DV121: back at its spot = within this many px (no drive back from closer).</summary>
        public const int ChargeHomePx = 8;
        /// <summary>DV121 (s): the way checked again while it drives; a failed check or a stop: looked at again after ChargeRetrySec;
        /// held by traffic this long (and less than ChargeHeldPx moved): stop; one drive at most ChargeMaxSec.</summary>
        public const double ChargeCheckSec = 0.25, ChargeRetrySec = 1.0, ChargeHeldSec = 0.6, ChargeHeldPx = 1.5, ChargeMaxSec = 10.0;
        /// <summary>DV121: no step aside from a shell while the jeep drives at its robot (counted: SPEC.md).</summary>
        public static bool ChargeNoDodge = true;
        /// <summary>DV121 (fair chances): a Laser or a hero aims at the driver of a vehicle that drives at him once it is
        /// this close (centre to centre, px; 80 = 5 tiles); each of his hits on it then snipes the driver with ChargeSnipeLaser /
        /// ChargeSnipeHero (the roll the game draws anyway for a driver hit), else it hits the hull. Counted, 100 fights a row from
        /// 6 tiles: the jeep wins 43 of 100 against a Laser, 48 of 100 against the hero pair (without it: 86 / 76 of 100).</summary>
        public static int ChargeSnipePx = 80;
        public static double ChargeSnipeLaser = 0.13, ChargeSnipeHero = 0.22;
        /// <summary>DV121 (fair chances): a Tough's rocket at a vehicle that drives at him: the lead worked out from where it will be
        /// when the rocket bursts (true = on), and this share of Zod's scatter kept (0.5: the average miss 15 px -> 6 px).</summary>
        public static bool ChargeRocketLead = true;
        public static double ChargeRocketScatter = 0.5;
        /// <summary>DV121 (fair chances, the Tough a jeep drives at stands his ground): his first rocket at it waits until it is within
        /// ChargeToughHoldPx + a drawn 0..ChargeToughHoldSpreadPx px (centre to centre; 30-50 px = about 2 to 3 tiles: one draw when it
        /// begins to drive at him), with no DV11 aim-up (ChargeToughNoAimUp: he has been aiming at it); his next rocket at it
        /// ChargeToughReload s after one (1.0 = Zod's longest aim hold, rtough.cpp:211, instead of the 1.442 s cycle). From 2.5 tiles a
        /// jeep reaches him in about 1.0 s after his rocket: fired farther out, his second rocket destroys it; let closer, the jeep's hull
        /// knocks him over first. Counted (SPEC section 10b): the jeep wins 97 of 200 fights (48.5 %) at every start distance from 40 to
        /// 100 px. ChargeToughReloadSpread: the decision sweep's other lever (a drawn reload), 0 in the game (no draw).</summary>
        public static bool ChargeToughNoAimUp = true;
        public static int ChargeToughHoldPx = 30, ChargeToughHoldSpreadPx = 20;
        /// <summary>DV121 (the Tough stands his ground, ZObject.ChargeToughFar): the distance he lets the jeep come is counted as open
        /// ground along its way (a road tile 1/1.689 of its length: the jeep drives 1.689 x as fast there, so he fires farther out and the
        /// race stays the same); false = the plain distance (the decision run only). ChargeToughMeetPx: where the count starts (the hull
        /// meets him at 23 px, its 16 px half length + his 7 px; counted, 200 fights at 100 px on a road: 23 px 106, 22 / 21 px 97 of 200,
        /// open ground 97 of 200).</summary>
        public static bool ChargeToughGround = true;
        public static int ChargeToughMeetPx = 22;
        public static double ChargeToughReload = 1.0, ChargeToughReloadSpread = 0;
        /// <summary>DV121 (fair chances): Toughs that stood their ground against a jeep driving at them (draws), the ticks they held
        /// their rocket, the rockets after which the next came ChargeToughReload s later.</summary>
        public int JeepChargeToughStands { get; internal set; }
        public int JeepChargeToughHeldTicks { get; internal set; }
        public int JeepChargeQuickReloads { get; internal set; }
        /// <summary>DV121: "drives at him now" = it drove at most this long ago (s).</summary>
        public const double ChargeRecentSec = 0.05;
        /// <summary>DV121: the light tank charges as the jeep does. Off: counted (the check's part I), standing it already wins against a
        /// lone Laser, Tough, Sniper and Electrobot 20 of 20, its rockets kill them before it gets there; a charge would only turn the hero
        /// pair from 8 wins into 20 of 20 and lose against 3 Toughs 20 of 20 instead of 14. The counted check switches it on for a count.</summary>
        public static bool ChargeLightTank = false;

        /// <summary>DV121 statistics (checks, the parity row): drives started (strong / close weak robot), chained to the next robot,
        /// drives back, arrivals back at the spot, charges a new order ended, drives stopped (time, reach, way, blocked, held), looks
        /// that found a robot it wants but not the way (the ground, a lane, a unit) or found him under an enemy gun / fort, its own steps
        /// aside dropped for a drive.</summary>
        public int JeepCharges { get; internal set; }
        public int JeepChargesStrong { get; internal set; }
        public int JeepChargesClose { get; internal set; }
        public int JeepChargeChains { get; internal set; }
        public int JeepChargeReturns { get; internal set; }
        public int JeepChargeHome { get; internal set; }
        public int JeepChargeOrdersTaken { get; internal set; }
        public int JeepChargeStopsTime { get; internal set; }
        public int JeepChargeStopsReach { get; internal set; }
        public int JeepChargeStopsWay { get; internal set; }
        public int JeepChargeStopsBlocked { get; internal set; }
        public int JeepChargeStopsHeld { get; internal set; }
        public int JeepChargeRefusedWay { get; internal set; }
        public int JeepChargeRefusedDanger { get; internal set; }
        public int JeepChargeDodgesDropped { get; internal set; }
        /// <summary>DV121: idle fights in which a robot the jeep runs over went first over the robot it was shooting.</summary>
        public int JeepChargeSwitched { get; internal set; }
        /// <summary>DV121 (fair chances): driver hits of a Laser / hero on a vehicle driving at him from close; Tough rockets aimed true at one.</summary>
        public int JeepChargeDriverHits { get; internal set; }
        public int JeepChargeAimedRockets { get; internal set; }

        /// <summary>DV121: holds the rule off (the Zod rows and every other row's probe in the parity report), puts it back on Dispose.</summary>
        public sealed class JeepChargeHold : IDisposable
        {
            readonly bool was;
            internal JeepChargeHold(bool on) { was = JeepChargeDeviation; JeepChargeDeviation = on; }
            public void Dispose() { JeepChargeDeviation = was; }
        }
        public static JeepChargeHold JeepChargeOff() => new JeepChargeHold(false);
        public static JeepChargeHold JeepChargeOn() => new JeepChargeHold(true);
    }
}
