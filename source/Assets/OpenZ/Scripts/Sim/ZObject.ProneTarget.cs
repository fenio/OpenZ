using System;

namespace OpenZ.Sim
{
    /// <summary>DV122: a sniper's stance as the rules have it (the view draws it). Every other unit is always Stand.</summary>
    public enum ZStance { Stand = 0, Kneel = 1, Prone = 2 }

    /// <summary>
    /// OpenZ deviation DV122 "a kneeling or lying sniper is a smaller target" (Zod has no stance: every robot is hit alike, one hit
    /// roll against damage_chance, zobject.cpp:2187, and the same blast for every body, zserver.cpp:1043-1112).
    /// <para>Before: the stance was the view's own choice (BS01Stance, RobotWeaponPose), so a sniper drawn lying flat was hit exactly as a
    /// standing one. Now the stance is a RULE and the view only draws it, so what is seen is what is hit.</para>
    /// <para>The stance rule (the view's rules of before, moved here unchanged; deterministic, no random draw): a sniper on foot (not the
    /// fort's roof sniper: his sandbag nest has its own cover, DV41 / DV111) picks stand / kneel / prone once per FIGHT: at the first
    /// target after <see cref="ZWorld.StanceFightGap"/> s without one, with the odds the view used (a target at 55 % of its range or more:
    /// prone 35 %, kneel 43 %, stand 22 %; nearer: kneel 40 %, stand 60 %), from a fixed hash of its id and its fight count. It goes down
    /// only after it has stood still with a target for <see cref="ZWorld.StanceDownStill"/> s, at most once a fight, and not within
    /// <see cref="ZWorld.StanceMinUpTime"/> s of getting up; it gets up for a real walk only (the sim walks it on an order or a chase, or
    /// it is moved <see cref="ZWorld.StanceRealWalkTime"/> s / <see cref="ZWorld.StanceRealWalkPx"/> px from where it went down), and a
    /// sniper that got up for a walk fights on standing until the fight is over; it stays down until the fight is over
    /// (<see cref="ZWorld.StanceFightGap"/> s without a target). Never down in water, while it would throw a grenade or while it is
    /// knocked down. The cover counts once it IS down: <see cref="KneelDownSecs"/> / <see cref="ProneDownSecs"/> after it began to go
    /// down (the length of the kneel / prone clip), at once no more when it gets up.</para>
    /// <para>What the cover does (only a sniper's, only with the switch on):
    /// direct fire (every gun with a hit roll: rifles, sniper rifles, the psycho's and the jeep's machine guns, lasers, the gatling, the
    /// APC's crew guns, the crane's and the tanks' hatch drivers; not the flame of a pyro, which rolls along the ground) hits a kneeling
    /// sniper with <see cref="ZWorld.KneelHitPct"/> % and a lying one with <see cref="ZWorld.ProneHitPct"/> % of its hit chance (the same
    /// one roll, scaled); the Electrobot's zap (Zod: it always hits) hits it with the same shares, a miss strikes the ground just behind
    /// him (own hashed roll, no draw of the world's random numbers; it recharges as after a bolt); a shell fired straight at him (light,
    /// medium and heavy tank, the gun) that would reach him (not one that falls short of his body) lands on him with the same shares,
    /// else it skims over him and bursts <see cref="ZWorld.ShellOverPx"/> px further on, behind him; every blast that reaches him
    /// (grenades, shells, rockets, the howitzer, the fort
    /// gun, a crate blast) takes <see cref="ZWorld.KneelBlastPct"/> % / <see cref="ZWorld.ProneBlastPct"/> % of its HP from him (less body
    /// above the ground). A headshot keeps its 15 % of the hits: lying head-first towards the shooter the head is still what shows, so
    /// fewer hits give fewer headshots and nothing more is taken off.</para>
    /// Switch <see cref="ZWorld.ProneTargetDeviation"/>: View/ZGame.cs sets it at every start (off in 'Zod exact'); off in headless
    /// worlds, in the parity report's Zod rows and in the counted checks unless they set it. Off: no stance, every roll and blast as before,
    /// unit for unit.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV122 applies to this unit: a sniper on foot (not the fort's roof sniper), the switch on.</summary>
        public bool ProneTargetRule => ZWorld.ProneTargetDeviation && Kind == ZKind.Robot && Type == "sniper" && !IsFortDefender;

