using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The Play proof of the shuttle's death at its drawn altitude (rule DV118, view only; menu OpenZ/V1.1/Shuttle: K256h Death Altitude
    /// Proof, Editor/OzShuttleDeathAltitudeMenu.cs), on level 16 in Play. Every kill after a fresh start of the level, a grunt squad of
    /// three aboard, the normal RTS camera at 70 m fixed over the ship at the kill:
    /// C1 at cruise height, rifle fire (the killing hit a grunt's round); C2 at cruise height destroyed outright (no hit drawn); H1
    /// hovering, a real grunt squad shoots it down; H2 hovering, a rocket; H3 hovering, a howitzer shell; L1 low, just after take-off
    /// (pads 3-5 m up), rifle fire; G1 landed, destroyed on the ground. The staged killing hits are OzShuttleAim's own last-hit record
    /// (what a drawn round on the hull writes), then the rules' own death (DamageHealth + UpdateObjectHealth).
    /// Logged: the drawn ship (root, pads over the ground) every frame from the kill to the break, whether it is drawn; the break (how,
    /// in the air or not, the frame); every explosion with its height; every piece's start (its place on the drawn hull, its height);
    /// the bodies' start (their seats on the drawn hull); all of them again 0.5 / 1 / 2 s after the break; the landing contacts' heights
    /// (pieces and bodies), the contacts up in the air; the sounds of the landings; the rest on the ground. A 10-picture strip at 70 m,
    /// one every 0.2 s from the kill, and one picture when everything lies. Counted PASS / FAIL lines. Output folder: <see cref="Folder"/>.
    /// Test only.
    /// </summary>
    public sealed partial class OzShuttleDeathAltitudeProof : MonoBehaviour
    {
        public static string Report = "idle";
        /// <summary>The output folder: the work tree's records folder found upward from the project at run time
        /// (milestones/shuttle_death_altitude), else the player's own data folder.</summary>
        public static string Folder()
        {
            const string sub = "shuttle_death_altitude";
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }
        /// <summary>mode: "all" or the kills' tags, e.g. "C1,H1".</summary>
        public static void Run(string mode)
        {
            Report = "running " + mode;
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var go = new GameObject("OzShuttleDeathAltitudeProof"); var p = go.AddComponent<OzShuttleDeathAltitudeProof>(); p.mode = mode;
        }

        sealed class Kill { public string tag, label, place, how, kind, expect; }
        static readonly Kill[] Kills =
        {
            new Kill { tag = "C1", label = "at cruise height, rifle fire", place = "cruise", how = "staged", kind = "grunt", expect = OzShuttleDeathPlan.Crack },
            new Kill { tag = "C2", label = "at cruise height, destroyed outright (no hit drawn)", place = "cruise", how = "direct", kind = "-", expect = OzShuttleDeathPlan.Burst },
            new Kill { tag = "H1", label = "hovering, a real grunt squad shoots it down", place = "hover", how = "real", kind = "grunt", expect = OzShuttleDeathPlan.Crack },
            new Kill { tag = "H2", label = "hovering, a rocket", place = "hover", how = "staged", kind = "missile_launcher", expect = OzShuttleDeathPlan.Burst },
            new Kill { tag = "H3", label = "hovering, a howitzer shell", place = "hover", how = "staged", kind = "howitzer", expect = OzShuttleDeathPlan.Split },
            new Kill { tag = "L1", label = "low, just after take-off, rifle fire", place = "low", how = "staged", kind = "grunt", expect = OzShuttleDeathPlan.Crack },
            new Kill { tag = "G1", label = "landed, destroyed on the ground", place = "ground", how = "direct", kind = "-", expect = OzShuttleDeathPlan.Ground },
        };

        string mode = "all";
        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder();
        int fails, passes, frames; string dir; float T0; Vector3? camAt; bool followShip;
        readonly Dictionary<string, int> plays = new Dictionary<string, int>(); float flushedTo;
        // the tracked kill
        bool tracking, killSeen, broke; int killFrame, breakFrame, hiddenBefore, framesBefore; float killAt, brokeAt; Vector3 killRoot, breakRoot; float killFeet; string killTag = "-";
        readonly List<(float t, Vector3 at, float strength)> booms = new List<(float, Vector3, float)>();
        int landedHigh; float landedHighWorst;
        readonly HashSet<OzShuttleCrewFall> landSeen = new HashSet<OzShuttleCrewFall>();

        void Start()
        {
            DontDestroyOnLoad(gameObject);                                                              // every kill starts the level afresh
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            FxManager.Exploded += OnBoom;
            StartCoroutine(All());
        }
        void OnDestroy() { FxManager.Exploded -= OnBoom; }

        /// <summary>Explosions this far (m, flat) from the ship at the kill are the kill's own (others on the level are not counted).</summary>
        const float BoomNearM = 30f;
        void OnBoom(Vector3 at, float strength) { if (tracking && killSeen && Time.time - killAt < 4f && Flat(at, killRoot) <= BoomNearM) booms.Add((Time.time - killAt, at, strength)); }

        /// <summary>The kill's moment, pose and strip: the rules' death seen (a real kill may come many seconds after the shooters start), or
        /// a staged kill just made (its break, and its blast, may come in this very frame's LateUpdate).</summary>
        void MarkKill()
        {
            if (killSeen || !ship) return;
            killSeen = true; killFrame = Time.frameCount; killAt = Time.time; killRoot = ship.transform.position; killFeet = ship.FeetHeight;
            StartCoroutine(Strip(killTag));
        }

        void Update()
        {
            if (followShip && ship && rts) { var p = ship.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); }
            else if (camAt.HasValue && rts) rts.focus = new Vector3(camAt.Value.x, 0f, camAt.Value.z);   // a FIXED camera from the kill on
            if (T0 > 0f && Time.unscaledTime - flushedTo > 1f) Flush();
            if (!tracking || !ship) return;
            if (!killSeen && (s.Destroyed || ship.DyingNow || ship.BrokenUp)) MarkKill();
            if (!killSeen) return;
            if (!broke)
            {   // the frames between the rules' death and the break (the break's own frame turns the hull off: that is the break, not a hidden frame)
                if (ship.BrokenUp) { broke = true; breakFrame = Time.frameCount; brokeAt = OzShuttleBreakup.LastBreakAt >= killAt - 0.001f ? OzShuttleBreakup.LastBreakAt : Time.time; breakRoot = ship.transform.position; }
                else { framesBefore++; if (!ship.HullDrawn) hiddenBefore++; }
            }
            foreach (var b in OzShuttleCrewFall.Last)
            {   // a body flagged as landed: how high its hips are over the ground then (on a piece or a roof it may be higher)
                if (!b || !b.Landed || landSeen.Contains(b)) continue;
                landSeen.Add(b);
                float h = Over(b.BodyPos);
                if (h > K419LandHighM) HighLanding(b, h);                                                             // K256h2: held up by a piece, a rock, a trunk or a landed body = lying (every bone asked)
            }
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleDeathAltitudeProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        float Over(Vector3 p) => OzShuttleBreakup.OverSurface(p);
        static bool OnPiece(Vector3 p)
        {
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider && (h.collider.GetComponentInParent<OzShuttleBreakup>() || !h.collider.attachedRigidbody) && h.collider.gameObject.layer != ZGame.WalkLayer && !CraterField.IsGroundCollider(h.collider)) return true;
            return false;
        }

        void Flush()
        {
            float to = flushedTo;
            foreach (var e in OzSoundLog.Since(flushedTo))
            {
                if (e.t <= flushedTo) continue;
                to = Mathf.Max(to, e.t);
                if (e.key == null) continue;
                bool played = e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"));
                if (played) plays[e.key] = (plays.TryGetValue(e.key, out int n) ? n : 0) + 1;
            }
            flushedTo = Mathf.Max(to, Time.unscaledTime - 0.01f);
        }
        int Played(string key) { Flush(); return (plays.TryGetValue(key, out int n) ? n : 0) - (plays0.TryGetValue(key, out int n0) ? n0 : 0); }   // since the kill
        readonly Dictionary<string, int> plays0 = new Dictionary<string, int>();

        // ------------------------------------------------------------------ helpers (as the K256b / K256e proofs')
        bool Bind()
        {
            g = ZGame.I; W = g ? g.World : null; if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) return false;
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            rts = FindFirstObjectByType<RtsCamera>();
            W.Bots.Clear(); dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            camAt = null; followShip = false;
            return true;
        }
        ZObject Squad(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
        void Attack(ZObject o) { if (o != null && !o.Destroyed && s != null) W.SendWaypoints(them, o, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } }); }
        void Clear(List<ZObject> list)
        {
            foreach (var o in list)
            {
                if (o == null) continue;
                var all = new List<ZObject> { o }; all.AddRange(o.Minions);
                foreach (var x in all) { x.KillAt = 0; if (g.Views.TryGetValue(x.Id, out var v)) { g.Views.Remove(x.Id); if (v) Destroy(v.gameObject); } }
            }
            list.Clear();
        }
        Vector3 W3(int x, int y) => ZGame.PxToWorld(x, y);
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        int Open(int tx, int ty, int r) { int n = 0; for (int j = -r; j <= r; j++) for (int i = -r; i <= r; i++) if (W.Path.TilePassable(tx + i, ty + j, true)) n++; return n; }

        /// <summary>A spot the ship cannot land on (it hovers there), 40-110 m away, open ground round it, the shooters' row 12 m south open.</summary>
        int[] HoverSpot(Vector3 nearW)
        {
            int[] best = null; float bestScore = float.MaxValue;
            for (int ty = 6; ty < W.Map.height - 12; ty++)
                for (int tx = 6; tx < W.Map.width - 6; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; float d = Flat(W3(x, y), nearW);
                    if (d < 40f || d > 110f) continue;
                    var q = W.ShuttleClamp(x, y); if (q.x != x || q.y != y) continue;
                    if (W.ShuttlePadFree(x, y, s, out string why)) continue;
                    if (why == "units" || why == "map edge" || why == "no map" || why == "repair bay busy" || string.IsNullOrEmpty(why)) continue;
                    bool ok = true; for (int k = -2; k <= 2 && ok; k++) ok = W.Path.TilePassable(tx + k, ty + 6, true) && W.Path.TilePassable(tx + k, ty + 7, true);
                    if (!ok) continue;
                    float score = d + 4f * (49 - Open(tx, ty, 3));
                    if (score < bestScore) { bestScore = score; best = new[] { x, y }; }
                }
            return best;
        }

        /// <summary>A killing hit on the hull as a drawn round leaves it (OzShuttleAim's last-hit record): its kind and power, the hull
        /// point it struck (the side facing a shooter 25 m south-west on the ground), its way.</summary>
        void StageHit(string kind)
        {
            var root = ship.transform; var mid = root.TransformPoint(new Vector3(0f, 2.2f, 0f));
            var from = new Vector3(mid.x - 18f, g.GroundY(mid.x - 18f, mid.z - 18f) + 1.2f, mid.z - 18f);
            var dirTo = (mid - from).normalized; var at = mid - dirTo * 2.4f;
            OzShuttleAim.LastHitAt = Time.time; OzShuttleAim.LastHitKind = kind; OzShuttleAim.LastHitPower = OzShuttleAim.Power(kind); OzShuttleAim.LastHitShip = s.Id;
            OzShuttleAim.LastHitPoint = at; OzShuttleAim.LastHitDir = dirTo;
        }

        // ------------------------------------------------------------------ the run
        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (!Bind()) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on, not 'Zod exact')"); fails++; Finish(); yield break; }
            T0 = Time.unscaledTime; flushedTo = T0;
            Log($"K256h death altitude proof '{mode}', {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; DropWhole {OzShuttleBreakup.DropWhole}, KeepShipDrawn {OzShuttleBreakup.KeepShipDrawn}, crew landing {OzShuttleCrewLanding.Enabled}");
            foreach (var k in Kills)
            {
                if (mode != "all" && !mode.Split(',').Select(x => x.Trim()).Contains(k.tag)) continue;
                yield return OneKill(k);
            }
            Finish();
        }

        IEnumerator OneKill(Kill k)
        {
            Log($"{k.tag} a fresh start of level 16: the kill {k.label}");
            var old = g; OzScenes.Restart();
            bool seen = false; float waited = 0f;
            while (waited < 150f)
            {
                if (ZodBriefing.BriefingActive) seen = true;
                var ng = ZGame.I;
                if (ng && ng != old && ng.World != null && ng.World.PlayableShuttle != null && OzPlayableShuttle.I && !ZodBriefing.BriefingActive && (seen || waited > 20f)) break;
                waited += Time.unscaledDeltaTime; yield return null;
            }
            if (!Bind()) { Check($"{k.tag} level 16 back with its shuttle", false); yield break; }
            yield return WaitFor(() => OzShuttleBreakup.PlanDone, 30f);
            yield return Wait(1.5f);
            var sq = Squad("grunt", (s.CenterX >> 4) + 3, (s.CenterY >> 4) + 4, me); yield return Wait(0.6f);
            Enter(sq); yield return WaitFor(() => s.Passengers.Count > 0, 30f); yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut && s.Passengers.Count >= 3, 20f);
            int aboard = s.Passengers.Count, lost0 = W.ShuttlePassengersLost, bodies0 = BoardingView.ShuttleBodies;
            var shooters = new List<ZObject>();
            // ---- the place
            if (k.place == "cruise" || k.place == "low")
            {
                var q = W.ShuttleClamp(s.CenterX, s.CenterY - 640); Move(q.x, q.y); followShip = true;
                if (k.place == "cruise")
                {
                    float steady = -1f, t = 0f;
                    while (t < 60f)
                    {
                        bool c = s.ShuttleState == ZShuttleState.Cruise && s.ShuttleAirborne && ship.FeetHeight >= 8f;
                        if (c) { if (steady < 0f) steady = 0f; steady += Time.deltaTime; if (steady >= 1.5f) break; } else steady = -1f;
                        t += Time.deltaTime; yield return null;
                    }
                }
                else yield return WaitFor(() => s.ShuttleAirborne && ship.FeetHeight >= 3f, 25f);
            }
            else if (k.place == "hover")
            {
                var spot = HoverSpot(ship.transform.position);
                if (spot == null) { Check($"{k.tag} a spot to hover over", false); yield break; }
                camAt = W3(spot[0], spot[1]); Move(spot[0], spot[1]);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover && ship.FeetHeight > 6f, 70f);
                yield return Wait(2.5f);
                Log($"{k.tag} hovering over tile ({spot[0] >> 4},{spot[1] >> 4}) ({s.ShuttleNoLand}), pads {ship.FeetHeight:F1} m up");
                if (k.how == "real")
                {
                    int hp0 = s.MaxHealth; s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);
                    shooters.Add(Squad(k.kind, spot[0] >> 4, (spot[1] >> 4) + 6, them)); shooters.RemoveAll(o => o == null);
                    s.MaxHealth = hp0;
                }
            }
            // ---- the kill (the camera fixed over the ship from here)
            followShip = false; camAt = ship.transform.position; if (rts) rts.SetHeightImmediate(70f);
            yield return null;
            BeginTrack(k.tag);
            Log($"{k.tag} KILL ({k.how}{(k.kind != "-" ? " " + k.kind : "")}) at the level clock {W.Time:F2} s: state {s.ShuttleState}, airborne {s.ShuttleAirborne}, the drawn root {Fmt(ship.transform.position)}, pads {ship.FeetHeight:F2} m over the ground, {aboard} aboard");
            if (k.how == "real")
            {
                s.SetHealth(1); foreach (var o in shooters) Attack(o);
                float t = 0f, nudge = 2f;
                while (t < 45f && !ship.BrokenUp) { if ((nudge -= Time.deltaTime) <= 0f) { nudge = 2f; foreach (var o in shooters) if (!o.Destroyed && o.AttackObject != s && o.Waypoints.Count == 0) Attack(o); } t += Time.deltaTime; yield return null; }
                if (!ship.BrokenUp) { Check($"{k.tag} the real {k.kind} fire brings it down within 45 s", false); StageHit(k.kind); s.DamageHealth(s.Health); W.UpdateObjectHealth(s); MarkKill(); }
                else Log($"{k.tag} shot down by the real fire after {t:F1} s (the killing hit {OzShuttleBreakup.LastKillKind}, power {OzShuttleBreakup.LastKillPower:F2})");
            }
            else
            {
                if (k.how == "staged") StageHit(k.kind); else OzShuttleAim.LastHitAt = -99f;
                s.DamageHealth(s.Health); W.UpdateObjectHealth(s);
                MarkKill();                                                                             // CombatView plays the death in this frame's LateUpdate: its blast is counted
            }
            yield return WaitFor(() => killSeen, 3f);
            yield return WaitFor(() => broke, 6f);
            if (!broke) { Check($"{k.tag} it breaks apart within 6 s of the kill", false); tracking = false; Clear(shooters); yield break; }
            Log($"{k.tag} the rules' death seen at the level clock {W.Time:F2} s: the drawn root then {Fmt(killRoot)}, pads {killFeet:F2} m over the ground");
            yield return BreakChecks(k, aboard, lost0, bodies0);
            Clear(shooters);
            tracking = false;
            yield return Wait(1.0f);
        }

        void BeginTrack(string tag)
        {
            booms.Clear(); landSeen.Clear(); landedHigh = 0; landedHighWorst = 0f; killTag = tag; landedOnTop.Clear();
            tracking = true; killSeen = false; broke = false; killFrame = Time.frameCount; killAt = Time.time; hiddenBefore = 0; framesBefore = 0;
            killRoot = ship.transform.position; killFeet = ship.FeetHeight;
            Flush(); plays0.Clear(); foreach (var kv in plays) plays0[kv.Key] = kv.Value;
        }

        string Fmt(Vector3 p) => $"({p.x:F1}, {p.y:F2}, {p.z:F1}) {Over(p):F2} m over the ground";

        struct Heights { public float lo, mid, hi; public int n; public override string ToString() => n == 0 ? "-" : $"{lo:F2} / {mid:F2} / {hi:F2} m ({n})"; }
        static Heights Of(List<float> l) { var h = new Heights { n = l.Count }; if (l.Count == 0) return h; l.Sort(); h.lo = l[0]; h.mid = l[l.Count / 2]; h.hi = l[l.Count - 1]; return h; }
        Heights PieceHeights() { var l = new List<float>(); foreach (var pc in OzShuttleBreakup.LastPieces) if (pc) l.Add(Over(pc.transform.position)); return Of(l); }
        Heights BodyHeights() { var l = new List<float>(); foreach (var b in OzShuttleCrewFall.Last) if (b) l.Add(Over(b.BodyPos)); return Of(l); }

        IEnumerator BreakChecks(Kill k, int aboard, int lost0, int bodies0)
        {
            string tag = k.tag; bool air = k.place != "ground";
            int air0 = OzShuttleBreakup.AirContacts, mid0 = OzShuttleCrewLanding.MidAir, puffs0 = OzShuttleBreakup.CrackPuffs;
            int thudAsked0 = OzBodySounds.ThudAsked, whoosh0 = OzBodySounds.WhooshAsked, clear0 = OzShuttleBreakup.RestClearMoves;
            yield return null;                                                                          // the pieces' and bodies' first frame
            // ---- the break: where, how
            var how = OzShuttleBreakup.LastHow; var centre = OzShuttleBreakup.LastCentre;
            Log($"{tag} broke {(OzShuttleBreakup.LastInAir ? "in the air" : "on the ground")} ('{how}') {brokeAt - killAt:F2} s after the kill ({breakFrame - killFrame} frames); the drawn root then {Fmt(breakRoot)} (moved {Vector3.Distance(breakRoot, killRoot):F2} m since the kill, {breakRoot.y - killRoot.y:+0.00;-0.00} m up); the ship drawn on {framesBefore - hiddenBefore} of {framesBefore} frames before the break");
            Log($"{tag} the blast's middle {Fmt(centre)}; pads at the break {OzShuttleBreakup.LastBreakFeet:F2} m; the pieces start {OzShuttleBreakup.LastSpawnLowM:F2} .. {OzShuttleBreakup.LastSpawnHighM:F2} m over the ground, at most {OzShuttleBreakup.LastSpawnOffM:F2} m from their places on the drawn hull");
            foreach (var b in booms) Log($"{tag}   EXPLOSION +{b.t:F2} s strength {b.strength:F2} at {Fmt(b.at)} ({Vector3.Distance(b.at, centre):F2} m from the blast's middle)");
            Check($"{tag} the ship is drawn on every frame from the kill to its break ({hiddenBefore} hidden of {framesBefore})", hiddenBefore == 0);
            Check($"{tag} the break is '{k.expect}' ({how}), {(air ? "in the air" : "on the ground")}", how == k.expect && OzShuttleBreakup.LastInAir == air);
            Check($"{tag} where it was drawn: the root at the break within 0.3 m of the kill's height ({breakRoot.y - killRoot.y:+0.00;-0.00} m)", Mathf.Abs(breakRoot.y - killRoot.y) <= 0.3f || !air);
            var seats = new List<Vector3>(); var door = ship.Door; if (door != null) for (int i = 0; i < ShuttleRearDoor.Seats; i++) seats.Add(door.SeatPoint(i));
            if (air)
            {
                var first = booms.Count > 0 ? booms[0] : (t: 0f, at: Vector3.zero, strength: 0f);
                bool lowBoom = booms.Any(b => b.t <= 1f && Over(b.at) < 1.5f);
                Check($"{tag} the explosion bursts at the hull's middle up there ({(booms.Count > 0 ? Vector3.Distance(first.at, centre).ToString("F2") + " m from it, " + Over(first.at).ToString("F2") + " m over the ground" : "none seen")}), none at the ground under it", booms.Count > 0 && Vector3.Distance(first.at, centre) <= 0.3f && Over(first.at) >= OzShuttleBreakup.LastBreakFeet + 1.5f && !lowBoom);
                Check($"{tag} every piece starts at its place on the drawn hull (at most {OzShuttleBreakup.LastSpawnOffM:F2} m off) and no lower than the pads ({OzShuttleBreakup.LastSpawnLowM:F2} m against {OzShuttleBreakup.LastBreakFeet:F2} m)", OzShuttleBreakup.LastSpawnOffM <= 0.3f && OzShuttleBreakup.LastSpawnLowM >= OzShuttleBreakup.LastBreakFeet - 0.5f);
            }
            else
            {
                Check($"{tag} on the ground: the blast at ground level ({(booms.Count > 0 ? Over(booms[0].at).ToString("F2") : "-")} m), the pieces from ground level ({OzShuttleBreakup.LastSpawnLowM:F2} .. {OzShuttleBreakup.LastSpawnHighM:F2} m)", booms.Count > 0 && Over(booms[0].at) <= 3.2f && OzShuttleBreakup.LastSpawnLowM <= 2.5f && OzShuttleBreakup.LastSpawnHighM <= 6f);
            }
            BigBlastLines(k, tag, air);                                                                 // K396: the big blast and the launch (OzShuttleDeathAltitudeProof.K396.cs)
            // the bodies' start: at their seats on the drawn hull
            float worstSeat = 0f; int nb = 0;
            foreach (var b in OzShuttleCrewFall.Last) { if (!b) continue; nb++; float d = 99f; foreach (var q in seats) d = Mathf.Min(d, Vector3.Distance(b.transform.position, q)); if (seats.Count > 0) worstSeat = Mathf.Max(worstSeat, d); }
            var b0 = BodyHeights(); var p0 = PieceHeights();
            Log($"{tag} at the break: pieces over the ground (min / median / max) {p0}; bodies' hips {b0}; bodies at most {worstSeat:F2} m from a seat of the drawn hull");
            Check($"{tag} everyone aboard is lost with it ({W.ShuttlePassengersLost - lost0} of {aboard}) and thrown out as bodies ({BoardingView.ShuttleBodies - bodies0})", aboard > 0 && W.ShuttlePassengersLost - lost0 == aboard && BoardingView.ShuttleBodies - bodies0 >= aboard);
            Check($"{tag} the bodies start at their seats in the drawn hull (at most {worstSeat:F2} m off, 0.3 allowed){(air ? $" up there (hips from {b0.lo:F2} m, the pads {OzShuttleBreakup.LastBreakFeet:F2} m)" : "")}", nb >= aboard && worstSeat <= 0.3f && (!air || b0.lo >= OzShuttleBreakup.LastBreakFeet - 0.3f));
            // ---- 0.5 / 1 / 2 s later
            var at = new Heights[3]; var bt = new Heights[3]; float[] marks = { 0.5f, 1f, 2f };
            for (int i = 0; i < 3; i++)
            {
                while (Time.time - brokeAt < marks[i]) yield return null;
                at[i] = PieceHeights(); bt[i] = BodyHeights();
                Log($"{tag} +{marks[i]:F1} s: pieces {at[i]}; bodies' hips {bt[i]}");
            }
            if (air)
                Check($"{tag} they fall from there: the pieces' median {p0.mid:F2} -> {at[0].mid:F2} -> {at[1].mid:F2} -> {at[2].mid:F2} m, the bodies' {b0.mid:F2} -> {bt[0].mid:F2} -> {bt[1].mid:F2} -> {bt[2].mid:F2} m (at +2 s at least 1 m under their highest at +0.5 / +1 s)",
                      at[2].mid < Mathf.Max(at[0].mid, at[1].mid) - 1f && bt[2].mid < Mathf.Max(bt[0].mid, bt[1].mid) - 1f);
            VaryLines(k, tag, air);   // K399: the pieces' own throws and moments (OzShuttleDeathAltitudeProof.K399.cs)
            // ---- the landings
            yield return WaitFor(() => OzShuttleBreakup.LastAllRested() && OzShuttleCrewFall.LandedNow() >= OzShuttleCrewFall.Last.Count, 9f);
            yield return Wait(1.0f); yield return WaitFor(() => OzShuttleBreakup.RestClearIdle(), 2f); Shot($"{tag}_landed_h70");
            int deep = OzShuttleBreakup.ThroughEachOther(out float worst, out string which); int bad = OzShuttleBreakup.BadRests(out string bw);
            var pe = PieceHeights(); var be = BodyHeights();
            int onTop = 0; foreach (var l in OzShuttleBreakup.LastLandings) if (l.Contains("on top of")) onTop++;
            Log($"{tag} at rest: pieces {pe} (all rested {OzShuttleBreakup.LastAllRested()}, worst overlap {worst * 100f:F1} cm {which}, floating / sunk {bad} {bw}); bodies {be}, landed {OzShuttleCrewFall.LandedNow()} of {OzShuttleCrewFall.Last.Count}");
            Log($"{tag} the pieces' landing contacts ({OzShuttleBreakup.LastLandings.Count}, the highest {OzShuttleBreakup.LastLandingMaxM:F2} m; {onTop} on top of something lying): {string.Join("; ", OzShuttleBreakup.LastLandings)}");
            Log($"{tag} contacts up in the air (no landing): pieces {OzShuttleBreakup.AirContacts - air0}, bodies {OzShuttleCrewLanding.MidAir - mid0}; the bodies' landings: {string.Join("; ", OzShuttleCrewLanding.Last)}");
            float fell = 99f; foreach (var b in OzShuttleCrewFall.Last) if (b) fell = Mathf.Min(fell, b.FellM);
            int crash = Played("turret") + Played("wreck_crash"), clank = Played("dst_debris_metal") + Played("shuttle_piece_land");
            Log($"{tag} sounds since the kill: crash {Played("turret")}, wreck crash {Played("wreck_crash")}, piece landings {Played("shuttle_piece_land")}, clanks {Played("dst_debris_metal")}, body thuds {Played("dst_ragdoll_thud")} (asked +{OzBodySounds.ThudAsked - thudAsked0}), whooshes {Played("dst_ragdoll_whoosh")} (asked +{OzBodySounds.WhooshAsked - whoosh0}); fire puffs {OzShuttleBreakup.FireBurns}, crack trail puffs +{OzShuttleBreakup.CrackPuffs - puffs0}; the least fall of a body {fell:F2} m");
            int unsup = OzShuttleBreakup.Unsupported(out string uw, out float hb, out string hbOn);                       // K256h2: support and overlap, two lines
            Log($"{tag} the pieces at rest: the highest middle {pe.hi:F2} m, the highest bottom {hb:F2} m ({hbOn}); high with nothing under it {unsup} {uw}; moved out of a neighbour at rest {OzShuttleBreakup.RestClearMoves - clear0}{(OzShuttleBreakup.RestClearLog.Count > 0 ? " (" + string.Join("; ", OzShuttleBreakup.RestClearLog) + ")" : "")}");
            Check($"{tag} the pieces lie still, each on the ground or on something under it (all rested, none floating or sunk, high with nothing under it {unsup}; the highest bottom {hb:F2} m)", OzShuttleBreakup.LastAllRested() && bad == 0 && unsup == 0);
            Check($"{tag} the pieces lie on each other, not inside each other (the deepest pair {worst * 100f:F1} cm {which}, 3 cm allowed)", worst <= 0.031f);
            Check($"{tag} the pieces land with sounds: a crash and clanks / landing sounds ({crash} / {clank})", crash > 0 && clank > 0);
            int lowLand = 0, highLoose = 0;
            foreach (var l in OzShuttleBreakup.LastLandings) { if (l.EndsWith(" ground") || l.EndsWith(" water")) lowLand++; else if (!l.Contains("on top of")) highLoose++; }
            Check($"{tag} no landing effect up in the air: every landing contact is on the ground / water ({lowLand}, within {OzShuttleDeathPlan.PieceNearM:F1} m of it included) or on top of something lying ({onTop}); others {highLoose}", highLoose == 0 && lowLand > 0);
            int highRest = BodiesHighAtRest(out float highRestWorst, out string heldAtRest);                                // K256h2: held up = lying (every bone asked)
            Log($"{tag} bodies flagged landed high up and held there: {(landedOnTop.Count > 0 ? string.Join("; ", landedOnTop) : "-")}; at rest high and held: {heldAtRest}");
            Check($"{tag} the bodies land on the ground or on something lying: all landed, none flagged landed with the hips more than {K419LandHighM:F1} m up ({K419LandWhy}) and nothing under it ({landedHigh}, worst {landedHighWorst:F2} m), the thud asked ({OzBodySounds.ThudAsked - thudAsked0})", OzShuttleCrewFall.LandedNow() >= OzShuttleCrewFall.Last.Count && landedHigh == 0 && OzBodySounds.ThudAsked > thudAsked0);
            Check($"{tag} the bodies rest on the ground or on something under them (hips over 1 m with nothing under them {highRest}, worst {highRestWorst:F2} m)", highRest == 0);
            if (air)
            {
                { bool fellOk = FallLine(out string fellText); Check($"{tag} the bodies fall from up there: every body fell at least 0.7 x the height it could fall where it landed ({fellText})", fellOk); }   // K256k
                Check($"{tag} fire and smoke: the burning pieces burn as they fall and lie ({OzShuttleBreakup.Burning()} burning now){(k.expect == OzShuttleDeathPlan.Crack ? $", the crack's trails puffed (+{OzShuttleBreakup.CrackPuffs - puffs0})" : "")}", OzShuttleBreakup.Burning() >= 1 && (k.expect != OzShuttleDeathPlan.Crack || OzShuttleBreakup.CrackPuffs > puffs0));
            }
            BigLandLines(k, tag, air);                                                                  // K396: the rumble, the pockets, the column, the landing field
            Flush();
        }

        IEnumerator Strip(string tag)
        {
            for (int i = 0; i < 10; i++)
            {
                float t0 = Time.time;
                Shot($"{tag}_strip_{i:00}_h70");
                while (Time.time - t0 < 0.2f) yield return null;
            }
        }

        void Finish()
        {
            Flush();
            Log("sounds played in the whole run: " + string.Join(", ", plays.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + kv.Value)));
            Log($"counts: cracks {OzShuttleBreakup.Cracks}, crack trails {OzShuttleBreakup.CrackTrails} ({OzShuttleBreakup.CrackPuffs} puffs), drops {OzShuttleBreakup.DropBreaks} (falls {OzShuttleBreakup.Falls}, held drawn {OzShuttleBreakup.DropHolds}), frames a dead ship was kept drawn {OzShuttleBreakup.KeptDrawnFrames}, piece contacts up in the air {OzShuttleBreakup.AirContacts} (quiet clanks {OzShuttleBreakup.AirClanks}), landing contacts {OzShuttleBreakup.LandingContacts} ({OzShuttleBreakup.LandingsOnTop} on top of something), body contacts up in the air {OzShuttleCrewLanding.MidAir}, body landings {OzShuttleCrewLanding.Landings}");
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS), {frames} frames, {Time.unscaledTime - T0:F0} s");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_K256h_" + mode.Replace(',', '_') + ".txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogError("[OzShuttleDeathAltitudeProof] could not write the log: " + ex.Message); }
            if (dn) dn.running = true;
            camAt = null; followShip = false;
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject, 0.5f);
        }
    }
}
