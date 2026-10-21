#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV123 "the Tough stands and fires" (the rule: Sim/ZObject.ToughSteady.cs, DV120's steady rule for the
    /// Tough) and the K306 counted check (menu OpenZ/V1.1/Tough: K306 Steady Fire Check). Measured headless on DV120's own stage
    /// (OpenZParity.V11SniperSteady.cs SsFight): the level 2 map without its start units, open dry sand (tile 41,57), the shooters, two
    /// blue grunt squads gx px to the east (one 40 px north, one 15 px further and 40 px south), every shooter squad ordered at once to
    /// attack the first squad's leader (the player's order); no tank, no grenade, no crate; the switches of a normal game (DV120 and its
    /// stance rules on). The Tough's drawn brace is a frame model of View/BS01Stance.cs DriveTough / OnFire (its clip lengths, its 0.25 s
    /// enemy scan, its 1.6 s linger) on the drawn body of UnitView (the same model as DV120's check), stepped with the sim at 100 frames a
    /// second; the DV11 aim (ZObject.ToughPrepare.cs) is read from the sim.</summary>
    public static partial class OpenZParity
    {
        static void AddToughSteadyDeviation(List<Deviation> list)
        {
            try { list.Add(DevToughSteady()); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV123", change = "Toughs stand and fire", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        // ------------------------------------------------------------------ cases
        internal sealed class TsfSquad
        {
            public string type; public int amount, dx, dy;
            public TsfSquad(string t, int a, int x, int y) { type = t; amount = a; dx = x; dy = y; }
        }

        internal sealed class TsfCase
        {
            public string name, what; public TsfSquad[] squads; public bool control;
        }

        static TsfSquad TsfQ(string t, int a, int dx = 0, int dy = 0) => new TsfSquad(t, a, dx, dy);

        /// <summary>The cases of the check: the Toughs (3 in one squad, as the question was asked; 2 = the game's squad), the mixed
        /// group of 3 Toughs and 3 Electrobots, the game's squads of 2 Toughs + 1 Electrobot, and the controls the rule must leave alone
        /// tick for tick (snipers with DV120, Electrobots, Pyros, grunts).</summary>
        internal static List<TsfCase> TsfCases()
        {
            var l = new List<TsfCase>();
            void C(string n, string what, bool control, params TsfSquad[] q) => l.Add(new TsfCase { name = n, what = what, squads = q, control = control });
            C("tough3", "3 Toughs in one squad", false, TsfQ("tough", 3));
            C("tough2", "2 Toughs (the game's squad)", false, TsfQ("tough", -1));
            C("mixed", "3 Toughs + 3 Electrobots side by side", false, TsfQ("tough", 3, 0, -24), TsfQ("electrobot", 1, 0, 8), TsfQ("electrobot", 1, 0, 24), TsfQ("electrobot", 1, 0, 40));
            C("mixed2", "2 Toughs + 1 Electrobot (the game's squads)", false, TsfQ("tough", -1, 0, -16), TsfQ("electrobot", 1, 0, 16));
            C("sniper", "3 snipers (DV120 + its stance rules, as the game has them)", true, TsfQ("sniper", -1));
            C("electro3", "3 Electrobots (3 single squads, as the game builds them; DV83)", true, TsfQ("electrobot", 1, 0, -16), TsfQ("electrobot", 1, 0, 0), TsfQ("electrobot", 1, 0, 16));
            C("pyro4", "4 Pyros (the game's squad)", true, TsfQ("pyro", -1));
            C("grunt3", "3 grunts (the game's squad)", true, TsfQ("grunt", -1));
            return l;
        }

        // ------------------------------------------------------------------ the counts
        /// <summary>The counters of one robot type in one case (summed over its fights).</summary>
        internal sealed class TsfCounts
        {
            public int fights, units, unfinished, lost, died, grunts, kills, shots;
            public int legs, legsAfterFirst, rev, revInRange, revChase, afterShot, creep, hops, walkBacks;
            public int rAgro, rHelp, rAttack, rReturn, rOther;
            public double fightSecs, px, pxAfterFirst, inRange, inRangeMoving, inRangeNoTarget, creepPx;
            // the Tough's drawn brace (BS01Stance.DriveTough model)
            public int bPrep, bRelWalk, bRelInRange, bCycles, bLowers, bDownUp, bShown, bUnbraced, bHeld, bEarly;
            // the Tough's DV11 aim (sim)
            public int engages, aims, reAims, reAimsSmall, reAimsWalk, tubeKept, pushesHeld, lateEngages, reAimsOnTarget, keptOnTarget, keptCarried;
            public double reAimGap;
            public ulong trace = 1469598103934665603UL, world = 1469598103934665603UL;

            public void Add(TsfCounts o)
            {
                fights += o.fights; units += o.units; unfinished += o.unfinished; lost += o.lost; died += o.died; grunts += o.grunts; kills += o.kills; shots += o.shots;
                legs += o.legs; legsAfterFirst += o.legsAfterFirst; rev += o.rev; revInRange += o.revInRange; revChase += o.revChase; afterShot += o.afterShot; creep += o.creep; hops += o.hops; walkBacks += o.walkBacks;
                rAgro += o.rAgro; rHelp += o.rHelp; rAttack += o.rAttack; rReturn += o.rReturn; rOther += o.rOther;
                fightSecs += o.fightSecs; px += o.px; pxAfterFirst += o.pxAfterFirst; inRange += o.inRange; inRangeMoving += o.inRangeMoving; inRangeNoTarget += o.inRangeNoTarget; creepPx += o.creepPx;
                bPrep += o.bPrep; bRelWalk += o.bRelWalk; bRelInRange += o.bRelInRange; bCycles += o.bCycles; bLowers += o.bLowers; bDownUp += o.bDownUp; bShown += o.bShown; bUnbraced += o.bUnbraced; bHeld += o.bHeld; bEarly += o.bEarly;
                engages += o.engages; aims += o.aims; reAims += o.reAims; reAimsSmall += o.reAimsSmall; reAimsWalk += o.reAimsWalk; tubeKept += o.tubeKept; pushesHeld += o.pushesHeld; reAimGap += o.reAimGap;
                lateEngages += o.lateEngages; reAimsOnTarget += o.reAimsOnTarget; keptOnTarget += o.keptOnTarget; keptCarried += o.keptCarried;
                trace = SnsFnv(trace, (long)o.trace); world = SnsFnv(world, (long)o.world);
            }

            public string Text(string type)
            {
                double uf = Math.Max(1, units);
                var sb = new StringBuilder();
                sb.Append($"{fights} fights, {units} unit-fights, {fightSecs:0.0} s of fighting{(unfinished > 0 ? $", {unfinished} NOT finished in 40 s" : "")}{(lost > 0 ? $", {lost} lost" : "")}, {died} of them died, {grunts} grunts killed ({kills} by this type), {shots} shots");
                sb.Append($" | movement: {legs} walking legs ({legs / uf:0.00} per unit-fight; agro {rAgro}, help call {rHelp}, attack approach {rAttack}, agro walk-back {rReturn}, other {rOther}), " +
                          $"{rev} reversals ({revInRange} with an enemy in range, {revChase} on a chase), {walkBacks} agro walk-backs, {creep} creep legs after a target ({creep / Math.Max(1.0, kills):0.00} per own kill, {creepPx / Math.Max(1, creep):0.0} px each), " +
                          $"{hops} short steps (0.5-2 m) after the first shot, {px:0} px walked ({pxAfterFirst:0} after the first shot), {100 * inRangeMoving / Math.Max(0.01, inRange):0.0} % of the in-range time walking, {inRangeNoTarget:0.00} s walking with an enemy in range and no target");
                if (type == "tough")
                    sb.Append($" | drawn brace: {bPrep} braces, {bRelWalk} let go for a walk ({bRelInRange} with an enemy in range), {bCycles} brace-let go-brace within 3 s, {bLowers} tube lowered (1.6 s without a threat), {bDownUp} lowered-braced within 3 s, " +
                              $"{bShown} rockets ({bUnbraced} not braced, {bHeld} held on screen for the brace, {bEarly} of them let go before the tube was down)" +
                              $" | DV11 aim: {engages} engagements, {aims} aims of 0.8 s, {reAims} of them again in the same fight ({reAimsSmall} after a step of 2 m or less, {reAimsWalk} after a walk; {reAimGap / Math.Max(1, reAims):0.00} s without a target before each), of the {lateEngages} engagements after more than 1.6 s without a target: aimed again while the drawn tube was already on target {reAimsOnTarget}{(TsfTubeKeptF != null ? $"; TRIAL rule: {tubeKept} engagements with no new aim (drawn tube on target {keptOnTarget} / carried {keptCarried})" : "")}; {pushesHeld} pushes not taken while firing");
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ the drawn body (UnitView: SmoothDamp body, stride, walkShown), as DV120's check models it
        sealed class TsfGait
        {
            const float StandHold = 0.12f, WalkOn = 0.3f, WalkOff = 0.15f, MinStand = 0.25f, MinWalk = 0.45f, StrideSlew = 6f, SmoothT = 0.16f;
            bool init, walkShown; double dx, dz, vx, vz, lx, lz; float stride, gaitT = 10f;

            public bool Step(ZObject o, float dt)
            {
                double px = o.CenterX / 8.0, pz = -o.CenterY / 8.0;
                float simSpeed = (float)Math.Sqrt(o.Dx * o.Dx + o.Dy * o.Dy) / 8f;
                if (!init) { dx = px; dz = pz; lx = dx; lz = dz; init = true; }
                else if (!walkShown && (px - dx) * (px - dx) + (pz - dz) * (pz - dz) < StandHold * StandHold) { vx = vz = 0; }
                else
                {
                    double cap = Math.Max(o.MoveSpeed / 8.0, simSpeed), ox = dx, oz = dz;
                    Damp(ref dx, ref dz, px, pz, ref vx, ref vz, SmoothT, cap * 1.15, dt);
                    double sx = dx - ox, sz = dz - oz, m = Math.Sqrt(sx * sx + sz * sz), lim = cap * dt;
                    if (m > lim && m > 0) { dx = ox + sx / m * lim; dz = oz + sz / m * lim; }
                }
                double stx = dx - lx, stz = dz - lz; lx = dx; lz = dz;
                float stepSpeed = dt > 0 ? (float)(Math.Sqrt(stx * stx + stz * stz) / dt) : 0f;
                stride = stride + Math.Max(-StrideSlew * dt, Math.Min(StrideSlew * dt, stepSpeed - stride));
                bool simWalk = o.IsMoving && o.MoveSpeed > 0, engaged = o.AttackObject != null && !o.AttackObject.Destroyed;
                bool aiming = !simWalk && engaged;
                bool wantWalk = walkShown ? stride > WalkOff : stride > WalkOn;
                gaitT += dt;
                if (wantWalk != walkShown && (gaitT >= (walkShown ? MinWalk : MinStand) || (!wantWalk && aiming))) { walkShown = wantWalk; gaitT = 0f; }
                return walkShown && o.MoveSpeed > 0;
            }

            static void Damp(ref double x, ref double z, double tx, double tz, ref double vx, ref double vz, double smooth, double maxSpeed, double dt)
            {
                smooth = Math.Max(0.0001, smooth);
                double omega = 2.0 / smooth, k = omega * dt, e = 1.0 / (1.0 + k + 0.48 * k * k + 0.235 * k * k * k);
                double cx = x - tx, cz = z - tz, ox = tx, oz = tz, maxC = maxSpeed * smooth, cm = cx * cx + cz * cz;
                if (cm > maxC * maxC) { double m = Math.Sqrt(cm); cx = cx / m * maxC; cz = cz / m * maxC; }
                tx = x - cx; tz = z - cz;
                double ttx = (vx + omega * cx) * dt, ttz = (vz + omega * cz) * dt;
                vx = (vx - omega * ttx) * e; vz = (vz - omega * ttz) * e;
                double outX = tx + (cx + ttx) * e, outZ = tz + (cz + ttz) * e;
                if ((ox - x) * (outX - ox) + (oz - z) * (outZ - oz) > 0) { outX = ox; outZ = oz; vx = (outX - ox) / dt; vz = (outZ - oz) / dt; }
                x = outX; z = outZ;
            }
        }

        // ------------------------------------------------------------------ the Tough's drawn brace (BS01Stance.DriveTough + OnFire, frame model)
        sealed class TsfBrace
        {
            enum St { Up, Prepare, Aim, Fire }
            const float PrepareLen = 0.833f, FireLen = 1.0f, Linger = 1.6f;
            readonly TsfGait gait = new TsfGait();
            St st = St.Up; float t, lastEngaged = -100f, nextScan, lastRelease = -100f, lastLower = -100f; bool predicted;
            public int prepares, relWalk, relInRange, cycles, lowers, downUp, shown, unbraced, held, early;
            /// <summary>The drawn tube: 0 carried, 1 coming down (tough_prepare), 2 on target (aim / fire).</summary>
            public int Tube => st == St.Up ? 0 : st == St.Prepare ? 1 : 2;

            public void Frame(ZObject o, ZWorld w, float now, float dt, bool rocket, bool enemyInRange, float flight = 1f)
            {
                bool walking = gait.Step(o, dt);
                bool engaged = o.AttackObject != null && !o.AttackObject.Destroyed;
                if (engaged) lastEngaged = now;
                t += dt;
                if ((nextScan -= dt) <= 0f) { nextScan = 0.25f; predicted = Predict(o, w); }
                bool threat = engaged || predicted, wantPrepare = threat && !walking;
                switch (st)
                {
                    case St.Up:
                        if (!wantPrepare) break;
                        st = St.Prepare; t = 0f; prepares++;
                        if (now - lastRelease < 3f) cycles++;
                        if (now - lastLower < 3f) downUp++;
                        break;
                    case St.Prepare:
                        if (walking) { Release(now, enemyInRange); break; }
                        if (t >= PrepareLen * 0.97f) { st = St.Aim; t = 0f; }
                        break;
                    case St.Aim:
                        if (walking) { Release(now, enemyInRange); break; }
                        if (threat) lastEngaged = now;
                        if (!threat && now - lastEngaged > Linger) { st = St.Up; lowers++; lastLower = now; }
                        break;
                    case St.Fire:
                        if (walking) { Release(now, enemyInRange); break; }
                        if (t >= FireLen * 0.96f) { st = St.Aim; t = 0f; }
                        break;
                }
                if (rocket)
                {
                    shown++;
                    if (st == St.Prepare && t < PrepareLen * 0.85f)
                    {   // UnitAim holds it while the tube still comes down (BS01Stance.HoldsFire), at most min(0.8 s, 0.6 x its flight); the tube is down at 0.7 of the clip
                        held++;
                        if (t + Math.Min(0.8f, 0.6f * flight) < PrepareLen * 0.7f) early++;
                    }
                    if (st == St.Prepare || st == St.Aim || st == St.Fire) { st = St.Fire; t = 0f; } else unbraced++;
                }
            }

            void Release(float now, bool inr) { st = St.Up; relWalk++; if (inr) relInRange++; lastRelease = now; }

            static bool Predict(ZObject o, ZWorld w)
            {
                if (o == null || !o.CanAttack() || o.Owner == Team.Neutral || o.IsMoving) return false;
                if (o.AttackObject != null && !o.AttackObject.Destroyed) return true;
                foreach (var x in w.Objects)
                    if (x.IsUnit && !x.Destroyed && x.KillAt < 0 && x.Owner != Team.Neutral && x.Owner != o.Owner && o.CanAttackObject(x) && o.WithinAttackRadius(x)) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ the sim side of one unit: walking legs, reversals, walk-backs, creep, the DV11 aim
        /// <summary>Debug only (the headless runner's "legs"): every walking leg, null otherwise.</summary>
        internal static Action<string> TsfLog;

        sealed class TsfTrack
        {
            public int legs, legsAfterFirst, rev, revInRange, revChase, afterShot, creep, walkBacks, hops, rAgro, rHelp, rAttack, rReturn, rOther;
            public double px, pxAfterFirst, inRange, inRangeMoving, inRangeNoTarget, creepPx;
            public int engages, aims, reAims, reAimsSmall, reAimsWalk, lateEngages, reAimsOnTarget, keptOnTarget, keptCarried; public double reAimGap;
            int engTube = -1; double pendingAt = -1;
            public string who = "", otherMode = "";
            int lx = int.MinValue, ly, x0, y0, x1, y1; double lastT = -1, legEnd = -99, legStart, lastTarget = -99, firstShot = -1;
            bool inLeg, legInRange, legBack, legAgro, legHelp, legAttack, legFight, legAfterFirst, legAfterTarget;
            int pvx, pvy; bool pvInRange, havePrev;
            // DV11: the spot and time of the last tick with a target; the engagement under way began after this gap and step
            bool hadTarget; int aimX, aimY; double aimAt = double.NegativeInfinity, engGap = -1, engStep = -1, lastReady = -1;

            public void Tick(ZObject s, double t, bool inr, bool shot, bool fight, int tube = -1)
            {
                if (shot && firstShot < 0) firstShot = t;
                double dt = lastT < 0 ? 0 : t - lastT; lastT = t;
                if (fight && inr) inRange += dt;
                if (lx == int.MinValue) { lx = s.CenterX; ly = s.CenterY; }
                bool moved = s.CenterX != lx || s.CenterY != ly;
                if (moved)
                {
                    double d = Math.Sqrt((double)(s.CenterX - lx) * (s.CenterX - lx) + (double)(s.CenterY - ly) * (s.CenterY - ly));
                    if (fight)
                    {
                        px += d; if (inr) { inRangeMoving += dt; if (s.AttackObject == null) inRangeNoTarget += dt; }
                        if (firstShot >= 0) pxAfterFirst += d;
                    }
                    if (!inLeg || t - legEnd > 0.25)
                    {
                        if (inLeg) Close();
                        inLeg = true; x0 = lx; y0 = ly; legStart = t; legInRange = false;
                        legBack = legAgro = legHelp = legAttack = false; legFight = fight; legAfterFirst = firstShot >= 0;
                        legAfterTarget = fight && t - lastTarget < 1.5;
                        if (legAfterTarget) afterShot++;
                    }
                    x1 = s.CenterX; y1 = s.CenterY; legEnd = t; legInRange |= inr;
                    if (s.Waypoints.Count > 0)
                    {
                        var wp = s.Waypoints[0];
                        if (wp.mode == WpMode.Agro) { if (SsStage(s) == "Return") legBack = true; else if (wp.leash > 0) legHelp = true; else legAgro = true; }
                        else if (wp.mode == WpMode.Attack) legAttack = true;
                        else { otherMode = wp.mode + (wp.playerGiven ? " (player)" : "") + (wp.refId >= 0 ? " #" + wp.refId : ""); }
                    }
                }
                // the DV11 aim: a new engagement (a target after none), and each new 0.8 s aim the sim starts (ToughReadyAt)
                bool has = s.AttackObject != null;
                if (has && !hadTarget)
                {
                    engages++; engTube = tube;
                    engGap = double.IsNegativeInfinity(aimAt) ? -1 : t - aimAt;
                    engStep = double.IsNegativeInfinity(aimAt) ? -1 : Math.Sqrt((double)(s.CenterX - aimX) * (s.CenterX - aimX) + (double)(s.CenterY - aimY) * (s.CenterY - aimY));
                    if (s.Type == "tough" && fight && engGap > ZWorld.ToughCarrySeconds + 1e-9) { lateEngages++; pendingAt = t; }
                    TsfLog?.Invoke($"   {who} {t:0.00} engages #{s.AttackObject.Id} after {engGap:0.00} s without a target, {engStep:0.0} px from where it last aimed, drawn tube {(tube == 2 ? "on target" : tube == 1 ? "coming down" : tube == 0 ? "carried" : "-")}");
                }
                if (s.Type == "tough" && fight)
                {
                    double ra = s.ToughReadyAt;
                    if (ra > 0 && ra != lastReady)
                    {
                        aims++;
                        if (aims > 1)
                        {
                            reAims++; reAimGap += Math.Max(0, engGap); if (engTube == 2) reAimsOnTarget++;
                            if (engStep >= 0 && engStep <= 16) reAimsSmall++; else reAimsWalk++;
                            TsfLog?.Invoke($"   {who} {t:0.00} DV11 aim again: {engGap:0.00} s without a target, {engStep:0.0} px from where it last aimed");
                        }
                    }
                    if (ra > 0) pendingAt = -1;
                    else if (pendingAt >= 0 && t - pendingAt > 0.05) { if (engTube == 2) keptOnTarget++; else keptCarried++; pendingAt = -1; }   // an engagement after the carry window and no new aim (the trial rule only, tools/trial)
                    lastReady = ra;
                }
                if (has) { aimX = s.CenterX; aimY = s.CenterY; aimAt = t; lastTarget = t; }
                hadTarget = has;
                lx = s.CenterX; ly = s.CenterY;
            }

            public void Close()
            {
                if (!inLeg) return;
                inLeg = false;
                if (!legFight) return;
                legs++; if (legAfterFirst) legsAfterFirst++;
                if (legBack) { rReturn++; walkBacks++; } else if (legHelp) rHelp++; else if (legAgro) rAgro++; else if (legAttack) rAttack++; else rOther++;
                int vx = x1 - x0, vy = y1 - y0; double m = Math.Sqrt((double)vx * vx + (double)vy * vy);
                if (legAfterTarget && (legAgro || legHelp) && !legAttack) { creep++; creepPx += m; }
                if (legAfterFirst && m >= 4 && m < 16) hops++;
                bool reversal = false;
                if (m >= 3)
                {
                    if (havePrev)
                    {
                        double c = (vx * (double)pvx + vy * (double)pvy) / (m * Math.Sqrt((double)pvx * pvx + (double)pvy * pvy));
                        if (c < -0.5) { rev++; reversal = true; if (legInRange || pvInRange) revInRange++; if (legAgro || legHelp || legBack) revChase++; }
                    }
                    pvx = vx; pvy = vy; pvInRange = legInRange; havePrev = true;
                }
                TsfLog?.Invoke($"   {who} {legStart:0.00}-{legEnd:0.00} {m,5:0.0} px {(legBack ? "agro walk-back" : legHelp ? "help call" : legAgro ? "agro" : legAttack ? "attack approach" : "other: " + otherMode)}" +
                               $"{(legInRange ? ", enemy in range" : "")}{(legAfterTarget ? ", within 1.5 s of a target" : "")}{(reversal ? ", REVERSAL" : "")}");
            }
        }

        // ------------------------------------------------------------------ one staged fight
        static readonly FieldInfo TsfTubeKeptF = typeof(ZWorld).GetField("ToughTubeKept", BindingFlags.Public | BindingFlags.Static);
        static int TsfTubeKept() => TsfTubeKeptF == null ? 0 : Convert.ToInt32(TsfTubeKeptF.GetValue(null));

        /// <summary>Sets a static of the rules by name for a band of numbers (headless runner only): "Name=value".</summary>
        internal static void TsfSet(string kv)
        {
            var p = kv.Split('='); if (p.Length != 2) return;
            foreach (var ty in new[] { typeof(ZWorld), typeof(ZPathing), typeof(SniperStanceGate) })
            {
                var f = ty.GetField(p[0], BindingFlags.Public | BindingFlags.Static);
                if (f == null || f.IsLiteral || f.IsInitOnly) continue;
                f.SetValue(null, f.FieldType == typeof(bool) ? (object)(p[1] == "1" || p[1] == "true") : Convert.ChangeType(p[1], f.FieldType, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            throw new ArgumentException("no such switch " + p[0]);
        }

        /// <summary>Extra switches for every fight (headless runner only: the band of numbers), "Name=value,Name=value".</summary>
        internal static string TsfExtra = "";

        /// <summary>One staged fight of <paramref name="c"/> with DV123 on or off (DV120's stage, see the class summary). Returns the
        /// counters per robot type of the shooters; "world" = a fingerprint of every object every tick.</summary>
        internal static Dictionary<string, TsfCounts> TsfFight(TsfCase c, bool on, int gx, int seed)
        {
            var res = new Dictionary<string, TsfCounts>();
            using (new SsSaved())
            {
                SsGameSwitches();
                ZWorld.SniperSteadyDeviation = true; SniperStanceGate.On = true;                // DV120 and its stance rules: the game as it is
                ZWorld.ToughSteadyDeviation = on;
                if (!string.IsNullOrEmpty(TsfExtra)) foreach (var kv in TsfExtra.Split(',')) TsfSet(kv);
                var w = ZWorld.Create(SettingsText, SsNoUnits(MapText("L02")), seed);
                w.Bots.Clear();
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                { var d = w.CreateObject("robot", "grunt", x, 16, tm); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }   // keep-alive grunts far away (the match never ends)
                for (int k = 0, n = (seed * 5 + gx / 10) % 9; k < n; k++)
                { var d = w.CreateObject("robot", "grunt", 48 + 16 * k, 16, Team.Red); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }   // as DV120's stage: the staged units' ids differ from fight to fight
                int bx = 41 * 16, by = 57 * 16;
                foreach (var o in w.Objects) if (o.Kind == ZKind.MapItem && o.Type == "grenades" && Math.Abs(o.CenterX - bx) < 400 && Math.Abs(o.CenterY - by) < 400) o.KillAt = 0.01;
                while (w.Time < 30.0) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var leaders = new List<ZObject>(); var sn = new List<ZObject>();
                foreach (var q in c.squads)
                {
                    var l = w.CreateRobotGroup(q.type, bx + q.dx, by + q.dy, Team.Red, q.amount);
                    leaders.Add(l); sn.Add(l); sn.AddRange(l.Minions);
                }
                var g1 = w.CreateRobotGroup("grunt", bx + gx, by - 40, Team.Blue);
                var g2 = w.CreateRobotGroup("grunt", bx + gx + 15, by + 40, Team.Blue);
                var gr = new List<ZObject> { g1 }; gr.AddRange(g1.Minions); gr.Add(g2); gr.AddRange(g2.Minions);
                var ids = new HashSet<int>(); foreach (var s in sn) ids.Add(s.Id);
                var brace = new Dictionary<int, TsfBrace>(); var tr = new Dictionary<int, TsfTrack>(); var kills = new Dictionary<int, int>(); var shots = new Dictionary<int, int>();
                foreach (var s in sn)
                {
                    tr[s.Id] = new TsfTrack { who = $"{s.Type}#{s.Id}" }; kills[s.Id] = shots[s.Id] = 0;
                    if (s.Type == "tough") brace[s.Id] = new TsfBrace();
                }
                int kept0 = TsfTubeKept(), push0 = ZWorld.SniperPushesHeld;
                double t0 = w.Time, end = -1, stop = t0 + 40.0; bool ordered = false, lost = false;
                ulong trace = 1469598103934665603UL, world = 1469598103934665603UL;
                while (w.Time < stop)
                {
                    if (!ordered)
                    {
                        ordered = true;
                        foreach (var l in leaders) if (!l.Destroyed) w.SendWaypoints(Team.Red, l, new[] { new ZWaypoint { mode = WpMode.Attack, refId = g1.Id, x = g1.CenterX, y = g1.CenterY, playerGiven = true } });
                    }
                    w.Tick(ZConst.TICK);
                    var shotBy = new HashSet<int>(); var rocketBy = new HashSet<int>(); var flightOf = new Dictionary<int, float>();
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Death)
                        {
                            var v = w.GetAny(e.objId);
                            if (v != null && gr.Contains(v) && ids.Contains(e.otherId)) kills[e.otherId]++;
                            continue;
                        }
                        if (!ids.Contains(e.objId)) continue;
                        if (e.type == ZEventType.Hitscan || e.type == ZEventType.ElectroBolt) { shotBy.Add(e.objId); shots[e.objId]++; }
                        else if (e.type == ZEventType.MissileFired && !e.flag) { shotBy.Add(e.objId); rocketBy.Add(e.objId); flightOf[e.objId] = e.value; shots[e.objId]++; }   // flag = a grenade
                    }
                    bool fight = end < 0;
                    foreach (var s in sn)
                    {
                        if (s.Destroyed || s.KillAt >= 0) continue;
                        bool inr = false;
                        foreach (var o in w.Objects)
                            if (o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != s.Owner && s.CanAttackObject(o) && s.WithinAttackRadius(o)) { inr = true; break; }
                        brace.TryGetValue(s.Id, out var bv); if (bv != null) bv.Frame(s, w, (float)w.Time, (float)ZConst.TICK, rocketBy.Contains(s.Id), inr && fight, flightOf.TryGetValue(s.Id, out var fl) ? fl : 1f);
                        tr[s.Id].Tick(s, w.Time, inr, shotBy.Contains(s.Id), fight, bv != null ? bv.Tube : -1);
                        trace = SnsFnv(SnsFnv(SnsFnv(trace, s.LocX), s.LocY), s.AttackObject != null ? s.AttackObject.Id : -1);
                    }
                    world = SnsFnv(world, w.Objects.Count);
                    foreach (var o in w.Objects)
                    {
                        world = SnsFnv(SnsFnv(SnsFnv(world, o.Id), o.LocX), o.LocY);
                        world = SnsFnv(SnsFnv(world, o.Health), o.AttackObject != null ? o.AttackObject.Id : -1);
                        world = SnsFnv(world, o.Waypoints.Count > 0 ? (long)o.Waypoints[0].mode * 100000 + o.Waypoints[0].refId : -1);
                    }
                    w.Events.Clear();
                    if (end < 0 && gr.TrueForAll(o => o.Destroyed)) { end = w.Time; stop = Math.Min(stop, end + 4.0); }
                    if (end < 0 && sn.TrueForAll(o => o.Destroyed || o.KillAt >= 0)) { end = w.Time; lost = true; stop = Math.Min(stop, end + 0.5); }
                }
                bool unfinished = end < 0; if (unfinished) end = w.Time;
                int killed = 0; foreach (var g in gr) if (g.Destroyed) killed++;
                var types = new List<string>(); foreach (var s in sn) if (!types.Contains(s.Type)) types.Add(s.Type);
                foreach (var ty in types)
                {
                    var r = new TsfCounts { fights = 1, unfinished = unfinished ? 1 : 0, lost = lost ? 1 : 0, fightSecs = end - t0, grunts = killed, trace = trace, world = world };
                    if (ty == "tough") { r.tubeKept = TsfTubeKept() - kept0; r.pushesHeld = ZWorld.SniperPushesHeld - push0; }
                    foreach (var s in sn)
                    {
                        if (s.Type != ty) continue;
                        r.units++; if (s.Destroyed || s.KillAt >= 0) r.died++;
                        var k = tr[s.Id]; k.Close();
                        r.legs += k.legs; r.legsAfterFirst += k.legsAfterFirst; r.rev += k.rev; r.revInRange += k.revInRange; r.revChase += k.revChase; r.afterShot += k.afterShot; r.creep += k.creep; r.creepPx += k.creepPx;
                        r.hops += k.hops; r.walkBacks += k.walkBacks; r.rAgro += k.rAgro; r.rHelp += k.rHelp; r.rAttack += k.rAttack; r.rReturn += k.rReturn; r.rOther += k.rOther;
                        r.px += k.px; r.pxAfterFirst += k.pxAfterFirst; r.inRange += k.inRange; r.inRangeMoving += k.inRangeMoving; r.inRangeNoTarget += k.inRangeNoTarget;
                        r.kills += kills[s.Id]; r.shots += shots[s.Id];
                        if (ty == "tough") { r.engages += k.engages; r.aims += k.aims; r.reAims += k.reAims; r.reAimsSmall += k.reAimsSmall; r.reAimsWalk += k.reAimsWalk; r.reAimGap += k.reAimGap; r.lateEngages += k.lateEngages; r.reAimsOnTarget += k.reAimsOnTarget; r.keptOnTarget += k.keptOnTarget; r.keptCarried += k.keptCarried; }
                        if (brace.TryGetValue(s.Id, out var bv))
                        { r.bPrep += bv.prepares; r.bRelWalk += bv.relWalk; r.bRelInRange += bv.relInRange; r.bCycles += bv.cycles; r.bLowers += bv.lowers; r.bDownUp += bv.downUp; r.bShown += bv.shown; r.bUnbraced += bv.unbraced; r.bHeld += bv.held; r.bEarly += bv.early; }
                    }
                    res[ty] = r;
                }
            }
            return res;
        }

        internal static readonly int[] TsfGx = { 150, 160, 180 };

        /// <summary>The case at the three distances for seeds <paramref name="s0"/>..<paramref name="s1"/> (seeds 1-6 = 18 fights, DV120's check).</summary>
        internal static Dictionary<string, TsfCounts> TsfMatrix(TsfCase c, bool on, int s0, int s1)
        {
            var all = new Dictionary<string, TsfCounts>();
            foreach (var gx in TsfGx)
                for (int s = s0; s <= s1; s++)
                    foreach (var kv in TsfFight(c, on, gx, s))
                    {
                        if (!all.TryGetValue(kv.Key, out var a)) all[kv.Key] = a = new TsfCounts();
                        a.Add(kv.Value);
                    }
            return all;
        }

        // ------------------------------------------------------------------ the counted check
        /// <summary>The K306 counted check: every case at 3 distances x seeds s0..s1, DV123 off (the game as before) and on.</summary>
        internal static string TsfReport(int s0, int s1, out bool all)
        {
            var sb = new StringBuilder(); bool ok_ = true;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); ok_ &= ok; }
            bool d0 = ZWorld.ToughSteadyDeviation, r0 = ZWorld.SniperSteadyDeviation, g0 = SniperStanceGate.On, p0 = ZWorld.ToughPrepareDeviation;
            int n = TsfGx.Length * (s1 - s0 + 1);
            sb.AppendLine($"K306 / DV123: {n} staged fights a case (3 distances x seeds {s0}-{s1}), DV123 off (before) and on (after); hold {ZWorld.ToughHoldSecs:0.0#} s, stand band {ZWorld.SniperStandBand} px, edge band {ZWorld.SniperEdgeBand} px; DV11 (0.8 s aim) as it is");
            var cases = TsfCases();
            var got = new Dictionary<string, (Dictionary<string, TsfCounts> off, Dictionary<string, TsfCounts> on)>();
            foreach (var c in cases)
            {
                var off = TsfMatrix(c, false, s0, s1); var on = TsfMatrix(c, true, s0, s1);
                got[c.name] = (off, on);
                sb.AppendLine($"CASE {c.name}: {c.what}");
                foreach (var kv in off) { sb.AppendLine($"  before {kv.Key,-10}: {kv.Value.Text(kv.Key)}"); sb.AppendLine($"  after  {kv.Key,-10}: {on[kv.Key].Text(kv.Key)}"); }
            }
            foreach (var c in cases)
            {
                var (off, on) = got[c.name];
                if (c.control)
                {
                    bool same = true; foreach (var kv in off) same &= on.TryGetValue(kv.Key, out var o) && o.world == kv.Value.world && o.Text(kv.Key) == kv.Value.Text(kv.Key);
                    V(same, $"C {c.name}: DV123 changes nothing for {c.what}: every object the same every tick");
                    continue;
                }
                var b = off["tough"]; var a = on["tough"];
                V(a.rev == 0 && a.revInRange == 0, $"T1 {c.name}: no turn-round: {a.rev} reversals ({a.revInRange} with an enemy in range); before {b.rev} ({b.revInRange})");
                V(a.walkBacks == 0, $"T2 {c.name}: no walk-back on a dodging target: {a.walkBacks}; before {b.walkBacks}");
                V(a.creep < b.creep || (a.creep == 0 && b.creep == 0), $"T3 {c.name}: fewer creep steps after a kill: {a.creep}; before {b.creep}");
                sb.AppendLine($"      (short steps of 0.5-2 m after the first shot: {a.hops}, before {b.hops}; walking legs {a.legs}, before {b.legs}; s walking with an enemy in range and no target {a.inRangeNoTarget:0.0}, before {b.inRangeNoTarget:0.0})");
                V(a.bCycles < b.bCycles || (a.bCycles == 0 && b.bCycles == 0), $"T5 {c.name}: fewer brace-let go-brace cycles: {a.bCycles}; before {b.bCycles} (tube lowered-braced within 3 s {a.bDownUp}, before {b.bDownUp})");
                V(a.reAimsOnTarget == 0 && a.reAimsSmall <= b.reAimsSmall, $"T6 {c.name}: the 0.8 s first-rocket aim (DV11) never with the drawn tube already on target ({a.reAimsOnTarget}; before {b.reAimsOnTarget}), and not more often after a step of 2 m or less: {a.reAimsSmall}; before {b.reAimsSmall}");
                sb.AppendLine($"      (all 0.8 s aims again: {a.reAims}, before {b.reAims}; after a walk of more than 2 m {a.reAimsWalk}, before {b.reAimsWalk}: each one the drawn brace coming back after a walk)");
                V(a.fightSecs <= b.fightSecs * 1.10, $"T7 {c.name}: fights as fast: {a.fightSecs:0.0} s, before {b.fightSecs:0.0} s ({100 * (a.fightSecs / Math.Max(0.01, b.fightSecs) - 1):+0.0;-0.0} %, at most +10 %)");
                V(a.unfinished <= b.unfinished && a.lost <= b.lost && a.grunts >= b.grunts, $"T8 {c.name}: every fight as won: {a.grunts} grunts killed, {a.unfinished} not finished, {a.lost} lost; before {b.grunts}, {b.unfinished}, {b.lost}");
                V(a.bUnbraced <= b.bUnbraced, $"T9 {c.name}: no more rockets from the carry: {a.bUnbraced} of {a.bShown} not braced; before {b.bUnbraced} of {b.bShown} (held on screen for the brace {a.bHeld}, before {b.bHeld})");
                if (on.TryGetValue("electrobot", out var ea) && off.TryGetValue("electrobot", out var eb))
                    V(ea.revChase == 0 && ea.walkBacks == 0, $"T10 {c.name}: the Electrobots beside them still stand (DV83): {ea.revChase} turn-rounds on a chase, {ea.walkBacks} walk-backs; before {eb.revChase}, {eb.walkBacks} (all reversals, with the team's spacing steps: {ea.rev}, before {eb.rev})");
            }
            var again = TsfMatrix(cases[1], true, s0, s0);
            var first = TsfMatrix(cases[1], true, s0, s0);
            V(again["tough"].world == first["tough"].world && again["tough"].Text("tough") == first["tough"].Text("tough"), "D the same fights twice: every object the same every tick (deterministic)");
            V(ZWorld.ToughSteadyDeviation == d0 && ZWorld.SniperSteadyDeviation == r0 && SniperStanceGate.On == g0 && ZWorld.ToughPrepareDeviation == p0, "S the switches are put back after the cases");
            all = ok_;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }

        static Deviation DevToughSteady()
        {
            string measured; bool pass;
            try
            {
                var c = TsfCases()[1];                                                         // 2 Toughs, the game's squad
                var off = TsfMatrix(c, false, 1, 2)["tough"]; var on = TsfMatrix(c, true, 1, 2)["tough"];
                pass = on.rev == 0 && on.walkBacks == 0 && on.creep <= off.creep && on.bCycles <= off.bCycles && on.reAimsOnTarget == 0 && on.reAimsSmall <= off.reAimsSmall && on.bUnbraced <= off.bUnbraced && on.unfinished <= off.unfinished && on.fightSecs <= off.fightSecs * 1.10;
                measured = $"{on.fights} staged fights of 2 Toughs against 6 grunts; rule off: {off.rev} turn-rounds, {off.walkBacks} walk-backs, {off.creep} creep steps, {off.bCycles} brace cycles, {off.bUnbraced} rockets not braced, {off.reAims} 0.8 s aims again ({off.reAimsSmall} after a step of 2 m or less), {off.fightSecs:0.0} s of fighting; " +
                           $"rule on: {on.rev} turn-rounds, {on.walkBacks} walk-backs, {on.creep} creep steps, {on.bCycles} brace cycles, {on.bUnbraced} rockets not braced, {on.reAims} 0.8 s aims again ({on.reAimsSmall} after a step of 2 m or less; with the drawn tube already on target {on.reAimsOnTarget}), {on.fightSecs:0.0} s";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV123", change = "Toughs stand and fire: no walk past an enemy in range, no walk back, fewer small steps after a kill",
                zod = "a walking robot neither fires nor takes a new target (zobject.cpp:1615); an agro walk returns to where it began when its target leaves the leash (zobject.cpp:2233-2328); grunts step aside from rockets",
                openz = "Sim/ZObject.ToughSteady.cs: DV120's steady rule (Sim/ZObject.SniperSteady.cs) for a Tough on foot: it stops for any enemy unit it can hit one tile inside its range, an agro walk never turns back, " +
                        $"while its squad holds a target or fired in the last {ZWorld.ToughHoldSecs:0.0#} s and an enemy stands within {ZWorld.SniperEdgeBand} px outside its range it keeps its spot, a firing Tough is not pushed by its own robots; " +
                        "DV11's 0.8 s first-rocket aim is unchanged (it comes when the drawn tube has to come back onto a target). The player's own orders are never held (DV16)",
                measured = measured,
                toggle = "ZWorld.ToughSteadyDeviation: View/ZGame.cs sets it at every start (off in 'Zod exact'); off in headless worlds and in the version 1.1 counted checks unless they set it",
                status = pass ? DevActive : DevCheck,
            };
        }
    }

#if UNITY_EDITOR
    /// <summary>K306 menus: the counted check (outside Play), the old / new rules switch and the counters in Play.</summary>
    public static class OzV11ToughSteadyCheck
    {
        [UnityEditor.MenuItem("OpenZ/V1.1/Tough: K306 Steady Fire Check (18 fights a case)")]
        static void Menu() => OzV11Kit.RunMenu("Tough: K306 Steady Fire Check (18 fights a case)", () => OpenZParity.TsfReport(1, 6, out _));

        /// <summary>A/B look in Play: the Toughs' rules of before (DV123 off) or K306's; the next stage shows them.</summary>
        [UnityEditor.MenuItem("OpenZ/V1.1/Tough: K306 Old or new Tough rules (Play, A-B)")]
        static void Toggle()
        {
            ZWorld.ToughSteadyDeviation = !ZWorld.ToughSteadyDeviation;
            UnityEngine.Debug.Log($"[OpenZ V1.1] K306: Toughs now play by the {(ZWorld.ToughSteadyDeviation ? "NEW rules (DV123: stand and fire)" : "OLD rules (as before K306; for comparison only)")}. " +
                                  "The next Play session starts with the new rules again.");
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Tough: K306 Report (Play)")]
        static void Counters()
        {
            var w = ZGame.I ? ZGame.I.World : null;
            UnityEngine.Debug.Log($"[OpenZ V1.1] K306: DV123 {(ZWorld.ToughSteadyDeviation ? "ON" : "off")} (since the last stage; the walk counts are DV120's, snipers and Toughs together)\n" +
                                  $"  agro walks ended by an enemy in range {ZWorld.SniperAgroEnded}, attacks held {ZWorld.SniperAttackHeld}, spot kept for an enemy at the edge {ZWorld.SniperKeptSpot} ticks, positions kept {ZWorld.SniperHeldPosition} checks, " +
                                  $"walk-backs not walked {ZWorld.SniperNoReturn}, close-in ticks {ZWorld.SniperCloseIns}, pushes not taken while firing {ZWorld.SniperPushesHeld}, stand ticks {(w != null ? w.SniperStands : 0)}");
        }
    }

    /// <summary>K306: DV120's clean stage with the game's squad of 2 Toughs; K322: its place by K248c's tests (OzSniperStagePlace: dry open
    /// ground where the units stand and walk with clear lines of fire, no flag, crate or empty vehicle in auto-grab reach of a leader that
    /// can stand idle, no other unit or fort in reach) with the Toughs' own walk and K322's clear view (OzClipStagePlace, Kind.Tough), near
    /// the middle of the screen, else the level's known places (the camera flies there); the clean clip stage first (OzClipStage.Start:
    /// the computer players out, every factory stopped, the units that are not the stage's taken away, held so for the take); both grunt
    /// squads 150 px east (OzSniperStagePlace.G1X / G2X, inside a grunt's agro reach). The stage's own units of a run before are taken
    /// away first; the Toughs are ordered to attack the first grunt squad's leader (the player's order). Counters start again at each stage.</summary>
    public static class OzV11ToughSteadyStage
    {
        static readonly List<int> staged = new List<int>();

        [UnityEditor.MenuItem("OpenZ/V1.1/Tough: K306 Stage 2 Toughs vs grunts (Play)")]
        static void Menu() => Run();

        /// <summary>Always true (staged, or why not in the console).</summary>
        public static bool Run()
        {
            var g = ZGame.I;
            if (!UnityEngine.Application.isPlaying || g == null || g.World == null) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K306 stage: start a level in Play first (Level 2, Desert)."); return true; }
            var w = g.World; var cam = UnityEngine.Camera.main;
            if (!cam) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K306 stage: no main camera."); return true; }
            int removed = 0;
            foreach (var id in staged) { var o = w.GetAny(id); if (o != null && !o.Destroyed && o.KillAt < 0) { o.KillAt = w.Time; removed++; } }
            staged.Clear();
            var ray = cam.ViewportPointToRay(new UnityEngine.Vector3(0.5f, 0.5f, 0f));
            var c = cam.transform.position; var plane = new UnityEngine.Plane(UnityEngine.Vector3.up, new UnityEngine.Vector3(0f, g.GroundY(c.x, c.z), 0f));
            if (!plane.Raycast(ray, out float dist)) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K306 stage: the camera does not look at the ground."); return true; }
            var px = ZGame.WorldToPx(ray.GetPoint(dist));
            // K322: the clean clip stage, then the place by K248c's tests with the Toughs' walk and the clear view (near the middle of the screen, else the level's known places)
            if (!OzClipStage.Start(g, px.x, px.y, "K306 Tough stage")) return true;
            if (!OzClipStagePlace.Find(OzClipStagePlace.Kind.Tough, w, (px.x >> 4) - 5, px.y >> 4, 14, g.player, out int stx, out int sty, out _, out _, out string placeNote))
            { UnityEngine.Debug.LogWarning($"[OpenZ V1.1] K306 stage: no place for a clean fight near the middle of the screen or at this level's known places: {placeNote}.{(removed > 0 ? $" ({removed} units of the stage before were taken away.)" : "")}"); return true; }
            int bx = stx * 16, by = sty * 16;
            if (Math.Abs(bx + 83 - px.x) > 64 || Math.Abs(by + 8 - px.y) > 64) { var rts = UnityEngine.Object.FindFirstObjectByType<RtsCamera>(); if (rts) rts.FlyTo(ZGame.PxToWorld(bx + 83, by + 8)); }   // K322: the place is off the middle of the screen: the camera flies there (same zoom)
            var enemy = g.player == Team.Red ? Team.Blue : Team.Red;
            var toughs = w.CreateRobotGroup("tough", bx, by, g.player);
            var g1 = w.CreateRobotGroup("grunt", bx + OzSniperStagePlace.G1X, by + OzSniperStagePlace.G1Y, enemy);
            var g2 = w.CreateRobotGroup("grunt", bx + OzSniperStagePlace.G2X, by + OzSniperStagePlace.G2Y, enemy);   // K322: 150 px east like the first (was 165: idle out of its agro reach it walks off; K248c's finding)
            foreach (var o in new[] { toughs, g1, g2 }) if (o != null) { staged.Add(o.Id); foreach (var m in o.Minions) staged.Add(m.Id); }
            OzClipStage.Stage(g, "K306 Tough stage", toughs, g1, g2);   // K322: the staged units' views (ZGame.SpawnUnitView, as for the game's own units made in Play), checked; the hold keeps them
            ZWorld.SniperAgroEnded = ZWorld.SniperAttackHeld = ZWorld.SniperKeptSpot = ZWorld.SniperHeldPosition = ZWorld.SniperNoReturn = ZWorld.SniperCloseIns = ZWorld.SniperPushesHeld = 0;
            if (toughs != null && g1 != null) w.SendWaypoints(g.player, toughs, new[] { new ZWaypoint { mode = WpMode.Attack, refId = g1.Id, x = g1.CenterX, y = g1.CenterY, playerGiven = true } });
            UnityEngine.Debug.Log($"[OpenZ V1.1] K306 stage: {(toughs != null ? 1 + toughs.Minions.Count : 0)} Toughs of {g.player} at tile {bx >> 4},{by >> 4} on dry open ground, two {enemy} grunt squads 9 tiles east (K322 place: {placeNote}); " +
                                  $"ordered to attack the first squad; DV123 {(ZWorld.ToughSteadyDeviation ? "ON" : "off")}{(removed > 0 ? $"; {removed} units of the stage before taken away" : "")}. " +
                                  "After the fight: menu OpenZ/V1.1/Tough: K306 Report (Play).");
            return true;
        }

        // K322: the place test is OzClipStagePlace.Check (Kind.Tough, OpenZParity.V11ClipStage.cs: K248c's tests with the Toughs' walk and the
        // clear view); K248b's old test here (Fits: 260 px round two points, the whole box dry) found no place on Level 2 (run 27B)
    }
#endif
}
#endif
