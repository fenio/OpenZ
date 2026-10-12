using System;

namespace OpenZ.Sim
{
    /// <summary>DV41: what a fort's roof defender is doing (the fort's state; the view animates the hatch and the man from it).</summary>
    public enum FortDefenderPhase { Down, Rising, Up, Lowering }

    /// <summary>
    /// Zod's forts have no crew of their own (bfort.cpp); this adds one.
    /// <list type="bullet">
    /// <item><b>Under attack</b> = the fort lost health to an ENEMY shot or blast within the last <see cref="ZWorld.FortDefenderAlarm"/>
    /// s (LastEnemyHit, the same record and the same 4 s window as the "our fort is taking fire" warning, FortAlert.HitWindow).
    /// The attack is over when no enemy hit came for <see cref="ZWorld.FortDefenderCalm"/> s (2x the slowest weapon cycle that can
    /// hurt a fort, the howitzer's 4.86 s: one steady attacker never lets him drop) and he has nobody in range.</item>
    /// <item><b>Which ring</b>: the four flower-shaped sandbag nests of fort_v2 (fort-local metres, measured bag by bag on the live
    /// FortBase mesh: circle through the 11 bag centroids, r 0.95; <see cref="ZWorld.FortDefenderRingLocal"/>), turned into fort px
    /// with the fort model's yaw (fort_back 0, fort_front 180 deg, OpenZSetup.Spawn) and 1 px = 1/8 m. The ring nearest the last
    /// attacker (its centre when that shot or blast landed; the blast point when the attacker was already gone). One hatch per
    /// nest, in its centre (the only free deck there: 0.5 m to the bags; the nests open toward the gun platform's corner blocks).</item>
    /// <item><b>The man</b>: a real grunt ZObject of the fort's team (Zod grunt settings: 1081 HP, 120 px range, 11 HP at 70 % every
    /// 0.5 s, ProcessAttackDamage's hitscan branch, DV23 line of sight from his ring with his fort's tiles exempt like a tower gun),
    /// standing on the ring centre (inside the fort's impassable tiles). Every unit can target him like any robot (passive engage,
    /// attack-move, orders, the CPU's all-out targets); rifles, shells, splash and grenades hurt him; he dies like a robot (Death
    /// event). His sandbag nest takes 3/4 of a blast that bursts outside it (<see cref="ZWorld.FortDefenderNestCover"/>): a tank
    /// shelling the wall on his side reaches his ring with its splash, and without the cover he died in a median 8.6 s, unseen. He
    /// never snipes drivers (a hatch rifle, like DV31's crane driver: a fixture of the fort does not empty tanks at its door). He
    /// takes no orders (SendWaypoints refuses him), is never selectable, never moves (move speed 0: no dodging), never captures
    /// flags or boards anything, is left out of the pathing layer (spacing, stuck guard), of Zod's scuffle nudges, of the unit
    /// count (the 70 cap), of the no-units rule and of the game-end check, and the CPU never counts him as its own unit.</item>
    /// <item><b>Timing</b>: he comes up at once (the tick after the hit) through the hatch of the chosen ring: <see
    /// cref="ZWorld.FortDefenderPop"/> s for the lid and the climb (targetable, not firing yet), then he fights: target = the fort's
    /// last attacker when it is in his range, else the nearest enemy unit in range (ties: object order), re-checked every 0.5 s
    /// (Zod's passive engage interval). When the attack ends he goes down (<see cref="ZWorld.FortDefenderLower"/> s) and is removed
    /// (gone, not dead: his health is kept for the next time). While the attack goes on from another ring's side (that ring nearer
    /// by more than <see cref="ZWorld.FortDefenderSwitchMarginPx"/> px) for <see cref="ZWorld.FortDefenderSwitchHold"/> s and he has
    /// nobody in range, he goes down and comes up there (<see cref="ZWorld.FortDefenderTransit"/> s through the fort). Killed: the
    /// next one may come out <see cref="ZWorld.FortDefenderRespawn"/> s later (FortAlert's 20 s attack-wave calm; at most one free
    /// grunt per 20 s and only while the fort is being hit, about a fort's own grunt rate at 0 flags: a squad of 3 in 72 s).</item>
    /// </list>
    /// Deterministic (multiplayer-ready): sim time, integer px, fixed object order, Zod's rand() stream only for his own hit rolls;
    /// nothing is drawn from it while he does not exist, so with DV41 off (ZWorld.FortDefenderDeviation) the sim is the previous
    /// build bit for bit. Pure sim: no Unity API, no frame time, no view state. The view (FortDefenderView, UnitView.FortDefender)
    /// only reads FortDefenderState / PhaseAt / Ring / Man and the man's ZObject.
    /// </summary>
    public partial class ZObject
    {
        // ================================================================== the man
        /// <summary>DV41: the fort whose roof this grunt defends (-1 = an ordinary unit). Set once, when the fort creates him.</summary>
        public int DefenderFortId { get; private set; } = -1;
        /// <summary>DV41: a fort's roof defender (never selectable, never ordered, not counted as a unit of his team).</summary>
        public bool IsFortDefender => DefenderFortId >= 0;
        double fdScan;
        double fdLx, fdLz, fdPrevLx, fdPrevLz, fdEdgeS, fdWalkMps, fdHoldUntil = -1, fdNextNade = -1, fdNadeRefillAt, fdLastT = -1;
        int fdFrom = -1, fdTo = -1, fdGoal = -1, fdNades;
        ZObject fdLobTarget;
        /// <summary>DV41 walk: where he stands on his fort's deck (fort-local metres, the fort view's frame), and a tick ago (view lerp).</summary>
        public double FortDefenderLocalX => fdLx;
        public double FortDefenderLocalZ => fdLz;
        public double FortDefenderPrevLocalX => fdPrevLx;
        public double FortDefenderPrevLocalZ => fdPrevLz;
        /// <summary>DV41 walk: the deck node he stands on (-1 = between two nodes), the node he walks to, grenades left.</summary>
        public int FortDefenderNode => fdFrom >= 0 && fdFrom == fdTo ? fdFrom : -1;
        public int FortDefenderGoal => fdGoal;
        public int FortDefenderGrenades => fdNades;
        /// <summary>DV41 walk: inside one of the four sandbag nests (within 0.6 m of a ring centre): only there the bags cover him.</summary>
        public bool FortDefenderInNest
        {
            get
            {
                if (!IsFortDefender) return false;
                if (!ZWorld.FortDefenderWalkDeviation) return true;
                for (int i = 0; i < ZWorld.FortDefenderRings; i++)
                {
                    double ex = fdLx - FortDefenderDeck[i * 2], ez = fdLz - FortDefenderDeck[i * 2 + 1];
                    if (ex * ex + ez * ez < 0.36) return true;
                }
                return false;
            }
        }
        /// <summary>DV41 walk statistics (checks): walks started, grenades lobbed, rifle shots fired, shots fired with a blocked line (must stay 0).</summary>
        public int FortDefenderWalks { get; private set; }
        public int FortDefenderLobs { get; private set; }
        public int FortDefenderRifleShots { get; private set; }
        public int FortDefenderBlockedShots { get; private set; }

