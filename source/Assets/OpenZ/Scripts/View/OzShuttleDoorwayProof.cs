using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// destroyed at every in-between moment, on level 16 in Play (EASY, the SHUTTLE box ON; menu OpenZ/V1.1/Shuttle: K418 Death Edge Cases
    /// Proof (Level 16, Play), Editor/OzShuttleDoorwayMenu.cs). The computer player's orders removed, the other teams hold their fire, the day
    /// clock held at morning, 60 fps; each case on its own ship (level 16's own for P1, the testing panel's drop for the rest), on the open
    /// ground east of the robot factory, killed by the rules' own death (DamageHealth + UpdateObjectHealth; P2b from a frame step that runs
    /// right after the rules' tick, so the ship dies in the very frame it touches down). One full-screen picture per case from the normal RTS
    /// camera at 70 m, 0.6 s after the kill (folder <see cref="Folder"/>).
    ///  P1 take-off (lift 0.5, the legs folding)        P2 normal landing (coming down, lift 0.3)   P2b at the touchdown (no landing voice after)
    ///  P3 crash landing: the slam / P3b righting itself P4 boarding: two inside, two on the ramp / walking in
    ///  P5 unloading: two on the ramp, two still seated  P6 ALAN &amp; BRAD into an EMPTY ship: Brad past the doorway, Alan still outside
    ///  P7 mid kickflip                                  P8 the repair station: the crane moving away
    /// Per case two counted lines: the RULES (aboard / lost / saved as DV126 says) and the DRAWING (one body thrown per lost robot; every
    /// saved robot drawn, visible, on the ground at its rules place, at least 5 m from the wreck, and it walks when ordered; no figure left on
    /// the ramp or held; the ship broken up and deselected; no landing voice after the kill). Plus: P2b the touchdown said nothing, P7 the flip
    /// was drawn when it died, P8 the crane rolled back home, and at the end the console's errors and exceptions during the run: 0.
    /// Test only.
    /// </summary>
    [DefaultExecutionOrder(-45)]                                                                         // after the rules' tick (-50), before the ship's view reads its notes (200)
    public sealed class OzShuttleDoorwayProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_death_edge_k418");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_death_edge_k418");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleDoorwayProof").AddComponent<OzShuttleDoorwayProof>();
        }

        static readonly Vector2 Focus = new Vector2(150, -92);
        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; BoardingView bv;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, errors, spotIx; string dir;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();
        System.Func<bool> killWhen; ZObject killShip; bool killedNow;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); bv = BoardingView.I;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            StartCoroutine(All());
        }
        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) kv.Key.AttackRadius = kv.Value;
            heldFire.Clear(); ZWorld.ShuttleRoughProofNext = false;
        }
        void OnLog(string msg, string stack, LogType t) { if ((t == LogType.Error || t == LogType.Exception || t == LogType.Assert) && !msg.StartsWith("[OzShuttleDoorwayProof]")) errors++; }

        /// <summary>The kill on a condition, checked right after the rules' tick (before the ship's view reads this frame's notes).</summary>
        void Update()
        {
            if (killWhen == null || killShip == null || W == null) return;
            if (killShip.Destroyed) { killWhen = null; return; }
            if (!killWhen()) return;
            killWhen = null; Kill(killShip); killedNow = true;
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleDoorwayProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(ZObject o, float h = 70f) { if (!rts || o == null) return; var p = ZGame.PxToWorld(o.CenterX, o.CenterY); rts.FreeLookOff(); rts.focus = new Vector3(p.x, 0f, p.z); rts.SetHeightImmediate(h); }
        static int Px(float x) => Mathf.Clamp(Mathf.FloorToInt(x * 8f / 16f), 1, 400) * 16 + 8;
        void Kill(ZObject s) { s.DamageHealth(s.Health); W.UpdateObjectHealth(s); }
        static bool Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;

        void HoldFire()
        {
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || heldFire.ContainsKey(o)) continue;
                heldFire[o] = o.AttackRadius; o.AttackRadius = 0;
            }
        }

        // ------------------------------------------------------------------ ships and squads
        /// <summary>A fresh ship (the testing panel's drop) on the next free spot round the proof ground, drawn.</summary>
        ZObject Ship(Team team)
        {
            for (int tries = 0; tries < 24; tries++)
            {
                int i = spotIx++;
                float a = i * 1.1f, r = 18f + 9f * (i % 4);
                var p = Focus + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                var s = W.SpawnShuttle(Px(p.x), Px(-p.y), team, -90f, out _);
                if (s == null) continue;
                var v = OzPlayableShuttle.SpawnView(g, s);
                if (v && team == Team.Neutral) OzShuttleEmptyLook.Ensure(v);
                return s;
            }
            return null;
        }

        ZObject Squad(ZObject s, int n = 4)
        {
            var (bx, by) = s.ShuttleBackPoint(5);
            if (!OzCheatSpawner.FreeTile(W, bx >> 4, by >> 4, 1, true, out int tx, out int ty)) return null;
            var o = W.CreateRobotGroup("grunt", tx * 16, ty * 16, me, n); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }

        bool Enter(ZObject sq, ZObject s) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
        bool Move(ZObject s, int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });

        IEnumerator Board(ZObject s, ZObject sq)
        {
            Enter(sq, s);
            yield return WaitFor(() => s.Passengers.Count > 0 || s.Drivers.Count > 1, 60f);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= s.ShuttleRampHoldUntil, 20f);
        }

        bool Pad(ZObject s, float minM, float maxM, out int px, out int py)
        {
            for (float r = minM; r <= maxM; r += 4f)
                for (int a = 0; a < 24; a++)
                {
                    float ang = a * Mathf.PI / 12f;
                    var (x, y) = W.ShuttleClamp(s.CenterX + (int)(Mathf.Cos(ang) * r * 8f), s.CenterY + (int)(Mathf.Sin(ang) * r * 8f));
                    if (W.ShuttlePadFree(x, y, s, out _)) { px = x; py = y; return true; }
                }
            px = py = 0; return false;
        }

        // ------------------------------------------------------------------ the kill and the two counted lines
        sealed class Before { public int seq, dseq, bodies, voices, walks, errors; public Vector3 at; public int sx, sy; public bool air; public int aboard; }

        Before Mark(ZObject s) => new Before
        {
            seq = W.ShuttleLostSeq, dseq = W.ShuttleDoorwaySeq, bodies = BoardingView.ShuttleBodies, voices = OzPlayableShuttle.LandingRobot + OzPlayableShuttle.LandingRachel,
            walks = BoardingView.DoorwayWalks, errors = errors, sx = s.CenterX, sy = s.CenterY, air = s.ShuttleAirborne, aboard = s.Passengers.Count,
        };

        IEnumerator After(string tag, ZObject s, Before b, int wantLost, int wantSaved, bool wantAir)
        {
            var ship = OzPlayableShuttle.Of(s);
            yield return Wait(0.6f);
            Shot("K418_" + tag + "_70m");
            yield return WaitFor(() => bv == null || bv.DoorwayWalking == 0, 8f);
            yield return Wait(1.5f);
            yield return WaitFor(() => SavedOnPlace(s, b), 6f);                                           // K420: the saved robots step back onto their rules places
            var lost = new List<ZShuttleLost>(); foreach (var r in W.ShuttleLostCrew) if (r.shipId == s.Id && r.seq > b.seq) lost.Add(r);
            var rows = new List<ZShuttleDoorwayRow>(); foreach (var r in W.ShuttleDoorwayRows) if (r.shipId == s.Id && r.seq > b.dseq) rows.Add(r);
            int saved = 0, drawn = 0, ground = 0, clear = 0; float worstOff = 0f, worstY = 0f;
            var savedRobots = new List<ZObject>();
            foreach (var r in rows)
            {
                if (!r.saved) continue;
                var o = W.Get(r.robotId); if (!Alive(o)) continue;
                saved++; savedRobots.Add(o);
                g.Views.TryGetValue(o.Id, out var v);
                bool vis = false; if (v && v.gameObject.activeInHierarchy) foreach (var rr in v.GetComponentsInChildren<Renderer>()) if (rr.enabled) { vis = true; break; }
                if (vis) drawn++;
                if (v)
                {
                    var want = ZGame.PxToWorld(o.CenterX, o.CenterY); var p = v.transform.position;
                    float off = new Vector2(p.x - want.x, p.z - want.z).magnitude, dy = Mathf.Abs(p.y - g.GroundY(p.x, p.z));
                    worstOff = Mathf.Max(worstOff, off); worstY = Mathf.Max(worstY, dy);
                    if (off <= OnPlaceM && dy <= 0.4f) ground++;
                }
                if (new Vector2(o.CenterX - b.sx, o.CenterY - b.sy).magnitude / 8f >= 5f) clear++;
            }
            bool rules = lost.Count == wantLost && saved == wantSaved && b.air == wantAir;
            Check($"{tag} RULES: {(b.air ? "in the air" : "on the ground")}, aboard {b.aboard}; lost {lost.Count} (want {wantLost}), saved {saved} (want {wantSaved}); the file's rows {rows.Count}", rules);
            // they walk when ordered
            float moved = 99f;
            if (savedRobots.Count > 0)
            {
                var p0 = new Dictionary<ZObject, Vector2>(); foreach (var o in savedRobots) p0[o] = new Vector2(o.CenterX, o.CenterY);
                foreach (var o in savedRobots) if (!o.IsMinion) W.SendWaypoints(me, o, new[] { ZWaypoint.Move(o.CenterX + 48, o.CenterY + 16, false) });
                yield return Wait(3f);
                moved = 99f; foreach (var kv in p0) if (Alive(kv.Key)) moved = Mathf.Min(moved, (new Vector2(kv.Key.CenterX, kv.Key.CenterY) - kv.Value).magnitude / 8f);
            }
            int bodies = BoardingView.ShuttleBodies - b.bodies, voices = OzPlayableShuttle.LandingRobot + OzPlayableShuttle.LandingRachel - b.voices;
            int ghostsLeft = bv ? bv.ShuttleGhostsFor(s.Id) + bv.ShuttleHeldFor(s.Id) : -1;
            bool desel = input == null || !input.Selection.Contains(s);
            bool broken = ship == null || ship.BrokenUp;
            int unseen = 0;                                                                                   // a thrown body with nothing drawn (a figure that was a child of the ship when the break hid its renderers)
            foreach (var cf in OzShuttleCrewFall.Last) { if (!cf) continue; bool seen = false; foreach (var rr in cf.GetComponentsInChildren<Renderer>(true)) if (rr.enabled && !(rr is ParticleSystemRenderer)) { seen = true; break; } if (!seen) unseen++; }
            bool view = bodies == lost.Count && unseen == 0 && drawn == saved && ground == saved && clear == saved && (saved == 0 || moved >= 2f) && ghostsLeft == 0 && desel && broken && voices == 0;
            Check($"{tag} DRAWN: bodies thrown {bodies} (want {lost.Count}, drawn invisible {unseen}); saved drawn {drawn}, on the ground at their place {ground} (worst {worstOff:F2} m off, {worstY:F2} m off the ground), >= 5 m from the wreck {clear}, the least walked when ordered {moved:F1} m; " +
                  $"figures left on the ramp / held {ghostsLeft}; the ship broken up {broken}, deselected {desel}; landing voices after the kill {voices}; walks on to their robots {BoardingView.DoorwayWalks - b.walks}", view);
        }

        /// <summary>K420: a saved robot counts as on its rules place within this (m, flat): the view's stand hold is 0.12 m.</summary>
        const float OnPlaceM = 0.15f;
        /// <summary>K420: every saved robot of that ship's rows (after b) drawn within OnPlaceM of its rules place.</summary>
        bool SavedOnPlace(ZObject s, Before b)
        {
            foreach (var r in W.ShuttleDoorwayRows)
            {
                if (r.shipId != s.Id || r.seq <= b.dseq || !r.saved) continue;
                var o = W.Get(r.robotId); if (!Alive(o) || !g.Views.TryGetValue(o.Id, out var v) || !v) continue;
                var want = ZGame.PxToWorld(o.CenterX, o.CenterY); var p = v.transform.position;
                if (new Vector2(p.x - want.x, p.z - want.z).magnitude > OnPlaceM) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ the cases
        IEnumerator P1()
        {
            var s = W.PlayableShuttle; if (!Alive(s)) { s = Ship(me); }
            var sq = Squad(s); if (sq == null) { Check("P1 a squad beside the ship", false); yield break; }
            yield return Board(s, sq);
            Pad(s, 45f, 80f, out int x, out int y); Move(s, x, y); Cam(s);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Climb && s.ShuttleLift >= 0.5, 20f);
            var b = Mark(s); Kill(s);
            yield return After("P1_takeoff", s, b, 4, 0, true);
        }

        IEnumerator Landing(string tag, bool rough, System.Func<ZObject, bool> when, int wantLost, bool wantAir)
        {
            var s = Ship(me); if (s == null) { Check(tag + " a ship dropped", false); yield break; }
            var sq = Squad(s); yield return Board(s, sq);
            Pad(s, 30f, 60f, out int x, out int y);
            ZWorld.ShuttleRoughProofNext = rough;
            Move(s, x, y); Cam(s);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 40f);
            Cam(s);
            var b = Mark(s); killedNow = false; killShip = s; killWhen = () => { if (!when(s)) return false; b = Mark(s); return true; };   // the marks of the very frame it dies
            yield return WaitFor(() => killedNow, 12f);
            if (!killedNow) { killWhen = null; Check(tag + " the moment came", false); yield break; }
            b.air = s.ShuttleAirborne; b.sx = s.CenterX; b.sy = s.CenterY;
            yield return After(tag, s, b, wantLost, 0, wantAir);
            ZWorld.ShuttleRoughProofNext = false;
        }

        IEnumerator P4()
        {
            var s = Ship(me); var sq = s != null ? Squad(s) : null;
            if (sq == null) { Check("P4 a ship and a squad", false); yield break; }
            Cam(s); Enter(sq, s);
            yield return WaitFor(() => s.Passengers.Count > 0, 60f);
            double tk = s.ShuttleFootTimes[1] + ZWorld.ShuttleBoardDoorSec + 0.02;
            yield return WaitFor(() => W.Time >= tk, 10f);
            var b = Mark(s); Kill(s);
            yield return After("P4_boarding", s, b, 2, 2, false);
        }

        IEnumerator P5()
        {
            var s = Ship(me); var sq = s != null ? Squad(s) : null;
            if (sq == null) { Check("P5 a ship and a squad", false); yield break; }
            yield return Board(s, sq); Cam(s);
            W.Eject(me, s);
            yield return WaitFor(() => s.Passengers.Count == 0 && s.ShuttleLastRampUse == ZShuttleRampUse.Unloaded, 10f);
            double tk = s.ShuttleFootTimes[1] - ZWorld.ShuttleRampOutSec + ZWorld.ShuttleExitDoorSec + 0.02;
            yield return WaitFor(() => W.Time >= tk, 10f);
            var b = Mark(s); Kill(s);
            yield return After("P5_unloading", s, b, 2, 2, false);
        }

        IEnumerator P6()
        {
            ZObject brad = null; foreach (var o in W.Objects) if (o.IsHero && o.HeroMemberName == "brad" && Alive(o) && o.Owner == me) { brad = o; break; }
            if (brad == null) { Check("P6 ALAN & BRAD on the map (the level's hero squad)", false); yield break; }
            var s = Ship(Team.Neutral); if (s == null) { Check("P6 an empty ship dropped", false); yield break; }
            Cam(s); Enter(brad, s);
            yield return WaitFor(() => s.Drivers.Count > 0, 90f);
            if (s.ShuttleFootTimes.Count < 2) { Check("P6 both heroes in the ramp's file", false); yield break; }
            double tk = s.ShuttleFootTimes[0] + ZWorld.ShuttleBoardDoorSec + 0.02;
            if (tk >= s.ShuttleFootTimes[1] + ZWorld.ShuttleBoardDoorSec) { Check("P6 Alan behind Brad in the file", false); yield break; }
            yield return WaitFor(() => W.Time >= tk, 10f);
            var b = Mark(s); Kill(s);
            yield return After("P6_heroes", s, b, 1, 1, false);
            ZObject alan = null; foreach (var o in W.Objects) if (o.IsHero && o.HeroMemberName == "alan" && Alive(o)) alan = o;
            Check($"P6 Alan saved as himself (a hero, {(alan != null ? alan.Health + " HP" : "NOT FOUND")}), Brad's row is a hero's (the view threw Brad's own model)", alan != null);
        }

        IEnumerator P7()
        {
            var s = Ship(me); var sq = s != null ? Squad(s) : null;
            if (sq == null) { Check("P7 a ship and a squad", false); yield break; }
            yield return Board(s, sq);
            var ship = OzPlayableShuttle.Of(s);
            Pad(s, 60f, 110f, out int x, out int y);
            input.Selection.Clear(); input.Selection.Add(s);
            var gp = ZGame.PxToWorld(x, y); var cam = Camera.main; var scr = cam ? (Vector2)cam.WorldToScreenPoint(gp) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            // the double right-click as PlayerInput gives it: the order, then the click, twice within the double-click time
            Move(s, x, y); OzShuttleKickflip.OrderClick(input.Selection, scr, gp, false, false);
            yield return null; yield return null;
            Move(s, x, y); OzShuttleKickflip.OrderClick(input.Selection, scr, gp, false, false);
            Cam(s);
            bool flipping = false;
            yield return WaitFor(() => { if (ship) Cam(s); return ship && ship.Kickflip.Phase == OzKickflipPhase.Flip && ship.Kickflip.T >= 0.4 * OzKickflipCurve.TotalSec; }, 25f);
            yield return new WaitForEndOfFrame();                                                          // K420: the flipped pose is on only from the view's LateUpdate to the gate (-47) of the next frame
            int baked0 = ship ? ship.KickBaked : 0, late0 = ship ? ship.KickDeathBakes : 0;                         // K420: the kill below comes here too, as the rules' tick's would (before the gate)
            flipping = ship && ship.Kickflip.Phase == OzKickflipPhase.Flip && ship.KickDrawn;
            Check($"P7 the kickflip was being drawn at the kill ({(ship ? ship.Kickflip.Phase.ToString() : "-")}, {(ship ? ship.Kickflip.T : 0):F2} s of {OzKickflipCurve.TotalSec:F2})", flipping);
            input.Selection.Clear();
            var b = Mark(s); Kill(s);
            yield return After("P7_kickflip", s, b, 4, 0, true);
            Check($"P7 the flip ended with the ship (drawn {(ship ? ship.KickDrawn : false)}, phase {(ship ? ship.Kickflip.Phase.ToString() : "-")})", ship == null || (!ship.KickDrawn && ship.Kickflip.Phase == OzKickflipPhase.Idle));
            Check($"P7 the wreck started from the flipped pose on screen (flips kept at the death: {(ship ? ship.KickBaked - baked0 : 0)}, of them by the late-kill path {(ship ? ship.KickDeathBakes - late0 : 0)})", ship == null || ship.KickBaked > baked0);   // K420
        }

        IEnumerator P8()
        {   // K420: staged (run 28: the squad was not aboard, the ship never reached the deck; units built later fired): the fire held every
            // 0.25 s, the ship dropped near the station, the squad checked aboard, the kill only when the ship turns over the deck
            ZObject st = null;
            foreach (var f in W.Flags) foreach (var bld in f.LinkedObjects) if (bld.Type == "repair" && st == null) { st = bld; if (bld.Owner != me) W.AwardZone(f, me); }
            if (st == null) { Check("P8 a repair station on level 16", false); yield break; }
            OzProofHoldFire.Attach(gameObject, W, null, me, false, true, true, Log);
            ZObject s = null; float dist = -1f;
            for (int r = 24; r <= 48 && s == null; r += 6)
                for (int a = 0; a < 12 && s == null; a++)
                {
                    float an = a * Mathf.PI / 6f;
                    var c = W.ShuttleClamp(st.CenterX + (int)(Mathf.Cos(an) * r * 8f), st.CenterY + (int)(Mathf.Sin(an) * r * 8f));
                    var o = W.SpawnShuttle(c.x, c.y, me, -90f, out _);
                    if (o == null) continue;
                    OzPlayableShuttle.SpawnView(g, o); s = o; dist = new Vector2(o.CenterX - st.CenterX, o.CenterY - st.CenterY).magnitude / 8f;   // the drop's own landing spot rule keeps it off the station
                }
            if (s == null) { Check("P8 a ship dropped 24 .. 48 m from the station", false); yield break; }
            for (int tries = 0; tries < 2 && s.Passengers.Count < 4; tries++)
            {
                var sq = Squad(s); if (sq == null) break;
                yield return Board(s, sq);
            }
            Log($"P8 staging (K420): ship #{s.Id} {dist:F1} m from the station #{st.Id}; the squad aboard {s.Passengers.Count} (want 4)");
            if (s.Passengers.Count != 4) { Check($"P8 the squad aboard before the repair order ({s.Passengers.Count} of 4)", false); yield break; }
            s.SetHealth(s.MaxHealth / 2);
            bool sent = W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = st.Id, x = st.CenterX, y = st.CenterY, playerGiven = true } });
            yield return WaitFor(() => { Cam(s); return !Alive(s) || (s.ShuttleDeckTurning && s.ShuttleCraneCallAt >= 0 && W.Time < s.ShuttleCraneClearAt - 0.4); }, 90f);
            bool stage = Alive(s) && s.ShuttleDeckTurning && s.ShuttleCraneCallAt >= 0;
            Log($"P8 staging (K420): repair order taken {sent}; now: state {s.ShuttleState}, airborne {s.ShuttleAirborne}, deck turning {s.ShuttleDeckTurning}, crane call {s.ShuttleCraneCallAt:F2} (clear at {s.ShuttleCraneClearAt:F2}, now {W.Time:F2}), aboard {s.Passengers.Count}, alive {Alive(s)}");
            if (!stage) { Check("P8 the ship turning over the station's deck with the squad aboard (the stage came)", false); yield break; }
            int backs0 = OzStationCrane.Backs;
            var b = Mark(s); Kill(s);
            yield return After("P8_repair_station", s, b, 4, 0, true);
            yield return WaitFor(() => !W.ShuttleCraneAway(st.Id), 10f);
            yield return Wait((float)ZWorld.ShuttleCraneMoveSec + 0.5f);
            Check($"P8 the crane rolled back home after the wreck's {ZWorld.ShuttleDeckWreckSec:F0} s (the rules' call ended {!W.ShuttleCraneAway(st.Id)}, rolls back {OzStationCrane.Backs - backs0}), the bay free ({st.RepairOccupantId < 0})", !W.ShuttleCraneAway(st.Id) && OzStationCrane.Backs > backs0 && st.RepairOccupantId < 0);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || bv == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || !ZWorld.ShuttleOn) { Log("no playable shuttle (level 16, the option SHUTTLE on, not 'Zod exact')"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttleDoorwayDeviation) { Log("the rule DV126 is off (ZWorld.ShuttleDoorwayDeviation)"); fails++; Finish(); yield break; }
            me = g.player;
            int bots = W.Bots.Count; W.Bots.Clear(); HoldFire();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            yield return WaitFor(() => OzShuttleBreakup.PlanDone, 30f);
            errors = 0;
            Log($"K418 death edge-case proof (DV126), {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; CPU removed {bots}, the other teams hold their fire ({heldFire.Count}); the doorway {ZWorld.ShuttleBoardDoorSec:F1} s after the foot walking in, {ZWorld.ShuttleExitDoorSec:F1} s after the seat walking out");
            yield return P1();
            yield return Landing("P2_landing", false, s => s.ShuttleState == ZShuttleState.Descend && s.ShuttleLift <= 0.3, 4, true);
            int v0 = OzPlayableShuttle.LandingRobot + OzPlayableShuttle.LandingRachel;
            yield return Landing("P2b_touchdown", false, s => s.ShuttleState == ZShuttleState.Landed, 4, false);
            Check($"P2b destroyed in the frame it touched down: no landing voice ({OzPlayableShuttle.LandingRobot + OzPlayableShuttle.LandingRachel - v0} landing voices; the robot's \"Touchdown!\" and Rachel's line belong to a landing that lived)", OzPlayableShuttle.LandingRobot + OzPlayableShuttle.LandingRachel == v0);
            yield return Landing("P3_crash_slam", true, s => s.ShuttleState == ZShuttleState.Landed && W.Time >= s.ShuttleTouchdownAt + 0.1, 4, false);
            yield return Landing("P3b_crash_righting", true, s => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelStartAt > 0 && W.Time >= s.ShuttleLevelStartAt + 0.5 * ZWorld.ShuttleRoughLevelSec, 4, false);
            yield return P4();
            yield return P5();
            yield return P6();
            yield return P7();
            yield return P8();
            yield return Wait(1f);
            Check($"the console during the whole run: {errors} errors / exceptions", errors == 0);
            Finish();
        }

        void Finish()
        {
            killWhen = null;
            Log($"K418 death edge-case proof: {passes} PASS, {fails} FAIL (expected 27 PASS)");
            Report = $"{passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "death_edge_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleDoorwayProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
