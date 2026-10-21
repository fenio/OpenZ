#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys, round 4: the RTS distance of every frame, the far flock over the sun from frame 0, the arrival of the
// lens over the battle, the stream's crocodile drawn swimming (croc_show) with its swim sound, the radar's zone and the laser robots'
// DRAWN shots, each counted in the take log (editor only, nothing here runs in normal play and no rule is changed).
//  - THE RTS RULE: a flyby never shows a unit "in the face": every captured frame, each living unit in the frame is measured (its
//    drawn height over the frame's height at its depth); the take log gives the largest share per shot and the frames over
//    RtsShareMax (a tenth of the frame height). REQUIRED: none over.
//  - the arrival: the first frame with 60 % or more of the staged living units in the frame, against the first shot of the staged
//    units (the game's fire events). REQUIRED for the sunrise flyby: the lens over the battle at most 1 s after the first shot.
//  - croc_show (cues[]): x z = the place near the animal to borrow (the nearest crocodile of the level that is drawn, free and not
//    hunting), via = the points it swims through (x, z pairs; end where it started: back home), kind = the take (default swim_past:
//    head and back up, the V wake by the level's own rule). The level's crocodile view lends the DRAWN animal (the flyover's own
//    hook, CrocodileView.Lend / LendPose / LendPoseMix / LendReturn): the rule's animal never moves, so the battle stays the same.
//    It glides to a stop at home, turns to the rule's own heading and settles from the swim into the lurk (no jump at a hand-back):
//    it stays lent until the take has ended (or until the rule needs it for a hunt). While it swims, it plays the swim sound every
//    swimming crocodile plays (critter_croc_pass_ripple, the values of CrocodileView.PassLoop).
//  - the laser robots: DRAWN shots (UnitView.LastFireAt moves with every shot the weapons view shows), the rolls behind them (the
//    game's fire events), hits taken, the longest time each was hit without a drawn shot, and which way each drawn shot went seen
//    from the lens: "seen firing" = within SeenFiringDeg of the direction to the lens (side-on or towards it: the gun, its flash and
//    the bolt leaving it are on the lens's side; from behind, the body hides them and the bolts read as incoming fire).
//    REQUIRED in the last camera shot: every laser with an enemy within reach (the game's own test: reach, walls, a clear line)
//    draws a shot, none is hit for more than SilentHitMaxS without a drawn shot, and SeenFiringShareMin or more of the drawn shots
//    with the shooter in the frame are seen firing.
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby
    {
        /// <summary>The RTS rule: a unit fills at most this share of the frame's height.</summary>
        public const float RtsShareMax = 0.10f;
        /// <summary>The swim of a shown crocodile (the level's swim_past take: 1.5 m/s).</summary>
        public const float CrocShowMps = 1.5f;
        /// <summary>The shown crocodile glides to a stop over this many metres at home, then turns and settles over CrocSettleS.</summary>
        public const float CrocGlideM = 1.5f, CrocSettleS = 1.5f;
        /// <summary>A drawn laser shot is seen firing when its direction is within this angle of the direction to the lens.</summary>
        public const float SeenFiringDeg = 110f;
        /// <summary>REQUIRED in the last shot: no laser hit for longer than this without a drawn shot (a knock-down holds a shot up to
        /// about 1.5 s); this share or more of the drawn laser shots with the shooter in the frame seen firing, at least SeenFiringMinShots.</summary>
        public const float SilentHitMaxS = 1.5f, SeenFiringShareMin = 0.6f;
        /// <summary>A laser counts as "an enemy within reach" in the last shot once an enemy stood within its reach (the game's own
        /// test) for this many seconds in all (time to turn and fire: an enemy that steps into reach in the last moment is no miss).</summary>
        public const float ReachMinS = 1.5f;
        public const int SeenFiringMinShots = 5;

        OzEpicFlybyShots shotCount;
        readonly Dictionary<int, float> unitH = new Dictionary<int, float>(); readonly Dictionary<int, int> unitHAt = new Dictionary<int, int>();
        float rtsMax; string rtsWho = "-"; int rtsFrame = -1, rtsOver, rtsFrames; float[] rtsShotMax; string[] rtsShotWho;
        int birds0 = -1, birdsLastFrame = -1, birdsSeenFrames, birdCallFrame = -2;
        int arriveFrame = -1, firstShotFrame = -1, stagedShots0 = -1; bool firstShotPre;
        float winT0;

        // ------------------------------------------------------------------ hook (after the units are placed)
        void Kf4Hook()
        {
            shotCount = gameObject.AddComponent<OzEpicFlybyShots>(); shotCount.W = W;
            rtsShotMax = new float[Mathf.Max(1, shots.Count)]; rtsShotWho = new string[rtsShotMax.Length];
            lSeenShot = new int[rtsShotMax.Length]; lInFrameShot = new int[rtsShotMax.Length];
            winT0 = shots.Count > 0 ? shots[shots.Count - 1].t0 : 0f;
            foreach (var s in sc.setup)
            {   // the zones the scenario awarded: who owns the zone's buildings now (the radar's zone in flyby A)
                if (s.action != "award_flag") continue;
                ZObject best = null; float bd = 14f;
                foreach (var f in W.Flags) { var c = g.CenterWorld(f); float d = new Vector2(c.x - s.x, c.z - s.z).magnitude; if (d < bd) { bd = d; best = f; } }
                if (best == null) continue;
                var sb = new StringBuilder();
                foreach (var b in best.LinkedObjects) sb.Append(b.Type).Append(" #").Append(b.Id).Append(" ").Append(b.Owner).Append("; ");
                Note("zone of flag #" + best.Id + " at " + Where(best) + ": flag " + best.Owner + "; its buildings: " + (sb.Length > 0 ? sb.ToString() : "none"));
            }
            foreach (var p in placed)
                foreach (int id in p.ids) { var o = W.Get(id); if (o != null && o.Type == "laser") lasers.Add(new LaserNote { id = id, tag = p.u.tag }); }
            Note("lasers of the take: " + lasers.Count + " robot(s) (drawn shots from each robot's view, rolls from the game's fire events); the last camera shot " +
                 winT0.ToString("F1") + "-" + sc.seconds.ToString("F1") + " s");
        }

        // ------------------------------------------------------------------ every captured frame (OnBeforeRender, after the lens is placed)
        void Kf4Frame(Vector3 pos, Quaternion rot, float fov)
        {
            float t = Mathf.Max(0, curI) / (float)fps;
            RtsMeasure(fov);
            ArrivalFrame();
            CrocShowFrame(t, 1f / fps);
            LaserFrame(t, pos);
        }

        float UnitHeight(UnitView v)
        {   // the drawn body (no sprites, effects or lines), measured again every 30 frames; the largest height seen counts (a robot
            // that got up from a lounger is measured standing)
            int id = v.Obj.Id;
            if (unitH.TryGetValue(id, out float known) && unitHAt.TryGetValue(id, out int at) && curI - at < 30) return known;
            bool any = false; var b = new Bounds();
            foreach (var r in v.GetComponentsInChildren<Renderer>(false))
            {
                if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is SpriteRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float h = any ? Mathf.Clamp(b.size.y, 0.8f, 6f) : (v.Obj.Kind == ZKind.Robot ? 2.2f : 3.2f);
            if (unitH.TryGetValue(id, out float before)) h = Mathf.Max(h, before);
            unitH[id] = h; unitHAt[id] = curI; return h;
        }

        void RtsMeasure(float fov)
        {
            if (!cam || !g) return;
            float th = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), best = 0f; UnitView bv = null; float bz = 0f;
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!Living(v)) continue;
                float h = UnitHeight(v);
                var vp = cam.WorldToViewportPoint(v.transform.position + Vector3.up * (h * 0.5f));
                if (vp.z <= 0.1f || vp.x < -0.02f || vp.x > 1.02f || vp.y < -0.05f || vp.y > 1.05f) continue;
                float share = h / (2f * vp.z * th);
                if (share > best) { best = share; bv = v; bz = vp.z; }
            }
            rtsFrames++;
            if (best > RtsShareMax) rtsOver++;
            int s = Mathf.Clamp(lastShot, 0, rtsShotMax.Length - 1);
            string who = bv ? bv.Obj.Owner + " " + bv.Obj.Type + " #" + bv.Obj.Id + " at " + bz.ToString("F1") + " m, frame " + curI : "-";
            if (best > rtsShotMax[s]) { rtsShotMax[s] = best; rtsShotWho[s] = who; }
            if (best > rtsMax) { rtsMax = best; rtsWho = who; rtsFrame = curI; }
        }

        bool InFrame(Vector3 world, float margin = 0f)
        {
            if (!cam) return false;
            var vp = cam.WorldToViewportPoint(world);
            return vp.z > 0.1f && vp.x >= -margin && vp.x <= 1f + margin && vp.y >= -margin && vp.y <= 1f + margin;
        }

        // ------------------------------------------------------------------ the arrival over the battle and the first shot
        void ArrivalFrame()
        {
            int sum = 0; if (shotCount != null) foreach (int id in stagedIds) sum += shotCount.ShotsOf(id);
            if (stagedShots0 < 0) { stagedShots0 = sum; firstShotPre = sum > 0; }                   // shots counted since the hook: the pre-roll's
            else if (firstShotFrame < 0 && !firstShotPre && sum > stagedShots0) firstShotFrame = curI;
            if (arriveFrame >= 0 || !g) return;
            int alive = 0, inn = 0;
            foreach (int id in stagedIds)
            {
                if (!g.Views.TryGetValue(id, out var v) || !Living(v)) continue;
                alive++; if (InFrame(v.transform.position + Vector3.up)) inn++;
            }
            if (alive > 0 && inn >= 0.6f * alive) arriveFrame = curI;
        }

        // ------------------------------------------------------------------ the far flock (Birds.cs calls this from every drawn frame)
        /// <summary>Birds.cs: the far flock's birds over the sun disc this frame: frame 0 into the take log; the seconds with five or
        /// more over the disc WHILE THE DISC IS IN THE FRAME; when the flock's first call played.</summary>
        void BirdsFrame(float t, int over)
        {
            if (curI < 0 || curI == birdsLastFrame) return;
            birdsLastFrame = curI;
            if (birdCallFrame == -2 && fbCall1) birdCallFrame = curI - 1;                                // set by the frame before (after this hook); -1 = in the pre-roll
            if (curI == 0 && birds0 < 0)
            {
                birds0 = over;
                Note("the far flock at frame 0: " + over + " birds over the sun disc (REQUIRED 5 or more: the birds on the sun from the first frame)");
            }
            if (over >= 5 && cam && InFrame(cam.transform.position + sunDir * 200f)) birdsSeenFrames++;
        }

        // ------------------------------------------------------------------ croc_show: the stream's crocodile drawn swimming
        Transform crocLent; ZCroc crocRule; Vector3[] crocPts; float[] crocCum;
        float crocT0 = -1f, crocYaw, crocLen, crocSeenFrom = -1f, crocSeenTo = -1f, crocNear = float.MaxValue, crocHoldFrom = -1f, crocHoldYaw0, crocBackAt = -1f;
        float crocRippleFrom = -1f, crocRippleTo = -1f; string crocRippleClip; AudioSource crocRipple;
        string crocTake = "swim_past", crocBackWhy; int crocFrames, crocSeen, crocHoldFrames, crocHoldSeen; bool crocBack;

        static float CrocRuleYaw(double heading) => Mathf.Atan2((float)System.Math.Cos(heading), (float)-System.Math.Sin(heading)) * Mathf.Rad2Deg;   // CrocodileView.Yaw

        /// <summary>The cue: null = try again next frame (the animal is busy), within window_s (default 3 s) of the cue's second.</summary>
        string CrocShowCue(Cue q, float t)
        {
            if (crocLent || crocT0 >= 0f) return "a crocodile is already shown";
            var pts = new List<Vector3> { new Vector3(q.x, 0f, q.z) };
            if (q.via != null) for (int i = 0; i + 1 < q.via.Length; i += 2) pts.Add(new Vector3(q.via[i], 0f, q.via[i + 1]));
            if (pts.Count < 2) return "croc_show needs via points (x, z pairs)";
            crocLent = CrocodileView.Lend(pts[0]);
            if (!crocLent)
            {
                if (t < q.t + (q.window_s > 0f ? q.window_s : 3f)) return null;
                return "NOT shown: no crocodile to lend near " + q.x + "," + q.z + " within " + (q.window_s > 0f ? q.window_s : 3f) + " s (the nearest one hunting, or none drawn)";
            }
            crocRule = null; double bd = double.MaxValue;                                       // the rule's animal behind the drawn one (the nearest home): its hunt ends the show
            foreach (var c in W.Crocs) { double dx = c.X / 8.0 - crocLent.position.x, dz = -c.Y / 8.0 - crocLent.position.z, d = dx * dx + dz * dz; if (d < bd) { bd = d; crocRule = c; } }
            var home = crocLent.position;                                                       // the swim starts and (when the last point is the cue's place) ends at its own drawn place
            var last = pts[pts.Count - 1];
            if (pts.Count > 2 && new Vector2(last.x - q.x, last.z - q.z).magnitude < 0.75f) pts[pts.Count - 1] = new Vector3(home.x, 0f, home.z);
            pts[0] = new Vector3(home.x, 0f, home.z);
            crocPts = pts.ToArray(); crocCum = new float[pts.Count]; crocLen = 0f;
            for (int i = 1; i < pts.Count; i++) { crocLen += Vector3.Distance(pts[i - 1], pts[i]); crocCum[i] = crocLen; }
            crocT0 = t; crocTake = string.IsNullOrEmpty(q.kind) ? "swim_past" : q.kind;
            crocYaw = crocLent.eulerAngles.y;                                                   // its drawn heading: the swim turns from it
            crocRipple = L16AnimalKit.Loop("critter_croc_pass_ripple", transform, home, 0.3f, 6f, 60f, "sd_croc_wake_loop_a", "sd_croc_wake_loop_b");   // the swim sound of every swimming crocodile (CrocodileView.PassLoop)
            if (crocRipple) { crocRippleFrom = t; crocRippleClip = crocRipple.clip ? crocRipple.clip.name : "?"; }
            return "the crocodile drawn at " + home.x.ToString("F1") + "," + home.z.ToString("F1") + " is lent: it swims " + crocLen.ToString("F1") + " m through " + (pts.Count - 1) +
                   " legs in about " + (crocLen / CrocShowMps + CrocGlideM / CrocShowMps).ToString("F1") + " s (" + crocTake + ", the V wake by the level's rule; its swim sound " +
                   (crocRipple ? crocRippleClip : "NOT made (no clip)") + "); drawn only, the rule's animal stays";
        }

        Vector3 CrocAt(float s)
        {
            s = Mathf.Clamp(s, 0f, crocLen);
            int i = 1; while (i < crocCum.Length - 1 && s > crocCum[i]) i++;
            float seg = Mathf.Max(1e-3f, crocCum[i] - crocCum[i - 1]);
            return Vector3.Lerp(crocPts[i - 1], crocPts[i], (s - crocCum[i - 1]) / seg);
        }

        /// <summary>The distance swum and the speed at `tl` seconds into the show: 1.5 m/s, gliding to a stop over the last CrocGlideM.</summary>
        void CrocProgress(float tl, out float s, out float speed)
        {
            float v = CrocShowMps, d = Mathf.Min(CrocGlideM, crocLen), tGlide = (crocLen - d) / v, a = v * v / (2f * Mathf.Max(0.01f, d));
            if (tl <= tGlide) { s = v * tl; speed = v; return; }
            float tau = Mathf.Min(tl - tGlide, v / a);
            s = crocLen - d + v * tau - 0.5f * a * tau * tau; speed = Mathf.Max(0f, v - a * tau);
        }

        void StopCrocRipple(float t)
        {
            if (crocRipple) { L16AnimalKit.StopLoop("critter_croc_pass_ripple", ref crocRipple); crocRippleTo = t; }
            crocRipple = null;
        }

        void CrocHandBack(float t, string why)
        {
            StopCrocRipple(t);
            if (crocLent) CrocodileView.LendReturn(crocLent);
            crocLent = null; crocBack = true; crocBackAt = t; crocBackWhy = why;
        }

        void CrocShowFrame(float t, float dt)
        {
            if (!crocLent || crocT0 < 0f) return;
            if (crocRule != null && crocRule.Busy)
            {   // the rule needs its animal (a hunt): the loan ends at once; never placed again
                CrocHandBack(t, "the rule took crocodile " + crocRule.Index + " (" + crocRule.State + ")");
                Note("croc_show: handed back at " + t.ToString("F2") + " s (frame " + curI + "): " + crocBackWhy);
                return;
            }
            CrocProgress(t - crocT0, out float s, out float speed);
            Vector3 p; string lvl = g ? g.level : "L16";
            bool swimming = s < crocLen - 0.01f;
            if (swimming)
            {
                p = CrocAt(s); var dir = CrocAt(s + 1.6f) - CrocAt(s - 0.4f);
                if (dir.sqrMagnitude > 1e-4f) crocYaw = Mathf.MoveTowardsAngle(crocYaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 140f * dt);
                p.y = OzWaterLevel.At(lvl, p.x, p.z, DetailKit.WaterY(lvl));
                crocLent.SetPositionAndRotation(p, Quaternion.Euler(0f, crocYaw, 0f));
                CrocodileView.LendPose(crocLent, crocTake, t - crocT0, speed);
                if (crocRipple)
                {   // CrocodileView.PassLoop's values at this speed
                    crocRipple.transform.position = p; float k = Mathf.Clamp01(speed / 4.5f);
                    L16AnimalKit.LoopVolume(crocRipple, 0.22f + 0.25f * k, 0.85f + 0.3f * k);
                }
                crocFrames++;
                if (InFrame(p + Vector3.up * 0.3f, -0.03f)) { crocSeen++; if (crocSeenFrom < 0f) crocSeenFrom = t; crocSeenTo = t; if (cam) crocNear = Mathf.Min(crocNear, cam.WorldToViewportPoint(p).z); }
                return;
            }
            // home: held there until the take has ended (a hand-back would snap it to the rule's heading and take), turning to the rule's
            // own heading and settling from the swim into the lurk
            if (crocHoldFrom < 0f)
            {
                crocHoldFrom = t; crocHoldYaw0 = crocYaw; StopCrocRipple(t);
                Note("croc_show: home at " + t.ToString("F2") + " s (frame " + curI + "): held there, turning to its own heading and settling into the lurk over " + CrocSettleS.ToString("F1") + " s; handed back after the take");
            }
            float u = Mathf.Clamp01((t - crocHoldFrom) / CrocSettleS), e = u * u * (3f - 2f * u);
            crocYaw = Mathf.LerpAngle(crocHoldYaw0, crocRule != null ? CrocRuleYaw(crocRule.Heading) : crocHoldYaw0, e);
            p = crocPts[crocPts.Length - 1]; p.y = OzWaterLevel.At(lvl, p.x, p.z, DetailKit.WaterY(lvl));
            crocLent.SetPositionAndRotation(p, Quaternion.Euler(0f, crocYaw, 0f));
            CrocodileView.LendPoseMix(crocLent, crocTake, t - crocT0, "lurk", t - crocHoldFrom, e, 0f);
            crocHoldFrames++; if (InFrame(p + Vector3.up * 0.3f, -0.03f)) crocHoldSeen++;
        }

        // ------------------------------------------------------------------ the laser robots
        sealed class LaserNote
        {
            public int id, hp = -1, rolls0 = -1, hits, drawn, drawnWin, inFrameWin, seenWin;
            public float lastFireAt = float.NaN, silentFrom = -1f, silentMax, silentMaxWin, reachS;
            public string tag; public bool reachWin, dead;
        }
        readonly List<LaserNote> lasers = new List<LaserNote>();
        int[] lSeenShot, lInFrameShot; readonly StringBuilder laserTrack = new StringBuilder(); int laserTrackN;

        /// <summary>An enemy the laser can fire at now by the game's own test (ZObject.WithinAttackRadius: its reach, walls, a clear
        /// line; CanAttackObject). Reads only (the line test's cache gives the rule the same answers).</summary>
        bool EnemyInReach(ZObject o)
        {
            foreach (var e in W.Objects)
            {
                if (e == null || e.Destroyed || e.KillAt >= 0 || !e.IsUnit || e.Owner == o.Owner || e.Owner == Team.Neutral) continue;
                if (o.CanAttackObject(e) && o.WithinAttackRadius(e)) return true;
            }
            return false;
        }

        void LaserFrame(float t, Vector3 lens)
        {
            if (lasers.Count == 0 || shotCount == null || !g) return;
            bool win = t >= winT0 - 1e-4f; int shot = Mathf.Clamp(lastShot, 0, lSeenShot.Length - 1);
            Vector3 sum = Vector3.zero; int nv = 0;
            foreach (var L in lasers)
            {
                if (L.dead) continue;
                var o = W.Get(L.id);
                g.Views.TryGetValue(L.id, out var v);
                if (L.hp < 0)
                {   // frame 0: the starting values (hits and shots of the pre-roll are not this take's)
                    L.hp = o != null ? o.Health : 0; L.rolls0 = shotCount.ShotsOf(L.id); L.lastFireAt = v ? v.LastFireAt : -100f;
                    continue;
                }
                if (o == null || o.Destroyed || o.KillAt >= 0) { L.dead = true; continue; }
                bool drawnNow = v && !Mathf.Approximately(v.LastFireAt, L.lastFireAt);
                if (v) L.lastFireAt = v.LastFireAt;
                bool hitNow = o.Health < L.hp; L.hp = o.Health;
                if (win && EnemyInReach(o)) { L.reachS += 1f / fps; if (L.reachS >= ReachMinS) L.reachWin = true; }
                if (drawnNow)
                {
                    L.drawn++; L.silentFrom = -1f;
                    Vector3 at = v.transform.position, to = o.AttackObject != null && !o.AttackObject.Destroyed ? g.CenterWorld(o.AttackObject) : at + v.transform.forward;
                    Vector2 fire = new Vector2(to.x - at.x, to.z - at.z), look = new Vector2(lens.x - at.x, lens.z - at.z);
                    bool seen = fire.sqrMagnitude > 1e-4f && look.sqrMagnitude > 1e-4f && Vector2.Angle(fire, look) <= SeenFiringDeg;
                    if (InFrame(at + Vector3.up * 1.2f))
                    {
                        lInFrameShot[shot]++; if (seen) lSeenShot[shot]++;
                        if (win) { L.inFrameWin++; if (seen) L.seenWin++; }
                    }
                    if (win) L.drawnWin++;
                }
                if (hitNow)
                {
                    L.hits++;
                    if (L.silentFrom < 0f) L.silentFrom = t;
                    L.silentMax = Mathf.Max(L.silentMax, t - L.silentFrom);
                    if (win) L.silentMaxWin = Mathf.Max(L.silentMaxWin, t - Mathf.Max(L.silentFrom, winT0));
                }
                if (win && v && Living(v)) { sum += v.transform.position + Vector3.up * 1.2f; nv++; }
            }
            int every = Mathf.Max(1, Mathf.RoundToInt(fps * 0.25f));
            if (win && curI % every == 0)
            {   // where the laser squad is in the frame every 0.25 s of the last shot (strip_kf4.py crops its pictures there)
                string at = "-";
                if (nv > 0 && cam) { var vp = cam.WorldToViewportPoint(sum / nv); if (vp.z > 0.1f) at = vp.x.ToString("F2") + "," + vp.y.ToString("F2"); }
                laserTrack.Append(laserTrackN++ == 0 ? "" : " ").Append(t.ToString("F2")).Append(":").Append(at);
            }
        }

        // ------------------------------------------------------------------ the take log (Finish) and the clean-up (Restore)
        void Kf4Summary()
        {
            var sb = new StringBuilder();
            for (int s = 0; s < rtsShotMax.Length; s++) sb.Append("shot ").Append(s + 1).Append(" ").Append(rtsShotMax[s].ToString("F3")).Append(" (").Append(rtsShotWho[s] ?? "-").Append("); ");
            Note("RTS distance: the largest unit in the frame " + rtsMax.ToString("F3") + " of the frame height (" + rtsWho + "); frames with a unit over " + RtsShareMax.ToString("F2") +
                 " of the frame height: " + rtsOver + " of " + rtsFrames + " (REQUIRED 0); per shot: " + sb);
            float fx = arriveFrame >= 0 ? arriveFrame / (float)fps : -1f, fy = firstShotPre ? 0f : firstShotFrame >= 0 ? firstShotFrame / (float)fps : -1f;
            Note("the battle in the frame from " + (fx >= 0f ? fx.ToString("F2") + " s" : "NEVER") + " (60 % or more of the staged living units); first shot " +
                 (firstShotPre ? "0.00 s (already in the pre-roll)" : fy >= 0f ? fy.ToString("F2") + " s" : "none") + " (the staged units' first fire event)" +
                 (fx >= 0f && fy >= 0f ? "; the lens arrives " + (fx - fy).ToString("F2") + " s after the first shot (REQUIRED for the sunrise flyby: 1.0 s or less)" : ""));
            if (sc.sun_birds != null && sc.sun_birds.on)
            {
                if (birds0 < 0) Note("the far flock at frame 0: not measured (no bird frame at frame 0)");
                Note("the far flock SEEN over the sun: five or more birds over the disc while the disc is in the frame for " + (birdsSeenFrames / (float)fps).ToString("F2") +
                     " s (REQUIRED 1.5 or more); the flock's first call (sd_jungle_bird_d) " +
                     (birdCallFrame >= 0 ? "at " + (birdCallFrame / (float)fps).ToString("F2") + " s (inside the clip)" : birdCallFrame == -1 ? "LOST: played in the pre-roll, before the sound capture" : "NOT played"));
            }
            if (crocT0 >= 0f || crocBack)
            {
                Note("croc_show: in the frame " + (crocSeen > 0 ? crocSeenFrom.ToString("F1") + "-" + crocSeenTo.ToString("F1") + " s (" + crocSeen + " of " + crocFrames + " frames of its swim), nearest " + crocNear.ToString("F1") + " m" : "NEVER (" + crocFrames + " frames of its swim)") +
                     "; " + (crocHoldFrom >= 0f ? "home at " + crocHoldFrom.ToString("F1") + " s, held there " + crocHoldFrames + " frames (in the frame " + crocHoldSeen + "), turned to its own heading and settled into the lurk; " : "") +
                     (crocBack ? "handed back at " + crocBackAt.ToString("F2") + " s: " + crocBackWhy : "handed back after the take (no jump in the clip)"));
                Note("croc_show sound: " + (crocRippleFrom >= 0f ? "critter_croc_pass_ripple LOOP from " + crocRippleFrom.ToString("F1") + " s to " + (crocRippleTo >= 0f ? crocRippleTo.ToString("F1") + " s" : "the end") + " (" + crocRippleClip + ")" : "NONE: the swim sound was not made (clip missing)"));
            }
            if (lasers.Count > 0)
            {
                int reached = 0, reachedDrew = 0, silentOver = 0, inFrame = 0, seen = 0; var ls = new StringBuilder();
                foreach (var L in lasers)
                {
                    int rolls = shotCount != null && L.rolls0 >= 0 ? shotCount.ShotsOf(L.id) - L.rolls0 : 0;
                    if (L.reachWin) { reached++; if (L.drawnWin > 0) reachedDrew++; }
                    if (L.silentMaxWin > SilentHitMaxS) silentOver++;
                    inFrame += L.inFrameWin; seen += L.seenWin;
                    ls.Append(L.tag).Append(" #").Append(L.id).Append(": drawn ").Append(L.drawn).Append(" (rolls ").Append(rolls).Append("), in the last shot drawn ").Append(L.drawnWin)
                      .Append(", in the frame ").Append(L.inFrameWin).Append(", seen firing ").Append(L.seenWin).Append(", enemy within reach ").Append(L.reachS.ToString("F1")).Append(" s")
                      .Append(", hit ").Append(L.hits).Append(", longest hit without a drawn shot ").Append(L.silentMax.ToString("F1")).Append(" s (last shot ").Append(L.silentMaxWin.ToString("F1")).Append(" s)")
                      .Append(L.dead ? " (fell)" : "").Append("; ");
                }
                Note("lasers in the last shot (" + winT0.ToString("F1") + "-" + sc.seconds.ToString("F1") + " s): of the " + reached + " with an enemy within reach for " + ReachMinS.ToString("F1") + " s or more (the game's own test: reach, walls, a clear line), " + reachedDrew + " drew a shot (REQUIRED all); " +
                     "lasers hit for more than " + SilentHitMaxS.ToString("F1") + " s without a drawn shot: " + silentOver + " (REQUIRED 0); drawn shots with the shooter in the frame " + inFrame +
                     ", seen firing (side-on or towards the lens, " + SeenFiringDeg.ToString("F0") + " deg or less) " + seen + " = " + (inFrame > 0 ? (100f * seen / inFrame).ToString("F0") : "0") +
                     " % (REQUIRED " + (100f * SeenFiringShareMin).ToString("F0") + " % or more of " + SeenFiringMinShots + " or more)");
                var ps = new StringBuilder();
                for (int s = 0; s < lSeenShot.Length; s++) ps.Append("shot ").Append(s + 1).Append(" ").Append(lSeenShot[s]).Append(" of ").Append(lInFrameShot[s]).Append("; ");
                Note("lasers seen firing per camera shot (drawn shots with the shooter in the frame): " + ps + ls);
                Note("laser squad in the frame every 0.25 s of the last shot (viewport x,y; - = none alive): " + (laserTrack.Length > 0 ? laserTrack.ToString() : "-"));
            }
        }

        void Kf4Restore()
        {
            StopCrocRipple(-1f);
            if (crocLent) { CrocodileView.LendReturn(crocLent); crocLent = null; }
            if (shotCount) { Destroy(shotCount); shotCount = null; }
        }
    }
}
#endif
