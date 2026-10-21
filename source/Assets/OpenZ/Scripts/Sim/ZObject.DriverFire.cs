using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// <para>Zod: the driver of a light, medium or heavy tank is only a picture over the open lid (zvehicle.cpp:268-330); the lid
    /// opens 4 times in 5 when the tank takes a new target that can hit drivers and closes 0 to 1.4 s after the tank lets go of it
    /// (zvehicle.cpp:198-240, zobject.cpp:1574-1588). The APC fires its crew's weapon from inside and never shows anybody
    /// (vapc.cpp:266-342). Release 1.0 of OpenZ: the tank driver aims and "fires" as a picture only (BoardingView), the lone APC
    /// driver fires a grunt squad's rifle out of the side ports (DV15, ZWorld.CrewRifleMul).</para>
    /// <para>DV87, three parts. Nothing here changes WHEN a tank driver comes up or goes back in.</para>
    /// <list type="bullet">
    /// <item><b>A. Tanks and the mech</b> (light / medium / heavy): while the lid is open and the crew is not resting (DV75) and not
    /// a veteran crew (DV12: never shown), the driver fires the weapon of the robot type that drives the vehicle
    /// (<see cref="DriverType"/>): that type's own row of the rule table: damage, hit chance, cycle, range, and for the Tough his
    /// rocket (missile speed with DV52, splash, Zod's lead and scatter, the target dodges), for the Electrobot his bolt (no roll, the
    /// stun of DV45). One robot's worth, as if he stood at the vehicle's centre on foot; no driver sniping from a hatch (like the
    /// crane driver). The default driver, a grunt, fires the crane driver's rifle (DV31): the grunt's row with
    /// <see cref="ZWorld.CrewRifleMul"/> x its damage. Target: the tank's own target when the driver's weapon reaches it with a
    /// clear line (Zod's barrier test + DV23), else the enemy unit nearest to the vehicle in his range (looked for every 0.5 s);
    /// units only, never buildings or map items. His first shot waits <see cref="ZWorld.CraneGunPopSeconds"/> after the lid opened
    /// (the lid and his rise on screen); his weapon keeps its own cycle across lid closings.</item>
    /// <item><b>B. Bridges and doors</b>: on a spot kit b1 protects (ZWorld.SnipeSafeSpot: a standing bridge and 88 px before each
    /// end, the exit lane of a fort or factory) the lid of a tank stays shut and shuts when it is open (looked at every tick of
    /// an open lid, after the tick's move and before the driver's shot: no driver ever fires from such a spot), a crew does not
    /// rest there, and the APC driver does not come up. Zod's lid roll is drawn as before. The crane (its driver can never be
    /// shot out) keeps DV31 everywhere.</item>
    /// <item><b>C. Troop carrier</b>: an owned APC with its one driver and nobody else aboard fires as in 1.0 (its crew weapon, the
    /// lone driver's with CrewRifleMul: for the default grunt exactly the crane driver's numbers; engage rules unchanged), but the
    /// driver now comes up through the roof hatch to do it: he is up from the moment the APC has a target to fire at until
    /// <see cref="ZWorld.CraneGunLinger"/> after the APC last had one, and the first shot of a fight waits
    /// <see cref="ZWorld.CraneGunPopSeconds"/> for him. With passengers aboard, for a veteran crew and on a bridge / in a door
    /// lane he stays inside and the APC fires out of its side ports as before. He cannot be shot out (the APC is not on Zod's
    /// list, unchanged).</item>
    /// </list>
    /// View: <see cref="ZEventType.DriverShot"/> per shot of a tank driver (BoardingView.DriverFire.cs draws his weapon and plays
    /// its sound); a Tough driver's rocket is a normal missile with ZMissile.driver set (its blast is drawn as a bazooka's). The
    /// APC driver's shots stay the APC's own Hitscan / MissileFired events.
    /// Deterministic: sim time and the world's Zod rand() stream only (one hit roll per direct shot, Zod's two scatter draws per
    /// rocket), no frame time, no view state. Switch: ZWorld.DriverFireDeviation (the game turns it on at every start, off in
    /// 'Zod exact'; headless default off). A Play session leaves the switch on in the editor: the parity report (OpenZParity.Run
    /// and CollectDeviations) turns it off for its own rows and puts it back, so every earlier row runs without it and draws
    /// nothing extra; the DV87 row's probe turns it on itself.
    /// </summary>
    public partial class ZObject
    {
        ZObject dfTarget; double dfNextScan, dfNextShot, dfLastShot = -100, dfUpSince = -1, dfUpSeen = -100, dfUpUntil = -1, dfSpotAt = -1; bool dfSpot;

        /// <summary>DV87: the unit the hatch driver of this tank is shooting at (null = none). View: he aims at it.</summary>
        public ZObject DriverFireTarget => dfTarget != null && !dfTarget.Destroyed && dfTarget.KillAt < 0 ? dfTarget : null;
        /// <summary>DV87: sim time of the hatch driver's last shot (-100 = never).</summary>
        public double DriverFireLastShot => dfLastShot;
        /// <summary>DV87 counters (checks): shots the hatch driver of this tank fired, and those that hit (rockets count as hits).</summary>
        public int DriverShots { get; private set; }
        public int DriverHits { get; private set; }

        /// <summary>DV87 part A: the driver of this light / medium / heavy tank is up in the open hatch and fires (the lid is Zod's
        /// own; a resting crew, a veteran crew and an empty vehicle never fire).</summary>
        public bool DriverFireUp => ZWorld.DriverFireDeviation && Kind == ZKind.Vehicle && HasLid && LidOpen && !Resting && !Destroyed &&
                                    Owner != Team.Neutral && Drivers.Count > 0 && !VeteranActive;

        /// <summary>DV87 part C: the lone driver of this APC is up in the roof hatch (a target now, or one within the last
        /// CraneGunLinger s). View only reads it.</summary>
        public bool ApcDriverUp => dfUpSince >= 0 && IsApc && !Destroyed && Owner != Team.Neutral && Drivers.Count == 1 && Passengers.Count == 0;

        /// <summary>DV87: the robot type whose weapon the hatch driver fires: the type that drives the vehicle (a vehicle that starts
        /// crewed or leaves a factory is driven by a grunt, zvehicle.cpp:155-168).</summary>
        public string DriverGunType => World == null || string.IsNullOrEmpty(DriverType) || World.S.Unit(DriverType) == null ? "grunt" : DriverType;
        /// <summary>DV87: HP one hit of the hatch driver's weapon removes: his type's attack_damage; the grunt's rifle x CrewRifleMul
        /// (the crane driver's rifle, DV31).</summary>
        public int DriverGunDamage { get { var u = DriverGunRow; return u == null ? 0 : DriverGunType == "grunt" ? u.Damage * ZWorld.CrewRifleMul : u.Damage; } }
        /// <summary>DV87: seconds from one shot of the hatch driver to the next: his type's attack_speed (the Electrobot: his bolt
        /// to bolt cycle, ZElectro.CycleTicks).</summary>
        public double DriverGunCycle { get { var u = DriverGunRow; return u == null ? 0 : DriverGunType == ZElectro.Type ? ZElectro.Seconds(ZElectro.CycleTicks) : u.attack_speed; } }
        /// <summary>DV87: the hatch driver's range in px (his type's attack_radius, measured from the vehicle's centre).</summary>
        public int DriverGunRadius { get { var u = DriverGunRow; return u == null ? 0 : u.attack_radius; } }
        ZUnitSettings DriverGunRow => World == null ? null : World.S.Unit(DriverGunType);

        // ------------------------------------------------------------------ part B: no driver comes up on a bridge / in a door lane
        /// <summary>DV87 part B: this tank or APC stands on a spot where no driver comes up through a hatch: kit b1's own position
        /// test (ZWorld.SnipeSafeSpot, with its switch). fresh = asked now: the lid roll, every tick of an open lid
        /// (DriverFireStep), the APC driver in a fight (so only vehicles with somebody up pay for it). Without it the answer of
        /// the last ZWorld.HatchSpotSeconds is used (the rest check of every crewed tank, which stands still).
        /// The crane is not asked: its driver cannot be shot out and DV31 stays as it is.</summary>
        internal bool HatchShutSpot(double time, bool fresh = false)
        {
            if (!ZWorld.DriverFireDeviation || World == null || Kind != ZKind.Vehicle || !(HasLid || IsApc)) return false;
            if (fresh || time >= dfSpotAt) { dfSpotAt = time + ZWorld.HatchSpotSeconds; dfSpot = World.SnipeSafeSpot(this); }
            return dfSpot;
        }

        /// <summary>DV87 part B: the open lid of this tank shuts at once (it stands on a bridge / in a door lane).</summary>
        void HatchShutNow() { LidOpen = false; doCloseLid = false; if (ZWorld.HatchesHeld < int.MaxValue) ZWorld.HatchesHeld++; }

        // ------------------------------------------------------------------ part A: light / medium / heavy
        /// <summary>End of ProcessServer and of the stun hold, every tick, right after the crane's DV31 step (so after the tick's
        /// move and the tank's own shot).</summary>
        void DriverFireStep(double time)
        {
            if (Kind != ZKind.Vehicle) return;
            if (IsApc) { ApcDriverStep(time); return; }
            if (!HasLid) return;
            if (LidOpen && HatchShutSpot(time, true)) HatchShutNow();                                // part B: an open lid shuts in the tick the tank rolls onto a bridge / into a door lane: asked every tick while it is open, after this tick's move and before his shot
            if (!DriverFireUp) { dfTarget = null; dfUpSince = -1; return; }
            if (dfUpSince < 0 || time - dfUpSeen > 0.1)                                              // he comes up (again): the lid and his rise first
            { dfUpSince = time; dfNextShot = Math.Max(dfNextShot, time + ZWorld.CraneGunPopSeconds); }
            dfUpSeen = time;
            if (time < dfNextShot) return;                                                            // the same test as a robot's own cooldown (zobject.cpp:2113) and the crane driver's
            var u = DriverGunRow; string type = DriverGunType; int damage = DriverGunDamage; double cycle = DriverGunCycle;
            if (u == null || damage <= 0 || cycle <= 0) return;
            var t = AttackObject;                                                                    // the tank's own target first
            if (!DriverFireValid(t, u.attack_radius))
            {
                t = dfTarget;                                                                        // else the one he picked before
                if (!DriverFireValid(t, u.attack_radius))
                {
                    t = null;
                    if (time >= dfNextScan) { dfNextScan = time + 0.5; t = DriverFireNearest(u.attack_radius); }   // Zod's passive check interval (zobject.cpp:1603)
                }
            }
            dfTarget = t;
            if (t == null) return;
            dfNextShot = time + cycle; dfLastShot = time;
            DriverShots++; if (ZWorld.DriverShotsFired < int.MaxValue) ZWorld.DriverShotsFired++;
            if (type == "tough") { DriverRocket(time, t, u, damage); return; }
            if (type == ZElectro.Type) { DriverBolt(time, t, damage); return; }
            if (type == "pyro") GroundFireShot(t);                                                    // DV117: a hatch Pyro's shot lights the ground at its target, hit or miss (Sim/ZWorld.GroundFire.cs)
            if (CoverMiss(t, World.Rng.Roll(), u.attack_damage_chance, type == "pyro")) { World.Emit(ZEventType.DriverShot, this, t, t.CenterX, t.CenterY, 0, 0, false); return; }   // miss (Zod's hit roll, zobject.cpp:2187; DV122: a kneeling / lying sniper is a smaller target, Sim/ZObject.ProneTarget.cs)
            DriverHits++;
            int hp0 = t.Health;
            t.DamageHealth(damage); t.LastEnemyHit = time;
            if (type == "pyro") t.LastFireHit = time;
            World.Emit(ZEventType.DriverShot, this, t, t.CenterX, t.CenterY, 0, 0, true);
            DriverHitBooked(t, hp0);
        }

        /// <summary>An enemy unit the hatch driver can hit from the vehicle: in his range (centre to centre, like Zod's
        /// points_within_distance), no Zod barrier in between, a clear line (DV23).</summary>
        bool DriverFireValid(ZObject o, int radius)
        {
            if (o == null || o == this || !o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner) return false;
            if (!Within(CenterX, CenterY, o.CenterX, o.CenterY, radius)) return false;
            if (!o.ShuttleAirborne && World.EngageBarrierBetween(CenterX, CenterY, o.CenterX, o.CenterY)) return false;   // DV118: no ground tile stops a shot at the flying shuttle
            return HasLineOfSight(o);
        }

        /// <summary>The enemy unit nearest to the vehicle that the hatch driver can hit (the first in object order when two are
        /// equally near).</summary>
        ZObject DriverFireNearest(int radius)
        {
            ZObject best = null; long bd = long.MaxValue;
            foreach (var o in World.Objects)
            {
                if (o.Owner == Owner || o.Owner == Team.Neutral || !o.IsUnit) continue;
                long dx = o.CenterX - CenterX, dy = o.CenterY - CenterY, d = dx * dx + dy * dy;
                if (d >= bd || !DriverFireValid(o, radius)) continue;
                bd = d; best = o;
            }
            return best;
        }

        /// <summary>What Zod's server pass does after a hit of this tick (ZWorld.Tick, side effects): the death relay with this
        /// vehicle as the attacker (the kill goes to its crew, DV12) and the squad's help call (DV53).</summary>
        void DriverHitBooked(ZObject t, int hp0)
        {
            World.UpdateObjectHealth(t, Id);
            if (t.Health < hp0) World.HelpCall(t, this);
            if (t.Destroyed && dfTarget == t) dfTarget = null;
        }

        /// <summary>The Tough's rocket from the hatch: Zod's missile (zobject.cpp:2140-2160): led, scattered 32 px, his splash, his
        /// missile speed (DV52); the target gets Zod's dodge call. ZMissile.driver marks it for the view.</summary>
        void DriverRocket(double time, ZObject t, ZUnitSettings u, int damage)
        {
            int speed = ZWorld.RocketSpeed("tough", u.attack_missile_speed);
            if (!EstimateMissileTarget(t, speed, out int tx, out int ty)) { tx = t.CenterX; ty = t.CenterY; }
            tx += World.Rng.Next(32) - 16; ty += World.Rng.Next(32) - 16;
            var m = new ZMissile { team = Owner, attackerId = Id, targetId = t.Id, damage = damage, radius = u.attack_damage_radius, x = tx, y = ty, driver = true };
            m.CalcExplodeTime(CenterX, CenterY, speed, time);
            World.AddMissile(m);
            DriverHits++;
            World.Emit(ZEventType.DriverShot, this, t, tx, ty, (float)(m.explodeTime - time), m.radius, true);
            t.DodgeMissile(tx, ty, m.explodeTime - time);
        }

        /// <summary>The Electrobot's bolt from the hatch (DV45, ZObject.Electro.cs Bolt): his damage, no roll, a bolt that does not
        /// kill stuns what can be stunned. The event carries the bolt's own fields: value = HP removed, flag = stunned, flag2 = killed.</summary>
        void DriverBolt(double time, ZObject t, int damage)
        {
            int hp0 = t.Health;
            t.DamageHealth(damage); t.LastEnemyHit = time; t.LastZapHit = time;
            bool killed = t.Destroyed, stunned = false;
            if (!killed && t.CanBeStunned()) stunned = t.ApplyStun(this);
            DriverHits++;
            World.Emit(ZEventType.DriverShot, this, t, t.CenterX, t.CenterY, hp0 - t.Health, 0, stunned, killed);
            DriverHitBooked(t, hp0);
        }

        // ------------------------------------------------------------------ part C: the APC's lone driver
        /// <summary>This APC has its one driver and nobody else aboard, he is not a veteran (never shown, DV12) and the APC does
        /// not stand on a bridge / in a door lane: he comes up to fire. The position is asked now (only an APC with a lone driver
        /// that has a target or has him up gets this far), so he is down in the tick the APC rolls onto such a spot.</summary>
        bool ApcDriverMayComeUp(double time) =>
            ZWorld.DriverFireDeviation && IsApc && !Destroyed && Owner != Team.Neutral && Drivers.Count == 1 && Passengers.Count == 0 && !VeteranActive && !HatchShutSpot(time, true);

        /// <summary>ProcessAttackDamage, after the no-target check and before any cooldown: the APC has a target to fire at. Its lone
        /// driver starts to come up now and stays up; true = he is not up yet (no shot, no cooldown used, nothing drawn from Zod's
        /// rand()). False for everything that is not such an APC.</summary>
        bool ApcDriverPopHold(double time)
        {
            if (Kind != ZKind.Vehicle || Type != "apc") return false;
            if (!ApcDriverMayComeUp(time)) return false;
            if (dfUpSince < 0) dfUpSince = time;
            dfUpUntil = time + ZWorld.CraneGunLinger;
            return time + 1e-9 < dfUpSince + ZWorld.CraneGunPopSeconds;
        }

        /// <summary>End of the APC's tick: he drops back in CraneGunLinger after the APC last had a target, and at once when a squad
        /// boards, the crew is promoted or the APC rolls onto a bridge / into a door lane.</summary>
        void ApcDriverStep(double time)
        {
            if (dfUpSince < 0) return;
            if (time >= dfUpUntil || !ApcDriverMayComeUp(time)) dfUpSince = -1;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV87 hatch drivers fire their own weapon (ZObject.DriverFire.cs). The game turns it on at every start (off in
        /// 'Zod exact'); headless default off.</summary>
        public static bool DriverFireDeviation = false;
        /// <summary>DV87 part B: seconds between two looks at where a crewed tank stands (bridge, door lane) for the rest check; an
        /// open lid and the APC driver in a fight ask every tick.</summary>
        public const double HatchSpotSeconds = 0.25;
        /// <summary>DV87 statistics (checks, all worlds): shots fired by tank drivers from the hatch; lids kept shut or shut again
        /// because the tank stood on a bridge / in a door lane.</summary>
        public static int DriverShotsFired, HatchesHeld;
    }
}
