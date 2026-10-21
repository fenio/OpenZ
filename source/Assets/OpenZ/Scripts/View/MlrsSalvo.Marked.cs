using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1 item 202 ("I saw an MLRS shot in an area, it did not even hit the light tank visually, but 1.2 s later it exploded for no
    /// visual reason"): the sim's MLRS shot is one missile (rocket 1) whose 10 m splash hurts every vehicle in it on rocket 1's tick; rockets
    /// 2-8 are drawn only. A vehicle 5-10 m from rocket 1 (often the target itself, dodging out of rocket 1's point during the flight) blew
    /// up with no drawn rocket near it, and the ripple rocket that came down beside it landed 0.1-1.7 s later.
    ///
    /// Now, at the shot, every vehicle and gun the shot's splash can reach (the sim's own point, radius and team filter of rocket 1, plus
    /// MarkMarginPx for one that drives in) gets the ripple rocket that would have landed nearest to it ("marked"): it leaves the rack
    /// first (MarkedGap apart), FOLLOWS its vehicle in flight (OpenZFxPlayer.Steer: its end point moves with the hull, a few cm a frame)
    /// and lands on it on the sim's blast frame (rocket 1's frame, the frame the vehicle's death plays). A vehicle or gun that stands still
    /// within StayCoreM of rocket 1's point is left to rocket 1. The other ripple rockets fill the disc after them, as before.
    /// Filler timing: the n marked rockets go first (MarkedGap apart, at most the ripple's spacing), so a filler rocket whose slot came
    /// before the slots the marked ones took now leaves (and lands) up to n x MarkedGap later (n = 1-4: 0.15-0.6 s at 1x); the volley's last
    /// rocket leaves no later than before. A robot thrown by a filler rocket (BlastWave, item 169) waits that much longer.
    /// Robots beside a marked vehicle: a marked rocket that landed this frame or the frame before still counts as a landing for
    /// NearestLanding (BlastWave.RobotDelay runs in LateUpdate, after StepMarked landed it), so such a robot flies with that rocket.
    /// A launcher that dies (or loses its driver) before its rockets leave drops its queued rockets at once (no wait for a rocket that
    /// never comes). View only: no sim object, event or damage; the volley keeps its 8 rockets.
    /// </summary>
    public partial class MlrsSalvo
    {
        /// <summary>Off = the old ripple (no rocket follows a vehicle; for the before / after check).</summary>
        public static bool MarkVehicles = true;
        /// <summary>Splash radius + this (px, 2 m): a vehicle that drives into the splash during the flight also gets its rocket.</summary>
        public const int MarkMarginPx = 16;
        /// <summary>A gun, or a vehicle that stands without orders, this close (m, hull to rocket 1's point) is left to rocket 1.</summary>
        public const float StayCoreM = 2f;
        /// <summary>Where on the vehicle the rocket lands (m over its ground point: on the hull).</summary>
        public const float AimUp = 0.9f;
        /// <summary>A marked rocket flies at least this share of the time left to the sim's blast (the marked ones leave closer together).</summary>
        public const float MarkedMinFlight = 0.6f;

        public static int MarkedQueued { get; private set; }
        public static int MarkedFired { get; private set; }
        public static int MarkedLandedOn { get; private set; }
        /// <summary>Play proof: the farthest a marked rocket's landing point moved with its vehicle during its flight (m).</summary>
        public static float MarkedFollowMax { get; private set; }

        struct Marked { public int shooter, target, index, proj; public float at, landAt, follow; public double simEnd; public Vector3 to, aimed; public bool launched, steered; }
        readonly List<Marked> marked = new List<Marked>();
        readonly HashSet<int> deadRacks = new HashSet<int>();

        /// <summary>Play proof: one marked rocket that landed: whose, on which vehicle, its drawn body (OpenZFxPlayer serial, -1 = none
        /// was drawn), the frame and point of its blast, whether its body was steered onto the vehicle every frame of its flight, and
        /// how far its end point followed the vehicle (m). The last 64.</summary>
        public struct MarkedLanding { public int shooter, target, proj, frame; public Vector3 at; public bool steered; public float follow; }
        public static readonly List<MarkedLanding> MarkedLog = new List<MarkedLanding>();
        /// <summary>vehicle id -> the shooter and frame a marked rocket landed on it.</summary>
        static readonly Dictionary<int, (int shooter, int frame)> hitOn = new Dictionary<int, (int, int)>();

        /// <summary>VehicleDeathSync: a marked rocket of `shooter` landed on vehicle `id` this frame or the frame before.</summary>
        public static bool HitOn(int id, int shooter) => hitOn.TryGetValue(id, out var h) && h.shooter == shooter && Time.frameCount - h.frame <= 1;
        /// <summary>Marked rockets that landed this frame or the frame before: where, whose (a vehicle beside the marked one is hit too).</summary>
        static readonly List<(Vector3 at, int shooter, int frame)> recent = new List<(Vector3, int, int)>();
        /// <summary>VehicleDeathSync: a marked rocket of `shooter` landed within `radius` m of p (flat) this frame or the frame before.</summary>
        public static bool LandedNear(int shooter, Vector3 p, float radius)
        {
            foreach (var r in recent)
            {
                if (r.shooter != shooter || Time.frameCount - r.frame > 1) continue;
                var d = r.at - p; d.y = 0f;
                if (d.sqrMagnitude <= radius * radius) return true;
            }
            return false;
        }
        /// <summary>Play proof: the frame the last marked rocket landed on vehicle `id` (-1 = none).</summary>
        public static int LastHitFrame(int id) => hitOn.TryGetValue(id, out var h) ? h.frame : -1;

        /// <summary>Play proof (the salvo check): unit views whose death start is timed: id -> the frame its view began to die (thrown,
        /// popped or removed), -1 = not yet. Read in LateUpdate (order 55: after CombatView and the held deaths' coroutines).</summary>
        static readonly Dictionary<int, int> watched = new Dictionary<int, int>();
        static readonly List<int> watchIds = new List<int>();
        public static void Watch(int id) => watched[id] = -1;
        public static void WatchClear() => watched.Clear();
        public static int ThrownFrame(int id) => watched.TryGetValue(id, out var f) ? f : -1;

        void LateUpdate()
        {
            if (watched.Count == 0) return;
            if (!game) game = ZGame.I;
            if (!game) return;
            watchIds.Clear(); watchIds.AddRange(watched.Keys);
            foreach (var id in watchIds)
                if (watched[id] < 0 && (!game.Views.TryGetValue(id, out var v) || !v || v.Dying)) watched[id] = Time.frameCount;
        }

        /// <summary>The sim's own missile of this launcher's shot (rocket 1): the newest one in flight.</summary>
        static ZMissile SimShot(ZWorld w, int shooter)
        {
            ZMissile best = null;
            foreach (var m in w.PendingMissiles) if (m.attackerId == shooter && !m.debris && !m.grenade && (best == null || m.explodeTime > best.explodeTime)) best = m;
            foreach (var m in w.QueuedMissiles) if (m.attackerId == shooter && !m.debris && !m.grenade && (best == null || m.explodeTime > best.explodeTime)) best = m;
            return best;
        }

        Vector3 AimOn(ZObject o)
        {
            var p = game.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position : game.CenterWorld(o);
            return p + Vector3.up * AimUp;
        }

        static float HullM(ZObject o, int px, int py)
        {
            float cx = o.LocX + o.WidthPix * 0.5f, cy = o.LocY + o.HeightPix * 0.5f;
            float dx = Mathf.Max(0f, Mathf.Abs(px - cx) - o.WidthPix * 0.5f), dy = Mathf.Max(0f, Mathf.Abs(py - cy) - o.HeightPix * 0.5f);
            return Mathf.Sqrt(dx * dx + dy * dy) / 8f;
        }

        /// <summary>Queue hook, right after rockets 2-8 were queued (launches[first ..]): the marked rockets take the place of the queued
        /// ones that would have landed nearest to their vehicles; the rest of the ripple follows them at its spacing.</summary>
        void QueueMarked(ZObject a, int first)
        {
            if (!MarkVehicles || a == null || game == null || game.World == null || first < 0) return;
            var w = game.World; var m = SimShot(w, a.Id);
            if (m == null || m.explodeTime <= w.Time || OzAirShots.NoMarks(w, m)) return;            // K337: not for a shot at the flying shuttle (only the ship can be hurt; View/OzAirShots.cs)
            float speed = Mathf.Max(0.25f, game.gameSpeed), left = OpenZFxPlayer.RealFlight((float)(m.explodeTime - w.Time), speed);
            var cands = new List<(ZObject o, float c)>();
            foreach (var o in w.Objects)
            {
                if ((o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) || o.Destroyed || o.KillAt >= 0 || o.MaxHealth <= 0) continue;
                if (m.team != Team.Neutral && o.Owner == m.team) continue;                              // the sim's filter: no friendly fire
                float dx = m.x - o.CenterX, dy = m.y - o.CenterY, c = Mathf.Sqrt(dx * dx + dy * dy);
                if (c >= m.radius + MarkMarginPx) continue;
                bool stays = o.Kind == ZKind.Cannon || (!o.IsMoving && o.Waypoints.Count == 0);
                if (stays && HullM(o, m.x, m.y) <= StayCoreM) continue;                                 // rocket 1 lands on it
                cands.Add((o, c));
            }
            if (cands.Count == 0) return;
            cands.Sort((p, q) => p.c.CompareTo(q.c));
            int n = Mathf.Min(cands.Count, Rockets - 1);
            float gap = Mathf.Min(Spacing / speed, (1f - MarkedMinFlight) * left / n);
            for (int k = 0; k < n; k++)
            {
                var o = cands[k].o; var aim = AimOn(o);
                int bi = -1; float bd = float.MaxValue;
                for (int i = first; i < launches.Count; i++)
                {
                    if (launches[i].shooter != a.Id) continue;
                    var q = launches[i].to - aim; q.y = 0f;
                    if (q.sqrMagnitude < bd) { bd = q.sqrMagnitude; bi = i; }
                }
                int index = bi >= 0 ? launches[bi].index : k + 1;
                if (bi >= 0) launches.RemoveAt(bi);
                marked.Add(new Marked { shooter = a.Id, target = o.Id, index = index, proj = -1, at = Time.time + (k + 1) * gap, simEnd = m.explodeTime, to = aim, aimed = aim });
                MarkedQueued++;
            }
            for (int i = first, j = 1; i < launches.Count; i++)                                         // the fill rockets after the marked ones, at the ripple's spacing
            {
                if (launches[i].shooter != a.Id) continue;
                var l = launches[i]; l.at = Time.time + n * gap + j++ * Spacing / speed; launches[i] = l;
            }
            // the last rocket leaves at n x gap + (Rockets - 1 - n) x Spacing <= (Rockets - 1) x Spacing (gap <= Spacing; all / gameSpeed):
            // inside the rack's aim hold that Queue sets right after this ((Rockets - 1) x Spacing / gameSpeed + 0.25 s)
        }

        /// <summary>Update hook (after the ripple's own launches and landings): launch, steer and land the marked rockets.</summary>
        void StepMarked()
        {
            if (marked.Count == 0 && launches.Count == 0) return;
            if (!game || game.World == null) { marked.Clear(); return; }
            DropDeadRacks();
            if (marked.Count == 0) return;
            float now = Time.time; var z = OpenZFxPlayer.I; bool live = z && z.isActiveAndEnabled;
            for (int i = 0; i < marked.Count; i++)
            {
                var m = marked[i];
                if (game.Views.TryGetValue(m.target, out var tv) && tv) m.to = tv.transform.position + Vector3.up * AimUp;   // follows its vehicle (a dead one stands still)
                if (!m.launched)
                {
                    if (now < m.at || firedThisFrame.Contains(m.shooter)) { marked[i] = m; continue; }
                    if (!FireMarked(ref m)) { deadRacks.Add(m.shooter); marked.RemoveAt(i--); continue; }   // the rack is gone: no more rockets
                    firedThisFrame.Add(m.shooter);
                }
                else
                {
                    if (!(live && m.proj >= 0 && z.Steer(m.proj, m.to))) m.steered = false;           // its drawn body follows the hull (proof: every frame)
                    var drift = m.to - m.aimed; drift.y = 0f; m.follow = Mathf.Max(m.follow, drift.magnitude);
                }
                if (game.World.Time >= m.simEnd - 1e-5 || now >= m.landAt + OpenZFxPlayer.SimEndGrace)
                {   // the sim's blast frame (rocket 1's, the vehicle's death): it lands on the hull
                    marked.RemoveAt(i--);
                    hitOn[m.target] = (m.shooter, Time.frameCount);
                    if (hitOn.Count > 256) hitOn.Clear();
                    recent.RemoveAll(r => Time.frameCount - r.frame > 1);
                    recent.Add((m.to, m.shooter, Time.frameCount));
                    if (m.follow > MarkedFollowMax) MarkedFollowMax = m.follow;
                    MarkedLandedOn++;
                    MarkedLog.Add(new MarkedLanding { shooter = m.shooter, target = m.target, proj = m.proj, frame = Time.frameCount, at = m.to, steered = m.launched && m.steered, follow = m.follow });
                    if (MarkedLog.Count > 64) MarkedLog.RemoveAt(0);
                    Land(new Landing { at = now, pos = m.to - Vector3.up * 0.2f, shooter = m.shooter, id = (m.shooter * 7919 + m.index * 131 + Time.frameCount) & 0x3fffffff });
                    continue;
                }
                marked[i] = m;
            }
            if (deadRacks.Count > 0) DropDeadRacks();
        }

        /// <summary>The launcher can still fire its rack (as Fire / FireMarked require).</summary>
        bool RackLive(int shooter)
        {
            if (!game.Views.TryGetValue(shooter, out var v) || !v || v.Dying || !v.isActiveAndEnabled) return false;
            var o = v.Obj;
            return o != null && !o.Destroyed && o.Owner != Team.Neutral;
        }

        /// <summary>A launcher killed (or its driver sniped) before its rockets left: its queued ripple and marked rockets go at once, so
        /// no death waits for a rocket that never comes (NearestLanding no longer sees them). In flight ones still land.</summary>
        void DropDeadRacks()
        {
            if (!MarkVehicles) { deadRacks.Clear(); return; }                                        // the old ripple as it was (before / after check)
            foreach (var l in launches) if (!deadRacks.Contains(l.shooter) && !RackLive(l.shooter)) deadRacks.Add(l.shooter);
            foreach (var m in marked) if (!m.launched && !deadRacks.Contains(m.shooter) && !RackLive(m.shooter)) deadRacks.Add(m.shooter);
            if (deadRacks.Count == 0) return;
            launches.RemoveAll(x => deadRacks.Contains(x.shooter));
            marked.RemoveAll(x => !x.launched && deadRacks.Contains(x.shooter));
            deadRacks.Clear();
        }

        bool FireMarked(ref Marked m)
        {
            if (!RackLive(m.shooter)) return false;
            var v = game.Views[m.shooter]; var o = v.Obj;
            var from = v.MuzzlePos();
            v.OnFire();                                                                               // tube empties, rack kick, hull jolt, next tube
            MlrsSideKick.Kick(v, from, m.to);
            float simLeft = Mathf.Max(0.05f, (float)(m.simEnd - game.World.Time));                   // sim s until rocket 1's blast
            var z = OpenZFxPlayer.I; bool live = z && z.isActiveAndEnabled; int before = live ? z.ProjectileSerial : 0;
            OpenZFxPlayer.NextSimEnd = m.simEnd;                                                      // its body goes on the sim's blast frame, like rocket 1's
            try { LegacyMatch.Launch(o, "missile_launcher", from, m.to, simLeft, FxManager.I); }
            finally { OpenZFxPlayer.NextSimEnd = -1; }
            m.proj = live ? z.NewestProjectile(before) : -1;
            var au = CombatAudio.I;
            if (au) au.PlayPitched("mlrs_salvo", from, LaunchGain, 0f, m.shooter, 0f, LegacyMatch.BankMlrsLaunch);
            m.landAt = Time.time + OpenZFxPlayer.RealFlight(simLeft, game.gameSpeed);
            m.aimed = m.to; m.launched = true; m.steered = m.proj >= 0;
            Launched++; MarkedFired++;
            return true;
        }

        /// <summary>NearestLanding hook: a marked rocket of `shooter` lands on the sim's blast frame where its vehicle is. One that landed
        /// this frame or the frame before (StepMarked in Update, before the deaths are handled in LateUpdate) counts as landing now, from
        /// its point: a robot beside a marked vehicle flies with that rocket, not with a filler rocket farther away.</summary>
        bool NearestMarked(int shooter, Vector3 p, float within, float speed, ref float best, ref float inSeconds, ref Vector3 at)
        {
            bool found = false;
            if (!game || game.World == null) return false;
            foreach (var m in marked)
            {
                if (m.shooter != shooter) continue;
                float t = (float)(m.simEnd - game.World.Time) / speed; var q = m.to; q.y = 0f; float d = Vector3.Distance(p, q);
                if (t < 0f || t > within || d >= best) continue;
                best = d; inSeconds = t; at = m.to - Vector3.up * AimUp; found = true;
            }
            foreach (var r in recent)
            {
                if (r.shooter != shooter || Time.frameCount - r.frame > 1) continue;
                var q = r.at; q.y = 0f; float d = Vector3.Distance(p, q);
                if (d >= best) continue;
                best = d; inSeconds = 0f; at = r.at - Vector3.up * AimUp; found = true;
            }
            return found;
        }
    }
}
