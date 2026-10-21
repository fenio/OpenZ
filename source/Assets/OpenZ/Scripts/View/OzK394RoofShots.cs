using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K394: the drawn rounds of a fort's roof defender (the sniper, DV111; the grunt with the rule off) leave his muzzle and fly to their
    /// drawn target point: straight at the flying shuttle and at anything his line clears, and only over the fort's real parapet when the
    /// straight line would run through it.
    /// <para>The fault: ShotClearance (the rule that bends a straight round over a bank or a fort) asked FortGunView.TopAt for the fort's
    /// height, and TopAt counts every point of a live fort's footprint as solid up to its WallTop (8.76 m) plus the 0.3 m margin. The roof
    /// man's muzzle is at 7.3-7.8 m (deck 6.37 m + kneeling / standing), under that block wherever he stands, so EVERY round of his was bent
    /// over a waypoint 1.2-8 m over his muzzle: the tracer's (and the vapour trail's) second leg left that point high above him.
    /// </para>
    /// <para>The fix (view only, two hooks): a muzzle on a fort's roof deck (<see cref="OnRoofDeck"/>, fort-local) no longer meets its own
    /// fort in TopAt; instead <see cref="OverParapet"/> (called first in ShotClearance.Bend) walks his line over the fort's real parapet
    /// (<see cref="Parapet"/>, measured on the FortBase mesh: side walls 7.05 m with a 7.5 m bump and two 7.32 m vents, front 7.21 m, rear 7.05 m with a
    /// 7.67 m hump; the thin flag pole and lamp posts left out) every 5 cm: under the wall top + margin anywhere = one waypoint on the wall's outer edge just over it (the round leaves his
    /// muzzle, clears the wall, goes on to its target), else the round stays straight (the terrain and other forts are still tested as
    /// before). The towers and the gun block are not obstacles here: the rules never let him fire through them (FortDefenderLineClear).
    /// Tower guns, the fort's big gun and everything else keep the old test (their muzzles are above the deck band).</para>
    /// </summary>
    public static class OzK394RoofShots
    {
        /// <summary>The kit's switch (the K394 check's BEFORE run turns it off for its run and puts it back).</summary>
        public static bool On = true;

        // ------------------------------------------------------------------ the roof deck (fort-local metres, the fort view's frame)
        /// <summary>Where a roof man's muzzle can be: over the deck (soles 6.37-6.42 m, muzzle up to ~8.5 m aiming high) out to the
        /// nothing else has a muzzle 5.8-9.0 m up inside the fort's walls), never over the gun block. Tower guns (pads 9.25 m) and the
        /// fort's big gun (>= 9.3 m) are above it.</summary>
        public const float DeckMinY = 5.8f, DeckMaxY = 9.0f, DeckHalfX = 7.55f, DeckZ0 = -7.8f, DeckZ1 = 6.25f;
        public const float GunHalfX = 3.6f, GunZ0 = -4.1f, GunZ1 = 2.95f;

        // ------------------------------------------------------------------ the parapet (FortBase height map, 5 cm)
        public const float OuterX = 7.55f, OuterZ0 = -7.8f, OuterZ1 = 6.25f, InnerX = 6.45f, InnerZ0 = -6.65f, InnerZ1 = 5.45f;
        public const float SideTop = 7.05f, SideBumpTop = 7.5f, SideBumpZ0 = -2.0f, SideBumpZ1 = 0.8f, SideVentTop = 7.32f, SideVentZa = -3.0f, SideVentZb = 2.3f, SideVentHalf = 0.25f;
        public const float FrontTop = 7.21f, RearTop = 7.05f, RearHumpTop = 7.67f, RearHumpX = 2.6f;
        /// <summary>Air between his round and the wall top (m; the general test's 0.3 m is kept for everything else), and the highest a
        /// second leg may start over his muzzle (m: a muzzle right behind a wall as high as itself would otherwise throw it far up).</summary>
        public static float RoofMargin = 0.1f, MaxRise = 1.0f;
        const float Step = 0.05f, MaxWalk = 25f, MinInner = 0.3f;

        /// <summary>Counts (the check): roof rounds seen, sent over the wall, left straight.</summary>
        public static int RoofRounds, OverWall, Straight;

        /// <summary>A fort-local point where a roof man's muzzle can be (no switch).</summary>
        public static bool InDeckBand(Vector3 l)
        {
            if (l.y < DeckMinY || l.y > DeckMaxY) return false;
            if (Mathf.Abs(l.x) > DeckHalfX || l.z < DeckZ0 || l.z > DeckZ1) return false;
            return !(Mathf.Abs(l.x) < GunHalfX && l.z > GunZ0 && l.z < GunZ1);
        }

        /// <summary>FortGunView.TopAt: this muzzle (fort-local) is on the fort's roof deck: the fort's block is not its obstacle
        /// (<see cref="OverParapet"/> handles its walls).</summary>
        public static bool OnRoofDeck(Vector3 lm) => On && InDeckBand(lm);

        /// <summary>The parapet's top (fort-local metres) at (x, z); float.MinValue on the deck inside it and past its outer faces.</summary>
        public static float Parapet(float x, float z)
        {
            float ax = Mathf.Abs(x);
            if (ax > OuterX || z < OuterZ0 || z > OuterZ1) return float.MinValue;
            if (ax < InnerX && z > InnerZ0 && z < InnerZ1) return float.MinValue;
            float h = float.MinValue;
            if (ax >= InnerX) h = z >= SideBumpZ0 && z <= SideBumpZ1 ? SideBumpTop : Mathf.Abs(z - SideVentZa) <= SideVentHalf || Mathf.Abs(z - SideVentZb) <= SideVentHalf ? SideVentTop : SideTop;
            if (z >= InnerZ1) h = Mathf.Max(h, FrontTop);
            if (z <= InnerZ0) h = Mathf.Max(h, ax <= RearHumpX ? RearHumpTop : RearTop);
            return h;
        }

        // ------------------------------------------------------------------ the live forts (refreshed once a second)
        static readonly List<Transform> forts = new List<Transform>(4);
        static float fortsAt = -10f; static ZWorld fortsWorld;
        static void Refresh()
        {
            var g = ZGame.I;
            if (!g || g.World == null) { forts.Clear(); fortsWorld = null; return; }
            if (g.World == fortsWorld && Time.time - fortsAt < 1f && Time.time >= fortsAt) return;
            fortsWorld = g.World; fortsAt = Time.time; forts.Clear();
            foreach (var o in g.World.Objects)
                if (o != null && o.IsFort && !o.Destroyed && g.StaticViews.TryGetValue(o.Id, out var go) && go && go.activeInHierarchy) forts.Add(go.transform);
        }

        /// <summary>The live fort whose roof deck holds this muzzle (its view's transform) and the muzzle in its frame.</summary>
        public static bool RoofFort(Vector3 muzzle, out Transform fort, out Vector3 local)
        {
            fort = null; local = default;
            Refresh();
            for (int k = 0; k < forts.Count; k++)
            {
                var t = forts[k]; if (!t) continue;
                var l = t.InverseTransformPoint(muzzle);
                if (InDeckBand(l)) { fort = t; local = l; return true; }
            }
            return OzK421RoofDefenders.Defender(muzzle, out fort, out local);                   // K421: else his fort's roof defender by who he is (his drawn muzzle), at any height
        }

        /// <summary>ShotClearance.Bend, first: a straight round from a roof man's muzzle. True = it runs under his fort's parapet top +
        /// margin: w = the waypoint on the wall's outer edge over it (both legs clear the wall). False = not a roof round, or straight over
        /// the wall (Bend goes on with the terrain and the other forts; TopAt leaves his own fort out).</summary>
        public static bool OverParapet(Vector3 from, Vector3 to, float margin, out Vector3 w)
        {
            w = default;
            if (!On || !RoofFort(from, out var t, out var a)) return false;
            RoofRounds++;
            var b = t.InverseTransformPoint(to);
            float dx = b.x - a.x, dz = b.z - a.z, h = Mathf.Sqrt(dx * dx + dz * dz);
            if (h < 0.5f) { Straight++; return false; }
            margin = Mathf.Min(margin, RoofMargin);
            float ux = dx / h, uz = dz / h, slope = (b.y - a.y) / h, end = Mathf.Min(h, MaxWalk);
            float dIn = -1f, dOut = -1f, top = float.MinValue, need = 0f;
            for (int i = 1; i * Step <= end; i++)
            {
                float s = i * Step, p = Parapet(a.x + ux * s, a.z + uz * s);
                if (p == float.MinValue) { if (dIn >= 0f) break; continue; }                   // out past the wall: one crossing from the deck
                if (dIn < 0f) dIn = s;
                dOut = s; if (p > top) top = p;
                need = Mathf.Max(need, p + margin - (a.y + slope * s));
            }
            if (dIn < 0f || need <= 0.001f) { Straight++; return false; }                       // over the wall top: straight
            float want = top + margin, floor = Mathf.Max(want, a.y + slope * dOut), wy = floor;
            if (a.y < want) wy = Mathf.Max(wy, a.y + (want - a.y) * dOut / Mathf.Max(MinInner, dIn));   // muzzle under the wall top: leg 1 clears its inner edge too
            wy = Mathf.Min(wy, Mathf.Max(floor, a.y + MaxRise));                                // ... but never far up over him
            w = t.TransformPoint(new Vector3(a.x + ux * dOut, wy, a.z + uz * dOut));
            OverWall++;
            return true;
        }

        // ------------------------------------------------------------------ the check (OzK394RoofShotLog)
        /// <summary>Set by the running check; null = nothing logged (the hook costs one null test per drawn round).</summary>
        public static OzK394RoofShotLog Log;
        /// <summary>OpenZFxPlayer.EmitRound: a straight round is drawn from -> to (bent: two legs, the second from w).</summary>
        public static void Note(Vector3 from, Vector3 to, bool bent, Vector3 w) { if (Log) Log.Round(from, to, bent, w); }
    }
}
