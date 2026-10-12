using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play-mode proof. Every scenario stages a red squad under fire from a blue squad (blue in range and shooting), then gives the
    /// order exactly as a right-click would (PlayerInput.OrderFor = the real BuildWaypoint, sent through ZWorld.SendWaypoints like
    /// PlayerInput.Flush) and watches the sim every frame:
    ///  1 ENTER an empty jeep 7 tiles away (a blue squad fires from 4 tiles): the leader must board (jeep turns red) within 25 s;
    ///  2 ATTACK a blue squad MINION while a second blue squad fires from closer: the squad must shoot only that robot until it dies;
    ///  3 PICKUP a grenade crate under fire: the squad must hold grenades within 25 s;
    ///  4 ENTER a gun while two enemy squads converge (attack-move bait): must board, never divert to fight.
    /// </summary>
    public class OrderCheck : MonoBehaviour
    {
        public static string Report = "idle";
        ZGame g; ZWorld W; PlayerInput input; readonly StringBuilder sb = new StringBuilder(); int pass, total;

        public static bool UiOnly;
        public static void Run(bool uiOnly = false)
        {
            UiOnly = uiOnly; Report = "running";
#if UNITY_EDITOR
            // synthetic mouse events only reach the game while the Game view has focus (Input System "respect Game view focus")
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OrderCheck").AddComponent<OrderCheck>();
        }

        void Start() { g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>(); StartCoroutine(All()); }

        Vector2Int Focus()
        {
            var cam = Camera.main;
            g.GroundPoint(cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0)), out var c);
            var px = ZGame.WorldToPx(c); return new Vector2Int(px.x >> 4, px.y >> 4);
        }

        ZObject Squad(string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Thing(string cat, string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "map_item" ? 1 : 2, cat == "map_item", out tx, out ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); return o;
        }
        void Kill(ZObject o) { if (o == null || o.Destroyed) return; foreach (var m in new List<ZObject>(o.Minions)) { m.SetHealth(0); W.UpdateObjectHealth(m); } o.SetHealth(0); W.UpdateObjectHealth(o); }
        bool Order(ZObject u, ZObject h)
        {
            var wp = input.OrderFor(u, h, h.CenterX, h.CenterY);
            return W.SendWaypoints(g.player, u, new List<ZWaypoint> { wp });
        }
        /// <summary>Order trace: every change of the unit's first waypoint and of its attack target, with the sim time.</summary>
        readonly StringBuilder trace = new StringBuilder(); string lastTr = "";
        void Trace(ZObject u)
        {
            if (u == null) return;
            string w = u.Waypoints.Count > 0 ? $"{u.Waypoints[0].mode}#{u.Waypoints[0].refId}{(u.Waypoints[0].playerGiven ? "P" : "")}{(u.Waypoints[0].attackTo ? "A" : "")}" : "none";
            string k = $"wp {w} n{u.Waypoints.Count} atk {(u.AttackObject != null ? u.AttackObject.Type + "#" + u.AttackObject.Id : "-")} mv {u.IsMoving} held {u.PathHeld} wait {u.PathWait} by {(u.PathState?.heldBy != null ? u.PathState.heldBy.Type + "#" + u.PathState.heldBy.Id + (u.PathState.heldBy.Owner == u.Owner ? "(own)" : "") : "-")} at ({u.CenterX >> 4},{u.CenterY >> 4}) end {u.PathState?.LastOrderEnd}";
            if (k != lastTr) { trace.AppendLine($"    {W.Time:F1}s {k}"); lastTr = k; }
        }
        void Log(string name, bool ok, string detail) { total++; if (ok) pass++; sb.AppendLine($"- {(ok ? "PASS" : "FAIL")} {name}: {detail}"); sb.Append(trace); trace.Clear(); lastTr = ""; Debug.Log($"[OrderCheck] {(ok ? "PASS" : "FAIL")} {name}: {detail}"); }

        IEnumerator All()
        {
            var f = Focus();
            foreach (var t in new[] { "gun", "gatling", "howitzer", "missile_cannon" }) yield return ExitByClick(f, t);   // quiet field first
            if (!UiOnly) {
            yield return Enter(f, "jeep", "vehicle", "1 enter jeep under fire");
            yield return AttackMinion(f);
            yield return Pickup(f);
            yield return Enter(f, "gun", "cannon", "4 enter gun, two enemy squads converging", true);
            }
            Report = $"{pass}/{total} PASS\n" + sb;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
            if (Application.isEditor && System.IO.Directory.Exists(dir)) System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "order_check.md"), $"# Order check ({System.DateTime.Now:HH:mm}, {g.name})\n\n{Report}");
            Debug.Log("[OrderCheck] " + Report);
            Destroy(gameObject);
        }

        IEnumerator Enter(Vector2Int f, string type, string cat, string name, bool twoEnemies = false)
        {
            var red = Squad("grunt", Team.Red, f.x - 4, f.y);
            foreach (var r in new List<ZObject>(red.Minions) { red }) { r.MaxHealth *= 6; r.SetHealth(r.MaxHealth); }   // must live to finish the order under fire
            var v = Thing(cat, type, Team.Neutral, f.x + 3, f.y + 1);
            var blue = Squad("grunt", Team.Blue, f.x - 4, f.y + 4);
            int dist0 = (int)Mathf.Sqrt((v.CenterX - red.CenterX) * (float)(v.CenterX - red.CenterX) + (v.CenterY - red.CenterY) * (float)(v.CenterY - red.CenterY));
            trace.AppendLine($"    start: {dist0} px to the target, move speed {red.MoveSpeed:F0} px/s");
            var blue2 = twoEnemies ? Squad("psycho", Team.Blue, f.x - 1, f.y - 4) : null;
            if (twoEnemies) { W.SendWaypoints(Team.Blue, blue2, new List<ZWaypoint> { ZWaypoint.Move(red.CenterX, red.CenterY) }); }
            yield return new WaitForSeconds(1.5f);                                                  // the fight is on
            bool fired = blue.AttackObject != null || (blue2 != null && blue2.AttackObject != null);
            Order(red, v);
            float t0 = Time.time; bool boarded = false; int diverted = 0; string lastMode = "";
            while (Time.time - t0 < 45f)                                                           // grunts walk 14 px/s (Zod): 7 tiles + detours
            {
                if (red.Destroyed && red.Successor != null) red = red.Successor;
                Trace(red);
                if (v.Owner == Team.Red) { boarded = true; break; }
                if (red.Destroyed) break;
                if (red.Waypoints.Count > 0) { lastMode = red.Waypoints[0].mode.ToString(); if (red.Waypoints[0].mode != WpMode.Enter) diverted++; }
                else { lastMode = "none"; break; }
                yield return null;
            }
            Log(name, boarded && diverted == 0, $"boarded {boarded} after {Time.time - t0:F1} s, frames diverted {diverted}, last wp {lastMode}, enemy firing at order {fired}");
            Kill(blue); Kill(blue2); Kill(red); if (v.Owner != Team.Neutral) { v.SetHealth(0); W.UpdateObjectHealth(v); } else { v.SetHealth(0); W.UpdateObjectHealth(v); }
            yield return new WaitForSeconds(2f);
        }

        // ---- real UI: select the crewed gun, click the EXIT CREW button with synthetic mouse events (as a player would)
        void MouseAt(Vector2 p, bool left) { var st = new UnityEngine.InputSystem.LowLevel.MouseState { position = p }; if (left) st = st.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left); UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, st); }
        IEnumerator ExitByClick(Vector2Int f, string type)
        {
            var gun = Thing("cannon", type, Team.Red, f.x + 2, f.y - 2);
            yield return new WaitForSeconds(0.6f);
            input.Selection.Clear(); input.Selection.Add(gun);
            yield return new WaitForSeconds(0.5f);
            var hud = OpenZHud.I; var rt = hud ? hud.ExitButtonRect : null;
            bool shown = rt && rt.gameObject.activeInHierarchy;
            trace.AppendLine($"    before click: owner {gun.Owner}, drivers {gun.Drivers.Count}, ejectable {gun.Ejectable}, destroyed {gun.Destroyed}, selected {input.Selection.Count}");
            int before = W.Objects.Count; bool neutral = false;
            if (shown)
            {
                Vector2 c = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                MouseAt(c, false); yield return null; yield return null;
                MouseAt(c, true); yield return null; yield return null;
                MouseAt(c, false); yield return null; yield return null; yield return null;
                neutral = gun.Owner == Team.Neutral;
                trace.AppendLine($"    after click: owner {gun.Owner}, drivers {gun.Drivers.Count}, selection {input.Selection.Count}, hot button {(hud.HotButton != null ? hud.HotButton.Rt.name : "-")}");
            }
            int robots = 0; foreach (var o in W.Objects) if (o.Kind == ZKind.Robot && o.Owner == Team.Red && !o.Destroyed && Mathf.Abs(o.CenterX - gun.CenterX) < 64 && Mathf.Abs(o.CenterY - gun.CenterY) < 64) robots++;
            Log($"5 EXIT CREW button click on a {type}", shown && neutral && robots > 0, $"button shown {shown}, gun neutral {neutral}, crew robots out {robots}");
            input.Selection.Clear();
            foreach (var o in new List<ZObject>(W.Objects)) if (o.Kind == ZKind.Robot && o.Owner == Team.Red && Mathf.Abs(o.CenterX - gun.CenterX) < 64 && Mathf.Abs(o.CenterY - gun.CenterY) < 64) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            gun.SetHealth(0); W.UpdateObjectHealth(gun);
            yield return new WaitForSeconds(1f);
        }

        IEnumerator PanelSlide()
        {
            var hud = OpenZHud.I; input.Selection.Clear();
            yield return new WaitForSeconds(0.6f);
            bool hidden = hud && !hud.UnitPanelRect.gameObject.activeSelf;
            foreach (var o in W.Objects) if (o.Kind == ZKind.Robot && o.Owner == Team.Red && !o.Destroyed && !o.IsMinion) { input.Selection.Add(o); break; }
            yield return new WaitForSeconds(0.5f);
            bool back = hud && hud.UnitPanelRect.gameObject.activeSelf && hud.UnitPanelHide < 0.01f;
            Log("6 unit panel hides with no selection, slides back on select", hidden && back, $"hidden when empty {hidden}, back when selected {back}");
            input.Selection.Clear();
        }

        IEnumerator AttackMinion(Vector2Int f)
        {
            var red = Squad("grunt", Team.Red, f.x - 3, f.y);
            foreach (var r in new List<ZObject>(red.Minions) { red }) { r.MaxHealth *= 6; r.SetHealth(r.MaxHealth); }   // the ordered squad must live through the test
            var near = Squad("psycho", Team.Blue, f.x - 3, f.y + 3);                                  // closer, shooting at us
            var far = Squad("grunt", Team.Blue, f.x + 3, f.y - 1);
            yield return new WaitForSeconds(1.5f);
            var target = far.Minions.Count > 0 ? far.Minions[far.Minions.Count - 1] : far;
            target.MaxHealth *= 4; target.SetHealth(target.MaxHealth);                               // lives long enough to watch loyalty
            var wpDbg = input.OrderFor(red, target, target.CenterX, target.CenterY);
            trace.AppendLine($"    order built: {wpDbg.mode}#{wpDbg.refId} attackTo {wpDbg.attackTo}; red owner {red.Owner}, player {g.player}, minion {red.IsMinion}");
            bool sent = Order(red, target);
            trace.AppendLine($"    at send: red #{red.Id} alive {!red.Destroyed}, wps {red.Waypoints.Count}{(red.Waypoints.Count > 0 ? " " + red.Waypoints[0].mode + "#" + red.Waypoints[0].refId : "")}, can attack target {red.CanAttackObject(target)}, target #{target.Id} hp {target.Health}");
            float t0 = Time.time; int wrong = 0, right = 0; bool dead = false;
            while (Time.time - t0 < 30f)
            {
                Trace(red);
                if (target.Destroyed) { dead = true; break; }
                if (red.Destroyed && red.Successor != null) red = red.Successor;
                if (red.Destroyed) break;
                var a = red.AttackObject;
                if (a != null) { if (a == target) right++; else wrong++; }
                yield return null;
            }
            Log("2 attack an enemy squad minion while another squad fires from closer", sent && wrong == 0 && (dead || right > 60), $"order accepted {sent}, target dead {dead} after {Time.time - t0:F1} s, frames firing at it {right}, at others {wrong}");
            Kill(near); Kill(far); Kill(red);
            yield return new WaitForSeconds(2f);
        }

        IEnumerator Pickup(Vector2Int f)
        {
            var red = Squad("grunt", Team.Red, f.x - 4, f.y);
            var crate = Thing("map_item", "grenades", Team.Neutral, f.x + 3, f.y);
            var blue = Squad("grunt", Team.Blue, f.x - 4, f.y + 4);
            blue.Grenades = 1;                                                                        // blue must not auto-grab the test crate (Zod idle grab, 220 px)
            yield return new WaitForSeconds(1.5f);
            if (crate.Destroyed) trace.AppendLine("    crate gone before the order!");
            bool sent = Order(red, crate);
            float t0 = Time.time; bool got = false; int diverted = 0;
            while (Time.time - t0 < 25f)
            {
                if (red.Destroyed && red.Successor != null) red = red.Successor;
                if (red.Destroyed) break;
                Trace(red);
                bool any = red.Grenades > 0; foreach (var m in red.Minions) if (m != null && m.Grenades > 0) any = true;
                if (any || crate.Destroyed) { got = any || red.HasExplosives(); if (got) break; }
                if (red.Waypoints.Count > 0 && red.Waypoints[0].mode != WpMode.PickupGrenades) diverted++;
                if (red.Waypoints.Count == 0) break;
                yield return null;
            }
            Log("3 pick up grenades under fire", sent && got && diverted == 0, $"order accepted {sent}, grenades {got} after {Time.time - t0:F1} s, frames diverted {diverted}");
            Kill(blue); Kill(red);
            yield return new WaitForSeconds(2f);
        }
    }
}
