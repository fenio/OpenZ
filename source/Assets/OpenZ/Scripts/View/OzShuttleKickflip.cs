using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K344, the shuttle's kickflip (an easter egg, view only): PlayerInput's hook. The game gives orders on the RIGHT button's release
    /// (a left double-click already means "every unit of that type on screen"); a double right-click was two identical orders until now.
    /// So the egg's gesture is the double RIGHT-click of a plain move (no Shift queue, no nearest-only Z): two order clicks within 0.35 s
    /// and about 36 px (x the UI scale) / 6 m of each other (<see cref="OzKickflipGesture"/>). Both clicks still give their orders exactly as
    /// before: this only remembers the click for each shuttle of the selection (a group: each shuttle in it; every other unit nothing),
    /// and the ship's view decides in its LateUpdate whether its order took and is far enough (View/OzPlayableShuttle.Kickflip.cs). The
    /// touch and gamepad layer gives its orders through the same virtual mouse, so a double order there is the same gesture. The minimap's
    /// right-click orders are a path of their own and are not part of it.
    /// </summary>
    public static class OzShuttleKickflip
    {
        static readonly OzKickflipGesture gesture = new OzKickflipGesture();
        static readonly int[] ids = new int[OzKickflipGesture.MaxShips];
        static readonly bool[] both = new bool[OzKickflipGesture.MaxShips];
        /// <summary>Proof counts: order clicks seen, double clicks, double clicks with at least one shuttle in both.</summary>
        public static int Clicks, Doubles, ShipDoubles;
        /// <summary>The last double click's ships (the Play proof reads it).</summary>
        public static int LastShips;

        /// <summary>PlayerInput, a right-button order click on the map (before its orders are flushed): queued = a Shift queue click,
        /// nearestOnly = the Z order (one unit only).</summary>
        public static void OrderClick(List<ZObject> selection, Vector2 screen, Vector3 ground, bool queued, bool nearestOnly)
        {
            if (selection == null) return;
            Clicks++;
            int n = 0;
            for (int k = 0; k < selection.Count && n < ids.Length; k++)
            {
                var o = selection[k];
                if (o != null && o.IsShuttle && !o.Destroyed) ids[n++] = o.Id;
            }
            float ui = Mathf.Max(0.75f, Screen.height / 1080f);
            bool dbl = gesture.Click(Time.unscaledTime, screen.x, screen.y, ground.x, ground.z, ui, !queued && !nearestOnly, ids, n, both);
            if (dbl) Doubles++;
            if (n == 0) return;
            var px = ZGame.WorldToPx(ground); int ships = 0;
            for (int k = 0; k < n; k++)
            {
                var v = OzPlayableShuttle.OfId(ids[k]); if (!v) continue;
                bool d = dbl && both[k]; if (d) ships++;
                if (!queued) v.KickflipClick(d, px.x, px.y);                                             // a queue click is no order yet
                else v.KickflipQueued(px.x, px.y);                                                        // K411: ... until the queue is sent (then it may flip by itself)
            }
            if (ships > 0) { ShipDoubles++; LastShips = ships; }
        }
    }
}
