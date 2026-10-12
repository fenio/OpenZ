using System.Collections.Generic;
using System.IO;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play proof for production (DV21 FIFO queue + REPEAT): records what one building builds, in order, from the sim's own events
    /// Runs before CombatView (order 50), which clears the event list. Pure view, read only.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class ProductionTrace : MonoBehaviour
    {
        static ProductionTrace inst;
        int buildingId = -1;
        readonly List<string> rows = new List<string>();
        public static readonly List<string> Built = new List<string>();

        /// <summary>Start tracing a building (clears the previous trace).</summary>
        public static void Begin(ZObject b)
        {
            if (b == null) return;
            if (!inst) inst = new GameObject("OZ_ProductionTrace").AddComponent<ProductionTrace>();
            inst.buildingId = b.Id; inst.rows.Clear(); Built.Clear();
            inst.rows.Add("game_time,event,unit,queue_after,repeat,current_after");
            inst.Row(b, "begin", b.CurUnit);
        }

        void Row(ZObject b, string ev, string unit)
            => rows.Add($"{b.World.Time:F2},{ev},{unit},{string.Join("|", b.Queue)},{(b.RepeatProduction ? "on" : "off")},{b.CurUnit ?? "idle"}");

        void Update()
        {
            var g = ZGame.I; if (buildingId < 0 || g == null || g.World == null) return;
            var W = g.World; var b = W.Get(buildingId); if (b == null) return;
            foreach (var e in W.Events)
            {
                if (e.type == ZEventType.UnitBuilt && e.otherId == buildingId) { var u = W.Get(e.objId); string t = u != null ? u.Type : "?"; Built.Add(t); Row(b, "built", t); }
                else if (e.type == ZEventType.CannonStored && e.objId == buildingId && b.StoredCannons.Count > 0) { string t = b.StoredCannons[b.StoredCannons.Count - 1]; Built.Add(t); Row(b, "stored", t); }
            }
        }

        /// <summary>Writes the trace; returns the path.</summary>
        public static string Save()
        {
            if (!inst) return null;
            var p = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/production_trace.csv"));
            File.WriteAllLines(p, inst.rows);
            return p;
        }
    }
}