        // ================================================================== the fort
        ZObject fdMan;
        FortDefenderPhase fdPhase;
        double fdPhaseAt = -100, fdNextSpawn = -100, fdSwitchSince = -1, fdNadesKeptAt;
        int fdRing = -1, fdHealth = -1, fdAttackerId = -1, fdFromX, fdFromY, fdNadesKeep = -1;
        double fdThreatAt = -100, fdThreatScan;
        bool fdHaveFrom, fdSwitching, fdHome;

        /// <summary>DV41: the fort's roof defender (null = nobody up; he exists while Rising, Up and Lowering).</summary>
        public ZObject FortDefenderMan => fdMan;
        /// <summary>DV41: the fort's defender phase and the sim time it began (view: lid, climb, sink).</summary>
        public FortDefenderPhase FortDefenderState => fdPhase;
        public double FortDefenderPhaseAt => fdPhaseAt;
        /// <summary>DV41: the ring (hatch) in use: index into ZWorld.FortDefenderRingLocal (-1 = none yet).</summary>
        public int FortDefenderRing => fdRing;
        /// <summary>DV41: earliest sim time the next defender may come up (death cooldown / ring change).</summary>
        public double FortDefenderNextSpawn => fdNextSpawn;
        /// <summary>DV41 statistics (checks): men sent up, killed, ring changes.</summary>
        public int FortDefenderSpawns { get; private set; }
        public int FortDefenderDeaths { get; private set; }
        public int FortDefenderSwitches { get; private set; }
        /// <summary></summary>
        public int FortDefenderThreats { get; private set; }
        public double FortDefenderThreatAt => fdThreatAt;

        /// <summary>
        /// counted a shell or blast that took health off the fort BODY (ProcessMissileDamage -> LastEnemyHit + NoteFortAttack). In a
        /// real match nobody shells the fort itself: attackers fight the fort's tower guns and its units round the gate and then walk in
        /// (ENTER_FORT), rifles cannot hurt buildings at all, and a missile aimed at a unit next to the fort never reaches the fort's centre
        /// with its splash. Only the FortDefenderShow harness (a heavy tank ordered onto the fort) ever triggered him. Now a fort is also
        /// under attack while an enemy unit is within FortDefenderThreatPx of its centre and attacking something of the fort's team (its
        /// tower guns, its units, the fort), or stands within FortDefenderNearPx of it (at the walls / the gate). Scanned every 0.5 s of
        /// sim time in object order (deterministic, multiplayer-safe); the nearest such enemy sets the side (ring choice) and the
        /// defender's first target unless a real hit on the fort is fresher.</summary>
        void FortDefenderThreatScan(double time)
        {
            if (!ZWorld.FortDefenderThreatDeviation || time < fdThreatScan) return;
            fdThreatScan = time + ZWorld.FortDefenderScan;
            long r2 = (long)ZWorld.FortDefenderThreatPx * ZWorld.FortDefenderThreatPx, n2 = (long)ZWorld.FortDefenderNearPx * ZWorld.FortDefenderNearPx;
            ZObject best = null; long bd = long.MaxValue;
            foreach (var o in World.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner || o.IsFortDefender) continue;
                long dx = o.CenterX - CenterX, dy = o.CenterY - CenterY, d = dx * dx + dy * dy;
                if (d > r2) continue;
                var a = o.AttackObject;
                bool fights = a != null && !a.Destroyed && a.Owner == Owner;
                if (!fights && d > n2) continue;
                if (d < bd) { bd = d; best = o; }
            }
            if (best == null) return;
            fdThreatAt = time; FortDefenderThreats++;
            bool freshHit = LastEnemyHit >= 0 && time - LastEnemyHit <= ZWorld.FortDefenderAlarm;
            if (!freshHit) { fdAttackerId = best.Id; fdFromX = best.CenterX; fdFromY = best.CenterY; fdHaveFrom = true; }
        }

        /// <summary>DV41 (ZWorld.ProcessMissileDamage): an enemy shell or blast took health off this fort; remember where it came from
        /// (the attacker's centre when it still exists, else the blast point) for the ring choice.</summary>
        internal void NoteFortAttack(int attackerId, int hitX, int hitY)
        {
            if (!IsFort) return;
            var a = World.Get(attackerId);
            fdAttackerId = a != null ? attackerId : -1;
            if (a != null && !a.Destroyed) { fdFromX = a.CenterX; fdFromY = a.CenterY; }
            else { fdFromX = hitX; fdFromY = hitY; }
            fdHaveFrom = true;
        }

        /// <summary>DV41: offset (px) of ring `ring`'s centre from this fort type's centre: fort-local metres x 8, turned by the model's
        /// yaw (fort_back 0 deg: px = 8 x, py = -8 z; fort_front 180 deg: px = -8 x, py = 8 z).</summary>
        public static void FortDefenderRingOffset(string fortType, int ring, out int dx, out int dy)
        {
            double lx = ZWorld.FortDefenderRingLocal[ring * 2], lz = ZWorld.FortDefenderRingLocal[ring * 2 + 1];
            int s = fortType == "fort_front" ? -1 : 1;
            dx = s * (int)Math.Round(8.0 * lx); dy = -s * (int)Math.Round(8.0 * lz);
        }

        /// <summary>DV41: ring `ring`'s centre in map px.</summary>
        public void FortDefenderRingCentre(int ring, out int x, out int y)
        {
            FortDefenderRingOffset(Type, ring, out int dx, out int dy);
            x = CenterX + dx; y = CenterY + dy;
        }

