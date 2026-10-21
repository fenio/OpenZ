using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the waypoint dots (Play mode, any level): 40 straight legs of 14 m over walkable tiles at the places
    /// where the ground is least flat (slopes, ramps, banks, gorge rims), with up to 4 legs over a bridge deck and 6 through
    /// shallow water. For every dot of a leg: its height minus the surface drawn under it (the rendered ground, a bridge deck
    /// or the water surface), once by the rule before version 1.1 (a straight line between the leg's two ends) and once by the
    /// rule the game uses now (SelectionOverlay.Seat). PASS = no dot below the surface and all between +0.03 and +0.25 m.
    /// The surface it measures against is the game's own look-up of the rendered ground (TrackSurface): where that look-up is
    /// off (close to a gorge wall on level 16) the count cannot see it; pictures do.
    /// </summary>
    public static class OzV11DotsCount
    {
        const float Len = 14f, First = 0.8f, Step = 1.2f, Lift = 0.1f;

        [MenuItem("OpenZ/V1.1/Count Waypoint Dots On The Surface (b14, Play mode)")]
        static void Menu() => Debug.Log("[OpenZ V1.1] b14 waypoint dots\n" + Run());

        public static string Run()
        {
            if (!Application.isPlaying || !ZGame.I || ZGame.I.World == null) return "Play mode in a level first.";
            var g = ZGame.I; var W = g.World; var surf = TrackMarks.I ? TrackMarks.I.Surface : null; if (surf == null) return "no track surface";
            var fxp = OpenZFxPlayer.I; int mw = W.Map.width, mh = W.Map.height;
            float RefY(Vector3 p)
            {
                if (!surf.Sample(p.x, p.z, out float y, out bool deck)) y = g.GroundY(p.x, p.z);
                if (!deck && OpenZFxPlayer.IsWaterTile(p) && fxp) { float wy = fxp.WaterYAt(p); if (!float.IsNegativeInfinity(wy) && wy > y) y = wy; }
                return y;
            }
            bool IsDeck(Vector3 p) { surf.Sample(p.x, p.z, out _, out bool deck); return deck; }
            bool Passable(float x, float z)
            {
                int tx = (int)(x / 2f), ty = (int)(-z / 2f); if (tx < 1 || ty < 1 || tx >= mw - 1 || ty >= mh - 1) return false;
                var c = W.Map.ClassAt(tx, ty); return c != ZClass.Impassable && c != ZClass.DeepWater;
            }
            var legs = new List<float[]>();
            for (float x = 9f; x < mw * 2f - 22f; x += 4f)
                for (float z = -9f; z > -(mh * 2f - 22f); z -= 4f)
                    for (int k = 0; k < 2; k++)
                    {
                        float bx = x + (k == 0 ? Len : 0f), bz = z - (k == 0 ? 0f : Len);
                        if (!Passable(x, z) || !Passable(bx, bz)) continue;
                        var a = new Vector3(x, 0, z); var b = new Vector3(bx, 0, bz); float ya = g.GroundY(x, z), yb = g.GroundY(bx, bz);
                        bool deck = IsDeck(a) || IsDeck(b), water = false, ok = true; float worst = 0f;
                        for (float s = First; s < Len; s += Step)
                        {
                            var p = Vector3.Lerp(a, b, s / Len); if (!Passable(p.x, p.z)) { ok = false; break; }
                            worst = Mathf.Max(worst, Mathf.Abs(Mathf.Lerp(ya, yb, s / Len) - RefY(p))); if (OpenZFxPlayer.IsWaterTile(p)) water = true;
                        }
                        if (ok) legs.Add(new[] { x, z, bx, bz, worst, deck ? 1f : 0f, water ? 1f : 0f });
                    }
            legs.Sort((p, q) => q[4].CompareTo(p[4]));
            var pick = new List<float[]>();
            bool Far(float[] l) { foreach (var q in pick) if (Mathf.Abs(q[0] - l[0]) + Mathf.Abs(q[1] - l[1]) < 10f) return false; return true; }
            int decks = 0, waters = 0;
            foreach (var l in legs) if (l[5] > 0.5f && decks < 4 && Far(l)) { pick.Add(l); decks++; }
            foreach (var l in legs) if (l[6] > 0.5f && l[5] < 0.5f && waters < 6 && Far(l)) { pick.Add(l); waters++; }
            foreach (var l in legs) { if (pick.Count >= 40) break; if (Far(l)) pick.Add(l); }
            int dots = 0, oldBelow = 0, oldOut = 0, newBelow = 0, newOut = 0; float oldMin = 9f, oldMax = -9f, newMin = 9f, newMax = -9f;
            foreach (var l in pick)
            {
                var a = new Vector3(l[0], 0, l[1]); var b = new Vector3(l[2], 0, l[3]); float ya = g.GroundY(a.x, a.z), yb = g.GroundY(b.x, b.z); bool deck = l[5] > 0.5f;
                for (float s = First; s < Len; s += Step)
                {
                    var p = Vector3.Lerp(a, b, s / Len); float r = RefY(p);
                    float dOld = Mathf.Lerp(ya, yb, s / Len) + Lift - r, dNew = SelectionOverlay.Seat(g, p, Lift, deck).y - r;
                    dots++; if (dOld < 0f) oldBelow++; if (dOld < 0.03f || dOld > 0.25f) oldOut++; if (dNew < 0f) newBelow++; if (dNew < 0.03f || dNew > 0.25f) newOut++;
                    oldMin = Mathf.Min(oldMin, dOld); oldMax = Mathf.Max(oldMax, dOld); newMin = Mathf.Min(newMin, dNew); newMax = Mathf.Max(newMax, dNew);
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine($"level {g.level}: {pick.Count} straight legs of {Len:0} m over walkable tiles, the hardest of {legs.Count} looked at ({decks} over a bridge deck, {waters} through shallow water), {dots} dots");
            sb.AppendLine($"dot height minus the drawn surface, rule before 1.1 (straight line between the leg's ends + {Lift:0.00}): {oldMin:0.00} to {oldMax:0.00} m; below the surface {oldBelow}, outside +0.03 .. +0.25 m {oldOut}");
            sb.AppendLine($"rule now (each dot on the surface + {Lift:0.00}): {newMin:0.00} to {newMax:0.00} m; below the surface {newBelow}, outside +0.03 .. +0.25 m {newOut}");
            sb.AppendLine((newBelow == 0 && newOut == 0 ? "PASS" : "FAIL") + $"  every dot stands between +0.03 and +0.25 m over the drawn surface ({dots - newOut} of {dots})");
            sb.Append("hardest legs (world x, z to x, z; error of the old rule in m):");
            for (int i = decks + waters; i < decks + waters + 3 && i < pick.Count; i++) sb.Append($"  {pick[i][0]:0},{pick[i][1]:0} to {pick[i][2]:0},{pick[i][3]:0} ({pick[i][4]:0.00})");
            sb.AppendLine();
            sb.AppendLine($"brightness factor of the dots now {SelectionOverlay.DotBrightness:0.000} at day / night value {NightGrade.NightK:0.00} (1.000 by day, {SelectionOverlay.NightDim:0.00} at full night)");
            return sb.ToString();
        }
    }
}
