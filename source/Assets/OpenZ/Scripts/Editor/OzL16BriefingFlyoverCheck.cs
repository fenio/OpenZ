using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the Level 16 mission-start briefing flyover (the shot list ZodShotList.L16 and its staging OzL16BriefingStage).
    /// Play mode, Level 16 loaded, the first briefing over and the camera left at its start view, the Game view at full size, hands off.
    /// Menu: OpenZ/Checks/L16 briefing flyover (Play mode), or OpenZ.EditorTools.OzL16BriefingFlyoverCheck.Run().
    /// It runs the level's briefing once more as the game does (voices, window, bars) and AS THE FIRST BRIEFING OF A MISSION IS: the
    /// commander's name plate is put back to what it is at a level's start for the run, so the name-plate joke would play if this
    /// briefing still had one. It saves ONE full-screen frame from the middle of every shot, and counts while it flies. The result is
    /// one log line that starts "[OpenZ L16 briefing flyover] PASS n/n" or "... FAIL" (also in Report and in checks.txt next to the
    /// pictures). 24 checks with the briefing line in the game.
    ///
    /// A  the list: the jungle has its own shot list, not the Desert's (1); the flight flies it (1); one full-screen frame per shot
    ///    was saved (1).
    /// B  the crocodile: in the pool shot the staged animal is inside the view on 75 % of the frames or more, with nothing of the
    ///    ground between it and the lens (1); it swims: 5 m or more over the shot and faster than 0.8 m/s on 75 % of the frames (1);
    ///    its wake is on for 60 % of the frames or more (1); it lies on its pool's water height within 5 cm (1); in the cut the near
    ///    animal is inside the view, 3 to 8 m from the lens, and its jaws have shut before the shot ends (1); when the level draws
    ///    crocodiles of its own, the staged ones are LENT by the level and no other crocodile is drawn within 3.5 m of a staged
    ///    one (1).
    /// C  the parrots: in each of the two bird shots at least BirdsNeeded birds are inside the frame for a quarter of a second or
    ///    more AND come within 12 m of the lens (2); 0 birds whose first or last drawn frame lies inside the picture, a bird that is
    ///    already flying on the first frame of its shot excepted: that is a cut, not an appearance (1).
    /// D  the sounds: flutter, squawks, wake loop, swell, snap and splash each start at least once and none was dropped (1); every
    ///    one that started while a voice spoke (the briefing's own voice or the announcer's start line) was started ducked (1).
    /// E  the timing (needs the level's briefing line): the whole flight is not longer than the list's limit and covers the voice
    ///    (1); the flight took as long as planned within half a second (1); the window opens the list's own quiet opening before the
    ///    first word, within 0.05 s, and the name plate is not rewritten: no name-plate joke on this briefing (1); the cut to the
    ///    crocodiles falls 0.69 s before the word CROCS within 0.10 s, in the pause after THAT (1); at least three cuts fall on line
    ///    breaks within 0.13 s (1); the last shot keeps its least length (1).
    /// F  the hand-back: the camera stands on the level's start view within 1 cm and 0.05 degrees, field of view back (1), with no
    ///    jump between the last frame of the flight and the first frame after it, within 2 cm: the last flown frame lies up to one
    ///    frame time before the end, where the camera still moves 9.7 m x that time squared, so 2 cm holds from 22 frames a second
    ///    up (1); every staged animal was given back (1).
    /// G  no frame with the camera inside geometry: never under the ground or the water plus the near plane, no collider within the
    ///    near plane, never inside the triangles of a scene object (1).
    /// Without the briefing line in the game the camera-only preview flies instead: the lines of E are NOT RUN and the verdict is FAIL.
    /// </summary>
    public static partial class OzL16BriefingFlyoverCheck
    {
        const string Tag = "[OpenZ L16 briefing flyover]";
        const string Level = "L16", LineId = "zod_brief_L16";
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        /// <summary>The least number of birds that must pass the lens in each bird shot.</summary>
        public static int BirdsNeeded = 4;
        /// <summary>The camera's near plane plus a little room (m): nothing may come nearer.</summary>
        public static float Near = 0.55f;
        /// <summary>The cut to the crocodiles comes this long before the word CROCS starts (s): the list's anchor (0.54 + ZodFlyover.CutLead).</summary>
        public static float CrocLead = 0.69f;
        /// <summary>The owner's script: the whole screen never comes nearer the chase than this (m), and the window's chase shot number.</summary>
        public static float FarChase = 20f; public static int ChaseWindowShot = 5;

        static IEnumerator run;
        static int waitFrame, ok, bad, notRun;
        static readonly StringBuilder lines = new StringBuilder();
        static string dir;

        [MenuItem("OpenZ/Checks/L16 briefing flyover (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode on Level 16 first"); return; }
            if (run != null) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            ok = bad = notRun = 0; lines.Clear(); Report = "running";
            dir = OutDir(); Directory.CreateDirectory(dir);
            run = Proof(); waitFrame = 0; EditorApplication.update += Step;
        }

        static string OutDir()
        {
            if (!string.IsNullOrEmpty(Folder)) return Path.GetFullPath(Folder);
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "L16_briefing_flyover");
        }

        static void Step()
        {
            if (run == null || !EditorApplication.isPlaying) { run = null; EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame) return;
            bool more;
            try { more = run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); more = false; }
            if (!more) { run = null; EditorApplication.update -= Step; return; }
            waitFrame = Time.frameCount + (run.Current is int n ? n : 1);
        }

        static void Check(bool pass, string what)
        {
            if (pass) ok++; else bad++;
            lines.Append(pass ? "PASS  " : "FAIL  ").AppendLine(what);
        }

        static void NotRun(string what) { notRun++; lines.Append("NOT RUN  ").AppendLine(what); }

        static void Stop(string why)
        {
            Report = Tag + " FAIL: " + why; Debug.LogError(Report);
            try { File.WriteAllText(Path.Combine(dir, "checks.txt"), Report + "\n"); } catch (System.Exception) { }
        }

        // ------------------------------------------------------------------ the camera against the scene
        sealed class Solid { public Renderer r; public MeshFilter mf; public Bounds b; }
        static readonly Dictionary<long, List<Solid>> grid = new Dictionary<long, List<Solid>>();
        static readonly Dictionary<Mesh, (Vector3[] v, int[] t)> meshData = new Dictionary<Mesh, (Vector3[], int[])>();
        static readonly HashSet<string> untested = new HashSet<string>();
        const float CellSize = 16f;

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Every mesh object of the scene that is small enough to be a thing (not the ground, the water, the map sides: those are
        /// asked through the ground and water heights), in a grid of 16 m cells.</summary>
        static int IndexScene(Transform skip)
        {
            grid.Clear(); meshData.Clear(); untested.Clear();
            int n = 0;
            foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!mr || (skip && mr.transform.IsChildOf(skip))) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var b = mr.bounds;
                if (b.size.x > 60f || b.size.z > 60f || b.size.sqrMagnitude < 1e-4f) continue;
                var s = new Solid { r = mr, mf = mf, b = b };
                int x0 = Mathf.FloorToInt((b.min.x - Near) / CellSize), x1 = Mathf.FloorToInt((b.max.x + Near) / CellSize);
                int z0 = Mathf.FloorToInt((b.min.z - Near) / CellSize), z1 = Mathf.FloorToInt((b.max.z + Near) / CellSize);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        long k = Key(x, z);
                        if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Solid>();
                        l.Add(s);
                    }
                n++;
            }
            return n;
        }

        static float PointTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {   // squared distance from p to the triangle a b c (closest point by regions)
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (ap - ab * (d1 / (d1 - d3))).sqrMagnitude;
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (ap - ac * (d2 / (d2 - d6))).sqrMagnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return (bp - (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)))).sqrMagnitude;
            float denom = 1f / (va + vb + vc); float v = vb * denom, w = vc * denom;
            return (ap - ab * v - ac * w).sqrMagnitude;
        }

        /// <summary>The name of a thing the camera point is inside of or nearer to than the near plane, or null.</summary>
        static string Touching(ZGame game, Vector3 p)
        {
            float ground = game.TerrainY(p.x, p.z), water = OzWaterLevel.At(Level, p.x, p.z, float.NegativeInfinity);
            bool onMap = p.x > -24f && p.x < 232f && p.z < 24f && p.z > -280f;                       // the level's ground model
            if (onMap && p.y < Mathf.Max(ground, water) + Near) return water > ground ? $"the water ({p.y - water:F2} m over it)" : $"the ground ({p.y - ground:F2} m over it)";
            var hits = Physics.OverlapSphere(p, Near, ~0, QueryTriggerInteraction.Ignore);
            if (hits != null && hits.Length > 0) return "collider " + hits[0].name;
            if (!grid.TryGetValue(Key(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize)), out var l)) return null;
            foreach (var s in l)
            {
                if (!s.r || !s.r.enabled || !s.r.gameObject.activeInHierarchy) continue;
                if (s.b.SqrDistance(p) > Near * Near) continue;
                var mesh = s.mf.sharedMesh;
                if (!meshData.TryGetValue(mesh, out var md))
                {
                    md = (null, null);
                    if (mesh.isReadable) { try { md = (mesh.vertices, mesh.triangles); } catch (System.Exception) { md = (null, null); } }
                    if (md.v == null || md.v.Length == 0 || md.t == null) { md = (null, null); }
                    meshData[mesh] = md;
                }
                if (md.v == null) { untested.Add(s.r.name + " (mesh " + mesh.name + " is not readable)"); continue; }
                var t = s.r.transform; Vector3 lp = t.InverseTransformPoint(p);
                var ls = t.lossyScale; float sc = Mathf.Max(1e-4f, Mathf.Min(Mathf.Abs(ls.x), Mathf.Min(Mathf.Abs(ls.y), Mathf.Abs(ls.z))));
                float lim = Near / sc; lim *= lim;
                for (int i = 0; i + 2 < md.t.Length; i += 3)
                    if (PointTriangle(lp, md.v[md.t[i]], md.v[md.t[i + 1]], md.v[md.t[i + 2]]) < lim) return s.r.name;
            }
            return null;
        }

        // ------------------------------------------------------------------ the run
        sealed class BirdSeen { public float inside, nearest = 1e9f, firstInto, lastInto, lastDur; public bool firstOnCut, firstInside, lastInside; public int lastFrame; }

        /// <summary>Can any of a parrot be seen? Its middle lies less than half a wing span outside the view (and not behind the lens).</summary>
        static bool BirdInPicture(Camera cam, Vector3 p)
        {
            float half = OzL16BriefingStage.EdgeMargin * 0.5f;
            Vector3 l = cam.transform.InverseTransformPoint(p);
            if (l.z < -half) return false;
            float tv = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), th = tv * cam.aspect;
            float dx = (Mathf.Abs(l.x) - l.z * th) / Mathf.Sqrt(1f + th * th), dy = (Mathf.Abs(l.y) - l.z * tv) / Mathf.Sqrt(1f + tv * tv);
            return Mathf.Max(dx, dy) < half;
        }

        /// <summary>The level's own crocodiles that are drawn right now (the animals step's view, found by name: this file compiles without it).</summary>
        static readonly List<(Transform root, SkinnedMeshRenderer[] skins)> levelCrocs = new List<(Transform, SkinnedMeshRenderer[])>();
        static int IndexLevelCrocs()
        {
            levelCrocs.Clear();
            var view = OzL16BriefingStage.LevelCrocodiles();
            if (!view) return -1;
            foreach (Transform c in view.transform)
                if (c.name.StartsWith("Crocodile")) levelCrocs.Add((c, c.GetComponentsInChildren<SkinnedMeshRenderer>(true)));
            return levelCrocs.Count;
        }

        /// <summary>Flat distance from a staged animal to the nearest OTHER crocodile the level draws right now (1e9 = none).</summary>
        static float NearestOther(Transform staged, Transform alsoStaged)
        {
            float best = 1e9f;
            if (!staged) return best;
            foreach (var (root, skins) in levelCrocs)
            {
                if (!root || root == staged || root == alsoStaged || staged.IsChildOf(root) || (alsoStaged && alsoStaged.IsChildOf(root))) continue;
                bool drawn = false;
                foreach (var sk in skins) if (sk && sk.enabled && sk.gameObject.activeInHierarchy) { drawn = true; break; }
                if (!drawn) continue;
                float dx = root.position.x - staged.position.x, dz = root.position.z - staged.position.z;
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        static bool InView(Camera cam, Vector3 p, float margin, out Vector3 v)
        {
            v = cam.WorldToViewportPoint(p);
            return v.z > cam.nearClipPlane && v.x >= margin && v.x <= 1f - margin && v.y >= margin && v.y <= 1f - margin;
        }

        static IEnumerator Proof()
        {
            var game = ZGame.I ? ZGame.I : Object.FindFirstObjectByType<ZGame>();
            if (!game || game.level != Level) { Stop("open Level 16 in Play mode (this scene is " + (game ? game.level : "no level") + ")"); yield break; }
            var rts = Object.FindFirstObjectByType<RtsCamera>();
            var cam = rts ? rts.GetComponent<Camera>() : null;
            if (!rts || !cam) { Stop("no game camera in this scene"); yield break; }
            if (ZodBriefing.BriefingActive || ZodBriefing.DebugLineId != null || ZodFlyover.Live != null)
            { Stop("a briefing or a transmission is still running: wait until the level is in the player's hands, then run again"); yield break; }
            if (!OzL16BriefingStage.I) { Stop("OzL16BriefingStage is not in this scene (it attaches itself on Level 16: is OzL16BriefingStage.cs compiled?)"); yield break; }
            yield return 2;

            // the list
            var list = ZodShotList.For(Level);
            int nShots = list != null && list.shots != null ? list.shots.Length : 0;
            Check(list == ZodShotList.L16 && nShots >= 4, $"the jungle has its own shot list: ZodShotList.For(\"{Level}\") answers the {(list != null ? list.level : "?")} list with {nShots} shots");

            var lib = ZodLibrary.Shared; var line = lib.Line(LineId); var clip = lib.Clip(LineId);
            bool real = line != null && clip;
            int indexed = IndexScene(OzL16BriefingStage.I.transform);
            Vector3 homePos = cam.transform.position; Quaternion homeRot = cam.transform.rotation; float homeFov = cam.fieldOfView;
            float logFrom = Time.unscaledTime; int sounds0 = OzL16BriefingStage.SoundsStarted, lent0 = OzL16BriefingStage.Lent, built0 = OzL16BriefingStage.Built, back0 = OzL16BriefingStage.GivenBack;
            // the FIRST briefing of a mission: the name plate as it is at a level's start (later in a session it reads GENERAL and the
            // name-plate joke is over, so a second briefing would hide a joke that the first one still plays)
            bool plateBefore = ZodComms.General;
            if (real) ZodComms.General = false;
            int levelCrocCount = IndexLevelCrocs(); float otherNear = 1e9f;
            string started = real ? ZodBriefing.DebugBriefing() : ZodCinePreview.Play(list != null ? list.NominalSeconds : 21f);
            if (ZodFlyover.Live == null || !ZodFlyover.Live.Cine) { ZodComms.General = plateBefore; Stop("the flight did not start: " + started); yield break; }
            var fly = ZodFlyover.Live;
            var plan = new List<(string name, float start, float dur)>(fly.CineShots);
            float total = plan.Count > 0 ? plan[plan.Count - 1].start + plan[plan.Count - 1].dur : 0f;
            bool names = plan.Count == nShots;
            for (int i = 0; names && i < nShots; i++) names = plan[i].name == list.shots[i].name;
            Check(names, $"the flight flies that list: {plan.Count} shots ({ZodFlyover.LastPlan})");

            // while it flies
            var shotSaved = new bool[plan.Count]; int saved = 0;
            var birdSeen = new Dictionary<string, Dictionary<int, BirdSeen>>();
            int swimFrames = 0, swimIn = 0, swimFast = 0, swimWake = 0, swimHidden = 0; float swimPath = 0f, swimOff = 0f; Vector3 swimLast = Vector3.zero; bool swimHas = false;
            int chaseFrames = 0, windowChaseFrames = 0, windowChaseLive = 0; float chaseNear = 1e9f; bool snapped = false;
            int frames = 0; string touched = null; int touchedFrames = 0;
            var voiceAt = new List<(float t, bool on)>();                                           // per frame: is a voice playing (the briefing's own, or the announcer's start line)?
            var shotLastFrame = new Dictionary<string, int>();
            float t0 = Time.unscaledTime, flightEnd = -1f; ResetVoiceClock();
            Vector3 lastPos = cam.transform.position; Quaternion lastRot = cam.transform.rotation;
            var walk = ~0;
            while (ZodFlyover.Live != null && Time.unscaledTime - t0 < total + 12f)
            {
                fly = ZodFlyover.Live;
                float dt = Time.unscaledDeltaTime;
                voiceAt.Add((Time.unscaledTime, (fly.VoiceOn != null && fly.VoiceOn()) || ZodBriefing.StartLinePlaying)); SampleVoiceClock();
                if (fly.CineShown(out string shot, out float into, out float dur))
                {
                    frames++;
                    shotLastFrame[shot] = frames;
                    float cutTime = Mathf.Max(0.05f, 1.5f * dt);                                        // within this of a shot's start or end a bird is cut with the picture
                    int si = plan.FindIndex(p => p.name == shot);
                    if (si >= 0 && !shotSaved[si] && into >= dur * 0.5f)
                    {
                        shotSaved[si] = true; saved++;
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"shot{si + 1}_{shot}_{Screen.width}x{Screen.height}.png"));
                    }
                    string hit = Touching(game, cam.transform.position);
                    if (hit != null) { touchedFrames++; if (touched == null) touched = $"{hit} in shot {shot} at {into:F2} s, camera {cam.transform.position:F2}"; }
                    // parrots
                    if (OzL16BriefingStage.BirdsNow.Count > 0)
                    {
                        if (!birdSeen.TryGetValue(shot, out var seen)) birdSeen[shot] = seen = new Dictionary<int, BirdSeen>();
                        foreach (var b in OzL16BriefingStage.BirdsNow)
                        {
                            if (!b) continue;
                            bool pictured = BirdInPicture(cam, b.position);
                            if (!seen.TryGetValue(b.GetInstanceID(), out var bs))
                                seen[b.GetInstanceID()] = bs = new BirdSeen { firstInto = into, firstOnCut = into <= cutTime, firstInside = pictured };
                            bs.lastInside = pictured; bs.lastInto = into; bs.lastDur = dur; bs.lastFrame = frames;
                            if (InView(cam, b.position, 0f, out _)) { bs.inside += dt; bs.nearest = Mathf.Min(bs.nearest, Vector3.Distance(cam.transform.position, b.position)); }
                        }
                    }
                    // the swimmer
                    if (shot == OzL16BriefingStage.SwimShot)
                    {
                        swimFrames++;
                        var sw = OzL16BriefingStage.Swimmer;
                        if (sw)
                        {
                            Vector3 head = sw.position + sw.forward * 0.9f + Vector3.up * 0.05f;
                            bool inView = InView(cam, head, 0.03f, out _);
                            Vector3 eye = cam.transform.position, aim = head + Vector3.up * 0.15f;
                            bool hidden = inView && Physics.Linecast(eye, aim, out var block, walk, QueryTriggerInteraction.Ignore)
                                          && block.distance < Vector3.Distance(eye, aim) - 1.5f && !block.collider.transform.IsChildOf(sw);
                            if (inView && !hidden) swimIn++;
                            if (hidden) swimHidden++;
                            if (swimHas) swimPath += Vector3.Distance(sw.position, swimLast);
                            swimLast = sw.position; swimHas = true;
                            if (OzL16BriefingStage.SwimmerSpeed > 0.8f) swimFast++;
                            if (OzL16BriefingStage.WakeOn) swimWake++;
                            float water = OzWaterLevel.At(Level, sw.position.x, sw.position.z, OzL16BriefingStage.PoolWaterY);
                            swimOff = Mathf.Max(swimOff, Mathf.Abs(sw.position.y - OzL16BriefingStage.CrocLift - water));
                        }
                    }
                    // the chase on the whole screen is seen from FAR only; it goes on in the briefing window's chase shot
                    if (OzL16BriefingStage.RobotNow || OzL16BriefingStage.Swimmer)
                    {
                        chaseFrames++;
                        if (OzL16BriefingStage.RobotNow) chaseNear = Mathf.Min(chaseNear, Vector3.Distance(cam.transform.position, OzL16BriefingStage.RobotNow.position));
                        if (OzL16BriefingStage.Swimmer) chaseNear = Mathf.Min(chaseNear, Vector3.Distance(cam.transform.position, OzL16BriefingStage.Swimmer.position));
                        var ls = OzL16LoungerSet.I;
                        if (ls && ls.BriefingOn && ls.WindowShot == ChaseWindowShot) { windowChaseFrames++; if (OzL16BriefingStage.RobotNow && OzL16BriefingStage.ChaseTau >= 0f) windowChaseLive++; }
                    }
                    snapped |= OzL16BriefingStage.Snapped;
                    // the level's own crocodiles beside the staged ones
                    if (levelCrocCount > 0)
                        otherNear = Mathf.Min(otherNear, Mathf.Min(NearestOther(OzL16BriefingStage.Swimmer, OzL16BriefingStage.Snapper), NearestOther(OzL16BriefingStage.Snapper, OzL16BriefingStage.Swimmer)));
                    flightEnd = Time.unscaledTime;
                }
                lastPos = cam.transform.position; lastRot = cam.transform.rotation;
                yield return 1;
            }
            bool plateAfter = ZodComms.General;                                                       // true = the name-plate joke rewrote the plate in this run
            ZodComms.General = plateBefore;
            if (ZodFlyover.Live != null) { Stop("the flight did not end within " + (total + 12f).ToString("F0") + " s"); yield break; }
            Vector3 afterPos = cam.transform.position; Quaternion afterRot = cam.transform.rotation; float afterFov = cam.fieldOfView;
            float wait = Time.unscaledTime;
            while ((ZodBriefing.BriefingActive || ZodCinePreview.I) && Time.unscaledTime - wait < 4f) yield return 1;
            yield return 3;

            // A
            Check(saved == plan.Count, $"one full-screen frame from the middle of every shot: {saved} of {plan.Count} saved in {dir}");
            // B
            var stageWarn = OzL16BriefingStage.LastWarning;
            Check(swimFrames > 0 && swimIn >= 0.75f * swimFrames, $"pool shot: the crocodile is inside the view, nothing of the ground before it, on {swimIn} of {swimFrames} frames (at least 75 %; hidden by ground on {swimHidden})");
            // shot's length, never more than the old 5 m asked of the longer shot
            int si5 = plan.FindIndex(p => p.name == OzL16BriefingStage.SwimShot);
            float swimWant = si5 >= 0 ? Mathf.Min(5f, 0.8f * OzL16BriefingStage.RobotSpeed * plan[si5].dur) : 5f;
            Check(swimFrames > 0 && swimPath >= swimWant && swimFast >= 0.75f * swimFrames, $"pool shot: it swims {swimPath:F1} m (at least {swimWant:F1}: 80 % of the chase in its {(si5 >= 0 ? plan[si5].dur : 0f):F2} s, at most 5) and is faster than 0.8 m/s on {swimFast} of {swimFrames} frames (at least 75 %)");
            Check(swimFrames > 0 && swimWake >= 0.6f * swimFrames, $"pool shot: its V wake is on for {swimWake} of {swimFrames} frames (at least 60 %)");
            Check(swimHas && swimOff <= 0.05f, $"pool shot: it lies on its pool's water height within {swimOff:F3} m (at most 0.05)");
            Check(chaseFrames > 0 && chaseNear >= FarChase && windowChaseFrames > 0 && windowChaseLive >= 0.9f * windowChaseFrames && snapped,
                $"the chase: the whole screen never comes nearer than {chaseNear:F1} m to the fleeing robot or the crocodile (at least {FarChase:F0}: no close-up full screen); it goes on in the window's chase shot, live on {windowChaseLive} of {windowChaseFrames} frames (at least 90 %); the jaws have shut: {(snapped ? "yes" : "NO")}");
            bool lentByLevel = OzL16BriefingStage.Animals != null;
            if (levelCrocCount < 0) Check(true, "the level draws no crocodiles of its own yet (the animals step is not in the game): the stage's own two are the only ones in the pool");
            else Check(lentByLevel && otherNear >= 3.5f,
                $"the level draws {levelCrocCount} crocodiles of its own: the staged ones are lent by the level (required): {(lentByLevel ? "yes" : "NO, the stage has no animal code to borrow from: is OzL16AnimalsForBriefing.cs of the animals step in the project?")}; "
                + $"the nearest OTHER crocodile drawn while one was staged: {(otherNear > 1e8f ? "none" : otherNear.ToString("F1") + " m")} (at least 3.5)");
            // C
            int birdShots = 0;
            foreach (var d in list.shots)
            {
                if (d == null || !birdSeen.TryGetValue(d.name, out var seen)) continue;
                birdShots++;
                int crossed = 0; float nearest = 1e9f;
                bool fromAbove = d.name == "our_base_home";                                                // the last shot looks down on the base: its flock passes far under the lens
                foreach (var kv in seen) { if (kv.Value.inside >= 0.25f && (fromAbove || kv.Value.nearest <= 12f)) crossed++; nearest = Mathf.Min(nearest, kv.Value.nearest); }
                Check(crossed >= BirdsNeeded, fromAbove
                    ? $"{d.name}: {crossed} of {seen.Count} parrots are inside the frame for 0.25 s or more, seen from above (at least {BirdsNeeded}; nearest {nearest:F1} m under the lens)"
                    : $"{d.name}: {crossed} of {seen.Count} parrots are inside the frame for 0.25 s or more and come within 12 m of the lens (at least {BirdsNeeded}; nearest {nearest:F1} m)");
            }
            for (int i = birdShots; i < 2; i++) Check(false, "a bird shot is missing: parrots passed the lens in " + birdShots + " shots (at least 2)");
            int birdsAll = 0, popIn = 0, popOut = 0, onCut = 0; string popWhat = "";
            foreach (var shotBirds in birdSeen)
            {
                shotLastFrame.TryGetValue(shotBirds.Key, out int lastOfShot);
                foreach (var kv in shotBirds.Value)
                {
                    var bs = kv.Value; birdsAll++;
                    if (bs.firstOnCut) onCut++;
                    else if (bs.firstInside) { popIn++; if (popWhat.Length == 0) popWhat = $"; first: a bird of {shotBirds.Key} appears in the picture at {bs.firstInto:F2} s"; }
                    bool cutWithShot = bs.lastFrame >= lastOfShot || bs.lastInto >= bs.lastDur - 0.05f;
                    if (!cutWithShot && bs.lastInside) { popOut++; if (popWhat.Length == 0) popWhat = $"; first: a bird of {shotBirds.Key} vanishes in the picture at {bs.lastInto:F2} s"; }
                }
            }
            Check(birdsAll > 0 && popIn + popOut == 0,
                $"no parrot appears or vanishes inside the picture: {popIn} of {birdsAll} first drawn inside it, {popOut} last drawn inside it (must be 0 and 0 at {Screen.width}x{Screen.height}; "
                + $"{onCut} were already flying on the first frame of their shot: a cut, not an appearance){popWhat}");
            // D
            var log = OzSoundLog.Since(logFrom);
            var keys = new Dictionary<string, int>(); int dropped = 0, underVoice = 0, underVoiceLoud = 0, startedCount = 0;
            float bus = CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;
            foreach (var e in log)
            {
                if (e.src != "L16B") continue;
                if (e.outcome != null && e.outcome.StartsWith("DROP")) { dropped++; continue; }
                if (e.outcome == null || !(e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) continue;
                keys.TryGetValue(e.key, out int c); keys[e.key] = c + 1; startedCount++;
                // was a voice speaking when it started? (this check's own samples: the frame before and the frame after must agree)
                float when = e.t;
                int at = voiceAt.FindLastIndex(v => v.t <= when + 1e-4f);
                bool speaking = at >= 0 && voiceAt[at].on && (at + 1 >= voiceAt.Count || voiceAt[at + 1].on) && (at == 0 || voiceAt[at - 1].on);
                if (!speaking) continue;
                underVoice++;
                if (e.vol > Gain(e.key) * bus * ZodFlyover.DipDuck * 1.05f) underVoiceLoud++;
            }
            string[] want = OzL16BriefingStage.SnapSound
                ? new[] { "brief_l16_parrot_flutter", "brief_l16_parrot_squawk", "brief_l16_croc_wake", "brief_l16_croc_surface", OzL16BriefingStage.JawEvent, "brief_l16_croc_splash" }
                : new[] { "brief_l16_parrot_flutter", "brief_l16_parrot_squawk", "brief_l16_croc_wake", "brief_l16_croc_surface" };
            var missing = new List<string>(); var counted = new StringBuilder();
            foreach (var k in want) { keys.TryGetValue(k, out int c); if (c == 0) missing.Add(k); counted.Append(k.Replace("brief_l16_", "")).Append(' ').Append(c).Append(", "); }
            Check(missing.Count == 0 && dropped == 0, $"every staged action has its sound in the sound log: {counted}dropped {dropped}{(missing.Count > 0 ? "; MISSING " + string.Join(", ", missing) : "")}");
            Check(startedCount > 0 && startedCount == OzL16BriefingStage.SoundsStarted - sounds0 && underVoiceLoud == 0,
                $"sounds under a voice (the briefing's own, or the announcer's start line) are ducked like the dip whoosh (x {ZodFlyover.DipDuck:F2}): {startedCount} started, {underVoice} of them while a voice spoke, {underVoiceLoud} of those over the ducked level");
            // the owner's script (items 113, 130): no announcer start line, no camera shutter sound, the jaws clack on their seconds
            int annLines = 0, shutters = 0, jaws = 0;
            foreach (var e in log)
            {
                if (e.src == "VO" && e.key == ZodBriefing.StartLineId && e.outcome != null && e.outcome.StartsWith("PLAY")) annLines++;
                if (e.key == ZodFlyover.ShutterEvent) shutters++;
                if (e.src == "L16B" && e.key == OzL16BriefingStage.JawEvent && e.outcome != null && e.outcome.StartsWith("PLAY")) jaws++;
            }
            Check(annLines == 0 && ZodBriefing.DebugStartLine().StartsWith("not played: the two robots"), $"no announcer start line on this briefing (item 113): {annLines} played; {ZodBriefing.DebugStartLine()}");
            Check(shutters == 0, $"no camera shutter sound (item 130): {shutters} '{ZodFlyover.ShutterEvent}' lines in the sound log");
            int wantJaws = OzL16BriefingStage.SnapsOnVoice != null ? OzL16BriefingStage.SnapsOnVoice.Length : 0;
            Check(wantJaws > 0 && jaws == wantJaws, $"the crocodile's own jaw event '{OzL16BriefingStage.JawEvent}' played {jaws} times (wanted {wantJaws}: once seen from far, twice in the window)");
            WordsLine(log, line);                                                                      // nothing but the clacks and water under a word (OzL16BriefingFlyoverCheck.Words.cs)
            // E
            if (real)
            {
                float voice = Mathf.Min(clip.length, line.duration > 0f ? line.duration : clip.length);
                var cutAt = ZodShotList.CutTimes(list.shots, line);                                       // the anchored cuts on the voice clock
                float preroll = plan.Count > 1 && cutAt.Length == plan.Count && !float.IsNaN(cutAt[1]) && ZodFlyover.LastAnchors.Length == plan.Count && !float.IsNaN(ZodFlyover.LastAnchors[1])
                    ? plan[1].start - cutAt[1] : float.NaN;
                float limit = list.maxTotal > 0f ? list.maxTotal : 24f;
                Check(total <= limit + 0.01f && total >= voice + 0.9f, $"length: the flight is planned {total:F2} s for {voice:F2} s of voice (not over {limit:F1}; window open to first word {preroll:F2} s, after the last word {total - preroll - voice:F2} s; {ZodShotList.LastRoom})");
                float flown = flightEnd - t0;
                Check(Mathf.Abs(flown - total) <= 0.5f, $"length: the shots were on screen for {flown:F2} s, planned {total:F2} s (within 0.5)");
                float opening = Mathf.Min(list.open, limit - (total - preroll));                      // what the list asks for, as far as its limit lets it
                Check(!float.IsNaN(preroll) && list.open > 0f && Mathf.Abs(preroll - opening) <= 0.05f && !plateAfter,
                    $"the first briefing of a mission: the window opens {preroll:F2} s before the first word, the list's own quiet opening is {opening:F2} s (within 0.05: no name-plate joke lead on top); "
                    + $"the name plate was {(plateAfter ? "REWRITTEN: the name-plate joke played on the robots' briefing (ZodShotList.QuietOpening is not asked in ZodBriefing.StartPerf)" : "not rewritten")}");
                int ci = plan.FindIndex(p => p.name == "pool_chase_high") + 1;                              // the shot after the far chase (item 176: the swamp filler, then our fort)
                if (ci > 0 && !float.IsNaN(preroll) && ZodShotList.FindWord(line, "is", -99f, out float isAt, out _))
                    Check(Mathf.Abs(preroll + isAt - ZodFlyover.CutLead - plan[ci].start) <= 0.10f, $"the whole screen leaves the chase on the word 'Is' (the pause after THAT, when the window cuts to the close chase): {plan[ci].start - preroll:F2} s on the voice for {isAt - ZodFlyover.CutLead:F2} (within 0.10)");
                else NotRun("the script: the line does not hold the word 'is', or the first cut is not anchored");
                int hi = plan.FindIndex(p => p.name == "our_base_home"); float lastWordEnd = line.words != null && line.words.Length > 0 ? line.words[line.words.Length - 1].end : voice;
                if (hi > 0 && !float.IsNaN(preroll))
                    Check(plan[hi].start - preroll >= lastWordEnd - 1.2f && plan[hi].start - preroll <= lastWordEnd, $"item 176: the cut to our fort comes in the last second of the talk: {plan[hi].start - preroll:F2} s on the voice, the last word ends {lastWordEnd:F2} s (within 1.2 s before it), after a swamp filler of {plan[hi - 1].dur:F2} s");
                else NotRun("item 176: no our_base_home shot or the first cut is not anchored");
                if (!float.IsNaN(preroll) && line.cues != null)
                {
                    int onBreak = 0; var which = new StringBuilder();
                    for (int i = 1; i < plan.Count; i++)
                        foreach (var c in line.cues)
                            if (c != null && Mathf.Abs(plan[i].start - (preroll + c.start - ZodFlyover.CutLead)) <= 0.13f) { onBreak++; which.Append(i).Append(' '); break; }
                    Check(onBreak >= 2, $"cuts on line breaks: {onBreak} of {plan.Count - 1} cuts fall within 0.13 s of a line break (cuts {which}; at least 2: the canyon on line 4 and the chase seen from far on line 5; the others are on the words 'holiday' and 'Is')");
                }
                else NotRun("cuts on line breaks: the first cut is not anchored on the line");
                Check(plan[plan.Count - 1].dur >= list.lastMin - 0.01f, $"the last shot keeps {plan[plan.Count - 1].dur:F2} s (at least {list.lastMin:F2})");
            }
            else
            {
                NotRun($"length against the voice: the line {LineId} is not in the game yet (voice clip or lines file): the camera-only preview flew");
                NotRun("the flight took as long as planned"); NotRun("the first briefing: the list's quiet opening, no name-plate joke");
                NotRun("the beat: the cut to the crocodiles on the word CROCS");
                NotRun("cuts on line breaks"); NotRun("the last shot's least length");
            }
            // F
            float dp = Vector3.Distance(afterPos, homePos), da = Quaternion.Angle(afterRot, homeRot);       // the first frame after the flight
            Check(dp < 0.01f && da < 0.05f && Mathf.Abs(afterFov - homeFov) < 0.01f, $"hand-back: the camera stands {dp * 100f:F2} cm and {da:F3} degrees from the view it had before the briefing (under 1 cm, 0.05 degrees), field of view {afterFov:F1} (was {homeFov:F1})");
            float jp = Vector3.Distance(afterPos, lastPos), ja = Quaternion.Angle(afterRot, lastRot);
            // the last flown frame lies up to a frame time before the end, where the camera still moves 9.7 m x that time squared: the limit is
            // that for two frame times of the frame limit in force (a slow frame), never under 2 cm (60 a second: 2 cm, 30 a second: 4.3 cm)
            float jfr = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60f, jMax = Mathf.Max(0.02f, 9.7f * (2f / jfr) * (2f / jfr));
            Check(jp < jMax && ja < 0.05f, $"hand-back: no jump between the last frame of the flight and the first frame after it ({jp * 100f:F2} cm, {ja:F3} degrees; under {jMax * 100f:F1} cm at the frame limit {jfr:F0} and 0.05 degrees: the last flown frame lies up to a frame time before the end)");
            int taken = OzL16BriefingStage.Lent - lent0 + OzL16BriefingStage.Built - built0, back = OzL16BriefingStage.GivenBack - back0;
            Check(taken > 0 && back == taken && !OzL16BriefingStage.Swimmer && !OzL16BriefingStage.Snapper && OzL16BriefingStage.BirdsNow.Count == 0,
                $"hand-back: every staged animal is back under the level's rules: {OzL16BriefingStage.Lent - lent0} lent by the level, {OzL16BriefingStage.Built - built0} built by the stage, {back} given back or removed");
            // G
            Check(touchedFrames == 0 && untested.Count == 0, $"the camera is never inside geometry: {frames} frames, {indexed} scene objects indexed, {touchedFrames} frames touching{(touched != null ? " (first: " + touched + ")" : "")}"
                + (untested.Count > 0 ? "; COULD NOT TEST " + untested.Count + " objects near the camera, first: " + First(untested) : ""));

            string verdict = bad == 0 && notRun == 0 ? "PASS" : "FAIL";
            Report = $"{Tag} {verdict} {ok}/{ok + bad + notRun}" + (notRun > 0 ? $" ({notRun} not run)" : "") + $" on {Level} at {Screen.width}x{Screen.height}, {(real ? "the briefing as in the game" : "CAMERA-ONLY PREVIEW (no briefing line)")}, animals {(OzL16BriefingStage.Animals != null ? "lent by the level" : "the stage's own")}"
                   + (string.IsNullOrEmpty(stageWarn) ? "" : " | the stage left out: " + stageWarn);
            var text = new StringBuilder();
            text.AppendLine(Report).AppendLine("plan: " + ZodFlyover.LastPlan).AppendLine("stage: " + OzL16BriefingStage.Status()).AppendLine().Append(lines);
            text.AppendLine().AppendLine("sound log (source L16B):");
            foreach (var e in log) if (e.src == "L16B") text.AppendLine($"  {e.t - logFrom,7:F2} s  {e.key,-26} {e.clip ?? "-",-28} {e.outcome}  vol {e.vol:F3}");
            try { File.WriteAllText(Path.Combine(dir, "checks.txt"), text.ToString().Replace("\r\n", "\n")); } catch (System.Exception ex) { Debug.LogWarning(Tag + " checks.txt not written: " + ex.Message); }
            if (verdict == "PASS") Debug.Log(Report + "\n" + lines); else Debug.LogError(Report + "\n" + lines);
        }

        static string First(HashSet<string> set) { foreach (var s in set) return s; return ""; }

        /// <summary>The stage's level of a cue before the bus and the duck.</summary>
        static float Gain(string key)
        {
            switch (key)
            {
                case "brief_l16_frog": return OzL16BriefingStage.GainFrog;
                case "brief_l16_parrot_flutter": return OzL16BriefingStage.GainFlutter;
                case "brief_l16_parrot_squawk": return OzL16BriefingStage.GainSquawk;
                case "brief_l16_croc_wake": return OzL16BriefingStage.GainWake;
                case "brief_l16_croc_surface": return OzL16BriefingStage.GainSurface;
                case "brief_l16_croc_snap": return OzL16BriefingStage.GainSnap;
                case "critter_croc_jaw_snap": return OzL16BriefingStage.GainSnap;
                case "brief_l16_croc_splash": return OzL16BriefingStage.GainSplash;
            }
            return 1f;
        }
    }
}
