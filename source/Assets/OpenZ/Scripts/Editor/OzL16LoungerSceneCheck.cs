// OpenZ (GPL-3.0) - Play check of the Level 16 lounger scene. Menu: OpenZ/Checks/L16 lounger scene (Play mode).
// Play Level 16 from the menu, let the first briefing end, then run the menu: it plays the briefing once more as the game does
// (ZodBriefing.DebugBriefing) and measures on the BRIEFING'S OWN CLOCK (the voice clip's seconds: the editor may run slower than real
// time, the clock does not), writes checks.txt and the window's picture of every shot into <project>/Logs/L16_lounger_scene and logs
// one line: "[OpenZ L16 lounger scene] PASS n/n".
//   A  the set: built, at the data's place, each piece on the ground, the lounger at the approved size, every clip of the data file
//      loaded with its length
//   B  the window: each of the five window shots was shown, in order, at its second; on every measured frame the shot's subjects
//      stand whole inside the picture and where the storyboard has them (the middle of their box within 0.12 of the kit's own, which
//      is one moment of the shot); at the stored second of every window shot each subject's elbows, wrists, head and head top are
//      within 3 cm of where the STORYBOARD POSE has them (the data file's "joints", metres in his lounger's own space): a box in the
//      picture does not see an arm that sticks out the wrong way, this line does
//   C  the cans: in every sampled frame each robot has exactly one can in a hand and the can stands within 10 degrees of upright;
//      on every measured frame the closed hand's nearest point is under 1 cm from the can's side and no point of it is more than
//      1.5 cm inside the can
//   D  the bodies: no bone of a robot under the cushion's top; the lounger fits as in the approved picture (cushion length to the
//      lying robot's length and cushion width to his shoulder width within 0.04 of the ratios measured on the approved scene, both
//      robots: the data file's "ratios"; ONE lounger model, sized on the bigger robot); the smaller robot 0.945 to 0.957 of the
//      bigger one's height; no joint of a robot moves more than 5 cm between two sampled frames while his clip is a loop or is held
//      at its end (a lying robot that twitches, a held pose that pops)
//   E  the jaws: each jaw opens inside its own subtitle cues and never outside them
//   F  the beats: every beat of the data started its clip within 0.1 s of its second; every beat of the endings' cue sheet has a clip
//   G  the sounds: the sound log holds every sound of the data file as "played", in the data's order (four servos, two creaks), and
//      none dropped, missing or left out
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Adjust a wait or a number if needed and say so.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    public static partial class OzL16LoungerSceneCheck
    {
        [Serializable] class EndBeat { public float t; public string who, beat; }
        [Serializable] class EndScene { public EndBeat[] beats; }
        [Serializable] class EndFile { public EndScene win, lose; }

        static readonly StringBuilder text = new StringBuilder(); static int pass, total; static string dir;
        static OzL16LoungerSet set; static double startedAt, endedAt = -1.0; static bool running, sawBrief;
        static readonly List<int> shotOrder = new List<int>(); static readonly Dictionary<int, float> shotFirst = new Dictionary<int, float>(); static readonly HashSet<int> shotSaved = new HashSet<int>();
        static int frames, canBad, tiltBad, gripBad, gripFrames, boneBad, boxBad, boxFrames, jawOutside; static float worstGrip, worstIn, worstTilt, worstBox, worstBone; static string boxNote = "", boneNote = "";
        static readonly Dictionary<string, int> jawIn = new Dictionary<string, int>(), jawInOpen = new Dictionary<string, int>();
        static Mesh baked;
        // B4: the stored places; D5: steady while lying or holding; G1: the sound log from the run's start
        static readonly HashSet<OzLoungerJoints> jointDone = new HashSet<OzLoungerJoints>(); static int jointPlaces, jointBad; static float worstJoint, worstStep, runAt; static string jointNote = "", stepNote = "";
        static readonly Dictionary<string, Vector3[]> lastPos = new Dictionary<string, Vector3[]>(); static readonly Dictionary<string, string> lastClip = new Dictionary<string, string>(); static int stepPairs, stepBad;

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f) return f; }
            return null;
        }

        static void Line(bool ok, string what) { total++; if (ok) pass++; text.Append(ok ? "PASS  " : "FAIL  ").Append(what).Append('\n'); }

        [MenuItem("OpenZ/Checks/L16 lounger scene (Play mode)")]
        static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ L16 lounger scene] enter Play on Level 16 first (from the menu, let the first briefing end)"); return; }
            set = OzL16LoungerSet.I;
            if (!set || !set.Ready) { Debug.LogError("[OpenZ L16 lounger scene] FAIL 0/1: no lounger set in this scene (" + OzL16LoungerSet.Status() + ")"); return; }
            dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/L16_lounger_scene")); Directory.CreateDirectory(dir);
            text.Length = 0; pass = total = 0; shotOrder.Clear(); shotFirst.Clear(); shotSaved.Clear(); jawIn.Clear(); jawInOpen.Clear();
            frames = canBad = tiltBad = gripBad = gripFrames = boneBad = boxBad = boxFrames = jawOutside = 0; worstGrip = worstIn = worstTilt = worstBox = worstBone = 0f; boxNote = boneNote = ""; sawBrief = false;
            jointDone.Clear(); jointPlaces = jointBad = stepPairs = stepBad = 0; worstJoint = worstStep = 0f; jointNote = stepNote = ""; lastPos.Clear(); lastClip.Clear(); runAt = Time.unscaledTime; ResetFrameCounts();
            string r = ZodBriefing.DebugBriefing();
            text.Append("L16 lounger scene check\n").Append("briefing: ").Append(r).Append('\n').Append(OzL16LoungerSet.Status()).Append('\n');
            if (!r.StartsWith("briefing started")) { Debug.LogError("[OpenZ L16 lounger scene] the briefing did not start: " + r); return; }
            startedAt = EditorApplication.timeSinceStartup; endedAt = -1.0; running = true;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        static Vector3[] Corners(Bounds b)
        {
            var c = new Vector3[8]; int i = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) c[i++] = b.center + Vector3.Scale(b.extents, new Vector3(x, y, z));
            return c;
        }

        static void Tick()
        {
            if (!running) return;
            if (!Application.isPlaying || !set) { running = false; EditorApplication.update -= Tick; return; }
            if (set.BriefingOn) { sawBrief = true; endedAt = -1.0; Sample(); }
            else if (sawBrief || EditorApplication.timeSinceStartup - startedAt > 90.0)
            {   // the briefing is over: both robots ease back to lying (0.9 s); the ratios are measured on the lying pose 2 s later
                if (endedAt < 0.0) endedAt = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - endedAt < 2.0) return;
                running = false; EditorApplication.update -= Tick; Finish();
            }
        }

        static void Sample()
        {
            frames++;
            float t = set.BriefClock; var data = set.Data; int shot = set.WindowShot; var cam = set.WindowCamera;
            if (shot > 0 && (shotOrder.Count == 0 || shotOrder[shotOrder.Count - 1] != shot)) { shotOrder.Add(shot); if (!shotFirst.ContainsKey(shot)) shotFirst[shot] = t; }
            OzLoungerShot sd = null; foreach (var s in data.shots) if (s.n == shot) sd = s;
            // the window's picture of each shot, once, a third into it
            if (sd != null && !shotSaved.Contains(shot) && t > sd.t0 + Mathf.Min(1.2f, (sd.t1 - sd.t0) * 0.4f))
            { shotSaved.Add(shot); text.Append("picture   ").Append(ZodBriefing.DebugSaveFeed(Path.Combine(dir, "window_shot" + shot + "_" + sd.name + ".png"))).Append('\n'); }
            bool measure = frames % 6 == 0;                                                              // the mesh is measured on every sixth frame
            var v = set.BriefVoice; float ct = v && v.isPlaying && v.clip ? v.timeSamples / (float)Mathf.Max(1, v.clip.frequency) : -1f;
            foreach (var r in set.Robots)
            {
                FrameCounts(r, sd, cam, t, ct, shot);                                                      // B5, B6, D6, D7 on every frame
                // C: one can in a hand, upright
                int on = 0, k = -1; for (int i = 0; i < 2; i++) if (r.can[i] && r.can[i].gameObject.activeInHierarchy) { on++; k = i; }
                if (on != 1) canBad++;
                Vector3 axis = Vector3.up, cc = Vector3.zero; float rad = 0.085f, half = 0.159f;
                if (k >= 0)
                {   // the can's own long line, middle, radius and half height from its mesh (whatever axis it was modelled along)
                    var can = r.can[k]; var mf = can.GetComponentInChildren<MeshFilter>(true);
                    if (mf && mf.sharedMesh)
                    {
                        var b = mf.sharedMesh.bounds; Vector3 e = b.extents; int ax = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
                        Vector3 la = Vector3.zero; la[ax] = 1f; axis = mf.transform.TransformDirection(la).normalized; cc = mf.transform.TransformPoint(b.center);
                        float s = Mathf.Abs(mf.transform.lossyScale.x); half = e[ax] * s; rad = (e.x + e.y + e.z - e[ax]) * 0.5f * s;
                    }
                    else { axis = can.forward; cc = can.position; float s = Mathf.Abs(can.lossyScale.x); rad *= s; half *= s; }
                    float tilt = Vector3.Angle(axis, Vector3.up); tilt = Mathf.Min(tilt, 180f - tilt);
                    if (r.clip == null || !r.clip.tilted) { worstTilt = Mathf.Max(worstTilt, tilt); if (tilt > 10f) tiltBad++; }   // round 3: not while he drinks
                }
                // E: jaws inside their own cues only (the jaw needs about 0.15 s to shut after its cue: counted as outside only after that)
                bool inCue = false, nearCue = false;
                foreach (var c in r.cues) { if (ct >= c.x && ct <= c.y) inCue = true; if (ct >= c.x - 0.2f && ct <= c.y + 0.45f) nearCue = true; }
                if (!jawIn.ContainsKey(r.who)) { jawIn[r.who] = 0; jawInOpen[r.who] = 0; }
                if (inCue) { jawIn[r.who]++; if (r.jaw > 0.02f) jawInOpen[r.who]++; }
                else if (!nearCue && r.jaw > 0.08f && ct >= 0f) jawOutside++;
                // B4: at the stored second of this window shot his joints are where the storyboard pose has them
                if (data.joints != null && r.piece)
                    foreach (var j in data.joints)
                    {
                        if (j.shot != shot || j.who != r.who || t < j.t || jointDone.Contains(j) || j.bone == null) continue;
                        jointDone.Add(j);
                        if (t > j.t + 0.6f) { jointBad++; if (jointNote.Length < 300) jointNote += $" shot {shot} {r.who}: no frame within 0.6 s of {j.t:F2} s;"; continue; }
                        for (int i = 0; i < j.bone.Length; i++)
                        {
                            var tr = FindDeep(r.root, j.bone[i]);
                            if (!tr) { jointBad++; if (jointNote.Length < 300) jointNote += $" {r.who} has no {j.bone[i]};"; continue; }
                            float d = Vector3.Distance(r.piece.InverseTransformPoint(tr.position), new Vector3(j.x[i], j.y[i], j.z[i]));
                            jointPlaces++; worstJoint = Mathf.Max(worstJoint, d);
                            if (d > (data.jointTol > 0f ? data.jointTol : 0.03f)) { jointBad++; if (jointNote.Length < 300) jointNote += $" shot {shot} {r.who} {j.bone[i]} {d * 100f:F1} cm off at {t:F2} s;"; }
                        }
                    }
                // D5: nothing jumps while he lies in a loop or holds the end of a clip
                bool steady = r.clip != null && r.piece && r.body && r.fadeT >= r.fadeDur && (r.clip.loop || r.clipT > r.clip.seconds + 0.05f);
                if (steady)
                {
                    var bs = r.body.bones; var nowPos = new Vector3[bs.Length];
                    for (int i = 0; i < bs.Length; i++) nowPos[i] = bs[i] ? r.piece.InverseTransformPoint(bs[i].position) : Vector3.zero;
                    if (lastPos.TryGetValue(r.who, out var was) && was != null && was.Length == nowPos.Length && lastClip.TryGetValue(r.who, out var wasClip) && wasClip == r.clip.name)
                    {
                        stepPairs++;
                        for (int i = 0; i < bs.Length; i++)
                        {
                            float d = Vector3.Distance(was[i], nowPos[i]); worstStep = Mathf.Max(worstStep, d);
                            if (d > 0.05f) { stepBad++; if (stepNote.Length < 300) stepNote += $" {r.who} {(bs[i] ? bs[i].name : "?")} {d * 100f:F1} cm in {r.clip.name} at {t:F2} s;"; }
                        }
                    }
                    lastPos[r.who] = nowPos; lastClip[r.who] = r.clip.name;
                }
                else lastPos[r.who] = null;
                if (!measure || !r.body || !r.body.sharedMesh) continue;
                if (!baked) baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                r.body.BakeMesh(baked, true);
                var verts = baked.vertices; var bw = r.body.sharedMesh.boneWeights; var bones = r.body.bones; var l2w = r.body.transform.localToWorldMatrix;
                // B: this robot inside the window's picture (whole in a two-shot, his head in a close-up), his head where the storyboard has it
                if (sd != null && cam && sd.subjects != null && sd.subjects.Contains(r.who))
                {
                    var want = r.who == "alan" ? sd.alanHead : sd.bradHead; float x0 = 9f, y0 = 9f, x1 = -9f, y1 = -9f; bool behind = false;
                    for (int i = 0; i < verts.Length; i += 5)
                    {
                        var vp = cam.WorldToViewportPoint(l2w.MultiplyPoint3x4(verts[i])); if (vp.z <= 0f) behind = true;
                        x0 = Mathf.Min(x0, vp.x); y0 = Mathf.Min(y0, vp.y); x1 = Mathf.Max(x1, vp.x); y1 = Mathf.Max(y1, vp.y);
                    }
                    boxFrames++;
                    bool close = sd.subjects == r.who;                                                       // a close-up: his head must be inside, not the whole body
                    Vector3 hv = HeadViewport(r, cam);
                    bool inside = close ? hv.z > 0f && hv.x > 0.02f && hv.x < 0.98f && hv.y > 0.02f && hv.y < 0.98f : !behind && x0 > -0.02f && y0 > -0.02f && x1 < 1.02f && y1 < 1.02f;
                    float off = want != null && want.Length == 2 && hv.z > 0f ? Mathf.Max(Mathf.Abs(hv.x - want[0]), Mathf.Abs(hv.y - want[1])) : 0f;
                    if (!inside || off > HeadTol) { boxBad++; if (boxNote.Length < 300) boxNote += $" shot {shot} {r.who} at {t:F2} s: head ({hv.x:F2}, {hv.y:F2}), stored ({(want != null && want.Length == 2 ? want[0] : -1f):F2}, {(want != null && want.Length == 2 ? want[1] : -1f):F2}), inside {inside};"; }
                    worstBox = Mathf.Max(worstBox, off);
                }
                // C: the closed hand on the can
                if (k >= 0)
                {
                    string hand = k == 1 ? "RightHand" : "LeftHand"; float near = 9f, deep = 0f; int n = 0;
                    for (int i = 0; i < verts.Length && i < bw.Length; i++)
                    {
                        int bi = bw[i].boneIndex0; if (bi < 0 || bi >= bones.Length || !bones[bi] || bones[bi].name != hand) continue;
                        Vector3 d = l2w.MultiplyPoint3x4(verts[i]) - cc; float along = Vector3.Dot(d, axis); if (Mathf.Abs(along) > half) continue;
                        float radial = (d - axis * along).magnitude; n++;
                        near = Mathf.Min(near, Mathf.Abs(radial - rad)); deep = Mathf.Max(deep, rad - radial);
                    }
                    gripFrames++;
                    if (n == 0) { near = 9f; }
                    worstGrip = Mathf.Max(worstGrip, near); worstIn = Mathf.Max(worstIn, deep);
                    if (near > 0.01f || deep > 0.015f) gripBad++;
                }
                // D: no bone of the body under the cushion (over the flat part of the cushion; the raised back has its own slope)
                if (r.piece)
                {
                    float top = data.cushion.top;
                    foreach (var b in bones)
                    {
                        if (!b) continue;
                        Vector3 lp = r.piece.InverseTransformPoint(b.position);
                        OzLoungerRatio own3 = null; if (data.ratios != null) foreach (var q3 in data.ratios) if (q3.who == r.who) own3 = q3;
                        // round 3: over HIS cushion's flat part only (his ratio row; the first kit's numbers when the row has none)
                        float half3 = own3 != null && own3.cushionWidth > 0f ? own3.cushionWidth * 0.5f - 0.05f : 0.55f, end3 = own3 != null && own3.cushionFlat > 0f ? -(own3.cushionFlat - 0.05f) : -1.75f;
                        if (Mathf.Abs(lp.x) > half3 || lp.z < end3 || lp.z > -0.05f) continue;
                        float under = (top - 0.10f) - lp.y;                                              // the cushion may be pressed in by up to 10 cm
                        if (under > 0f) { boneBad++; worstBone = Mathf.Max(worstBone, under); if (boneNote.Length < 200) boneNote += $" {r.who} {b.name} {under * 100f:F1} cm at {t:F2} s;"; }
                    }
                }
            }
        }

        static float Extent(SkinnedMeshRenderer smr, Transform frame, Func<string, bool> part, int axis, out float lo, out float hi)
        {
            lo = 9f; hi = -9f;
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave }; smr.BakeMesh(m, true);
            var verts = m.vertices; var bw = smr.sharedMesh.boneWeights; var bones = smr.bones; var l2w = smr.transform.localToWorldMatrix;
            for (int i = 0; i < verts.Length && i < bw.Length; i++)
            {
                int bi = bw[i].boneIndex0; if (bi < 0 || bi >= bones.Length || !bones[bi] || !part(bones[bi].name)) continue;
                float v = frame.InverseTransformPoint(l2w.MultiplyPoint3x4(verts[i]))[axis]; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
            }
            UnityEngine.Object.DestroyImmediate(m);
            return hi - lo;
        }

        static void Finish()
        {
            var data = set.Data;
            // A
            var root = set.Root;
            Line(Mathf.Abs(root.position.x - data.place.x) < 0.05f && Mathf.Abs(root.position.z - data.place.z) < 0.05f, $"A1 the set stands at the data's place: ({root.position.x:F2}, {root.position.z:F2}) for ({data.place.x:F2}, {data.place.z:F2})");
            float worstGap = 0f; string gapNote = "";
            foreach (var p in data.pieces)
            {
                var t = set.Piece(p.name); if (!t) { gapNote += " " + p.name + " MISSING;"; worstGap = 9f; continue; }
                float g = Mathf.Abs(t.position.y - (ZGame.I ? ZGame.I.GroundY(t.position.x, t.position.z) : 0f)); worstGap = Mathf.Max(worstGap, g); gapNote += $" {p.name} {g * 100f:F1} cm;";
            }
            Line(worstGap < 0.06f, "A2 every piece on the level's ground (its origin within 6 cm of the ground under it):" + gapNote);
            var lo = set.Piece("lounger_brad"); var lr = lo ? lo.GetComponentInChildren<Renderer>() : null;
            Vector3 sz = lr ? lr.localBounds.size : Vector3.zero;
            Line(lr && Mathf.Abs(sz.z - data.loungerSize.length) < 0.06f && Mathf.Abs(sz.x - data.loungerSize.width) < 0.06f, $"A3 the lounger at the approved size: {sz.z:F2} x {sz.x:F2} m (length x width; the data {data.loungerSize.length:F2} x {data.loungerSize.width:F2})");
            int clipsOk = 0; string clipNote = "";
            foreach (var c in data.clips)
            {
                var ac = Resources.Load<AnimationClip>(c.res);
                bool good = ac && !ac.empty && AnimationUtility.GetCurveBindings(ac).Length > 0 && Mathf.Abs(ac.length - c.seconds) < 0.06f;
                if (good) clipsOk++; else if (clipNote.Length < 300) clipNote += " " + c.who + " " + c.name + (ac ? $" ({ac.length:F2} s for {c.seconds:F2}, {AnimationUtility.GetCurveBindings(ac).Length} curves)" : " (not loaded)") + ";";
            }
            Line(clipsOk == data.clips.Length, $"A4 every clip of the data file is loaded, has curves and its length: {clipsOk} of {data.clips.Length}{clipNote}");
            // B
            var wantOrder = new List<int>(); foreach (var s in data.shots) if (s.window) wantOrder.Add(s.n);
            Line(string.Join(",", shotOrder) == string.Join(",", wantOrder), "B1 the window showed its shots in order: " + string.Join(",", shotOrder) + " (wanted " + string.Join(",", wantOrder) + "; all five are the window's: shot 5 is the chase)");
            float worstCut = 0f; string cutNote = "";
            foreach (var s in data.shots) { if (!s.window || s.n == 1 || !shotFirst.ContainsKey(s.n)) continue; float d = Mathf.Abs(shotFirst[s.n] - s.t0); worstCut = Mathf.Max(worstCut, d); cutNote += $" {s.n}: {shotFirst[s.n]:F2} s for {s.t0:F2};"; }
            Line(shotFirst.Count >= wantOrder.Count && worstCut < 0.1f, "B2 each window shot began at its second (within 0.1 s):" + cutNote);
            Line(boxFrames > 0 && boxBad == 0, $"B3 the subjects inside the window's picture (whole in a two-shot, the head in a close-up) and each head where the storyboard has it (within {HeadTol:F2} of the window, with the push, the slide and the drift): {boxBad} bad of {boxFrames} measured robot frames, the largest head offset {worstBox:F2}{boxNote}");
            FinishFrameCounts(shotOrder.Count);
            int jointRows = data.joints != null ? data.joints.Length : 0;
            Line(jointRows > 0 && jointDone.Count == jointRows && jointBad == 0, $"B4 elbows, wrists, head and head top where the storyboard pose has them at the stored second of every window shot (within {(data.jointTol > 0f ? data.jointTol : 0.03f) * 100f:F0} cm: they breathe and look about now): {jointBad} bad of {jointPlaces} places in {jointDone.Count} of {jointRows} rows, the largest gap {worstJoint * 100f:F1} cm{jointNote}");
            // C
            Line(canBad == 0, $"C1 exactly one can in a hand of each robot in every frame: {canBad} bad of {frames * 2}");
            Line(gripFrames > 0 && gripBad == 0, $"C2 the closed hand on the can: its nearest point at most {worstGrip * 100f:F2} cm from the can's side (under 1 cm), deepest point inside the can {worstIn * 100f:F2} cm (under 1.5 cm), {gripBad} bad of {gripFrames} measured hand frames (grips " + (set.Find("alan").gripOk && set.Find("brad").gripOk ? "in" : "MISSING") + ")");
            Line(tiltBad == 0, $"C3 the cans upright within 10 degrees: worst {worstTilt:F1} degrees, {tiltBad} bad");
            // D
            Line(boneBad == 0, $"D1 no bone of a robot under the cushion: {boneBad} bad, deepest {worstBone * 100f:F1} cm{boneNote}");
            foreach (var r in set.Robots)
            {
                if (!r.body || !r.piece) { Line(false, "D2 " + r.who + ": no body to measure"); continue; }
                // lying length along the cushion's own line: boots' reach from the fold on the flat part + the head's reach up the raised back
                Extent(r.body, r.piece, n => n.Contains("Foot") || n.Contains("Toe") || n.Contains("Leg"), 2, out float zlo, out _);
                float back = data.cushion.backDeg * Mathf.Deg2Rad, headUp = 0f;
                var m = new Mesh { hideFlags = HideFlags.HideAndDontSave }; r.body.BakeMesh(m, true); var l2w = r.body.transform.localToWorldMatrix;
                foreach (var v in m.vertices) { Vector3 p = r.piece.InverseTransformPoint(l2w.MultiplyPoint3x4(v)); if (p.z > 0f) headUp = Mathf.Max(headUp, p.z * Mathf.Cos(back) + (p.y - data.cushion.top) * Mathf.Sin(back)); }
                UnityEngine.Object.DestroyImmediate(m);
                OzLoungerRatio want = null; if (data.ratios != null) foreach (var q in data.ratios) if (q.who == r.who) want = q;
                // round 2: a lounger scaled to EACH robot: his own cushion from his ratio row (the first kit's data: one cushion for both)
                float cushLen = want != null && want.cushionLength > 0f ? want.cushionLength : data.cushion.length, cushWid = want != null && want.cushionWidth > 0f ? want.cushionWidth : data.cushion.width;
                float body = -zlo + headUp, lenRatio = cushLen / Mathf.Max(0.1f, body);
                float shoulders = Extent(r.body, r.piece, n => n.Contains("Shoulder") || n.Contains("Pad") || (r.who == "alan" && n.EndsWith("Arm") && !n.Contains("Fore")), 0, out _, out _);
                float widRatio = cushWid / Mathf.Max(0.1f, shoulders);
                // end along its line, against his length lying STRAIGHT (both from his ratio row: he lies with his knees over the foot end now, so
                // his length cannot be measured on him), never over lengthMost; and, measured here, his boots at least 0.30 m past the foot end.
                float most3 = want != null && want.lengthMost > 0f ? want.lengthMost : 1.15f, straight3 = want != null && want.lyingLength > 0f ? want.lyingLength : body;
                float whole3 = want != null && want.wholeLength > 0f ? want.wholeLength : cushLen, ratio3 = whole3 / Mathf.Max(0.1f, straight3), past3 = want != null && want.footEnd > 0f ? -zlo - want.footEnd : 9f;
                Line(want != null && ratio3 <= most3 && past3 >= 0.30f, $"D2 {r.who}: his whole lounger {whole3:F2} m (head end to foot end) to his length lying straight {straight3:F2} m = {ratio3:F3} (at most {most3:F2}), his boots {(past3 > 8f ? 0f : past3):F2} m past its foot end (at least 0.30; his cushion's length {cushLen:F2} m, as he lies now {body:F2} m)");
                float wide3 = want != null && want.widthMost > 0f ? want.widthMost : 1.25f;
                Line(want != null && Mathf.Abs(widRatio - want.width) <= 0.04f && widRatio <= wide3, $"D3 {r.who}: his cushion's width {cushWid:F2} m to his shoulder width {shoulders:F2} m = {widRatio:F3} (at most {wide3:F2}: his shoulders and arms overhang; this kit's scene: {(want != null ? want.width : 0f):F3}, within 0.04; the smaller robot: across his upper arms)");
            }
            var a = set.Find("alan"); var b2 = set.Find("brad");
            float ha = a.body && a.body.sharedMesh ? Mathf.Max(a.body.sharedMesh.bounds.size.x, Mathf.Max(a.body.sharedMesh.bounds.size.y, a.body.sharedMesh.bounds.size.z)) * a.root.lossyScale.y : 0f;
            float hb = b2.body && b2.body.sharedMesh ? Mathf.Max(b2.body.sharedMesh.bounds.size.x, Mathf.Max(b2.body.sharedMesh.bounds.size.y, b2.body.sharedMesh.bounds.size.z)) * b2.root.lossyScale.y : 0f;
            float hr = hb > 0f ? ha / hb : 0f;
            Line(hr >= 0.945f && hr <= 0.957f, $"D4 standing heights: the smaller robot {ha:F3} m, the bigger {hb:F3} m, ratio {hr:F3} (0.945 to 0.957; the game's own scale rule gives 0.954)");
            Line(stepPairs > 0 && stepBad == 0, $"D5 no joint moves more than 5 cm between two sampled frames while a robot lies in a loop or holds the end of a clip: {stepBad} bad, the largest step {worstStep * 100f:F2} cm over {stepPairs} pairs of frames{stepNote}");
            // E
            foreach (var r in set.Robots)
            {
                int n = jawIn.TryGetValue(r.who, out var x) ? x : 0, o = jawInOpen.TryGetValue(r.who, out var y) ? y : 0;
                Line(n > 10 && o > n * 0.4f, $"E1 {r.who}: jaw open on {o} of {n} frames inside his own cues (more than 40 %)");
            }
            Line(jawOutside == 0, $"E2 no jaw open outside its own cues while the voice plays: {jawOutside} frames");
            // F
            int beats = 0, onTime = 0; string beatNote = "";
            foreach (var bd in data.beats)
            {
                if (bd.scene != "brief" || bd.t < -900f || string.IsNullOrEmpty(bd.clip)) continue;
                beats++; float best = 9f;
                foreach (var l in set.BeatLog) if (l.who == bd.who && l.beat == bd.beat) best = Mathf.Min(best, Mathf.Abs(l.t - bd.t));
                if (best < 0.1f) onTime++; else beatNote += $" {bd.who} {bd.beat} off {best:F2} s;";
            }
            Line(beats > 0 && onTime == beats, $"F1 the briefing's beats started their clips at their seconds (within 0.1 s): {onTime} of {beats}{beatNote}");
            var ta = Resources.Load<TextAsset>("OpenZ/Zod/ending_L16_cues");
            if (!ta) Line(false, "F2 the endings' cue sheet (Resources/OpenZ/Zod/ending_L16_cues) is not in the game: its beats were not checked");
            else
            {
                var ef = JsonUtility.FromJson<EndFile>(ta.text); int nb = 0, okb = 0; string miss = "";
                foreach (var pair in new[] { ("win", ef.win), ("lose", ef.lose) })
                    if (pair.Item2 != null && pair.Item2.beats != null)
                        foreach (var eb in pair.Item2.beats)
                        {
                            if (eb.who != "alan" && eb.who != "brad" && eb.who != "both") continue;
                            nb++; if (set.HasBeat(pair.Item1, eb.who, eb.beat)) okb++; else miss += $" {pair.Item1} {eb.who} {eb.beat};";
                        }
                Line(nb > 0 && okb == nb, $"F2 every robot beat of the endings' cue sheet has its clip: {okb} of {nb}{miss}");
            }
            // G: every sound of the data file is in the sound log as played, in the data's order
            var wantSnd = new List<string>(); foreach (var sd in data.sounds) wantSnd.Add(sd.ev == "zod_boys_servo" ? "brief_" + sd.at + "_servo" : "brief_" + sd.ev);
            var gotSnd = new List<string>(); int sndBad = 0; string sndNote = OzSoundLog.On ? "" : " THE SOUND LOG IS SWITCHED OFF;";
            foreach (var e in OzSoundLog.Since(runAt))
            {
                if (e.src != "L16Loungers" || e.key == null || !e.key.StartsWith("brief_")) continue;
                gotSnd.Add(e.key);
                if (e.outcome != "played") { sndBad++; if (sndNote.Length < 300) sndNote += $" {e.key}: {e.outcome};"; }
            }
            Line(wantSnd.Count > 0 && sndBad == 0 && string.Join(",", gotSnd) == string.Join(",", wantSnd), $"G1 the sound log holds the briefing's {wantSnd.Count} set sounds as played, in order, none dropped: {gotSnd.Count - sndBad} played of {wantSnd.Count} wanted ({string.Join(", ", gotSnd)}){sndNote}");
            text.Append(OzL16LoungerSet.Status()).Append('\n');
            string head = $"[OpenZ L16 lounger scene] {(pass == total ? "PASS" : "FAIL")} {pass}/{total} on the briefing's own clock, {frames} frames sampled";
            File.WriteAllText(Path.Combine(dir, "checks.txt"), head + "\n" + text);
            if (pass == total) Debug.Log(head + " (" + dir + ")"); else Debug.LogError(head + "\n" + text);
        }
    }
}
