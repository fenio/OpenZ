#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys: counts the shots of every unit from the game's own fire events (hitscan, missile, tesla bolt) for the
// take log's laser lines (OzEpicFlyby.Kf4.cs). It reads the event list of the frame before the combat view clears it (the combat view
// runs at order 50); it never changes the list. Added by a take, gone with it; editor only.
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    [DefaultExecutionOrder(40)]
    public sealed class OzEpicFlybyShots : MonoBehaviour
    {
        public ZWorld W;
        readonly Dictionary<int, int> shots = new Dictionary<int, int>();

        /// <summary>Shots fired so far by this object id (since the take added this counter).</summary>
        public int ShotsOf(int id) => shots.TryGetValue(id, out int n) ? n : 0;

        void LateUpdate()
        {
            if (W == null) return;
            var ev = W.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type != ZEventType.Hitscan && e.type != ZEventType.MissileFired && e.type != ZEventType.ElectroBolt) continue;
                shots.TryGetValue(e.objId, out int n); shots[e.objId] = n + 1;
            }
        }
    }
}
#endif
