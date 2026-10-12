using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The ruin's fire of a destroyed building: where its ground fires and smoke columns stand. The burning itself (how long, the
    /// caps, the end) is OzRuinFires; this part only knows the ruin. Spots lie on the ruin's free floor, clear of the standing
    /// remains, at the floor's own height (so a flame stands on the rubble floor or comes out of a mound, never in the air), on dry
    /// ground only, from the building's seed: the same building burns in the same places every time. The fort has its own blaze
    /// and gets none.
    /// </summary>
    public partial class BuildingBreaker
    {
        /// <summary>A spot keeps this far from the standing remains (m) and from the next spot (m).</summary>
        public static float RuinFireClear = 0.6f, RuinFireGap = 1.4f;

        /// <summary>The collapse has begun: this ruin burns for a while (not the fort). `bounds` = the fallen building's footprint.</summary>
        void RuinFireBegin(Bounds bounds)
        {
            if (!OzRuinFires.Enabled || obj == null || obj.IsFort) return;
            OzRuinFires.Building(obj, RuinFireSpots(bounds, OzRuinFires.FiresPerRuin + OzRuinFires.SmokesPerRuin));
        }

        /// <summary>The building stands again (crane, repair): its fire goes at once.</summary>
        void RuinFireEnd() { if (obj != null) OzRuinFires.End(obj.Id); }

        List<Vector3> RuinFireSpots(Bounds bounds, int want)
        {
            var spots = new List<Vector3>(); var rnd = new System.Random(obj.Id * 9973 + 39 + 404);
            shellSets.TryGetValue(Key(obj.Type), out var s);
            if (s != null && s.dist != null && ruinGo)
            {   // on the ruin's free floor (the floor grid of BuildingBreaker.Shell.cs), clear of the remains; a narrow floor: any free cell
                int N = ShellGrid; var b = s.box; var rt = ruinGo.transform; var open = new List<int>();
                for (int i = 0; i < N * N; i++) if (s.dist[i] >= RuinFireClear) open.Add(i);
                if (open.Count == 0) for (int i = 0; i < N * N; i++) if (s.dist[i] > 0f) open.Add(i);
                for (int t = 0; t < want * 12 && spots.Count < want && open.Count > 0; t++)
                {
                    int c = open[rnd.Next(open.Count)];
                    var w = rt.TransformPoint(new Vector3(b.min.x + b.size.x * (c % N + (float)rnd.NextDouble()) / N, b.min.y, b.min.z + b.size.z * (c / N + (float)rnd.NextDouble()) / N));
                    RuinFireSpot(spots, w.x, w.z, w.y + ShellFloorLift);
                }
            }
            // no floor grid (a building without a ruin mesh): the middle of its footprint, on the ground
            for (int t = 0; t < want * 8 && spots.Count < want; t++)
                RuinFireSpot(spots, Mathf.Lerp(bounds.min.x, bounds.max.x, 0.2f + 0.6f * (float)rnd.NextDouble()), Mathf.Lerp(bounds.min.z, bounds.max.z, 0.2f + 0.6f * (float)rnd.NextDouble()), float.NegativeInfinity);
            return spots;
        }

        /// <summary>Adds the spot at (x, z) unless it is over open water or too near another one. `floorY` = the ruin floor's lowest
        /// height there (the model's base plus the floor's lift); the spot stands on the higher of it and the ground.</summary>
        static void RuinFireSpot(List<Vector3> spots, float x, float z, float floorY)
        {
            if (!OpenZPyroFire.DrySpot(x, z, out var g)) return;
            var p = new Vector3(x, Mathf.Max(g.y + ShellFloorLift, floorY) + 0.02f, z);
            for (int i = 0; i < spots.Count; i++) { float dx = spots[i].x - x, dz = spots[i].z - z; if (dx * dx + dz * dz < RuinFireGap * RuinFireGap) return; }
            spots.Add(p);
        }
    }
}
