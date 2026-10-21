using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K437 Play proof: the shuttle lands where it really fits (rule DV128, Sim/ZWorld.ShuttleFairSpot.cs), on level 16 in Play (menu
    /// OpenZ/V1.1/Shuttle: K437 Island Landing Proof, Editor/OzShuttleFairSpotMenu.cs). Counted PASS / FAIL lines and full-screen pictures from
    /// the normal RTS camera at 70 m. Set-up: the CPU teams removed, morning (day clock 0.10, held), a red grunt squad of 4 boards the ship.
    /// The game's pointer is moved by queued input events inside the game only (no desktop input); the order goes through the click's own
    /// filter (OzShuttleOrderAim.Filter, LANDABLE SPOTS ONLY) and EXIT is the game's own command (PlayerInput.EjectUnit). Stages:
    ///  O  the rule as it was (the switch off for a moment): the cursor over the gatling island's landing tile is NO;
    ///  I1 the rule on: the cursor over the island's tile 29,68 is the move cursor, the ship's mark is not red;
    ///  I2 over the gatling's own tile (no move cursor, the mark red), the flag's tile (no move cursor) and the water east of the island (NO);
    ///  I3 the order to tile 29,68: kept, the ship flies there and lands (no hover) facing south (95-100 deg), its mark never red;
    ///  I4 EXIT: the squad of 4 comes down the ramp onto the island (ground a robot can stand on), nobody inside the ship;
    ///  I5 one robot takes the island's flag beside the ship;
    ///  S1 a shore landing only the new rule takes (water under the pods, the pads on dry ground), near the ship's start: it lands.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_fair_spot_k437, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleFairSpotProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_fair_spot_k437");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_fair_spot_k437");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleFairSpotProof").AddComponent<OzShuttleFairSpotProof>();
        }

        const int IslandTx = 29, IslandTy = 68, GunTx = 29, GunTy = 63, FlagTx = 28, FlagTy = 66, WaterTx = 33, WaterTy = 66;
        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; OpenZCursors cursors; Team me = Team.Red;
        ZObject s, lead; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); cursors = FindFirstObjectByType<OpenZCursors>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleFairSpotProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        static string F(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        static int Mid(int t) => t * 16 + 8;
        Vector3 Ground(int px, int py) { float x = px / 8f, z = -py / 8f; return new Vector3(x, g.GroundY(x, z), z); }

        /// <summary>The game's pointer onto the map point (px) by a queued mouse state (inside the game only), then the cursor a moment later
        /// (the cursor's test is spread over frames and cached per tile).</summary>
        IEnumerator PointAt(int px, int py, string[] outCursor)
        {
            var cam = Camera.main; outCursor[0] = "?";
            if (!cam || Mouse.current == null) yield break;
            var sp = cam.WorldToScreenPoint(Ground(px, py));
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = new Vector2(sp.x, sp.y) });
            yield return Wait(0.35f);
            outCursor[0] = cursors ? cursors.Current : "none";
        }

        IEnumerator Board()
        {
            if (s.Passengers.Count > 0) yield break;
            var (bx, by) = s.ShuttleBackPoint(6);
            lead = W.CreateRobotGroup("grunt", bx - 8, by - 8, me, 4);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 15f);
            W.SendWaypoints(me, lead, new[] { new ZWaypoint { mode = WpMode.Enter, x = s.CenterX, y = s.CenterY, refId = s.Id, playerGiven = true } });
            yield return WaitFor(() => s.Passengers.Count >= 4, 60f);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f);
            yield return Wait(0.5f);
        }

        /// <summary>The click's own path for one move: OzShuttleOrderAim.Filter (LANDABLE SPOTS ONLY), then the orders sent.</summary>
        bool Order(int x, int y)
        {
            var pending = new Dictionary<ZObject, List<ZWaypoint>> { [s] = new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } } };
            if (OzShuttleOrderAim.Filter(g, pending)) return false;
            return pending.ContainsKey(s) && W.SendWaypoints(me, s, pending[s]);
        }

        IEnumerator Fly(int x, int y, string tag, System.Action<bool, bool, float> done)
        {
            int hov0 = s.ShuttleHovers; float maxRed = 0f;
            bool kept = Order(x, y);
            yield return WaitFor(() => s.ShuttleState != ZShuttleState.Landed, 5f);
            float t = 0f;
            while (t < 120f && !(s.ShuttleState == ZShuttleState.Landed || s.ShuttleState == ZShuttleState.Hover))
            {
                if (ship.Shadow != null) maxRed = Mathf.Max(maxRed, ship.Shadow.RedNow);
                Cam(g.CenterWorldSmooth(s), 70f);
                t += Time.deltaTime; yield return null;
            }
            yield return WaitFor(() => s.ShuttleLevelled && W.Time >= s.ShuttleRampHoldUntil, 6f);
            yield return Wait(1.5f);
            bool landed = s.ShuttleState == ZShuttleState.Landed && s.ShuttleHovers == hov0;
            Log($"   {tag}: order kept {kept}, {(landed ? "landed" : "state " + s.ShuttleState + " ('" + s.ShuttleNoLand + "')")} at {F(s.CenterX / 8.0)},{F(s.CenterY / 8.0)} heading {F(ZObject.ShuttleNormDeg(s.ShuttleHeadingDeg))}, the mark's red at most {F(maxRed)}");
            done(kept, landed, maxRed);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K437 fair landing spot proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {F(W.Time)} s; the rule {ZWorld.ShuttleFairSpotOn}; SHUTTLE ORDERS landable only {OzShuttleOrdersOption.LandableOnly}; plant crowns handed in {W.ShuttleKeepOutCount}");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            Check("P0 the K437 rule is on and the orders take landable spots only", ZWorld.ShuttleFairSpotOn && OzShuttleOrdersOption.LandableOnly);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled, 30f);
            yield return Board();
            Check($"P0 a squad of 4 aboard ({s.Passengers.Count})", s.Passengers.Count == 4);
            int ix = Mid(IslandTx), iy = Mid(IslandTy);
            var cur = new string[1];
            // ---- O: the rule as it was
            Select(s); Cam(Ground(ix, iy - 40), 70f); yield return Wait(0.5f);
            ZWorld.ShuttleFairSpotOn = false;
            yield return PointAt(Mid(IslandTx + 3), iy, cur); yield return Wait(1.1f); yield return PointAt(ix, iy, cur);   // a fresh answer for the tile (the cursor's cache lives 1 s)
            string curOld = cur[0]; bool neverOld = W.ShuttleNeverLands(ix, iy, s, out string whyOld);
            ZWorld.ShuttleFairSpotOn = true;
            Check($"O the rule as it was: over the island's tile {IslandTx},{IslandTy} the cursor '{curOld}', never lands {neverOld} ({whyOld})", neverOld && curOld == "nono");
            // ---- I1: the rule on, the cursor over the island's landing tile
            yield return PointAt(Mid(IslandTx - 2), Mid(IslandTy - 1), cur); yield return Wait(1.1f);
            yield return PointAt(ix, iy, cur);
            string curNow = cur[0]; bool neverNow = W.ShuttleNeverLands(ix, iy, s, out string whyNow); bool aimRed = OzShuttleOrderAim.AimBlocked(W, s, Ground(ix, iy));
            Shot("K437_I1_island_cursor_day_h70");
            Check($"I1 over the island's tile {IslandTx},{IslandTy}: the cursor '{curNow}', never lands {neverNow} ({whyNow}), the mark red {aimRed}", !neverNow && curNow == "move" && !aimRed);
            // ---- I2: still refused
            yield return PointAt(Mid(GunTx), Mid(GunTy), cur); string curGun = cur[0]; bool gunNever = W.ShuttleNeverLands(Mid(GunTx), Mid(GunTy), s, out string whyGun); bool gunRed = OzShuttleOrderAim.AimBlocked(W, s, Ground(Mid(GunTx), Mid(GunTy)));
            Shot("K437_I2_gatling_no_cursor_day_h70");
            yield return PointAt(Mid(FlagTx), Mid(FlagTy), cur); string curFlag = cur[0]; bool flagNever = W.ShuttleNeverLands(Mid(FlagTx), Mid(FlagTy), s, out string whyFlag);
            yield return PointAt(Mid(WaterTx), Mid(WaterTy), cur); string curWater = cur[0]; bool waterNever = W.ShuttleNeverLands(Mid(WaterTx), Mid(WaterTy), s, out string whyWater);
            Check($"I2 still refused: the gatling's tile (cursor '{curGun}', {whyGun}, mark red {gunRed}), the flag's tile ('{curFlag}', {whyFlag}), the water east of the island ('{curWater}', {whyWater})",
                  gunNever && gunRed && curGun != "move" && flagNever && whyFlag == "flag" && curFlag != "move" && waterNever && whyWater == "water" && curWater == "nono");   // the gun's own tile: refused, its word is the place's first ("water": the gun stands on the island's corner, the counted check's A says the same)
            // ---- I3: the flight to the island
            bool kept = false, landed = false; float red = 1f;
            yield return Fly(ix, iy, "I3 to the island", (k, l, r) => { kept = k; landed = l; red = r; });
            double hd = ZObject.ShuttleNormDeg(s.ShuttleHeadingDeg);
            bool atSpot = Mathf.Abs(s.CenterX - ix) <= 2 && Mathf.Abs(s.CenterY - iy) <= 2;
            Cam(g.CenterWorldSmooth(s), 70f); yield return Wait(0.6f);
            Shot("K437_I3_island_landed_day_h70");
            Check($"I3 the order kept {kept}; it landed on the island's tile {atSpot} (no hover {landed}), facing {F(hd)} deg (95-100 wanted), its mark never red ({F(red)})", kept && landed && atSpot && hd >= 92 && hd <= 103 && red < 0.05f);
            // ---- I4: EXIT onto the island
            bool taken = input.EjectUnit(s);
            yield return WaitFor(() => s.Passengers.Count == 0, 12f);
            yield return Wait(2.5f);
            var squad = new List<ZObject>();
            foreach (var o in W.Objects) if (o.IsRobot && o.Owner == me && !o.Destroyed && o.KillAt < 0 && Mathf.Abs(o.CenterX - s.CenterX) < 160 && Mathf.Abs(o.CenterY - s.CenterY) < 160) squad.Add(o);
            int bad = 0; foreach (var r in squad) { int tx = r.CenterX >> 4, ty = r.CenterY >> 4; var c = W.Map.ClassAt(tx, ty); if (c == ZClass.DeepWater || c == ZClass.Impassable || !W.Path.TilePassable(tx, ty, true)) bad++; }
            int inside = W.ShuttleUnitsInside(s, 0.0);
            Shot("K437_I4_squad_out_day_h70");
            Check($"I4 EXIT taken {taken}: {squad.Count} robots out, {bad} on ground a robot cannot stand on, {inside} inside the ship", taken && squad.Count == 4 && bad == 0 && inside == 0);
            // ---- I5: the island's flag beside the ship
            ZObject flag = null; foreach (var f in W.Flags) if ((f.LocX >> 4) == FlagTx && (f.LocY >> 4) == FlagTy) flag = f;
            ZObject taker = null; foreach (var r in squad) if (!r.IsMinion) taker = r; if (taker == null && squad.Count > 0) taker = squad[0];
            if (taker != null && flag != null) W.SendWaypoints(me, taker, new[] { ZWaypoint.Move(flag.CenterX, flag.CenterY, false) });
            yield return WaitFor(() => flag != null && flag.Owner == me, 40f);
            yield return Wait(1.0f);
            Shot("K437_I5_flag_taken_day_h70");
            Check($"I5 the island's flag taken by the squad beside the ship: {(flag != null ? flag.Owner.ToString() : "no flag")}", flag != null && flag.Owner == me);
            // ---- S1: a shore landing only the new rule takes (water under the pods, the pads on dry ground), near the ship's start
            // shore tiles (dry, a water tile within 2 tiles) nearest the ship's start first, tested a few a frame (no long stall), the first that
            // the rule takes now and the rule as it was refused for water
            int[] shore = null; int sx0 = 118 * 8, sy0 = 82 * 8, e = ZWorld.ShuttleEdgeTiles;
            var cand = new List<(long d, int x, int y)>();
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = Mid(tx), y = Mid(ty); long d = (long)(x - sx0) * (x - sx0) + (long)(y - sy0) * (y - sy0);
                    if (d > 480L * 480L) continue;
                    var c0 = W.Map.ClassAt(tx, ty); if (c0 != ZClass.Ground && c0 != ZClass.Road) continue;
                    bool wetNear = false;
                    for (int dy = -2; dy <= 2 && !wetNear; dy++) for (int dx = -2; dx <= 2 && !wetNear; dx++) { var c1 = W.Map.ClassAt(tx + dx, ty + dy); wetNear = c1 == ZClass.Water || c1 == ZClass.DeepWater; }
                    if (wetNear) cand.Add((d, x, y));
                }
            cand.Sort((p1, p2) => p1.d.CompareTo(p2.d));
            int tested = 0;
            foreach (var (d, x, y) in cand)
            {
                if (++tested % 8 == 0) yield return null;
                if (W.ShuttleNeverLands(x, y, s, out _) || !W.ShuttlePadFree(x, y, s, out _)) continue;
                ZWorld.ShuttleFairSpotOn = false; bool old = W.ShuttleNeverLands(x, y, s, out string wo); ZWorld.ShuttleFairSpotOn = true;
                if (!old || wo != "water") continue;
                shore = new[] { x, y }; break;
            }
            Log($"   S1: {cand.Count} shore tiles within 60 m of the start, {tested} tested");
            if (shore == null) { Check("S1 a shore place only the new rule takes near the start", false); }
            else
            {
                int ov0 = ZWorld.ShuttleOverhangWater;
                yield return Fly(shore[0], shore[1], "S1 to the shore", (k, l, r) => { kept = k; landed = l; red = r; });
                Cam(g.CenterWorldSmooth(s), 70f); yield return Wait(0.6f);
                Shot("K437_S1_shore_pods_over_water_day_h70");
                Check($"S1 a shore place the rule as it was refused for water ({F(shore[0] / 8.0)},{F(shore[1] / 8.0)}): order kept {kept}, landed {landed}, water under the pods counted {ZWorld.ShuttleOverhangWater - ov0 > 0}", kept && landed);
            }
            Finish();
        }

        void Finish()
        {
            Log($"K437 fair landing spot proof: {passes} PASS, {fails} FAIL");
            Report = fails == 0 && passes > 0 ? "PASS" : "FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "fair_spot_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleFairSpotProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
