// OpenZ (GPL-3.0) - version 1.1 item 214
// the fort blast THROWS the trees.
// Each tree still goes when the wave's front reaches it (OzTreeFall.Blast.cs: the queue, the pace of 4 a frame and 8 ms, the camp
// keep-out of run 19), on the same flat front as the robots (BlastSurfaceTiming, off by default, makes a tree up a ridge wait for
// the front drawn ALONG the ground instead: a switch for a side-by-side look only). Then, by its flat distance from the blast
// (ThrowNear, ThrowMid):
//  * near: uprooted (UprootShare of them: a burst of earth and clods at the foot) or snapped at the base, and THROWN: launched
//    outward and up, tumbling crown first about an axis across the push (turned a little per tree), it flies, lands, bounces once
//    when it lands hard, slams down flat and slides on until friction stops it (the tumble is held to what keeps the foot from swinging
//    back towards the blast); earth, bark and leaves burst off at the launch and where it lands; the foot always ends at least the
//    broken foot's shove (FarSkid.x) out from where it stood, so no thrown tree ends where it stood;
//  * middle: snapped at the base and thrown less far (the same flight, slower and lower);
//  * far: bent hard and broken over in the wave's direction: a fast fall from the foot (a strong first spin, no slow tip-over in
//    place) while the broken foot is shoved outward (FarSkid), a hard landing and a rebound.
// Never INTO the endings' set: a throw whose reach (flight + slide + half the tree, with a 20 % margin, and never less than the tree
// lying from its shoved foot) would come within the crown + BlastSceneClear of the set, an endings' camera spot or the line between
// them is made a middle throw, then a short throw, and only then broken over (the run-19 rule already keeps up a tree whose fall
// alone would reach them); where the trees came to rest is counted (ThrowSetClosest).
// The end of the level's ground (OzBlastGround.World): a throw that would leave it is made shorter to land inside it; where that
// leaves less than a short throw, and for a near or middle tree that has no ground under it (past the end of the ground's
// collider: the outer jungle of the rims), it is a SHORT snapped throw (the launch scaled down so the foot is thrown ShortThrow
// metres, never more than its own throw). Past the end of the ground the log lands and lies on its own foot's height.
// The felled trees lie as logs and sink away like the other fallen trees (OzTreeFall.life, then 3 s). While they lie on the ground
// nothing runs for them each frame (they sleep until they sink); a log in the water rocks a little with a faint ring at its crown now
// and then where the camera looks, as the run-19 fall's floating logs do.
// Sounds: one grouped layer, never one per tree: the trees' notes (crack at the break, earth for an uprooting, whoosh for a throw,
// thud and leaves where they land, a splash in the water, the scrape of a log sliding on over the ground, with dust at its foot) are
// gathered per kind for a short window and played as ONE sound at the
// middle of the notes on screen (louder for more trees), at most a few a second per kind and ThrowSoundsMost per blast; the counts
// per kind (notes, sounds played, notes folded or capped) in ThrowStatus.
// Switch: BlastThrow (true = on; false = the run-19 tip-over from the foot, for the before / after count).
// Play check: return OpenZ.OzTreeFall.ThrowStatus();
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public partial class OzTreeFall
    {
        public static bool BlastThrow = true;
        /// <summary>false (default) = the trees go on the flat front, exactly as the robots; true = they wait for the front drawn along the
        /// ground (up to about half a second on a ridge): a switch for a side-by-side look only.</summary>
        public static bool BlastSurfaceTiming = false;
        /// <summary>Flat metres from the blast: thrown inside ThrowNear, snapped and thrown less far inside ThrowMid, bent and broken over out to the reach.</summary>
        public static float ThrowNear = 45f, ThrowMid = 78f;
        /// <summary>Launch speeds of the tree's middle (m/s outward, m/s up) and its tumble (rad/s): x at 8 m (near) or at ThrowNear (middle), y at
        /// ThrowNear (near) or at ThrowMid (middle). Each tree gets 0.85 to 1.15 of them.</summary>
        public static Vector2 NearOut = new Vector2(20f, 10f), NearUp = new Vector2(9f, 4.5f), NearSpin = new Vector2(4.2f, 2.8f);
        public static Vector2 MidOut = new Vector2(9f, 5f), MidUp = new Vector2(4.5f, 2.2f), MidSpin = new Vector2(3.2f, 2.4f);
        /// <summary>The far ring: first spin of the fall over (rad/s) and the shove of the broken foot (m), x at ThrowMid, y at the reach.</summary>
        public static Vector2 FarSpin = new Vector2(3.2f, 2.4f), FarSkid = new Vector2(2.2f, 0.9f);
        public static float UprootShare = 0.55f, SlideDecel = 6f, BounceUp = 0.3f, BounceKeep = 0.6f, SpinKeep = 0.45f, AirDrag = 0.15f;
        /// <summary>The tree's middle as a share of its height, the crown's middle, the metres a throw keeps from the end of the ground,
        /// the seconds the trunk takes to slam down flat after it lands.</summary>
        public static float ComK = 0.4f, CrownK = 0.72f, EdgeClear = 4f, SettleSeconds = 0.35f;
        /// <summary>The short snapped throw (no ground under the tree, the end of the level's ground, or the set): how far it throws the foot
        /// (m, x..y per tree); the most a log's slide starts at when its foot is pushed on to the broken foot's shove (m/s); the slide speed
        /// from which a sliding log scrapes and raises dust (m/s).</summary>
        public static Vector2 ShortThrow = new Vector2(3f, 4.5f);
        public static float SlideMost = 8f, SlideNoteSpeed = 2f;
        public static int ThrowSoundsMost = 70;

        /// <summary>Counts of the blast: thrown near, thrown in the middle ring (short throws included), broken over; for the set: made
        /// shorter (a middle or a short throw), broken over; no ground under the tree: a short throw; the end of the ground: shorter to land
        /// inside it, a short throw; uprooted; landed, in the water, bounces, slides that scraped; trees that waited for the front along the
        /// ground and the longest wait; the most trees moving at once, the longest flight (s) and throw (m), the shortest throw of a thrown
        /// tree's foot (m), the most ms one frame spent moving them; the closest a resting trunk (foot to top) lies to the set, the endings'
        /// camera spots or the lines between them (m; infinity = no set).</summary>
        public static int ThrownNear, ThrownMid, BrokenOver, CampShorter, CampHeld, NoGroundShort, EdgeShorter, EdgeHeld, Uprooted, ThrowLanded, ThrowWet, ThrowBounces, ThrowSlides, ThrowLagged, ThrowMovingMost;
        public static float ThrowLagMost, ThrowFlightMost, ThrowTravelMost, ThrowTravelLeast = float.PositiveInfinity, ThrowMsMost, ThrowSetClosest = float.PositiveInfinity;
        static readonly string[] SoundKind = { "crack", "earth", "whoosh", "thud", "splash", "leaves", "slide" };
        public static readonly int[] SoundNotes = new int[7], SoundPlays = new int[7], SoundFolded = new int[7];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetThrow()
        {
            ThrownNear = ThrownMid = BrokenOver = CampShorter = CampHeld = NoGroundShort = EdgeShorter = EdgeHeld = Uprooted = ThrowLanded = ThrowWet = ThrowBounces = ThrowSlides = ThrowLagged = ThrowMovingMost = 0;
            ThrowLagMost = ThrowFlightMost = ThrowTravelMost = ThrowMsMost = 0f; ThrowTravelLeast = ThrowSetClosest = float.PositiveInfinity; soundsThisBlast = 0;
            for (int i = 0; i < SoundKind.Length; i++) { SoundNotes[i] = SoundPlays[i] = SoundFolded[i] = 0; groups[i] = new Group(); }
        }

        sealed class Flyer
        {
            public Stand t; public Transform h; public Quaternion r0;
            public int mode, phase;                     // mode 0 thrown, 1 snapped + thrown, 2 broken over; phase 0 air, 1 settling, 2 falling over, 3 rebound, 4 lying, 5 sinking
            public Vector3 c, v, dir, axis, away, foot0, pivot0, skidTo, footNow; public float th, w, L, hc, hcc, rc, clock, tc, s, landTh, amp, footY, th0, thT, y0, wake, depth, lift;
            public int bounces; public bool wet, noGround, uproot; public float pool, ringAt, dustAt;
        }
        readonly List<Flyer> flying = new List<Flyer>(), lying = new List<Flyer>(), floating = new List<Flyer>();
        float nextWake;

        // ------------------------------------------------------------------------------------------------ the queue's front along the ground
        /// <summary>BlastTick: a tree up a slope waits for the front drawn along the ground (once); true = put back in the queue.</summary>
        bool Lagged(ref BlastItem q)
        {
            float lag = OpenZFortShockwave.SurfaceLag(q.foot);
            if (lag < 0.01f) return false;
            q.lagged = true; q.at += lag; ThrowLagged++; if (lag > ThrowLagMost) ThrowLagMost = lag;
            int i = blastQueue.Count; while (i > 0 && blastQueue[i - 1].at > q.at) i--;
            blastQueue.Insert(i, q);
            return true;
        }

        // ------------------------------------------------------------------------------------------------ the launch
        float GroundFor(Flyer f, float x, float z) => !f.noGround && OzBlastGround.Y(x, z, out float y) ? y : f.footY;

        static float PredictReach(float L, float vout, float vup)
        {
            float tf = (vup + Mathf.Sqrt(vup * vup + 2f * 9.81f * ComK * L)) / 9.81f;
            float slide = 0.8f * BounceKeep * vout; slide = slide * slide / (2f * SlideDecel);
            return 1.2f * (vout * tf + slide) + 0.5f * L;
        }

        /// <summary>The throw's flight + slide in PredictReach (without the half tree and the margin).</summary>
        static float ThrowPart(float L, float vout, float vup) => (PredictReach(L, vout, vup) - 0.5f * L) / 1.2f;

        /// <summary>The launch scaled down (never up) so the throw's flight + slide is `part` metres.</summary>
        static void Shorten(float L, float part, ref float vout, ref float vup)
        {
            if (ThrowPart(L, vout, vup) <= part) return;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 12; i++) { float m = 0.5f * (lo + hi); if (ThrowPart(L, vout * m, vup * m) > part) hi = m; else lo = m; }
            vout *= lo; vup *= lo;
        }

        /// <summary>The throw's reach for the set: never less than the tree lying from its foot shoved FarSkid.x out.</summary>
        static float Reach(Flyer f, float vout, float vup) => Mathf.Max(PredictReach(f.L, vout, vup), f.L + f.rc + FarSkid.x);

        static void Plan(float d, System.Random rng, out int mode, out float vout, out float vup, out float spin, out float skid)
        {
            float R() => 0.85f + 0.3f * (float)rng.NextDouble();
            vout = vup = skid = 0f;
            if (d < ThrowNear)
            {
                float u = Mathf.Clamp01((d - 8f) / Mathf.Max(1f, ThrowNear - 8f));
                mode = 0; vout = Mathf.Lerp(NearOut.x, NearOut.y, u) * R(); vup = Mathf.Lerp(NearUp.x, NearUp.y, u) * R(); spin = Mathf.Lerp(NearSpin.x, NearSpin.y, u) * R();
            }
            else if (d < ThrowMid)
            {
                float u = Mathf.Clamp01((d - ThrowNear) / Mathf.Max(1f, ThrowMid - ThrowNear));
                mode = 1; vout = Mathf.Lerp(MidOut.x, MidOut.y, u) * R(); vup = Mathf.Lerp(MidUp.x, MidUp.y, u) * R(); spin = Mathf.Lerp(MidSpin.x, MidSpin.y, u) * R();
            }
            else
            {
                float u = Mathf.Clamp01((d - ThrowMid) / Mathf.Max(1f, BlastFellRadius - ThrowMid));
                mode = 2; spin = Mathf.Lerp(FarSpin.x, FarSpin.y, u) * R(); skid = Mathf.Lerp(FarSkid.x, FarSkid.y, u);
            }
        }

        /// <summary>The throw's flat line (from the foot along away, reach long) comes within radius + BlastSceneClear of the set, an
        /// endings' camera spot, the line between them, or the endings' camera where it stands now.</summary>
        bool ThrowReaches(Vector3 foot, Vector3 away, float reach, float radius)
        {
            Vector2 f0 = new Vector2(foot.x, foot.z), f1 = f0 + new Vector2(away.x, away.z) * reach; float clear = radius + BlastSceneClear;
            for (int i = 0; i < sceneSpotCount; i++)
                if (SegDist(f0, f1, new Vector2(sceneSpots[0].x, sceneSpots[0].z), new Vector2(sceneSpots[i].x, sceneSpots[i].z)) < clear) return true;
            var cam = ZodBriefing.EndingHolding ? Camera.main : null;
            if (cam) { var p = new Vector2(cam.transform.position.x, cam.transform.position.z); if (SegDist(f0, f1, p, p) < clear) return true; }
            return false;
        }

        /// <summary>FellByBlast (BlastThrow on): the tree is thrown, snapped and thrown, or broken over, by its distance from the blast.</summary>
        void Throw(Stand t, Vector3 away, Vector3 from)
        {
            var h = t.holder; var foot = h.position;
            var f = new Flyer { t = t, h = h, r0 = h.rotation, L = Mathf.Max(2f, t.height), foot0 = foot, footY = foot.y, away = away };
            f.hc = ComK * f.L; f.hcc = CrownK * f.L; f.rc = Mathf.Min(t.radius, f.hcc * 0.9f);
            var rng = new System.Random(t.tx * 7919 + t.ty * 104729 + BlastFelled * 31 + 7);
            f.dir = Quaternion.AngleAxis(((float)rng.NextDouble() - 0.5f) * 24f, Vector3.up) * away;     // the tumble turns a little off the push
            f.axis = Vector3.Cross(Vector3.up, f.dir).normalized;                                        // + angle tips the crown towards dir
            f.noGround = !OzBlastGround.Y(foot.x, foot.z, out _);
            float d = new Vector2(foot.x - from.x, foot.z - from.z).magnitude;
            Plan(d, rng, out int mode, out float vout, out float vup, out float spin, out float skid);
            float shortPart = Mathf.Lerp(ShortThrow.x, ShortThrow.y, (float)rng.NextDouble()) + f.hc;   // the foot's short throw + the lean the tumble lays the tree over by
            if (mode == 0 && ThrowReaches(foot, away, Reach(f, vout, vup), f.rc))
            {   // towards the set: a middle throw
                Plan(ThrowNear + 1f, rng, out mode, out vout, out vup, out spin, out skid); CampShorter++;
            }
            if (mode < 2)
            {
                if (f.noGround) { mode = 1; Shorten(f.L, shortPart, ref vout, ref vup); NoGroundShort++; }   // past the ground's collider: a short snapped throw (it lands on its foot's height)
                else
                {   // the end of the level's ground: shorter to land inside it; where that leaves less than a short throw, a short throw
                    float room = OzBlastGround.Room(foot, away) - EdgeClear;
                    if (PredictReach(f.L, vout, vup) > room)
                    {
                        float fit = (room - 0.5f * f.L) / 1.2f;
                        if (fit >= shortPart) { Shorten(f.L, fit, ref vout, ref vup); EdgeShorter++; }
                        else { mode = 1; Shorten(f.L, shortPart, ref vout, ref vup); EdgeHeld++; }
                    }
                }
                if (mode == 1 && ThrowReaches(foot, away, Reach(f, vout, vup), f.rc))
                {   // still towards the set: a short throw, and only if even that reaches it, broken over
                    Shorten(f.L, shortPart, ref vout, ref vup);
                    if (ThrowReaches(foot, away, Reach(f, vout, vup), f.rc)) { mode = 2; spin = FarSpin.x; skid = FarSkid.x; CampHeld++; }
                    else CampShorter++;
                }
            }
            if (mode == 2 && skid > 0f && ThrowReaches(foot, away, f.L + f.rc + skid, f.rc)) skid = 0f;   // past the end of the ground the foot is shoved on its own height
            f.mode = mode;
            var cam = Camera.main; bool seen = DetailKit.OnScreen(cam, foot) || DetailKit.OnScreen(cam, foot + Vector3.up * f.L);
            var fx = OpenZFxPlayer.I;
            Note(0, foot + Vector3.up * (f.L * 0.3f), mode == 0 ? 1f : mode == 1 ? 0.85f : 0.7f);
            if (mode < 2)
            {   // launched: the middle rises and flies outward, the crown goes over first
                spin = Mathf.Min(spin, (vout + 1f) / f.hc);                                              // the foot never swings back towards the blast
                f.c = foot + Vector3.up * f.hc; f.v = away * vout + Vector3.up * vup; f.w = spin; f.phase = 0;
                f.uproot = mode == 0 && rng.NextDouble() < UprootShare;
                if (mode == 0) ThrownNear++; else ThrownMid++;
                if (f.uproot)
                {   // the root ball tears out: earth and clods
                    Uprooted++; Note(1, foot, 1f);
                    if (seen) { if (fx) { fx.Dust(foot + Vector3.up * 0.3f, 1.1f); fx.Dust(foot + away * 1.2f, 0.7f); } if (bits != null) bits.Burst(foot + Vector3.up * 0.4f, 10, 1, 3.6f, 0.28f, rng); }
                }
                else if (seen && bits != null) bits.Burst(foot + Vector3.up * 0.5f, 8, 0, 3f, 0.22f, rng);   // splinters at the snap
                if (seen && bits != null) bits.Burst(foot + Vector3.up * (f.L * 0.8f), 8, 3, 4f, 0.24f, rng);   // leaves torn off the crown
                Note(2, foot + Vector3.up * (f.L * 0.5f), Mathf.Clamp01(vout / 16f) + 0.3f);
            }
            else
            {   // broken over: the foot snaps and is shoved outward, the tree goes over fast
                BrokenOver++;
                f.pivot0 = foot + f.dir * Mathf.Clamp(t.radius * 0.12f, 0.15f, 0.45f); f.skidTo = away * skid; f.w = spin; f.th = 0f; f.phase = 2;
                var at = f.pivot0 + f.skidTo + f.dir * f.hcc; float gy = GroundFor(f, at.x, at.z);
                f.pool = fx && OpenZFxPlayer.IsWaterTile(at) ? fx.WaterYAt(at) : float.NegativeInfinity;
                f.wet = !float.IsNegativeInfinity(f.pool) && f.pool > gy + 0.1f;
                f.landTh = Mathf.Acos(Mathf.Clamp(((f.wet ? f.pool : gy + 0.6f * f.rc) - f.pivot0.y) / f.hcc, -0.25f, 0.9f));
                if (seen && bits != null) bits.Burst(foot + Vector3.up * 0.4f, 6, 0, 2.6f, 0.2f, rng);
            }
            flying.Add(f);
        }

        // ------------------------------------------------------------------------------------------------ every frame
        /// <summary>BlastTick, first: moves the flying, settling and falling trees, wakes the lying ones to sink, plays the sound groups.</summary>
        void ThrowTick()
        {
            FlushSounds(false);
            if (flying.Count == 0 && lying.Count == 0 && floating.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var game = ZGame.I; float dt = game && game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f);
            if (flying.Count > ThrowMovingMost) ThrowMovingMost = flying.Count;
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var f = flying[i];
                if (!f.h) { flying.RemoveAt(i); continue; }
                bool done = f.phase == 0 ? Air(f, dt) : f.phase == 1 ? Settle(f, dt) : f.phase == 2 ? Over(f, dt) : f.phase == 3 ? Rebound(f, dt) : Sink(f, dt);
                if (!done) continue;
                flying.RemoveAt(i);
                if (f.phase == 4) { f.wake = Time.time + Random.Range(life.x, life.y); if (f.wet) { f.s = 0f; f.ringAt = 1.5f; floating.Add(f); } else lying.Add(f); }
            }
            if (floating.Count > 0)
            {   // the logs in the water rock a little, with a faint ring at the crown now and then where the camera looks (as the run-19 fall's)
                var fx = OpenZFxPlayer.I;
                for (int i = floating.Count - 1; i >= 0; i--)
                {
                    var f = floating[i];
                    if (!f.h) { floating.RemoveAt(i); continue; }
                    if (Time.time >= f.wake) { floating.RemoveAt(i); Wake(f); continue; }
                    if (dt <= 0f) continue;
                    f.s += dt;
                    float bob = 0.012f * Mathf.Sin(f.s * 1.7f) * Mathf.Min(1f, f.s);
                    if (f.mode < 2) f.h.SetPositionAndRotation(f.footNow, Quaternion.AngleAxis((f.th + bob) * Mathf.Rad2Deg, f.axis) * f.r0);
                    else OverPose(f, f.landTh + bob);
                    if (f.s < f.ringAt) continue;
                    f.ringAt = f.s + Random.Range(2.2f, 3.4f);
                    var cr = Along(f, f.hcc);
                    if (fx && !float.IsNegativeInfinity(f.pool) && WreckFx.OnScreen(cr, 0.1f)) fx.RippleCalm(new Vector3(cr.x, f.pool, cr.z), 0.45f, 2f);
                }
            }
            if (Time.time >= nextWake)
            {
                nextWake = Time.time + 0.25f;
                for (int i = lying.Count - 1; i >= 0; i--)
                {
                    var f = lying[i];
                    if (!f.h) { lying.RemoveAt(i); continue; }
                    if (Time.time < f.wake) continue;
                    lying.RemoveAt(i); Wake(f);
                }
            }
            float ms = (float)sw.Elapsed.TotalMilliseconds; if (ms > ThrowMsMost) ThrowMsMost = ms;
        }

        /// <summary>The log's time is up: it sinks away (Sink).</summary>
        void Wake(Flyer f) { f.phase = 5; f.s = 0f; f.footNow = f.h.position; f.depth = f.t.radius * 1.7f + 0.6f; flying.Add(f); }

        /// <summary>The point of the trunk `along` metres up from its foot, as it is posed now.</summary>
        static Vector3 Along(Flyer f, float along) => f.h.position + f.h.rotation * Quaternion.Inverse(f.r0) * (Vector3.up * along);

        Vector3 Up(Flyer f) => Vector3.up * Mathf.Cos(f.th) + f.dir * Mathf.Sin(f.th);
        void PoseAir(Flyer f) { var u = Up(f); f.h.SetPositionAndRotation(f.c - u * f.hc, Quaternion.AngleAxis(f.th * Mathf.Rad2Deg, f.axis) * f.r0); }

        /// <summary>In the air: gravity, a little air drag, the tumble; the first hard contact bounces it once, the next one lays it down.</summary>
        bool Air(Flyer f, float dt)
        {
            f.clock += dt; f.tc += dt;
            f.v.y -= 9.81f * dt; float drag = 1f - AirDrag * dt; f.v.x *= drag; f.v.z *= drag;
            f.c += f.v * dt; f.th += f.w * dt;
            PoseAir(f);
            if (dt <= 0f) return false;
            var u = Up(f); var foot = f.c - u * f.hc; var crown = f.c + u * (f.hcc - f.hc);
            float pf = f.tc > 0.15f ? foot.y - GroundFor(f, foot.x, foot.z) : 9f, pc = crown.y - 0.6f * f.rc - GroundFor(f, crown.x, crown.z);
            float pen = Mathf.Min(pf, pc);
            if (!(pen < 0f && f.v.y < 0f) && f.clock < 6f) return false;
            var hit = pc < pf ? crown : foot; bool crownHit = pc < pf;
            if (f.bounces == 0 && f.clock > 0f && f.clock > ThrowFlightMost) ThrowFlightMost = f.clock;
            float speed = Mathf.Abs(f.v.y);
            Land(f, hit, crownHit, Mathf.Clamp01(new Vector3(f.v.x, f.v.y, f.v.z).magnitude / 14f) + 0.25f);
            if (f.bounces == 0 && speed > 3f && f.clock < 6f)
            {
                f.bounces++; ThrowBounces++; f.c.y -= pen; f.v.y = speed * BounceUp; f.v.x *= BounceKeep; f.v.z *= BounceKeep; f.w *= SpinKeep; f.tc = 0f;
                return false;
            }
            // down: the foot slides on, the trunk slams down flat onto the ground (or the water) over SettleSeconds
            f.phase = 1; f.s = 0f; f.footNow = foot; f.y0 = foot.y; f.th0 = f.th;
            f.v = new Vector3(f.v.x, 0f, f.v.z) * 0.8f;
            float side = Mathf.Sin(f.th) >= 0f ? 1f : -1f;
            var cc = new Vector3(foot.x, 0f, foot.z) + f.dir * (side * f.hcc); float gy = GroundFor(f, foot.x, foot.z), gc = GroundFor(f, cc.x, cc.z);
            var fx = OpenZFxPlayer.I;
            f.pool = fx && OpenZFxPlayer.IsWaterTile(foot) ? fx.WaterYAt(foot) : float.NegativeInfinity;
            f.wet = fx && fx.WetAt(foot);
            f.lift = f.wet ? f.pool - 0.25f : gy + 0.12f;
            // the foot ends at least the broken foot's shove (FarSkid.x) out from where it stood: a foot the tumble kept near its place
            // slides on further (a tall tree thrown short lays itself down round its foot)
            float need = FarSkid.x - Vector3.Dot(new Vector3(foot.x - f.foot0.x, 0f, foot.z - f.foot0.z), f.away);
            if (need > 0f) { float sp0 = Mathf.Min(SlideMost, Mathf.Sqrt(2f * SlideDecel * need)), along = Vector3.Dot(f.v, f.away); if (along < sp0) f.v += f.away * (sp0 - along); }
            float slide = new Vector2(f.v.x, f.v.z).magnitude; f.dustAt = 1e9f;
            if (slide > SlideNoteSpeed && !f.wet)
            {   // the trunk scrapes on over the ground: a grouped slide sound, dust at its foot
                ThrowSlides++; Note(6, foot, Mathf.Clamp01(slide / 10f) + 0.3f);
                if (fx && DetailKit.OnScreen(Camera.main, foot)) { fx.Dust(foot, 0.7f); f.dustAt = 0.5f; }
            }
            float e = Mathf.Asin(Mathf.Clamp((gc + 0.6f * f.rc - f.lift) / f.hcc, -0.5f, 0.6f));
            float tgt = side * (Mathf.PI * 0.5f - e);
            f.thT = tgt + Mathf.Round((f.th - tgt) / (Mathf.PI * 2f)) * Mathf.PI * 2f;
            return false;
        }

        bool Settle(Flyer f, float dt)
        {
            f.s += dt;
            float sp = new Vector2(f.v.x, f.v.z).magnitude;
            if (sp > 0f) { float k = Mathf.Max(0f, sp - SlideDecel * dt) / sp; f.v.x *= k; f.v.z *= k; }
            f.footNow += new Vector3(f.v.x, 0f, f.v.z) * dt;
            if (f.s >= f.dustAt)
            {   // more dust while it still slides fast, where the camera looks
                f.dustAt = sp > SlideNoteSpeed ? f.s + 0.5f : 1e9f; var fx = OpenZFxPlayer.I;
                if (fx && sp > SlideNoteSpeed && DetailKit.OnScreen(Camera.main, f.footNow)) fx.Dust(f.footNow, 0.5f);
            }
            float gy = f.wet ? f.lift : GroundFor(f, f.footNow.x, f.footNow.z) + 0.12f;
            float k2 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f.s / Mathf.Max(0.05f, SettleSeconds)));
            f.th = Mathf.Lerp(f.th0, f.thT, k2);
            f.footNow.y = Mathf.Lerp(f.y0, gy, k2);
            f.h.SetPositionAndRotation(f.footNow, Quaternion.AngleAxis(f.th * Mathf.Rad2Deg, f.axis) * f.r0);
            if (dt <= 0f || f.s < SettleSeconds || sp > 0.05f) { if (f.s < 5f) return false; }
            Rest(f, f.footNow);
            return true;
        }

        /// <summary>Broken over: the foot shoved outward over 0.35 s, the tree falls from it fast (th'' = 3g / 2L sin th from a strong first spin).</summary>
        bool Over(Flyer f, float dt)
        {
            f.clock += dt;
            float k3 = 3f * 9.81f / (2f * f.L);
            f.w += k3 * Mathf.Sin(f.th) * dt; f.th += f.w * dt;
            bool down = f.th >= f.landTh || f.clock > 6f;
            if (down) f.th = f.landTh;
            OverPose(f, f.th);
            if (!down || dt <= 0f) return false;
            var piv = f.pivot0 + f.skidTo * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f.clock / 0.35f));
            var crown = piv + Quaternion.AngleAxis(f.landTh * Mathf.Rad2Deg, f.axis) * (Vector3.up * f.hcc);
            if (f.clock > ThrowFlightMost) ThrowFlightMost = f.clock;
            Land(f, crown, true, Mathf.Clamp01(f.w * f.hcc / 9f) + 0.25f);
            f.phase = 3; f.s = 0f; f.amp = Mathf.Clamp(f.w * 2.5f, 2f, 6f) * Mathf.Deg2Rad;
            return false;
        }

        void OverPose(Flyer f, float th)
        {
            var off = f.skidTo * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(f.clock / 0.35f));
            var piv = f.pivot0 + off; var p0 = f.foot0 + off;
            var q = Quaternion.AngleAxis(th * Mathf.Rad2Deg, f.axis);
            f.h.SetPositionAndRotation(piv + q * (p0 - piv), q * f.r0);
        }

        bool Rebound(Flyer f, float dt)
        {
            f.s += dt;
            OverPose(f, f.landTh - f.amp * Mathf.Exp(-7f * f.s) * Mathf.Abs(Mathf.Sin(10f * f.s)));
            if (f.s < 0.7f) return false;
            OverPose(f, f.landTh);
            Rest(f, f.h.position);
            return true;
        }

        bool Sink(Flyer f, float dt)
        {
            f.s += dt / 3f;
            f.h.position = f.footNow + Vector3.down * (f.depth * f.s * f.s);
            if (f.s < 1f) return false;
            foreach (var p in f.t.plants) if (p) p.gameObject.SetActive(false);
            return true;
        }

        /// <summary>Where it hits: a thud (and the leaves when the crown hits), dust or a splash, earth and leaves bursting off.</summary>
        void Land(Flyer f, Vector3 at, bool crown, float weight)
        {
            var fx = OpenZFxPlayer.I; var cam = Camera.main;
            float wy = fx && OpenZFxPlayer.IsWaterTile(at) ? fx.WaterYAt(at) : float.NegativeInfinity;
            bool wet = !float.IsNegativeInfinity(wy) && fx.WetAt(at);
            at.y = wet ? wy : GroundFor(f, at.x, at.z) + 0.2f;
            Note(wet ? 4 : 3, at, weight);
            if (crown) Note(5, at, weight * 0.8f);
            if (!DetailKit.OnScreen(cam, at)) return;
            var rng = new System.Random(f.t.tx * 31 + f.t.ty * 17 + Time.frameCount);
            if (wet) { if (fx) { fx.Splash(at, 0.9f); fx.Ripple(at, 0.9f); } }
            else if (fx) { fx.Dust(at, 0.9f); if (f.mode < 2) fx.Dust(at + f.away * 2f, 0.6f); }
            if (bits != null) { if (crown) bits.Burst(at + Vector3.up * 0.4f, 10, 2, 2.8f, 0.12f, rng); bits.Burst(at + Vector3.up * 0.2f, 6, 1, 2.4f, 0.16f, rng); }
        }

        void Rest(Flyer f, Vector3 foot)
        {
            f.phase = 4; f.footNow = foot; f.t.landed = true; Landed++; ThrowLanded++;
            if (f.wet) { ThrowWet++; Floated++; }
            float travel = new Vector2(foot.x - f.foot0.x, foot.z - f.foot0.z).magnitude; if (travel > ThrowTravelMost) ThrowTravelMost = travel;
            if (f.mode < 2 && travel < ThrowTravelLeast) ThrowTravelLeast = travel;
            if (sceneSpotCount > 0)
            {   // where it came to rest: the trunk (foot to top) against the set, the endings' camera spots and the lines between them
                var top = Along(f, f.L); Vector2 a = new Vector2(foot.x, foot.z), b = new Vector2(top.x, top.z), s0 = new Vector2(sceneSpots[0].x, sceneSpots[0].z);
                for (int i = 0; i < sceneSpotCount; i++) { float sd = SegDist(a, b, s0, new Vector2(sceneSpots[i].x, sceneSpots[i].z)); if (sd < ThrowSetClosest) ThrowSetClosest = sd; }
            }
            Log(f.t, f.mode == 0 ? "thrown, down" : f.mode == 1 ? "snapped and thrown, down" : "broken over, down");
        }

        // ------------------------------------------------------------------------------------------------ the grouped sound layer
        sealed class Group { public int n, seen; public Vector3 sum, sumSeen; public float w, start = -1f, last = -10f; }
        static readonly Group[] groups = { new Group(), new Group(), new Group(), new Group(), new Group(), new Group(), new Group() };
        static int soundsThisBlast; static float blastSoundsFor = -100f;
        static readonly string[] SoundKey = { "prop", "rock", "tree_whoosh", "debris", "debris", "prop", "rock" };
        static readonly string[][] SoundClips =
        {
            new[] { "sd_debris_wood_a", "sd_debris_wood_b", "sd_debris_wood_c" },
            new[] { "Foley/sd_crater_clods_a", "Foley/sd_crater_clods_b" },
            new[] { "sd_body_whoosh_a", "sd_body_whoosh_b", "sd_body_whoosh_c", "SoundV3/s3_toss_whoosh_b", "SoundV3/s3_toss_whoosh_d" },
            new[] { "Foley/sd_dive_land_sand_a", "Foley/sd_dive_land_sand_b", "Foley/sd_dive_land_sand_c" },
            new[] { "sd_debris_splash_a", "sd_debris_splash_b" },
            new[] { "DetailsV2/dv2_tumbleweed_rustle_a", "DetailsV2/dv2_tumbleweed_rustle_b" },
            new[] { "Foley/sd_crater_clods_a", "Foley/sd_crater_clods_b" },                                     // the slide: earth scraped, low
        };
        static readonly string[] BigCrack = { "L16/sd_bridge_collapse_wood_a", "L16/sd_bridge_collapse_wood_b" };   // many trees breaking at once
        static readonly float[] SoundWindow = { 0.10f, 0.15f, 0.25f, 0.10f, 0.20f, 0.25f, 0.25f }, SoundGap = { 0.16f, 0.33f, 0.33f, 0.16f, 0.33f, 0.33f, 0.33f };
        static readonly float[] SoundVol = { 0.85f, 0.7f, 0.6f, 0.85f, 0.6f, 0.5f, 0.55f };
        static readonly Vector2[] SoundPitch = { new Vector2(0.85f, 1.1f), new Vector2(0.85f, 1f), new Vector2(0.62f, 0.8f), new Vector2(0.62f, 0.8f), new Vector2(0.9f, 1.05f), new Vector2(0.85f, 1f), new Vector2(0.55f, 0.7f) };
        static float lastBigCrack = -10f;

        void Note(int kind, Vector3 at, float weight)
        {
            if (blastSeen != blastSoundsFor) { blastSoundsFor = blastSeen; soundsThisBlast = 0; }
            SoundNotes[kind]++;
            var g = groups[kind];
            if (g.n == 0) g.start = Time.time;
            g.n++; g.sum += at; g.w = Mathf.Max(g.w, weight);
            if (DetailKit.OnScreen(Camera.main, at)) { g.seen++; g.sumSeen += at; }
        }

        /// <summary>Plays each kind's gathered notes as one sound once its window is over (and its gap since the last one has passed).</summary>
        static void FlushSounds(bool all)
        {
            for (int k = 0; k < groups.Length; k++)
            {
                var g = groups[k];
                if (g.n == 0 || (!all && (Time.time - g.start < SoundWindow[k] || Time.time - g.last < SoundGap[k]))) continue;
                if (soundsThisBlast >= ThrowSoundsMost) { SoundFolded[k] += g.n; Clear(g); continue; }
                var at = g.seen > 0 ? g.sumSeen / g.seen : g.sum / g.n;
                float vol = SoundVol[k] * (0.55f + 0.45f * Mathf.Min(1f, g.n / 4f)) * Mathf.Clamp(g.w, 0.4f, 1f);
                float pitch = Random.Range(SoundPitch[k].x, SoundPitch[k].y);
                var clips = SoundClips[k];
                if (k == 0 && g.n >= 5 && Time.time - lastBigCrack > 1.5f) { clips = BigCrack; lastBigCrack = Time.time; }
                var ca = CombatAudio.I;
                if (ca) ca.PlayPitched(SoundKey[k], at, vol, 0f, -1, pitch, clips);
                else DetailKit.Pool3D.Play("OpenZ/Audio/Sfx/" + clips[Random.Range(0, clips.Length)], at, vol, pitch);
                OzSoundLog.Add("TreeThrow", "tree_" + SoundKind[k], clips[0], $"group of {g.n} ({g.seen} on screen)", vol, at);
                SoundPlays[k]++; SoundFolded[k] += g.n - 1; soundsThisBlast++; g.last = Time.time;
                Clear(g);
            }
        }

        static void Clear(Group g) { g.n = 0; g.seen = 0; g.sum = g.sumSeen = Vector3.zero; g.w = 0f; g.start = -1f; }

        /// <summary>For the checks: the frames kept since the blast (BlastFrameStatus's numbers): count, median ms, frames over 50 ms.</summary>
        public static bool BlastFrameNumbers(out int n, out float median, out int over50)
        {
            n = 0; median = 0f; over50 = 0;
            if (!I || I.frameMs.Count == 0) return false;
            var a = new List<float>(I.frameMs); a.Sort();
            n = a.Count; median = a[n / 2]; foreach (var ms in a) if (ms > 50f) over50++;
            return true;
        }

        /// <summary>Play check: the thrown trees in one line.</summary>
        public static string ThrowStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"blast trees thrown: {(BlastThrow ? "on" : "OFF (the tip-over)")}, along the ground {(BlastSurfaceTiming ? "on" : "off")}; thrown {ThrownNear} (uprooted {Uprooted}), snapped and thrown {ThrownMid}, broken over {BrokenOver}; ");
            sb.Append($"for the set: shorter {CampShorter}, broken over {CampHeld}; no ground under the tree: a short throw {NoGroundShort}; the end of the ground: shorter {EdgeShorter}, a short throw {EdgeHeld}; ");
            sb.Append($"landed {ThrowLanded} (in the water {ThrowWet}, bounced {ThrowBounces}, slid and scraped {ThrowSlides}); the shortest throw of a thrown tree's foot {(float.IsPositiveInfinity(ThrowTravelLeast) ? "-" : ThrowTravelLeast.ToString("0.0"))} m; ");
            sb.Append($"the closest a resting trunk lies to the set or the endings' camera spots {(float.IsPositiveInfinity(ThrowSetClosest) ? "- (no set)" : ThrowSetClosest.ToString("0.0") + " m")}; ");
            sb.Append($"waited for the front along the ground {ThrowLagged} (longest {ThrowLagMost:0.00} s); longest flight {ThrowFlightMost:0.00} s, longest throw {ThrowTravelMost:0.0} m; most moving at once {ThrowMovingMost}, most ms in a frame {ThrowMsMost:0.00}; sounds:");
            for (int k = 0; k < SoundKind.Length; k++) sb.Append($" {SoundKind[k]} {SoundNotes[k]} notes -> {SoundPlays[k]} played");
            sb.Append($" (at most {ThrowSoundsMost} a blast)");
            if (I) sb.Append($"; moving now {I.flying.Count}, lying {I.lying.Count}, floating {I.floating.Count}");
            return sb.ToString();
        }
    }
}
