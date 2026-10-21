using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// The shuttle's orders on the player's side (rule DV118, K256g; the option SHUTTLE ORDERS, UI/OzShuttleOrdersOption.cs). With LANDABLE
    /// SPOTS ONLY (the default):
    ///  * THE CURSOR (OpenZCursors, after its own decision): where the game would show the move cursor and every selected unit that would
    ///    take the move is a shuttle, a place where none of them can ever land (ZWorld.ShuttleNeverLands: the landing spot rule with the
    ///    units left out, or the map's edge band) shows the game's NO cursor (the one of every order the selection cannot take), with its
    ///    sound (OzSoundV3.CursorChanged). A mixed group keeps the move cursor, as the game's cursor always shows what at least one of the
    ///    selected units can do (OpenZCursors.Decide: canMove / canAttack of any unit); the shuttle's own mark turns red meanwhile
    ///    (OzShuttleShadow, while the right button aims).
    ///  * THE CLICK (PlayerInput.Flush, before it sends the click's orders; the map, the minimap, touch and the gamepad pointer all end
    ///    there): ZWorld.ShuttleOrdersFilter takes such a shuttle out of the click: no order, no order mark. When nobody is left the refusal
    ///    blip plays (ui_order_invalid, as for any order no selected unit could take); otherwise the others get theirs (their own
    ///    acknowledgement only). The ship's mark turns red for 2 s (OzShuttleShadow reads ZObject.ShuttleNoLandOrders). A patrol order (Q,
    ///    then the far end) asks <see cref="PatrolRefused"/> per unit (a patrol gives the shuttle its first move).
    /// ANYWHERE: nothing here does anything (the cursor and the orders as they were).
    /// The test is cached per map tile and ship and asked again every second (it tries a 5-degree lattice of headings of the landing spot
    /// rule, one 15-degree lattice a frame near an obstacle), and a new tile is tested at most every 0.08 s (the cursor may lag the pointer
    /// that long; the click is always tested exactly, the whole rule at once).
    /// </summary>
    public static class OzShuttleOrderAim
    {
        /// <summary>Proof counters: shuttle orders taken out of clicks, clicks where nobody was left (the blip), frames the NO cursor was
        /// shown for a shuttle; the last reason and when (unscaled time).</summary>
        public static int Refused, RefusedClicks, NoCursorFrames;
        public static string LastWhy = "";
        public static float LastRefusedAt = -10f;
        /// <summary>The test's cache life (s), and the least time between two tests of different tiles (s: a pointer swept over the map asks
        /// at most about 12 tests a second; the click itself is always tested at once, ZWorld.ShuttleOrdersFilter). Review fix: the life 1 s
        /// (was 0.25 s): the answer no longer depends on the ship's heading or on units, only on the ground and what stands on it
        /// (ZWorld.ShuttleNeverLattices).</summary>
        public static float CacheSec = 1f, MinGapSec = 0.08f;

        // review fix: the answers per (tile, ship), not one entry for all ships (two shuttles selected over such a place asked the rule again
        // on every frame, for each of them). A place near an obstacle is worked out one lattice of headings a frame (ZWorld.ShuttleNeverLandsPart:
        // at most one part of the rule a frame, about what the old single test cost); until its answer is final the cursor keeps the last answer
        // it showed (a lag of a frame or two, never a flicker), and an answer being worked out again after CacheSec stays on meanwhile.
        sealed class Ask { public bool has, never; public string why = ""; public float at; public int next; }   // next: the part to ask next, -1 = none
        static readonly Dictionary<long, Ask> cache = new Dictionary<long, Ask>();
        static readonly Dictionary<int, (bool never, string why)> lastOf = new Dictionary<int, (bool, string)>();
        static readonly Dictionary<int, float> lastStart = new Dictionary<int, float>();
        static ZWorld cacheWorld; static int partFrame = -1;
        static readonly List<ZObject> refusedNow = new List<ZObject>();
        static readonly List<ZObject> ships = new List<ZObject>();

        /// <summary>The shuttle s can never land at the map point (px): the rules' ZWorld.ShuttleNeverLands, cached per tile and ship, worked
        /// out over a few frames near an obstacle (the click itself always asks the whole rule at once: ZWorld.ShuttleOrdersFilter).</summary>
        public static bool NeverAt(ZWorld w, ZObject s, int x, int y, out string why)
        {
            why = "";
            if (w == null || s == null) return false;
            float now = Time.unscaledTime; int frame = Time.frameCount;
            if (w != cacheWorld || cache.Count > 512) { cache.Clear(); lastOf.Clear(); lastStart.Clear(); cacheWorld = w; }
            long key = ((long)s.Id << 32) | ((long)((x >> 4) & 0xFFFF) << 16) | (long)((y >> 4) & 0xFFFF);
            if (!cache.TryGetValue(key, out var e)) { e = new Ask { next = 0 }; cache[key] = e; }
            if (e.has && e.next < 0 && now - e.at >= CacheSec) e.next = 0;                                     // old: asked again, its answer shown meanwhile
            bool gap = e.next == 0 && !e.has && lastStart.TryGetValue(s.Id, out float ls) && now - ls < MinGapSec;   // a new tile right after the last: a moment later
            if (e.next >= 0 && !gap && frame != partFrame)
            {
                partFrame = frame;
                if (e.next == 0) lastStart[s.Id] = now;
                int r = w.ShuttleNeverLandsPart(x, y, s, e.next, out string wy);
                if (r != 1) { e.has = true; e.never = r != 0; e.why = r != 0 ? wy : ""; e.at = now; e.next = -1; }
                else { e.next++; if (!e.has) e.why = wy; }
            }
            if (e.has) { lastOf[s.Id] = (e.never, e.why); why = e.why; return e.never; }
            if (lastOf.TryGetValue(s.Id, out var l)) { why = l.why; return l.never; }                               // not final yet: the last answer shown
            why = e.why; return e.next > 0;                                                                          // the very first: no lattice so far fits
        }

        /// <summary>OpenZCursors.LateUpdate: the cursor its Decide() chose, NO where only shuttles would take the move and none of them can
        /// ever land at the point under the pointer.</summary>
        public static string Cursor(string c, OpenZHud hud)
        {
            // review fix: GRAB too: over a flag Decide() shows GRAB for any selection that can move, but a shuttle never takes a flag and its
            // click there is refused (the flag lies in its footprint at every heading): with only shuttles selected that is the NO cursor too
            if ((c != "move" && c != "grab") || hud == null || !OzShuttleOrdersOption.LandableOnly) return c;
            if (OzShuttleQueueInput.CursorFree()) return c;                                                 // K410 (DV125): with Shift held the point may be a middle point (flown over): the move cursor
            var input = hud.input; var g = hud.game; var w = hud.W; var m = Mouse.current;
            if (input == null || g == null || w == null || m == null || hud.PointerOverUi || (hud.Rally && hud.Rally.Armed)) return c;
            ships.Clear();
            foreach (var u in input.Selection)
            {
                if (u == null || u.Destroyed || u.Owner != hud.Player || !u.CanMove) continue;
                if (!u.IsShuttle) return c;                                                                   // a mixed group: the others go (the move cursor)
                ships.Add(u);
            }
            if (ships.Count == 0) return c;
            var cam = input.cam ? input.cam.GetComponent<Camera>() : Camera.main;
            if (!cam || !g.GroundPoint(cam.ScreenPointToRay(m.position.ReadValue()), out var gp)) return c;
            var px = ZGame.WorldToPx(gp);
            foreach (var s in ships) if (!NeverAt(w, s, px.x, px.y, out _)) return c;                       // one of them can land there
            NoCursorFrames++;
            return "nono";
        }

        /// <summary>PlayerInput.Flush, first: the click's orders through ZWorld.ShuttleOrdersFilter. True = nobody is left (the refusal blip
        /// has played, the click is over: no order, no order mark).</summary>
        public static bool Filter(ZGame g, Dictionary<ZObject, List<ZWaypoint>> pending)
        {
            if (g == null || g.World == null || pending == null || pending.Count == 0 || !OzShuttleOrdersOption.LandableOnly) return false;
            refusedNow.Clear();
            int n = g.World.ShuttleOrdersFilter(g.player, pending, true, refusedNow);
            if (n == 0) return false;
            Refused += n; LastRefusedAt = Time.unscaledTime; LastWhy = ZWorld.ShuttleOrdersNoLandWhy;
            Debug.Log($"[OpenZ Orders] shuttle: no order to a place where it can never land ({LastWhy}): {n} ship(s) left out of the click{(pending.Count > 0 ? $", {pending.Count} other unit(s) keep theirs" : "")} (SHUTTLE ORDERS: LANDABLE SPOTS ONLY)");
            if (pending.Count > 0) return false;
            RefusedClicks++;
            AudioDirector.UI("ui_order_invalid");                                                                // every visible action has its sound: the refusal blip
            return true;
        }

        /// <summary>PlayerInput's patrol order (Q, then the right button at the far end), per unit before ZWorld.SendPatrol: true = a shuttle
        /// that could never land at the far end (LANDABLE SPOTS ONLY): it gets no order (a patrol sends the shuttle its first move, which
        /// would fly it there); counted like a click's refusal (the blip is OrderEach's, when no unit took the order).</summary>
        public static bool PatrolRefused(ZWorld w, ZObject u, int x, int y)
        {
            if (w == null || u == null || !u.IsShuttle || !OzShuttleOrdersOption.LandableOnly || !w.ShuttleNeverLands(x, y, u, out string why)) return false;
            u.ShuttleNoLandOrders++; ZWorld.ShuttleOrdersNoLand++; ZWorld.ShuttleOrdersNoLandWhy = why;
            Refused++; LastRefusedAt = Time.unscaledTime; LastWhy = why;
            Debug.Log($"[OpenZ Orders] shuttle: no patrol to a place where it can never land ({why}) (SHUTTLE ORDERS: LANDABLE SPOTS ONLY)");
            return true;
        }

        /// <summary>The right button aims a move for ship s at the world point p: true when it can never land there (LANDABLE SPOTS ONLY) or
        /// cannot land there now (ANYWHERE: the old cursor test with the units, OzPlayableShuttle.LandableAt).</summary>
        public static bool AimBlocked(ZWorld w, ZObject s, Vector3 p)
        {
            if (w == null || s == null) return false;
            if (OzShuttleOrdersOption.LandableOnly) { var px = ZGame.WorldToPx(p); return NeverAt(w, s, px.x, px.y, out _); }
            return !OzPlayableShuttle.LandableAt(w, s, p, out _);
        }
    }
}
