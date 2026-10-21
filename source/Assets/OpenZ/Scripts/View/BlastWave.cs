using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    ///
    /// Causes found: (1) the sim kills every robot of a splash in the tick of its MissileExploded and CombatView threw all of them
    /// on that one frame (the whole squad in sync); (2) an MLRS shot is drawn as an 8-rocket ripple (MlrsSalvo): rocket 1 is the
    /// sim's and lands on the sim's blast, rockets 2-8 are cosmetic and land 0.15-1.05 s later spread over Zod's 10 m splash, so a
    /// robot standing next to where a ripple rocket was about to land was already flying (it had died from rocket 1, metres away).
    ///
    /// Fix (view only; the sim, its damage and its death tick are untouched): a robot killed by a blast is thrown when the blast's
    /// shock front reaches it, distance / WaveSpeed after the impact (+ a per-robot 0-Jitter s from its id), from the blast point;
    /// for an MLRS shot the ripple rocket that lands nearest to the robot counts when it is nearer than rocket 1: the robot flies
    /// when THAT rocket lands (never before an impact next to it). Until then the robot view holds its pose (UnitView.HoldForDeath),
    /// then UnitView.Die runs as before (ragdoll + Zod flight / pop / melt); its death cry waits with it (AudioDirector).
    /// The answer is cached per robot and frame, so AudioDirector (order 40) and CombatView (order 50) agree. Survivors' knock-downs
    /// (HitReactions) get the same distance delay.
    /// </summary>
    public static class BlastWave
    {
        /// <summary>Off = every splash death on the impact frame (the old behaviour).</summary>
        public static bool Enabled = true;
        /// <summary>Shock front speed (m/s): a robot at the edge of a 5 m tank splash flies 0.125 s after one at its centre, at the
        /// edge of a 10 m MLRS splash 0.25 s.</summary>
        public const float WaveSpeed = 16f;
        /// <summary>Per-robot extra delay (s), from its id: two robots at the same distance never fly on the same frame.</summary>
        public const float Jitter = 0.09f;
        /// <summary>Longest total delay (s) (the last ripple rocket lands ~1.1 s after rocket 1).</summary>
        public const float MaxDelay = 1.6f;
        /// <summary>Version 1.1 item 169: on = a robot killed by a blast is thrown on the explosion's own frame (the sim kills it on the
        /// blast's tick; the front delay of 16 m/s + 0-0.09 s per robot held it standing 0.1-0.9 s while the blast played); it gets its
        /// own push from the blast point (CombatView.BlastPower: power by distance, seeded scatter, spin), so a squad never flies alike.
        /// An MLRS kill still waits for its ripple rocket when one lands nearer, but never inside rocket 1's core. Off = the old front
        /// (for the before / after check).</summary>
        public static bool ImpactFrame = true;
        /// <summary>Item 169: inside this share of the MLRS splash radius round the sim's own rocket (rocket 1) a robot flies with it.</summary>
        public const float RippleCoreShare = 0.5f;
        /// <summary>Item 169 statistics: robot blast deaths thrown on the impact frame.</summary>
        public static int OnImpactFrame { get; private set; }

        struct Entry { public int frame; public float delay; public Vector3 from; public bool ripple; }
        static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
        static int cacheFrame = -1;

        /// <summary>Statistics (Play proof): robot deaths delayed, of them thrown by a ripple rocket, largest delay (s).</summary>
        public static int Delayed { get; private set; }
        public static int ByRipple { get; private set; }
        public static float MaxSeen { get; private set; }
        /// <summary>H0 round 2 (GP3): robot deaths held until the killing drawn round arrived, and the largest such hold (s).</summary>
        public static int BulletHeld { get; private set; }
        public static float MaxBulletHold { get; private set; }
        /// <summary>Off = a robot killed by a round (bolt, tracer, laser bolt, sniper crack, jeep / gatling bullet) falls at once, as before.</summary>
        public static bool BulletArrival = true;

        /// <summary>Real seconds from now until robot o (its Death event e) is thrown, the point it is thrown from, and whether that
        /// point is an MLRS ripple rocket's landing (else the sim's blast). 0 / found false = no blast of its killer this frame
        /// reached it (a bullet, flame or older splash death: at once, as before).</summary>
        public static float RobotDelay(ZGame g, ZEvent e, ZObject o, out Vector3 from, out bool ripple)
        {
            from = default; ripple = false;
            if (!Enabled || g == null || g.World == null || o == null || o.Kind != ZKind.Robot || e.otherId < 0) return 0f;
            if (!e.flag2 && !BulletArrival) return 0f;
            if (cacheFrame != Time.frameCount) { cache.Clear(); cacheFrame = Time.frameCount; }
            if (cache.TryGetValue(o.Id, out var c)) { from = c.from; ripple = c.ripple; return c.delay; }
            if (!e.flag2) return BulletDelay(g, e, o, out from);
            // this frame's blasts of the killer (the sim emits the MissileExploded, then the Death events of its victims)
            var ev = g.World.Events; bool any = false; ZEvent best = default; float bestD = float.MaxValue;
            for (int i = 0; i < ev.Count; i++)
            {
                var b = ev[i];
                if (b.type != ZEventType.MissileExploded || b.objId != e.otherId || b.radius <= 0) continue;
                float dx = b.x - o.CenterX, dy = b.y - o.CenterY, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= b.radius + 2f || d >= bestD) continue;
                bestD = d; best = b; any = true;
            }
            if (!any) { cache[o.Id] = new Entry { frame = Time.frameCount, delay = 0f, from = default }; return 0f; }
            var p = g.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position : g.CenterWorld(o);
            var at = ZGame.PxToWorld(best.x, best.y); at.y = g.GroundY(at.x, at.z);
            float wait = 0f;
            var killer = g.World.GetAny(e.otherId);
            if (killer != null && killer.Kind == ZKind.Vehicle && killer.Type == "missile_launcher" &&
                (!ImpactFrame || Flat(p, at) > RippleCoreShare * best.radius / 8f) &&                // item 169: inside rocket 1's core it flies with rocket 1
                MlrsSalvo.NearestLanding(g, killer.Id, p, best.radius / 8f + 1f, 2.5f, out float landIn, out var landAt) &&
                Flat(p, landAt) + 0.5f < Flat(p, at))
            { wait = landIn; at = landAt; ripple = true; }                                             // a ripple rocket lands next to it: that one throws it
            float delay = ImpactFrame ? Mathf.Min(MaxDelay, wait)                                     // item 169: on the explosion's own frame (an MLRS ripple rocket's: its landing frame)
                                      : Mathf.Min(MaxDelay, wait + Flat(p, at) / WaveSpeed + Jitter * Hash01(o.Id));
            from = at;
            cache[o.Id] = new Entry { frame = Time.frameCount, delay = delay, from = at, ripple = ripple };
            if (delay > 0.015f) { Delayed++; if (ripple) ByRipple++; if (delay > MaxSeen) MaxSeen = delay; }
            else OnImpactFrame++;
            return delay;
        }

        /// <summary>H0 round 2 (GP3): the sniper's crack now flies at Zod's 37.5 m/s like every drawn round (rsniper.cpp:173 EBullet,
        /// ebullet.cpp:7), so a killing round is in the air for up to ~0.5 s. The rule that robots fly on the impact frame,
        /// never before, then holds for rounds too: a robot killed this frame by a Hitscan hit of its killer is thrown when that round
        /// (drawn at once for a killing roll, WeaponsV2Runner / LegacyMatch.Shot / OpenZPyroFire) reaches it, from the killer's side
        /// (CombatView keeps its own 'from'). View only: the sim's death tick and damage are untouched. Crushes and older hits (no Hitscan
        /// of the killer on it this frame) fall at once as before.
        /// H0 round 3 (reviewer): the hold is the DRAWN round's flight, not killer centre -> victim centre / 37.5: from the muzzle the round
        /// leaves (WeaponsV2Runner.DrawnMuzzle) to the body point it hits (AimPos), through the same head-arrival formula the drawing
        /// code uses (WeaponsV2Runner.HeadTime / OpenZFxPlayer.ShotHeadTime; the pyro's flame: distance / 37.5 m/s). Round 2's sniper
        /// throw came 4-5 frames after the crack's head had arrived (it starts 1.5 m out and is 2.6 m long).</summary>
        static float BulletDelay(ZGame g, ZEvent e, ZObject o, out Vector3 from)
        {
            from = default;
            var ev = g.World.Events; bool shot = false;
            for (int i = 0; i < ev.Count && !shot; i++) { var b = ev[i]; shot = b.type == ZEventType.Hitscan && b.objId == e.otherId && b.otherId == o.Id && b.flag; }
            var killer = shot ? g.World.GetAny(e.otherId) : null;
            if (killer == null) { cache[o.Id] = new Entry { frame = Time.frameCount, delay = 0f }; return 0f; }
            var kp = g.Views.TryGetValue(killer.Id, out var kv) && kv ? kv.transform.position : g.CenterWorld(killer);
            var aim = g.Views.TryGetValue(o.Id, out var v) && v ? v.AimPos() : g.CenterWorld(o) + Vector3.up * 0.95f;
            string type = RoundType(killer);
            float dist = Vector3.Distance(WeaponsV2Runner.DrawnMuzzle(g, killer), aim);
            float delay = Mathf.Min(MaxDelay, RoundFlight(type, dist, g.gameSpeed));
            from = kp;
            cache[o.Id] = new Entry { frame = Time.frameCount, delay = delay, from = kp };
            if (delay > 0.015f) { BulletHeld++; if (delay > MaxBulletHold) MaxBulletHold = delay; }
            BulletKills.Add(new BulletKill { killer = killer.Id, victim = o.Id, frame = Time.frameCount, type = type, dist = dist, deathAt = Time.time, throwAt = Time.time + delay });
            if (BulletKills.Count > 64) BulletKills.RemoveAt(0);
            return delay;
        }

        /// <summary>The weapon type a unit's round is drawn as (APC crews shoot their crew type, the crane driver the grunt's bolt).</summary>
        public static string RoundType(ZObject a) => a == null ? "" : a.Kind == ZKind.Vehicle && a.Type == "apc" ? a.CrewWeaponType : a.Type == "crane" ? "grunt" : a.Type;

        /// <summary>Seconds a drawn round / flame of `type` needs for `dist` m from its muzzle (the drawing code's own numbers).</summary>
        public static float RoundFlight(string type, float dist, float gameSpeed)
        {
            if (type == "pyro") return dist / LegacyMatch.ShotSpeed(gameSpeed);                            // OpenZPyroFire.Cue: the flame arrives after distance / 37.5 m/s
            if (WeaponsV2.Enabled && WeaponsV2Runner.Draws(type)) return WeaponsV2Runner.HeadTime(type, dist, gameSpeed);
            return OpenZFxPlayer.ShotHeadTime(LegacyMatch.ShotWeapon(type), dist, LegacyMatch.ShotSpeed(gameSpeed));
        }

        /// <summary>H0 round 3 (GP3 proof): a robot killed by a round: its sim death (Time.time), when BulletDelay throws it, when
        /// CombatView really threw it (UnitView.Die). The drawing code notes when ITS killing round arrives (DrawnKills, the same frame).
        /// View-side statistics only, the last 64.</summary>
        public struct BulletKill { public int killer, victim, frame, thrownFrame; public string type; public float dist, deathAt, throwAt, thrownAt; }
        public struct DrawnKill { public int killer, victim, frame; public float at; }
        public static readonly List<BulletKill> BulletKills = new List<BulletKill>();
        public static readonly List<DrawnKill> DrawnKills = new List<DrawnKill>();
        /// <summary>The drawing code: its killing round from `killer` reaches `victim` `flight` s from now.</summary>
        public static void NoteDrawn(int killer, ZObject victim, float flight)
        {
            if (victim == null || (victim.Kind != ZKind.Robot && victim.Kind != ZKind.Vehicle && victim.Kind != ZKind.Cannon)) return;   // version 1.1 item 202: vehicles and guns wait for their killing round too (VehicleDeathSync)
            DrawnKills.Add(new DrawnKill { killer = killer, victim = victim.Id, frame = Time.frameCount, at = Time.time + flight });
            if (DrawnKills.Count > 64) DrawnKills.RemoveAt(0);
        }
        /// <summary>CombatView.RobotDeathLater: the held robot was thrown now.</summary>
        public static void NoteThrown(int victim)
        {
            for (int i = BulletKills.Count - 1; i >= 0; i--)
                if (BulletKills[i].victim == victim && BulletKills[i].thrownAt <= 0f) { var k = BulletKills[i]; k.thrownAt = Time.time; k.thrownFrame = Time.frameCount; BulletKills[i] = k; return; }
        }
        /// <summary>CombatView.RobotDeathLater: when the drawn round of `killer` that killed `victim` in frame `frame` arrives (false = none noted).</summary>
        public static bool DrawnArrival(int killer, int victim, int frame, out float at)
        {
            for (int i = DrawnKills.Count - 1; i >= 0; i--)
            {
                var d = DrawnKills[i];
                if (d.frame < frame) break;
                if (d.frame == frame && d.killer == killer && d.victim == victim) { at = d.at; return true; }
            }
            at = 0f; return false;
        }

        /// <summary>HitReactions: the knock-down / stagger of a survivor starts when the front reaches it.</summary>
        public static float SurvivorDelay(float distanceM) => Enabled && !ImpactFrame ? distanceM / WaveSpeed : 0f;   // item 169: knocked on the blast's frame too

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
        static float Hash01(int id) { uint h = (uint)id * 2654435761u; h ^= h >> 15; return (h & 1023u) / 1023f; }
    }
}
