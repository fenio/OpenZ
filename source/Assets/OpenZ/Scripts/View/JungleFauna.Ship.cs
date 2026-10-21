// OpenZ (GPL-3.0) - Level 16: the parrots and monkeys keep away from the flying shuttle.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the parrots flee from the playable shuttle (rule DV118, view only) and never fly through it. The ship is a MOVING
    /// keep-out: a capsule of <see cref="ShipKeepOut"/> m flat round its drawn middle, from <see cref="ShipBandBelow"/> m under its root to
    /// <see cref="ShipBandAbove"/> m over it, read once a frame from OzPlayableShuttle (where it is drawn, its velocity, whether it is up).
    /// <list type="bullet">
    /// <item>the parrots in the air: within <see cref="ShipFright"/> m their circle's middle slides out (the whole circle outside the
    /// keep-out), a bird inside is pushed to the rim along its own direction or lifted over the ship (whichever is nearer), it beats
    /// hard for a while, and calls once (wing burst + squawk) per encounter; it never moves faster than <see cref="ShipFleeSpeed"/>
    /// m/s (the ship flies 5.25 m/s: a bird always gets out);</item>
    /// <item>a sitting group takes fright from the flying ship within <see cref="ShipFright"/> m (a loud low ship frightens from farther
    /// than a jeep); its ring and its way to a new spot are pushed out of the keep-out; no new spot within <see cref="ShipSpotKeep"/> m
    /// of the ship or of where it is going, and no flight to a spot across it;</item>
    /// <item>monkeys take fright from the ship in the air within 16 m, hovering too.</item>
    /// </list>
    /// Counted for the checks: <see cref="ShipFrights"/>, <see cref="ShipFlyerBursts"/>, <see cref="ShipClearance"/> (a bird's distance to
    /// the capsule, minus = inside).
    /// </summary>
    public sealed partial class JungleFauna
    {
        public static float ShipKeepOut = 10f, ShipBandBelow = 2f, ShipBandAbove = 7.5f, ShipFright = 30f, ShipFleeSpeed = 14f, ShipSpotKeep = 25f, ShipOver = 8f;
        public static int ShipFrights, ShipFlyerBursts, ShipPushes;
        readonly HashSet<Flyer> shipFlyers = new HashSet<Flyer>();
        static int shipFrame = -1; static bool shipOn, shipUp; static Vector3 shipAt, shipVel, shipGoal;
        struct ShipNowRow { public Vector3 at, vel, goal; public bool up; }
        static readonly List<ShipNowRow> shipRows = new List<ShipNowRow>(4);

        /// <summary>The drawn shuttles this frame (several may fly: the testing panel), then the one nearest `near` (flat) is the ship the
        /// question is about.</summary>
        static void ShipNow(Vector3 near)
        {
            if (shipFrame != Time.frameCount)
            {
                shipFrame = Time.frameCount; shipRows.Clear();
                for (int i = 0; i < OzPlayableShuttle.Count; i++)
                {
                    var s = OzPlayableShuttle.At(i);
                    if (!s || !s.isActiveAndEnabled || s.Obj == null || s.Obj.Destroyed || s.BrokenUp) continue;
                    var at = s.transform.position;
                    shipRows.Add(new ShipNowRow { at = at, vel = s.Velocity, up = s.Obj.ShuttleAirborne, goal = s.Obj.ShuttleGoalX >= 0 ? ZGame.PxToWorld(s.Obj.ShuttleGoalX, s.Obj.ShuttleGoalY) : at });
                }
            }
            shipOn = shipRows.Count > 0; if (!shipOn) { shipUp = false; return; }
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < shipRows.Count; i++) { float d = Flat(near, shipRows[i].at); if (d < bd) { bd = d; best = i; } }
            var r = shipRows[best]; shipAt = r.at; shipVel = r.vel; shipUp = r.up; shipGoal = r.goal;
        }

        /// <summary>A point's distance to the ship's keep-out capsule (m; minus = inside); 999 without a ship.</summary>
        public static float ShipClearance(Vector3 p)
        {
            ShipNow(p); if (!shipOn) return 999f;
            float flat = Flat(p, shipAt) - ShipKeepOut, lo = shipAt.y - ShipBandBelow, hi = shipAt.y + ShipBandAbove;
            float vert = p.y < lo ? lo - p.y : p.y > hi ? p.y - hi : -Mathf.Min(p.y - lo, hi - p.y);
            return flat > 0f ? (vert > 0f ? Mathf.Sqrt(flat * flat + vert * vert) : flat) : (vert > 0f ? vert : Mathf.Max(flat, vert));
        }

        /// <summary>A target point pushed out of the capsule: to its rim (flat) or over its top, whichever is nearer.</summary>
        static Vector3 OutOfShip(Vector3 p, Vector3 dirHint)
        {
            float lo = shipAt.y - ShipBandBelow - ShipMarginM, hi = shipAt.y + ShipBandAbove + ShipMarginM;   // + the margin kept outside the keep-out (JungleFauna.ShipGuard.cs)
            if (p.y < lo || p.y > hi) return p;
            var d = p - shipAt; d.y = 0f; float fl = d.magnitude;
            if (fl >= ShipKeepOut + ShipMarginM) return p;
            var dir = new Vector3(dirHint.x, 0f, dirHint.z); if (dir.sqrMagnitude < 1e-4f) dir = d; if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; dir.Normalize();
            float over = Mathf.Max(shipAt.y + ShipOver, hi + 0.2f), toRim = ShipKeepOut + ShipMarginM - fl, toTop = over - p.y;
            if (toTop < toRim) return new Vector3(p.x, over, p.z);
            var q = shipAt + (fl > 0.1f ? d / fl : dir) * (ShipKeepOut + ShipMarginM + 0.2f); return new Vector3(q.x, p.y, q.z);
        }

        /// <summary>A flying parrot's place this frame near the ship: its circle slides out, a bird inside is pushed out, no faster than ShipFleeSpeed.</summary>
        Vector3 ShipFly(Flyer f, Vector3 p, float dt)
        {
            ShipNow(f.pos); if (!shipOn) { shipFlyers.Remove(f); return p; }
            float d = Flat(f.pos, shipAt);
            bool band = p.y > shipAt.y - ShipBandBelow - 3f && p.y < shipAt.y + ShipBandAbove + 3f;
            if (d > ShipFright + 6f || !band) { shipFlyers.Remove(f); if (d > ShipFright + 6f) return p; }
            if (band && d < ShipFright)
            {
                float need = ShipKeepOut + ShipMarginM + f.radius + 2f; var c = f.centre - new Vector2(shipAt.x, shipAt.z); float cr = c.magnitude;
                if (cr < need)
                {
                    var u = cr > 0.5f ? c / cr : new Vector2(f.pos.x - shipAt.x, f.pos.z - shipAt.z).normalized; if (u.sqrMagnitude < 0.5f) u = Vector2.up;
                    var shift = u * need - c; f.centre += shift; p.x += shift.x; p.z += shift.y;
                }
                if (shipFlyers.Add(f))
                {
                    f.burstLeft = Mathf.Max(f.burstLeft, 2.5f); ShipFlyerBursts++;
                    Snd("critter_parrot_flutter", f.pos, 0.75f, 8f, 90f, "sd_parrot_flutter_a", "sd_parrot_flutter_b", "sd_parrot_flutter_c");
                    Snd("amb_l16_parrot_squawk", f.pos, 0.8f, 15f, 150f, "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d");
                }
            }
            var q = OutOfShip(p, f.pos - shipAt);
            if ((q - p).sqrMagnitude > 1e-4f) ShipPushes++;
            if (shipFlyers.Contains(f) || (q - p).sqrMagnitude > 1e-4f) return Vector3.MoveTowards(f.pos, q, ShipFleeSpeed * dt);
            return q;
        }

        /// <summary>A sitting group: the flying ship within ShipFright frightens it (the threat = the ship's ground point).</summary>
        bool ShipScares(Group g, out Vector3 at, out float dist)
        {
            ShipNow(g.centre); at = g.centre; dist = 999f;
            if (!shipOn || !shipUp) return false;
            dist = Flat(g.centre, shipAt);
            if (dist > ShipFright) return false;
            at = new Vector3(shipAt.x, g.centre.y, shipAt.z); ShipFrights++;
            return true;
        }

        /// <summary>No new spot near the flying ship or where it is going.</summary>
        static bool ShipKeeps(Vector3 c)
        {
            ShipNow(c); if (!shipOn || !shipUp) return false;
            return Flat(c, shipAt) < ShipSpotKeep || Flat(c, shipGoal) < ShipSpotKeep;
        }

        /// <summary>The straight flight from a to b passes the flying ship's keep-out (where it is and where it is going).</summary>
        static bool ShipCrosses(Vector3 a, Vector3 b)
        {
            ShipNow((a + b) * 0.5f); if (!shipOn || !shipUp) return false;
            for (int k = 0; k < 2; k++)
            {
                var s = k == 0 ? shipAt : shipGoal;
                var ab = new Vector2(b.x - a.x, b.z - a.z); var ac = new Vector2(s.x - a.x, s.z - a.z);
                float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector2.Dot(ac, ab) / ab.sqrMagnitude) : 0f;
                if ((ac - ab * t).magnitude < ShipKeepOut + 3f) return true;
            }
            return false;
        }

        /// <summary>A group bird's flight target out of the ship's keep-out.</summary>
        static Vector3 ShipAvoid(Vector3 p) { ShipNow(p); if (!shipOn) return p; var q = OutOfShip(p, p - shipAt); if ((q - p).sqrMagnitude > 1e-4f) ShipPushes++; return q; }

        /// <summary>Monkeys: the ship in the air within r m (hovering too).</summary>
        static bool ShipNear(Vector3 foot, float r, out Vector3 at)
        {
            ShipNow(foot); at = foot;
            if (!shipOn || !shipUp || Flat(foot, shipAt) > r) return false;
            at = shipAt; return true;
        }

        /// <summary>One line for the checks.</summary>
        public static string ShipStatus() => $"parrots and the shuttle: {ShipFrights} group frights, {ShipFlyerBursts} flyer bursts, {ShipPushes} pushes out of the keep-out ({ShipKeepOut:F0} m, band -{ShipBandBelow:F0} .. +{ShipBandAbove:F1} m)";
    }
}
