#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K444 Play proof: level 17's own shuttles (rule DV134, Sim/ZWorld.L17Shuttles.cs), on level 17 in Play (menu OpenZ/V1.1/Shuttle: K444
    /// Level 17 Shuttles Proof, Editor/OzL17ShuttleMenu.cs). Start level 17 from the battlefield screen on EASY or MEDIUM with the SHUTTLE box
    /// ON (both ships), or on HARD / with the box off (the car-park ship only: the fort stages are then skipped and say so). Counted PASS /
    /// FAIL lines and full-screen pictures from the normal RTS camera at 70 m:
    ///  P0 the rule and the box as the level started (the fort ship wanted = level 17, EASY / MEDIUM, the box on);
    ///  P1 the empty car-park ship on its spot, drawn, dark (no pilot); the fort ship (when wanted) on its spot, drawn, lit, the level's
    ///     playable shuttle; nothing else of the rules inside either outline + 1 m (ZWorld.ShuttleUnitsInside);
    ///  P2 no drawn prop inside either ship: every renderer of the scene (lamps, bollards, boulders, sandbags, details, the night lamps;
    ///     not the ships, not the units, not the ground: flat or wider than 12 m) whose box comes within 0.3 m of the outline; the nearest
    ///     one is logged;
    ///  P3 pictures of both ships where they stand, at the level's own light and in morning light;
    ///  P4 a red squad of 4 ordered into the empty ship boards it: the ship turns red and its lights come on; the CPU teams are removed for
    ///     the rest of the proof (headless check E proves the CPU never takes it);
    ///  P5 both ships fly at the same time on crossing routes (the car-park ship to a free spot south-east of the fort, the fort ship to a
    ///     free spot in the car park's north): both land, no hover, their middles never closer than the traffic rule's separation (K338);
    ///  P6 EXIT: the 3 passengers come out of the car-park ship.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/l17_shuttles_k444, found upward at run time). Test only: editor only
    /// (this file compiles to nothing in a player build).
    /// </summary>
    public sealed class OzL17ShuttleProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "l17_shuttles_k444");
            return System.IO.Path.Combine(Application.persistentDataPath, "l17_shuttles_k444");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzL17ShuttleProof").AddComponent<OzL17ShuttleProof>();
        }

        ZGame g; ZWorld W; RtsCamera rts; Team me = Team.Red;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzL17ShuttleProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        static string F(double v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        static bool AtSpot(ZObject s, int[] spot) => s != null && s.CenterX == spot[0] * 16 + 8 && s.CenterY == spot[1] * 16 + 8 && Mathf.Abs((float)ZObject.ShuttleNormDeg(s.ShuttleHeadingDeg - spot[2])) < 0.01f;
        static string Where(ZObject s) => s == null ? "none" : $"#{s.Id} {s.Owner} tile {s.CenterX >> 4},{s.CenterY >> 4} heading {s.ShuttleHeadingDeg:0}, pilots {s.Drivers.Count}, aboard {s.Passengers.Count}";

        /// <summary>P2: renderers of the scene whose box comes within 0.3 m of the ship's outline (not the ship's own, not a unit's, not the ground).</summary>
        int PropsInside(ZObject s, out string nearest, out string names)
        {
            int n = 0; float best = 99f; nearest = "none"; var nm = new List<string>();
            var c = g.CenterWorldSmooth(s); float gy = g.GroundY(c.x, c.z);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (OzPlayableShuttle.OfRenderer(r) || r.GetComponentInParent<UnitView>()) continue;
                var b = r.bounds;
                if (b.size.x > 12f || b.size.z > 12f || b.max.y < gy + 0.15f || b.min.y > gy + 6f) continue;
                if (Mathf.Abs(b.center.x - c.x) > 14f || Mathf.Abs(b.center.z - c.z) > 14f) continue;
                float gmin = 99f;
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                    {
                        float x = Mathf.Lerp(b.min.x, b.max.x, i / 4f), z = Mathf.Lerp(b.min.z, b.max.z, j / 4f);
                        gmin = Mathf.Min(gmin, (float)s.ShuttleOutlineGapM(x * 8.0, -z * 8.0));
                    }
                if (gmin < best) { best = gmin; nearest = r.name + " " + F(gmin) + " m"; }
                if (gmin < 0.3f) { n++; if (nm.Count < 6) nm.Add(r.name + " (" + F(gmin) + " m)"); }
            }
            names = string.Join(", ", nm);
            return n;
        }

        (int x, int y) FreeSpot(ZObject s, int tx, int ty)
        {
            for (int r = 0; r <= 12; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        int x = (tx + dx) * 16 + 8, y = (ty + dy) * 16 + 8;
                        if (W.ShuttlePadFree(x, y, s, out _)) return (x, y);
                    }
            return (-1, -1);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || g.level != "L17") { Log("no level 17 in Play"); fails++; Finish(); yield break; }
            me = g.player;
            var d = g.gameDifficulty; bool box = OzShuttleStart.On, wantFort = OzShuttleStart.L17Start(g.level, d, box) && ZWorld.L17ShuttlesDeviation && ZWorld.ShuttlePlayableDeviation;
            Log($"K444 level 17 shuttles proof, {Screen.width}x{Screen.height}, {OpenZDifficulty.Label(d)}, the box {(box ? "ON" : "OFF")}, world time {F(W.Time)} s; rule {ZWorld.L17ShuttlesDeviation}, fort ship asked {ZWorld.L17StartShuttle}");
            var cp = W.L17CarParkShuttle; var fs = W.L17FortShuttle;
            Check($"P0 the rule on and the box's answer as the level started (fort ship wanted {wantFort}, asked {ZWorld.L17StartShuttle})", ZWorld.L17ShuttlesDeviation && ZWorld.L17StartShuttle == wantFort);
            // ---- P1
            var cv = OzPlayableShuttle.Of(cp); var cl = cv ? cv.GetComponent<OzShuttleEmptyLook>() : null;
            Check($"P1 the empty car-park ship on its spot, drawn and dark: {Where(cp)}, view {(cv ? cv.name : "NONE")}, dark {(cl ? cl.IsDark.ToString() : "no look")}",
                  cp != null && cp.ShuttleIsEmpty && AtSpot(cp, ZWorld.L17CarParkShuttleSpot) && cv && cl && cl.IsDark);
            if (wantFort)
            {
                var fv = OzPlayableShuttle.Of(fs); var fl = fv ? fv.GetComponent<OzShuttleEmptyLook>() : null;
                Check($"P1 the fort ship on its spot, drawn and lit, the playable one: {Where(fs)}, view {(fv ? fv.name : "NONE")}, dark {(fl ? fl.IsDark.ToString() : "no look")}, playable {(W.PlayableShuttle != null ? "#" + W.PlayableShuttle.Id : "none")}",
                      fs != null && fs.Owner == me && fs.Drivers.Count == 1 && AtSpot(fs, ZWorld.L17FortShuttleSpot) && fv && !(fl && fl.IsDark) && W.PlayableShuttle == fs);
            }
            else Check($"P1 no fort ship ({OpenZDifficulty.Label(d)}, the box {(box ? "ON" : "OFF")}): {Where(fs)}", fs == null);
            foreach (var s in new[] { cp, fs })
            {
                if (s == null) continue;
                int inside = W.ShuttleUnitsInside(s, 1.0);
                int props = PropsInside(s, out string nearest, out string names);
                Check($"P1 nothing of the rules within 1 m of the {(s == cp ? "car-park" : "fort")} ship's outline ({inside})", inside == 0);
                Check($"P2 no drawn prop within 0.3 m of the {(s == cp ? "car-park" : "fort")} ship's outline ({props}{(props > 0 ? ": " + names : "")}; the nearest: {nearest})", props == 0);
            }
            // ---- P3 pictures
            foreach (var s in new[] { cp, fs })
            {
                if (s == null) continue;
                Cam(g.CenterWorldSmooth(s), 70f); yield return Wait(1.2f);
                Shot($"K444_P3_{(s == cp ? "carpark" : "fort")}_level_light_h70");
            }
            var dn = FindFirstObjectByType<DayNight>(); float t0 = dn ? dn.timeOfDay : 0f; bool run0 = dn && dn.running;
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            foreach (var s in new[] { cp, fs })
            {
                if (s == null) continue;
                Cam(g.CenterWorldSmooth(s), 70f); yield return Wait(1.2f);
                Shot($"K444_P3_{(s == cp ? "carpark" : "fort")}_morning_h70");
            }
            if (cp == null) { Finish(); yield break; }
            // ---- P4 the player takes the empty one
            int bots = W.Bots.Count; W.Bots.Clear(); Log($"set-up: CPU removed ({bots}), morning 0.10 held");
            var (bx, by) = cp.ShuttleBackPoint(6);
            var lead = W.CreateRobotGroup("grunt", bx - 8, by - 8, me, 4);
            Cam(g.CenterWorldSmooth(cp), 55f); yield return Wait(0.5f);
            bool sent = W.SendWaypoints(me, lead, new[] { new ZWaypoint { mode = WpMode.Enter, refId = cp.Id, x = cp.CenterX, y = cp.CenterY, playerGiven = true } });
            yield return WaitFor(() => cp.Owner == me && cp.Passengers.Count == 3 && cp.ShuttleRampPhase == ZShuttleRamp.Shut && W.Time >= cp.ShuttleRampHoldUntil, 60f);
            yield return Wait(2.0f);
            var look = OzPlayableShuttle.Of(cp) ? OzPlayableShuttle.Of(cp).GetComponent<OzShuttleEmptyLook>() : null;
            Check($"P4 a red squad of 4 ordered in took it: ordered {sent}, {Where(cp)}, lit {(look ? (!look.IsDark).ToString() : "no look")}", sent && cp.Owner == me && cp.Drivers.Count == 1 && cp.Passengers.Count == 3 && look && !look.IsDark);
            Shot("K444_P4_carpark_taken_h55");
            // ---- P5 both fly at once on crossing routes
            var (gx, gy) = FreeSpot(cp, 95, 55);
            (int x, int y) fgo = fs != null ? FreeSpot(fs, 72, 88) : (-1, -1);
            int hc = cp.ShuttleHovers, hf = fs != null ? fs.ShuttleHovers : 0, flc = cp.ShuttleFlights, flf = fs != null ? fs.ShuttleFlights : 0;
            bool o1 = gx > 0 && W.SendWaypoints(me, cp, new[] { new ZWaypoint { mode = WpMode.Move, x = gx, y = gy, playerGiven = true } });
            bool o2 = fs == null || (fgo.x > 0 && W.SendWaypoints(me, fs, new[] { new ZWaypoint { mode = WpMode.Move, x = fgo.x, y = fgo.y, playerGiven = true } }));
            float minSep = 999f, t = 0f; bool shotMid = false;
            while (t < 150f)
            {
                bool cDone = cp.ShuttleFlights > flc && cp.ShuttleState == ZShuttleState.Landed && cp.ShuttleLift <= 0;
                bool fDone = fs == null || (fs.ShuttleFlights > flf && fs.ShuttleState == ZShuttleState.Landed && fs.ShuttleLift <= 0);
                if (cDone && fDone) break;
                if (fs != null && cp.ShuttleAirborne && fs.ShuttleAirborne) minSep = Mathf.Min(minSep, Mathf.Sqrt(Mathf.Pow(cp.CenterX - fs.CenterX, 2) + Mathf.Pow(cp.CenterY - fs.CenterY, 2)) / 8f);
                Cam(g.CenterWorldSmooth(cp), 70f);
                if (!shotMid && cp.ShuttleState == ZShuttleState.Cruise && t > 4f) { Shot("K444_P5_carpark_ship_flying_h70"); shotMid = true; }
                t += Time.deltaTime; yield return null;
            }
            yield return Wait(2f);
            bool landedC = cp.ShuttleState == ZShuttleState.Landed && cp.ShuttleHovers == hc && Mathf.Abs(cp.CenterX - gx) <= 16 && Mathf.Abs(cp.CenterY - gy) <= 16;
            bool landedF = fs == null || (fs.ShuttleState == ZShuttleState.Landed && fs.ShuttleHovers == hf);
            Check($"P5 both flew and landed (car-park ship to tile {gx >> 4},{gy >> 4}: {cp.ShuttleState}, hovers {cp.ShuttleHovers - hc}; fort ship {(fs == null ? "not there" : "to tile " + (fgo.x >> 4) + "," + (fgo.y >> 4) + ": " + fs.ShuttleState + ", hovers " + (fs.ShuttleHovers - hf))}; orders {o1} / {o2})", o1 && o2 && landedC && landedF);
            if (fs != null) Check($"P5 while both were up their middles stayed {F(minSep)} m apart or more (the traffic rule keeps {F(ZWorld.ShuttleAirSepM)} m; 0.5 m slack for the strafe)", minSep >= ZWorld.ShuttleAirSepM - 0.5f || minSep > 900f);
            Cam(g.CenterWorldSmooth(cp), 70f); yield return Wait(1f); Shot("K444_P5_carpark_ship_landed_h70");
            // ---- P6 EXIT
            yield return WaitFor(() => cp.ShuttleLevelled && W.Time >= cp.ShuttleRampHoldUntil, 8f);
            int before = W.Objects.Count(o => o.IsRobot && o.Owner == me && !o.Destroyed && o.KillAt < 0);
            bool ej = W.Eject(me, cp);
            yield return WaitFor(() => cp.Passengers.Count == 0, 20f); yield return Wait(4f);
            int after = W.Objects.Count(o => o.IsRobot && o.Owner == me && !o.Destroyed && o.KillAt < 0);
            Check($"P6 EXIT: {after - before} robots came out (want 3), aboard {cp.Passengers.Count}", ej && after - before == 3 && cp.Passengers.Count == 0);
            Shot("K444_P6_squad_out_h70");
            if (dn) { dn.timeOfDay = t0; dn.Apply(t0); dn.running = run0; }
            Finish();
        }

        void Finish()
        {
            Report = $"K444 L17 SHUTTLES PROOF: {passes} PASS {fails} FAIL";
            Log(Report);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "l17_shuttles_proof_log.txt"), sb.ToString());
            Destroy(gameObject, 1f);
        }
    }
}
#endif
