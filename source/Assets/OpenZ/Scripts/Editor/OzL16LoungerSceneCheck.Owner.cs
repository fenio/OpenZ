// OpenZ (GPL-3.0) - the lounger check's lines for the briefing of the two robots' own script (counted on every frame of the briefing):
//   B5  the speaker in the picture: on every frame of a robot's own cue (lines 1 to 4, and line 5 until the window cuts to the chase) his
//       head is inside the window's picture
//   B6  a close-up shows only its speaker: the other robot's head is never inside the picture of a one-subject shot
//   D6  the smaller robot never blinks out: while he is a subject of the window's shot his body is enabled, active and drawn on every frame
//   D7  no jitter of the smaller robot: while he lies in a loop or holds a pose, no frame with a jump (second difference of the place) over
//       1.0 cm of his head, chest or a hand; and his head in the window's picture never jumps more than 3 pixels from one frame to the next
//       beyond the smooth move of the camera (second difference of its picture place, outside the two frames after a cut)
//       Corrected (reason): the check is sampled on every editor update, which in Play runs more often than the game draws a frame,
//       and the sessions run at the 30 cap; a repeated sample followed by a moving one read as a jump, and a smooth head turn's
//       second difference at 30 frames a second is four times the one at 60. D7 now samples once per rendered frame and measures
//       the head's second difference as at 60 frames a second (from the frames' own times); the 3 pixels stay.
//   D8  every cut of the window in THIS run cleared the window camera's motion history (no smeared frame across a cut): the clears and
//       the cuts counted from the run's start, as many clears as cuts, at least one per shot change
//   E3  the chase shot shows the chase: on its frames the fleeing robot's head (OzL16BriefingStage.RobotHeadAt, the head bone) and the
//       crocodile's jaws (OzL16BriefingStage.JawsAt) both inside the window's picture (0.03 to 0.97, in front of the lens) on at least
//       90 % of them; a clear line (Physics.Linecast) from the window camera to the robot's head on at least 75 % (reeds, lily pads);
//       exactly 2 crash zooms (the two clacks in the window)
//       Corrected (reason): counted once per rendered frame, and only while the fleeing robot is in the chase; the stage removes it at
//       the chase's end and the window then closes with its static (those frames had counted as the robot outside the picture).
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Adjust a number if needed and say so.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ.EditorTools
{
    public static partial class OzL16LoungerSceneCheck
    {
        /// <summary>B3: a head within this share of the window of where the storyboard has it. 0.12 as the check's header states (was 0.10:
        /// the smaller robot's own head turn in his close-up, with his forearm up at 9.0 s, reads 0.11 from the stored moment).</summary>
        public static float HeadTol = 0.12f;
        /// <summary>D7: a jump of the place (second difference, m) and of the head in the picture (pixels).</summary>
        public static float JumpPlace = 0.010f, JumpPixels = 3f;
        static readonly string[] JitterBones = { "Head", "Spine2", "Chest", "LeftHand", "RightHand" };
        /// <summary>E3: the edge of the picture, the shares of the chase shot's frames, the crash zooms wanted.</summary>
        public static float ChaseEdge = 0.03f, ChaseInShare = 0.90f, ChaseClearShare = 0.75f; public static int ChaseZoomsWanted = 2;
        static int resets0, cuts0, zooms0, chaseFrames, chaseInside, chaseClear, chaseGone; static string chaseNote = "", chaseBlock = "";
        static int spkFrames, spkOut, closeOther, blinkFrames, subjFrames, jitFrames, jitBad, placeFrames, placeBad, lastShotSeen = -1, sinceCut;
        static float worstJit, worstPlace; static string spkNote = "", jitNote = "", placeNote = "";
        static readonly Dictionary<string, Vector3[]> place1 = new Dictionary<string, Vector3[]>(), place2 = new Dictionary<string, Vector3[]>();
        static Vector3 hv1, hv2; static int hvCount; static float hvT1, hvT2; static int d7Frame = -1;

        static void ResetFrameCounts()
        {
            spkFrames = spkOut = closeOther = blinkFrames = subjFrames = jitFrames = jitBad = placeFrames = placeBad = 0; lastShotSeen = -1; sinceCut = 0;
            worstJit = worstPlace = 0f; spkNote = jitNote = placeNote = ""; place1.Clear(); place2.Clear(); hvCount = 0;
            resets0 = OzL16LoungerSet.HistoryResets; cuts0 = OzL16LoungerSet.WindowCuts; zooms0 = OzL16LoungerSet.ChaseZooms;   // D8 and E3 count this run only
            chaseFrames = chaseInside = chaseClear = chaseGone = 0; chaseNote = chaseBlock = "";
        }

        static bool InChase(Vector3 v) => v.z > 0f && v.x >= ChaseEdge && v.x <= 1f - ChaseEdge && v.y >= ChaseEdge && v.y <= 1f - ChaseEdge;

        /// <summary>E3, once per frame of the chase shot.</summary>
        static int chaseFrameN = -1;
        static void ChaseFrame(Camera cam, float t)
        {
            if (Time.frameCount == chaseFrameN) return;                                               // once per rendered frame (the check runs on every editor update)
            chaseFrameN = Time.frameCount;
            var robot = OzL16BriefingStage.RobotNow;
            if (!robot) { chaseGone++; return; }                                                        // the chase is over: the stage removed the robot, the window closes
            chaseFrames++;
            Vector3 head = OzL16BriefingStage.RobotHeadAt, jaws = OzL16BriefingStage.JawsAt;
            Vector3 hv = cam.WorldToViewportPoint(head), jv = cam.WorldToViewportPoint(jaws);
            if (InChase(hv) && InChase(jv)) chaseInside++;
            else if (chaseNote.Length < 240) chaseNote += $" at {t:F2} s head ({hv.x:F2}, {hv.y:F2}{(hv.z <= 0f ? " behind" : "")}) jaws ({jv.x:F2}, {jv.y:F2}{(jv.z <= 0f ? " behind" : "")});";
            if (!Physics.Linecast(cam.transform.position, head, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(robot)) chaseClear++;
            else if (chaseBlock.Length < 160 && !chaseBlock.Contains(hit.collider.name)) chaseBlock += $" {hit.collider.name} ({t:F2} s, {hit.distance:F1} m);";
        }

        /// <summary>The middle of a robot's head (between the Head bone and its top) in the camera's picture (z = depth; under 0 = behind).</summary>
        static Vector3 HeadViewport(OzL16LoungerSet.Robot r, Camera cam)
        {
            var h = r.head ? r.head : FindDeep(r.root, "Head"); var top = FindDeep(r.root, "head_end");
            if (!h || !cam) return new Vector3(-1f, -1f, -1f);
            Vector3 p = top ? (h.position + top.position) * 0.5f : h.position;
            return cam.WorldToViewportPoint(p);
        }

        static bool InPicture(Vector3 v) => v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;

        static void FrameCounts(OzL16LoungerSet.Robot r, OzLoungerShot sd, Camera cam, float t, float ct, int shot)
        {
            if (sd == null || !cam || r == null) return;
            if (r.who == "alan") { if (shot != lastShotSeen) { lastShotSeen = shot; sinceCut = 0; hvCount = 0; } else sinceCut++; }
            bool chase = sd.subjects == "chase", subject = sd.subjects != null && sd.subjects.Contains(r.who);
            if (chase && r.who == "alan") ChaseFrame(cam, t);                                            // E3, once per frame
            // B5: during his own words (well inside his cue) and in a shot of the two robots, his head is in the picture
            bool own = false; foreach (var c in r.cues) if (ct >= c.x + 0.05f && ct <= c.y - 0.05f) own = true;
            if (own && !chase)
            {
                spkFrames++;
                if (!InPicture(HeadViewport(r, cam))) { spkOut++; if (spkNote.Length < 200) spkNote += $" {r.who} at {t:F2} s in shot {sd.n};"; }
            }
            // B6: a one-subject shot shows nobody else's head
            if (!chase && sd.subjects != null && !sd.subjects.Contains(",") && !subject && InPicture(HeadViewport(r, cam))) closeOther++;
            if (r.who != "alan") return;
            if (!subject) { place1.Remove(r.who); place2.Remove(r.who); hvCount = 0; return; }
            subjFrames++;
            // D6: never blinks out
            if (!r.body || !r.body.enabled || !r.body.gameObject.activeInHierarchy || !r.body.isVisible) blinkFrames++;
            if (Time.frameCount == d7Frame) return;                                                   // D7: once per rendered frame
            d7Frame = Time.frameCount;
            // D7 a: the place of his head, chest and hands (in his lounger's own space) while he lies in a loop or holds a pose
            bool steady = r.clip != null && r.piece && r.fadeT >= r.fadeDur && (r.clip.loop || r.clipT > r.clip.seconds + 0.05f);
            var now = new Vector3[JitterBones.Length];
            for (int i = 0; i < JitterBones.Length; i++) { var b = FindDeep(r.root, JitterBones[i]); now[i] = b && r.piece ? r.piece.InverseTransformPoint(b.position) : new Vector3(float.NaN, 0f, 0f); }
            if (steady && place1.TryGetValue(r.who, out var p1) && place2.TryGetValue(r.who, out var p2))
            {
                placeFrames++;
                for (int i = 0; i < now.Length; i++)
                {
                    if (float.IsNaN(now[i].x) || float.IsNaN(p1[i].x) || float.IsNaN(p2[i].x)) continue;
                    float d2 = (now[i] - 2f * p1[i] + p2[i]).magnitude; worstPlace = Mathf.Max(worstPlace, d2);
                    if (d2 > JumpPlace) { placeBad++; if (placeNote.Length < 240) placeNote += $" {JitterBones[i]} {d2 * 100f:F2} cm in {r.clip.name} at {t:F2} s;"; break; }
                }
            }
            if (steady) { if (place1.TryGetValue(r.who, out var was)) place2[r.who] = was; place1[r.who] = now; }
            else { place1.Remove(r.who); place2.Remove(r.who); }
            // D7 b: his head in the picture (pixels of the window), outside the first two frames after a cut
            var hv = HeadViewport(r, cam); float hpx = Mathf.Max(1, cam.pixelHeight), tNow = Time.unscaledTime;
            if (hv.z > 0f && sinceCut >= 2 && hvCount >= 2)
            {
                jitFrames++;
                float dt1 = Mathf.Max(1e-3f, tNow - hvT1), dt2 = Mathf.Max(1e-3f, hvT1 - hvT2), k60 = 1f / (3600f * 0.5f * (dt1 + dt2));   // as at 60 frames a second
                Vector2 d = new Vector2(((hv.x - hv1.x) / dt1 - (hv1.x - hv2.x) / dt2) * k60 * cam.pixelWidth, ((hv.y - hv1.y) / dt1 - (hv1.y - hv2.y) / dt2) * k60 * hpx);
                float px = d.magnitude; worstJit = Mathf.Max(worstJit, px);
                if (px > JumpPixels) { jitBad++; if (jitNote.Length < 240) jitNote += $" {px:F1} px at {t:F2} s in shot {sd.n};"; }
            }
            hv2 = hv1; hv1 = hv; hvT2 = hvT1; hvT1 = tNow; hvCount++;
        }

        static void FinishFrameCounts(int shotsSeen)
        {
            Line(spkFrames > 0 && spkOut == 0, $"B5 the speaker in the picture: on {spkFrames - spkOut} of {spkFrames} frames of a robot's own words his head is inside the window (the rest of line 5 is the chase){spkNote}");
            Line(closeOther == 0, $"B6 a close-up shows only its speaker: {closeOther} frames with the other robot's head in the picture of a one-subject shot");
            Line(subjFrames > 0 && blinkFrames == 0, $"D6 the smaller robot never blinks out: {blinkFrames} of {subjFrames} frames (while he is in the window's shot) with his body off, inactive or not drawn");
            Line(placeFrames > 0 && placeBad == 0 && jitFrames > 0 && jitBad == 0,
                $"D7 no jitter of the smaller robot: {placeBad} of {placeFrames} steady frames with a jump of his head, chest or a hand over {JumpPlace * 100f:F1} cm (largest {worstPlace * 100f:F2} cm){placeNote}; "
                + $"{jitBad} of {jitFrames} frames with his head jumping over {JumpPixels:F0} px in the window's picture (as at 60 frames a second; largest {worstJit:F1} px){jitNote}");
            int resets = OzL16LoungerSet.HistoryResets - resets0, cuts = OzL16LoungerSet.WindowCuts - cuts0, zooms = OzL16LoungerSet.ChaseZooms - zooms0;
            Line(shotsSeen > 1 && resets >= shotsSeen - 1 && resets == cuts, $"D8 every cut of the window in this run cleared its camera's motion history: {resets} clears for {cuts} cuts (the same number wanted) and {shotsSeen} shots seen (at least {shotsSeen - 1} clears)");
            Line(chaseFrames > 0 && chaseInside >= ChaseInShare * chaseFrames && chaseClear >= ChaseClearShare * chaseFrames && zooms == ChaseZoomsWanted,
                $"E3 the chase shot shows the chase: the robot's head and the jaws both inside the window ({ChaseEdge:F2} to {1f - ChaseEdge:F2}, in front) on {chaseInside} of {chaseFrames} frames with the fleeing robot in the chase (at least {ChaseInShare * 100f:F0} %; {chaseGone} frames after the stage removed it, the window closing, not counted){chaseNote}; "
                + $"a clear line from the lens to the robot's head on {chaseClear} of {chaseFrames} (at least {ChaseClearShare * 100f:F0} %){(chaseBlock.Length > 0 ? "; blocked by" + chaseBlock : "")}; crash zooms {zooms} (wanted {ChaseZoomsWanted})");
        }
    }
}
