using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the parrots and monkeys keep away at night and in rain (OzJungleLife.Hushed), and come back with the day and the
    /// dry weather. The crocodiles are not part of this (CrocodileView: active always); nor are the snakes.
    /// <list type="bullet">
    /// <item>The three parrots in the air fly off: each turns out to a point 150 m away over the map's edge side, climbing, over
    /// about 25 s, and is not drawn once it is out there; when it is over they fly back in to their circles the same way.</item>
    /// <item>A parrot group that sits on the screen lifts off quietly (one soft wing burst, no squawk), makes one short turn and
    /// flies away off the screen; no group lands or wakes while it holds.</item>
    /// <item>When it is over, a group whose spot is on the screen flies in from off the screen (FlyInFrom m or more out toward the
    /// map's side) and lands there with the landing take: it never shows up already sitting. A group whose spot is off the screen
    /// wakes there as before (in the rim round the view, before it can be seen).</item>
    /// <item>A sitting monkey climbs up into its tree's crown (ClimbUpMetres up and in to the trunk over ClimbUpSeconds, a little later
    /// than the one before it, smaller as it goes into the leaves down to ClimbShrinkTo) and is not drawn there; it climbs down again
    /// when it is over. A hidden monkey does not run from robots; a troop that ran away does not come back while it holds.</item>
    /// <item>No call of a parrot or a monkey is played while it holds (JungleFauna.Snd).</item>
    /// </list>
    /// Proof: <see cref="HushStatus"/> (counts of birds out / away, monkeys hidden, groups that flew back in).
    /// </summary>
    public sealed partial class JungleFauna
    {
        /// <summary>Seconds a flying parrot takes to fly off or back; seconds a monkey takes to climb up into the crown.</summary>
        public static float FlyOffSeconds = 25f, ClimbUpSeconds = 4f;
        /// <summary>Metres a monkey climbs up into the crown; its size at the top (1 = it does not get smaller).</summary>
        public static float ClimbUpMetres = 1.5f, ClimbShrinkTo = 0.35f;
        /// <summary>Metres out (toward the map's side) a parrot group that comes back starts its flight in; farther while that point is on the screen.</summary>
        public static float FlyInFrom = 40f;
        public static int GroupsLeftQuietly, GroupsFlewIn, FlyersAway, MonkeysHidden;

        sealed class Away { public float k; public Vector3 to; }
        readonly Dictionary<Flyer, Away> flyAway = new Dictionary<Flyer, Away>();
        readonly Dictionary<Ape, float> apeHide = new Dictionary<Ape, float>();
        readonly HashSet<Group> backAfterHush = new HashSet<Group>();                             // groups that were away: their first wake after it flies in
        bool hush, hushStarted; float hushTurnedAt = -99f;

        void StepHush(float dt)
        {
            bool now = OzJungleLife.Hushed;
            if (!hushStarted)
            {   // the level starts at night or in rain: they are away from the first frame (nobody sees them fly off at the start)
                hushStarted = true; hush = now; GroupsLeftQuietly = GroupsFlewIn = 0;
                if (now)
                {
                    foreach (var g in groups) backAfterHush.Add(g);
                    foreach (var f in flyers) { var a = new Away { k = 1f }; var d = f.pos - new Vector3(104f, 0f, -128f); d.y = 0f; if (d.sqrMagnitude < 1f) d = Vector3.right; a.to = f.pos + d.normalized * 150f + Vector3.up * 30f; flyAway[f] = a; }
                    foreach (var a in apes) { apeHide[a] = 1f; a.rig.Show(false); }
                }
            }
            if (now != hush) { hush = now; hushTurnedAt = clock; if (now) foreach (var g in groups) backAfterHush.Add(g); }
            int away = 0;
            foreach (var kv in flyAway) if (kv.Value.k >= 0.999f) away++;
            FlyersAway = away;
            int hid = 0;
            foreach (var kv in apeHide) if (kv.Value >= 0.999f) hid++;
            MonkeysHidden = hid;
        }

        /// <summary>A flying parrot's place this frame: its circle's place, or on its way out / in, or out (then not drawn).</summary>
        Vector3 HushFly(Flyer f, Vector3 p, float dt)
        {
            if (!flyAway.TryGetValue(f, out var a))
            {
                if (!hush) return p;
                a = new Away(); flyAway[f] = a;
            }
            if (hush && a.k <= 0f)
            {   // out over the nearer map side, away from the middle, 150 m, high
                var mid = new Vector3(104f, 0f, -128f); var d = p - mid; d.y = 0f;
                if (d.sqrMagnitude < 1f) d = Vector3.right;
                a.to = p + d.normalized * 150f; a.to.y = p.y + 30f;
            }
            a.k = Mathf.MoveTowards(a.k, hush ? 1f : 0f, dt / Mathf.Max(1f, FlyOffSeconds));
            if (a.k <= 0f) { flyAway.Remove(f); return p; }
            float s = Mathf.SmoothStep(0f, 1f, a.k);
            return Vector3.Lerp(p, a.to, s);
        }

        /// <summary>0 = not drawn (out there), else 1.</summary>
        float HushShow(Flyer f) => flyAway.TryGetValue(f, out var a) && a.k >= 0.999f ? 0f : 1f;

        /// <summary>A sitting group that has bird objects lifts off quietly and goes off the screen (no landing while it holds).</summary>
        void Leave(Group g)
        {
            if (g.st != GS.Sitting || !g.alive) return;
            g.st = GS.Fright; g.t = 0f; GroupsLeftQuietly++;
            var off = g.centre - new Vector3(104f, 0f, -128f); off.y = 0f; if (off.sqrMagnitude < 1f) off = Vector3.forward;
            g.threat = g.centre - off.normalized * 10f;                                            // they go out toward the map's side
            for (int k = 0; k < g.birds.Count; k++) { var b = g.birds[k]; b.delay = 0.6f * k / Mathf.Max(1, g.birds.Count - 1); b.t = 0f; b.from = b.seat; }
            var away = (g.centre - g.threat).normalized;
            g.ringC = g.centre + away * 8f; g.ringC.y = Ground(g.ringC.x, g.ringC.z) + R(g.rng, 7f, 9f);
            g.ringLen = 0.8f;
            foreach (var b in g.birds) { b.ringR = R(g.rng, 6f, 8f); b.ringAng = Mathf.Atan2(b.seat.z - g.ringC.z, b.seat.x - g.ringC.x); b.ringH = R(g.rng, -0.6f, 0.6f); }
            if (sounds) L16AnimalKit.Play("critter_parrot_flutter", g.centre, 0.45f, 8f, 90f, 1f, "sd_parrot_flock_flutter_a", "sd_parrot_flock_flutter_b");   // the one soft wing burst of the leaving
        }

        /// <summary>Where a parrot group wakes (StepGroups, only when it is not night or rain). After the night or the rain a group whose
        /// spot is on the screen flies in from off the screen and lands; otherwise it wakes in place as before (in the rim, unseen).</summary>
        void ComeBack(Group g)
        {
            if (backAfterHush.Contains(g) && Seen(g.centre, 0f)) FlyIn(g); else Wake(g);
            if (g.alive) backAfterHush.Remove(g);
        }

        /// <summary>The group's birds start together FlyInFrom m or more out toward the map's side (as far as it takes to be off the
        /// screen), 8 m up, and fly to their spot; the group's own travel and landing take them down onto their seats.</summary>
        void FlyIn(Group g)
        {
            if (g.alive) return;
            if (NearestUnit(g.centre, 0, out _) < (g.tree ? treeFright : groundFright) + 2f) { Relocate(g, false); return; }   // units at the spot: somewhere else (as Wake does)
            var off = g.centre - new Vector3(104f, 0f, -128f); off.y = 0f; if (off.sqrMagnitude < 1f) off = Vector3.forward; off.Normalize();
            var from = g.centre;
            for (float d = Mathf.Max(10f, FlyInFrom); ; d += 15f)
            {
                from = g.centre + off * d; from.y = Mathf.Max(Ground(from.x, from.z), 0f) + 8f;
                if (!Seen(from, 6f) || d >= 160f) break;
            }
            var to = SpotCentre(g.tree, g.spot); var dir = to - from; dir.y = 0f;
            float yaw = dir.sqrMagnitude > 0.01f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : 0f;
            for (int k = 0; k < g.size; k++)
            {
                var b = TakeBird(); if (b == null) break;
                b.seat = SeatOf(g, k); b.phase = R(g.rng, 0f, 3f); b.ringH = R(g.rng, -0.6f, 0.6f); b.move = 0; b.moveT = R(g.rng, 2f, 9f); b.t = 0f; b.delay = 0f;
                b.pos = from + new Vector3(R(g.rng, -2.4f, 2.4f), b.ringH, R(g.rng, -2.4f, 2.4f)); b.yaw = yaw;
                b.rig.root.SetPositionAndRotation(b.pos, Quaternion.Euler(0f, yaw, 0f));                   // never drawn for a frame where the bird was last
                g.birds.Add(b);
            }
            g.alive = g.birds.Count > 0;
            if (!g.alive) return;
            g.toSpot = g.spot; g.target = to; g.st = GS.Travel; g.t = 0f; GroupsFlewIn++;
        }

        bool ApeHidden(Ape a) => apeHide.TryGetValue(a, out var k) && k > 0.01f;

        /// <summary>A sitting monkey on its way up into the crown, up there, or on its way down. True = it is handled here this frame.</summary>
        bool HideApe(Ape a, float dt)
        {
            apeHide.TryGetValue(a, out float k);
            if (!hush && k <= 0f) return false;
            if (clock - hushTurnedAt < 0.6f * a.k)                                                 // one after the other
            {
                if (k <= 0f) return false;
                if (k >= 0.999f) return true;                                                      // up there, its turn to come down has not come yet
            }
            float was = k;
            k = Mathf.MoveTowards(k, hush ? 1f : 0f, dt / Mathf.Max(0.5f, ClimbUpSeconds));
            if (k <= 0f)
            {   // down again: on its seat, back to its own idle
                apeHide.Remove(a); a.rig.Show(true); a.rig.root.localScale = Vector3.one; a.pos = a.seat; a.idleT = R(a.rng, 2f, 6f);
                return false;
            }
            apeHide[a] = k;
            var foot = OnGround(JungleFaunaData.TroopTrees[a.troop]);
            var into = new Vector3(foot.x, a.seat.y, foot.z) - a.seat;                             // in toward the trunk
            float s = Mathf.SmoothStep(0f, 1f, k);
            a.pos = a.seat + into * (0.6f * s) + Vector3.up * (ClimbUpMetres * s);
            var d = into; if (d.sqrMagnitude > 0.01f) a.yaw = Mathf.LerpAngle(a.yaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 4f));
            bool shown = k < 0.999f;
            a.rig.Show(shown);
            if (shown)
            {
                a.rig.root.localScale = Vector3.one * Mathf.Lerp(1f, ClimbShrinkTo, Mathf.SmoothStep(0.6f, 1f, k));   // into the leaves
                a.rig.root.SetPositionAndRotation(a.pos, Quaternion.Euler(0f, a.yaw, 0f));
                a.t += dt * (Mathf.Abs(k - was) > 1e-5f ? 1f : 0f);
                PoseFrames(a.rig, Loop(a.rig, ClimbA, ClimbB, a.t), 0f, 0f);
            }
            return true;
        }

        /// <summary>One line for the checks: whether the jungle is quiet and how many birds and monkeys are away.</summary>
        public static string HushStatus()
        {
            if (!I) return "JungleFauna not running (not level 16)";
            int flyersOut = 0; foreach (var f in I.flyers) if (f.rig.Shown) flyersOut++;
            int monkeysDrawn = 0; foreach (var a in I.apes) if (a.rig.Shown) monkeysDrawn++;
            return $"hushed {I.hush}; parrots in the air drawn {flyersOut}/{I.flyers.Count} (away {FlyersAway}); parrot groups with birds {GroupsAlive} ({BirdObjects} birds drawn), left quietly {GroupsLeftQuietly}, flew back in {GroupsFlewIn}; " +
                   $"monkeys drawn {monkeysDrawn}/{I.apes.Count} (hidden in the crowns {MonkeysHidden}); " + OzJungleLife.Status();
        }
    }
}
