using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shuttle's break-up throws the rocket fuel cans out of its hull (a little joke, view only; the cans: OzFuelCan.cs). Called
    /// once at the end of <see cref="Break"/> (every death: in the air, on the ground, the dropped ship hitting the ground), after the
    /// pieces were launched: 5 to 10 cans (a seeded pick per death), each at a place inside the hull (the middle of the broken pieces'
    /// box, in the ship's own frame), each thrown out of the blast's middle along its own way from there, with the speed of a light
    /// piece of this very break (a leg, a shard, a dome half, an engine pod or the ramp leaf, picked by the same seed: whatever throws
    /// those, throws the cans), a little faster and spinning hard; a piece not moving yet (launched a moment later) gives the break's
    /// own light-piece throw instead. They go with the wreck: the pieces' own wreck time.
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        static readonly List<Vector3> canAt = new List<Vector3>(), canVel = new List<Vector3>(), canSpin = new List<Vector3>();
        static readonly List<Quaternion> canRot = new List<Quaternion>();
        static readonly List<bool> canCrushed = new List<bool>();
        static readonly List<OzShuttleBreakup> lightPieces = new List<OzShuttleBreakup>();
        static readonly List<Collider> pieceCols = new List<Collider>();

        static void FuelCans(Group grp, Transform root, bool air, Vector3 burst, Vector3 hitDir)
        {
            if (!OzFuelCan.Enabled || grp == null || !root || grp.pieces.Count == 0) return;
            int id = grp.obj != null ? grp.obj.Id : grp.emitter;
            int seed = id * 7919 + Breaks * 31 + 397;
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            int n = rnd.Next(Mathf.Min(OzFuelCan.MinCount, OzFuelCan.MaxCount), Mathf.Max(OzFuelCan.MinCount, OzFuelCan.MaxCount) + 1);
            // the hull's inside: the broken pieces' box in the ship's own frame, its middle half
            var w2l = root.worldToLocalMatrix; bool any = false; var box = new Bounds();
            foreach (var pc in grp.pieces)
            {
                if (!pc || !pc.vis) continue;
                var r = pc.vis.GetComponent<Renderer>(); if (!r) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = w2l.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (!any) { box = new Bounds(c, Vector3.zero); any = true; } else box.Encapsulate(c);
                }
            }
            if (!any) box = new Bounds(new Vector3(0f, 2.6f, 0f), new Vector3(4f, 3f, 8f));
            lightPieces.Clear();
            foreach (var pc in grp.pieces) if (pc && pc.rb && (pc.Kind == "leg" || pc.Kind == "shard" || pc.Kind == "dome" || pc.Kind == "engine" || pc.Kind == "leaf")) lightPieces.Add(pc);
            var vel = grp.vel; float power = grp.power;
            float outK = (air ? 1f : 0.5f) * (0.6f + 0.4f * power), upK = (air ? 1f : 0.4f) * (0.7f + 0.3f * power);
            var shove = new Vector3(hitDir.x, Mathf.Max(0f, hitDir.y) * 0.6f, hitDir.z) * (power * (air ? 1f : 0.5f));
            var gm = ZGame.I;
            canAt.Clear(); canVel.Clear(); canSpin.Clear(); canRot.Clear(); canCrushed.Clear();
            for (int i = 0; i < n; i++)
            {
                var local = box.center + Vector3.Scale(box.extents, new Vector3(R(-0.45f, 0.45f), R(-0.35f, 0.25f), R(-0.45f, 0.45f)));
                var at = root.TransformPoint(local);
                if (gm) at.y = Mathf.Max(at.y, gm.GroundY(at.x, at.z) + 0.25f);                          // standing on its pads the hull's floor is low: never in the ground
                var dir = at - burst; dir.y = 0f; if (dir.sqrMagnitude < 1e-3f) dir = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)); dir.Normalize();
                dir = Quaternion.Euler(0f, R(-30f, 30f), 0f) * dir;
                float h = 0f, u = 0f;
                var lp = lightPieces.Count > 0 ? lightPieces[rnd.Next(lightPieces.Count)] : null;
                if (lp && lp.rb && !lp.rb.isKinematic)
                {   // the speed of a light piece of this break, along the can's own way out
                    var rel = lp.rb.linearVelocity - vel; u = rel.y; rel.y = 0f; h = rel.magnitude;
                }
                Vector3 v;
                if (air && CanAirOwn) v = CanAirThrow(rnd, vel, dir, shove, grp.how);                    // K424: an air can tumbles out with its own small throw (OzShuttleBreakup.FuelCansAir.cs)
                else if (h + Mathf.Abs(u) > 0.5f) v = vel + dir * (h * R(0.9f, 1.3f)) + Vector3.up * (Mathf.Max(u, 1.5f * upK + 0.5f) * R(0.9f, 1.25f));
                else
                {   // the break's own light-piece throw (a leg's or a shard's), a little faster
                    float vo = R(4.5f, 7f), vu = R(3.5f, 6.5f), sp = 1f;
                    if (grp.how == "drop") { vo *= DropThrowK; vu *= DropThrowK; }
                    CrackThrow(grp.how, ref vo, ref vu, ref sp);                                            // the crack's low, slow throw (as the pieces)
                    v = vel + dir * (vo * outK) + Vector3.up * (vu * upK) + shove * R(1.2f, 2.4f);
                }
                canAt.Add(at); canVel.Add(v);
                canSpin.Add(new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized * R(8f, 20f));
                canRot.Add(Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f)));
                canCrushed.Add(rnd.NextDouble() < OzFuelCan.CrushedShare);
            }
            pieceCols.Clear(); foreach (var pc in grp.pieces) if (pc && pc.col) pieceCols.Add(pc.col);
            NoteCanThrow(OzFuelCan.Throw(id, grp.how, air, grp.centre, pieceCols, canAt, canRot, canVel, canSpin, canCrushed, grp.wreckAt - grp.age, seed), vel, air);   // K424: how they left the ship (the Play check)
        }
    }
}
