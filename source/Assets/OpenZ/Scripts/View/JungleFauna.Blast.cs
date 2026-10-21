// OpenZ (GPL-3.0) - Level 16: the parrots keep away from the fort blast.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the parrots keep away from the fort blast (the nuke's impact, <see cref="OpenZNukeStrike.LastImpactPoint"/>). At the impact
    /// every parrot group within <see cref="BlastFleeRadius"/> takes off at once (its wing burst and calls) and leaves away from the blast,
    /// and every flying parrot within it beats hard, climbs and flies out of <see cref="BlastKeepOut"/>. From then on, for the rest of the
    /// level (the ending included), no flight path, landing spot or perch of a parrot comes within <see cref="BlastKeepOut"/> of the blast.
    /// Counted for the checks: <see cref="BlastStatus"/> (how many fled, the closest any parrot came in the first 2 s and from 2 to 20 s).
    /// </summary>
    public sealed partial class JungleFauna
    {
        /// <summary>Keep-out circle round the blast (m), the radius that takes fright (m), the fastest a fleeing parrot flies (m/s), how much
        /// higher a fleeing flyer climbs (m), how long a fled group stays away before it may choose a new spot (s).</summary>
        public static float BlastKeepOut = 60f, BlastFleeRadius = 120f, BlastFleeSpeed = 30f, BlastClimb = 6f, BlastFleeSeconds = 15f;   // K271: the flyers' dash 25 -> 30 m/s
        /// <summary>K271: a fled group's take-off and first flight (m/s: from 25 m out it is past the keep-out at 2 s) and how long it keeps
        /// that pace before it eases back to its own 6.5 m/s over 3 s.</summary>
        public static float BlastGroupSpeed = 20f, BlastFastSeconds = 3f;
        public static bool BlastOn; public static Vector3 BlastAt; public static float BlastTime = -100f;
        public static int BlastFledGroups, BlastFledBirds, BlastFledFlyers;
        public static float BlastClosestEarly = 999f, BlastClosestLate = 999f;
        float impactSeen = -100f; bool blastReset;
        readonly HashSet<Flyer> blastFlyers = new HashSet<Flyer>();
        readonly HashSet<Group> blastGroups = new HashSet<Group>();

        void StepBlast()
        {
            if (!blastReset)
            {
                blastReset = true; BlastOn = false; BlastTime = -100f; BlastFledGroups = BlastFledBirds = BlastFledFlyers = 0;
                BlastClosestEarly = BlastClosestLate = 999f; impactSeen = OpenZNukeStrike.LastImpactTime;
            }
            float imp = OpenZNukeStrike.LastImpactTime;
            if (imp > impactSeen && Time.time - imp < 1f) { impactSeen = imp; StartBlast(OpenZNukeStrike.LastImpactPoint); }
            if (!BlastOn) return;
            float since = Time.time - BlastTime;
            if (since > 20f) return;
            float m = 999f;
            foreach (var f in flyers) m = Mathf.Min(m, Flat(f.pos, BlastAt));
            foreach (var g in groups) if (g.alive) foreach (var b in g.birds) m = Mathf.Min(m, Flat(b.pos, BlastAt));
            if (since < 2f) BlastClosestEarly = Mathf.Min(BlastClosestEarly, m); else BlastClosestLate = Mathf.Min(BlastClosestLate, m);
        }

        void StartBlast(Vector3 at)
        {
            BlastOn = true; BlastAt = at; BlastTime = Time.time;
            foreach (var g in groups)
            {
                float d = Flat(g.centre, at);
                if (d >= BlastFleeRadius) continue;
                blastGroups.Add(g);
                if (!g.alive) continue;                                                                 // not drawn: it simply chooses a spot outside later
                if (g.st == GS.Travel) { g.toSpot = -1; var away = g.centre - at; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Vector3.forward; g.target = g.centre + away.normalized * 120f; BlastFledGroups++; continue; }
                if (g.st != GS.Sitting) g.st = GS.Sitting;                                              // taking off or landing: up again at once
                Scare(g, at, d); g.ringLen = 0f; BlastFledGroups++; BlastFledBirds += g.birds.Count;     // K271: no ring at all over the blast: straight away
                foreach (var b in g.birds) { b.t = 0.375f; b.delay *= 0.1f / 0.35f; }                   // K271: startled: no crouch, all up within 0.1 s
            }
            int calls = 0;
            foreach (var f in flyers)
            {
                if (Flat(f.pos, at) >= BlastFleeRadius) continue;
                blastFlyers.Add(f); f.burstLeft = 3f; BlastFledFlyers++;
                // K271: its circle's middle out along the bird's OWN direction from the blast (the whole circle outside the keep-out) and its
                // circle point the nearest one: the bird dashes straight out from where it is, never across the circle back over the blast
                float need = BlastKeepOut + f.radius + 2f; var c0 = f.centre - new Vector2(at.x, at.z);
                if (c0.magnitude < need)
                {
                    var u = new Vector2(f.pos.x - at.x, f.pos.z - at.z).normalized; if (u.sqrMagnitude < 0.5f) u = c0.sqrMagnitude > 1e-4f ? c0.normalized : Vector2.up;
                    f.centre = new Vector2(at.x, at.z) + u * need; f.ang = Mathf.Atan2(f.pos.z - f.centre.y, f.pos.x - f.centre.x);
                }
                if (calls++ < 3)
                {
                    Snd("critter_parrot_flutter", f.pos, 0.75f, 8f, 90f, "sd_parrot_flutter_a", "sd_parrot_flutter_b", "sd_parrot_flutter_c");
                    Snd("amb_l16_parrot_squawk", f.pos, 0.8f, 15f, 150f, "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d");
                }
            }
            Debug.Log($"[OpenZ] JungleFauna L16: the fort blast at ({at.x:F0}, {at.z:F0}): {BlastFledGroups} parrot groups ({BlastFledBirds} birds) and {BlastFledFlyers} flying parrots flee; nothing comes within {BlastKeepOut:F0} m of it from now on");
        }

        /// <summary>A flying parrot's place this frame after the blast: never inside the keep-out circle (pushed out to its rim), the fled ones
        /// climb for a while, and no bird moves faster than <see cref="BlastFleeSpeed"/> (so a bird inside dashes out instead of jumping).</summary>
        Vector3 BlastFly(Flyer f, Vector3 p, float dt)
        {
            if (!BlastOn) return p;
            // K271: the circle's middle stays keep-out + radius + 2 m out (along its own direction: it slides round that rim towards its
            // route), so the whole circle lies outside; the bird's point moves with it
            float need = BlastKeepOut + f.radius + 2f; var c = f.centre - new Vector2(BlastAt.x, BlastAt.z); float cr = c.magnitude;
            if (cr < need)
            {
                var u = cr > 0.5f ? c / cr : new Vector2(f.pos.x - BlastAt.x, f.pos.z - BlastAt.z).normalized; if (u.sqrMagnitude < 0.5f) u = Vector2.up;
                var shift = u * need - c; f.centre += shift; p.x += shift.x; p.z += shift.y;
            }
            var d = p - BlastAt; d.y = 0f;
            if (d.magnitude < BlastKeepOut)
            {   // a guard (the night / rain way out): to the rim along the BIRD's own direction (K271: not the point's, it swung across the circle)
                var dir = f.pos - BlastAt; dir.y = 0f; if (dir.sqrMagnitude < 1e-4f) dir = d; if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; dir.Normalize();
                var q = BlastAt + dir * BlastKeepOut; p = new Vector3(q.x, p.y, q.z);
            }
            if (blastFlyers.Contains(f))
            {
                float s = Time.time - BlastTime;
                p.y += BlastClimb * Mathf.SmoothStep(0f, 1f, s / 2f) * (1f - Mathf.SmoothStep(0f, 1f, (s - 8f) / 6f));
            }
            return Vector3.MoveTowards(f.pos, p, BlastFleeSpeed * dt);
        }

        /// <summary>A place the blast keeps the parrots away from (spots, perches).</summary>
        static bool BlastKeeps(Vector3 c) => BlastOn && Flat(c, BlastAt) < BlastKeepOut;

        /// <summary>The straight flight from a to b passes within the keep-out circle.</summary>
        static bool BlastCrosses(Vector3 a, Vector3 b)
        {
            if (!BlastOn) return false;
            var ab = new Vector2(b.x - a.x, b.z - a.z); var ac = new Vector2(BlastAt.x - a.x, BlastAt.z - a.z);
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(ac, ab) / ab.sqrMagnitude) : 0f;
            return (ac - ab * t).magnitude < BlastKeepOut;
        }

        /// <summary>A group that fled the blast leaves the screen first (no new spot for a while).</summary>
        bool BlastHolds(Group g) => BlastOn && blastGroups.Contains(g) && Time.time - BlastTime < BlastFleeSeconds;

        /// <summary>K271: a group that fled the blast takes off and flies its first <see cref="BlastFastSeconds"/> at <see cref="BlastGroupSpeed"/>,
        /// then eases back to its own speed over 3 s; any other group: its own speed.</summary>
        float BlastPace(Group g, float own) => BlastHolds(g) ? Mathf.Lerp(BlastGroupSpeed, own, Mathf.SmoothStep(0f, 1f, (Time.time - BlastTime - BlastFastSeconds) / 3f)) : own;

        /// <summary>One line for the checks.</summary>
        public static string BlastStatus()
        {
            if (!BlastOn) return "parrots and the fort blast: no blast yet";
            return $"parrots and the fort blast at ({BlastAt.x:F0}, {BlastAt.z:F0}), {Time.time - BlastTime:F1} s ago: fled {BlastFledGroups} groups ({BlastFledBirds} perched birds) and {BlastFledFlyers} flying parrots; the closest any parrot came: {BlastClosestEarly:F1} m in the first 2 s, {BlastClosestLate:F1} m from 2 to 20 s (keep-out {BlastKeepOut:F0} m)";
        }
    }
}
