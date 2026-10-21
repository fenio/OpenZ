using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256e Play proof of the playable shuttle's flight look (level 16, Play; test only). Every picture is full screen from the normal
    /// RTS camera at the standard 70 m, the camera FIXED for every strip (never following the ship), the subject in the middle:
    /// <list type="bullet">
    /// <item>S6 the heroes' camp (first, at the level start, while the two heroes lie on their loungers): the ship flies over the camp
    /// and then hovers over it (it cannot land on them) for 6 s; every frame each hero's reason to get up is read (an order, a move, a
    /// hit, a threat, a missile near: the antic director's own words); the line names the reason if they get up.</item>
    /// <item>M11 the hover: a 3 s strip (10 frames, 0.3 s) from a low point of the bob: the hull's rise and sink in px (>= 15); the bob
    /// over 10 s (0.55-0.75 m); the shadow's breathing with it. Crossings, each a fixed-camera strip of 12 frames: over the own fort's
    /// roof, over a cliff or hill, over a kapok cell: the smallest gap (>= 1.0 m), the fastest climb (<= 4.05 m/s), the rise before and
    /// the sink after; 05b retaken with the ship over the kapok in the middle of the screen, clear of the unit panel.</item>
    /// <item>S1 the bank: flying north, a 90 degree re-order: 10 frames through the turn (level, in, peak, out, level); the peak (12-20.5
    /// deg) and its fastest change (<= 30 deg/s); one frame of a turn on the spot (level, <= 2 deg).</item>
    /// <item>M19 the easing: a take-off from open grass and a flight 45 m east under a fixed camera: frames every 0.25 s from the lift-off
    /// to the cruise and from the start of the braking to the touchdown (the ship's screen place per frame: the spacing grows, then
    /// shrinks); three pictures: level, the peak nose-down, the peak nose-up.</item>
    /// <item>M18 the strafe: hovering facing north (a road edge in view), enemy rockets from ahead: 14 frames over 7 s (out, hold, back)
    /// and each glide's metres, seconds and peak sideways speed (OzPlayableShuttle.GlideLog).</item>
    /// <item>M9 the crash landing, once on each side (the rules' side of the next landing is known from its number), on open grass; the
    /// second one with an own squad standing near (they flinch): 16 frames over 4 s from just before the contact, a tilted / levelled
    /// pair (the high pads' px over the ground: >= 15), the crash's numbers, "Coming in hot!" before the contact and no landing line.</item>
    /// </list>
    /// Output: <see cref="Folder"/>: the pictures, frames.tsv (every picture: its time, the ship's screen place and numbers),
    /// proof_log.txt, sound_log.txt (the whole take, flushed every 2 s: no 2000-line cap). The strips and onion-skin sheets are made
    /// from them by the kit's tools/e_flight_strips.py. Report: <see cref="Report"/>.
    /// </summary>
    public sealed class OzShuttleFlightProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string OutDir = "";

        /// <summary>The output folder: the work tree's records found upward from the project (milestones/k256e_flight_look_proof, or
        /// OutDir there), else the player's own data folder.</summary>
        public static string Folder()
        {
            string sub = string.IsNullOrEmpty(OutDir) ? "k256e_flight_look_proof" : OutDir;
            if (System.IO.Path.IsPathRooted(sub)) return sub;
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }

        /// <summary>mode: "E" every stage (S6 first: start right after the level start), or one of "S6", "M11", "S1", "M19", "M18", "M9".</summary>
        public static void Run(string mode = "E")
        {
            Report = "running " + mode;
            var go = new GameObject("OzShuttleFlightProof"); go.AddComponent<OzShuttleFlightProof>().mode = mode;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256e Flight Look Proof (Level 16, Play, right after the level start)")]
        static void MenuAll() => FromMenu("E");
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256e Flight Look Proof, crash landings only (Level 16, Play)")]
        static void MenuCrash() => FromMenu("M9");
        static void FromMenu(string m)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256e flight look proof: enter Play on level 16 first"); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
            OutDir = ""; Run(m);
            Debug.Log("[OpenZ] K256e flight look proof " + m + " started; its report: OzShuttleFlightProof.Report, the logs in " + Folder());
        }
