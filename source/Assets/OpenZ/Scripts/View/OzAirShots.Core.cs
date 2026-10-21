using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K337 (the rounds fired at the flying shuttle that miss it): the numbers and the decisions, with no engine call in this file
    /// (View/OzAirShots.cs feeds them from the game; the kit's headless count compiles this same file). View only: no rule changes.
    /// The rules end every rocket, shell, missile and grenade aimed at the flying shuttle at its end point UP THERE (ZWorld.ShuttleAirBlast:
    /// only the ship can be hurt, nobody on the ground, hit or miss, also after the ship is shot down). So a drawn round never falls to the
    /// ground and never flies on for ever:
    ///  * a hit: the full blast at the hull, as before;
    ///  * a miss: it flies on along its path to the rules' end point and ends there in a small air burst (PopScale of its blast, a quieter and
    ///    higher pop); or earlier, at its cap (LifeK x the time it needed to reach the ship, never more than CapMaxSimS of game time), as soon
    ///    as the rules' end point is out of the ship's reach (a sure miss: no rule hit can follow); its later blast is then not drawn again;
    ///  * nothing of it on the ground (no ground light, ring, dust, scorch, crater, broken prop or lamp, no robot knocked down); the salvo's
    ///    extra rockets burst round it in the air also when the ship has flown on more than 10 m (the mech's twin too), and an MLRS shot at the
    ///    flying ship sends no "marked" rocket onto a vehicle (MlrsSalvo.Marked: the ship itself, or a vehicle on the ground under it, which
    ///    the rules leave alone); a round in flight when the ship breaks up bursts up there where it was going;
    ///  * every drawn end point and miss line inside the map and at most CeilingM over the ground, the arcs of the projectiles at the ship
    ///    too (ArcGravity); no drawn projectile older than its hard cap; at most MaxLive projectiles drawn at once (the oldest ends first).
    /// Bullets, tracers, lasers, the flame and the tesla bolt keep their own ends (the miss line past the hull, now inside the map box); grenades
    /// and the heavy shells (howitzer, the fort's gun) fly their own lob to the rules' end point as before (their drawers' arcs, up to about
    /// 40 m over the ground at 1x in the 10-minute count; their own pools of 32 and 12; no landing clink up in the air).
    /// </summary>
    public static partial class OzAirShots
    {
        /// <summary>The kit's switch (on in the game; off = every drawer as before, for the before / after count).</summary>
        public static bool On = true;

        // ------------------------------------------------------------------ the numbers
        /// <summary>A drawn projectile's hard cap: LifeK x its own drawn flight (+ the sim rocket's grace), never more than CapMaxSimS of game time
        /// (the longest flight at the flying ship in the 10-minute count: 12.4 s, a missile cannon's lead far ahead of a strafing ship, a miss).</summary>
        public const float LifeK = 1.5f, CapMaxSimS = 8f, Grace = 0.25f;
        /// <summary>The projectile pool's global cap (OpenZFxPlayer: 96 as before); full = the oldest ends first (it was: the new one is not drawn).</summary>
        public const int MaxLive = 96;
        /// <summary>The ship's reach for the sure-miss test: VmaxK x its top speed (the strafe under heavy fire takes it to about 2 x that: 10.2 m/s
        /// in the count) x the time left + the blast's radius + MarginPx.</summary>
        public const float VmaxK = 2.5f, MarginPx = 8f;
        /// <summary>The miss's air burst: its blast x PopScale (size, LEG radius), its sound x PopGain at PopPitch, from the blast bank's quieter takes.</summary>
        public const float PopScale = 0.55f, PopGain = 0.5f, PopPitch = 1.12f;
        public static readonly string[] BankPop = { "explosion-v3-20260909", "rocket-blast-sep11", "explosion-v2-20260909" };
        /// <summary>The air box: inside the map EdgeM from its edge, at most CeilingM over the ground.</summary>
        public const float CeilingM = 32f, EdgeM = 1f;
        /// <summary>A salvo's extra rocket (MLRS 2-8, the missile cannon's twin, the mech's twin) belongs to the sim's round it lands within FollowerM of.</summary>
        public const float FollowerM = 7f;
        /// <summary>Above the ground more than this = up in the air (K256e's air-burst gate, OzShuttleBreakup.AirBurstM).</summary>
        public const float AloftM = 3f;

        // ------------------------------------------------------------------ the counts (proofs)
        public static int Recorded, Bound, Followers, Pops, PopsAtCap, PopsFollowers, PopsAtEnd, HitsDrawn, AlreadyEnded, LostBursts, CapEnds, Evictions,
                          SkippedLandings, QuietLandings, NoClink, SalvoAloft, Boxed, MaxLiveSeen, ArcsFlattened, MarksLeftOut;
        public static float MaxAgeEnded, MaxPopAge;
        public static void ResetCounts()
        {
            Recorded = Bound = Followers = Pops = PopsAtCap = PopsFollowers = PopsAtEnd = HitsDrawn = AlreadyEnded = LostBursts = CapEnds = Evictions = 0;
            SkippedLandings = QuietLandings = NoClink = SalvoAloft = Boxed = MaxLiveSeen = ArcsFlattened = MarksLeftOut = 0; MaxAgeEnded = MaxPopAge = 0f;
        }

        /// <summary>The drawn arc of a round at the flying ship (and of its salvo's extras) under the ceiling. The drawers fly every projectile
        /// on the parabola from + (to - from) s + up A (s - s^2), A = g T^2 / 2 (OpenZFxPlayer.Launch / StepProjectiles, T = its drawn flight),
        /// so a long lead flight at 1x rose far above the ship (12.3 s at g 6: about 110 m over the ground, its air burst out of sight). The arc's
        /// top is y0 + (A + dy)^2 / (4 A); it stays at most at the ceiling while A &lt;= (sqrt(h0) + sqrt(h1))^2 (h0, h1 = the room from `from`
        /// and from `to` up to the ceiling). Returns the gravity to fly with: g itself when its arc stays under the ceiling, else the lower one
        /// whose top just touches it. The end points and the flight time stay (the round still lands on its blast).</summary>
        public static float ArcGravity(float g, float T, float h0, float h1)
        {
            if (g <= 0f || T <= 0.05f) return g;
            float a = Mathf.Sqrt(Mathf.Max(0.25f, h0)) + Mathf.Sqrt(Mathf.Max(0.25f, h1));
            return Mathf.Min(g, 2f * a * a / (T * T));
        }
        /// <summary>The highest point of that parabola (start height y0, dy = to.y - from.y), for the proofs.</summary>
        public static float ArcTop(float y0, float dy, float g, float T)
        {
            float A = g * T * T * 0.5f;
            if (A <= 1e-4f) return y0 + Mathf.Max(0f, dy);
            float s = Mathf.Clamp01((A + dy) / (2f * A));
            return y0 + dy * s + A * s * (1f - s);
        }

        // ------------------------------------------------------------------ the rounds aimed at the flying ship (fixed tables, no allocation)
        public struct Rec
        {
            public int gen;                                      // 0 = free; else this record's number (a bind holds it: a reused slot is not its round)
            public bool used, popped;                            // used: its blast is still to come
            public int attacker, target, ex, ey, radius;
            public double end;                                   // the rules' explode time (sim s)
            public float firedAt, life, capAge, popAt;           // real s: fired at, its drawn flight, the age a sure miss may end at, when it popped
            public Vector3 from, to, popPos;
        }
        public const int RecMax = 64;
        public static readonly Rec[] Recs = new Rec[RecMax];
        static int recNext, genNext;

        /// <summary>A drawn projectile (OpenZFxPlayer serial) bound to a record; slot = serial % BindMax (at most MaxLive projectiles live).</summary>
        public struct Bind { public int serial, rec, gen; public bool follower; public float stagger; }
        public const int BindMax = 256;
        public static readonly Bind[] Binds = new Bind[BindMax];

        public struct Seen { public int id; public float y, at; }
        static readonly Seen[] seen = new Seen[8];

        public static void Clear()
        {
            for (int i = 0; i < RecMax; i++) Recs[i] = default;
            for (int i = 0; i < BindMax; i++) Binds[i] = new Bind { serial = -1, rec = -1 };
            for (int i = 0; i < seen.Length; i++) seen[i] = new Seen { id = -1 };
            recNext = 0;
        }
        static OzAirShots() { Clear(); }

        /// <summary>A drawn round leaves for the flying ship (CombatView / FortGunView through OzShuttleAim.NoteFired). hull = the ship's hull middle
        /// now, gs = the game speed, now = real time. Its cap: LifeK x the share of its flight that takes it to the ship (a lead far ahead gives a
        /// short cap; a round at a ship that holds still gets LifeK x its whole flight: it always reaches its end first).</summary>
        public static int Record(ZEvent e, ZObject a, ZObject t, Vector3 from, Vector3 to, Vector3 hull, float gs, float now)
        {
            if (!On || t == null) return -1;
            gs = Mathf.Max(0.05f, gs);
            float life = Mathf.Max(0.05f, e.value / gs);
            float dEnd = (to - from).magnitude, dShip = (hull - from).magnitude;
            float normal = life * Mathf.Clamp01(dShip / Mathf.Max(0.5f, dEnd));
            int i = recNext; recNext = (recNext + 1) % RecMax;
            Recs[i] = new Rec
            {
                gen = ++genNext, used = true, attacker = a != null ? a.Id : e.objId, target = t.Id, ex = e.x, ey = e.y, radius = e.radius, end = e.end,
                firedAt = now, life = life, capAge = Mathf.Min(LifeK * normal + Grace, CapMaxSimS / gs), from = from, to = to,
            };
            Recorded++;
            return i;
        }

        /// <summary>The live round whose drawn end is exactly p (the salvo helpers and the grenade get the very vector CombatView drew to).</summary>
        public static int RecAt(Vector3 p, float eps = 0.05f)
        {
            for (int i = 0; i < RecMax; i++) if (Recs[i].used && (Recs[i].to - p).sqrMagnitude <= eps * eps) return i;
            return -1;
        }

        /// <summary>The same, also a round whose blast came this very frame or just before (the grenade's last step after its blast).</summary>
        public static int RecAt(Vector3 p, float now, float eps)
        {
            for (int i = 0; i < RecMax; i++)
            {
                var r = Recs[i];
                if ((r.used || (r.gen != 0 && now - r.firedAt <= r.life + 1f)) && (r.to - p).sqrMagnitude <= eps * eps) return i;
            }
            return -1;
        }

        /// <summary>The live record of a blast (its shooter, its target and the rules' end point).</summary>
        public static int RecOf(int attacker, int target, int ex, int ey)
        {
            for (int i = 0; i < RecMax; i++) { var r = Recs[i]; if (r.used && r.attacker == attacker && r.target == target && r.ex == ex && r.ey == ey) return i; }
            return -1;
        }

        /// <summary>Records of rounds long gone (their blast never came: a level restart, a held shot that was dropped) are let go.</summary>
        public static void Expire(double simNow, float now)
        {
            for (int i = 0; i < RecMax; i++)
                if (Recs[i].used && ((simNow > Recs[i].end + 4.0 && now > Recs[i].firedAt + Recs[i].life + 4f) || now < Recs[i].firedAt || simNow < Recs[i].end - 600.0)) Recs[i].used = false;
        }

        /// <summary>A projectile drawn to `to`: bound to the round it draws (the same end point) or, as a salvo's extra rocket, to the round it
        /// lands beside (within FollowerM, fired in the last 2 s). False = a round at anything else (left alone).</summary>
        public static bool BindLaunch(int serial, Vector3 to, float now)
        {
            if (!On || serial < 0) return false;
            int r = RecAt(to); bool follower = false;
            if (r < 0)
            {
                float best = FollowerM * FollowerM;
                for (int i = 0; i < RecMax; i++)
                {
                    var q = Recs[i]; if (q.gen == 0 || (!q.used && !q.popped) || now - q.firedAt > 2f) continue;
                    float d = (q.to - to).sqrMagnitude; if (d < best) { best = d; r = i; follower = true; }
                }
            }
            if (r < 0) return false;
            Binds[serial % BindMax] = new Bind { serial = serial, rec = r, gen = Recs[r].gen, follower = follower, stagger = follower ? 0.03f + 0.04f * (serial % 4) : 0f };
            Bound++; if (follower) Followers++;
            return true;
        }

        static bool BindOf(int serial, out Bind b)
        {
            b = default;
            if (serial < 0) return false;
            b = Binds[serial % BindMax];
            return b.serial == serial && b.rec >= 0 && Recs[b.rec].gen == b.gen;
        }
        public static void Unbind(int serial) { if (serial >= 0 && Binds[serial % BindMax].serial == serial) Binds[serial % BindMax] = new Bind { serial = -1, rec = -1 }; }
        public static bool IsBound(int serial) => BindOf(serial, out _);

        /// <summary>The rules can no longer hit the ship with this round: the ship is gone, or its end point is beyond the ship's reach in the
        /// time left. Never while the ship stands on the ground (the blast then is a real one there) or once the blast is due.</summary>
        public static bool SureMiss(in Rec r, ZWorld w)
        {
            if (w == null) return false;
            var s = w.GetAny(r.target);
            if (s == null || s.Destroyed) return true;
            if (!s.ShuttleAirborne) return false;
            double left = r.end - w.Time;
            if (left <= 0.0) return false;
            double vmax = VmaxK * System.Math.Max(1.0, s.RealMoveSpeed), dx = r.ex - s.CenterX, dy = r.ey - s.CenterY;
            return System.Math.Sqrt(dx * dx + dy * dy) > vmax * left + r.radius + MarginPx;
        }

        /// <summary>One drawn projectile's step: true = it ends here now in a small air burst (the caller draws the burst at pos and stops it).
        /// The round itself: at its cap once it is a sure miss and up in the air (more than AloftM over the ground under it: a round still low
        /// on its way up flies on); a salvo's extra: when its round has ended so (and it has flown as long).</summary>
        public static bool PopNow(int serial, float age, float life, Vector3 pos, float groundY, float now, ZWorld w)
        {
            if (!On || pos.y - groundY <= AloftM || !BindOf(serial, out var b)) return false;
            ref var r = ref Recs[b.rec];
            if (b.follower)
            {
                if (!r.popped || now < r.popAt + b.stagger || age < Mathf.Min(r.capAge, life - 0.02f)) return false;
                PopsFollowers++; Pops++; MaxPopAge = Mathf.Max(MaxPopAge, age);
                return true;
            }
            if (!r.used || r.popped || age < r.capAge || age >= r.life - 0.02f || !SureMiss(r, w)) return false;
            r.popped = true; r.popAt = now; r.popPos = pos;
            PopsAtCap++; Pops++; MaxPopAge = Mathf.Max(MaxPopAge, age);
            return true;
        }

        /// <summary>A drawn projectile's hard end (real s).</summary>
        public static float LifeCap(float life, float gs) => Mathf.Min(LifeK * life + Grace, CapMaxSimS / Mathf.Max(0.05f, gs));
        /// <summary>A projectile stopped at its hard cap: true = it is a round at the ship, up in the air (the caller draws its air burst; its
        /// blast is not drawn again unless the rules hit the ship with it). Low (AloftM or less over the ground): it ends unseen, its blast
        /// later draws the burst up there.</summary>
        public static bool CapEnded(int serial, Vector3 pos, float groundY, float now)
        {
            CapEnds++;
            if (pos.y - groundY <= AloftM || !BindOf(serial, out var b)) return false;
            ref var r = ref Recs[b.rec];
            if (!b.follower && !r.popped) { r.popped = true; r.popAt = now; r.popPos = pos; }
            Pops++; PopsAtCap++;
            return true;
        }

        // ------------------------------------------------------------------ the blast (CombatView.MissileExploded -> LegacyMatch.MissileBlast)
        public enum Blast { NotAloft, Hit, Miss, AlreadyEnded }

        /// <summary>The rules' own test for this blast and the ship (ZWorld.ProcessMissileDamage: not its own team, the splash square, the
        /// circle, hit points), and that the ship took a blast in the ticks of this frame (spanSim: so a ship shot down earlier is not "hit").</summary>
        public static bool RulesHit(ZEvent e, ZObject attacker, ZObject t, ZWorld w, double spanSim)
        {
            if (t == null || w == null || t.MaxHealth <= 0) return false;
            if (attacker != null && attacker.Owner != Team.Neutral && t.Owner == attacker.Owner) return false;
            int dx = e.x - t.CenterX, dy = e.y - t.CenterY;
            if (System.Math.Abs(dx) > e.radius || System.Math.Abs(dy) > e.radius) return false;
            float mag = (float)System.Math.Sqrt((float)(dx * dx + dy * dy));
            if (mag >= e.radius) return false;
            return w.Time - t.LastMissileHit <= spanSim + 1e-6;
        }

        /// <summary>What the view draws for a blast aimed at the ship while it is (or was, broken up) up in the air.</summary>
        public static Blast Classify(ZEvent e, ZObject attacker, ZObject t, ZWorld w, bool aloft, double spanSim)
        {
            if (!On || !aloft || t == null || !t.IsShuttle || e.flag2) return Blast.NotAloft;
            int r = RecOf(e.objId, e.otherId, e.x, e.y);
            bool hit = RulesHit(e, attacker, t, w, spanSim);
            bool popped = r >= 0 && Recs[r].popped;
            if (r >= 0) Recs[r].used = false;
            if (hit) { HitsDrawn++; return Blast.Hit; }
            if (popped) { AlreadyEnded++; return Blast.AlreadyEnded; }
            PopsAtEnd++; Pops++;
            return Blast.Miss;
        }

        /// <summary>A blast at the ship while it is not drawn high (0-2.5 m: taking off or landing, drawn as before by K256e's rule): true = the
        /// rules still have the ship in the air, did not hit it, and this round already burst up there at its cap (its blast is not drawn a
        /// second time). A ship that has landed (a real blast on the ground) or a hit: false, drawn as before.</summary>
        public static bool EndedAlready(ZEvent e, ZObject attacker, ZObject t, ZWorld w, double spanSim)
        {
            if (!On || t == null || !t.IsShuttle || !t.ShuttleAirborne || e.flag2) return false;
            int r = RecOf(e.objId, e.otherId, e.x, e.y);
            if (r < 0) return false;
            bool popped = Recs[r].popped;
            Recs[r].used = false;
            if (!popped) return false;
            if (RulesHit(e, attacker, t, w, spanSim)) { HitsDrawn++; return false; }
            AlreadyEnded++;
            return true;
        }

        /// <summary>A salvo's extra rocket (or the missile cannon's third burst) whose round already ended in its air burst: its own landing is
        /// not drawn (MlrsSalvo / MissileCannonSalvo / MechRockets): it burst with it.</summary>
        public static bool SkipLanding(int shooter, Vector3 pos, float now)
        {
            if (!On) return false;
            for (int i = 0; i < RecMax; i++)
            {
                var r = Recs[i];
                if (r.gen == 0 || !r.popped || r.attacker != shooter || now > r.popAt + r.life + 3f) continue;
                if ((r.to - pos).sqrMagnitude <= FollowerM * FollowerM) { SkippedLandings++; return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ the ship's last height up there (a round in flight when it broke up)
        public static void SeenAloft(int id, float hullY, float now)
        {
            int k = -1;
            for (int i = 0; i < seen.Length && k < 0; i++) if (seen[i].id == id) k = i;
            for (int i = 0; i < seen.Length && k < 0; i++) if (seen[i].id < 0) k = i;
            if (k < 0) { k = 0; for (int i = 1; i < seen.Length; i++) if (seen[i].at < seen[k].at) k = i; }
            seen[k] = new Seen { id = id, y = hullY, at = now };
        }
        public static bool LastAloft(int id, float now, out float hullY)
        {
            hullY = 0f;
            for (int i = 0; i < seen.Length; i++) if (seen[i].id == id && now - seen[i].at <= 30f) { hullY = seen[i].y; return true; }
            return false;
        }

        /// <summary>The point up there for a blast at the ship that is no longer drawn flying (broken up): where the drawn round was going, else
        /// the rules' end point at the ship's last hull height. False = nothing known (the ground point stays, as before).</summary>
        public static bool LostPoint(ZObject t, Vector3 ground, float now, out Vector3 p)
        {
            p = ground;
            if (!On || t == null || !t.IsShuttle || !t.ShuttleAirborne) return false;
            for (int i = 0; i < RecMax; i++)
            {
                var r = Recs[i];
                if (!r.used || r.target != t.Id) continue;
                if (Mathf.Abs(r.ex / 8f - ground.x) <= 0.07f && Mathf.Abs(-r.ey / 8f - ground.z) <= 0.07f) { p = r.to; return true; }
            }
            if (LastAloft(t.Id, now, out float y)) { p = new Vector3(ground.x, Mathf.Max(ground.y, y), ground.z); return true; }
            return false;
        }

        // ------------------------------------------------------------------ the air box
        /// <summary>The end of the segment from -> to cut where it first leaves the box (x in [x0, x1], z in [z0, z1], y at most yMax).</summary>
        public static Vector3 InBox(Vector3 from, Vector3 to, float x0, float x1, float z0, float z1, float yMax)
        {
            var d = to - from; float t = 1f;
            if (to.x < x0 && d.x < 0f) t = Mathf.Min(t, (x0 - from.x) / d.x);
            if (to.x > x1 && d.x > 0f) t = Mathf.Min(t, (x1 - from.x) / d.x);
            if (to.z < z0 && d.z < 0f) t = Mathf.Min(t, (z0 - from.z) / d.z);
            if (to.z > z1 && d.z > 0f) t = Mathf.Min(t, (z1 - from.z) / d.z);
            if (to.y > yMax && d.y > 0f) t = Mathf.Min(t, (yMax - from.y) / d.y);
            if (t >= 1f) return to;
            Boxed++;
            return from + d * Mathf.Max(0f, t);
        }

        /// <summary>A point kept inside the map (EdgeM from its edge: x in [0, x1], z in [zMin, 0]) and at most CeilingM over groundY.</summary>
        public static Vector3 InMap(Vector3 p, float x1, float zMin, float groundY)
        {
            var q = new Vector3(Mathf.Clamp(p.x, EdgeM, Mathf.Max(EdgeM, x1 - EdgeM)), Mathf.Min(p.y, groundY + CeilingM), Mathf.Clamp(p.z, Mathf.Min(-EdgeM, zMin + EdgeM), -EdgeM));
            if ((q - p).sqrMagnitude > 1e-6f) Boxed++;
            return q;
        }

        /// <summary>The proof's one line of counts.</summary>
        public static string CountsText() =>
            $"rounds at the flying ship {Recorded}, drawn projectiles bound {Bound} (salvo extras {Followers}); air bursts of misses {Pops} (at the cap {PopsAtCap}, " +
            $"salvo extras with them {PopsFollowers}, at the rules' end point {PopsAtEnd}); hits drawn {HitsDrawn}; blasts not drawn again {AlreadyEnded}; " +
            $"after a break-up up there {LostBursts}; hard-cap ends {CapEnds}; pool full, oldest ended {Evictions}; extra landings skipped {SkippedLandings}; " +
            $"fort landings quiet {QuietLandings}; grenade clinks up there left out {NoClink}; salvo extras kept in the air {SalvoAloft}; points kept in the map box {Boxed}; " +
            $"arcs kept under the ceiling {ArcsFlattened}; MLRS shots at the flying ship without marked rockets {MarksLeftOut}; " +
            $"most projectiles at once {MaxLiveSeen}; oldest drawn projectile {MaxAgeEnded:0.00} s; latest air burst {MaxPopAge:0.00} s";
    }
}
