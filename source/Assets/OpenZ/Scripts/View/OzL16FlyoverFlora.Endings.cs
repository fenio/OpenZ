// OpenZ (GPL-3.0) - Level 16, round 4: the low-shot look in the endings, and its counted check.
//  - The far hills of a low shot are held low under the drawn sun's bearing only for the mission-start flyover, where the rising sun
//    stands on the horizon. An ending (any hour) and a picture taken with ForceOn get the hills' natural crest there too: held low, the
//    crest ran flat and ruler straight across the horizon behind the endings' blast.
//    shapes), and drawn plants whose lowest point stands more than FloatAbove m over the ground under them (leaves in the air).
//    Call it in Play at any moment of an ending; it counts what the game camera drew in the frame before.
using System.Text;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16FlyoverFlora
    {
        /// <summary>A plant counts as floating when its lowest point stands this far over the ground under its middle (m).</summary>
        public static float FloatAbove = 3f;

        /// <summary>BuildFar: the hills are held low under the sun's bearing (only while the mission-start flyover shows the rising sun).</summary>
        static bool FarGapWanted()
        {
            var fly = ZodFlyover.Live;
            return fly != null && fly.Active && fly.Cine && fly.SkyWanted;
        }

        /// <summary>Play check: what of the level's plants the game camera drew in the last frame that a low shot must not show.</summary>
        public static string LeafCheck()
        {
            if (!I) return "leaf check: not in this scene (level 16 only)";
            var root = GameObject.Find(FloraRoot); var g = ZGame.I;
            if (!root || !g) return "leaf check: no plants or no game";
            int farDrawn = 0, floating = 0, seen = 0; var names = new StringBuilder();
            foreach (var lg in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!lg || !lg.enabled || !lg.gameObject.activeInHierarchy) continue;
                var lods = lg.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    if (lods[i].renderers == null) continue;
                    foreach (var r in lods[i].renderers)
                    {
                        if (!r || !r.enabled || !r.isVisible) continue;
                        if (i > 0) { farDrawn++; continue; }
                        seen++;
                        var b = r.bounds; float gy = g.GroundY(b.center.x, b.center.z);
                        if (b.min.y > gy + FloatAbove)
                        {
                            floating++;
                            if (floating <= 6) names.Append(names.Length > 0 ? ", " : "").Append(r.name).Append(" +").Append((b.min.y - gy).ToString("F1")).Append(" m");
                        }
                    }
                }
            }
            return $"leaf check: low-shot look {(LowLookOn ? "ON" : "off")}, far leaf cells drawn {farDrawn}, plants drawn {seen}, of them standing more than {FloatAbove:F0} m over the ground {floating}" + (names.Length > 0 ? " (" + names + ")" : "");
        }
    }
}
