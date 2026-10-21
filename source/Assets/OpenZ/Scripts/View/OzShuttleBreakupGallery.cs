using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256e Play proof of the shuttle's break-up and of the weapons gallery's blast weapons (rule DV118, the third round; menu
    /// OpenZ/V1.1/Shuttle: K256e Break-up Proof, Editor/OzShuttleBreakupGalleryMenu.cs), on level 16 in Play. Counted PASS / FAIL lines,
    /// the whole take's sound log (flushed every 2 s, no cap), full-screen pictures from the normal RTS camera at 70 m with a FIXED camera
    /// (it never follows the ship), strips as half-size frames in a sub-folder.
    /// (G) the ship HOVERS (over a spot it cannot land on, so it holds still) while each blast weapon in turn fires at it from 12 m:
    /// the electrobot's bolt, the light tank, the mech rockets, the gun cannon, the missile cannon (the review's retakes: no ground light
    /// or shock ring under the flying ship, counted by OpenZFxPlayer's air-burst gate, and the exp2 blasts near it read from its log), the
    /// grunts' grenades (at least 3, one on the hull, one picture with a grenade near the hull) and, moved into the blue fort's reach with
    /// the fort's roof defenders and the other shooters round it taken out, the fort's own gun (at least 3 shells, one on the hull, a
    /// picture of a shell in flight from the fort to the hull and one of its blast on the hull).
    /// (K) first a light tank destroyed on open ground (the wreck the shuttle's is compared with: stills at +1.5 s and +6 s, its luma),
    /// then four kills, each after a fresh start of the level with a squad aboard and the ship hovering: a rocket salvo (the mid-air
    /// burst), a howitzer shell and the fort's gun (the split: a second blast, a 12-15 m scatter), rifle fire (the drop: 1.0-1.5 s tilted
    /// and smoking, the break on the ground). Each: a 36-frame strip (0.25 s apart, from the hit to rest), the breaking / landed / 6 s
    /// stills, the body panels' median luma by day (45-55; the intact hull and the grass beside it measured the same way), the fires and
    /// the smoulder at 6 s, the scatter, every piece's first contact heard, the fire's crackle, no ground ring under the flying ship.
    /// Output folder: <see cref="Folder"/> (built at run time from the project's place). Test only.
    /// </summary>
    public sealed partial class OzShuttleBreakupGallery : MonoBehaviour                        // K256j: + OzShuttleBreakupGallery.KeepOn.cs (the fort stages' staging)
    {
        public static string Report = "idle";
        /// <summary>The output folder: the work tree's records folder found upward from the project at run time
        /// (milestones/shuttle_round3_breakup), else the player's own data folder.</summary>
        public static string Folder()
        {
            const string sub = "shuttle_round3_breakup";
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }
        public static void Run(string mode)
        {
            Report = "running " + mode;
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var go = new GameObject("OzShuttleBreakupGallery"); var p = go.AddComponent<OzShuttleBreakupGallery>(); p.mode = mode;
        }

        string mode = "GK";
        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder(); readonly StringBuilder snd = new StringBuilder();
        int fails, passes, frames; string dir; float T0, flushedTo; Vector3? camAt;
        readonly List<ZObject> made = new List<ZObject>();
        readonly Dictionary<string, int[]> soundRows = new Dictionary<string, int[]>();   // key -> played, dropped
        readonly Dictionary<string, float> soundLow = new Dictionary<string, float>();

        void Start()
        {
            DontDestroyOnLoad(gameObject);                                                              // the kills each start the level afresh
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Update()
        {
            if (camAt.HasValue && rts) rts.focus = new Vector3(camAt.Value.x, 0f, camAt.Value.z);       // a FIXED camera (never following the ship)
            if (T0 > 0f && Time.unscaledTime - flushedTo > 2f) Flush();
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleBreakupGallery] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }

        // ------------------------------------------------------------------ the whole take's sound log (2 s flushes, no cap)
        void Flush()
        {
            float to = flushedTo;
            foreach (var e in OzSoundLog.Since(flushedTo))
            {
                if (e.t <= flushedTo) continue;
                snd.AppendLine($"  {e.t - T0,8:F2} {e.src} {e.key} {e.clip} {e.outcome} vol {e.vol:F2} dist {e.dist:F1}");
                to = Mathf.Max(to, e.t);
                if (e.key == null) continue;
                if (!soundRows.TryGetValue(e.key, out var row)) soundRows[e.key] = row = new int[2];
                bool played = e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP") || e.src == "DOOR");
                if (played) { row[0]++; soundLow[e.key] = soundLow.TryGetValue(e.key, out var lo) ? Mathf.Min(lo, e.vol) : e.vol; } else row[1]++;
            }
            flushedTo = Mathf.Max(to, Time.unscaledTime - 0.01f);
        }
        int Played(string key) { Flush(); return soundRows.TryGetValue(key, out var r) ? r[0] : 0; }

        // ------------------------------------------------------------------ helpers (as the K256b proof's)
        ZObject Squad(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Vehicle(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject("vehicle", type, tx * 16, ty * 16, t); if (o != null) g.SpawnUnitView(o);
            return o;
        }
        ZObject Cannon(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject("cannon", type, tx * 16, ty * 16, t); if (o != null) g.SpawnUnitView(o);
            return o;
        }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
        void Attack(ZObject o) { if (o != null && !o.Destroyed && s != null) W.SendWaypoints(them, o, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } }); }
        /// <summary>The staging's shooters (or the fort's defenders) leave without a trace (no death drawn): out of the rules, their views gone.</summary>
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
        void Heal() { if (s != null && !s.Destroyed && s.Health < s.MaxHealth) s.SetHealth(s.MaxHealth); }
        Vector3 W3(int x, int y) => ZGame.PxToWorld(x, y);
        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
        int Open(int tx, int ty, int r) { int n = 0; for (int j = -r; j <= r; j++) for (int i = -r; i <= r; i++) if (W.Path.TilePassable(tx + i, ty + j, true)) n++; return n; }

        /// <summary>A spot the ship cannot land on (so it hovers there and holds still), flat distance minM-maxM m from nearW, with open
        /// ground round it (the pieces fall on grass) and, when asked, the shooters' row 12 m to its south open: the nearest of the most open.</summary>
        int[] HoverSpot(Vector3 nearW, float minM, float maxM, bool shooters, System.Func<int, int, bool> also = null)
        {
            int[] best = null; float bestScore = float.MaxValue;
            for (int ty = 6; ty < W.Map.height - 12; ty++)
                for (int tx = 6; tx < W.Map.width - 6; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; float d = Flat(W3(x, y), nearW);
                    if (d < minM || d > maxM) continue;
                    var q = W.ShuttleClamp(x, y); if (q.x != x || q.y != y) continue;
                    if (W.ShuttlePadFree(x, y, s, out string why)) continue;
                    if (why == "units" || why == "map edge" || why == "no map" || why == "repair bay busy" || string.IsNullOrEmpty(why)) continue;
                    if (shooters) { bool ok = true; for (int k = -2; k <= 2 && ok; k++) ok = W.Path.TilePassable(tx + k, ty + 6, true) && W.Path.TilePassable(tx + k, ty + 7, true); if (!ok) continue; }
                    if (also != null && !also(x, y)) continue;
                    float score = d + 4f * (49 - Open(tx, ty, 3));                                       // open grass round it first, then near
                    if (score < bestScore) { bestScore = score; best = new[] { x, y }; }
                }
            return best;
        }

        /// <summary>The ship flown to (x, y) and hovering there (it cannot land); the fixed camera on that spot.</summary>
        IEnumerator HoverAt(int[] spot, string tag)
        {
            camAt = W3(spot[0], spot[1]);
            Move(spot[0], spot[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover && ship.FeetHeight > 6f, 70f);
            yield return Wait(2.5f);                                                                     // past the refusal's red shadow and voice
            Log($"{tag} hovering over tile ({spot[0] >> 4},{spot[1] >> 4}) ({s.ShuttleNoLand}), pads {ship.FeetHeight:F1} m up; the camera fixed there at 70 m");
        }

        /// <summary>The fort's roof defenders and the enemy's other shooters round it leave (the shells counted are the fort gun's own).</summary>
        int ClearFort(ZObject fort)
        {
            var list = new List<ZObject>(); var fc = W3(fort.CenterX, fort.CenterY);
            foreach (var o in W.Objects)
                if (o != fort && o.Owner == them && !o.Destroyed && o.KillAt < 0 && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && o.Leader == null
                    && Flat(W3(o.CenterX, o.CenterY), fc) < 50f) list.Add(o);
            int n = list.Count; Clear(list);
            KeepMatchOn(fort);                                                                           // K256j (M16): with no enemy unit left the rules end the game and the fort's gun falls silent
            return n;
        }

        /// <summary>The time (s) of the last drawn flight of `key`'s rocket / shell / grenade (OzShuttleAim's FIRED line), -1 = none.</summary>
        static float LastFlight(string key)
        {
            var lines = OzShuttleAim.Lines.ToString().Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var l = lines[i]; if (!l.Contains(" " + key + "#") || !l.Contains(" FIRED ")) continue;
                int j = l.IndexOf("flight ", System.StringComparison.Ordinal); if (j < 0) return -1f;
                int e = l.IndexOf(" s", j + 7, System.StringComparison.Ordinal); if (e < 0) return -1f;
                return float.TryParse(l.Substring(j + 7, e - j - 7).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : -1f;
            }
            return -1f;
        }

        /// <summary>exp2 blasts (their own ground light and two ground rings, OpenZFxPlayer.Exp2.cs) drawn more than 2 m up within 10 m of the
        /// flying ship since the exp2 count was `from` (OpenZFxPlayer.Exp2Total: the log keeps only its last 400 lines, so a line index
        /// stops moving once it is full; the count never does).</summary>
        int Exp2NearShip(int from, out int upTo)
        {
            int n = 0; var log = OpenZFxPlayer.Exp2Log; upTo = OpenZFxPlayer.Exp2Total;
            int fresh = Mathf.Clamp(upTo - from, 0, log.Count);
            for (int i = log.Count - fresh; i < log.Count; i++)
            {
                var l = log[i]; int a = l.IndexOf("at=(", System.StringComparison.Ordinal); if (a < 0) continue;
                int b = l.IndexOf(')', a); if (b < 0) continue;
                var parts = l.Substring(a + 4, b - a - 4).Split(',');
                if (parts.Length == 6) parts = new[] { parts[0] + "." + parts[1], parts[2] + "." + parts[3], parts[4] + "." + parts[5] };   // a decimal-comma culture
                if (parts.Length < 3) continue;
                if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
                var p = new Vector3(x, y, z);
                if (ship && Flat(p, ship.transform.position) < 10f && p.y - g.GroundY(x, z) > 2f) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ pictures: strips and the luma count
        IEnumerator Strip(string name, int n, float every)
        {
            var sub = System.IO.Path.Combine(dir, name); System.IO.Directory.CreateDirectory(sub);
            for (int i = 0; i < n; i++)
            {
                float t0 = Time.unscaledTime;
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex)
                {
                    var rt = RenderTexture.GetTemporary(tex.width / 2, tex.height / 2, 0);
                    Graphics.Blit(tex, rt);
                    var small = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    var was = RenderTexture.active; RenderTexture.active = rt; small.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); small.Apply(); RenderTexture.active = was;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(sub, $"{name}_{i:00}.jpg"), small.EncodeToJPG(88));
                    RenderTexture.ReleaseTemporary(rt); Destroy(small); Destroy(tex);
                }
                while (Time.unscaledTime - t0 < every) yield return null;
            }
            Log($"strip {name}: {n} frames, {every:F2} s apart (half size, {name}/)");
        }

        IEnumerator AtTime(float at, System.Action then) { while (Time.unscaledTime < at) yield return null; then(); }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static float Median(List<float> l) { if (l.Count == 0) return -1f; l.Sort(); return l[l.Count / 2]; }

        /// <summary>The screen's luma (sRGB 0-255) at the visible points of the given meshes (faces toward the camera, nothing in front),
        /// and at ground points round `around` (radius 14-20 m) for the grass beside them. which: -1 every vertex, 0 the outer surface
        /// only (not the last sub-mesh: a panel's inside), 1 the inside only.</summary>
        IEnumerator MeasureLuma(List<(MeshFilter mf, Collider own)> parts, Vector3 around, bool needRay, List<float> outParts, List<float> outGround, int which = -1, bool hideFx = false)
        {
            var cam = Camera.main; if (!cam) yield break;
            var pts = new List<Vector2>(); var gpts = new List<Vector2>();
            var cp = cam.transform.position;
            foreach (var (mf, own) in parts)
            {
                if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var m = mf.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices; var ns = mf.sharedMesh.normals;
                HashSet<int> inside = null;
                if (which >= 0 && mf.sharedMesh.subMeshCount > 1) inside = new HashSet<int>(mf.sharedMesh.GetTriangles(mf.sharedMesh.subMeshCount - 1));
                int step = Mathf.Max(1, vs.Length / 400);
                for (int i = 0; i < vs.Length; i += step)
                {
                    if (which >= 0 && (inside != null && inside.Contains(i)) != (which == 1)) continue;
                    var w = m.MultiplyPoint3x4(vs[i]); var n = ns.Length == vs.Length ? m.MultiplyVector(ns[i]).normalized : Vector3.up;
                    var to = cp - w; if (Vector3.Dot(n, to.normalized) < 0.15f) continue;
                    if (needRay && Physics.Raycast(cp, (w - cp).normalized, out var hit, to.magnitude - 0.12f, ~0, QueryTriggerInteraction.Ignore) && hit.collider != own) continue;
                    var sp = cam.WorldToScreenPoint(w + n * 0.02f); if (sp.z <= 0f) continue;
                    pts.Add(sp);
                }
            }
            for (int i = 0; i < 48; i++)
            {
                float a = i * 0.1309f * 2f, r = 14f + (i % 4) * 2f;
                var w = around + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); w.y = g.GroundY(w.x, w.z) + 0.02f;
                if (Physics.Raycast(cp, (w - cp).normalized, out var hit, (w - cp).magnitude - 0.3f, ~0, QueryTriggerInteraction.Ignore) && !(hit.collider.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(hit.collider))) continue;
                var sp = cam.WorldToScreenPoint(w); if (sp.z > 0f) gpts.Add(sp);
            }
            var hidden = new List<Renderer>();
            if (hideFx)
            {   // the paint itself: the effect player's particles (fire, smoke) hidden for the counted frame only
                var fxp = OpenZFxPlayer.I;
                if (fxp) foreach (var r in fxp.GetComponentsInChildren<ParticleSystemRenderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            foreach (var r in hidden) if (r) r.enabled = true;
            if (!tex) yield break;
            float sx = tex.width / (float)Screen.width, sy = tex.height / (float)Screen.height;
            foreach (var p in pts) { int x = (int)(p.x * sx), y = (int)(p.y * sy); if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) outParts.Add(Luma(tex.GetPixel(x, y))); }
            foreach (var p in gpts) { int x = (int)(p.x * sx), y = (int)(p.y * sy); if (x >= 0 && y >= 0 && x < tex.width && y < tex.height) outGround.Add(Luma(tex.GetPixel(x, y))); }
            Destroy(tex);
        }

        // ------------------------------------------------------------------ the run
        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (!Bind()) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on, not 'Zod exact')"); fails++; Finish(); yield break; }
            T0 = Time.unscaledTime; flushedTo = T0;
            yield return WaitFor(() => OzShuttleBreakup.PlanDone, 30f);                                    // the break-up plan is cut one step a frame from the level's start
            Log($"K256e break-up proof {mode}, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}; break-up plan: {OzShuttleBreakup.LastPlan}");
            Log($"the body panels (ship root space, m): {OzShuttleBreakup.LastShellSizes} largest side {OzShuttleBreakup.ShellMaxM:F2} m (the ship's width 6.39 m: at most 0.45 x = 2.88 m)");
            Check("M2 the rear body is cut into >= 8 curved panels, none larger than 0.45 x the ship's width (2.88 m)", OzShuttleBreakup.ShellPieces >= 8 && OzShuttleBreakup.ShellMaxM <= 2.88f);
            yield return Wait(1.0f);
            if (mode.Contains("G")) yield return Gallery();
            if (mode.Contains("K")) { yield return TankWreck(); yield return Kills(); }
            Finish();
        }

        bool Bind()
        {
            g = ZGame.I; W = g ? g.World : null; if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) return false;
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            rts = FindFirstObjectByType<RtsCamera>();
            W.Bots.Clear(); dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            made.Clear(); camAt = null;
            return true;
        }

        // ------------------------------------------------------------------ G: blast weapons at the hovering ship
        struct Gun { public string key, label, kind, type; public Gun(string k, string l, string kd, string t) { key = k; label = l; kind = kd; type = t; } }
        static readonly Gun[] AirGuns =
        {
            new Gun("electrobot", "electrobot tesla bolts", "squad", "electrobot"), new Gun("light", "light tank", "vehicle", "light"),
            new Gun("medium", "mech rockets", "vehicle", "medium"), new Gun("gun", "gun cannon", "cannon", "gun"),
            new Gun("missile_cannon", "missile cannon", "cannon", "missile_cannon"), new Gun("grenade grunt", "grunt grenades", "grenades", "grunt"),
        };
        static readonly string[] Retake = { "G07", "G09", "G10", "G15", "G17", "G18" };

        IEnumerator Gallery()
        {
            Log("G blast weapons at the HOVERING ship (it holds still over a spot it cannot land on), healed between them; the camera fixed at 70 m");
            var spot = HoverSpot(ship.transform.position, 40f, 110f, true);
            if (spot == null) { Check("G a spot to hover over with open ground and the shooters' row", false); yield break; }
            int hp0 = s.MaxHealth; s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);
            yield return HoverAt(spot, "G");
            int stx = spot[0] >> 4, sty = (spot[1] >> 4) + 6;
            OzShuttleAim.ProbeReset(); OzShuttleAim.Recording = true;
            int rings0 = OzShuttleBreakup.GroundFxUnderAirShip, gated0 = OzShuttleBreakup.AirBurstsGated, e2from = OpenZFxPlayer.Exp2Total, e2total = 0;
            for (int k = 0; k < AirGuns.Length; k++)
            {
                var gun = AirGuns[k]; string tag = Retake[k];
                int r0 = OzShuttleBreakup.GroundFxUnderAirShip;
                yield return OneGun(gun, tag, stx, sty);
                int e2 = Exp2NearShip(e2from, out e2from); e2total += e2;
                OzShuttleAim.Probes.TryGetValue(gun.key, out var p);
                Log($"{tag} {gun.label}: shots {(p != null ? p.shots : 0)}, blasts {(p != null ? p.blasts : 0)}, on the hull {(p != null ? p.blastsOnHull : 0)} (+ hits on the hull {(p != null ? p.onHull : 0)}/{(p != null ? p.hits : 0)}), lowest blast {(p != null && p.blasts > 0 ? p.minBlastH : 0f):F1} m; ground light / rings drawn under the flying ship {OzShuttleBreakup.GroundFxUnderAirShip - r0}; exp2 blasts near it (their own ground rings) {e2}");
                Check($"M3 {tag} {gun.label}: no ground light or shock ring under the flying ship (air-burst gate)", OzShuttleBreakup.GroundFxUnderAirShip - r0 == 0);
                if (gun.kind == "grenades")
                    Check($"M16 {tag} grenades at the ship: >= 3 thrown, >= 1 bursting on the hull", p != null && p.shots >= 3 && p.blastsOnHull >= 1);
            }
            yield return FortGun("G19", false);
            int e2fort = Exp2NearShip(e2from, out e2from); e2total += e2fort;                              // the fort's shells (exp2 Big) too
            OzShuttleAim.Recording = false;
            Log($"G air bursts gated {OzShuttleBreakup.AirBurstsGated - gated0}; exp2 blasts near the flying ship {e2total} (G19 the fort's gun {e2fort}; at the flying ship LegacyMatch / MlrsSalvo draw the gated blast instead: no exp2 ground light or rings)");
            Check("M3 the exp2 blasts near the flying ship (their own ground rings), the fort's gun included: 0", e2total == 0);
            Check("M3 the whole gallery: ground light / rings under the flying ship = 0", OzShuttleBreakup.GroundFxUnderAirShip - rings0 == 0);
            if (s != null && !s.Destroyed) { s.MaxHealth = hp0; s.SetHealth(hp0); }
            Log("G the drawn shots per weapon:\n" + OzShuttleAim.ProbeText().TrimEnd());
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "gallery_shots_E.txt"), OzShuttleAim.ProbeText() + "\n" + OzShuttleAim.Lines); } catch { }
        }

        /// <summary>One weapon at the hovering ship: its shooters 12 m south, at least 4 drawn shots or blasts (up to 22 s), the ship healed,
        /// a picture at its first blast near the hull (a grenade: in flight near the hull).</summary>
        IEnumerator OneGun(Gun gun, string tag, int stx, int sty)
        {
            Log($"{tag} {gun.label}");
            var list = new List<ZObject>();
            switch (gun.kind)
            {
                case "squad": list.Add(Squad(gun.type, stx, sty, them)); break;
                case "grenades": { var sq = Squad(gun.type, stx, sty, them); if (sq != null) sq.Grenades = 8; list.Add(sq); break; }
                case "vehicle": list.Add(Vehicle(gun.type, stx, sty, them)); break;
                case "cannon": list.Add(Cannon(gun.type, stx, sty, them)); break;
            }
            list.RemoveAll(o => o == null);
            foreach (var o in list) Attack(o);
            OzShuttleAim.Probes.TryGetValue(gun.key, out var p0); int sh0 = p0 != null ? p0.shots : 0, bl0 = p0 != null ? p0.blasts : 0;
            float t = 0f, nudge = 2f; bool pic = false;
            while (t < 22f)
            {
                if (s == null || s.Destroyed || ship.BrokenUp) break;
                if (s.Health < s.MaxHealth / 2) Heal();
                if ((nudge -= Time.deltaTime) <= 0f) { nudge = 2f; foreach (var o in list) if (!o.Destroyed && o.AttackObject != s && o.Waypoints.Count == 0) Attack(o); }
                OzShuttleAim.Probes.TryGetValue(gun.key, out var p); int shots = (p != null ? p.shots : 0) - sh0, blasts = (p != null ? p.blasts : 0) - bl0;
                if (!pic && gun.kind == "grenades" && shots > 0)
                {   // a grenade in flight near the hull: 75 % of its drawn flight
                    pic = true; float fl = LastFlight(gun.key); yield return Wait(fl > 0f ? fl * 0.75f : 0.5f); Shot($"E_{tag}_grenade_near_hull_h70"); continue;
                }
                if (!pic && gun.kind != "grenades" && (blasts > 0 || (shots > 0 && gun.kind == "squad")))
                { pic = true; yield return Wait(0.08f); Shot($"E_{tag}_{gun.key.Replace(' ', '_')}_air_blast_h70"); continue; }
                bool enough = gun.kind == "grenades" ? blasts >= 3 && (p != null && p.blastsOnHull >= 1) : shots >= 4;
                if (enough && t >= 6f) break;
                t += Time.deltaTime; yield return null;
            }
            Clear(list); Heal(); yield return Wait(1.5f); Heal();
        }

        /// <summary>The fort's own gun at the hovering ship: the ship moved into the blue fort's reach (over a spot it cannot land on), the
        /// fort's defenders and the other shooters round it gone; at least 3 shells (up to 90 s), one on the hull; pictures: a shell in
        /// flight from the fort to the hull, its blast on the hull. kill = the ship is let down to 1 HP when the fort fires (K3).</summary>
        IEnumerator FortGun(string tag, bool kill)
        {
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == them && !o.Destroyed && !o.FortGunDestroyed);
            if (fort == null) { Check($"{tag} an enemy fort with its gun", false); yield break; }
            var hu = W.S != null ? W.S.Unit("howitzer") : null; long reach = hu != null ? ZObject.FortGunRange(hu) - 24 : 210;
            var fc = W3(fort.CenterX, fort.CenterY);
            var spot = HoverSpot(fc, 16f, 34f, false, (x, y) => { long ex = x - fort.CenterX, ey = y - fort.CenterY; return fort.FortGunCanReach(x, y) && ex * ex + ey * ey <= reach * reach; });
            if (spot == null) { Check($"{tag} a spot to hover over in the fort gun's reach", false); yield break; }
            int gone = ClearFort(fort);
            Log($"{tag} the fort's own gun: the ship to tile ({spot[0] >> 4},{spot[1] >> 4}), {Flat(W3(spot[0], spot[1]), fc):F0} m from the fort #{fort.Id}; its defenders and the shooters round it taken out: {gone}");
            float fly = 0f; Move(spot[0], spot[1]); camAt = Vector3.Lerp(W3(spot[0], spot[1]), fc, 0.45f);   // the camera between the fort and the ship: the shell's whole way in view
            while (fly < 80f && !(s.ShuttleState == ZShuttleState.Hover && ship.FeetHeight > 6f)) { if (s.Health < s.MaxHealth * 0.6f) Heal(); fly += Time.deltaTime; yield return null; }
            yield return Wait(2.0f);
            bool was = OzShuttleAim.Recording; OzShuttleAim.Recording = true;
            OzShuttleAim.Probes.TryGetValue("fort gun", out var p0); int sh0 = p0 != null ? p0.shots : 0, oh0 = p0 != null ? p0.blastsOnHull : 0, bl0 = p0 != null ? p0.blasts : 0;
            float t = 0f; bool picFly = false, picHit = false; int lastShots = 0;
            double fw0 = W.Time, fwin = FortWindowSec(fort, out string fwhy); float fu0 = Time.unscaledTime;    // the window follows the rules' reload (OzShuttleBreakupGallery.Window.cs)
            Log($"{tag} the window: {fwhy}");
            while (FortWindowOpen(fw0, fwin, fu0))
            {
                if (s == null || ship.BrokenUp) break;
                if (s.Destroyed) { yield return WaitFor(() => ship.BrokenUp, 3f); break; }                         // K419b: the rules' death comes a frame before the view's break: the stage waits for it
                OzShuttleAim.Probes.TryGetValue("fort gun", out var p);
                int shots = (p != null ? p.shots : 0) - sh0, onHull = (p != null ? p.blastsOnHull : 0) - oh0;
                FortHold(kill, shots);                                                                   // K256j (M16): G19 nothing brings it down while the shells are counted; K3 held at FortKillHp from the first shell (OzShuttleBreakupGallery.KeepOn.cs)
                if (!picFly && shots > 0) { picFly = true; lastShots = shots; float fl = LastFlight("fort gun"); yield return Wait(fl > 0f ? fl * 0.5f : 0.4f); Shot($"E_{tag}_fort_gun_shell_in_flight_h70"); continue; }
                if (!picHit && onHull > 0) { picHit = true; yield return Wait(0.05f); Shot($"E_{tag}_fort_gun_blast_on_hull_h70"); continue; }
                lastShots = shots;
                if (!kill && shots >= 3 && onHull >= 1 && t >= 8f) break;
                t += Time.deltaTime; yield return null;
            }
            OzShuttleAim.Probes.TryGetValue("fort gun", out var pe);
            int S = (pe != null ? pe.shots : 0) - sh0, B = (pe != null ? pe.blasts : 0) - bl0, H = (pe != null ? pe.blastsOnHull : 0) - oh0;
            Log($"{tag} the fort's gun: {S} shells, {B} blasts, {H} on the hull; pictures: in flight {picFly}, on the hull {picHit}");
            if (!kill) Check($"M16 {tag} the fort's gun at the ship: >= 3 shells, >= 1 on the hull, a shell seen in flight from the fort", S >= 3 && H >= 1 && picFly);
            OzShuttleAim.Recording = was;
        }

        // ------------------------------------------------------------------ K: the tank wreck to compare with, then the four kills
        IEnumerator TankWreck()
        {
            Log("W a blue light tank destroyed on open ground: the vehicle wreck the shuttle's pieces are compared with (stills +1.5 s and +6 s, the same luma count)");
            var spot = HoverSpot(ship.transform.position, 40f, 110f, true);
            if (spot == null) { Log("W no open spot"); yield break; }
            int tx = spot[0] >> 4, ty = (spot[1] >> 4) + 3;
            var tank = Vehicle("light", tx, ty, them);
            if (tank == null) { Log("W no tank made"); yield break; }
            camAt = W3(tank.CenterX, tank.CenterY);
            yield return Wait(2.0f);
            tank.DamageHealth(tank.Health); W.UpdateObjectHealth(tank);
            yield return Wait(1.5f); Shot("E_W_tank_wreck_landed_h70");
            var parts = new List<(MeshFilter, Collider)>();
            if (g.Views.TryGetValue(tank.Id, out var tv) && tv) foreach (var mf in tv.GetComponentsInChildren<MeshFilter>()) { var mr = mf.GetComponent<MeshRenderer>(); if (mr && mr.enabled) parts.Add((mf, null)); }
            var lp = new List<float>(); var lg = new List<float>();
            yield return MeasureLuma(parts, W3(tank.CenterX, tank.CenterY), false, lp, lg);
            Log($"W the tank wreck's luma: median {Median(lp):F0} ({lp.Count} points), the grass round it {Median(lg):F0} ({lg.Count})");
            yield return Wait(4.5f); Shot("E_W_tank_wreck_6s_h70");
            yield return Wait(1.0f);
        }

        static readonly Gun[] KillGuns =
        {
            new Gun("missile_launcher", "a rocket salvo", "vehicle", "missile_launcher"), new Gun("howitzer", "a howitzer shell", "cannon", "howitzer"),
            new Gun("fort gun", "the fort's gun", "fort", "fort"), new Gun("grunt", "rifle fire", "squad", "grunt"),
        };
        static string[] KillHow => new[] { "burst", "split", "split", OzShuttleBreakup.DropWhole ? "drop" : OzShuttleDeathPlan.Crack };   // K256h: rifle fire cracks it open in the air (the drop only with its switch)

        IEnumerator Kills()
        {
            var hows = new HashSet<string>(); float burstWide = 0f;
            for (int k = 0; k < KillGuns.Length; k++)
            {
                var gun = KillGuns[k]; string tag = "K" + (k + 1);
                Log($"{tag} a fresh start of level 16 for the kill by {gun.label}");
                var old = g; OzScenes.Restart();                                                          // the game's own restart (the pause menu's)
                bool seen = false; float waited = 0f;
                while (waited < 150f)
                {
                    if (ZodBriefing.BriefingActive) seen = true;
                    var ng = ZGame.I;
                    if (ng && ng != old && ng.World != null && ng.World.PlayableShuttle != null && OzPlayableShuttle.I && !ZodBriefing.BriefingActive && (seen || waited > 20f)) break;
                    waited += Time.unscaledDeltaTime; yield return null;
                }
                if (!Bind()) { Check($"{tag} level 16 back with its shuttle", false); yield break; }
                yield return Wait(1.5f);
                var sq = Squad("grunt", (s.CenterX >> 4) + 3, (s.CenterY >> 4) + 4, me); yield return Wait(0.6f);
                Enter(sq); yield return WaitFor(() => s.Passengers.Count > 0, 30f); yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut, 15f);
                int aboard = s.Passengers.Count;
                int splits0 = OzShuttleBreakup.Splits, drops0 = OzShuttleBreakup.DropBreaks, second0 = OzShuttleBreakup.SecondBlasts, heard0 = OzShuttleBreakup.FirstContactsHeard, firsts0 = OzShuttleBreakup.FirstContacts,
                    rings0 = OzShuttleBreakup.GroundFxUnderAirShip, loops0 = Played("dst_fire_loop"), land0 = Played("shuttle_piece_land"), clank0 = Played("dst_debris_metal");
                var shooters = new List<ZObject>();
                if (gun.kind == "fort")
                {
                    yield return FortKill(tag);
                }
                else
                {
                    var spot = HoverSpot(ship.transform.position, 40f, 110f, true);
                    if (spot == null) { Check($"{tag} a spot to hover over", false); continue; }
                    int hp0 = s.MaxHealth; s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);
                    yield return HoverAt(spot, tag);
                    yield return HullLuma(tag);
                    s.MaxHealth = hp0; s.SetHealth(1);                                                        // the first real hit brings it down
                    int stx = spot[0] >> 4, sty = (spot[1] >> 4) + 6;
                    switch (gun.kind)
                    {
                        case "squad": shooters.Add(Squad(gun.type, stx, sty, them)); break;
                        case "vehicle": shooters.Add(Vehicle(gun.type, stx, sty, them)); break;
                        case "cannon": shooters.Add(Cannon(gun.type, stx, sty, them)); break;
                    }
                    shooters.RemoveAll(o => o == null);
                    foreach (var o in shooters) Attack(o);
                    float t = 0f, nudge = 2f;
                    while (t < 45f && !ship.BrokenUp) { if ((nudge -= Time.deltaTime) <= 0f) { nudge = 2f; foreach (var o in shooters) if (!o.Destroyed && o.AttackObject != s && o.Waypoints.Count == 0) Attack(o); } t += Time.deltaTime; yield return null; }
                }
                if (!ship.BrokenUp && s != null && s.Destroyed) yield return WaitFor(() => ship.BrokenUp, 3f);       // K419b: killed in the stage, broken up a frame later
                if (!ship.BrokenUp) { Check($"{tag} {gun.label} brings it down within the time", false); s.DamageHealth(s.Health); W.UpdateObjectHealth(s); yield return WaitFor(() => ship.BrokenUp, 5f); }
                float brokeAt = Time.unscaledTime; int burningAt10 = -1;
                StartCoroutine(Strip($"E_{tag}_{gun.key.Replace(' ', '_')}_strip", 36, 0.25f));
                StartCoroutine(AtTime(brokeAt + 10f, () => burningAt10 = OzShuttleBreakup.Burning()));      // the three fires, 10 s after the break
                yield return Wait(OzShuttleBreakup.Falling ? 0.6f : 0.35f); Shot($"E_{tag}_{gun.key.Replace(' ', '_')}_breaking_h70");
                yield return WaitFor(() => !OzShuttleBreakup.Falling, 4f);
                yield return WaitFor(() => OzShuttleBreakup.LastAllRested(), 9f);
                float restAt = Time.unscaledTime;
                yield return Wait(1.0f); Shot($"E_{tag}_{gun.key.Replace(' ', '_')}_landed_h70");
                // the body panels' luma (by day) against the grass beside them
                var parts = new List<(MeshFilter, Collider)>();
                foreach (var pc in OzShuttleBreakup.LastPieces) if (pc && pc.Kind == "body" && pc.PieceCollider) { var mf = pc.PieceCollider.GetComponent<MeshFilter>(); if (mf) parts.Add((mf, pc.PieceCollider)); }
                var lp = new List<float>(); var lg = new List<float>(); var li = new List<float>(); var la = new List<float>(); var lg2 = new List<float>(); var lg3 = new List<float>();
                yield return MeasureLuma(parts, OzShuttleBreakup.LastBreakGround, true, lp, lg, 0, true);   // the painted outer faces (fire and smoke hidden for that frame)
                yield return MeasureLuma(parts, OzShuttleBreakup.LastBreakGround, true, li, lg2, 1);   // the grey ribbed insides
                yield return MeasureLuma(parts, OzShuttleBreakup.LastBreakGround, true, la, lg3, -1);  // all of them as they lie
                float bodyL = Median(lp), grassL = Median(lg), innerL = Median(li), allL = Median(la);
                yield return Wait(Mathf.Max(0f, 6f - (Time.unscaledTime - restAt))); Shot($"E_{tag}_{gun.key.Replace(' ', '_')}_fires_6s_h70");
                int burning = OzShuttleBreakup.Burning();
                yield return WaitFor(() => burningAt10 >= 0, 12f);
                OzShuttleBreakup.Scatter(out float far, out float mean); OzShuttleBreakup.BodyField(out float bodyWide, out float bodyFar);
                if (k == 0) burstWide = bodyWide;
                string how = OzShuttleBreakup.LastHow; hows.Add(how);
                Log($"{tag} killed by {OzShuttleBreakup.LastKillKind} (power {OzShuttleBreakup.LastKillPower:F2}): the break '{how}' {(OzShuttleBreakup.LastInAir ? "in the air" : "on the ground")}; second blasts +{OzShuttleBreakup.SecondBlasts - second0}; drop {OzShuttleBreakup.LastFallSec:F2} s from {OzShuttleBreakup.LastFallHeight:F1} m, tilted up to {OzShuttleBreakup.LastFallTilt:F1} deg; "
                    + $"scatter: farthest {far:F1} m, mean {mean:F1} m from the ground under the break, the body panels' field {bodyWide:F1} m wide (farthest panel {bodyFar:F1} m); pieces {OzShuttleBreakup.Shown()}; body panels' luma median: painted faces {bodyL:F0} ({lp.Count} points), insides {innerL:F0} ({li.Count}), all {allL:F0} ({la.Count}); the grass beside {grassL:F0} ({lg.Count}); burning at the 6 s still {burning}, 10 s after the break {burningAt10}; "
                    + $"first contacts {OzShuttleBreakup.FirstContacts - firsts0}, heard {OzShuttleBreakup.FirstContactsHeard - heard0} (queued {OzShuttleBreakup.QueuedClanks}, dropped late {OzShuttleBreakup.QueuedDropped}); sounds: fire loop +{Played("dst_fire_loop") - loops0}, piece land +{Played("shuttle_piece_land") - land0}, clank +{Played("dst_debris_metal") - clank0}; {aboard} aboard");
                Check($"M16 {tag} {gun.label}: the break is '{KillHow[k]}'", how == KillHow[k]);
                if (gun.kind == "fort") Check($"M16 {tag} a real fort-gun kill (the killing hit is the fort's gun)", OzShuttleBreakup.LastKillKind == "fort gun");
                if (KillHow[k] == "split")
                {
                    Check($"M16 {tag} the second blast splits it: the body panels' field {SplitFieldText} wide ({bodyWide:F1} m)", OzShuttleBreakup.SecondBlasts - second0 == 1 && bodyWide >= SplitFieldMin && bodyWide <= SplitFieldMax);   // K396: the bigger throw (OzShuttleBreakupGallery.K396.cs)
                    Check($"M16 {tag} split wider than K1's burst (body field {bodyWide:F1} m against {burstWide:F1} m: at least x{SplitOverBurst:F1}, K396's design: View/OzShuttleBreakupGallery.K419.cs)", burstWide > 0f && bodyWide >= SplitOverBurst * burstWide);
                }
                if (KillHow[k] == "drop") Check($"M16 {tag} the drop: 1.0-1.5 s, tilted >= 18 deg, smoking, broken on the ground", OzShuttleBreakup.LastFallSec >= 0.95f && OzShuttleBreakup.LastFallSec <= 1.6f && OzShuttleBreakup.LastFallTilt >= 18f && !OzShuttleBreakup.LastInAir && OzShuttleBreakup.FallSmoke > 0);
                if (KillHow[k] == OzShuttleDeathPlan.Crack) Check($"M16 {tag} the crack: broken in the air where it was drawn (the pieces from {OzShuttleBreakup.LastSpawnLowM:F1} m, the pads {OzShuttleBreakup.LastBreakFeet:F1} m, at most {OzShuttleBreakup.LastSpawnOffM:F2} m off their hull places), trailing smoke as they fall", OzShuttleBreakup.LastInAir && OzShuttleBreakup.LastSpawnOffM <= 0.3f && OzShuttleBreakup.LastSpawnLowM >= OzShuttleBreakup.LastBreakFeet - 0.5f && OzShuttleBreakup.CrackPuffs > 0);   // K256h
                Check($"M2 {tag} the body panels keep their paint: the painted faces' median luma 45-55 by day ({bodyL:F0})", bodyL >= 45f && bodyL <= 55f);
                Check($"M2 {tag} no black slabs: every view of the panels (paint + inside) reads 45-75 and under the grass ({allL:F0} against {grassL:F0})", allL >= 45f && allL <= 75f && (grassL < 0f || allL < grassL));
                Check($"M2 {tag} the three biggest pieces burn with their smoke column 10 s after the break ({burningAt10}), fires in the 6 s still ({burning})", burningAt10 >= 3 && burning >= 1);
                Check($"M15 {tag} every piece's first contact is heard", OzShuttleBreakup.FirstContactsHeard - heard0 >= OzShuttleBreakup.FirstContacts - firsts0 && OzShuttleBreakup.FirstContacts - firsts0 > 0);
                Check($"M2 {tag} the fires crackle (the fire loop plays)", Played("dst_fire_loop") - loops0 > 0);
                Check($"M3 {tag} no ground light or ring under the flying ship", OzShuttleBreakup.GroundFxUnderAirShip - rings0 == 0);
                BigBlastLines(tag, how, far);                                                               // K396: the big blast, its sounds, the launch (OzShuttleBreakupGallery.K396.cs)
                Clear(shooters);
                yield return Wait(1.0f);
            }
            Check($"M16 the kills break in 3 different ways (burst, split, {KillHow[3]})", hows.Contains("burst") && hows.Contains("split") && hows.Contains(KillHow[3]));   // K256h: the rifle's break is the crack (the drop with its switch)
        }

        /// <summary>The intact hull's luma while it hovers (the same count as the pieces', no occlusion test: the hull has no collider).</summary>
        IEnumerator HullLuma(string tag)
        {
            var parts = new List<(MeshFilter, Collider)>();
            foreach (var mf in ship.GetComponentsInChildren<MeshFilter>()) if (mf.name == "SH_Hull" || mf.name.StartsWith("SH_Hull")) parts.Add((mf, null));
            var lp = new List<float>(); var lg = new List<float>();
            yield return MeasureLuma(parts, new Vector3(ship.transform.position.x, g.GroundY(ship.transform.position.x, ship.transform.position.z), ship.transform.position.z), false, lp, lg);
            Log($"{tag} the intact hull's luma: median {Median(lp):F0} ({lp.Count} points), the grass {Median(lg):F0} ({lg.Count})");
        }

        /// <summary>K3: the fort's gun brings it down (hovering in its reach, healed until the fort fires, then let down to 1 HP).</summary>
        IEnumerator FortKill(string tag)
        {
            s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);                                            // nothing on the way brings it down; the fort's shell does (1 HP)
            bool rec = OzShuttleAim.Recording; OzShuttleAim.Recording = true;
            yield return FortGun(tag, true);
            OzShuttleAim.Recording = rec;
        }

        // ------------------------------------------------------------------ the end
        void Finish()
        {
            Flush();
            var t = new StringBuilder();
            t.AppendLine("per action (whole take): key, played, dropped, lowest played volume");
            foreach (var kv in soundRows.OrderBy(k => k.Key)) t.AppendLine($"  {kv.Key,-28} {kv.Value[0],5} {kv.Value[1],5} {(soundLow.TryGetValue(kv.Key, out var lo) ? lo : 0f):F2}");
            Log(t.ToString().TrimEnd());
            Log($"break-up counts: breaks {OzShuttleBreakup.Breaks} (bursts {OzShuttleBreakup.Bursts}, splits {OzShuttleBreakup.Splits}, drops {OzShuttleBreakup.DropBreaks}), second blasts {OzShuttleBreakup.SecondBlasts}, air clouds {OzShuttleBreakup.AirClouds} ({OzShuttleBreakup.CloudPuffs} puffs), falls {OzShuttleBreakup.Falls} (timeouts {OzShuttleBreakup.FallTimeouts}), "
                + $"panels {OzShuttleBreakup.ShellPieces}, ember rims {OzShuttleBreakup.RimPieces}, fire pieces {OzShuttleBreakup.FirePieces}, smoulderers {OzShuttleBreakup.Smoulderers}, fire puffs {OzShuttleBreakup.FireBurns}, smoulder puffs {OzShuttleBreakup.SmoulderPuffs}, wreck fires at rest {OzShuttleBreakup.WreckFiresAtRest}, main scorches {OzShuttleBreakup.MainScorches}; "
                + $"air bursts gated {OzShuttleBreakup.AirBurstsGated}, ground fx under the flying ship {OzShuttleBreakup.GroundFxUnderAirShip} (real ground hits under it {OzShuttleBreakup.GroundBlastsUnderAirShip})");
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS), {frames} frames, {Time.unscaledTime - T0:F0} s");
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_E_breakup_" + mode + ".txt"), sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "sound_log_E_breakup_" + mode + ".txt"), snd.ToString());
            }
            catch (System.Exception ex) { Debug.LogError("[OzShuttleBreakupGallery] could not write the logs: " + ex.Message); }
            if (dn) dn.running = true;
            camAt = null;
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject, 0.5f);
        }
    }
}
