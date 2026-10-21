using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 331: the fort cannon's pieces against the rocks (OzRockSolid makes the rocks round the landing solid while the
    /// pieces exist):
    /// <list type="bullet">
    /// <item>every collider of a piece (and of the rams riding on it) includes the rock layer, so the pieces hit the rocks, clank / crash
    /// and raise dust there as on the ground (the same contact code), slide off and come to rest on or beside them;</item>
    /// <item>on the landing frame the rocks near it are made at once, and a piece that would start in a rock (the toss lands on free ground,
    /// so none should) is moved straight out of it;</item>
    /// <item>resting: a piece on a rock rests "on rock" where it lies; the step that puts a piece onto the ground, and the push out of
    /// another piece, never move it into a rock;</item>
    /// <item>when the rock part it rests on or leans against goes (the stump replaces the pillar, or the rules remove the rock), the piece
    /// wakes and falls again (it may sound and rest again);</item>
    /// <item>at the end of the wreck time a piece lying on a rock pops away with a puff (it cannot sink through the rock); the others sink
    /// into the ground as before.</item>
    /// </list>
    /// </summary>
    public partial class OzFortGunBreakup
    {
        /// <summary>Statistics (Play check): contacts with a rock, pieces moved out of a rock on the landing frame (the deepest, m), pieces
        /// woken when their rock part went, pieces popped away on a rock at the end.</summary>
        public static int RockTouches, RockStartMoves, RockWoken, RockPops;
        public static float RockStartWorst;
        public static string LastRockStart = "-";
        /// <summary>The pieces alive now (OzRockSolid keeps the rocks solid while there are any).</summary>
        public static int LiveCount => live.Count;

        Collider restCol, leanCol;
        float wokeAt;
        static readonly Collider[] rockBuf = new Collider[32];

        static void RockLayerOn(Collider c) => OzRockSolid.Include(c);

        /// <summary>Break(), after the lift: the rocks near the landing are made now, and a piece that starts in a rock is moved out.</summary>
        static void RocksAtStart(Group grp, Vector3 impact)
        {
            if (!OzRockSolid.Enabled || OzRockSolid.Layer < 0 || grp == null) return;
            OzRockSolid.BuildNear(impact, OzRockSolid.ArmRadius + 4f);
            Physics.SyncTransforms();
            float worst = 0f; int moved = 0;
            foreach (var pc in grp.pieces) { if (!pc) continue; float d = pc.OutOfRocks(); if (d > 0f) { moved++; worst = Mathf.Max(worst, d); } }
            RockStartMoves += moved; RockStartWorst = Mathf.Max(RockStartWorst, worst);
            LastRockStart = moved > 0 ? $"{moved} piece(s) started in a rock (deepest {worst * 100f:0} cm) and were moved out" : "no piece started in a rock";
        }

        /// <summary>Moves this piece straight out of every switched-on rock collider its colliders lie in deeper than 1 cm (up to 4
        /// rounds); returns the deepest depth it had (m, 0 = clear).</summary>
        float OutOfRocks()
        {
            float worst = 0f;
            for (int round = 0; round < 4; round++)
            {
                bool any = false;
                foreach (var a in cols)
                {
                    if (!a || !a.enabled) continue;
                    var b = a.bounds;
                    int n = Physics.OverlapBoxNonAlloc(b.center, b.extents + Vector3.one * 0.05f, rockBuf, Quaternion.identity, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < n; i++)
                    {
                        var rc = rockBuf[i];
                        if (!OzRockSolid.IsRock(rc)) continue;
                        if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out var dir, out float dist) && dist > 0.01f)
                        {
                            transform.position += dir * (dist + 0.01f); Physics.SyncTransforms();
                            worst = Mathf.Max(worst, dist); any = true;
                        }
                    }
                }
                if (!any) break;
            }
            return worst;
        }

        /// <summary>How deep (m) its colliders would lie in a switched-on rock if it moved by `delta` (DepthIfMoved: the step onto the
        /// ground never goes into a rock).</summary>
        float RockDepthIfMoved(Vector3 delta)
        {
            if (OzRockSolid.Layer < 0) return 0f;
            float deep = 0f;
            foreach (var a in cols)
            {
                if (!a || !a.enabled) continue;
                var b = a.bounds;
                int n = Physics.OverlapBoxNonAlloc(b.center + delta, b.extents + Vector3.one * 0.05f, rockBuf, Quaternion.identity, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var rc = rockBuf[i];
                    if (OzRockSolid.IsRock(rc) && Physics.ComputePenetration(a, a.transform.position + delta, a.transform.rotation, rc, rc.transform.position, rc.transform.rotation, out _, out float d))
                        deep = Mathf.Max(deep, d);
                }
            }
            return deep;
        }

        /// <summary>OzRockSolid: these rock colliders go. The pieces resting on or leaning against them wake and fall again; how many.</summary>
        internal static int SupportGone(List<Collider> rock)
        {
            if (rock == null || rock.Count == 0) return 0;
            int n = 0;
            foreach (var p in live)
            {
                if (!p || p.watcher || p.retiring || !p.frozen) continue;
                if ((p.restCol && rock.Contains(p.restCol)) || (p.leanCol && rock.Contains(p.leanCol))) { p.WakeFromRest(); n++; }
            }
            RockWoken += n;
            return n;
        }

        void WakeFromRest()
        {
            frozen = false; snapT = -1f;
            if (RestOn == "ground") OnGround--; else if (RestOn.StartsWith("on ")) OnOther--; else if (RestOn == "floating") Floating--; else if (RestOn == "sunk") Sunk--; else if (RestOn == "water") InWaterCount--;
            if (Rested) { Rested = false; RestedCount--; }
            RestOn = "-"; restCol = null; leanCol = null;
            hits = 0; crashed = false; lastHit = -1f; knockTries = 0;                                      // it lands again: heard again
            wokeAt = age;
            if (rb) { rb.isKinematic = false; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.WakeUp(); }
        }

        /// <summary>Retire(): a piece lying on a rock pops away with a puff instead of sinking through the rock.</summary>
        void RetireOnRock()
        {
            if (!OzRockSolid.Enabled || OzRockSolid.Mask == 0) return;
            bool onRock = RestOn == "on rock" || Physics.Raycast(Centre() + Vector3.up * 0.2f, Vector3.down, Size + 0.6f, OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
            if (!onRock) return;
            sinkSpeed = 0f; sinkT = 0f; RockPops++;
            if (WreckFx.OnScreen(transform.position)) FxManager.I?.Dust(Centre(), 0.5f);
            PopOut.Begin(transform, PopOut.Style.Debris, () => Faded++, true, false);
        }
    }
}