        /// <summary>DV122: the stance it is in or going into (Stand for every other unit and with the switch off). The view draws it.</summary>
        public ZStance Stance { get; private set; }
        /// <summary>DV122: the game time it took <see cref="Stance"/>.</summary>
        public double StanceSince { get; private set; } = -1;
        /// <summary>DV122: this fight's stance (picked at the first target of a fight) and the number of fights so far.</summary>
        public ZStance StancePick { get; private set; }
        public int StanceFights { get; private set; }

        /// <summary>DV122: the clip lengths of getting down (s): the cover counts from then on.</summary>
        public const double KneelDownSecs = 0.417, ProneDownSecs = 0.833;

        /// <summary>DV122: the stance that covers it now: Kneel / Prone once it is down, else Stand (also while it is still going down).</summary>
        public ZStance CoverStance =>
            ZWorld.ProneTargetDeviation && Stance != ZStance.Stand && World != null &&
            World.Time - StanceSince >= (Stance == ZStance.Prone ? ProneDownSecs : KneelDownSecs) - 1e-9 ? Stance : ZStance.Stand;

        double psStill, psWalk, psSinceTarget = 999, psSinceUp = 999;
        int psDownX, psDownY, psLastX = int.MinValue, psLastY;
        bool psWalked, psFixed;

        /// <summary>Counted checks and the Play stage only: this sniper keeps the given stance (already down: it covers at once) until
        /// <see cref="ReleaseStanceForCheck"/>.</summary>
        public void HoldStanceForCheck(ZStance s) { psFixed = true; Stance = s; StanceSince = (World != null ? World.Time : 0) - 10; }
        /// <summary>Counted checks and the Play stage only: the rules decide its stance again.</summary>
        public void ReleaseStanceForCheck() { psFixed = false; }
        /// <summary>A stance held by a check or the stage.</summary>
        public bool StanceHeldForCheck => psFixed;

        void SetStance(ZStance s) { Stance = s; StanceSince = World.Time; }

        /// <summary>DV122, once a tick for every sniper (ZWorld.ProneTargetTick, after the spacing layer moved everyone, before the
        /// missiles burst): the stance rule of the class summary.</summary>
        internal void StanceStep(double dt)
        {
            if (psFixed) return;
            if (!ProneTargetRule || Destroyed || KillAt >= 0)
            {
                if (Stance != ZStance.Stand) SetStance(ZStance.Stand);
                psStill = psWalk = 0; psLastX = int.MinValue;
                return;
            }
            var t = AttackObject;
            bool engaged = t != null && !t.Destroyed;
            bool moved = psLastX != int.MinValue && (CenterX != psLastX || CenterY != psLastY);
            psLastX = CenterX; psLastY = CenterY;
            bool moving = IsMoving, walking = moving || moved;
            bool order = moving && Waypoints.Count > 0 && !(Waypoints[0].mode == WpMode.Move && !Waypoints[0].playerGiven && Waypoints[0].refId < 0);   // not the spacing layer's own small moves
            psStill = engaged && !walking ? psStill + dt : 0;
            psWalk = moving ? psWalk + dt : 0;
            psSinceUp += dt;
            if (engaged)
            {
                if (psSinceTarget > ZWorld.StanceFightGap)
                {   // a new fight: its stance, once
                    psWalked = false; StanceFights++; ZWorld.StancePicks++;
                    double dx = t.CenterX - CenterX, dy = t.CenterY - CenterY;
                    StancePick = PickStance(Id, StanceFights, AttackRadius > 0 ? Math.Sqrt(dx * dx + dy * dy) / AttackRadius : 0);
                }
                psSinceTarget = 0;
            }
            else psSinceTarget += dt;
            var wc = World.Map.ClassAt(CenterX >> 4, CenterY >> 4);
            bool water = wc == ZClass.Water || wc == ZClass.DeepWater;
            bool knocked = KnockedDown(World.Time), throws = engaged && StanceThrows(t);
            if (Stance == ZStance.Stand)
            {
                if (engaged && StancePick != ZStance.Stand && psStill >= ZWorld.StanceDownStill - 1e-9 && !psWalked && psSinceUp >= ZWorld.StanceMinUpTime - 1e-9 && !water && !knocked && !throws
                    && SniperStanceSettled())                                              // K248d: only where the fight needs no more walking (ZObject.SniperSettle.cs)
                { SetStance(StancePick); psDownX = CenterX; psDownY = CenterY; ZWorld.StanceLieDowns++; }
                return;
            }
            int ddx = CenterX - psDownX, ddy = CenterY - psDownY, rw = ZWorld.StanceRealWalkPx;
            if (order || (walking && (psWalk >= ZWorld.StanceRealWalkTime - 1e-9 || ddx * ddx + ddy * ddy >= rw * rw)))
            { SetStance(ZStance.Stand); psWalked = true; psSinceUp = 0; ZWorld.StanceGetUpsWalk++; }                   // a real walk: up, and standing for the rest of this fight
            else if (water || knocked || throws)
            { SetStance(ZStance.Stand); psSinceUp = 0; ZWorld.StanceGetUpsOther++; }                                   // water, a knockdown, a grenade throw
            else if (!engaged && psSinceTarget > ZWorld.StanceFightGap)
            { SetStance(ZStance.Stand); psSinceUp = 0; ZWorld.StanceGetUpsLinger++; }                                  // the fight is over (a new one right after it: up StanceMinUpTime s first)
        }

