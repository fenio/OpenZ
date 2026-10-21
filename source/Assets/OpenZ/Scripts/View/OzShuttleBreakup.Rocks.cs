using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: the shuttle's break-up pieces against the rocks (OzRockSolid.All.cs, OzRockBody). Every big piece
    /// (domes, pods, legs, the hull panels, the ramp leaf; the small shards stay as they are) meets the rocks within its reach while it
    /// flies and tumbles: it hits them, slides off and rests on or beside them (its rest ray sees the rock: "on" it); the step that puts a
    /// resting piece onto the ground never goes into a rock (DepthIfMoved); at the end of the wreck time a piece lying on a rock pops away
    /// with a puff instead of sinking through it (Retire). A ship dropping whole (a rifle-class kill) breaks where its hull meets a rock
    /// (FallGap: OzRockBody.FallGapRock; the rocks round the falling ship are switched on by OzRockDebris); on the break frame the
    /// pieces that start in a rock are moved out of it together, as one ship (<see cref="RockShiftMaxM"/> at most). The crew's bodies are
    /// ragdolls (RobotRagdoll.Launch). View only.
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>The break frame: the pieces are moved out of a rock together by at most this much (m); what is left deeper is eased
        /// out by the physics (OzRockBody).</summary>
        public static float RockShiftMaxM = 1f;
        /// <summary>Breaks whose pieces were moved out of a rock, and the largest move (m).</summary>
        public static int RockShifts; public static float RockShiftLargest;

        /// <summary>Build: a big piece meets the rocks (its wake ends before the wreck time).</summary>
        void RockPiece()
        {
            var r = vis ? vis.GetComponent<Renderer>() : null;
            var size = r ? r.bounds.size : Vector3.one;
            OzRockBody.Add(gameObject, rb, OzRockBody.Kind.ShuttlePiece, size, WreckSeconds - 0.5f, () => retiring);
        }

        static readonly Collider[] rockHits = new Collider[16];

        /// <summary>Break, after the pieces are made and lifted off the ground: pieces lying in a switched-on rock (the dropped ship met it
        /// in its last step, or the ship stood against one) are moved out of it, all together along the deepest one's way out, then lifted
        /// off the ground again when that move took one into it.</summary>
        static void RocksAtBreak(Group grp)
        {
            if (grp == null || !OzRockSolid.AllOn || OzRockSolid.Layer < 0) return;
            var total = Vector3.zero;
            for (int it = 0; it < 6; it++)
            {
                float deep = 0f; var way = Vector3.zero;
                foreach (var pc in grp.pieces)
                {
                    var c = pc ? pc.col : null; if (!c || !c.enabled) continue;
                    var b = c.bounds;
                    int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.02f, rockHits, Quaternion.identity, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                    {
                        var rc = rockHits[i]; if (!OzRockSolid.IsRock(rc)) continue;
                        if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out var dir, out float d) && d > deep)
                        { deep = d; way = dir; }
                    }
                }
                if (deep <= 0.02f) break;
                var move = way * (deep + 0.01f);
                if ((total + move).magnitude > RockShiftMaxM) move = Vector3.ClampMagnitude(total + move, RockShiftMaxM) - total;
                if (move.sqrMagnitude < 1e-6f) break;
                foreach (var pc in grp.pieces) if (pc) pc.transform.position += move;
                total += move;
                Physics.SyncTransforms();
            }
            if (total.sqrMagnitude < 1e-6f) return;
            float lift = 0f; foreach (var pc in grp.pieces) if (pc) lift = Mathf.Max(lift, pc.LiftNeed());
            if (lift > 0f) { foreach (var pc in grp.pieces) if (pc) pc.transform.position += Vector3.up * lift; Physics.SyncTransforms(); }
            RockShifts++; RockShiftLargest = Mathf.Max(RockShiftLargest, total.magnitude);
        }
    }
}
