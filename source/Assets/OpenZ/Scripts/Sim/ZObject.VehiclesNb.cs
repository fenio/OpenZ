using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// static switch (default on, ZGame sets them at every start from PlayerPrefs; off in 'Zod exact' and in every parity run).
    /// <list type="bullet">
    /// <item><b>DV30 heavy tank twin barrels</b>: the two barrels fire one after the other (one-two, not together), each
    /// shot with half of Zod's heavy tank damage so the total matches. Zod fires ONE missile of `Damage` every attack_speed
    /// (zobject.cpp:2102-2213, heavy 5000 HP every 4.088 s). With DV30 the cycle, range, lead, scatter, missile speed, splash radius
    /// and the target's dodge stay Zod's; the cycle's missile carries floor(Damage / 2) and a second missile carrying the rest
    /// (Damage - floor(Damage / 2): the pair always sums to Zod's Damage exactly) leaves <see cref="ZWorld.HeavyPairGap"/> s later
    /// with Zod's own lead + scatter at the same target (or the first shell's point when the target is gone). Its MissileFired
    /// event carries flag2 = true (second barrel), which no Zod consumer reads for MissileFired. A heavy that dies or loses its
    /// driver before the second shell does not fire it.</item>
    /// <item><b>DV31 crane driver shoots back</b>: when the crane is under
    /// attack its driver pops out of a hatch and shoots back. Zod's crane has no weapon (Damage 0, never CanAttack, never agros). With DV31
    /// a crewed crane checks every 0.5 s (like CheckPassiveEngage) for an enemy unit that is attacking IT (AttackObject == crane,
    /// or an Attack / Agro waypoint on it) and stands within the driver's rifle range with a clear line (Zod's engage barrier +
    /// DV23 sight); the first such unit in object order becomes its target. The driver pops out of the cab hatch
    /// (<see cref="ZWorld.CraneGunPopSeconds"/>, the lid + rise) and fires the grunt's rifle: Zod's grunt damage, hit chance and
    /// cycle (zobject.cpp:2102-2213 hitscan branch; no snipe: a crane is not a sniping unit). The crane keeps its orders (repair
    /// missions, moves), never gets an agro waypoint, never engages anyone who is not shooting at it; CanAttack() stays false, so
    /// Zod's passive engage, auto-grab and the CPU's unit logic see the same crane as before.</item>
    /// </list>
    /// Deterministic: sim time and the world's own Zod rand() stream only, no frame time, no view state (multiplayer-ready rule).
    /// </summary>
    public partial class ZObject
    {
        // ------------------------------------------------------------------ DV30 heavy tank twin barrels
        double hpSecondAt = -1; int hpTargetId = -1, hpDamage, hpTx, hpTy; bool hpPlayerGiven;

        /// <summary>DV30: the heavy's second shell is loaded (sim time it leaves; -1 = none). View / probes.</summary>
        public double HeavySecondAt => hpSecondAt;

        /// <summary>ProcessAttackDamage (missile branch, not a grenade): the damage the cycle's missile carries. Zod = Damage; DV30 on a
        /// heavy tank = the first barrel's half, and the second barrel is loaded for HeavyPairGap s later.</summary>
        int HeavyPairFirst(double time, ZObject t, bool playerGiven, int tx, int ty)
        {
            if (!ZWorld.HeavyPairDeviation || Kind != ZKind.Vehicle || Type != "heavy" || t == null) return Damage;
            int first = Damage / 2;
            hpDamage = Damage - first; hpSecondAt = time + ZWorld.HeavyPairGap; hpTargetId = t.Id; hpPlayerGiven = playerGiven;
            hpTx = tx; hpTy = ty;
            return first;
        }

        /// <summary>Top of ProcessAttackDamage, every tick: fires the loaded second barrel when its time has come (whatever the unit
        /// does meanwhile: a new order, a lost target, driving on). Zod's lead + scatter for its own flight, the target dodges.</summary>
        void HeavyPairStep(double time)
        {
            if (hpSecondAt < 0 || time + 1e-9 < hpSecondAt) return;
            hpSecondAt = -1;
            if (!ZWorld.HeavyPairDeviation || Destroyed || Owner == Team.Neutral || Drivers.Count == 0) return;
            var t = World.Get(hpTargetId);
            int tx, ty;
            if (t != null && !t.Destroyed && t.KillAt < 0 && t.Owner != Owner)
            {
                if (!EstimateMissileTarget(t, MissileSpeed, out tx, out ty)) { tx = t.CenterX; ty = t.CenterY; }
                tx += World.Rng.Next(32) - 16; ty += World.Rng.Next(32) - 16;                 // Zod's scatter (zobject.cpp:2150-2151)
                ShellOverCover(t, ref tx, ref ty);                                              // DV122: the second shell too may skim over a kneeling / lying sniper (Sim/ZObject.ProneTarget.cs)
            }
            else { t = null; tx = hpTx; ty = hpTy; }                                         // target gone: the barrel was already laid
            var m = new ZMissile { team = Owner, attackerId = Id, targetId = t != null ? t.Id : hpTargetId, playerGiven = hpPlayerGiven,
                                   damage = hpDamage, radius = DamageRadius, x = tx, y = ty };
            m.CalcExplodeTime(CenterX, CenterY, MissileSpeed, time);
            World.AddMissile(m);
            World.Emit(ZEventType.MissileFired, this, t, tx, ty, (float)(m.explodeTime - time), m.radius, false, true);   // flag2: second barrel
            t?.DodgeMissile(tx, ty, m.explodeTime - time);
        }

        // ------------------------------------------------------------------ DV31 crane driver shoots back
        ZObject cgTarget; double cgNextScan, cgNextShot, cgLastShot = -100, cgUpSince = -1;

        /// <summary>DV31: the unit the crane's driver is shooting at (null = none). View: BoardingView aims the hatch driver at it.</summary>
        public ZObject CraneGunTarget => cgTarget;
        /// <summary>DV31: sim time of the driver's last shot.</summary>
        public double CraneGunLastShot => cgLastShot;
        /// <summary>DV31: the driver is up in the cab hatch (a target, or shots within the last CraneGunLinger s). View only reads it.</summary>
        public bool CraneGunUp => ZWorld.CraneGunDeviation && Type == "crane" && Kind == ZKind.Vehicle && !Destroyed && Owner != Team.Neutral &&
                                  (cgTarget != null || World.Time - cgLastShot < ZWorld.CraneGunLinger);

        /// <summary>The driver's rifle (Zod's grunt: 11 HP, 70 %, 0.5 s, 120 px). A crane is always driven by the robot that took it,
        /// but a cab hatch fits a rifle only, so every driver type shoots the grunt's rifle.</summary>
        ZUnitSettings CraneRifle => World.S.Unit("grunt");

        /// <summary>End of ProcessServer, every tick (after Zod's own attack code, which never fires for a crane).</summary>
        void CraneGunStep(double time)
        {
            if (Type != "crane" || Kind != ZKind.Vehicle) return;
            if (!ZWorld.CraneGunDeviation || Owner == Team.Neutral || Drivers.Count == 0) { cgTarget = null; cgUpSince = -1; return; }
            var u = CraneRifle; if (u == null) return;
            if (cgTarget != null && !CraneGunValid(cgTarget, u.attack_radius)) cgTarget = null;
            if (time >= cgNextScan)
            {
                cgNextScan = time + 0.5;                                                        // Zod's passive check interval (zobject.cpp:1603)
                if (cgTarget == null)
                {
                    foreach (var o in World.Objects)
                        if (CraneGunValid(o, u.attack_radius)) { cgTarget = o; break; }            // first in object order, like CheckPassiveEngage
                    if (cgTarget != null && (cgUpSince < 0 || time - cgLastShot >= ZWorld.CraneGunLinger))
                    { cgUpSince = time; cgNextShot = Math.Max(cgNextShot, time + ZWorld.CraneGunPopSeconds); }   // the driver pops out first
                }
            }
            if (cgTarget == null) { if (time - cgLastShot >= ZWorld.CraneGunLinger) cgUpSince = -1; return; }
            if (time < cgNextShot) return;
            var t = cgTarget;
            cgNextShot = time + u.attack_speed; cgLastShot = time;
            if (CoverMiss(t, World.Rng.Roll(), u.attack_damage_chance)) { World.Emit(ZEventType.Hitscan, this, t, t.CenterX, t.CenterY, 0, 0, false); return; }   // miss (DV122: a kneeling / lying sniper is a smaller target, Sim/ZObject.ProneTarget.cs)
            t.DamageHealth(u.Damage * ZWorld.CrewRifleMul);
            World.UpdateObjectHealth(t, Id);
            World.Emit(ZEventType.Hitscan, this, t, t.CenterX, t.CenterY, 0, 0, true, false);
            if (t.Destroyed) cgTarget = null;
        }

        /// <summary>An enemy unit that is attacking this crane, in rifle range with a clear line.</summary>
        bool CraneGunValid(ZObject o, int radius)
        {
            if (o == null || o == this || !o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner) return false;
            bool threat = o.AttackObject == this;
            if (!threat && o.Waypoints.Count > 0) { var w = o.Waypoints[0]; threat = (w.mode == WpMode.Attack || w.mode == WpMode.Agro) && w.refId == Id; }
            if (!threat) return false;
            if (!Within(CenterX, CenterY, o.CenterX, o.CenterY, radius)) return false;
            if (!o.ShuttleAirborne && World.EngageBarrierBetween(CenterX, CenterY, o.CenterX, o.CenterY)) return false;   // DV118: no ground tile stops a shot at the flying shuttle
            return HasLineOfSight(o);
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>vehicles_nb_v1 DV30: the heavy tank fires its two barrels one-two, half of Zod's damage each (ZObject.VehiclesNb.cs).
        /// OFF = Zod exact (one shell of the full damage). Static like DuelDeviation; ZGame sets it at every start (PlayerPrefs
        /// oz_heavy_pair, oz_zod_exact).</summary>
        public static bool HeavyPairDeviation = true;
        /// <summary></summary>
        public const double HeavyPairGap = 0.36;

        /// <summary>vehicles_nb_v1 DV31: a crewed crane's driver pops out of the cab hatch and shoots back at units attacking it
        /// (ZObject.VehiclesNb.cs). OFF = Zod exact (the crane has no weapon). PlayerPrefs oz_crane_gun, oz_zod_exact.</summary>
        public static bool CraneGunDeviation = true;
        /// <summary>The driver's first shot waits for the lid (Zod's lid 0.4 s, BoardingView) and his rise (0.3 s).</summary>
        public const double CraneGunPopSeconds = 0.7;
        /// <summary>Seconds the driver stays up after his last shot (then he drops back in and the lid closes).</summary>
        public const double CraneGunLinger = 2.0;
        /// <summary>
        /// The lone crew rifles of the deviations (DV31 crane driver, DV41 fort roof defender, DV15 APC driver
        /// without passengers) hit with a grunt SQUAD's rifle (Zod's 3 grunts x 11 HP = 33 HP per hit, the firepower a Zod APC driven
        /// by a grunt squad has, vapc.cpp:341), same hit chance, cycle and range. Applies only while the deviation itself is on.</summary>
        public const int CrewRifleMul = 3;
    }
}