        /// <summary>DV41: the ring nearest the last attack (ties: the lower index); -1 = no attack seen.</summary>
        public int FortDefenderRingFor()
        {
            if (!fdHaveFrom) return -1;
            // RTS camera in every day frame; a nest within ~35 deg of the barrel's bearing is taken only when no other is free
            var g = FortGunTarget; bool barrel = g != null && !g.Destroyed;
            int best = -1; long bd = long.MaxValue;
            for (int i = 0; i < ZWorld.FortDefenderRings; i++)
            {
                FortDefenderRingCentre(i, out int x, out int y);
                long d = (long)(x - fdFromX) * (x - fdFromX) + (long)(y - fdFromY) * (y - fdFromY);
                if (barrel)
                {
                    double rx = x - CenterX, ry = y - CenterY, bx = g.CenterX - CenterX, by = g.CenterY - CenterY;
                    double rl = Math.Sqrt(rx * rx + ry * ry), bl = Math.Sqrt(bx * bx + by * by);
                    if (rl > 1 && bl > 1 && (rx * bx + ry * by) / (rl * bl) > 0.819) d += 1L << 40;   // under the barrel: last choice
                }
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>DV41: how much nearer (px) ring `want` is to the last attack than the ring in use (hysteresis for ring changes).</summary>
        double FortDefenderRingGain(int want)
        {
            FortDefenderRingCentre(fdRing, out int ax, out int ay); FortDefenderRingCentre(want, out int bx, out int by);
            return Math.Sqrt((double)(ax - fdFromX) * (ax - fdFromX) + (double)(ay - fdFromY) * (ay - fdFromY))
                 - Math.Sqrt((double)(bx - fdFromX) * (bx - fdFromX) + (double)(by - fdFromY) * (by - fdFromY));
        }

        /// <summary>DV41, ProcessBuilding of a fort, every tick (after the DV7 fort gun): the defender's state machine.</summary>
        internal void FortDefenderStep(double time)
        {
            if (!IsFort) return;
            if (fdMan != null && (fdMan.Destroyed || World.Get(fdMan.Id) != fdMan))
            {   // killed (or removed from outside): the next one after the cooldown, at full health
                bool killed = fdMan.Destroyed;
                fdMan = null; SetFdPhase(FortDefenderPhase.Down, time);
                if (killed) { fdNextSpawn = time + ZWorld.FortDefenderRespawn; fdHealth = -1; fdNadesKeep = -1; FortDefenderDeaths++; }
            }
            bool on = ZWorld.FortDefenderDeviation && Owner != Team.Neutral;
            bool frozen = World.GameOver && ZWorld.EndFreezeDeviation;                               // DV10: nothing new after the game over
            if (on && !frozen) FortDefenderThreatScan(time);
            double lastThreat = Math.Max(LastEnemyHit, fdThreatAt);
            bool attacked = lastThreat >= 0 && time - lastThreat <= ZWorld.FortDefenderAlarm;
            bool calm = time - lastThreat >= ZWorld.FortDefenderCalm;
            switch (fdPhase)
            {
                case FortDefenderPhase.Down:
                    if (on && !frozen && attacked && time >= fdNextSpawn)
                    {
                        int ring = FortDefenderRingFor();
                        if (ring >= 0) FortDefenderSpawn(time, ring);
                    }
                    break;
                case FortDefenderPhase.Rising:
                    if (!on) { Lower(time, false); break; }
                    if (time - fdPhaseAt >= ZWorld.FortDefenderPop) SetFdPhase(FortDefenderPhase.Up, time);
                    break;
                case FortDefenderPhase.Up:
                {
                    if (ZWorld.FortDefenderWalkDeviation)
                    {
                        // nearest hatch on foot, down there. DV41 switched off / the game over: down at once (the hatch nearest to him).
                        bool busyW = fdMan.AttackObject != null || fdMan.fdLobTarget != null;
                        if (!on || frozen) { fdRing = fdMan.FdNearestRing(); Lower(time, false); break; }
                        fdHome = calm && !busyW;
                        if (fdHome && fdMan.FortDefenderNode >= 0 && fdMan.FortDefenderNode < ZWorld.FortDefenderRings) { fdRing = fdMan.FortDefenderNode; Lower(time, false); }
                        break;
                    }
                    bool busy = fdMan.AttackObject != null;
                    if (!on || ((calm || frozen) && !busy)) { Lower(time, false); break; }
                    int want = attacked ? FortDefenderRingFor() : -1;                                    // only while the attack goes on
                    if (want >= 0 && want != fdRing && !busy && FortDefenderRingGain(want) > ZWorld.FortDefenderSwitchMarginPx) { if (fdSwitchSince < 0) fdSwitchSince = time; }
                    else fdSwitchSince = -1;
                    if (fdSwitchSince >= 0 && time - fdSwitchSince >= ZWorld.FortDefenderSwitchHold) Lower(time, true);
                    break;
                }
                case FortDefenderPhase.Lowering:
                    if (time - fdPhaseAt < ZWorld.FortDefenderLower) break;
                    fdHealth = fdMan.Health;                                                           // gone, not dead: he keeps his wounds
                    fdNadesKeep = fdMan.fdNades; fdNadesKeptAt = time;                                 // ... and the grenades he has left
                    fdMan.Disengage(); fdMan.KillAt = time;                                            // DoKillMe without a death (like entering a vehicle)
                    fdMan = null; SetFdPhase(FortDefenderPhase.Down, time);
                    if (fdSwitching) fdNextSpawn = Math.Max(fdNextSpawn, time + ZWorld.FortDefenderTransit);
                    fdSwitching = false;
                    break;
            }
        }

        void SetFdPhase(FortDefenderPhase p, double time) { fdPhase = p; fdPhaseAt = time; fdSwitchSince = -1; }

        void Lower(double time, bool toSwitch)
        {
            fdMan.Disengage();
            fdSwitching = toSwitch;
            if (toSwitch) FortDefenderSwitches++;
            SetFdPhase(FortDefenderPhase.Lowering, time);
        }

        /// <summary>DV41: a grunt of the fort's team on ring `ring`'s centre (Zod grunt settings, move speed 0, no driver sniping),
        /// with the health he went down with last time; the view spawns from the Created event (otherId = the fort).</summary>
        void FortDefenderSpawn(double time, int ring)
        {
            FortDefenderRingCentre(ring, out int x, out int y);
            var m = World.CreateObject("robot", "grunt", x - 8, y - 8, Owner);
            m.DefenderFortId = Id;
            m.fdWalkMps = m.MoveSpeed / 8.0;
            m.MoveSpeed = 0; m.RealMoveSpeed = 0;                                                  // no Zod movement: no pathing, no dodging
            m.CanSnipe = ZWorld.FortDefenderSnipes;
            m.Damage *= ZWorld.CrewRifleMul;
            if (fdHealth > 0) m.Health = Math.Min(m.MaxHealth, fdHealth);
            m.fdScan = time;
            m.fdFrom = m.fdTo = m.fdGoal = ring; m.fdEdgeS = 0; m.fdLastT = time;                  // deck node `ring` = the ring's nest centre
            m.fdLx = m.fdPrevLx = FortDefenderDeck[ring * 2]; m.fdLz = m.fdPrevLz = FortDefenderDeck[ring * 2 + 1];
            int kept = fdNadesKeep < 0 ? ZWorld.FortDefenderNades : fdNadesKeep + (int)((time - fdNadesKeptAt) / ZWorld.FortDefenderNadeRefill);
            m.fdNades = Math.Min(ZWorld.FortDefenderNades, kept); m.fdNadeRefillAt = time + ZWorld.FortDefenderNadeRefill;
            fdMan = m; fdRing = ring; fdSwitching = false; fdHome = false;
            SetFdPhase(FortDefenderPhase.Rising, time);
            FortDefenderSpawns++;
            World.Emit(ZEventType.Created, m, this);
        }

        // ------------------------------------------------------------------ the man's tick (ZObject.ProcessServer)
        /// <summary>DV41: the defender's whole ProcessServer: no waypoints, no pathing, no auto-grab; while his fort says Up he picks a
        /// target every 0.5 s and fires Zod's grunt rifle (ProcessAttackDamage).</summary>
        void FortDefenderManStep(double time)
        {
            var f = World.Get(DefenderFortId);
            if (f == null || f.Destroyed || f.fdMan != this) { Disengage(); KillAt = time; return; }   // orphan: gone
            if (Waypoints.Count > 0) { Waypoints.Clear(); StopMove(); }
            bool up = f.fdPhase == FortDefenderPhase.Up && ZWorld.FortDefenderDeviation && !(World.GameOver && ZWorld.EndFreezeDeviation);
            double dt = fdLastT >= 0 ? Math.Max(0, time - fdLastT) : 0; fdLastT = time;
            fdPrevLx = fdLx; fdPrevLz = fdLz;
            if (!up) { Disengage(); fdLobTarget = null; Dx = Dy = 0; return; }
            if (!ZWorld.FortDefenderWalkDeviation)
            {   // fort_defender_v1 as built: bolted to his ring
                if (AttackObject != null && !FortDefenderValid(AttackObject)) Disengage();
                if (time >= fdScan)
                {
                    fdScan = time + ZWorld.FortDefenderScan;
                    if (AttackObject == null) { var t = FortDefenderPick(f); if (t != null) Engage(t); }
                }
                ProcessAttackDamage(time, false);
                return;
            }
            // clear line, and lobs grenades over the towers when no spot has one (or while he walks there)
            if (fdNades < ZWorld.FortDefenderNades) { if (time >= fdNadeRefillAt) { fdNades++; fdNadeRefillAt = time + ZWorld.FortDefenderNadeRefill; } }
            else fdNadeRefillAt = time + ZWorld.FortDefenderNadeRefill;
            if (fdLobTarget != null && !FdLobbable(fdLobTarget)) fdLobTarget = null;
            if (AttackObject != null && (FortDefenderNode != fdGoal || !FortDefenderValid(AttackObject))) Disengage();
            bool arrived = FdWalk(f, time, dt);
            if (time >= fdScan || arrived) { fdScan = time + ZWorld.FortDefenderScan; FortDefenderPlan(f); }
            if (AttackObject != null && FortDefenderNode == fdGoal && time >= fdHoldUntil)
            {
                if (time >= nextDamageTime)
                {   // a shot leaves now: count it and check its line against the fort (must never be blocked)
                    FortDefenderRifleShots++;
                    FdToLocal(f, AttackObject.CenterX, AttackObject.CenterY, out double tx, out double tz);
                    if (!FortDefenderLineClear(fdLx, fdLz, tx, tz)) FortDefenderBlockedShots++;
                }
                ProcessAttackDamage(time, false);
            }
            else if (fdLobTarget != null) FdLob(time, fdLobTarget);
        }

        /// <summary>An enemy unit this defender can shoot now: Zod's CanAttackObject, his grunt range from the ring (centre to
        /// centre), Zod's engage barrier and DV23's line (his fort's own tiles exempt); walking on: and a clear line past the fort's own
        /// towers and gun block (FortDefenderLineClear) from where he stands.</summary>
        bool FortDefenderValid(ZObject o)
        {
            if (o == null || o == this || !o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner) return false;
            if (!CanAttackObject(o) || !Within(CenterX, CenterY, o.CenterX, o.CenterY, AttackRadius)) return false;
            if (ZWorld.FortDefenderWalkDeviation)
            {
                var f = World.Get(DefenderFortId); if (f == null) return false;
                FdToLocal(f, o.CenterX, o.CenterY, out double tx, out double tz);
                if (!FortDefenderLineClear(fdLx, fdLz, tx, tz)) return false;
            }
            return WithinAttackRadius(o);
        }

        /// <summary>The deck graph (fort-local metres x, z; the fort view's frame, fort_back unturned): nodes 0-3 = the four nest centres
        /// (= ZWorld.FortDefenderRingLocal, where the hatches are), then the walkable deck round the gun platform: the rear corridor
        /// (flag side), the two side corridors between the vents, the front corridor (gate side). Measured on the live FortBase height
        /// edge keeps >= 0.14 m free round his feet through the nest openings and >= 0.3 m in the corridors; never over the bags, the
        /// vents, the gun platform or the flag socket).</summary>
        public static readonly double[] FortDefenderDeck =
        {
            -4.05, -4.2,  4.05, -4.2,  -4.05, 3.1,  4.05, 3.1,                                  // 0-3 nests (rings)
            -3.0, -4.2,  -2.5, -4.9,  -1.2, -5.3,  -3.4, -5.9,                                   // 4-7 rear, left
             3.0, -4.2,   2.5, -4.9,   1.2, -5.3,   3.4, -5.9,                                   // 8-11 rear, right
            -3.95, -3.4, -3.9, -2.4,  -4.3, -1.5,  -4.9, -0.6,  -4.5, 0.1,  -3.9, 2.1,           // 12-17 left side
             4.0, -3.3,   3.95, -2.4,  4.4, -1.4,   4.9, -0.6,   4.5, 0.1,   3.9, 2.1,           // 18-23 right side
            -2.9, 3.1,   -2.4, 3.9,   -1.1, 4.7,   -3.3, 4.9,                                    // 24-27 front, left
             2.9, 3.1,    2.4, 3.9,    1.1, 4.7,    3.3, 4.9,                                    // 28-31 front, right
        };
        static readonly int[] FdEdges =
        {
            0, 4, 4, 5, 5, 6, 5, 7, 6, 10,  1, 8, 8, 9, 9, 10, 9, 11,
            0, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 2,
            1, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22, 23, 23, 3,
            2, 24, 24, 25, 25, 26, 25, 27, 26, 30,  3, 28, 28, 29, 29, 30, 29, 31,
        };
        /// <summary>Nodes he walks through but never stops on to fire (the squeezes through the nests' openings).</summary>
        static readonly bool[] FdPassOnly = BuildPassOnly(4, 8, 12, 17, 18, 23, 24, 28);
        static bool[] BuildPassOnly(params int[] ids) { var b = new bool[FortDefenderDeck.Length / 2]; foreach (var i in ids) b[i] = true; return b; }
        public static int FortDefenderDeckNodes => FortDefenderDeck.Length / 2;
        static double[,] fdDist; static int[,] fdNext;

        /// <summary>All-pairs walk distances + next hops over the deck graph (Floyd-Warshall, fixed order: deterministic).</summary>
        static void FdGraph()
        {
            if (fdDist != null) return;
            int n = FortDefenderDeckNodes; var d = new double[n, n]; var nx = new int[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { d[i, j] = i == j ? 0 : double.MaxValue / 4; nx[i, j] = i == j ? i : -1; }
            for (int e = 0; e < FdEdges.Length; e += 2)
            {
                int a = FdEdges[e], b = FdEdges[e + 1]; double l = FdLen(a, b);
                d[a, b] = d[b, a] = l; nx[a, b] = b; nx[b, a] = a;
            }
            for (int k = 0; k < n; k++) for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
                if (d[i, k] + d[k, j] < d[i, j] - 1e-9) { d[i, j] = d[i, k] + d[k, j]; nx[i, j] = nx[i, k]; }
            fdNext = nx; fdDist = d;
        }
        static double FdLen(int a, int b)
        {
            double ex = FortDefenderDeck[b * 2] - FortDefenderDeck[a * 2], ez = FortDefenderDeck[b * 2 + 1] - FortDefenderDeck[a * 2 + 1];
            return Math.Sqrt(ex * ex + ez * ez);
        }

        /// <summary>The fort's blockers for his rifle (fort-local metres): the four corner towers (pads at FortGunView's TowerPads,
        /// their bodies 2.7 m round the pad centre at his muzzle's height, deck + 1.5 = 7.8 m; the pads' 9.25 m tops and the tower
        /// guns on them stand higher than any line he can fire) and the central gun block (platform 6.9-7.2 m with the turret on it,
        /// its corner lamps; +-3.6 x -4.1..2.95: a line from 7.8 m drops under the platform top within 1.3 m, so it never crosses it).
        /// The walls round the deck are a 0.7-1.1 m parapet he fires over (his muzzle 0.4-0.8 m above it); the flag pole stands on
        /// its socket from 8.3 m up, over his line.</summary>
        static readonly double[] FdTowers = { -6.92, -6.58, 6.92, -6.58, -6.93, 5.34, 6.93, 5.34 };
        public const double FortDefenderTowerR = 2.7, FortDefenderGunHalfX = 3.6, FortDefenderGunZ0 = -4.1, FortDefenderGunZ1 = 2.95, FortDefenderLineStep = 0.25;

        /// <summary>True when his line of fire from (ax, az) to (bx, bz) (fort-local metres) passes no tower and not over the gun
        /// block: sampled every 0.25 m (fixed doubles, deterministic).</summary>
        public static bool FortDefenderLineClear(double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) return true;
            int n = (int)Math.Ceiling(len / FortDefenderLineStep);
            double r2 = FortDefenderTowerR * FortDefenderTowerR;
            for (int k = 1; k <= n; k++)
            {
                double t = Math.Min(1.0, k * FortDefenderLineStep / len), x = ax + dx * t, z = az + dz * t;
                if (Math.Abs(x) > 10.1 || Math.Abs(z) > 9.9) continue;                                     // off the fort
                if (Math.Abs(x) < FortDefenderGunHalfX && z > FortDefenderGunZ0 && z < FortDefenderGunZ1) return false;
                for (int i = 0; i < 8; i += 2) { double ex = x - FdTowers[i], ez = z - FdTowers[i + 1]; if (ex * ex + ez * ez < r2) return false; }
            }
            return true;
        }

        /// <summary>Fort-local metres (x, z) <-> map px: the ring offset's rule (fort_back 0 deg: px = 8 x, py = -8 z; fort_front 180 deg).</summary>
        static void FdToLocal(ZObject fort, int x, int y, out double lx, out double lz)
        {
            int s = fort.Type == "fort_front" ? -1 : 1;
            lx = s * (x - fort.CenterX) / 8.0; lz = -s * (y - fort.CenterY) / 8.0;
        }
        static void FdToPx(ZObject fort, double lx, double lz, out double x, out double y)
        {
            int s = fort.Type == "fort_front" ? -1 : 1;
            x = fort.CenterX + s * 8.0 * lx; y = fort.CenterY - s * 8.0 * lz;
        }

        /// <summary>Walk distance (m) from where he is to deck node j (mid-edge: the shorter way round).</summary>
        double FdWalkTo(int j)
        {
            FdGraph();
            if (fdFrom == fdTo) return fdDist[fdFrom, j];
            double l = FdLen(fdFrom, fdTo);
            return Math.Min(fdEdgeS + fdDist[fdFrom, j], l - fdEdgeS + fdDist[fdTo, j]);
        }

        /// <summary>The nest (ring) nearest to him on foot (ties: the lower index).</summary>
        internal int FdNearestRing()
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < ZWorld.FortDefenderRings; i++) { double d = FdWalkTo(i); if (d < bd - 1e-9) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Could he fire at o from deck node s: grunt range (centre to centre), a clear line past the towers and the gun block,
        /// Zod's engage barrier and DV23's line from that spot (his fort's own tiles exempt).</summary>
        bool FdShotFrom(ZObject f, int s, ZObject o, double tx, double tz)
        {
            double sx = FortDefenderDeck[s * 2], sz = FortDefenderDeck[s * 2 + 1];
            FdToPx(f, sx, sz, out double pxd, out double pyd);
            int px = (int)Math.Floor(pxd), py = (int)Math.Floor(pyd);
            if (!Within(px, py, o.CenterX, o.CenterY, AttackRadius)) return false;
            if (!FortDefenderLineClear(sx, sz, tx, tz)) return false;
            if (!o.IsDestroyableImpass && World.EngageBarrierBetween(px, py, o.CenterX, o.CenterY)) return false;
            return !LosActive || World.Sight.ClearFrom(px, py, DefenderFortId, o);
        }

        /// <summary>The firing spot for o nearest to him on foot (-1 = none); cost = the walk (m).</summary>
        int FdBestStand(ZObject f, ZObject o, double bound, out double cost)
        {
            FdToLocal(f, o.CenterX, o.CenterY, out double tx, out double tz);
            int best = -1; cost = bound;
            for (int s = 0; s < FortDefenderDeckNodes; s++)
            {
                if (FdPassOnly[s]) continue;
                double c = FdWalkTo(s);
                if (c >= cost - 1e-9 || !FdShotFrom(f, s, o, tx, tz)) continue;
                cost = c; best = s;
            }
            return best;
        }

        /// <summary>An enemy unit near enough to his fort to matter (his throw range from the farthest deck spot, 7.7 m = 62 px off the
        /// centre).</summary>
        bool FdEligible(ZObject f, ZObject o) =>
            o != null && o != this && o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != Owner && CanAttackObject(o)
            && Within(f.CenterX, f.CenterY, o.CenterX, o.CenterY, FdLobRange + 64);

        /// <summary>His throw range from the roof: his grunt range (Zod's grenade throw) + ZWorld.FortDefenderLobBonusPx for the height of
        /// the deck (6.3 m up), so the threat radius that calls him up (FortDefenderThreatPx) is within his reach from its side.</summary>
        int FdLobRange => AttackRadius + (ZWorld.FortDefenderIdleDeviation ? ZWorld.FortDefenderLobBonusPx : 0);

        /// <summary>A grenade reaches o from where he stands: his throw range and no line needed: it arcs over the towers.</summary>
        bool FdLobbable(ZObject o) =>
            o != null && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != Owner && o.IsUnit && CanAttackObject(o)
            && Within(CenterX, CenterY, o.CenterX, o.CenterY, FdLobRange);

        /// <summary>The deck spot (not a squeeze) nearest to o in map px (ties: the lower index): where he waits for o.</summary>
        int FdNodeNearest(ZObject f, ZObject o)
        {
            int best = FortDefenderNode >= 0 ? FortDefenderNode : fdFrom; long bd = long.MaxValue;
            for (int k = 0; k < FortDefenderDeckNodes; k++)
            {
                if (FdPassOnly[k]) continue;
                FdToPx(f, FortDefenderDeck[k * 2], FortDefenderDeck[k * 2 + 1], out double kx, out double ky);
                long dx = (long)Math.Floor(kx) - o.CenterX, dy = (long)Math.Floor(ky) - o.CenterY, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        /// <summary>Every 0.5 s (and on arriving): pick the target and the spot. The fort's last attacker when any spot has a line to it,
        /// else the enemy whose firing spot is the shortest walk (ties: the nearer, then object order); no spot for anyone: he stays and
        /// lobs at the attacker / the nearest enemy in throw range; on the way to a spot he lobs at his target (or the nearest); nobody for
        /// either and his fort calm (fdHome): he walks back to the nearest hatch.</summary>
        void FortDefenderPlan(ZObject f)
        {
            fdLobTarget = null;
            if (AttackObject != null && FortDefenderNode == fdGoal) return;                                // shooting from his spot: keep it
            ZObject best = null; int bestS = -1; double bestC = double.MaxValue; long bestD = long.MaxValue;
            var a = f.fdAttackerId >= 0 ? World.Get(f.fdAttackerId) : null;
            if (FdEligible(f, a)) { int s = FdBestStand(f, a, double.MaxValue, out double c); if (s >= 0) { best = a; bestS = s; bestC = c; } }
            if (best == null)
                foreach (var o in World.Objects)
                {
                    if (!FdEligible(f, o)) continue;
                    int s = FdBestStand(f, o, bestC + 1e-6, out double c); if (s < 0) continue;
                    long d = (long)(o.CenterX - CenterX) * (o.CenterX - CenterX) + (long)(o.CenterY - CenterY) * (o.CenterY - CenterY);
                    if (c < bestC - 1e-9 || d < bestD) { best = o; bestS = s; bestC = c; bestD = d; }
                }
            if (best != null)
            {
                fdGoal = bestS;
                if (FortDefenderNode == bestS) { Engage(best); return; }
                fdLobTarget = FdLobbable(best) ? best : FdNearestLobbable(f);                              // on the way there
                return;
            }
            Disengage();
            fdLobTarget = FdNearestLobbable(f);
            if (fdLobTarget != null && fdNades <= 0 && ZWorld.FortDefenderIdleDeviation)
            {   // out of grenades and no spot with a line: he steps along the deck toward his target (the node nearest to it), so a
                // line may open as it moves, and he is there for the next grenade (never standing still behind a tower)
                fdGoal = FdNodeNearest(f, fdLobTarget);
                return;
            }
            if (fdLobTarget == null)
            {   // nobody in throw range from here: the attacker (else the nearest enemy near the fort) within throw range of the
                // nearest spot on foot: he walks there to lob (the attack over and nobody near: back to a hatch)
                var a2 = f.fdAttackerId >= 0 ? World.Get(f.fdAttackerId) : null;
                ZObject lt = FdEligible(f, a2) ? a2 : null; long ld = long.MaxValue;
                if (lt == null)
                    foreach (var o in World.Objects)
                    {
                        if (!FdEligible(f, o)) continue;
                        long d = (long)(o.CenterX - CenterX) * (o.CenterX - CenterX) + (long)(o.CenterY - CenterY) * (o.CenterY - CenterY);
                        if (d < ld) { ld = d; lt = o; }
                    }
                int ls = -1; double lc = double.MaxValue;
                if (lt != null)
                    for (int k = 0; k < FortDefenderDeckNodes; k++)
                    {
                        if (FdPassOnly[k]) continue;
                        double c = FdWalkTo(k); if (c >= lc - 1e-9) continue;
                        FdToPx(f, FortDefenderDeck[k * 2], FortDefenderDeck[k * 2 + 1], out double kx, out double ky);
                        if (Within((int)Math.Floor(kx), (int)Math.Floor(ky), lt.CenterX, lt.CenterY, FdLobRange)) { lc = c; ls = k; }
                    }
                if (ls >= 0) { fdGoal = ls; return; }
                if (lt != null && ZWorld.FortDefenderIdleDeviation) { fdGoal = FdNodeNearest(f, lt); return; }  // out of reach: to the threatened side
                if (f.fdHome) { fdGoal = FdNearestRing(); return; }                                              // the attack is over: back to a hatch
            }
            if (FortDefenderNode < 0) fdGoal = fdEdgeS < FdLen(fdFrom, fdTo) * 0.5 && !FdPassOnly[fdFrom] ? fdFrom : fdTo;   // mid-edge: the nearer end
            else if (FdPassOnly[FortDefenderNode]) fdGoal = FdNearestRing();
            else fdGoal = FortDefenderNode;
        }

        ZObject FdNearestLobbable(ZObject f)
        {
            var a = f.fdAttackerId >= 0 ? World.Get(f.fdAttackerId) : null;
            if (FdLobbable(a)) return a;
            ZObject best = null; long bd = long.MaxValue;
            foreach (var o in World.Objects)
            {
                if (!FdLobbable(o)) continue;
                long d = (long)(o.CenterX - CenterX) * (o.CenterX - CenterX) + (long)(o.CenterY - CenterY) * (o.CenterY - CenterY);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        /// <summary>One tick of walking along the deck graph toward fdGoal at grunt speed (turns back mid-edge when the goal is nearer
        /// that way; holds still while a grenade leaves his hand). Moves his map px with him (XOver / YOver carry, Dx / Dy = his walk
        /// velocity in px/s for the view's gait and Zod's target lead). True = he arrived on a node this tick.</summary>
        bool FdWalk(ZObject f, double time, double dt)
        {
            FdGraph();
            if (fdGoal < 0) fdGoal = FortDefenderNode >= 0 ? FortDefenderNode : fdFrom;
            double vx = 0, vz = 0; bool arrived = false;
            if (time >= fdHoldUntil && !(fdFrom == fdTo && fdFrom == fdGoal))
            {
                if (fdFrom == fdTo) { fdTo = fdNext[fdFrom, fdGoal]; fdEdgeS = 0; FortDefenderWalks++; }
                else
                {
                    double l0 = FdLen(fdFrom, fdTo);
                    if (fdEdgeS + fdDist[fdFrom, fdGoal] < l0 - fdEdgeS + fdDist[fdTo, fdGoal] - 1e-9) { int k = fdFrom; fdFrom = fdTo; fdTo = k; fdEdgeS = l0 - fdEdgeS; }
                }
                double step = fdWalkMps * dt;
                while (true)
                {
                    double l = FdLen(fdFrom, fdTo), left = l - fdEdgeS;
                    if (step < left) { fdEdgeS += step; break; }
                    step -= left; fdFrom = fdTo; fdEdgeS = 0;
                    if (fdFrom == fdGoal) { arrived = true; break; }
                    fdTo = fdNext[fdFrom, fdGoal];
                }
                if (fdFrom != fdTo)
                {
                    double l = FdLen(fdFrom, fdTo);
                    vx = (FortDefenderDeck[fdTo * 2] - FortDefenderDeck[fdFrom * 2]) / l * fdWalkMps;
                    vz = (FortDefenderDeck[fdTo * 2 + 1] - FortDefenderDeck[fdFrom * 2 + 1]) / l * fdWalkMps;
                }
            }
            if (fdFrom == fdTo) { fdLx = FortDefenderDeck[fdFrom * 2]; fdLz = FortDefenderDeck[fdFrom * 2 + 1]; }
            else
            {
                double k = fdEdgeS / FdLen(fdFrom, fdTo);
                fdLx = FortDefenderDeck[fdFrom * 2] + (FortDefenderDeck[fdTo * 2] - FortDefenderDeck[fdFrom * 2]) * k;
                fdLz = FortDefenderDeck[fdFrom * 2 + 1] + (FortDefenderDeck[fdTo * 2 + 1] - FortDefenderDeck[fdFrom * 2 + 1]) * k;
            }
            FdToPx(f, fdLx, fdLz, out double ex, out double ey);
            int cx = (int)Math.Floor(ex), cy = (int)Math.Floor(ey);
            SetCords(cx - (WidthPix >> 1), cy - (HeightPix >> 1)); XOver = ex - cx; YOver = ey - cy;
            int s = f.Type == "fort_front" ? -1 : 1;
            Dx = (float)(s * 8.0 * vx); Dy = (float)(-s * 8.0 * vz);
            return arrived;
        }

        /// <summary>A grenade lobbed at t: Zod's grenade (grenade_damage x MAX_UNIT_HEALTH, its radius, speed and cooldown, the lead and
        /// +-24 px scatter of ProcessAttackDamage's grenade branch), from his own supply (3, +1 every 10 s), bypassing DV23's LosHoldShot
        /// (a lob needs no line: DV40, a grunt may grenade anyone); he stands still for the throw (1.1 s, the view's throw clip).</summary>
        void FdLob(double time, ZObject t)
        {
            if (fdNades <= 0 || time < fdNextNade || time < nextDamageTime || !FdLobbable(t)) return;
            if (!EstimateMissileTarget(t, MissileSpeed, out int tx, out int ty)) { tx = t.CenterX; ty = t.CenterY; }
            tx += World.Rng.Next(48) - 24; ty += World.Rng.Next(48) - 24;
            var m = new ZMissile { team = Owner, attackerId = Id, targetId = t.Id, playerGiven = false, grenade = true };
            m.damage = (int)(World.S.grenade_damage * ZConst.MAX_UNIT_HEALTH); m.radius = World.S.grenade_damage_radius;
            m.x = tx; m.y = ty; m.CalcExplodeTime(CenterX, CenterY, World.S.grenade_missile_speed, time);
            fdNades--; FortDefenderLobs++;
            fdNextNade = nextDamageTime = time + World.S.grenade_attack_speed;
            fdHoldUntil = time + ZWorld.FortDefenderThrowHold;
            World.AddMissile(m);
            World.Emit(ZEventType.MissileFired, this, t, tx, ty, (float)(m.explodeTime - time), m.radius, true);
            t.DodgeMissile(tx, ty, m.explodeTime - time);
        }

        /// <summary>The fort's last attacker when in range, else the nearest enemy unit in range (ties: object order).</summary>
        ZObject FortDefenderPick(ZObject fort)
        {
            var a = fort.fdAttackerId >= 0 ? World.Get(fort.fdAttackerId) : null;
            if (FortDefenderValid(a)) return a;
            ZObject best = null; long bd = long.MaxValue;
            foreach (var o in World.Objects)
            {
                if (!FortDefenderValid(o)) continue;
                long d = (long)(o.CenterX - CenterX) * (o.CenterX - CenterX) + (long)(o.CenterY - CenterY) * (o.CenterY - CenterY);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>fort_defender_v1 DV41: a fort under attack sends a grunt up through a roof hatch into the sandbag ring on the attack's
        /// side (ZObject.FortDefender.cs). OFF = Zod exact (forts have no crew). Static like GrenadeRuleDeviation; ZGame sets it at
        /// every start (PlayerPrefs oz_fort_defender, oz_zod_exact); the parity report's Zod rows run with it off.</summary>
        public static bool FortDefenderDeviation = true;
        /// <summary>
        /// He walks the roof deck (ZObject.FortDefenderDeck) to a spot with a clear line past the
        /// towers and the gun, fires only along such a line, and lobs grenades when no spot has one. Off = the ring-bolted v1.</summary>
        public static bool FortDefenderWalkDeviation = true;
        /// <summary>
        /// something of the fort's team, or within FortDefenderNearPx) counts as an attack too, not only a shell on the fort body (which
        /// </summary>
        public static bool FortDefenderThreatDeviation = true;
        /// <summary>The defender is never idle while an enemy is near his fort: his grenades carry FortDefenderLobBonusPx farther from
        /// the 6.3 m deck (so every enemy within the threat radius that called him up is within his throw from its side), he walks to
        /// the deck spot nearest an enemy he cannot reach yet, and with no grenade left and no clear line he steps toward his target
        /// </summary>
        public static bool FortDefenderIdleDeviation = true;
        /// <summary>The extra throw range from the roof (px; 64 px = 8 m: 120 + 64 = 184 px from his spot, and his spots stand up to
        /// 62 px off the fort centre on the side he walks to, so 246 px of the 250 px threat radius).</summary>
        public const int FortDefenderLobBonusPx = 64;
        /// <summary>250 px = Zod's own "fort in danger" radius (zplayer.cpp:1086-1096, the FORT music level; 31 m); 160 px = 20 m, the
        /// fort's walls and gate apron.</summary>
        public const int FortDefenderThreatPx = 250, FortDefenderNearPx = 160;
        /// <summary>DV41 grenades: his supply when he comes up fresh, one back every FortDefenderNadeRefill s of sim time (at most 3).</summary>
        public const int FortDefenderNades = 3;
        public const double FortDefenderNadeRefill = 10.0;
        /// <summary>DV41 grenades: he stands still this long for a throw (the view's 1.1 s grenade_throw, release at 0.56 s).</summary>
        public const double FortDefenderThrowHold = 1.1;
        /// <summary>DV41: an enemy hit on the fort within this many s = under attack (FortAlert.HitWindow, the fort warning).</summary>
        public const double FortDefenderAlarm = 4.0;
        /// <summary>DV41: no enemy hit for this many s (and nobody in his range) = the attack is over: he goes down.</summary>
        public const double FortDefenderCalm = 10.0;
        /// <summary>DV41: lid (0.35 s) + climb (0.85 s) before he can fire; the view animates exactly this.</summary>
        public const double FortDefenderPop = 1.2;
        /// <summary>DV41: the climb down + the lid; he is removed at its end.</summary>
        public const double FortDefenderLower = 1.0;
        /// <summary>DV41: after a defender is killed, seconds before the next one may come up.</summary>
        public const double FortDefenderRespawn = 20.0;
        /// <summary>DV41: a ring change: seconds inside the fort between going down one hatch and coming up another.</summary>
        public const double FortDefenderTransit = 2.0;
        /// <summary>DV41: the attack must come from another ring's side this long (with nobody in his range) before he changes ring.</summary>
        public const double FortDefenderSwitchHold = 4.0;
        /// <summary>DV41: a ring change needs the new ring nearer the attack by more than this (px, 3 m): no swaps between the two rings
        /// of one side when shells scatter along the wall between them.</summary>
        public const int FortDefenderSwitchMarginPx = 24;
        /// <summary>DV41: target check interval (Zod's CheckPassiveEngage, zobject.cpp:1603).</summary>
        public const double FortDefenderScan = 0.5;
        /// <summary>DV41: a blast whose centre lies outside his sandbag nest (farther than the bags' outer edge, 1.38 m = 11 px, from
        /// him) hurts him by 1 / FortDefenderNestCover of Zod's splash (ZWorld.ProcessMissileDamage); a shell into the nest, rifles,
        /// lasers and flames hit him in full. Measured (tools/FdTests assault): without it a heavy tank shelling the fort wall on his
        /// side (its 50 px splash reaches the rings, 48-63 px inside the walls) killed every defender in a median 8.6 s, before he
        /// had fired a shot.</summary>
        public const int FortDefenderNestPx = 11, FortDefenderNestCover = 4;
        /// <summary>DV41: the defender's rifle never snipes drivers (hatch rifle, like DV31's crane driver).</summary>
        public const bool FortDefenderSnipes = false;
        /// <summary>DV41: the four flower-shaped sandbag nests on the fort_v2 roof, fort-local metres (x, z) pairs, index = ring:
        /// 0 (-x, -z), 1 (+x, -z), 2 (-x, +z), 3 (+x, +z); -z = the rear block with the flag, +z = the gate side. Measured on the live
        /// centres within 0.3 cm of these; deck under the centre 6.29-6.36 m).</summary>
        public static readonly double[] FortDefenderRingLocal = { -4.05, -4.2, 4.05, -4.2, -4.05, 3.1, 4.05, 3.1 };
        public const int FortDefenderRings = 4;
    }
}
