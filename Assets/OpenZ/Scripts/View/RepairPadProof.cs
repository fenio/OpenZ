using System.Collections;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// the player; a red light tank (40 %) and a red jeep (50 %) are selected and sent with the R key (a proof keyboard device, the real
    /// PlayerInput path); the tank drives onto the pad and stays visible (hoist down, weld sparks + crackle, health pulses + ticks), a blue
    /// grunt squad walks up and the tank fires from the pad; full health -> RepairOut, drives out and back to its start spot; the jeep waits
    /// outside (no two in the bay, min distance logged every frame), then goes in and is called off by a player move order on the pad (it
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class RepairPadProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("RepairPadProof").AddComponent<RepairPadProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; readonly StringBuilder sb = new StringBuilder(); int fails; string dir; float T0;
        ZObject st, A, B; ZObject blue; int aShotsOnPad, bShots; float minAB = 9999f, minABWhileAPad = 9999f; bool aHiddenOnPad, aSeenOnPad; Keyboard proofKb, realKb;
        readonly System.Collections.Generic.List<ZObject> blueSquad = new System.Collections.Generic.List<ZObject>();

        void Log(string s) { sb.AppendLine(s); Debug.Log("[RepairPadProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }
        ZObject Spawn(string cat, string type, int tx, int ty, Team team)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", out tx, out ty);
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        static float Px(ZObject a, ZObject b) => Mathf.Sqrt((a.CenterX - b.CenterX) * (float)(a.CenterX - b.CenterX) + (a.CenterY - b.CenterY) * (float)(a.CenterY - b.CenterY));

        void Start()
        {
            g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/bundle_exe2_repair_station"));
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Update()
        {
            if (W == null || A == null) return;
            foreach (var e in W.Events)
                if ((e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan) && e.objId == A.Id && A.OnRepairPad(st.Id)) aShotsOnPad++;
            if (B != null && !A.Destroyed && !B.Destroyed)
            {
                float d = Px(A, B); minAB = Mathf.Min(minAB, d);
                if (A.OnRepairPad(st.Id)) minABWhileAPad = Mathf.Min(minABWhileAPad, d);
            }
            if (A.OnRepairPad(st.Id)) { aSeenOnPad = true; var v = V(A); if (!v || !v.gameObject.activeInHierarchy) aHiddenOnPad = true; }
        }

        void OnDestroy() { KbOff(); }
        void KbOn() { realKb = Keyboard.current; proofKb = InputSystem.AddDevice<Keyboard>("OzProofKeyboard"); proofKb.MakeCurrent(); }
        void KbOff() { if (proofKb != null) { InputSystem.RemoveDevice(proofKb); proofKb = null; } if (realKb != null) { realKb.MakeCurrent(); realKb = null; } }
        IEnumerator PressR()
        {
            KbOn();
            InputSystem.QueueStateEvent(proofKb, new KeyboardState()); yield return null;
            InputSystem.QueueStateEvent(proofKb, new KeyboardState(OzBindings.KeyOf(OzAction.RepairVehicles))); yield return null; yield return null;
            InputSystem.QueueStateEvent(proofKb, new KeyboardState()); yield return null;
            KbOff();
        }

        IEnumerator All()
        {
            yield return new WaitForSeconds(1f);
            T0 = Time.unscaledTime;
            Log($"bundle_exe2_repair_station proof (DV62), {g.level}, {Screen.width}x{Screen.height}, test mode {PlayerPrefs.GetInt("oz_testmode", 0)}, DV62 {ZWorld.RepairInPlaceDeviation}, key {OzBindings.Cap(OzBindings.KeyOf(OzAction.RepairVehicles))} (NEXT ROBOT {OzBindings.Cap(OzBindings.KeyOf(OzAction.NextRobot))}), conflicts: {OzBindings.ConflictText()}");
            st = W.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Type == "repair" && !o.Destroyed && o.Owner != Team.Blue);
            if (st == null) { Check("a repair station on this level", false); Finish(); yield break; }
            if (st.Owner != g.player) { var f = W.Flags.FirstOrDefault(x => x.ZoneIndex == st.ZoneIndex); if (f != null) W.AwardZone(f, g.player); }
            Check($"repair#{st.Id} belongs to the player ({st.Owner})", st.CanRepairUnit(g.player));
            st.GetRepairEntrance(out int ex, out int ey); st.GetRepairCenter(out int cx, out int cy);
            A = Spawn("vehicle", "light", (ex >> 4) - 2, (ey >> 4) + 10, g.player);
            B = Spawn("vehicle", "jeep", (ex >> 4) + 4, (ey >> 4) + 11, g.player);
            A.SetHealth(A.MaxHealth * 40 / 100); B.SetHealth(B.MaxHealth * 50 / 100);
            int aX = A.CenterX, aY = A.CenterY, bX = B.CenterX, bY = B.CenterY;
            Log($"A light#{A.Id} at ({aX},{aY}) {A.Health}/{A.MaxHealth}; B jeep#{B.Id} at ({bX},{bY}) {B.Health}/{B.MaxHealth}; pad centre ({cx},{cy}), entrance ({ex},{ey})");
            var pad = g.CenterWorld(st);
            if (rts) { rts.FlyTo(pad + new Vector3(0, 0, -6f)); rts.ZoomTo(26f); }
            yield return new WaitForSeconds(1.2f);
            // ---- the R key on both selected damaged vehicles
            input.Selection.Clear(); input.Selection.Add(A); input.Selection.Add(B);
            float tR = Time.unscaledTime;
            yield return PressR();
            yield return null;
            bool aGo = A.Waypoints.Count > 0 && A.Waypoints[0].mode == WpMode.UnitRepair, bGo = B.Waypoints.Count > 0 && B.Waypoints[0].mode == WpMode.UnitRepair;
            Log($"R pressed (proof keyboard): A wp {(aGo ? "UNIT_REPAIR -> #" + A.Waypoints[0].refId : "none")}, B wp {(bGo ? "UNIT_REPAIR -> #" + B.Waypoints[0].refId : "none")}, order sound ui_order_repair {Played(tR, "ui_order_repair")}");
            Check("R sends every selected damaged vehicle to the nearest own repair station (real key path)", aGo && bGo);
            if (!aGo || !bGo) { input.SendToRepair(); Log("fallback: SendToRepair() called directly"); }
            // ---- A on the pad: visible, hoist down, weld, health ticks
            yield return WaitFor(() => A.OnRepairPad(st.Id), 40f);
            float tPad = Time.unscaledTime; double simPad = W.Time; int hp0 = A.Health;
            Log($"A on the pad at sim {W.Time:F1} s: ({A.CenterX},{A.CenterY}) hp {A.Health}, station RepairingUnit {st.RepairingUnit}, B stage on pad {B.OnRepairPad(st.Id)}, B {Px(A, B):F0} px away");
            Check("A drives onto the pad and stays in the world (not deleted, view shown)", A.OnRepairPad(st.Id) && !A.Destroyed && A.KillAt < 0 && V(A) && V(A).gameObject.activeInHierarchy);
            if (rts) { rts.FlyTo(V(A).transform.position + new Vector3(0, 0, -3f)); rts.ZoomTo(16f); }
            yield return WaitFor(() => st.RepairPadProgress >= 0.35f, 6f);
            yield return new WaitForSeconds(0.15f);
            Shot("01_on_pad_welding");
            // ---- a blue squad walks up: A fires from the pad
            int reach = Mathf.Max(48, (int)(A.AttackRadius * 0.75f));
            blue = Spawn("robot", "grunt", (cx >> 4) + 1, (cy + reach) >> 4, Team.Blue);
            blueSquad.Clear(); if (blue != null) { blueSquad.Add(blue); blueSquad.AddRange(blue.Minions); }
            yield return WaitFor(() => aShotsOnPad > 0 || !A.OnRepairPad(st.Id), 6f);
            if (aShotsOnPad > 0) { yield return new WaitForSeconds(0.25f); Shot("02_on_pad_firing"); }
            Log($"blue grunt squad x{blueSquad.Count} {(blue != null ? Px(A, blue).ToString("F0") : "-")} px from A (A attack radius {A.AttackRadius}): A shots while on the pad {aShotsOnPad}, A target {(A.AttackObject != null ? A.AttackObject.Type + "#" + A.AttackObject.Id : "none")}");
            Check("A fires from the pad (fully operational while repaired)", aShotsOnPad > 0);
            foreach (var r in blueSquad) if (!r.Destroyed) { r.SetHealth(0); W.UpdateObjectHealth(r); }
            yield return WaitFor(() => !A.OnRepairPad(st.Id), 15f);
            double simDone = W.Time;
            int weld = Played(tPad, "bld_repair_weld"), tick = Played(tPad, "bld_repair_tick"), hoist = Played(tPad, "bld_repair_hoist"), work = Played(tPad, "bld_repair_work"), outS = Played(tPad, "repairout") + Played(tPad, "bld_repair_out");
            Log($"A repaired: {hp0} -> {A.Health}/{A.MaxHealth} in {simDone - simPad:F2} s sim on the pad (Zod: 5.00 s; hits on the pad add time); heal ticks {ZObject.PadTicks}, healed {ZObject.PadHealed}; view: weld sparks {RepairBayView.Sparks}, bursts {RepairBayView.Bursts}, health pulses {RepairBayView.Pulses}; sounds PLAY: weld {weld}, tick {tick}, hoist {hoist}, work {work}, repair out {outS}; A hidden on the pad {aHiddenOnPad}");
            Check("full health on the pad, about Zod's 5 s", A.Health == A.MaxHealth && simDone - simPad >= 4.9 && simDone - simPad < 9.0);
            Check("repair effect + sound: weld sparks, crackle, health pulses + ticks, hoist", RepairBayView.Sparks > 10 && RepairBayView.Bursts >= 2 && RepairBayView.Pulses >= 3 && weld >= 3 && tick >= 3 && hoist >= 1);
            Check("A never hidden while on the pad", aSeenOnPad && !aHiddenOnPad);
            Check("no two vehicles in the bay (B kept >= 20 px off A while A was on the pad)", minABWhileAPad >= 20f);
            if (rts) { rts.FlyTo(pad + new Vector3(0, 0, -6f)); rts.ZoomTo(24f); }
            // ---- B: in, then called off by a player order
            yield return WaitFor(() => B.OnRepairPad(st.Id), 25f);
            yield return new WaitForSeconds(0.25f);
            int hpB = B.Health; int mx = (ex >> 4) + 8, my = (ey >> 4) + 4;
            var mv = ZWaypoint.Move(mx * 16 + 8, my * 16 + 8, false);
            W.SendWaypoints(g.player, B, new[] { mv });
            yield return null;
            bool leaving = B.LeavingRepairPad(st.Id), next = B.Waypoints.Count >= 2 && B.Waypoints[1].mode == WpMode.Move;
            Log($"B on the pad, called off at {hpB}/{B.MaxHealth} (progress {st.RepairPadProgress:F2}): leaving {leaving}, then MOVE queued {next}, station RepairingUnit {st.RepairingUnit}, called off {ZObject.PadCalledOff}");
            bool direct = !leaving && B.Waypoints.Count >= 1 && B.Waypoints[0].mode == WpMode.Move && !st.RepairingUnit;
            Check("a player order calls the vehicle off the pad at once (drives out, then the order)", (leaving && next && !st.RepairingUnit) || direct);
            yield return WaitFor(() => B.Waypoints.Count == 0 && !B.IsMoving, 25f);
            float dMove = Mathf.Sqrt((B.CenterX - (mx * 16 + 8)) * (float)(B.CenterX - (mx * 16 + 8)) + (B.CenterY - (my * 16 + 8)) * (float)(B.CenterY - (my * 16 + 8)));
            float dHome = Mathf.Sqrt((B.CenterX - bX) * (float)(B.CenterX - bX) + (B.CenterY - bY) * (float)(B.CenterY - bY));
            Log($"B after the call-off: ({B.CenterX},{B.CenterY}) hp {B.Health}/{B.MaxHealth}, {dMove:F0} px from the order's spot, {dHome:F0} px from its start; bay free {st.RepairOccupantId < 0}");
            Check("B followed the order (no automatic return), partly repaired, bay free", dMove <= 40f && dHome > 48f && B.Health < B.MaxHealth && B.Health > hpB - 1 && st.RepairOccupantId < 0);
            // ---- A drives back to where it was
            yield return WaitFor(() => A.Waypoints.Count == 0 && !A.IsMoving, 30f);
            float back = Mathf.Sqrt((A.CenterX - aX) * (float)(A.CenterX - aX) + (A.CenterY - aY) * (float)(A.CenterY - aY));
            Log($"A after the repair: ({A.CenterX},{A.CenterY}), {back:F0} px from its start ({aX},{aY}); returns {ZObject.PadReturns}");
            Check("after the repair A drives back to its spot before the trip", back <= 24f && ZObject.PadReturns >= 1);
            Log($"min A-B distance over the run {minAB:F0} px (vehicles 32 px wide)");
            Check("no overlap at the bay door: A and B centres never closer than 24 px", minAB >= 24f);
            Shot("03_after");
            Finish();
        }

        void Finish()
        {
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL (" + fails + ")")}");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning(ex.Message); }
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject, 0.5f);
        }
    }
}