        /// <summary>DV122: its next shot at t would be a grenade throw (its squad's grenades, DV40's rule; snipers never fire walking).</summary>
        bool StanceThrows(ZObject t) =>
            (Grenades > 0 || (Leader != null && Leader.Grenades > 0)) && !(ZWorld.GrenadeRuleDeviation && GrenadeRuleType != "grunt" && t.Kind == ZKind.Robot);

        /// <summary>DV122: the fight's stance from a fixed hash of the unit id and the fight number, with the odds the view used before
        /// (the same generator and numbers: a long shot at 55 % of the range or more prone 35 %, kneel 43 %, stand 22 %; nearer kneel 40 %).</summary>
        public static ZStance PickStance(int unitId, int fight, double range01)
        {
            uint s;
            unchecked
            {
                uint h = (uint)(unitId * 7919 + 13) * 0x9E3779B1u ^ ((uint)fight + 0x7F4A7C15u) * 0x85EBCA77u; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
                int seed = (int)(h & 0x7FFFFFFF);
                s = (uint)(seed * 747796405 + 2891336453u) | 1u;
            }
            float u = 0f;
            for (int i = 0; i < 3; i++) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; u = (s & 0xFFFFFF) / 16777216f; }
            if (range01 >= ZWorld.StanceLongRange) return u < 0.35f ? ZStance.Prone : u < 0.78f ? ZStance.Kneel : ZStance.Stand;
            return u < 0.40f ? ZStance.Kneel : ZStance.Stand;
        }

        // ------------------------------------------------------------------ the cover
        /// <summary>The flame of a pyro (a robot, or an APC with a pyro crew): it rolls along the ground, a lying body burns the same.</summary>
        bool IsFlameGun => (IsRobot && Type == "pyro") || (Kind == ZKind.Vehicle && Type == "apc" && CrewWeaponType == "pyro");

        /// <summary>A flat-firing shell gun (aimed straight at its target): the light, medium and heavy tank, the gun.</summary>
        bool FlatShellGun => (Kind == ZKind.Vehicle && (Type == "light" || Type == "medium" || Type == "heavy")) || (Kind == ZKind.Cannon && Type == "gun");

        static bool CoveredSniper(ZObject t) => t != null && t.Kind == ZKind.Robot && t.Type == "sniper";

        /// <summary>DV122, every hit roll of a gun at t (ProcessAttackDamage, the crane's driver, a hatch driver): true = a miss. Zod's
        /// one roll against the hit chance; against a kneeling / lying sniper the chance is scaled to KneelHitPct / ProneHitPct %. Switch
        /// off, another target or a flame: exactly Zod's test.</summary>
        internal bool CoverMiss(ZObject t, double roll, double chance, bool flame = false)
        {
            chance = ShuttleKickHitChance(t, roll, chance);                                         // DV129: a flipping shuttle is a harder target (the same roll; unchanged for any other target)
            if (!ZWorld.ProneTargetDeviation || !CoveredSniper(t)) return roll > chance;
            if (flame || IsFlameGun) return roll > chance;
            var c = t.CoverStance;
            double k = c == ZStance.Prone ? ZWorld.ProneHitPct / 100.0 : c == ZStance.Kneel ? ZWorld.KneelHitPct / 100.0 : 1.0;
            bool miss = roll > chance * k;
            ZWorld.CoverShots[(int)c]++; if (!miss) ZWorld.CoverHits[(int)c]++;
            return miss;
        }

        /// <summary>DV122, a flat shell's aim point (after Zod's lead and scatter): a shell that falls short of a kneeling / lying sniper's
        /// body (its burst point more than half his width in front of his centre, along the line of fire) lands where it lands, as on a
        /// standing one (the ground in front of him takes it); one that would reach him lands on him with KneelHitPct / ProneHitPct % (own
        /// hashed roll), else it skims over him and bursts ShellOverPx further along its line, behind him (never nearer to him).</summary>
        internal void ShellOverCover(ZObject t, ref int tx, ref int ty)
        {
            if (!ZWorld.ProneTargetDeviation || !FlatShellGun || !CoveredSniper(t)) return;
            var c = t.CoverStance;
            if (c == ZStance.Stand) return;
            double dx = t.CenterX - CenterX, dy = t.CenterY - CenterY, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1) return;
            double ux = dx / d, uy = dy / d;
            if ((tx - t.CenterX) * ux + (ty - t.CenterY) * uy < -(t.WidthPix >> 1)) { ZWorld.ShellsShort++; return; }   // short of his body: the ground in front of him takes it
            ZWorld.ShellsAtCover++;
            if (World.CoverRoll(this, t) < (uint)(c == ZStance.Prone ? ZWorld.ProneHitPct : ZWorld.KneelHitPct)) return;
            tx += (int)(ux * ZWorld.ShellOverPx); ty += (int)(uy * ZWorld.ShellOverPx);
            ZWorld.ShellsOver++;
        }

        /// <summary>DV122, the Electrobot's bolt (ElectroFire, on its release tick): true = it missed a kneeling / lying sniper (own hashed
        /// roll, KneelHitPct / ProneHitPct % hit) and struck the ground ZapMissPx behind him: no damage, no stun; it recharges as after a
        /// bolt. The event: ElectroBolt with no target (otherId -1), x / y = the ground point, value 0.</summary>
        bool ZapMissCover(ZObject t)
        {
            if (!ZWorld.ProneTargetDeviation || !CoveredSniper(t)) return false;
            var c = t.CoverStance;
            if (c == ZStance.Stand) return false;
            ZWorld.ZapsAtCover++;
            if (World.CoverRoll(this, t) < (uint)(c == ZStance.Prone ? ZWorld.ProneHitPct : ZWorld.KneelHitPct)) return false;
            double dx = t.CenterX - CenterX, dy = t.CenterY - CenterY, d = Math.Sqrt(dx * dx + dy * dy);
            int gx = t.CenterX + (d >= 1 ? (int)(dx / d * ZWorld.ZapMissPx) : ZWorld.ZapMissPx), gy = t.CenterY + (d >= 1 ? (int)(dy / d * ZWorld.ZapMissPx) : 0);
            elPhase = ElectroPhase.Recharging; elRelease = -1; elReady = World.TickIndex + ZElectro.RechargeTicks; elTarget = t.Id;
            ElBolts++; World.ElectroBolts++; ZWorld.ZapsMissed++;
            World.Emit(ZEventType.ElectroBolt, this, null, gx, gy, 0, ZElectro.RechargeTicks, false, false);
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV122 switch (see ZObject.ProneTarget.cs). View/ZGame.cs sets it at every start (off in 'Zod exact'); headless default off.</summary>
        public static bool ProneTargetDeviation = false;
        /// <summary>DV122: per cent of a gun's hit chance against a kneeling / lying sniper (also the share of zaps and of flat shells that
        /// land on him). Chosen with the counted check (K305 kit, out/range_1000.txt): 18 % / 40 % fewer hits.</summary>
        public static int KneelHitPct = 82, ProneHitPct = 60;
        /// <summary>DV122: per cent of a blast's HP a kneeling / lying sniper takes (10 % / 25 % less body above the ground).</summary>
        public static int KneelBlastPct = 90, ProneBlastPct = 75;
        /// <summary>DV122: a flat shell that skims over a kneeling / lying sniper bursts this many px (2.5 m) further along its line than its
        /// aim point (only a shell that would reach him; one that falls short is not moved); a zap that misses strikes the ground this many
        /// px (1.5 m) behind him.</summary>
        public static int ShellOverPx = 20, ZapMissPx = 12;
        /// <summary>DV122 stance rule (the view's numbers of before): a fight ends after StanceFightGap s without a target; down after
        /// StanceDownStill s still with a target; a real walk = StanceRealWalkTime s of walking or StanceRealWalkPx px (0.6 m) from where
        /// it went down; StanceMinUpTime s up before it goes down again; a long shot from StanceLongRange of its range.</summary>
        public static double StanceFightGap = 6.0, StanceDownStill = 0.3, StanceRealWalkTime = 0.35, StanceMinUpTime = 3.0, StanceLongRange = 0.55;
        public static int StanceRealWalkPx = 5;

        /// <summary>DV122 statistics (checks, the Play report): stance picks, lie-downs, get-ups for a walk / after the fight / for water,
        /// a knockdown or a throw; per cover stance (Stand, Kneel, Prone) the gun shots at snipers and their hits; flat shells at a covered
        /// sniper that would reach him and those that skimmed over, and those that fell short of him (not moved); zaps at a covered sniper
        /// and those that missed; blasts on a covered sniper and the HP the cover kept.</summary>
        public static int StancePicks, StanceLieDowns, StanceGetUpsWalk, StanceGetUpsLinger, StanceGetUpsOther, ShellsAtCover, ShellsOver, ShellsShort, ZapsAtCover, ZapsMissed, BlastsAtCover;
        public static long BlastHpSaved;
        public static readonly int[] CoverShots = new int[3], CoverHits = new int[3];

        public static void ResetCoverCounters()
        {
            StancePicks = StanceLieDowns = StanceGetUpsWalk = StanceGetUpsLinger = StanceGetUpsOther = ShellsAtCover = ShellsOver = ShellsShort = ZapsAtCover = ZapsMissed = BlastsAtCover = 0;
            BlastHpSaved = 0;
            for (int i = 0; i < 3; i++) CoverShots[i] = CoverHits[i] = 0;
        }

        /// <summary>DV122: this world's count of cover rolls (the hash's own counter).</summary>
        public int CoverRolls { get; private set; }
        bool stanceAny;

        /// <summary>DV122: one roll 0..99 for a zap or a flat shell at a covered sniper. Its own stream (never the world's Rng): a hash of
        /// the shooter, the target, this world's count of such rolls and the game clock (ms).</summary>
        internal uint CoverRoll(ZObject shooter, ZObject target)
        {
            uint h = unchecked(CoverMix((uint)shooter.Id * 0x9E3779B1u ^ CoverMix((uint)target.Id * 0x85EBCA77u + (uint)CoverRolls * 0xC2B2AE3Du)
                                         ^ (uint)(long)Math.Round(Time * 1000.0) ^ 0x27D4EB2Fu));
            CoverRolls++;
            return h % 100u;
        }

        static uint CoverMix(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; } }

        /// <summary>DV122, ZWorld.Tick after the spacing layer and before the missiles burst: every sniper's stance (switch off: nothing
        /// is done, unless a stance is still down from before the switch went off).</summary>
        internal void ProneTargetTick(double dt)
        {
            if (!ProneTargetDeviation && !stanceAny) return;
            bool any = false;
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (o.Kind != ZKind.Robot || o.Type != "sniper") continue;
                o.StanceStep(dt);
                any |= o.Stance != ZStance.Stand;
            }
            stanceAny = any;
        }

        /// <summary>DV122, ProcessMissileDamage: the HP a blast takes from o (a kneeling / lying sniper: KneelBlastPct / ProneBlastPct % of
        /// it, rounded); every other body and the switch off: unchanged.</summary>
        internal int CoverBlast(ZObject o, int dmg)
        {
            if (!ProneTargetDeviation || dmg <= 0 || o == null) return dmg;
            var c = o.CoverStance;
            if (c == ZStance.Stand) return dmg;
            int k = (int)(((long)dmg * (c == ZStance.Prone ? ProneBlastPct : KneelBlastPct) + 50) / 100);
            BlastsAtCover++; BlastHpSaved += dmg - k;
            return k;
        }

        /// <summary>Turns DV122 off until it is disposed, then puts back what was there. The parity report holds one while it measures (a
        /// Play session leaves the switch on); its own row turns the rule on by itself.</summary>
        public sealed class ProneTargetHold : IDisposable
        {
            readonly bool was;
            internal ProneTargetHold() { was = ProneTargetDeviation; ProneTargetDeviation = false; }
            public void Dispose() { ProneTargetDeviation = was; }
        }
        public static ProneTargetHold ProneTargetOff() => new ProneTargetHold();
    }
}
