using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256g2 Play proof (rule DV118, owner item 343 + the K256g review points P2 / P3), on level 16 in Play (menu OpenZ/V1.1/Shuttle:
    /// K256g2 Mark Proof, Editor/OzShuttleMarkMenu.cs). Counted PASS / FAIL lines and full-screen pictures from the normal RTS camera at
    /// 70 m (uncropped; the self-shadow pair close at 18 m). Set-up: the CPU teams removed, morning (day clock 0.10, held), 60 fps.
    /// <list type="bullet">
    /// <item>M1 the lines of the off-Unity twin's cases, flown at cruise with a run-up (Z0 open ground first: the reference), each with six
    /// moments: a picture with the mark and one without it (ProofHide) the next frame; the mark's darkening inside its core from the two
    /// (luminance). PASS per line: drawn as the decal every frame of the line, its centre glides (jitter rms &lt;= 0.3 px, worst &lt;= 1.5
    /// px at the camera), no size step (the radius &lt;= 0.05 m a frame), visible at every moment (darkening
    /// at least a quarter of the reference on open ground: never vanishes).</item>
    /// <item>M2 down onto the deck of station 92_29 (the repair order): the deck under the mark (PlaneKind deck), drawn every frame until
    /// the touchdown's cross-fade takes it; then along the station again with the crane parked away and its rams (C3).</item>
    /// <item>M3 its self-shadow in the air: a picture close with it and one without (OzShuttleSelfShadow.Enabled off a frame): the hull darker
    /// where the pods shade it, the ground round it the same in both (no real shadow on the ground).</item>
    /// <item>M4 (Watch): stays on and checks every hand-over at a kill in the air (run the K256e Break-up Proof GK, or shoot it down):
    /// the mark and the real shadow add up to one (within 0.02), neither changes by more than 0.05 a frame, it lasts 0.5 .. 0.75 s.</item>
    /// </list>
    /// <see cref="Folder"/>
    /// </summary>
    public sealed partial class OzShuttleMarkProof : MonoBehaviour                                // + OzShuttleMarkProof.Guard.cs (its ship is not shot down while it flies the lines)
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_mark_proof_k256g2");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_mark_proof_k256g2");
        }
        public static void Run(bool watchOnly)
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var p = new GameObject("OzShuttleMarkProof").AddComponent<OzShuttleMarkProof>(); p.watchOnly = watchOnly;
        }

        // the lines (world x, z): the twin's cases on level 16 (tools/cases.py of the kit)
        static readonly (string id, string what, Vector2 a, Vector2 b)[] Lines =
        {
            ("Z0", "open flat ground (the reference)", new Vector2(136, -82), new Vector2(176, -82)),
            ("A1", "the ravine: the 6 m deep channel and the shallow river", new Vector2(70, -110), new Vector2(125, -110)),
            ("A2", "along the jungle bridge over the deep channel", new Vector2(144, -178), new Vector2(144, -122)),
            ("B1", "across the fort", new Vector2(72, -30), new Vector2(136, -30)),
            ("B2", "across the vehicle factory 19_30", new Vector2(18, -65), new Vector2(66, -65)),
            ("B5", "over the big pyramid", new Vector2(165, -42), new Vector2(204, -42)),
            ("C1", "across the repair station 92_29 (crane at rest)", new Vector2(163, -62), new Vector2(205, -62)),
            ("C2", "along the repair station 92_29 (deck, tower, beam)", new Vector2(189.5f, -88), new Vector2(189.5f, -36)),
            ("D1", "over the tall tree cluster", new Vector2(118, -54), new Vector2(160, -61)),
        };

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir; bool watchOnly; Color32[] grab; int gw, gh; float refDark = -1f; int hoSeen;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleMarkProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        Camera CamNow => rts ? rts.GetComponent<Camera>() : Camera.main;
        float PxPerM(Vector3 at) { var c = CamNow; if (!c) return 0f; var a = c.WorldToScreenPoint(at); var b = c.WorldToScreenPoint(at + c.transform.right); return new Vector2(b.x - a.x, b.y - a.y).magnitude; }
        static int Px(float x) => Mathf.Clamp(Mathf.FloorToInt(x * 8f / 16f), 1, 400) * 16 + 8;
        bool MoveTo(Vector2 w) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = Px(w.x), y = Px(-w.y), playerGiven = true } });
        IEnumerator Grab() { yield return new WaitForEndOfFrame(); var tex = ScreenCapture.CaptureScreenshotAsTexture(); grab = tex.GetPixels32(); gw = tex.width; gh = tex.height; Destroy(tex); }
        static float Luma(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        float MeanLuma(Color32[] px, int w, int h, Vector2 c, float r)
        {
            double sum = 0; int n = 0; int x0 = Mathf.Max(0, (int)(c.x - r)), x1 = Mathf.Min(w - 1, (int)(c.x + r)), y0 = Mathf.Max(0, (int)(c.y - r)), y1 = Mathf.Min(h - 1, (int)(c.y + r));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if ((x - c.x) * (x - c.x) + (y - c.y) * (y - c.y) <= r * r) { sum += Luma(px[y * w + x]); n++; }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (watchOnly) { yield return Watch(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            OzWaterSurface.Ensure();
            Log($"K256g2 mark proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; CPU removed {bots}, day 0.10 held; the drawn water: {OzWaterSurface.WaterCells} cells of {OzWaterSurface.Cell} m from {OzWaterSurface.Meshes} mesh(es)");
            Check("M0 the drawn water surface is read on this level (cells > 0, no unreadable water mesh)", OzWaterSurface.WaterCells > 0 && OzWaterSurface.Unreadable == 0);
            GuardOn();                                                                                   // the lines cross the enemy fort: the ship is not shot down (OzShuttleMarkProof.Guard.cs)
            foreach (var l in Lines) yield return Fly(l.id, l.what, l.a, l.b);
            GuardHpOff();                                                                                // the deck stage with the ship's own hit points (60 %, R to the station); the enemies hold their fire
            yield return Deck();
            GuardOn();
            yield return SelfShadow();
            GuardHpOff(); Log(GuardText());
            Finish();
        }

        IEnumerator Fly(string id, string what, Vector2 a, Vector2 b)
        {
            var d = (b - a).normalized; float L = (b - a).magnitude;
            var run = a - d * 15f; var over = b + d * 10f;
            MoveTo(run);
            yield return WaitFor(() => s.ShuttleAirborne && new Vector2(ship.transform.position.x - run.x, ship.transform.position.z - run.y).magnitude < 3f, 60f);
            yield return Wait(1.0f);
            var mid = (a + b) * 0.5f; Cam(new Vector3(mid.x, 0f, mid.y), 70f);
            MoveTo(over);
            OzShuttleShadow.JitterMaxM = 0f; OzShuttleShadow.JitterSum2 = 0f; OzShuttleShadow.ShipJitterMaxM = 0f; OzShuttleShadow.ShipJitterSum2 = 0f; OzShuttleShadow.JitterFrames = 0;
            OzShuttleShadow.RadiusStepMaxM = 0f; OzShuttleShadow.RefStepMaxM = 0f;
            int inLine = 0, decal = 0, lat0 = OzShuttleShadow.LatticeFrames; float t = 0f; int k = 0; float darkMin = 9f; string darks = "";
            float T = L / Mathf.Max(1f, (float)s.RealMoveSpeed / 8f);
            while (t < T * 3f + 20f)
            {
                var p = ship.transform.position; float along = Vector2.Dot(new Vector2(p.x, p.z) - a, d);
                if (along > L) break;
                if (along >= 0f)
                {
                    inLine++; if (ship.Shadow != null && ship.Shadow.DecalOn) decal++;
                    if (k < 6 && along >= L * (k + 0.5f) / 6f)
                    {   // a moment: the picture with the mark, the next frame without it
                        var sh = ship.Shadow; var c = CamNow;
                        if (sh != null && c)
                        {
                            yield return Grab(); var A = grab; int w = gw, h = gh;
                            var cen = c.WorldToScreenPoint(new Vector3(sh.Centre.x, sh.RefY, sh.Centre.z)); float r = 0.45f * OzShuttleShadow.AlongM * sh.GrowNow * PxPerM(sh.Centre);
                            Shot($"{id}_{k + 1}_mark");
                            OzShuttleShadow.ProofHide = true; yield return null; yield return Grab(); var B = grab; OzShuttleShadow.ProofHide = false;
                            float la = MeanLuma(A, w, h, cen, r), lb = MeanLuma(B, w, h, cen, r), dk = lb > 1f ? 1f - la / lb : 0f;
                            darkMin = Mathf.Min(darkMin, dk); darks += $" {dk:F3}";
                        }
                        k++;
                    }
                }
                t += Time.deltaTime; yield return null;
            }
            int n = Mathf.Max(1, OzShuttleShadow.JitterFrames); float ppm = ship.Shadow != null ? PxPerM(ship.Shadow.Centre) : 0f;
            float jr = Mathf.Sqrt(OzShuttleShadow.JitterSum2 / n) * ppm, jm = OzShuttleShadow.JitterMaxM * ppm;
            Log($"{id} {what}: {inLine} frames on the line, decal {decal}, lattice {OzShuttleShadow.LatticeFrames - lat0}; jitter rms {jr:F2} px, worst {jm:F2} px; radius step {OzShuttleShadow.RadiusStepMaxM:F3} m, surface step {OzShuttleShadow.RefStepMaxM:F3} m; darkening at the 6 moments:{darks}");
            if (id == "Z0") refDark = darkMin;
            Check($"{id} drawn as the decal on every frame of the line ({decal} of {inLine})", inLine > 30 && decal == inLine);
            Check($"{id} the mark glides: jitter rms <= 0.3 px, worst <= 1.5 px ({jr:F2} / {jm:F2})", OzShuttleShadow.JitterFrames > 30 && jr <= 0.3f && jm <= 1.5f);
            Check($"{id} no size step: the radius changes by <= 0.05 m a frame ({OzShuttleShadow.RadiusStepMaxM:F3}; the eased surface {OzShuttleShadow.RefStepMaxM:F3} m)", OzShuttleShadow.RadiusStepMaxM <= 0.05f);
            Check($"{id} visible at all 6 moments: its core darkened by at least a quarter of the open-ground reference ({darkMin:F3} vs {refDark:F3})", k == 6 && refDark > 0.005f && darkMin >= 0.25f * refDark);
        }

        IEnumerator Deck()
        {
            ZObject station = null; float best = 1e9f; var want = new Vector3(189.5f, 0f, -62f);
            foreach (var f in W.Flags) foreach (var b2 in f.LinkedObjects)
                    if (b2.Type == "repair") { var c = ZGame.PxToWorld(b2.CenterX, b2.CenterY); float dd = (c - want).sqrMagnitude; if (dd < best) { best = dd; station = b2; if (b2.Owner != me) W.AwardZone(f, me); } }
            if (station == null) { Check("M2 a repair station on this level", false); yield break; }
            s.SetHealth(Mathf.Max(1, s.MaxHealth * 6 / 10)); input.Selection.Clear(); input.Selection.Add(s);
            Cam(want, 70f);
            Check("M2 R sends it to the repair station", input.SendToRepair());
            bool deck = false; int gaps = 0; float t = 0f;
            while (t < 70f && !(s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && ship.DeckStation >= 0))
            {
                var sh = ship.Shadow;
                if (sh != null && ship.DeckStation >= 0 && sh.Shown && sh.PlaneKind == "deck") deck = true;
                if (sh != null && s.ShuttleAirborne && ship.GroundShare < 0.5f && !sh.Shown) gaps++;
                t += Time.deltaTime; yield return null;
            }
            Shot("M2_on_the_deck");
            Check("M2 coming down onto the deck the mark lies on it (PlaneKind deck)", deck);
            Check($"M2 never gone in the air before the touchdown's cross-fade ({gaps} frames without it)", gaps == 0);
            yield return Wait(2f);
            yield return Fly("C3", "along the station with the crane parked away and its rams", new Vector2(189.5f, -62f), new Vector2(189.5f, -36f));
        }

        IEnumerator SelfShadow()
        {
            var cen = new Vector2(150, -82); MoveTo(cen);
            yield return WaitFor(() => s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 40f);
            if (s.ShuttleState == ZShuttleState.Landed) { MoveTo(new Vector2(150, -150)); yield return WaitFor(() => s.ShuttleAirborne, 10f); yield return Wait(4f); }
            Cam(ship.transform.position, 18f); yield return Wait(1.5f);
            var ss = ship.SelfShadow; int f0 = OzShuttleSelfShadow.Frames;
            yield return Wait(0.5f);
            Check($"M3 in the air its own shadow map is drawn ({OzShuttleSelfShadow.Frames - f0} frames, {OzShuttleSelfShadow.ReceiversLast} receivers)", ss && ss.On && OzShuttleSelfShadow.Frames > f0 && OzShuttleSelfShadow.ReceiversLast > 0);
            var c = CamNow; var sp = c.WorldToScreenPoint(ship.transform.position + Vector3.up * 2.4f); float r = 3.2f * PxPerM(ship.transform.position);
            var gp = c.WorldToScreenPoint(new Vector3(ship.transform.position.x, g.GroundY(ship.transform.position.x, ship.transform.position.z), ship.transform.position.z));
            yield return Grab(); var A = grab; int w = gw, h = gh; Shot("M3_selfshadow_air_on");
            OzShuttleSelfShadow.Enabled = false; yield return null; yield return null; yield return Grab(); var B = grab; Shot("M3_selfshadow_air_off"); OzShuttleSelfShadow.Enabled = true;
            float hullOn = MeanLuma(A, w, h, sp, r), hullOff = MeanLuma(B, w, h, sp, r);
            float groundOn = MeanLuma(A, w, h, gp, r * 0.6f), groundOff = MeanLuma(B, w, h, gp, r * 0.6f);
            Log($"M3 the hull's mean luminance with / without its self-shadow {hullOn:F1} / {hullOff:F1}; the ground under it {groundOn:F1} / {groundOff:F1}");
            Check("M3 the pods shade the hull in the air (the hull darker with the self-shadow on)", hullOff > 1f && hullOn < hullOff * 0.995f);
            Check("M3 the ground under it is the same in both (no real shadow down there: within 1 %)", groundOff > 1f && Mathf.Abs(groundOn / groundOff - 1f) <= 0.01f);
        }

        IEnumerator Watch()
        {
            Log("K256g2 hand-over watch: every kill in the air from now on (run the K256e Break-up Proof GK, or shoot it down); the menu again stops it");
            hoSeen = OzShuttleShadowHandOver.Finished;
            while (!stop)
            {
                if (OzShuttleShadowHandOver.Finished > hoSeen)
                {
                    hoSeen = OzShuttleShadowHandOver.Finished;
                    Log($"hand-over {hoSeen}: {OzShuttleShadowHandOver.LastFrames} frames, {OzShuttleShadowHandOver.LastSec:F2} s, pieces taken in {OzShuttleShadowHandOver.PiecesTaken}, |mark + shadow - 1| worst {OzShuttleShadowHandOver.LastSumErr:F3}, steps mark {OzShuttleShadowHandOver.LastMarkStepMax:F3} / shadow {OzShuttleShadowHandOver.LastRealStepMax:F3}");
                    Check($"M4 hand-over {hoSeen}: the mark and the real shadow add up to one (worst {OzShuttleShadowHandOver.LastSumErr:F3} <= 0.02)", OzShuttleShadowHandOver.LastSumErr <= 0.02f);
                    Check($"M4 hand-over {hoSeen}: no pop (each changes by <= 0.05 a frame: {OzShuttleShadowHandOver.LastMarkStepMax:F3} / {OzShuttleShadowHandOver.LastRealStepMax:F3})", OzShuttleShadowHandOver.LastMarkStepMax <= 0.05f && OzShuttleShadowHandOver.LastRealStepMax <= 0.05f);
                    Check($"M4 hand-over {hoSeen}: it lasts 0.5 .. 0.75 s ({OzShuttleShadowHandOver.LastSec:F2})", OzShuttleShadowHandOver.LastSec >= 0.5f && OzShuttleShadowHandOver.LastSec <= 0.75f);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "handover_watch_log.txt"), sb.ToString());
                }
                yield return null;
            }
            Finish();
        }
        public static bool stop;

        void Finish()
        {
            Log($"K256g2 mark proof: {passes} PASS, {fails} FAIL, {frames} pictures");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, watchOnly ? "handover_watch_log.txt" : "mark_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
