using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K419b, the mark visible proof's ground stretches (test only). Run 28 on Level 16 found "only sand": no grass, no road, no water.
    /// <list type="bullet">
    /// <item>Grass: the old test called a spot grass only where the drawn ground's dust strength is under 0.25. Level 16's jungle grass
    /// is painted saturated, so the ground read-back counts it as soft (dust 1.00): the stretch the proof called "sand" at (132, -108) IS
    /// the green grass (its picture). On the jungle the soft ground is now told apart by its painted colour: green-olive (green at
    /// least 0.9 x red and 1.25 x blue) = grass, else (brown, yellow) = sand / dirt. Other planets keep the old test.</item>
    /// <item>Road and water: the old stretch was 12 m along x and 5 m across, the class weights over 0.85 everywhere. Level 16's roads are
    /// one tile (2 m) wide and its water runs diagonally (tools/l16_classes.py on the live map: 0 such road stretches, water only at
    /// 30-45 deg). Now the stretch may run along 8 headings (along x first, then z, then the diagonals) and a road or a narrow water
    /// channel is tested on its middle line +-1 m (12 m long); wide water +-2.5 m is preferred. Grass and sand keep +-2.5 m. The ship
    /// flies the stretch along its heading (the run-up, the pass and the measuring point turned with it).</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleMarkVisibleProof
    {
        /// <summary>Headings tried (deg from +x toward +z), in this order.</summary>
        static readonly float[] K419Headings = { 0f, 90f, 45f, 135f, 30f, 60f, 120f, 150f };
        readonly Dictionary<Vector2, Vector2> k419Dir = new Dictionary<Vector2, Vector2>();

        /// <summary>The flight direction over a stretch found here (+x for any other point).</summary>
        Vector2 StretchDir(Vector2 mid) => k419Dir.TryGetValue(mid, out var d) ? d : Vector2.right;

        /// <summary>A ground kind at (x, z): 0 grass, 1 road, 2 sand / dirt, 3 water, -1 mixed (the jungle's soft ground by its colour).</summary>
        static int KindAtK419(TrackSurface sf, bool jungle, float x, float z)
        {
            sf.Classes(x, z, out float road, out float water, out float rock);
            if (water > 0.85f) return 3;
            if (water > 0.1f || rock > 0.15f) return -1;
            if (road > 0.85f) return 1;
            if (road > 0.1f) return -1;
            float dust = sf.DustStrength(x, z);
            if (!jungle) return dust > 0.6f ? 2 : dust < 0.25f ? 0 : -1;
            if (dust <= 0.6f) return -1;                                                                  // the jungle: hard ground is neither
            var c = sf.DustColor(x, z);
            return c.g >= 0.9f * c.r && c.g >= 1.25f * c.b ? 0 : 2;
        }

        /// <summary>Instead of FindStretch: the middle of a 12 m stretch of one kind (any of the headings), nearest the level's middle, away
        /// from forts; its heading kept for the pass.</summary>
        bool FindStretchK419(TrackSurface sf, int kind, out Vector2 mid)
        {
            mid = default;
            bool jungle = W != null && W.Map != null && W.Map.planet == "jungle";
            float[] widths = kind == 3 ? new[] { 2.5f, 1.0f } : kind == 1 ? new[] { 1.0f } : new[] { 2.5f };
            var c0 = new Vector2(112f, -100f);
            foreach (var hw in widths)
                foreach (var hd in K419Headings)
                {
                    float a = hd * Mathf.Deg2Rad; var u = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var v = new Vector2(-u.y, u.x);
                    float best = float.MaxValue; Vector2 bm = default;
                    for (float z = -170f; z <= -28f; z += 2f)
                        for (float x = 26f; x <= 196f; x += 2f)
                        {
                            float dd = (new Vector2(x, z) - c0).sqrMagnitude; if (dd >= best) continue;
                            if (KindAtK419(sf, jungle, x, z) != kind) continue;
                            bool ok = true;
                            for (float s = -6f; s <= 6f && ok; s += 1.5f)
                                for (float w = -hw; w <= hw + 1e-3f && ok; w += hw)
                                {
                                    float px = x + u.x * s + v.x * w, pz = z + u.y * s + v.y * w;
                                    if (KindAtK419(sf, jungle, px, pz) != kind || FortGunView.FortTopAt(new Vector3(px, 0f, pz), FarAway) > float.MinValue) ok = false;
                                }
                            if (ok) { best = dd; bm = new Vector2(x, z); }
                        }
                    if (best < float.MaxValue)
                    {
                        mid = bm; k419Dir[bm] = u;
                        Log($"stretch kind {kind}: heading {hd:F0} deg, +-{hw:F1} m across, 12 m long{(jungle ? " (the jungle: grass and sand by the painted colour " + (Color32)sf.DustColor(bm.x, bm.y) + ")" : "")}");
                        return true;
                    }
                }
            return false;
        }
    }
}
