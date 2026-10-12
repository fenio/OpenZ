using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// In-game speed measurement for the parity checks: samples one sim unit every frame (sim time + continuous position)
    /// and keeps the per-frame speeds bucketed by the terrain class under its centre. Medians = measured px/s.
    /// </summary>
    public class SpeedProbe : MonoBehaviour
    {
        public int objectId;
        public readonly Dictionary<ZClass, List<double>> Samples = new Dictionary<ZClass, List<double>>();
        public readonly List<Vector3> Trail = new List<Vector3>();
        double lastT = -1, lastX, lastY; ZClass lastC;

        void LateUpdate()
        {
            var g = ZGame.I; if (g == null || g.World == null) return;
            var o = g.World.Get(objectId); if (o == null) return;
            double t = g.World.Time;
            var c = g.World.Map.ClassAt(o.CenterX >> 4, o.CenterY >> 4);
            if (lastT >= 0 && t > lastT && o.IsMoving && c == lastC)
            {
                double d = System.Math.Sqrt((o.PosX - lastX) * (o.PosX - lastX) + (o.PosY - lastY) * (o.PosY - lastY));
                if (d > 0) { if (!Samples.TryGetValue(c, out var l)) Samples[c] = l = new List<double>(); l.Add(d / (t - lastT)); }
            }
            if (Trail.Count == 0 || (Trail[Trail.Count - 1] - g.CenterWorld(o)).sqrMagnitude > 1f) Trail.Add(g.CenterWorld(o));
            lastT = t; lastX = o.PosX; lastY = o.PosY; lastC = c;
        }

        public string Summary()
        {
            var o = ZGame.I.World.Get(objectId);
            return string.Join(", ", Samples.Select(kv =>
            {
                var l = kv.Value.OrderBy(v => v).ToList();
                return $"{kv.Key}: median {l[l.Count / 2]:F2} px/s over {l.Count} frames";
            })) + $" | move_speed {o?.MoveSpeed}";
        }
    }
}