#endif

        string mode = "E";
        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder(), frames = new StringBuilder();
        int fails, passes, shots; string dir; float T0, lastFlush, flushT; bool camFixed; Vector3 camAt;
        readonly Queue<string> shotQ = new Queue<string>(); int lastShotFrame = -1;
        readonly List<ZObject> made = new List<ZObject>();
        string soundPath;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            soundPath = System.IO.Path.Combine(dir, "sound_log.txt");
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleFlightProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }

        /// <summary>A picture this frame or the next free one (one capture a frame); its line in frames.tsv is written when it is taken.</summary>
        void Shot(string name) => shotQ.Enqueue(name);
        void TakeShot(string name)
        {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); shots++; lastShotFrame = Time.frameCount;
            var cam = Camera.main; Vector3 hull = Vector3.zero, root = Vector3.zero, gnd = Vector3.zero;
            if (cam && ship && !ship.BrokenUp)
            {
                hull = cam.WorldToScreenPoint(ship.transform.TransformPoint(new Vector3(0f, 2.6f, 0f)));
                root = cam.WorldToScreenPoint(ship.transform.position);
                var p = ship.transform.position; gnd = cam.WorldToScreenPoint(new Vector3(p.x, g.GroundY(p.x, p.z), p.z));
            }
            string nums = ship ? $"{ship.FeetHeight:F2}\t{ship.BobNow:F3}\t{ship.BankDeg:F2}\t{ship.PitchNowDeg:F2}\t{(s != null ? s.ShuttleSpeedPx / 8.0 : 0):F2}\t{(s != null ? s.ShuttleStrafePx / 8.0 : 0):F2}\t{ship.CrashRollNow:F2}\t{ship.CrashT:F2}" : "";
            float camH = rts ? rts.height : -1f, camD = cam && ship ? Vector3.Distance(cam.transform.position, ship.transform.position) : -1f;
            frames.AppendLine($"{name}\t{Time.unscaledTime - T0:F3}\t{(W != null ? W.Time : 0):F3}\t{hull.x:F1}\t{Screen.height - hull.y:F1}\t{root.x:F1}\t{Screen.height - root.y:F1}\t{gnd.x:F1}\t{Screen.height - gnd.y:F1}\t{nums}\t{camH:F1}\t{camD:F1}");
            Log($"frame {name}.png (hull at {hull.x:F0}, {Screen.height - hull.y:F0} px)");
        }

        /// <summary>The picture's close twin (rule 2: an h70 picture is told from its close twin): after the h70 one is taken, the same view
        /// at 30 m for one picture named *_extra_close, then back to 70 m (frames.tsv also logs every picture's camera height).</summary>
        IEnumerator CloseTwin(string name70)
        {
            while (shotQ.Count > 0) yield return null;
            yield return null; yield return null;
            if (!rts) yield break;
            rts.SetHeightImmediate(30f); yield return null; yield return null;
            Shot(name70.Replace("_h70", "") + "_extra_close");
            while (shotQ.Count > 0) yield return null;
            yield return null; yield return null;
            rts.SetHeightImmediate(70f); yield return null;
        }

        /// <summary>The camera fixed on a ground point (x, z) for a strip; null = it follows the ship (between strips, to find it again).</summary>
        void Fix(Vector3? at) { camFixed = at.HasValue; if (at.HasValue) camAt = at.Value; }

        void Update()
        {
            if (shotQ.Count > 0 && Time.frameCount != lastShotFrame) TakeShot(shotQ.Dequeue());
            if (rts)
            {
                if (camFixed) rts.focus = new Vector3(camAt.x, 0f, camAt.z);
                else if (ship && !ship.BrokenUp) { var p = ship.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); }
            }
            if ((flushT += Time.unscaledDeltaTime) >= 2f) { flushT = 0f; Flush(); }
        }

        /// <summary>The whole take's sound log: every entry since the last flush appended to the file (no cap).</summary>
        void Flush()
        {
            var es = OzSoundLog.Since(lastFlush); if (es.Count == 0) return;
            var t = new StringBuilder();
            foreach (var e in es) { t.AppendLine($"  {e.t - T0,7:F2} {e.src} {e.key} {e.clip} {e.outcome} vol {e.vol:F2} dist {e.dist:F1}"); soundKeys.Add((e.t, e.key, e.outcome)); }
            lastFlush = es[es.Count - 1].t + 1e-4f;
            try { System.IO.File.AppendAllText(soundPath, t.ToString()); } catch { }
        }
        readonly List<(float t, string key, string outcome)> soundKeys = new List<(float, string, string)>();
        bool Played(string key, float from, float to) { Flush(); foreach (var e in soundKeys) if (e.key == key && e.t >= from && e.t <= to && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("SAY"))) return true; return false; }
        float FirstPlay(string key, float from) { Flush(); foreach (var e in soundKeys) if (e.key == key && e.t >= from && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("SAY"))) return e.t; return -1f; }
        int Asked(string key, float from) { Flush(); int n = 0; foreach (var e in soundKeys) if (e.key == key && e.t >= from) n++; return n; }

        // ------------------------------------------------------------------ the rules' helpers
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool MoveW(Vector3 w) { var q = W.ShuttleClamp(Mathf.RoundToInt(w.x * 8f), Mathf.RoundToInt(-w.z * 8f)); return Move(q.x, q.y); }
        static Vector3 PxW(int x, int y) => ZGame.PxToWorld(x, y);
        Vector3 ShipG() { var p = ship.transform.position; return new Vector3(p.x, 0f, p.z); }
        ZObject Squad(string type, Vector3 at, Team t)
        {
            int tx = Mathf.FloorToInt(at.x * 8f) >> 4, ty = Mathf.FloorToInt(-at.z * 8f) >> 4;
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            made.Add(o); return o;
        }
        void Attack(ZObject o) { if (o != null && !o.Destroyed && s != null) W.SendWaypoints(them, o, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } }); }
        void Clear()
        {
            foreach (var o in made)
            {
                if (o == null) continue;
                var all = new List<ZObject> { o }; all.AddRange(o.Minions);
                foreach (var x in all) { x.KillAt = 0; if (g.Views.TryGetValue(x.Id, out var v)) { g.Views.Remove(x.Id); if (v) Destroy(v.gameObject); } }
            }
            made.Clear();
        }
        void Heal() { if (s != null && !s.Destroyed && s.Health < s.MaxHealth) s.SetHealth(s.MaxHealth); }
        /// <summary>The tallest plant within r of (x, z) (m), from a 4 m grid of the level's plants built once (the place finders ask it a lot).</summary>
        float PlantTallest(Vector3 c, float r)
        {
            if (plantGrid == null)
            {
                plantGrid = new Dictionary<long, float>(); var fw = OzFloraWind.I;
                if (fw != null) foreach (var p in fw.All)
                    {
                        if (p == null || !p.R || p.Gone) continue; var t = p.R.transform.position; long k = Cell(t.x, t.z);
                        float h = p.Height * Mathf.Abs(p.R.transform.lossyScale.y);
                        if (!plantGrid.TryGetValue(k, out float have) || h > have) plantGrid[k] = h;
                    }
            }
            float best = 0f; int r4 = Mathf.CeilToInt(r / 4f), cx = Mathf.FloorToInt(c.x / 4f), cz = Mathf.FloorToInt(c.z / 4f);
            for (int i = -r4; i <= r4; i++) for (int j = -r4; j <= r4; j++) if (plantGrid.TryGetValue(((long)(cx + i) << 32) ^ (uint)(cz + j), out float h) && h > best) best = h;
            return best;
        }
        Dictionary<long, float> plantGrid;
        static long Cell(float x, float z) => ((long)Mathf.FloorToInt(x / 4f) << 32) ^ (uint)Mathf.FloorToInt(z / 4f);
        /// <summary>A free pad at about r px from the ship toward angDeg (the old proof's way: rings outward, fanning to both sides).</summary>
        int[] Pad(int r, float angDeg, int span)
        {
            for (int dr = 0; dr <= 200; dr += 16)
                for (int k = 0; k <= span; k += 10)
                    foreach (int sg in new[] { 1, -1 })
                    {
                        double a = (angDeg + sg * k) * System.Math.PI / 180.0;
                        var q = W.ShuttleClamp(s.CenterX + (int)(System.Math.Cos(a) * (r + dr)), s.CenterY + (int)(System.Math.Sin(a) * (r + dr)));
                        if (W.ShuttlePadFree(q.x, q.y, s, out _)) return new[] { q.x, q.y };
                    }
            return null;
        }
        bool UnitsNear(Vector3 c, float r)
        {
            foreach (var o in W.Objects) if (o != s && !o.Destroyed && o.IsUnit && (PxW(o.CenterX, o.CenterY) - c).magnitude < r) return true;
            return false;
        }
        /// <summary>The nearest tile middle (within maxM of the ship, at least minM) that satisfies want(x px, y px, why the pad is not free).</summary>
        int[] Spot(System.Func<int, int, string, bool> want, float minM, float maxM)
        {
            long best = long.MaxValue; int[] r = null; long lo = (long)(minM * 8f) * (long)(minM * 8f), hi = (long)(maxM * 8f) * (long)(maxM * 8f);
            for (int ty = 6; ty < W.Map.height - 6; ty++)
                for (int tx = 6; tx < W.Map.width - 6; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - s.CenterX) * (x - s.CenterX) + (long)(y - s.CenterY) * (y - s.CenterY);
                    if (d >= best || d < lo || d > hi) continue;
                    W.ShuttlePadFree(x, y, s, out string why);
                    if (want(x, y, why ?? "")) { best = d; r = new[] { x, y }; }
                }
            return r;
        }
        bool OpenGrass(int x, int y, float r)
        {
            var c = PxW(x, y);
            return PlantTallest(c, r) < 2.5f && !UnitsNear(c, r + 3f) && !OpenZFxPlayer.IsWaterTile(c);
        }
        bool RoadNear(int x, int y, float r)
        {
            var tm = TrackMarks.I; if (!tm || tm.Surface == null) return false;
            var c = PxW(x, y);
            for (int k = 0; k < 16; k++) { float a = k * Mathf.PI / 8f; tm.Surface.Classes(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r, out float road, out _, out _); if (road > 0.5f) return true; }
            return false;
        }
        IEnumerator FlyTo(int[] q, float max)
        {
            if (q == null) yield break;
            Move(q[0], q[1]);
            yield return WaitFor(() => s.Destroyed || (s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed) && new Vector2(s.CenterX - q[0], s.CenterY - q[1]).magnitude < 24f && !ship.CrashOn, max);
        }
        /// <summary>The hull's sample points (the hull box corners, the two pods) over what stands under them: the smallest gap (m).</summary>
        float Gap()
        {
            var t = ship.transform; float gap = 999f;
            foreach (var x in new[] { -2.2f, 2.2f }) foreach (var z in new[] { -3.2f, 3.3f }) gap = Mathf.Min(gap, GapAt(t.TransformPoint(new Vector3(x, 0.6f, z))));
            gap = Mathf.Min(gap, GapAt(t.TransformPoint(new Vector3(2.47f, 1.1f, -0.1f)))); gap = Mathf.Min(gap, GapAt(t.TransformPoint(new Vector3(-2.47f, 1.1f, -0.1f))));
            return gap;
        }
        float GapAt(Vector3 q) { ship.Sky.Query(q.x, q.z, 0.5f, true, out float ht, out float pt); float top = Mathf.Max(ht, pt); return top > float.MinValue ? q.y - top : 999f; }

        // ------------------------------------------------------------------ the take
        IEnumerator All()
        {
            T0 = Time.unscaledTime; lastFlush = T0; Application.targetFrameRate = 60;
            try { System.IO.File.WriteAllText(soundPath, ""); } catch { }
            if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s) ?? OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            Log($"K256e shuttle flight look proof {mode}, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}; shuttle #{s.Id}");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            Log($"set-up: CPU removed {bots}, day 0.10 held, camera 70 m (fixed for every strip)");
            Log($"the numbers drawn: bob {ship.bobM:F2} m at {ship.bobHz:F2} / {ship.bobHz2:F2} Hz, drifts {ship.driftDeg:F1} deg; bank x{ship.bankPerTurn:F2} up to {ship.bankMax:F0} deg, at most {ship.bankRateMax:F0} deg/s; nose {ship.pitchDeg:F0} deg; gaps: air {OzShuttleSkyTop.Air:F1}, hard {OzShuttleSkyTop.HardGap:F1}, plants {OzShuttleSkyTop.PlantGap:F1} m");
            yield return Wait(1.0f);
            bool all = mode == "E";
            if (all || mode == "S6") yield return Camp();
            if (all || mode == "M11") { yield return Hover(); yield return Crossings(); }
            if (all || mode == "S1") yield return Bank();
            if (all || mode == "M19") yield return Easing();
            if (all || mode == "M18") yield return Strafe();
            if (all || mode == "M9") { yield return Crash(1); yield return Crash(-1); }
            Finish();
        }

        // ------------------------------------------------------------------ S6: the heroes' camp under the ship
        IEnumerator Camp()
        {
            Log("S6 the heroes' camp: the A proof's own staging (a squad boards from 10 m in front of the dome; 60 m east, a 90 degree re-order north halfway), then a hover over the camp for 6 s (it cannot land on them); each hero's reason to get up read every frame");
            var heroes = new List<UnitView>();
            foreach (var o in W.Objects) if (o.IsHero && o.Owner == me && !o.Destroyed && g.Views.TryGetValue(o.Id, out var v) && v && v.AnticActive) heroes.Add(v);
            if (heroes.Count == 0) { Check("S6 the two heroes lie on their loungers at the proof's start (start the proof right after the level start)", false); yield break; }
            var camp = Vector3.zero; foreach (var v in heroes) camp += v.transform.position; camp /= heroes.Count; camp.y = 0f;
            var stops = new Dictionary<int, string>(); var last = new Dictionary<int, string>();
            float closest = 999f; bool pic = false; string stage = "start";
            void Watch()
            {
                foreach (var v in heroes)
                {
                    if (!v || v.Obj == null || stops.ContainsKey(v.Obj.Id)) continue;
                    if (v.AnticActive)
                    {
                        string r = v.AnticBusyReason() ?? AnticDirector.ThreatReason(v);
                        if (r != null) last[v.Obj.Id] = r;
                        continue;
                    }
                    var near = new List<string>();
                    foreach (var m in W.PendingMissiles) if (m != null && !m.debris && (PxW(m.x, m.y) - v.transform.position).magnitude < AnticDirector.MissileNearM) near.Add($"missile of {m.team} #{m.attackerId}");
                    stops[v.Obj.Id] = $"{v.Obj.HeroMemberName} #{v.Obj.Id} got up at {Time.unscaledTime - T0:F2} s ({stage}; the ship {s.ShuttleState}, {Vector3.Distance(ship.transform.position, v.transform.position):F1} m away, its pads {ship.FeetHeight:F1} m up); the last reason seen: {(last.TryGetValue(v.Obj.Id, out var lr) ? lr : "none")}; {string.Join(", ", near)}";
                }
                float d = new Vector2(ship.transform.position.x - camp.x, ship.transform.position.z - camp.z).magnitude;
                if (s.ShuttleAirborne && d < closest) closest = d;
                if (!pic && s.ShuttleAirborne && d < 4f) { pic = true; Shot("S6a_over_the_camp_h70"); }
            }
            IEnumerator Until(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { Watch(); t += Time.deltaTime; yield return null; } }
            Fix(Vector3.Lerp(ShipG(), camp, 0.5f));
            // the boarding (the A proof's S2)
            stage = "boarding";
            double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            var sq = Squad("grunt", PxW((int)(s.CenterX + System.Math.Cos(hd) * 80), (int)(s.CenterY + System.Math.Sin(hd) * 80)), me);
            made.Remove(sq);                                                                                          // it flies with the ship: never cleared by this proof
            yield return Until(() => false, 0.8f);
            int shuts0 = s.ShuttleRampShuts;
            if (sq != null) W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
            yield return Until(() => s.ShuttleRampShuts > shuts0, 30f); yield return Until(() => false, 0.6f);
            // the flight (the A proof's S3): 60 m east, a 90 degree re-order north halfway
            stage = "the flight east";
            var q1 = Pad(480, 0f, 60); if (q1 != null) Move(q1[0], q1[1]);
            yield return Until(() => s.ShuttleState == ZShuttleState.Cruise, 12f); yield return Until(() => false, 2.2f);
            stage = "the turn north";
            var q2 = Pad(420, -90f, 60); if (q2 != null) Move(q2[0], q2[1]);
            yield return Until(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && !ship.CrashOn, 40f);
            // the hover over the camp
            stage = "the hover over the camp";
            Fix(camp); MoveW(camp);
            yield return Until(() => s.ShuttleState == ZShuttleState.Hover, 25f);
            float hov = 0f; bool pic2 = false;
            while (hov < 6f) { Watch(); if (!pic2 && hov > 2.5f) { pic2 = true; Shot("S6b_hover_over_the_camp_h70"); } hov += Time.deltaTime; yield return null; }
            var lines = AnticDirector.Log.Where(l => (l.Contains("cancel") || l.Contains("end ")) && heroes.Any(v => v && v.Obj != null && l.Contains("#" + v.Obj.Id + " "))).ToList();   // its own words: "cancel Sunbed on <type> #<id> at .. s in ..: <reason>"
            Log($"S6 the ship passed {closest:F1} m from the camp's middle, then hovered over it {hov:F1} s; the antic director's lines for the heroes: {(lines.Count == 0 ? "none" : string.Join(" | ", lines))}");
            foreach (var kv in stops) Log("S6 " + kv.Value);
            Check($"S6 the heroes stay on their loungers (the parasols as laid out) while a squad boards, the ship flies over and hovers over the camp (got up: {stops.Count}; the reason is in the lines above)", stops.Count == 0);
            Fix(null);
        }

        // ------------------------------------------------------------------ M11: the hover bob, the crossings, 05b
        IEnumerator Hover()
        {
            Log("M11 the hover bob: a 3 s fixed-camera strip from a low point of the bob");
            var q = Spot((x, y, why) => (why == "building" || why == "rocks" || why == "blocked" || why == "units") && OpenGrassAround(x, y), 30f, 160f);
            if (q == null) { Check("M11 a place to hover over open ground", false); yield break; }
            yield return FlyTo(q, 45f);
            Check("M11 it hovers (it cannot land there)", s.ShuttleState == ZShuttleState.Hover);
            yield return Wait(3f);
            Fix(ShipG());
            float last = ship.BobNow;
            yield return WaitFor(() => { bool turn = ship.BobNow > last && ship.BobNow < -0.45f; last = ship.BobNow; return turn; }, 8f);
            var ys = new List<float>(); var sc = new List<float>(); var al = new List<float>(); var cam = Camera.main;
            for (int i = 0; i < 10; i++)
            {
                Shot($"05c_hover_bob_{i:00}_h70");
                if (cam) ys.Add(Screen.height - cam.WorldToScreenPoint(ship.transform.TransformPoint(new Vector3(0f, 2.6f, 0f))).y);
                if (ship.Shadow != null) { sc.Add(ship.Shadow.ScaleNow); al.Add(ship.Shadow.StrengthNow); }
                yield return Wait(0.3f);
            }
            yield return CloseTwin("05c_hover_bob_09_h70");
            float rise = 0f, sink = 0f;
            for (int a = 0; a < ys.Count; a++) for (int b = a + 1; b < ys.Count; b++) { rise = Mathf.Max(rise, ys[a] - ys[b]); sink = Mathf.Max(sink, ys[b] - ys[a]); }
            Log($"M11 the hull's screen height over the strip (px from the top): {string.Join(" ", ys.Select(v => v.ToString("F0")))}; the shadow's size {Range(sc)}, its strength {Range(al)}");
            Check($"M11 the 3 s hover strip shows a rise ({rise:F1} px) and a sink ({sink:F1} px) of at least 15 px each", rise >= 15f && sink >= 15f);
            float lo = 9f, hi = -9f, t = 0f;
            while (t < 10f) { lo = Mathf.Min(lo, ship.BobNow); hi = Mathf.Max(hi, ship.BobNow); t += Time.deltaTime; yield return null; }
            Check($"M11 the bob over 10 s: {(hi - lo) * 0.5f:F2} m (0.55-0.75)", (hi - lo) * 0.5f >= 0.55f && (hi - lo) * 0.5f <= 0.75f);
            Fix(null);
        }
        static string Range(List<float> l) => l.Count == 0 ? "-" : $"{l.Min():F3} .. {l.Max():F3}";
        bool OpenGrassAround(int x, int y)
        {
            var c = PxW(x, y); return PlantTallest(c, 10f) < 3f && !OpenZFxPlayer.IsWaterTile(c);
        }

        IEnumerator Crossings()
        {
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == me && !o.Destroyed);
            if (fort != null) { var c = PxW(fort.CenterX, fort.CenterY); yield return Cross("05d_over_the_fort_roof", c, Vector3.forward, "the own fort's roof"); }
            else Check("M11 the own fort to fly over", false);
            // a cliff or a hill: the ground rises 4 m or more within 8 m, the nearest such edge
            Vector3 edge = Vector3.zero, up = Vector3.zero; float bestD = 1e9f;
            for (int ty = 8; ty < W.Map.height - 8; ty += 2)
                for (int tx = 8; tx < W.Map.width - 8; tx += 2)
                {
                    var c = PxW(tx * 16 + 8, ty * 16 + 8); float gy = g.GroundY(c.x, c.z);
                    foreach (var d in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                    {
                        var p = c + d * 8f; if (g.GroundY(p.x, p.z) - gy < 4f) continue;
                        float dd = (c - ShipG()).sqrMagnitude; if (dd < bestD) { bestD = dd; edge = c + d * 4f; up = d; }
                    }
                }
            if (bestD < 1e9f) yield return Cross("05e_over_a_cliff", edge, up, "a cliff / hill edge (the ground 4 m higher within 8 m)");
            else Check("M11 a cliff or hill to fly over", false);
            var fw = OzFloraWind.I; OzFloraWind.Plant kap = null;
            float mapW = W.Map.width * 2f, mapH = W.Map.height * 2f;
            System.Func<Vector3, bool> OnMap = q => q.x > 24f && q.x < mapW - 24f && q.z < -24f && q.z > -mapH + 24f;   // run 27D: the crossing's path stays on the map
            if (fw != null) foreach (var p in fw.All) if (p != null && p.R && !p.Gone && p.Height * p.R.transform.lossyScale.y > 11f && OnMap(p.R.transform.position + Vector3.left * 45f) && OnMap(p.R.transform.position + Vector3.right * 30f)) { if (kap == null || Vector3.Distance(p.R.transform.position, ship.transform.position) < Vector3.Distance(kap.R.transform.position, ship.transform.position)) kap = p; }
            if (kap != null) { var k = kap.R.transform.position; yield return Cross("05f_over_kapoks", new Vector3(k.x, 0f, k.z), Vector3.right, "a kapok (" + (kap.Height * kap.R.transform.lossyScale.y).ToString("F1") + " m)", true); }
            else Check("M11 a kapok to fly over", false);
        }

        /// <summary>A crossing: from 30 m before the point to 30 m beyond it along dir, the camera fixed on the point; 12 frames from 16 m
        /// before it; the gap, the climb, the rise before and the sink after.</summary>
        IEnumerator Cross(string name, Vector3 at, Vector3 dirW, string what, bool retake05b = false)
        {
            Log($"M11 over {what} at ({at.x:F1}, {at.z:F1})");
            dirW.y = 0f; dirW.Normalize();
            var from = at - dirW * 45f; var to = at + dirW * 30f;                                           // run 27D: from 45 m out (at 30 m it had already climbed)
            MoveW(from); yield return WaitFor(() => (s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed) && !ship.CrashOn && new Vector2(ship.transform.position.x - from.x, ship.transform.position.z - from.z).magnitude < 6f, 50f);
            Fix(at); MoveW(to);
            float gapMin = 999f, climb = 0f, hBefore = -1f, hOver = -1f, hAfter = -1f, t = 0f, nextShot = 0f; int n = 0; bool pic = false;
            while (t < 40f)
            {
                var p = ship.transform.position; float along = Vector3.Dot(new Vector3(p.x - at.x, 0f, p.z - at.z), dirW);
                if (s.ShuttleAirborne) { gapMin = Mathf.Min(gapMin, Gap()); if (s.ShuttleState == ZShuttleState.Cruise) climb = Mathf.Max(climb, ship.ClimbRate); }
                if (hBefore < 0f && along > -40f) hBefore = p.y;
                if (Mathf.Abs(along) < 1.5f) hOver = Mathf.Max(hOver, p.y);
                if (along > 22f && hAfter < 0f) hAfter = p.y;
                if (along > -16f && n < 12 && t >= nextShot) { Shot($"{name}_{n:00}_h70"); n++; nextShot = t + 0.45f; }
                if (retake05b && !pic && Mathf.Abs(along) < 1.0f) { pic = true; Shot("05b_over_kapoks_h70"); }
                if (along > 26f || ((s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed) && along > 0f)) break;
                t += Time.deltaTime; yield return null;
            }
            Log($"M11 {what}: the root {hBefore:F1} m before, {hOver:F1} m over it, {hAfter:F1} m after; the smallest gap {gapMin:F2} m; the fastest climb {climb:F2} m/s; {n} frames");
            Check($"M11 over {what}: nothing cuts through the ship (gap {gapMin:F2} >= 1.0 m) and it climbs smoothly (at most {climb:F2} <= 4.05 m/s)", gapMin >= 1.0f && climb <= 4.05f);
            float need = ship.Sky.Wanted(new Vector3(at.x, 0f, at.z), Vector3.zero, true, out _, out _) - g.GroundY(at.x, at.z);   // the root height over its ground the obstacle asks
            bool needs = need > (float)ZWorld.ShuttleCruiseM + 0.3f;
            Check($"M11 over {what}: {(needs ? "it rises before" : "no rise needed (it asks " + need.ToString("F1") + " m over its ground, the cruise is " + ZWorld.ShuttleCruiseM.ToString("F0") + " m): it holds its height; rise")} ({hOver - hBefore:+0.0;-0.0} m) and sinks after ({hAfter - hOver:+0.0;-0.0} m)",
                  needs ? hOver > hBefore + 0.3f && hAfter < hOver - 0.3f : Mathf.Abs(hOver - hBefore) < 2.5f);
            if (retake05b) Check("M11 05b retaken: the ship over the kapok in the middle of the screen (clear of the unit panel)", pic);
            Fix(null);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 30f);
            yield return WaitFor(() => !ship.CrashOn, 6f);
        }

        // ------------------------------------------------------------------ S1: the bank
        IEnumerator Bank()
        {
            Log("S1 the bank: flying north over open ground, a 90 degree re-order to the east; 10 frames through the turn");
            var p0 = Spot((x, y, why) => why == "" && OpenGrass(x, y, 14f) && OpenGrass(x, y - 400, 14f) && OpenGrass(x + 280, y - 520, 14f), 20f, 160f);
            if (p0 == null) { Check("S1 an open stretch for the turn", false); yield break; }
            yield return FlyTo(p0, 50f);
            var a = PxW(p0[0], p0[1]); var north = a + Vector3.forward * 50f;
            MoveW(north);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.9 * s.RealMoveSpeed && ship.transform.position.z > a.z + 18f, 25f);
            var here = ShipG(); Fix(here + new Vector3(8f, 0f, 10f));
            ship.BankPeak = 0f; ship.BankRatePeak = 0f;
            MoveW(here + new Vector3(40f, 0f, 4f));                                                                  // about 90 degrees to the east
            for (int i = 0; i < 10; i++) { Shot($"09b_bank_strip_{i:00}_h70"); yield return Wait(0.25f); }
            yield return Wait(1f);
            Log($"S1 the bank through the turn: peak {ship.BankPeak:F1} deg, fastest change {ship.BankRatePeak:F1} deg/s (game time)");
            Check($"S1 it leans {ship.BankPeak:F1} deg into the turn (12-20.5)", ship.BankPeak >= 12f && ship.BankPeak <= 20.5f);
            Check($"S1 the lean changes at most {ship.BankRatePeak:F1} deg/s (<= 30)", ship.BankRatePeak <= 30f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 30f);
            // the turn on the spot: from a stand, a goal behind it
            if (s.ShuttleState == ZShuttleState.Landed) { yield return WaitFor(() => !ship.CrashOn, 6f); }
            var back = ShipG() - ship.transform.forward * 30f; Fix(ShipG());
            float slow = 0f, t = 0f; bool pic = false; MoveW(back);
            while (t < 8f)
            {
                if (s.ShuttleAirborne && s.ShuttleSpeedPx < 5.0 && ship.FeetHeight > 2f) { slow = Mathf.Max(slow, Mathf.Abs(ship.TurnBankDeg)); if (!pic && Mathf.Abs(s.ShuttleTurnRateDeg) > 30f) { pic = true; Shot("09c_turn_on_the_spot_level_h70"); } }
                t += Time.deltaTime; yield return null;
            }
            Check($"S1 turning on the spot it stays level (its turn bank at most {slow:F2} <= 2 deg), picture taken {pic}", slow <= 2f);
            Fix(null);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 30f);
            yield return WaitFor(() => !ship.CrashOn, 6f);
        }

        // ------------------------------------------------------------------ M19: the easing
        IEnumerator Easing()
        {
            Log("M19 the easing: a lift-off and a 45 m flight east under a fixed camera, frames every 0.25 s; level, nose-down, nose-up");
            var p0 = Spot((x, y, why) => why == "" && OpenGrass(x, y, 12f) && OpenGrass(x + 360, y, 12f) && W.ShuttlePadFree(x + 360, y, s, out _), 15f, 160f);
            if (p0 == null) { Check("M19 an open stretch 45 m long", false); yield break; }
            yield return FlyTo(p0, 50f);
            if (s.ShuttleState != ZShuttleState.Landed) { yield return Wait(2f); }
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !ship.CrashOn && s.ShuttleLevelled, 10f);
            var a = PxW(p0[0], p0[1]); var b = PxW(p0[0] + 360, p0[1]);
            Fix(Vector3.Lerp(a, b, 0.5f));
            float acc = Mathf.Max(1f, (float)s.RealMoveSpeed) / (float)ZWorld.ShuttleAccelSec, pMax = -99f, pMin = 99f;
            bool down = false, upP = false, level = false; int n = 0;
            Move(p0[0] + 360, p0[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Climb, 10f);
            float t = 0f, next = 0f;
            while (t < 30f && !(s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.97 * s.RealMoveSpeed && t > 3f && n > 4))
            {   // lift-off to cruise
                if (t >= next) { Shot($"15a_lift_off_to_cruise_{n:00}_h70"); n++; next += 0.25f; }
                float k = (float)s.ShuttleAccelPx / acc; pMax = Mathf.Max(pMax, ship.PitchNowDeg);
                if (!down && k > 0.5f && ship.PitchNowDeg >= 0.92f * ship.pitchDeg) { down = true; Shot("15d_peak_nose_down_h70"); }
                t += Time.deltaTime; yield return null;
            }
            yield return WaitFor(() => { if (!level && Mathf.Abs(ship.PitchNowDeg) < 0.4f && s.ShuttleSpeedPx > 0.97 * s.RealMoveSpeed) { level = true; Shot("15c_level_h70"); } return (float)s.ShuttleAccelPx < -0.3f * acc || s.ShuttleState == ZShuttleState.Descend; }, 20f);
            n = 0; t = 0f; next = 0f;
            while (t < 20f && s.ShuttleState != ZShuttleState.Landed)
            {   // braking to touchdown
                if (t >= next) { Shot($"15b_braking_to_touchdown_{n:00}_h70"); n++; next += 0.25f; }
                pMin = Mathf.Min(pMin, ship.PitchNowDeg);
                if (!upP && (float)s.ShuttleAccelPx < -0.5f * acc && ship.PitchNowDeg <= -0.92f * ship.pitchDeg) { upP = true; Shot("15e_peak_nose_up_h70"); }
                t += Time.deltaTime; yield return null;
            }
            Shot($"15b_braking_to_touchdown_{n:00}_h70");
            Log($"M19 the nose: {pMax:F1} deg down speeding up, {pMin:F1} deg braking (pitchDeg {ship.pitchDeg:F0}); pictures nose-down {down}, nose-up {upP}, level {level}");
            Check($"M19 the nose dips speeding up ({pMax:F1} >= 5 deg) and lifts braking ({pMin:F1} <= -5 deg)", pMax >= 5f && pMin <= -5f);
            Check("M19 the three pictures: level, the peak nose-down, the peak nose-up", down && upP && level);
            Log("M19 the spacing per 0.25 s is in frames.tsv (the ship's screen place); tools/e_flight_strips.py makes the onion-skin sheets and counts it");
            Fix(null);
            yield return WaitFor(() => !ship.CrashOn, 6f);
        }

        // ------------------------------------------------------------------ M18: the strafe
        IEnumerator Strafe()
        {
            Log("M18 the strafe: hovering facing north, enemy rockets from ahead: 14 frames over 7 s (out, hold, back)");
            var q = Spot((x, y, why) => (why == "building" || why == "rocks" || why == "blocked" || why == "units") && OpenGrassAround(x, y) && RoadNear(x, y, 14f) && W.ShuttlePadFree(x, y + 240, s, out _), 30f, 260f);
            bool placed = false; holder = null;
            if (q == null)
            {   // no natural blocked place by a road: an own robot holds the pad (it cannot land on it)
                q = Spot((x, y, why) => why == "" && OpenGrass(x, y, 10f) && RoadNear(x, y, 14f) && W.ShuttlePadFree(x, y + 240, s, out _), 30f, 260f);
                if (q != null) { holder = Squad("grunt", PxW(q[0], q[1] + 16), me); made.Remove(holder); placed = holder != null; yield return Wait(1f); }
            }
            if (q == null) { Check("M18 a place to hover beside a road", false); yield break; }
            Log($"M18 the hover place ({(placed ? "an own squad holds the pad" : "a building / rocks hold the pad")}), a road edge within 14 m");
            yield return FlyTo(new[] { q[0], q[1] + 240 }, 50f);                                                  // 30 m south of it
            yield return WaitFor(() => !ship.CrashOn && s.ShuttleLevelled, 6f);
            yield return FlyTo(q, 40f);
            Check($"M18 it hovers facing north (heading {s.ShuttleHeadingDeg:F0}, -90 = north)", s.ShuttleState == ZShuttleState.Hover && Mathf.Abs(Mathf.DeltaAngle((float)s.ShuttleHeadingDeg, -90f)) < 25f);
            yield return Wait(2.5f);
            var c = ShipG(); Fix(c);
            int threats0 = s.ShuttleThreatsSeen, glides0 = ship.Glides; Heal(); float tWhoosh = Time.unscaledTime;
            var shooters = Squad("tough", c + Vector3.forward * 22f, them); Attack(shooters);
            yield return WaitFor(() => { Heal(); if (shooters != null && shooters.Waypoints.Count == 0 && shooters.AttackObject != s) Attack(shooters); return s.ShuttleThreatsSeen > threats0; }, 20f);
            Clear();                                                                                                 // one salvo: the fire stops, the ship drifts back
            for (int i = 0; i < 14; i++) { Heal(); Shot($"16b_strafe_strip_{i:00}_h70"); yield return Wait(0.5f); }
            yield return WaitFor(() => ship.Glides > glides0, 8f);
            Log("M18 the glides: " + OzPlayableShuttle.GlideLog.ToString().TrimEnd().Split('\n').LastOrDefault());
            Check($"M18 one glide out {ship.GlideOutM:F2} m (4.5-5.5) in {ship.GlideOutSec:F2} s (1.6-2.0), back in {ship.GlideBackSec:F2} s (3.6-4.4), peak sideways {ship.GlidePeakMps:F2} m/s",
                  ship.Glides > glides0 && ship.GlideOutM >= 4.5f && ship.GlideOutM <= 5.5f && ship.GlideOutSec >= 1.6f && ship.GlideOutSec <= 2.0f && ship.GlideBackSec >= 3.6f && ship.GlideBackSec <= 4.4f);
            Check($"M18 no flip of its sideways direction within 3 s (shortest {ship.GlideFlipGapMin:F2} s)", ship.GlideFlipGapMin >= 2.95f);
            int whooshes = 0; float whooshLo = 9f, whooshHi = 0f;                                                  // M15: the glide out (at least 0.14) and the drift back are heard, none under 0.12
            foreach (var e in OzSoundLog.Since(tWhoosh))
                if (e.key == "shuttle_strafe_whoosh" && e.outcome != null && e.outcome.StartsWith("PLAY")) { whooshes++; whooshLo = Mathf.Min(whooshLo, e.vol); whooshHi = Mathf.Max(whooshHi, e.vol); }
            Check($"M15 the strafe whoosh is heard with the glide: {whooshes} played, the loudest {whooshHi:F3} (>= 0.14), the quietest {(whooshes > 0 ? whooshLo : 0f):F3} (>= 0.12)", whooshes > 0 && whooshHi >= 0.14f && whooshLo >= 0.12f);
            Clear(); if (holder != null) { made.Add(holder); Clear(); holder = null; } Heal(); Fix(null);
        }
        ZObject holder;

        // ------------------------------------------------------------------ M9: the crash landing, each side
        static int ShuttleHash(int a, int b) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (int)(h & 0x7fffffff); } }
        int NextSide() => (ShuttleHash(s.Id + 7, s.ShuttleLandings + 1) & 1) == 0 ? 1 : -1;

        IEnumerator Crash(int side)
        {
            string tag = side > 0 ? "plus_x" : "minus_x";
            Log($"M9 a crash landing, the {(side > 0 ? "+x" : "-x")} side first, on open grass{(side < 0 ? ", an own squad standing near" : "")}");
            for (int k = 0; k < 4 && NextSide() != side; k++)
            {   // the next landing's side is the other one: a landing first (it may be rough by its number: then that one counts for its side)
                var p = Spot((x, y, why) => why == "" && OpenGrass(x, y, 10f), 25f, 120f);
                if (p == null) break;
                yield return FlyTo(p, 40f); yield return WaitFor(() => !ship.CrashOn && s.ShuttleLevelled, 8f);
            }
            if (NextSide() != side) { Check($"M9 the {tag} crash: the next landing's side could not be brought round", false); yield break; }
            var q = Spot((x, y, why) => why == "" && OpenGrass(x, y, 14f) && !UnitsNear(PxW(x, y), 16f), 40f, 160f);
            if (q == null) { Check("M9 open grass for the crash", false); yield break; }
            var c = PxW(q[0], q[1]);
            ZObject crowd = null;
            var appr = c - ShipG(); appr.y = 0f; appr = appr.sqrMagnitude > 1f ? appr.normalized : Vector3.forward;
            var beside = new Vector3(appr.z, 0f, -appr.x);                                                          // beside the hull (never in the ramp's lane behind it)
            if (side < 0) { crowd = Squad("grunt", c + beside * 8f, me); yield return Wait(1f); }
            if (side < 0 && !W.ShuttlePadFree(q[0], q[1], s, out string why2)) { Log("M9 the squad stands on the pad (" + why2 + "): it is moved off"); Clear(); crowd = Squad("grunt", c + beside * 9.2f, me); yield return Wait(1f); }
            Fix(c);
            int flinch0 = 0, landed0 = OzPlayableShuttle.CrashLandedLines, scr0 = OzPlayableShuttle.ScrapesLaid; float t0 = Time.unscaledTime;
            OzPlayableShuttle.ProofRoughNext = true;
            Move(q[0], q[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend && s.ShuttleLift * ZWorld.ShuttleDescendSec <= 0.3, 60f);
            var cam = Camera.main; var gaps = new List<float>(); bool tiltShot = false; float tiltT = -1f;
            for (int i = 0; i < 16; i++)
            {
                Shot($"07d_crash_{tag}_{i:00}_h70");
                float until = Time.time + 0.25f;
                while (Time.time < until)
                {   // run 27D: the tilted picture at the rest (1.2-2.0 s after the contact), the high pads' px measured in that same frame
                    if (!tiltShot && ship.CrashOn && ship.CrashT >= 0.80f && ship.CrashT <= 0.98f)   // K426: the rest is 0.75-1.0 s after the contact now (was 1.2-2.4)
                    {
                        tiltShot = true; tiltT = ship.CrashT; Shot($"07f_crash_tilted_{tag}_h70");
                        if (cam) for (int k = 0; k < 4; k++)
                            {
                                var ps = new[] { new Vector3(2.228f, 0f, 1.029f), new Vector3(-2.168f, 0f, 1.079f), new Vector3(1.829f, 0f, -2.843f), new Vector3(-1.771f, 0f, -2.843f) }[k];
                                if (ps.x * ship.CrashSide > 0f) continue;
                                var w = ship.transform.TransformPoint(ps); var a = cam.WorldToScreenPoint(w); var b = cam.WorldToScreenPoint(new Vector3(w.x, g.GroundY(w.x, w.z), w.z));
                                gaps.Add(Vector2.Distance(a, b));
                            }
                    }
                    yield return null;
                }
            }
            Log($"M9 {tag}: the tilted picture at {tiltT:F2} s after the contact ({(tiltShot ? "taken" : "NOT taken: the rest was not seen in the strip")})");
            if (tiltShot) yield return CloseTwin($"07f_crash_tilted_{tag}_h70");
            yield return WaitFor(() => !ship.CrashOn, 6f);
            yield return Wait(0.4f); Shot($"07g_crash_levelled_{tag}_h70");
            flinch0 = ship.CrashFlinched;
            float hot = FirstPlay("vo_shuttle_hot", t0), slam = FirstPlay("wreck_crash", t0);
            int landedAsked = Asked("vo_shuttle_landed", t0);
            Log(OzShuttleTouchdown.Log.ToString().TrimEnd());
            Log($"M9 {tag}: side drawn {(ship.CrashSide > 0 ? "+x" : "-x")}; the low side rebounded {ship.CrashHopSeen:F2} m, the other side down at {ship.CrashHighAtSeen:F3} s; at rest {ship.CrashRestTiltSeen:F1} deg, held {ship.CrashRestHeldSec:F2} s at 13 deg or more, the high pads {ship.CrashHighPadUpSeen:F2} m up ({string.Join(" / ", gaps.Select(x => x.ToString("F1")))} px over the ground at 70 m); level {OzShuttleTouchdown.LevelOff:F3} s off the rules' moment; scrape mark {ship.CrashScrapeLen:F1} m; robots flinched {flinch0}; \"Coming in hot!\" asked {ship.CrashHotLead:F2} s before the contact, played at {hot - T0:F2}, the slam at {slam - T0:F2}; landing lines asked {landedAsked}");
            Check($"M9 {tag}: the side the rules named is drawn", Mathf.RoundToInt(ship.CrashSide) == side);
            Check($"M9 {tag}: the low side rebounds {ship.CrashHopSeen:F2} m (0.25-0.45; S7 >= 0.15) and the other side comes down at {ship.CrashHighAtSeen:F3} s, the frame before at {ship.CrashHighPrevT:F3} s (its moment 0.30-0.35 between them)", ship.CrashHopSeen >= 0.25f && ship.CrashHopSeen <= 0.45f && ship.CrashHighAtSeen >= 0.30f && (ship.CrashHighAtSeen <= 0.35f || ship.CrashHighPrevT < 0.35f));
            Check($"M9 {tag}: it rests tilted {ship.CrashRestTiltSeen:F1} deg (13-14) for {ship.CrashRestHeldSec:F2} s (>= 0.8; K426: 1.5 before the shorter rest), then rights itself on the rules' moment ({OzShuttleTouchdown.LevelOff:F3} <= 0.05 s)", ship.CrashRestTiltSeen >= 13f && ship.CrashRestTiltSeen <= 14f && ship.CrashRestHeldSec >= 0.8f && OzShuttleTouchdown.LevelOff >= 0f && OzShuttleTouchdown.LevelOff <= 0.05f);
            Check($"M9 {tag}: the tilt reads at 70 m: the high pads stand {(gaps.Count > 0 ? gaps.Min() : 0f):F1} px over the ground (>= 15)", gaps.Count > 0 && gaps.Min() >= 15f);
            Check($"M9 {tag}: no pad in the ground (at most {OzShuttleTouchdown.WorstPadIn * 100f:F1} cm)", OzShuttleTouchdown.WorstPadIn <= 0.031f);
            Check($"M9 {tag}: a dark scrape mark 5-7 m long is left ({ship.CrashScrapeLen:F1} m)", OzPlayableShuttle.ScrapesLaid > scr0 && ship.CrashScrapeLen >= 5f && ship.CrashScrapeLen <= 7f);
            Check($"M9 {tag}: \"Coming in hot!\" before the contact ({(slam > 0 && hot > 0 ? slam - hot : -1f):F2} s before the slam; asked {ship.CrashHotLead:F2} s before), never the landing line ({OzPlayableShuttle.CrashLandedLines - landed0} asked on a crash, {landedAsked} in the log)",
                  hot > 0f && slam > 0f && hot < slam && OzPlayableShuttle.CrashLandedLines == landed0 && landedAsked == 0);
            if (side < 0) Check($"M9 {tag}: the own squad standing near flinches ({flinch0} robots)", crowd == null || flinch0 > 0);
            Clear(); Fix(null);
        }

        // ------------------------------------------------------------------ the end
        void Finish()
        {
            OzPlayableShuttle.ProofRoughNext = false;
            Flush();
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS), {shots} pictures, {Time.unscaledTime - T0:F0} s");
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "frames.tsv"), "name\treal_s\tsim_s\thull_x\thull_y\troot_x\troot_y\tground_x\tground_y\tfeet_m\tbob_m\tbank_deg\tpitch_deg\tspeed_mps\tstrafe_m\tcrash_roll_deg\tcrash_t\tcamera_height_m\tcamera_to_ship_m\n" + frames);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "glide_log.txt"), OzPlayableShuttle.GlideLog.ToString());
            }
            catch (System.Exception ex) { Debug.LogError("[OzShuttleFlightProof] could not write the logs: " + ex.Message); }
            if (dn) dn.running = true;
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject, 1f);
        }
    }
}
