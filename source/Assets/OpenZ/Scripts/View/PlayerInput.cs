using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// Z's mouse and keyboard controls (zplayer.cpp / zplayer_events.cpp, research spec 1.2-1.9, pseudo-code 9.3):
    /// left press deselects and starts a box, drag = live box of own leaders/vehicles/cannons, click (moved &lt;= 2 px) picks
    /// ONE random own unit under the pointer (minion -> its leader); right-button RELEASE gives the order (per-unit mode from
    /// the hovered object, attack-move by default, Ctrl forces attack-move, Alt plain move, Shift queues and sends on
    /// release, Z orders only the nearest unit and drops it); Ctrl+0-9 store, 0-9 recall (again = fly there);
    /// Ctrl+R/V/C/A select all robots/vehicles/cannons/robots+vehicles; R/V/G cycle one unit and fly to it.
    /// Hit tests use each unit's Zod rectangle (16 px robots, 32 px vehicles/cannons) projected to the screen.
    /// </summary>
    public class PlayerInput : MonoBehaviour
    {
        public ZGame game;
        public RtsCamera cam;
        public readonly List<ZObject> Selection = new List<ZObject>();
        System.Predicate<ZObject> selPrune;
        readonly Dictionary<ZObject, List<ZWaypoint>> pending = new Dictionary<ZObject, List<ZWaypoint>>();
        /// <summary>Shift was held when the right button went down: that click belongs to the waypoint queue even when Shift is let
        /// go a moment before the button (orders are given when the button goes up).</summary>
        bool rightDownShift;
        /// <summary>Shift-queued orders not sent yet (drawn by SelectionOverlay as markers + lines).</summary>
        public IReadOnlyDictionary<ZObject, List<ZWaypoint>> Pending => pending;
        bool additive; float lastClickTime = -10f; string lastClickType;
        readonly Dictionary<int, List<ZObject>> groups = new Dictionary<int, List<ZObject>>();
        public ZObject Hover { get; private set; }

        Vector2 boxStart; bool lDown, dragging;
        float lastCycleTime = -100f; char lastCycleKind; int lastCycleId = -1;

        // order marker ("-ED" cursor echo, 3 s) and path display
        public Vector3 MarkerPos { get; private set; }
        public float MarkerTime { get; private set; } = -10f;
        public float SelectTime { get; private set; } = -10f;

        Camera Cam => cam ? cam.GetComponent<Camera>() : Camera.main;
        ZWorld W => game.World;

        void Awake()
        {
            realBounds.Clear();
            if (!game) game = GetComponent<ZGame>(); if (!game) game = FindFirstObjectByType<ZGame>();
            if (!cam) cam = FindFirstObjectByType<RtsCamera>();
        }

        void OnDestroy() { realBounds.Clear(); }

        void Update()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null || kb == null || game == null || game.World == null) return;
            for (int i = 0; i < Selection.Count; i++)                                                   // a selected squad leader died: keep the squad selected (its new leader)
            {
                var s = Selection[i]; int guard = 0;
                while (s != null && (s.Destroyed || s.KillAt >= 0) && s.Successor != null && guard++ < 8) s = s.Successor;   // KC4b: also a leader who left the map (boarded): the squad's next member stays selected
                if (s != null && s.Leader != null && !s.Leader.Destroyed && s.Leader.KillAt < 0 && s.Leader.Owner == s.Owner) s = s.Leader;   // bug 12 / 18: a picked member -> its (new) leader
                if (s != null && s != Selection[i] && !s.Destroyed && s.Owner == game.player && !Selection.Contains(s)) Selection[i] = s;
                else if (s != null && s != Selection[i] && Selection.Contains(s)) { Selection.RemoveAt(i); i--; }
            }
            Selection.RemoveAll(selPrune ??= o => o.Destroyed || o.Owner != game.player || (o.KillAt >= 0 && W.Get(o.Id) != o));   // KC4b: a robot removed from the map (boarded) leaves the selection once the squad's hand-over is followed
            Vector2 mp = mouse.position.ReadValue();
            Hover = HoverAt(mp);

            bool over = W.GameOver && ZWorld.EndFreezeDeviation;                                 // endgame_ui_v1: after the win no orders (selection + camera stay)
            if (over) { if (Placing != null) CancelPlace(); pending.Clear(); }
            else UpdatePlacement(mouse, kb, mp);
            if (Placing != null) return;                                                  // placement owns the mouse (Zod: next left release places)

            // ---- left button: deselect on press, live box, final selection on release
            if (mouse.leftButton.wasPressedThisFrame && InView(mp))
            {
                lDown = true; dragging = false; boxStart = mp;
                additive = kb.shiftKey.isPressed;                                          // Shift: add to / remove from the selection
                if (!additive) Selection.Clear();
            }
            if (lDown && mouse.leftButton.isPressed)
            {
                if (!dragging && (mp - boxStart).sqrMagnitude > BoxSqr) AudioDirector.UI("ui_box_select");
                if ((mp - boxStart).sqrMagnitude > BoxSqr) dragging = true;
                if (dragging) CollectBox(mp);
            }
            if (lDown && mouse.leftButton.wasReleasedThisFrame)
            {
                if (dragging) CollectBox(mp); else CollectClick(mp, kb.ctrlKey.isPressed);
                lDown = dragging = false;
                if (Selection.Count > 0) SelectTime = Time.unscaledTime;
            }

            // ---- right button release: order
            bool shift = kb.shiftKey.isPressed, ctrl = kb.ctrlKey.isPressed, alt = kb.altKey.isPressed;
            if (mouse.rightButton.wasPressedThisFrame) rightDownShift = shift;
            if (!over && !ctrl && !alt && Selection.Count > 0)
            {
                if (OzBindings.Pressed(OzAction.Patrol)) { PatrolArmed = true; AudioDirector.UI("ui_click"); Debug.Log("[OpenZ Orders] patrol armed: right-click the far end"); }
                if (OzBindings.Pressed(OzAction.StopUnits)) OrderEach((w, u) => w.SendStop(game.player, u), "stop");
                if (OzBindings.Pressed(OzAction.HoldPosition)) OrderEach((w, u) => w.SendHold(game.player, u), "hold position");
            }
            if (PatrolArmed && (over || Selection.Count == 0 || mouse.leftButton.wasPressedThisFrame)) { PatrolArmed = false; AudioDirector.UI("ui_cancel"); }
            if (PatrolArmed && !over && mouse.rightButton.wasReleasedThisFrame && InView(mp))
            {
                PatrolArmed = false;
                if (game.GroundPoint(Cam.ScreenPointToRay(mp), out var pg))
                {
                    var pt = ZGame.WorldToPx(pg);
                    MarkerPos = pg;
                    OrderEach((w, u) => !OzShuttleOrderAim.PatrolRefused(w, u, pt.x, pt.y) && w.SendPatrol(game.player, u, pt.x, pt.y), $"patrol to ({pt.x},{pt.y})");   // K256g SHUTTLE ORDERS: no patrol for a shuttle to a place where it can never land
                }
                else AudioDirector.UI("ui_order_invalid");
            }
            else if (!over && mouse.rightButton.wasReleasedThisFrame && InView(mp) && Selection.Count > 0)
            {
                // the last click of a queue: Shift let go just before the right button used to make it a plain order that replaced
                // the whole queue (the units went straight to the last point). It is the queue's last point: the queue is sent with it.
                bool queueClick = shift || (rightDownShift && pending.Count > 0);
                if (!queueClick && TryEject(mp)) { }                                                    // crews_v2: Zod EXIT (zplayer_events.cpp:397-424)
                else if (game.GroundPoint(Cam.ScreenPointToRay(mp), out var gp))
                {
                    var pt = ZGame.WorldToPx(gp);
                    OzShuttleQueueInput.Click(pending.Count == 0, queueClick);                                // K410 (DV125): a Shift click for a flying shuttle goes after its route (View/OzShuttleQueueInput.cs)
                    foreach (var u in Selection)
                    {
                        if (!pending.TryGetValue(u, out var l)) pending[u] = l = new List<ZWaypoint>();
                        l.Add(BuildWaypoint(u, Hover, pt.x, pt.y, ctrl, alt));
                    }
                    MarkerPos = gp;
                    OzShuttleKickflip.OrderClick(Selection, mp, gp, queueClick, OzBindings.Held(OzAction.NearestOnly));   // K344: a double right-click of a far move = the shuttle's kickflip (view only; the orders as before; View/OzShuttleKickflip.cs)
                    if (!shift) Flush(OzBindings.Held(OzAction.NearestOnly), mp);   // menu_exe_v1: bound key (Z)
                }
                else AudioDirector.UI("ui_order_invalid");
            }
            // the queue is sent when Shift is up; a right button that went down with Shift held is still part of it: wait for that click
            if (pending.Count > 0 && !kb.shiftKey.isPressed && !(rightDownShift && mouse.rightButton.isPressed)) Flush(OzBindings.Held(OzAction.NearestOnly), mp);
            if (!mouse.rightButton.isPressed) rightDownShift = false;

            // ---- groups, select-all, cycling
            for (int d = 0; d <= 9; d++)
            {
                var key = kb[d == 0 ? Key.Digit0 : (Key)((int)Key.Digit1 + d - 1)];            // Input System order: Digit1..Digit9, then Digit0
                if (!key.wasPressedThisFrame) continue;
                if (ctrl) groups[d] = new List<ZObject>(Selection);
                else if (groups.TryGetValue(d, out var g))
                {
                    g.RemoveAll(o => o.Destroyed || o.Owner != game.player);
                    if (g.Count > 0 && g.SequenceEqual(Selection)) FlyTo(Average(g));
                    else { Selection.Clear(); Selection.AddRange(g); SelectTime = Time.unscaledTime; SelectVoice(); }
                }
            }
            if (ctrl && !alt)
            {
                if (OzBindings.Pressed(OzAction.AllRobots)) SelectAll(o => o.Kind == ZKind.Robot);                    // menu_exe_v1: bound keys (Ctrl + R V C A)
                if (OzBindings.Pressed(OzAction.AllVehicles)) SelectAll(o => o.Kind == ZKind.Vehicle);
                if (OzBindings.Pressed(OzAction.AllCannons)) SelectAll(o => o.Kind == ZKind.Cannon);
                if (OzBindings.Pressed(OzAction.AllUnits)) SelectAll(o => o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle);
            }
            else if (!alt)
            {
                bool menuKey = OzRallyKey.Take(this), repairKey = !menuKey && OzBindings.Pressed(OzAction.RepairVehicles) && SendToRepair();
                if (OzBindings.Pressed(OzAction.NextRobot) && !((repairKey || menuKey) && OzBindings.KeyOf(OzAction.NextRobot) == OzBindings.KeyOf(OzAction.RepairVehicles))) Cycle('r', ZKind.Robot, mp);   // menu_exe_v1: bound keys (R V G)
                if (OzBindings.Pressed(OzAction.NextVehicle)) Cycle('v', ZKind.Vehicle, mp);
                if (OzBindings.Pressed(OzAction.NextGun)) Cycle('g', ZKind.Cannon, mp);
            }
        }

        /// <summary></summary>
        public bool PatrolArmed { get; private set; }
        /// <summary>Checks: DV68 orders sent from the keys.</summary>
        public static int PatrolOrders, StopOrders, HoldOrders;

        /// <summary>DV68: one command per selected unit through ZWorld (like SendWaypoints), Alan acknowledges, the order marker pops.</summary>
        void OrderEach(System.Func<ZWorld, ZObject, bool> send, string what)
        {
            int took = 0; bool robots = false;
            foreach (var u in Selection.ToArray())
                if (u != null && !u.Destroyed && send(game.World, u)) { took++; if (u.Kind == ZKind.Robot) robots = true; }
            if (what.StartsWith("patrol")) PatrolOrders += took; else if (what == "stop") StopOrders += took; else HoldOrders += took;
            Debug.Log($"[OpenZ Orders] {what}: {took} of {Selection.Count} units");
            if (took == 0) { AudioDirector.UI("ui_order_invalid"); return; }
            if (robots) CombatAudio.I?.Voice("order_ack", CombatAudio.AlanOrder, 0.9f, 0.8f, true);
            else AudioDirector.UI("ui_click");
            MarkerTime = Time.unscaledTime;
        }

        bool InView(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;

        /// <summary>The pointer travel (px, squared) past which a held left press is the selection box and no longer a click
        /// (2 px; Zod: more than 1 px, zplayer.cpp:2581).</summary>
        public const float BoxSqr = 4f;

        /// <summary>b30: a left press that went down on an own fort / factory is held back by the HUD, because it may be the click
        /// that opens the build menu. Once it has travelled it is a box-select after all, and the HUD hands it over here
        /// (OpenZHud.Update): the box runs from where the button went down, exactly as if this script had taken the press itself
        /// (the old selection goes unless Shift is held, the box sound, a patrol that waited for its far end is dropped).
        /// released = the button came up on this same frame: the box is final at once.</summary>
        public void BoxFromHeldPress(Vector2 start, Vector2 now, bool shift, bool released)
        {
            if (game == null || game.World == null) return;
            if (PatrolArmed) { PatrolArmed = false; AudioDirector.UI("ui_cancel"); }
            lDown = true; dragging = true; boxStart = start; additive = shift;
            AudioDirector.UI("ui_box_select");
            CollectBox(now);                                                                            // clears the old selection unless Shift is held
            if (!released) return;                                                                      // Update goes on with the box and ends it on the release
            lDown = dragging = false;
            if (Selection.Count > 0) SelectTime = Time.unscaledTime;
        }

        // ------------------------------------------------------------------ selection
        /// <summary>Screen rectangle of a unit's Zod footprint (ground rectangle extruded to the model height).</summary>
        public Rect ScreenRect(ZObject o)
        {
            var c = Cam;
            // but the model is drawn up to ~2 m off it (UnitView spacing offset + eased display, bridge decks, knock pushes), so a click on
            // the drawn robot / vehicle missed (squad members spread out, a lone robot and some vehicles unselectable, an enemy tank with the
            // GO cursor): units are picked where they are DRAWN; (3 + 22) vehicles / guns / buildings get a tight box from their real mesh
            if (o.IsUnit && game.Views.TryGetValue(o.Id, out var uv) && uv && uv.gameObject.activeInHierarchy)
            {
                if (o.Kind != ZKind.Robot && MeshRect(o, out var mr)) return MinSize(Grow(mr, 3f * UiK), 22f * UiK);
                float rad = o.Kind == ZKind.Robot ? 0.6f * OzHeroSquadView.Tall(o) : o.Kind == ZKind.Cannon ? 1.3f : 1.6f, hh = o.Kind == ZKind.Robot ? 1.85f * OzHeroSquadView.Tall(o) : 2.2f;   // KC4b: a hero's size
                return MinSize(BoxRect(c, uv.transform.position, rad, hh), (o.Kind == ZKind.Robot ? 16f : 22f) * UiK);
            }
            if (o.Kind == ZKind.Building && MeshRect(o, out var br)) return br;
            if (o.Type == ZWorld.DetailPropType && DetailPropHub.OfSim(o.Id) is DetailProp dp && dp && !dp.Broken)   // DV67: picked where the prop is drawn, its own height
                return MinSize(BoxRect(c, dp.Foot, Mathf.Max(0.5f, dp.hitRadius), Mathf.Max(1f, dp.hitHeight)), 18f * UiK);
            float h = o.Kind == ZKind.Robot ? 1.8f : o.Kind == ZKind.Building ? 6f : o.Kind == ZKind.MapItem ? (o.Type == "hut" || o.Type.StartsWith("map_object") ? 2.2f : 1.2f) : 2.2f;   // ZF4: base-prop huts / cacti / blocks stand ~2 m
            var a = ZGame.PxToWorld(o.PosX, o.PosY); var b = ZGame.PxToWorld(o.PosX + o.WidthPix, o.PosY + o.HeightPix);
            float gy = game.GroundY((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f);
            if (o.Kind == ZKind.Cannon && game.Views.TryGetValue(o.Id, out var tv) && tv && tv.OnTower)
            {   // crews_v2: a fort tower gun is drawn on its tower top (TowerMounts), ~9 m up and up to 3.7 m from its sim footprint:
                // select / target / EXIT-test it where it is drawn
                var d = tv.transform.position - (a + b) * 0.5f; d.y = 0f; a += d; b += d; gy = tv.transform.position.y;
            }
            if (o.IsFortDefender && game.Views.TryGetValue(o.Id, out var fdv) && fdv) gy = fdv.transform.position.y;   // fort_defender_v1 DV41: on the roof
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var w = new Vector3((i & 1) == 0 ? a.x : b.x, gy + ((i & 4) == 0 ? 0 : h), (i & 2) == 0 ? a.z : b.z);
                var s = c.WorldToScreenPoint(w);
                if (s.z < 0) continue;
                mn = Vector2.Min(mn, s); mx = Vector2.Max(mx, s);
            }
            return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
        }

        static float UiK => Mathf.Max(0.75f, Screen.height / 1080f);
        static Rect Grow(Rect r, float px) => Rect.MinMaxRect(r.xMin - px, r.yMin - px, r.xMax + px, r.yMax + px);
        static Rect MinSize(Rect r, float px)
        {
            float w = Mathf.Max(r.width, px), h = Mathf.Max(r.height, px);
            return new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h);
        }
        static Rect BoxRect(Camera c, Vector3 p, float rad, float h)
        {
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue); bool any = false;
            for (int i = 0; i < 8; i++)
            {
                var s = c.WorldToScreenPoint(p + new Vector3((i & 1) == 0 ? -rad : rad, (i & 4) == 0 ? 0f : h, (i & 2) == 0 ? -rad : rad));
                if (s.z < 0) continue; any = true; mn = Vector2.Min(mn, s); mx = Vector2.Max(mx, s);
            }
            return any ? Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y) : new Rect(-9999f, -9999f, 0f, 0f);
        }

        /// <summary>
        /// the object's REAL model: every solid renderer's own mesh box (MeshFilter / skinned local bounds, not the world AABB of a
        /// rotated part) in the view's local space, cached per id; glow / fx / flags / shadows / transparent parts left out.
        /// Buildings are inset 6 % a side so a click next to a factory or fort does not grab it. Also feeds OpenZUnitOverlay's brackets.</summary>
        readonly Dictionary<int, (Transform t, Bounds local)> meshBox = new Dictionary<int, (Transform, Bounds)>();
        static readonly List<Renderer> meshRs = new List<Renderer>();
        /// <summary></summary>
        public static int MeshBoxMeasures;
        /// <summary>
        /// own model and then only moves with it (the old 1.5 s re-measure made a still jeep's hover frame snap a second later: the
        /// skinned crew's world box and the turret angle changed between measures). Left out: crew figures (crew_* subtrees: drivers,
        /// gunners, commanders), the tower gunner perch, any tower / plinth / pedestal / mount / fort part, glow / fx / flags /
        /// shadows / transparent parts. A skinned body (the mech) counts with its own local bounds on its root bone.</summary>
        static bool SkipPart(Transform t, Transform root)
        {
            for (var a = t; a && a != root; a = a.parent)
            {
                var n = a.name;
                if (n.StartsWith("crew_") || n.StartsWith("OZ_GunnerPerch") || n.StartsWith("OZ_Crew") || n.Contains("Driver") || n.Contains("Gunner") || n.Contains("Commander")
                    || n.Contains("Tower") || n.Contains("Plinth") || n.Contains("Pedestal") || n.Contains("Mount") || n.Contains("Fort")) return true;
            }
            return false;
        }
        /// <summary>
        /// A vehicle's frame is its hull / body only: every gun barrel subtree (BarrelPitch / Barrel /
        /// Recoil / Muzzle / BarrelSpin: the jeep MG, tank and heavy tank barrels, the mech's spinning gun), antennas and pennants are left
        /// out; the jeep's whole MG post (TurretYaw) too, since on the jeep the turret is the gun (a tank keeps its turret body).</summary>
        static bool GunPart(Transform t, Transform root, bool jeep)
        {
            for (var a = t; a && a != root; a = a.parent)
            {
                var n = a.name;
                if (n.Contains("Barrel") || n.Contains("Recoil") || n.Contains("Muzzle") || n.Contains("Antenna") || n.Contains("Pennant") || n.Contains("_spin")
                    || jeep && (n.Contains("TurretYaw") || n.StartsWith("Gun"))) return true;
            }
            return false;
        }
        /// <summary>
        /// 8 corners of a building's box project well above its real outline from the RTS camera (the empty top corners of a 20 x 20 x 12 m
        /// box over walls and towers), so a building's frame comes from its real silhouette: per 24 x 24 ground cell of the model the highest
        /// and the lowest vertex (towers, gun, walls kept; ~1000 points at most), measured once, projected per frame (cached per frame).</summary>
        readonly Dictionary<int, Vector3[]> meshPts = new Dictionary<int, Vector3[]>();
        readonly Dictionary<int, (int frame, Rect r, bool ok)> ptsRect = new Dictionary<int, (int, Rect, bool)>();
        static readonly List<Vector3> ptsTmp = new List<Vector3>();
        static Vector3[] Silhouette(List<Vector3> pts, Bounds lb)
        {
            const int N = 24;
            var hi = new int[N * N]; var lo = new int[N * N];
            for (int i = 0; i < hi.Length; i++) { hi[i] = -1; lo[i] = -1; }
            float sx = N / Mathf.Max(0.01f, lb.size.x), sz = N / Mathf.Max(0.01f, lb.size.z);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                int cx = Mathf.Clamp((int)((p.x - lb.min.x) * sx), 0, N - 1), cz = Mathf.Clamp((int)((p.z - lb.min.z) * sz), 0, N - 1), c = cz * N + cx;
                if (hi[c] < 0 || p.y > pts[hi[c]].y) hi[c] = i;
                if (lo[c] < 0 || p.y < pts[lo[c]].y) lo[c] = i;
            }
            var outp = new List<Vector3>();
            for (int c = 0; c < hi.Length; c++) { if (hi[c] >= 0) outp.Add(pts[hi[c]]); if (lo[c] >= 0 && lo[c] != hi[c]) outp.Add(pts[lo[c]]); }
            return outp.ToArray();
        }
        /// <summary>Play check: the number of silhouette points of a building's frame (0 = box frame).</summary>
        public int ProofSilhouettePoints(int id) => meshPts.TryGetValue(id, out var p) && p != null ? p.Length : 0;

        /// <summary>(e): the split howitzer parts (howitzer_turret_split / _v2_parts / _pitch_v2 / _barrel_v2) carry the WHOLE model's
        /// stale mesh bounds (4 m tall each, the barrel 7 m), so the howitzer's frame was twice the gun. A readable mesh's box comes
        /// from its real vertices (once per mesh asset, cached); others keep mesh.bounds.</summary>
        static readonly Dictionary<Mesh, Bounds> realBounds = new Dictionary<Mesh, Bounds>();
        static Bounds RealBounds(Mesh m)
        {
            if (realBounds.TryGetValue(m, out var b)) return b;
            b = m.bounds;
            if (m.isReadable && m.vertexCount > 0)
            {
                var vs = m.vertices; b = new Bounds(vs[0], Vector3.zero);
                for (int i = 1; i < vs.Length; i++) b.Encapsulate(vs[i]);
            }
            realBounds[m] = b; return b;
        }
        public bool MeshRect(ZObject o, out Rect r)
        {
            r = default;
            if (o == null) return false;
            if (!meshBox.TryGetValue(o.Id, out var mb) || !mb.t)
            {
                GameObject go = null;
                if (game.Views.TryGetValue(o.Id, out var v) && v) go = v.gameObject; else if (game.StaticViews.TryGetValue(o.Id, out var sgo)) go = sgo;
                if (!go) return false;
                var t = go.transform; bool any = false; var lb = new Bounds(); var w2l = t.worldToLocalMatrix;
                bool unit = o.Kind != ZKind.Building, jeep = o.Type == "jeep";
                ptsTmp.Clear();
                go.GetComponentsInChildren(meshRs);
                foreach (var rr in meshRs)
                {
                    if (!rr.enabled || rr is ParticleSystemRenderer || rr is TrailRenderer || rr is LineRenderer || rr is BillboardRenderer) continue;
                    var n = rr.gameObject.name;
                    if (n.Contains("Glow") || n.Contains("Beam") || n.Contains("Flag") || n.Contains("Shadow") || n.Contains("Ring") || n.Contains("Decal")
                        || n.Contains("Dust") || n.Contains("Flash") || n.Contains("Fx") || n.Contains("Halo") || n.Contains("Light")) continue;
                    var sm = rr.sharedMaterial; if (sm && sm.renderQueue >= 2900) continue;                // transparent / additive fx
                    if (unit && SkipPart(rr.transform, t)) continue;                                           // (e): the unit's own model only
                    if (o.IsShuttle && !rr.gameObject.name.StartsWith("SH_")) continue;                         // DV118: the shuttle's frame: its hull, pods, legs, nozzles and lamps (no ramp, bay or effects)
                    if (o.Kind == ZKind.Vehicle && GunPart(rr.transform, t, jeep)) continue;
                    Bounds mbnd; Matrix4x4 m2w;
                    if (rr is SkinnedMeshRenderer smr)
                    {
                        if (unit && o.Kind != ZKind.Vehicle) continue;                                        // a skinned part on a gun = a crew figure
                        mbnd = smr.localBounds; m2w = (smr.rootBone ? smr.rootBone : smr.transform).localToWorldMatrix;
                    }
                    else
                    {
                        var mf = rr.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; mbnd = RealBounds(mf.sharedMesh); m2w = rr.transform.localToWorldMatrix;
                        if ((!unit || o.IsShuttle) && mf.sharedMesh.isReadable)
                        {   // (7): the building's real vertices (strided to ~20k per mesh) for its silhouette
                            var vs = mf.sharedMesh.vertices; var l2 = w2l * m2w; int step = Mathf.Max(1, vs.Length / 20000);
                            for (int i = 0; i < vs.Length; i += step) ptsTmp.Add(l2.MultiplyPoint3x4(vs[i]));
                        }
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        var c = w2l.MultiplyPoint3x4(m2w.MultiplyPoint3x4(mbnd.center + Vector3.Scale(mbnd.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                        if (!any) { lb = new Bounds(c, Vector3.zero); any = true; } else lb.Encapsulate(c);
                    }
                }
                if (!any) return false;
                meshBox[o.Id] = mb = (t, lb); MeshBoxMeasures++;
                meshPts[o.Id] = (!unit || o.IsShuttle) && ptsTmp.Count > 8 ? Silhouette(ptsTmp, lb) : null;   // DV118: the shuttle's frame is its silhouette too
                ptsTmp.Clear();
            }
            if ((o.Kind == ZKind.Building || o.IsShuttle) && meshPts.TryGetValue(o.Id, out var sil) && sil != null)
            {   // (7): the projected silhouette, once per frame per building
                int f = Time.frameCount;
                if (ptsRect.TryGetValue(o.Id, out var pr) && pr.frame == f) { r = pr.r; return pr.ok; }
                var cm = Cam; bool ok = false;
                if (cm)
                {
                    var l2w = OzPlayableShuttle.FrameL2W(o, mb.t); Vector2 smn = new Vector2(float.MaxValue, float.MaxValue), smx = new Vector2(float.MinValue, float.MinValue);
                    for (int i = 0; i < sil.Length; i++)
                    {
                        var sp = cm.WorldToScreenPoint(l2w.MultiplyPoint3x4(sil[i])); if (sp.z < 0) continue;
                        ok = true; smn = Vector2.Min(smn, sp); smx = Vector2.Max(smx, sp);
                    }
                    if (ok)
                    {
                        r = Rect.MinMaxRect(smn.x, smn.y, smx.x, smx.y);
                        float ix = r.width * 0.02f, iy = r.height * 0.02f; r = Rect.MinMaxRect(r.xMin + ix, r.yMin + iy, r.xMax - ix, r.yMax - iy);   // the old 6 % inset was for the box overshoot
                    }
                }
                ptsRect[o.Id] = (f, r, ok);
                return ok;
            }
            var cam = Cam; if (!cam) return false;
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = new Vector2(float.MinValue, float.MinValue); bool vis = false;
            for (int i = 0; i < 8; i++)
            {
                var lc = mb.local.center + Vector3.Scale(mb.local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var sp = cam.WorldToScreenPoint(mb.t.TransformPoint(lc)); if (sp.z < 0) continue;
                vis = true; mn = Vector2.Min(mn, sp); mx = Vector2.Max(mx, sp);
            }
            if (!vis) return false;
            r = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            if (o.Kind == ZKind.Building) { float ix = r.width * 0.06f, iy = r.height * 0.06f; r = Rect.MinMaxRect(r.xMin + ix, r.yMin + iy, r.xMax - ix, r.yMax - iy); }
            return true;
        }

        /// <summary>
        /// member whose leader died this tick picks itself (the sim hands the squad over on delete, Update re-points it).</summary>
        static ZObject PickHead(ZObject o)
        {
            if (o == null || o.IsFortDefender) return null;
            var l = o.Leader;
            if (l == null) return o.Selectable ? o : null;
            if (l.Destroyed || l.KillAt >= 0 || l.Owner != o.Owner) return o;
            return l.Selectable ? l : null;
        }

        static bool OwnPickable(ZObject o, Team me) => o.Owner == me && !o.Destroyed && o.KillAt < 0 && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon);

        void CollectBox(Vector2 mp)
        {
            var r = Rect.MinMaxRect(Mathf.Min(boxStart.x, mp.x), Mathf.Min(boxStart.y, mp.y), Mathf.Max(boxStart.x, mp.x), Mathf.Max(boxStart.y, mp.y));
            if (!additive) Selection.Clear();
            foreach (var o in W.Objects)
            {
                if (!OwnPickable(o, game.player)) continue;
                var head = PickHead(o); if (head == null || Selection.Contains(head)) continue;
                if (r.Overlaps(ScreenRect(o))) Selection.Add(head);
            }
        }

        void CollectClick(Vector2 mp, bool ctrl = false)
        {
            if (!additive) Selection.Clear();
            var cands = W.Objects.Where(o => OwnPickable(o, game.player) && ScreenRect(o).Contains(mp))
                                 .Select(PickHead).Where(o => o != null).Distinct().ToList();
            if (cands.Count == 0) { lastClickType = null; return; }
            var pick = cands[UnityEngine.Random.Range(0, cands.Count)];                  // Zod picks one at random (zplayer.cpp:2583-2614); view RNG: never the sim's seeded stream (unit_voices_v1 review)
            // double-click: every own unit of that type on screen (standard RTS)
            string pickKind = OzHeroSquadView.FaceKey(pick);                                              // KC4b: a hero squad is its own kind here (a grunt's double-click leaves the heroes out, a hero's takes the heroes)
            if (!additive && lastClickType == pickKind && Time.unscaledTime - lastClickTime < 0.35f)
            {
                var screen = new Rect(0, 0, Screen.width, Screen.height);
                Selection.Clear();
                Selection.AddRange(W.OwnedSelectable(game.player).Where(o => OzHeroSquadView.FaceKey(o) == pickKind && screen.Overlaps(ScreenRect(o))));
                lastClickType = null; SelectAllAt = Time.unscaledTime; SelectVoice(); return;              // sound_v3
            }
            lastClickType = pickKind; lastClickTime = Time.unscaledTime;
            if (additive && Selection.Contains(pick)) { Selection.Remove(pick); RemovedAt = Time.unscaledTime; }   // sound_v3           // Shift+click toggles
            else if (!Selection.Contains(pick)) Selection.Add(pick);
        }

        void SelectAll(System.Func<ZObject, bool> f)
        {
            Selection.Clear();
            Selection.AddRange(W.OwnedSelectable(game.player).Where(f));
            SelectTime = Time.unscaledTime;
            SelectAllAt = SelectTime; if (Selection.Count == 0) AudioDirector.UI("ui_select_empty");   // sound_v3
        }

        /// <summary>R/V/G (zplayer.cpp:1634-1691): first press or &gt; 7 s: the unit nearest to the mouse, else the next id.</summary>
        /// <summary>
        /// Every selected own damaged vehicle that is not already on a repair trip gets UNIT_REPAIR to the
        /// nearest own repair station through SEND_WAYPOINTS (the sim's DV62 brings it back to where it stood). false = no damaged vehicle
        /// selected (the key falls through to NEXT ROBOT when they share it).</summary>
        public bool SendToRepair()
        {
            if (W == null || W.GameOver) return false;
            var vs = Selection.Where(o => o != null && o.Owner == game.player && o.Kind == ZKind.Vehicle && !o.Destroyed && !o.IsMinion && o.CanBeRepaired()
                                          && !(o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.UnitRepair) && !(o.IsShuttle && o.ShuttleRepairTarget >= 0)).ToList();   // item 271: the shuttle on its repair trip
            if (vs.Count == 0)
            {
                if (!Selection.Any(o => o != null && o.Kind == ZKind.Vehicle && o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.UnitRepair)) return false;
                AudioDirector.UI("ui_order_repair"); return true;                                          // already on their way / on the pad
            }
            var stations = W.Objects.Where(b => b.Kind == ZKind.Building && b.CanRepairUnit(game.player)).ToList();
            if (stations.Count == 0)
            {
                AudioDirector.UI("ui_order_invalid");
                OpenZHud.I?.Toast("NO REPAIR STATION", HudStyle.Danger);
                return true;
            }
            int sent = 0;
            foreach (var v in vs)
            {
                var st = stations.OrderBy(b => (b.CenterX - v.CenterX) * (double)(b.CenterX - v.CenterX) + (b.CenterY - v.CenterY) * (double)(b.CenterY - v.CenterY)).ThenBy(b => b.Id).First();
                var wp = BuildWaypoint(v, st, st.CenterX, st.CenterY, false, false);
                if (wp.mode != WpMode.UnitRepair) continue;
                if (W.SendWaypoints(game.player, v, new[] { wp }) && ((v.Waypoints.Count > 0 && v.Waypoints[0].mode == WpMode.UnitRepair) || (v.IsShuttle && v.ShuttleRepairTarget == st.Id))) sent++;   // item 271
            }
            if (sent == 0) { AudioDirector.UI("ui_order_invalid"); return true; }
            MarkerTime = Time.unscaledTime;
            OpenZHud.I?.Toast(sent == 1 ? "VEHICLE TO REPAIR" : sent + " VEHICLES TO REPAIR", HudStyle.Muted);
            Debug.Log($"[OpenZ] R: {sent} damaged vehicle(s) sent to repair and back");
            return true;
        }

        void Cycle(char kind, ZKind k, Vector2 mp)
        {
            var list = W.OwnedSelectable(game.player).Where(o => o.Kind == k).OrderBy(o => o.Id).ToList();
            if (list.Count == 0) { AudioDirector.UI("ui_select_empty"); return; }                      // sound_v3
            ZObject pick;
            if (kind != lastCycleKind || Time.unscaledTime - lastCycleTime > 7f || lastCycleId < 0)
            {
                game.GroundPoint(Cam.ScreenPointToRay(mp), out var gp); var p = ZGame.WorldToPx(gp);
                pick = list.OrderBy(o => (o.CenterX - p.x) * (o.CenterX - p.x) + (o.CenterY - p.y) * (o.CenterY - p.y)).First();
            }
            else pick = list.FirstOrDefault(o => o.Id > lastCycleId) ?? list[0];
            lastCycleKind = kind; lastCycleTime = Time.unscaledTime; lastCycleId = pick.Id;
            Selection.Clear(); Selection.Add(pick); SelectTime = Time.unscaledTime; SelectVoice();
            FlyTo(game.CenterWorld(pick));
        }

        Vector3 Average(List<ZObject> g) { var s = Vector3.zero; foreach (var o in g) s += game.CenterWorld(o); return s / g.Count; }
        void FlyTo(Vector3 p) { if (cam) cam.FlyTo(p); }

        // ------------------------------------------------------------------ hover + orders
        /// <summary>Last object in list order under the pointer that an order can target (zplayer_events.cpp:111-142).</summary>
        ZObject HoverAt(Vector2 mp)
        {
            ZObject h = null, hu = null;
            int treeX = -100000, treeY = -100000;                                                          // DV88: the tile under the pointer (levels with trees that can be felled only)
            if (W.TreeTiles.Count > 0 && game.GroundPoint(Cam.ScreenPointToRay(mp), out var treeAt)) { var tp = ZGame.WorldToPx(treeAt); treeX = tp.x >> 4; treeY = tp.y >> 4; }
            var kbd = Keyboard.current; bool forceBridge = kbd != null && kbd.ctrlKey.isPressed;
            // skipped unless Ctrl was held, so a destroyed bridge never became the crane's target; a broken bridge is a target while
            // a crane of ours is selected (repair cursor + CraneRepair order)
            bool craneSel = false; foreach (var s in Selection) if (s != null && s.Type == "crane" && s.Owner == game.player && !s.Destroyed) { craneSel = true; break; }
            // vehicles were never hover targets, so an own APC with room gave neither the ENTER cursor nor the Enter order (DV15)
            bool robotSel = false; foreach (var s in Selection) if (s != null && s.Kind == ZKind.Robot && s.Owner == game.player && !s.Destroyed) { robotSel = true; break; }
            foreach (var o in W.Objects)
            {
                if ((o.Destroyed && o.Kind != ZKind.Building) || (o.IsMinion && o.Owner == game.player)) continue;   // DV16: an ENEMY squad member is a target (Zod hovers minions too); only our own minions are skipped
                if (o.BlockTiles != null && W.IsTreeTile(o) && (Mathf.Abs((o.LocX >> 4) - treeX) > 8 || Mathf.Abs((o.LocY >> 4) - treeY) > 8)) continue;   // DV88: a tree more than 8 tiles from the pointer cannot be under it (its pick box is at most 4.5 m high): no pick box is worked out for it
                // target only when explicitly ordered (Ctrl held = attack)
                if (o.IsBridge && !forceBridge && !(craneSel && o.Destroyed)) continue;
                bool interesting = o.Type == "flag" || o.Type == "grenades" || o.Kind == ZKind.Building || o.Type == "rock" || o.Type == ZWorld.WreckType /* l17_details_v1 DV39 */ ||
                                   o.Type == ZWorld.DetailPropType ||
                                   o.Type == "hut" || o.Type.StartsWith("map_object") || /* ZF4 / BP3: huts (base props), cacti, boulders, blocks: Zod hovers every object (zplayer_events.cpp:129-141) */
                                   ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && o.Owner != game.player) ||
                                   (robotSel && o.Owner == game.player && o.CanTakePassengers(game.player));   // (11): an own APC with room
                if (interesting && ScreenRect(o).Contains(mp)) { if (o.IsUnit && !o.Destroyed) hu = o; else h = o; }
            }
            return hu ?? h;
        }

        /// <summary>Hostile unit within attack radius + agro distance (zplayer.cpp:2726-2744).</summary>
        bool NearHostiles(ZObject u)
        {
            if (u.U == null) return false;
            double r = u.U.attack_radius + W.S.agro_distance;
            foreach (var o in W.Objects)
                if ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && o.Owner != Team.Neutral && o.Owner != u.Owner && !o.Destroyed
                    && (o.CenterX - u.CenterX) * (double)(o.CenterX - u.CenterX) + (o.CenterY - u.CenterY) * (double)(o.CenterY - u.CenterY) <= r * r)
                    return true;
            return false;
        }

        /// <summary>The per-unit order table of spec 1.4 (zplayer.cpp:2746-2881).</summary>
        /// <summary>Checks (OrderCheck): the exact order a right-click on `h` at (x, y) gives `u`, no modifier keys.</summary>
        public ZWaypoint OrderFor(ZObject u, ZObject h, int x, int y) => BuildWaypoint(u, h, x, y, false, false);

        ZWaypoint BuildWaypoint(ZObject u, ZObject h, int x, int y, bool ctrl, bool alt)
        {
            var wp = ZWaypoint.Move(x, y, !NearHostiles(u));
            if (ctrl) { wp.attackTo = true; wp.fightOnWay = true; }                     // b49: Ctrl = the unit may leave its way to fight what it meets (Sim/ZObject.Calm.cs)
            if (ctrl) wp.forceFire = true;                                              // DV113: Ctrl = force fire: a Pyro flames the building or bridge it is sent at (Sim/ZObject.PyroForceBurn.cs)
            if (alt) wp.attackTo = false;
            if (h == null) return wp;
            wp.refId = h.Id;
            if (h.Type == "flag") return wp;                                                // move onto the flag = capture
            bool notOurs = h.Owner != game.player;
            // zplayer.cpp:2746-2881 special rows
            // orders never divert to fight on the way (Zod's attackTo diversion hijacked them, zobject.cpp:2561/2645)
            if (u.CanBeRepaired() && h.CanRepairUnit(u.Owner)) { wp.mode = WpMode.UnitRepair; wp.attackTo = false; wp.x = h.CenterX - 8; wp.y = h.CenterY; return wp; }
            if (u.Type == "crane" && h.CanBeRepairedByCrane(u.Owner) && h.GetCraneCenter(out int kx, out int ky)) { wp.mode = WpMode.CraneRepair; wp.attackTo = false; wp.x = kx; wp.y = ky; return wp; }
            if ((u.Kind == ZKind.Robot || u.Kind == ZKind.Vehicle) && h.CanEnterFort(u.Owner) && OnFortDoor(h, x, y)) { wp.mode = WpMode.EnterFort; wp.attackTo = false; return wp; }
            if (h.Destroyed) { wp.refId = -1; return wp; }                                     // rubble: just move there
            // captures it, as in Zod); only Ctrl (force attack) attacks it
            if (h.Kind == ZKind.Building && h.Owner == Team.Neutral && !ctrl) { wp.refId = -1; return wp; }
            if (ZWorld.EmptyClickWaypoint(u, h, ctrl, ref wp)) return wp;                     // DV81: an empty vehicle or gun is attacked only with Ctrl held; without it robots enter (below), vehicles move next to it, guns get no order (Sim/ZWorld.EmptyTargets.cs)
            switch (u.Kind)
            {
                case ZKind.Cannon: if (notOurs) wp.mode = WpMode.Attack; return wp;
                case ZKind.Vehicle: if (notOurs) wp.mode = WpMode.Attack; return wp;
                case ZKind.Robot:
                    // enter / pickup aim at the object's centre, not the clicked ground point: in 3D the click on a model's top lands
                    // outside its footprint and the order then ended silently next to it (Zod's 2D click is always inside the sprite)
                    if (h.Type == "grenades" && u.Type != "tough" && u.CanPickupGrenades()) { wp.mode = WpMode.PickupGrenades; wp.attackTo = false; wp.x = h.CenterX; wp.y = h.CenterY; return wp; }
                    if ((h.Owner == Team.Neutral && (h.Kind == ZKind.Vehicle || h.Kind == ZKind.Cannon)) || h.CanTakePassengers(u)) { wp.mode = WpMode.Enter; wp.attackTo = false; wp.x = h.CenterX; wp.y = h.CenterY; return wp; }
                    if (notOurs) wp.mode = WpMode.Attack;
                    return wp;
            }
            return wp;
        }

        /// <summary>
        /// (OpenZCursors) was shown, but the right-click on the unit only ever sent a move waypoint. Zod's runclick_event
        /// (zplayer_events.cpp:397-424): without Shift, ONE selected unit that CanEjectDrivers (APC, ejectable cannon; fort tower
        /// cannons are not, bfort.cpp:534-548 / zcannon.cpp:51) under the cursor -> EJECT_VEHICLE (the dev waypoints are dropped).</summary>
        bool TryEject(Vector2 mp)
        {
            if (Selection.Count != 1) return false;
            var u = Selection[0];
            if (!OzShuttleAirOffers.Exit(u) || !ScreenRect(u).Contains(mp)) return false;           // apc_v1 DV15: an APC without passengers has nothing to EXIT; K430b: a shuttle up in the air neither: the click goes on as a move order (UI/OzShuttleAirOffers.cs)
            EjectUnit(u);
            return true;
        }

        /// <summary>The EXIT command (zserver_events.cpp:631-731 via ZWorld.Eject): the crew pops out as a squad of the player, the
        /// vehicle / gun turns neutral; BoardingView shows the gunner / crew getting out. Returns false when refused.</summary>
        /// <summary>sound_v3: unscaled time of the last EXIT order / Shift+click removal / select-all (AudioDirector picks its cue).</summary>
        public static float EjectedAt = -10f, RemovedAt = -10f, SelectAllAt = -10f;

        public bool EjectUnit(ZObject u)
        {
            if (u == null) return false;
            pending.Remove(u);                                                                            // ClearDevWayPointsOfSelected
            bool ok = OzShuttleAirOffers.ExitOrder(u) && W.Eject(game.player, u);                     // K430b: no EXIT order up in the air (refused with the blip below; UI/OzShuttleAirOffers.cs)
            if (!ok) AudioDirector.UI("ui_order_invalid");
            else { MarkerTime = Time.unscaledTime; EjectedAt = MarkerTime; }                             // sound_v3
            return ok;
        }

        /// <summary>Fort "enter" click area (bfort.cpp:517-532): front local x 64..96, y 32..128; back x 64..96, y 16..80.</summary>
        public static bool OnFortDoor(ZObject f, int px, int py)
        {
            int lx = px - f.LocX, ly = py - f.LocY;
            if (lx < 64 || lx > 96) return false;
            return f.Type == "fort_front" ? ly >= 32 && ly <= 128 : ly >= 16 && ly <= 80;
        }

        // ------------------------------------------------------------------ cannon placement (zplayer.cpp:3480-3566)
        /// <summary>Building whose first stored cannon is being placed (null = not placing). Until the HUD's production window
        /// exists (U6) placement starts with the bound key (default C): the own building under the pointer, else the first own building with stored cannons.</summary>
        public ZObject Placing { get; private set; }
        public Vector2Int PlaceTile { get; private set; }
        public bool PlaceValid { get; private set; }
        GameObject ghost; string ghostType;
        Vector3 ghostScale0 = Vector3.one; bool ghostOnTower;                                               // combat_nb_v1 NB28
        /// <summary>combat_nb_v1 NB28: the placement cursor is on a fort tower pad (the ghost sits on the pad, PlaceTile = the Zod turret spot).</summary>
        public bool PlaceOnTower => ghostOnTower;
        /// <summary>k252: which of Placing's stored guns is placed: 0 = the first (PLACE, the key, as before); a ready-gun tile by the
        /// building picks its own gun (UI/OzReadyGuns.cs). The Sim's PlaceCannon takes the index; its rules do not depend on it.</summary>
        public int PlaceIndex { get; private set; }
        /// <summary>k252: the same placement as PLACE for one stored gun of an own building (a ready-gun tile).</summary>
        public void BeginPlaceCannon(ZObject b, int index) { BeginPlaceCannon(b); if (Placing == b) PlaceIndex = Mathf.Clamp(index, 0, b.StoredCannons.Count - 1); }

        public void BeginPlaceCannon(ZObject b) { if (b != null && b.Owner == game.player && b.StoredCannons.Count > 0) { Placing = b; PlaceIndex = 0; } }
        public void CancelPlace() { Placing = null; if (ghost) ghost.SetActive(false); PlacementFootprint.Hide(); }

        void UpdatePlacement(Mouse mouse, Keyboard kb, Vector2 mp)
        {
            if (OzBindings.Pressed(OzAction.PlaceCannon) && !kb.ctrlKey.isPressed && Placing == null && !(OzCheatSpawner.OwnsP && OzBindings.KeyOf(OzAction.PlaceCannon) == Key.P))   /* bound key (C), not with Ctrl: Ctrl+C is ALL CANNONS */                  // dev flag on: P = cheat overlay, placement via PLACE
            {
                var h = Hover != null && Hover.Owner == game.player && Hover.StoredCannons.Count > 0 ? Hover : null;
                if (h == null) foreach (var o in W.Objects) if (o.Kind == ZKind.Building && o.Owner == game.player && o.StoredCannons.Count > 0) { h = o; break; }
                BeginPlaceCannon(h);
            }
            if (Placing == null) return;
            if (Placing.Owner != game.player || Placing.StoredCannons.Count == 0 || kb.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) { if (kb.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) AudioDirector.UI("ui_cancel"); CancelPlace(); return; }
            if (PlaceIndex >= Placing.StoredCannons.Count) PlaceIndex = Placing.StoredCannons.Count - 1;   // k252 (UI/OpenZProductionWindow.Buttons.cs, UI/OzReadyGuns.cs): the gun being placed
            if (!game.GroundPoint(Cam.ScreenPointToRay(mp), out var gp)) return;
            var px = ZGame.WorldToPx(gp);
            // combat_nb_v1 NB28: the 2x2 spot whose centre is nearest the pointer (was: the tile under it = the ghost's top-left, the
            // ghost's centre up to 16 px off the pointer); on a fort tower pad the pad's Zod turret spot (the pad is 9 m up and 3.7 m
            // off the spot: the ground point under the pointer lay inside the fort)
            PlaceTile = new Vector2Int(Mathf.RoundToInt(px.x / 16f - 1f), Mathf.RoundToInt(px.y / 16f - 1f));
            ghostOnTower = TowerMounts.TurretSpotUnderRay(game, Cam.ScreenPointToRay(mp), Placing.StoredCannons[PlaceIndex], out var towerTile, out var towerSeat, out float towerYaw);
            if (ghostOnTower) PlaceTile = towerTile;
            PlaceValid = W.CannonPlacable(Placing, PlaceTile.x, PlaceTile.y);
            var type = Placing.StoredCannons[PlaceIndex];   // k252 (UI/OpenZProductionWindow.Buttons.cs, UI/OzReadyGuns.cs): the gun being placed (0 = the first, as before)
            if (!ghost || ghostType != type)
            {
                if (ghost) Destroy(ghost);
                var pf = game.Prefab("Z_" + type); ghostType = type;
                ghost = pf ? Instantiate(pf) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                ghost.name = "CannonGhost"; ghostScale0 = ghost.transform.localScale;                     // combat_nb_v1 NB28
                foreach (var c in ghost.GetComponentsInChildren<Collider>()) Destroy(c);
            }
            ghost.SetActive(true);
            var wpos = ZGame.PxToWorld(PlaceTile.x * 16 + 16, PlaceTile.y * 16 + 16); wpos.y = game.GroundY(wpos.x, wpos.z);
            ghost.transform.position = wpos;
            if (ghostOnTower)
            {   // combat_nb_v1 NB28: seated like UnitView seats a tower gun (base plate centred on the mount, x CannonScale, outward)
                var rot = Quaternion.Euler(0f, towerYaw, 0f);
                ghost.transform.localScale = ghostScale0 * TowerMounts.CannonScaleFor(type);
                ghost.transform.SetPositionAndRotation(towerSeat, rot);
                var axis = TowerMounts.FootprintCentreLocal(ghost.transform);
                ghost.transform.position = towerSeat - rot * (axis * Mathf.Abs(ghost.transform.lossyScale.x));
            }
            else { ghost.transform.localScale = ghostScale0; ghost.transform.rotation = Quaternion.identity; }
            var tp = ghost.GetComponent<TeamPaint>(); if (tp) { tp.Team = PlaceValid ? game.player : Team.Neutral; tp.Charred = PlaceValid ? 0f : 0.6f; }
            if (ghostOnTower) PlacementFootprint.Show(game, towerSeat, 4f * TowerMounts.CannonScale, PlaceValid, true, towerYaw);
            else PlacementFootprint.Show(game, wpos, 4f, PlaceValid, false, 0f);
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                var c = W.PlaceCannon(game.player, Placing, PlaceTile.x, PlaceTile.y, PlaceIndex);   // k252 (UI/OpenZProductionWindow.Buttons.cs, UI/OzReadyGuns.cs): that gun (the Sim's own index)
                if (c != null) CancelPlace();
                else AudioDirector.UI("ui_cannon_place_invalid");
                if (c == null) OzGunZone.Refused(this);                                                  // the reason in words, as a HUD notice (OzGunZone)
            }
        }

        void SelectVoice() { if (W.GameOver) return; if (Selection.Any(o => o.Kind == ZKind.Robot)) CombatAudio.I?.Voice("select", CombatAudio.AlanSelect, 0.85f, 0.8f, true); }

        /// <summary>zplayer.cpp:2942-3016: send every pending list (Z held: only the unit nearest to its first waypoint).</summary>
        void Flush(bool zHeld, Vector2 mp)
        {
            OzShuttleQueueInput.Prefix(game.World, pending);                                         // K410 (DV125): after a Shift queue a flying shuttle's route still to fly goes in front (View/OzShuttleQueueInput.cs)
            if (OzShuttleOrderAim.Filter(game, pending)) { pending.Clear(); return; }               // K256g SHUTTLE ORDERS: no order for a shuttle to a place where it can never land (View/OzShuttleOrderAim.cs); nobody left = the blip, no order mark
            ZObject only = null;
            if (zHeld && Selection.Count > 0)
                only = Selection.Where(pending.ContainsKey).OrderBy(u =>
                {
                    var w0 = pending[u][0]; return (u.LocX - w0.x) * (double)(u.LocX - w0.x) + (u.LocY - w0.y) * (double)(u.LocY - w0.y);
                }).FirstOrDefault();
            bool robots = false; int took = 0;
            foreach (var kv in pending)
                if (only == null || kv.Key == only)
                {
                    int kept = kv.Key.CanOverwriteWP() ? 0 : 1;
                    if (W.SendWaypoints(game.player, kv.Key, kv.Value)) { if (kv.Key.Kind == ZKind.Robot) robots = true; if (kv.Key.Waypoints.Count > kept) took++; }
                }
            if (took == 0 && pending.Count > 0) AudioDirector.UI("ui_order_invalid");                   // every waypoint refused (cannot attack / enter that)
            if (robots) CombatAudio.I?.Voice("order_ack", CombatAudio.AlanOrder, 0.9f, 0.8f, true);        // Alan acknowledges (plan/05)
            pending.Clear();
            MarkerTime = Time.unscaledTime;
            if (only != null) Selection.Remove(only);
        }

        // ------------------------------------------------------------------ box
        static Texture2D white;
        void OnGUI()
        {
            if (!lDown || !dragging || Mouse.current == null) return;
            if (!white) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
            Vector2 a = boxStart, b = Mouse.current.position.ReadValue();
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
            GUI.color = new Color(1, 1, 1, 0.08f); GUI.DrawTexture(r, white);
            GUI.color = new Color(1, 1, 1, 0.9f);
            // marching ants like Zod's box: dashed border that crawls
            float off = (Time.unscaledTime * 24f) % 8f;
            for (float x = r.xMin - off; x < r.xMax; x += 8f) { float x0 = Mathf.Max(x, r.xMin), x1 = Mathf.Min(x + 4f, r.xMax); if (x1 > x0) { GUI.DrawTexture(new Rect(x0, r.yMin, x1 - x0, 2), white); GUI.DrawTexture(new Rect(x0, r.yMax - 2, x1 - x0, 2), white); } }
            for (float y = r.yMin - off; y < r.yMax; y += 8f) { float y0 = Mathf.Max(y, r.yMin), y1 = Mathf.Min(y + 4f, r.yMax); if (y1 > y0) { GUI.DrawTexture(new Rect(r.xMin, y0, 2, y1 - y0), white); GUI.DrawTexture(new Rect(r.xMax - 2, y0, 2, y1 - y0), white); } }
            GUI.color = Color.white;
        }
    }
}
