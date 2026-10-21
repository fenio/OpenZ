using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shuttle's big blast (rule DV118, view only: the kill, the damage and the crew's deaths are the rules' as before). Shot down in
    /// the air the ship used to burst with the light tank's small table blast and no blast sound of its own: from the RTS camera it read
    /// as an egg cracking and a small puff. Now every AIR death (burst, split and the rifles' crack alike) blows the hull apart with the
    /// game's own big explosion:
    /// <list type="bullet">
    /// <item>the second explosion type's BIG look (OpenZFxPlayer.Exp2, Exp2Kind.Big: white core, orange billows, red halo, flash,
    /// starburst spikes, hundreds of sparks, embers, metal debris, maroon smoke, its orange light) at the hull's middle, sized for the
    /// ship (U <see cref="BlastUBurst"/> / <see cref="BlastUSplit"/> / <see cref="BlastUCrack"/> m, a fort-class blast; the heavy tank's
    /// is 4.8), in its AIR form (OpenZFxPlayer.Exp2Air: no ground light, fire ring, rolling billows, burning secondaries or flat ring on
    /// the ground under it);</item>
    /// <item>under it the game's table blast of a big vehicle, scaled to the ship (<see cref="BlastS"/>, the heavy tank's 2.1): its dark
    /// air-burst smoke, sparks, embers and fire clouds round it (the air-burst gate keeps it off the ground);</item>
    /// <item>its sounds: the heavy vehicle death boom (the "vdeath" heavy bank, as every big vehicle's death) and, <see cref="RumbleDelay"/>
    /// s later, the deep rumble layer of the big shells' impacts (the heavy bank pitched to <see cref="RumblePitch"/>, as OpenZHeavyShells),
    /// plus the big explosion's own fire roar, debris rain and shrapnel tinks;</item>
    /// <item>one short camera kick (the howitzer impact's <see cref="KickPx"/> px, through OpenZFxShake.Kick: the SCREEN SHAKE and CALM
    /// SCREEN settings decide, the gate's cooldown does not hold it back (as a building's collapse); none off screen);</item>
    /// <item>then <see cref="SecondariesBurst"/> (split 3, crack 2) small burning pockets bursting round the fireball in its first half second (exp2 Small, air
    /// form, each with the satellite boom of the medium explosion) and a dark smoke column rising from the blast for
    /// <see cref="ColumnSec"/> s (beside the break's dark cloud);</item>
    /// <item>the pieces start with the blast's impact (<see cref="BigThrow"/>): thrown out of it harder (body panels out x<see cref="BodyOutK"/>,
    /// domes / pods / ramp leaf x<see cref="HeavyOutK"/>, legs x<see cref="LightOutK"/>, the crack also x<see cref="CrackOutK"/>), spinning
    /// x<see cref="SpinK"/> (the 14 rad/s cap kept), the split's own out-throw x<see cref="SplitOutK"/> (its second blast still splits it wider): the landing field about 1.55-1.9 x as wide (the kit's twin); the crew's bodies are thrown
    /// with them x<see cref="CrewK"/> (BoardingView.ShuttleCrewLost's hook). The pieces' look (K256k), their rest and solid rules (K256h2,
    /// K331b / K331d) are not touched.</item>
    /// </list>
    /// Destroyed on the GROUND the ship now plays the same big explosion in its ground form every time, as the big vehicles' deaths do
    /// (before: the light tank's blast, the medium exp2 at 45 %), with the same boom and rumble, its wreck fire and scorch.
    /// Switch: <see cref="BigBlast"/> (off = the old small blasts and throw, an error in the log once).
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>Switch: the big blast and its throw (on).</summary>
        public static bool BigBlast = true;
        /// <summary>The big explosion's size unit (m) per air break, on the ground; the table blast's strength and LEG radius under it.</summary>
        public static float BlastUBurst = 6.2f, BlastUSplit = 6.8f, BlastUCrack = 5.6f, GroundU = 5.6f, BlastS = 2.4f, BlastLegRadius = 6f;
        /// <summary>The boom (x the heavy bank's gain, its pitch), the rumble (x the heavy bank's gain, its pitch, its delay s).</summary>
        public static float BoomGain = 1f, BoomPitch = 0.88f, RumbleGain = 0.8f, RumblePitch = 0.72f, RumbleDelay = 0.12f;
        /// <summary>The camera kick (px at the 1080 px reference, the howitzer impact's).</summary>
        public static float KickPx = OpenZFxCatalog.ShakeHowitzerImpactPx;
        /// <summary>The small burning pockets: how many per break (burst, split, crack), when (s), their size (x U), how far out (x U).</summary>
        public static int SecondariesBurst = 2, SecondariesSplit = 3, SecondariesCrack = 2;
        public static float SecondaryFrom = 0.12f, SecondaryTo = 0.45f, SecondaryU = 0.36f, SecondaryOut0 = 0.45f, SecondaryOut1 = 0.85f;
        /// <summary>The smoke column: how long (s), how often it puffs (s), how fast it climbs (m/s).</summary>
        public static float ColumnSec = 4.5f, ColumnEvery = 0.09f, ColumnRise = 1.6f;
        /// <summary>The throw out of the blast (air breaks only): out / up per kind, the crack's extra, the spin; the crew's throw.</summary>
        public static float BodyOutK = 2.3f, BodyUpK = 1.9f, HeavyOutK = 2.0f, HeavyUpK = 1.45f, LightOutK = 1.8f, LightUpK = 1.3f, CrackOutK = 1.3f, CrackUpK = 1.2f, SpinK = 1.6f, CrewK = 1.6f;
        /// <summary>A heavy shell's split: its own out-throw x this (its second blast, unchanged, then splits the field: kept within range).</summary>
        public static float SplitOutK = 1.15f;   // K419a: was 0.9: the split's launch median 6.5 m/s or more, the farthest piece inside 50 m (tools/twin_split_launch.py)

        // ------------------------------------------------------------------ counts (the Play proof reads them)
        public static int BigAirBlasts, BigGroundBlasts, BigExp2Played, BigBooms, BigRumbles, BigKicks, BigSecondaries, BigColumnPuffs, BigThrows, BigFallbacks;
        /// <summary>The last break's big blast: the exp2 kind played ("Big air", "Big ground", "-" = none), its size unit, where; its boom /
        /// rumble heard (CombatAudio played it), the kick given; the pieces' launch out of the blast (relative to the ship's own velocity,
        /// m/s: the slowest, the median, the fastest, the body panels' slowest), their spin (rad/s, median), the crew's throw factor.</summary>
        public static string LastBigKind = "-"; public static float LastBigU, LastBigTime = -1f; public static Vector3 LastBigAt, LastBreakVel;
        public static bool LastBoom, LastRumble, LastKick; public static int LastSecondaries;
        public static float LastLaunchMin, LastLaunchMedian, LastLaunchMax, LastBodyLaunchMin, LastSpinMedian, LastCrewK = 1f;
        public static string LastLaunchText = "-";
        static bool offSaid;

        static LegacyMatch.BlastSpec ShipBlast() => new LegacyMatch.BlastSpec
        {
            label = "playable shuttle", kind = FxBlastKind.Vehicle, s = BlastS, legRadius = BlastLegRadius, key = "vdeath",
            clips = LegacyMatch.BankHeavy, gain = LegacyMatch.GainHeavy, pitch = BoomPitch, wreck = 1.5f
        };

        static bool BigOn(out OpenZFxPlayer z)
        {
            z = OpenZFxPlayer.I;
            if (!BigBlast) { if (!offSaid) { offSaid = true; Debug.LogError("[OpenZ] shuttle big blast switched off: the old small blasts and throw"); } return false; }
            if (!z || !z.isActiveAndEnabled) { BigFallbacks++; return false; }
            return true;
        }

        // ------------------------------------------------------------------ the throw (Break's launch loop)
        /// <summary>Break's launch loop, after the crack's own numbers: an air break throws every piece out of the big blast.</summary>
        static void BigThrow(string how, bool air, string kind, ref float vo, ref float vu, ref float sp)
        {
            if (!BigBlast || !air || how == OzShuttleDeathPlan.Drop || how == OzShuttleDeathPlan.Ground) return;
            if (how == OzShuttleDeathPlan.Crack) { vo *= CrackOutK; vu *= CrackUpK; }
            else if (how == OzShuttleDeathPlan.Split) vo *= SplitOutK;
            switch (kind)
            {
                case "leg": case "shard": vo *= LightOutK; vu *= LightUpK; break;
                case "dome": case "engine": case "leaf": vo *= HeavyOutK; vu *= HeavyUpK; break;
                default: vo *= BodyOutK; vu *= BodyUpK; break;
            }
            sp *= SpinK; BigThrows++;
        }

        /// <summary>BoardingView.ShuttleCrewLost: the bodies' throw factor (the big blast throws them with the pieces in the air).</summary>
        public static float CrewThrowK(bool air) => BigBlast && air ? CrewK : 1f;

        // ------------------------------------------------------------------ the blasts (Break's blast line)
        /// <summary>An air break's blast (false = the old table blast plays). Called after the launch: it records the launch.</summary>
        static bool AirBigBlast(ZObject o, Group grp, Vector3 centre, FxManager fx, CombatAudio au, string how, Transform holder)
        {
            if (!BigOn(out var z)) return false;
            float U = how == OzShuttleDeathPlan.Split ? BlastUSplit : how == OzShuttleDeathPlan.Crack ? BlastUCrack : BlastUBurst;
            var spec = ShipBlast();
            var sh = z.Shake; LastKick = sh != null && sh.Kick(centre, KickPx, "shuttle_air_blast", true); if (LastKick) BigKicks++;   // first, past the gate's cooldown as a building's collapse (the killing shell's own kick may have just played; the two never add up: the bigger one plays); the blasts' own smaller kicks then wait at the gate
            LegacyMatch.DoBlast(centre, spec, o.Id, fx);                                                           // the table blast (air form: dark smoke, nothing on the ground)
            bool e2 = z.Exp2Air(centre, U, Exp2Kind.Big, o.Id * 613 + 29, spec.s, spec.kind, true);
            if (e2) BigExp2Played++;
            Booms(spec, centre, au, o.Id);
            int n = how == OzShuttleDeathPlan.Split ? SecondariesSplit : how == OzShuttleDeathPlan.Crack ? SecondariesCrack : SecondariesBurst;
            var after = holder ? holder.gameObject.AddComponent<BigBlastAfter>() : null;
            LastSecondariesFired = 0; LastColumnPuffs = 0;   // K419a: this blast's own counts (OzShuttleBreakup.PieceBand.cs)
            if (after) after.Begin(centre, U, n, o.Id, grp != null ? grp.vel : Vector3.zero);
            LastSecondaries = n;
            LastBigKind = e2 ? "Big air" : "-"; LastBigU = U; LastBigAt = centre; LastBigTime = Time.time; LastBreakVel = grp != null ? grp.vel : Vector3.zero; BigAirBlasts++;
            NoteLaunch(grp);
            Debug.Log($"[OpenZ] shuttle #{o.Id} big air blast ({how}): exp2 {(e2 ? "Big" : "NOT played")} U {U:F1} m at {centre}, table blast s {spec.s:F1}, boom {LastBoom}, kick {LastKick}, {n} burning pockets; launch {LastLaunchText}");
            return true;
        }

        /// <summary>A ground death's blast (false = the old vehicle death plays): the big explosion every time, its boom and rumble, the
        /// wreck fire, the metal rain.</summary>
        static bool GroundBigBlast(ZObject o, Vector3 p, FxManager fx, CombatAudio au)
        {
            if (!BigOn(out var z)) return false;
            var d = ShipBlast();
            FxManager.RaiseExploded(p, d.s / OpenZFxCatalog.LegacyToM12);
            bool e2 = z.Exp2(p + Vector3.up * 0.6f, GroundU, Exp2Kind.Big, o.Id * 977 + 5, d.s, d.kind, false, true);
            if (!e2) LegacyMatch.DoBlast(p + Vector3.up * 0.6f, d, o.Id, fx); else BigExp2Played++;
            if (d.wreck > 0f) z.WreckFire(p, d.wreck);
            LastKick = false;
            Booms(d, p, au, o.Id);
            OzSoundV3.DeathTail(o, p);
            LastBigKind = e2 ? "Big ground" : "-"; LastBigU = GroundU; LastBigAt = p; LastBigTime = Time.time; LastBreakVel = Vector3.zero; LastSecondaries = 0; BigGroundBlasts++;
            LastLaunchText = "-";
            return true;
        }

        /// <summary>The heavy vehicle death boom now, the big shells' deep rumble a moment later.</summary>
        static void Booms(LegacyMatch.BlastSpec d, Vector3 at, CombatAudio au, int id)
        {
            LastBoom = LastRumble = false;
            if (!au) return;
            int p0 = au.Played;
            au.PlayPitched(d.key, at, d.gain * BoomGain, 0f, -1, d.pitch, d.clips);
            LastBoom = au.Played > p0; if (LastBoom) BigBooms++;
            void Rumble()
            {
                var a = CombatAudio.I; if (!a) return;
                int q0 = a.Played;
                a.PlayPitched("heavy", at, LegacyMatch.GainHeavy * RumbleGain, 0f, -1, RumblePitch, LegacyMatch.BankHeavy);
                if (a.Played > q0) { LastRumble = true; BigRumbles++; }
            }
            var ad = AudioDirector.I;
            if (ad && RumbleDelay > 0f) ad.Schedule(RumbleDelay, Rumble); else Rumble();
        }

        /// <summary>The pieces' launch out of the blast (their velocity less the ship's own, right after the launch).</summary>
        static void NoteLaunch(Group grp)
        {
            var sp = new List<float>(); var spin = new List<float>(); float bodyMin = float.MaxValue;
            if (grp != null)
                foreach (var pc in grp.pieces)
                {
                    if (!pc || !pc.rb) continue;
                    float v = (pc.rb.linearVelocity - grp.vel).magnitude; sp.Add(v); spin.Add(pc.rb.angularVelocity.magnitude);
                    if (pc.Kind == "body") bodyMin = Mathf.Min(bodyMin, v);
                }
            if (sp.Count == 0) { LastLaunchText = "-"; LastLaunchMin = LastLaunchMedian = LastLaunchMax = LastBodyLaunchMin = LastSpinMedian = 0f; return; }
            sp.Sort(); spin.Sort();
            LastLaunchMin = sp[0]; LastLaunchMedian = sp[sp.Count / 2]; LastLaunchMax = sp[sp.Count - 1]; LastSpinMedian = spin[spin.Count / 2];
            LastBodyLaunchMin = bodyMin < float.MaxValue ? bodyMin : 0f; LastCrewK = CrewThrowK(true);
            LastLaunchText = $"{LastLaunchMin:F1} / {LastLaunchMedian:F1} / {LastLaunchMax:F1} m/s (slowest body panel {LastBodyLaunchMin:F1}), spin median {LastSpinMedian:F1} rad/s, {sp.Count} pieces";
        }

        // ------------------------------------------------------------------ the burning pockets and the smoke column after an air blast
        sealed class BigBlastAfter : MonoBehaviour
        {
            Vector3 c, drift; float U, t, colT; int id, si; readonly List<(float at, Vector3 pos)> secs = new List<(float, Vector3)>();

            internal void Begin(Vector3 centre, float u, int n, int shipId, Vector3 shipVel)
            {
                c = centre; U = u; id = shipId; drift = new Vector3(shipVel.x, 0f, shipVel.z) * 0.35f;
                var rnd = new System.Random(shipId * 7919 + BigAirBlasts * 131 + 396);
                float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
                var g = ZGame.I; float floor = (g ? g.GroundY(c.x, c.z) : c.y - 12f) + 3f;
                float a0 = R(0f, 6.2831853f);
                for (int i = 0; i < n; i++)
                {
                    float a = a0 + i * 6.2831853f / Mathf.Max(1, n) + R(-0.5f, 0.5f), r = R(SecondaryOut0, SecondaryOut1) * U;
                    var p = c + new Vector3(Mathf.Cos(a) * r, R(-0.15f, 0.35f) * U, Mathf.Sin(a) * r);
                    p.y = Mathf.Max(p.y, Mathf.Max(floor, c.y - 1f));                                              // up in the air, never at the ground under it
                    secs.Add((R(SecondaryFrom, SecondaryTo), p));
                }
                secs.Sort((x, y) => x.at.CompareTo(y.at));
            }

            void Update()
            {
                var g = ZGame.I; float dt = g && g.paused ? 0f : Time.deltaTime;
                if (dt <= 0f) return;
                t += dt;
                var z = OpenZFxPlayer.I; bool live = z && z.isActiveAndEnabled;
                while (si < secs.Count && t >= secs[si].at)
                {
                    var p = secs[si].pos + drift * t; si++;
                    if (live && z.Exp2Air(p, U * SecondaryU, Exp2Kind.Small, id * 41 + si, 1f, FxBlastKind.Vehicle, true)) { BigSecondaries++; LastSecondariesFired++; }   // K419a: counted per blast
                    var au = CombatAudio.I; if (au) au.PlayPitched("exp2_sat", p, LegacyMatch.GainExplosion * 0.45f, 0.03f, -1, 1.02f + 0.1f * Random.value, LegacyMatch.BankExplosion);
                }
                if (live && t < ColumnSec && (colT -= dt) <= 0f)
                {   // the dark column: puffs climbing from the blast, wider and slower as it rises
                    colT = ColumnEvery;
                    float k = t / ColumnSec;
                    var at = c + drift * t + Vector3.up * (ColumnRise * t * 0.8f) + new Vector3(Random.Range(-1f, 1f), Random.Range(-0.3f, 0.6f), Random.Range(-1f, 1f)) * (0.18f * U);
                    z.EmitChannel("leg_smoke", at, new Vector3(Random.Range(-0.25f, 0.25f), ColumnRise * (0.8f + 0.4f * Random.value), Random.Range(-0.25f, 0.25f)),
                        3.2f + Random.value * 1.2f, (2.6f + Random.value * 1.4f) * Mathf.Lerp(1f, 1.6f, k), Random.value < 0.6f ? OpenZFxCatalog.WreckSmokeDark : OpenZFxCatalog.WreckBurnSmoke, 0.9f);
                    if (t < 0.9f && Random.value < 0.5f)
                        z.EmitChannel("leg_fire", at + Vector3.down * 0.4f, new Vector3(0f, 1.1f, 0f), 0.45f + Random.value * 0.2f, (2.0f + Random.value) * (1f - t / 0.9f) + 0.6f,
                            Random.value < 0.5f ? OpenZFxCatalog.WreckFlameB : OpenZFxCatalog.WreckFlameC, 1f);
                    BigColumnPuffs++; LastColumnPuffs++;   // K419a: counted per blast
                }
                if (t >= ColumnSec && si >= secs.Count) Destroy(this);
            }
        }
    }
}
