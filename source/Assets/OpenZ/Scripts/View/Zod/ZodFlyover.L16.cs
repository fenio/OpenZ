// OpenZ (GPL-3.0)
// The jungle gets its own shot list (it flew the Desert's before: ZodShotList.For answered the Desert list to every level but the Car Park):
//   1 dawn_swamp        sunrise over the north-east pool and the lost pyramid; a parrot flock crosses the lens
//   2 rim_fall_parrots  the north-west pool as a postcard, the rim fall at its far end; parrots come out over the fall and pass the lens
//   3 pool_glide        low over that pool: a crocodile swims through the frame (nose, eyes, V wake)
//   4 crocs             THE OWNER'S BEAT: the cut to the crocodiles on the smaller robot's word, the jaws snap
//   5 enemy_fort        the mission line: low along the middle road to the enemy fort, a flag in the foreground
//   6 our_base_home     our fort, easing into the start camera (the hand-back has no jump)
// hard cuts or one short dip, cuts on the line breaks. New here, and switched on by this list only:
//  - CUT ANCHORS. A shot may say where the cut INTO it belongs: on a word of the briefing line (cutWord: at the word, at the start of
//    its subtitle cue, or at the start of the next cue) or on a cue by its number. The crocodile cut must land on the word, whatever the
//    final recording's times are; a list without anchors (Desert, Car Park) is planned exactly as before.
//  - THE QUIET OPENING and THE ROOM OF THE LAST SHOT. On the other levels the name-plate joke gives the flight 7 s before the first
//    word. The two robots hold this briefing, so there is no joke (the joke corrects the commander's plate): a list that asks for
//    `open` seconds between the window opening and the first word gets them INSTEAD of the joke (QuietOpening; the announcer's
//    briefing line fits into them as on the other levels), and `lastMin` seconds for its last shot; the whole briefing never gets
//    longer than `maxTotal` (the opening gives way first). ZodBriefing.StartPerf asks QuietOpening and BriefingRoom once each.
//  - ZodBriefing.StartLinePlaying: true while the announcer's start line plays (the stage ducks its bird sounds under it).
//  - The animals of the shots (the swimmer, the flock at the lens) are staged by OzL16BriefingStage, which reads CineShown / CinePoseAt.
// Play checks: ZodFlyover.Status(), ZodFlyover.LastPlan, ZodShotList.LastRoom, the menu OpenZ/Checks/L16 briefing flyover (Play mode).
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodCineShotDef
    {
        /// <summary>Cut anchor: the cut INTO this shot belongs to this word of the briefing line (its first occurrence at or after the cut
        /// before it; case and punctuation ignored). Empty = none.</summary>
        public string cutWord;
        /// <summary>With cutWord: 0 = at the word itself, 1 = at the start of the subtitle cue that holds it (a line break), 2 = at the start
        /// of the cue after that one (the next line).</summary>
        public int cutAt;
        /// <summary>Cut anchor without a word, or when the line does not hold cutWord: the 1-based number of the cue whose start is the cut
        /// (0 = none).</summary>
        public int cutCue;
        /// <summary>Seconds added to the anchor (ZodFlyover.CutLead is taken off as for every cut).</summary>
        public float cutOffset;
        /// <summary>When the NEXT shot's anchor cannot be found: the next cut = this shot's cut + hold (0 = none).</summary>
        public float hold;
        /// <summary>This shot never gets less: the next cut is pushed back for it (0 = none).</summary>
        public float min;
    }

    public sealed partial class ZodBriefingLookFile
    {
        /// <summary>The Level 16 shot list (empty = the code list).</summary>
        public ZodCineShotDef[] L16;
        /// <summary>ZodShotList.L16's open / lastMin / maxTotal (0 = keep the code numbers).</summary>
        public float L16Open, L16LastMin, L16MaxTotal;
    }

    public sealed partial class ZodShotList
    {
        /// <summary>The quiet opening this list asks for: seconds from the window opening to the first word (0 = none: the briefing's own preroll).</summary>
        public float open;
        /// <summary>The least length of the last shot, the one that eases into the start camera (0 = none).</summary>
        public float lastMin;
        /// <summary>The whole briefing, window open to hand-back, never gets longer than this through `open` (0 = no limit).</summary>
        public float maxTotal;

        /// <summary>L16 jungle, 208 x 256 m. Enemy (blue) fort at the top (104, -30), ours (red) at the bottom (104, -229), the middle road
        /// between them with a flag at (99, -77). Swamp pools: north-west (x 49..65, z -8..-56, water at -1.25 m, the rim fall at its north
        /// end) and north-east (x 176..203, z -10..-52, water at -1.58 m, the lost pyramid in it). Sun at the 0.935 start: heading 116.6 deg.
        /// Every pose below was flown off-line over the level's height data and placement list before it came into the game.</summary>
        public static ZodShotList L16 = new ZodShotList
        {
            level = "L16",
            // <numbers: written from the level's shot sheet, do not edit by hand>
            open = 0.8f, lastMin = 2.0f, maxTotal = 24f,
            shots = new[]
            {
                // talk in the window: the sunrise, the postcard pool, the canyon from high up, the chase seen from FAR, our fort. The close
                // chase lives only in the window (its last shot). Cuts: on the word 'holiday', on line 4 ('Hey, ...'), on line 5 ('What
                // about THAT?!': the chase is not shown before it is talked about), on 'Is' (the pause after 'THAT?!').
                // 1. Sunrise over the swamp (kept as it was): the low sun disc with its bloom and the birds over it, the lost pyramid.
                new ZodCineShotDef { name = "dawn_swamp", subject = ZodCineSubject.Sunrise, seconds = 6.39f, point = new Vector2(190f, -31f),
                    yawA = 0f, yawB = -1f, pitchA = 8f, pitchB = 9f, distA = 34f, distB = 38f, slide = 8f, fovA = 40f, fovB = 40f, floor = 2f },
                // 2. The holiday postcard (kept): the north-west pool, parrots over the fall; the cut on the word 'holiday'.
                new ZodCineShotDef { name = "rim_fall_parrots", subject = ZodCineSubject.Free, seconds = 4.48f,
                    camA = new Vector3(66.5f, 5.5f, -53.5f), camB = new Vector3(64.8f, 4.6f, -49f), lookA = new Vector3(52.5f, 0f, -20f),
                    lookB = new Vector3(52f, -0.2f, -18f), fovA = 38f, fovB = 38f, floor = 2f, dip = 0.35f, whoosh = true, cutWord = "holiday", cutAt = 0, cutCue = 2 },
                // 3. THE CANYON FROM HIGH UP, calm, during the bigger robot's line 4 ('Hey, as long as we're out of danger, enjoy!'): the two
                //    gorges of the middle of the map with their walls and the falls at their north ends, one slow glide north. The cut on line 4.
                new ZodCineShotDef { name = "canyon_high", subject = ZodCineSubject.Free, seconds = 3.45f, camA = new Vector3(96f, 44f, -160f),
                    camB = new Vector3(101f, 41f, -150f), lookA = new Vector3(100f, -3f, -88f), lookB = new Vector3(104f, -3f, -80f),
                    fovA = 40f, fovB = 40f, floor = 2f, cutWord = "hey", cutAt = 1, cutCue = 4 },
                // 4. THE CHASE SEEN FROM FAR: high over the pool's east bank (about 25 m over the water, 33 m from the chase): the blue robot
                //    flees south through the pool, the crocodile with its wake behind it, small in the frame; the jaws clack once on 'THAT'.
                //    The cut on line 5 ('What about THAT?!'), the start of the cue.
                new ZodCineShotDef { name = "pool_chase_high", subject = ZodCineSubject.Free, seconds = 1.7f, camA = new Vector3(80f, 24f, -31f),
                    camB = new Vector3(79.7f, 23.3f, -34f), lookA = new Vector3(57.4f, -1.2f, -37.5f), lookB = new Vector3(57.4f, -1.2f, -40f),
                    fovA = 40f, fovB = 40f, floor = 2f, cutWord = "what", cutAt = 1, cutCue = 5 },
                //    pool), one slow glide; the cut on 'Is' (the window cuts to the close chase at the same moment); it runs on while the
                //    small robot finishes his line.
                new ZodCineShotDef { name = "swamp_filler_high", subject = ZodCineSubject.Free, seconds = 2.0f, camA = new Vector3(205f, 40f, -88f),
                    camB = new Vector3(201f, 38f, -83f), lookA = new Vector3(186f, -1.6f, -36f), lookB = new Vector3(184f, -1.6f, -32f),
                    fovA = 40f, fovB = 40f, floor = 2f, cutWord = "is" },
                // 6. Our fort from behind and above the south rim, craning up into the start camera of the level; the cut only in the
                new ZodCineShotDef { name = "our_base_home", subject = ZodCineSubject.OwnBaseHome, seconds = 2.0f, yawA = 6f, pitchA = 38f,
                    distA = 74f, fovA = 30f, aimUp = 3f, ease = ZodCineEase.InOut, cutWord = "watch" },
            }
            // </numbers>
        };

        /// <summary>ZodBriefing.StartPerf: this level's shot list brings its own quiet opening, so its level-start briefing plays NO
        /// name-plate joke (the joke would add its own 7 s before the first word and write over the plate of whoever holds the
        /// briefing). False for the Desert and the Car Park: their lists ask for no opening and keep the joke.</summary>
        public static bool QuietOpening(string level)
        {
            var list = For(level);
            return list != null && list.open > 0f;
        }

        /// <summary>EnsureFile: the Level 16 part of the data file.</summary>
        static void LoadL16(ZodBriefingLookFile f)
        {
            if (f == null) return;
            if (f.L16 != null && f.L16.Length > 0) L16.shots = f.L16;
            if (f.L16Open > 0f) L16.open = f.L16Open;
            if (f.L16LastMin > 0f) L16.lastMin = f.L16LastMin;
            if (f.L16MaxTotal > 0f) L16.maxTotal = f.L16MaxTotal;
            Debug.Log($"[ZodFlyover] briefing data: shots L16 {(L16.shots != null ? L16.shots.Length : 0)}, opening {L16.open:F2} s, last shot at least {L16.lastMin:F2} s, at most {L16.maxTotal:F1} s in all");
        }

        /// <summary>ToJson: the Level 16 part.</summary>
        static void SaveL16(ZodBriefingLookFile f)
        {
            if (f == null) return;
            f.L16 = L16.shots; f.L16Open = L16.open; f.L16LastMin = L16.lastMin; f.L16MaxTotal = L16.maxTotal;
        }

        // ---------------------------------------------------------------------------------------- cut anchors
        /// <summary>What BriefingRoom did last (Play check).</summary>
        public static string LastRoom = "no briefing room asked yet";

        static ZodCineShotDef[] NonNull(ZodCineShotDef[] shots)
        {
            var l = new List<ZodCineShotDef>();
            if (shots != null) foreach (var d in shots) if (d != null) l.Add(d);
            return l.ToArray();
        }

        /// <summary>The spoken part of a subtitle cue ("Alan: What ..." -> " What ...").</summary>
        static string Said(string cue)
        {
            if (string.IsNullOrEmpty(cue)) return "";
            int k = cue.IndexOf(':'), sp = cue.IndexOf(' ');
            return k > 0 && k < 14 && (sp < 0 || sp > k) ? cue.Substring(k + 1) : cue;
        }

        /// <summary>Start (s from the voice start) of the first occurrence of `word` at or after `after`, and the index of the cue that holds
        /// it (-1 = the line has no cues). From the word times of the line; a line without word times: the word's place by its letters
        /// inside the cue that holds it.</summary>
        public static bool FindWord(ZodLine line, string word, float after, out float t, out int cue)
        {
            t = float.NaN; cue = -1;
            if (line == null) return false;
            string w = ZodLibrary.Norm(word);
            if (w.Length == 0) return false;
            var cues = line.cues;
            if (line.words != null)
                foreach (var x in line.words)
                {
                    if (x == null || x.start < after - 1e-4f) continue;
                    if (ZodLibrary.Norm(x.word) != w) continue;
                    t = x.start; break;
                }
            if (float.IsNaN(t) && cues != null)
                foreach (var c in cues)
                {
                    if (c == null) continue;
                    string text = ZodLibrary.Norm(Said(c.text));
                    int k = text.IndexOf(w, StringComparison.Ordinal);
                    if (k < 0) continue;
                    float tk = c.start + (c.end - c.start) * k / Mathf.Max(1, text.Length);
                    if (tk < after - 1e-4f) continue;
                    t = tk; break;
                }
            if (float.IsNaN(t)) return false;
            if (cues != null) for (int i = 0; i < cues.Length; i++) if (cues[i] != null && cues[i].start <= t + 0.01f) cue = i;
            return true;
        }

        /// <summary>Seconds from the voice start of the cut INTO each shot, NaN = that cut is not anchored (index 0 always: the first shot
        /// starts with the window).</summary>
        public static float[] CutTimes(ZodCineShotDef[] shots, ZodLine line)
        {
            int n = shots != null ? shots.Length : 0;
            var at = new float[n];
            for (int i = 0; i < n; i++) at[i] = float.NaN;
            if (n < 2 || line == null) return at;
            float lead = ZodFlyover.CutLead;
            var cues = line.cues;
            for (int i = 1; i < n; i++)
            {
                var d = shots[i]; var prev = shots[i - 1];
                if (d == null) continue;
                float t = float.NaN;
                float after = float.IsNaN(at[i - 1]) ? -99f : at[i - 1];
                if (!string.IsNullOrEmpty(d.cutWord) && FindWord(line, d.cutWord, after, out float tw, out int ci))
                {
                    if (d.cutAt <= 0) t = tw;
                    else if (ci >= 0 && d.cutAt == 1) t = cues[ci].start;
                    else if (ci >= 0 && ci + 1 < cues.Length && cues[ci + 1] != null) t = cues[ci + 1].start;
                    if (!float.IsNaN(t)) t += d.cutOffset - lead;
                }
                if (float.IsNaN(t) && d.cutCue > 0 && cues != null && d.cutCue <= cues.Length && cues[d.cutCue - 1] != null)
                    t = cues[d.cutCue - 1].start + d.cutOffset - lead;
                if (float.IsNaN(t) && prev != null && prev.hold > 0f && !float.IsNaN(at[i - 1])) t = at[i - 1] + prev.hold;
                if (!float.IsNaN(t) && prev != null && prev.min > 0f && !float.IsNaN(at[i - 1]) && t - at[i - 1] < prev.min) t = at[i - 1] + prev.min;
                at[i] = t;
            }
            return at;
        }

        /// <summary>ZodBriefing.StartPerf, level-start briefing only: the room the level's shot list asks for. `end` (s from the voice start
        /// to the end of the briefing) grows so that the last shot keeps `lastMin` s after its anchored cut; `preroll` (window open to
        /// first word) grows to `open`, as far as `maxTotal` lets it. A list without these numbers (Desert, Car Park) changes nothing.</summary>
        public static void BriefingRoom(string level, ZodLine line, ref float preroll, float voiceEnd, ref float end)
        {
            var list = For(level);
            if (list == null || list.shots == null || (list.open <= 0f && list.lastMin <= 0f)) return;
            float pre0 = preroll, end0 = end;
            var at = CutTimes(NonNull(list.shots), line);
            int n = at.Length;
            if (n > 0 && list.lastMin > 0f && !float.IsNaN(at[n - 1])) end = Mathf.Max(end, at[n - 1] + list.lastMin);
            if (list.open > 0f)
            {
                float room = list.maxTotal > 0f ? list.maxTotal - end : float.MaxValue;
                preroll = Mathf.Max(preroll, Mathf.Min(list.open, room));
            }
            LastRoom = $"{level}: window open to first word {pre0:F2} -> {preroll:F2} s (asked {list.open:F2}), voice {voiceEnd:F2} s, end {end0:F2} -> {end:F2} s after the voice start "
                     + $"(last shot at least {list.lastMin:F2} s), {preroll + end:F2} s in all (at most {list.maxTotal:F1})";
            Debug.Log("[ZodFlyover] briefing room " + LastRoom);
        }
    }

    public sealed partial class ZodBriefing
    {
        /// <summary>True while the announcer's start line of a level briefing plays (ZodBriefing.StartLine.cs: it has a source of its own,
        /// so ZodFlyover.VoiceOn does not know it). Read by OzL16BriefingStage only, which ducks its sounds under it as under the
        /// briefing's own voice; the dip whoosh of the other levels does not read it and stays as it is.</summary>
        public static bool StartLinePlaying => I && I.startLineOn && I.startLine && I.startLine.isPlaying;
    }

    public sealed partial class ZodFlyover
    {
        /// <summary>The least length an anchored plan leaves any shot (s): a guard for cue sheets with words in an unexpected order.</summary>
        public static float AnchorMin = 0.8f;
        /// <summary>The last anchored plan: per shot the cut on the briefing clock (s from the window opening), NaN = that cut was not
        /// anchored. Empty = the last plan had no anchors.</summary>
        public static float[] LastAnchors = new float[0];

        /// <summary>PlanCine, after Fit: the cuts that are anchored on the line take their places; a free cut keeps Fit's place when it
        /// still lies MinShot inside its anchored neighbours, else it is shared out between them by the nominal lengths. A list without
        /// anchors is left exactly as Fit planned it.</summary>
        void Anchor(float preroll, float total, ZodLine line)
        {
            LastAnchors = new float[0];
            if (cine == null || line == null || game == null) return;
            var list = ZodShotList.For(game.level);
            if (list == null || list.shots == null) return;
            var defs = new List<ZodCineShotDef>();
            foreach (var d in list.shots) if (d != null) defs.Add(d);
            int n = cine.Count;
            if (defs.Count != n || n < 2) return;
            var at = ZodShotList.CutTimes(defs.ToArray(), line);
            bool any = false;
            for (int i = 1; i < n; i++) if (!float.IsNaN(at[i])) any = true;
            if (!any) return;
            var cuts = new float[n + 1]; var pinned = new bool[n + 1];
            for (int i = 0; i < n; i++) cuts[i] = cine[i].start;
            cuts[0] = 0f; cuts[n] = total; pinned[0] = pinned[n] = true;
            for (int i = 1; i < n; i++) if (!float.IsNaN(at[i])) { cuts[i] = preroll + at[i]; pinned[i] = true; }
            for (int i = 1; i < n; i++)
            {
                if (pinned[i]) continue;
                int a = i - 1; while (!pinned[a]) a--;
                int b = i + 1; while (!pinned[b]) b++;
                if (cuts[i] >= cuts[a] + MinShot && cuts[i] <= cuts[b] - MinShot && cuts[i] > cuts[i - 1]) continue;
                float sum = 0f, upto = 0f;
                for (int k = a; k < b; k++) { sum += cine[k].nominal; if (k < i) upto += cine[k].nominal; }
                cuts[i] = Mathf.Lerp(cuts[a], cuts[b], sum > 0f ? upto / sum : 0.5f);
            }
            for (int i = 1; i < n; i++) cuts[i] = Mathf.Min(Mathf.Max(cuts[i], cuts[i - 1] + AnchorMin), total - AnchorMin * (n - i));
            var sb = new StringBuilder();
            sb.Append($"{total:F2} s, {n} shots, anchored:");
            var shown = new float[n];
            for (int i = 0; i < n; i++)
            {
                var s = cine[i]; s.start = cuts[i]; s.dur = Mathf.Max(0.05f, cuts[i + 1] - cuts[i]);
                shown[i] = pinned[i] && i > 0 ? cuts[i] : float.NaN;
                sb.Append($" | {i + 1} {s.name} {s.start:F2}+{s.dur:F2}{(i == 0 ? "" : pinned[i] ? (s.dip > 0f ? " dip, on the line" : " cut, on the line") : s.dip > 0f ? " dip" : " cut")} A{V(s.a.pos)}->B{V(s.b.pos)} fov {s.a.fov:F0}->{s.b.fov:F0}");
            }
            LastPlan = sb.ToString(); LastAnchors = shown;
            Debug.Log($"[ZodFlyover] shot list {cineLevel} ({game.level}), cuts anchored on the line: {LastPlan}");
        }

        /// <summary>The shot on screen and the seconds into it AS SHOWN (a frame held with HoldShot / HoldU included). False while nothing
        /// of the list is on screen (no flight, the orbit flight, the hand-back).</summary>
        // ------------------------------------------------------------------------------------------------ the crocodile part: a hand-held danger camera
        /// <summary>The two crocodile shots of Level 16. Hand-held: drift (deg) in yaw, pitch and roll, shift (m), a fine tremble (deg).
        /// The crash zooms on the jaws, one at every snap: the lens factor, seconds in and out, how far the aim swings to the jaws,
        /// seconds before the snap it starts and after the snap it holds. A shutter click of the interface on each. Statics: tune in Play.</summary>
        public static float HandYaw = 0.55f, HandPitch = 0.45f, HandRoll = 0.6f, HandShift = 0.05f, HandTremble = 0.12f,
            CrashLens = 0.55f, CrashIn = 0.09f, CrashOut = 0.14f, CrashAim = 0.85f, CrashLead = 0.20f, CrashHold = 0.28f, ShutterGain = 0.8f;
        public static string ShutterEvent = "ui_camera_jump";
        /// <summary>Checks: crash zooms begun in this Play session.</summary>
        public static int CrashZooms;
        float crashPrev, l16PoolDur = 4.6f; int crashLastIn = -1;

        void L16Danger(string shot, float into, float dur, bool hold)
        {
            if (cineLevel != "L16" || !camT || hold || !FullScreenDanger) return;
            bool pool = shot == OzL16BriefingStage.SwimShot, cut = shot == OzL16BriefingStage.SnapShot;
            if (!pool && !cut) { crashPrev = 0f; crashLastIn = -1; return; }
            if (pool) l16PoolDur = Mathf.Max(1f, dur);
            float tau = pool ? into : l16PoolDur + into, now = Time.unscaledTime; bool calm = OzMotion.Reduced;
            float k = 0f; int which = -1;
            for (int j = 0; j < OzL16BriefingStage.SnapCount; j++)
            {   // in just before the snap, held over it, out again
                float sn = OzL16BriefingStage.ChaseSnapSecond(j, l16PoolDur), a = sn - CrashLead, b = sn + CrashHold;
                float kj = Mathf.Clamp01((tau - a) / Mathf.Max(0.02f, CrashIn)) * Mathf.Clamp01((b + CrashOut - tau) / Mathf.Max(0.02f, CrashOut));
                if (kj > k) { k = kj; which = j; }
            }
            k = k * k * (3f - 2f * k);
            if (calm) k *= 0.5f;
            if (k > 0.02f && crashPrev <= 0.02f && which != crashLastIn) { crashLastIn = which; CrashZooms++; }   // no camera shutter sound: it read as an explosion
            crashPrev = k;
            float amp = (calm ? 0.25f : 1f) * (1f + 0.6f * k);
            float n1 = Mathf.PerlinNoise(now * 0.9f, 1.3f) - 0.5f, n2 = Mathf.PerlinNoise(4.1f, now * 0.8f) - 0.5f, n3 = Mathf.PerlinNoise(now * 0.7f, 9.2f) - 0.5f;
            float t1 = Mathf.PerlinNoise(now * 7.3f, 2.2f) - 0.5f, t2 = Mathf.PerlinNoise(6.6f, now * 6.7f) - 0.5f;
            var rot = camT.rotation;
            if (k > 0.001f && OzL16BriefingStage.ChaseTau >= 0f)
            {   // the aim swings to the jaws
                var to = OzL16BriefingStage.JawsAt - camT.position;
                if (to.sqrMagnitude > 1f) rot = Quaternion.Slerp(rot, Quaternion.LookRotation(to, Vector3.up), k * CrashAim);
            }
            camT.rotation = rot * Quaternion.Euler((n1 * 2f * HandPitch + t1 * HandTremble) * amp, (n2 * 2f * HandYaw + t2 * HandTremble) * amp, n3 * 2f * HandRoll * amp);
            camT.position += (camT.right * (n2 * 2f) + camT.up * (n1 * 2f)) * (HandShift * amp);
            if (camC && k > 0.001f) camC.fieldOfView = Mathf.Lerp(camC.fieldOfView, camC.fieldOfView * CrashLens, k);
        }

        public bool CineShown(out string name, out float into, out float dur)
        {
            name = null; into = 0f; dur = 0f;
            if (!Active || cine == null || cineHanding) return false;
            bool hold = HoldShot >= 0 && HoldShot < cine.Count;
            int i = hold ? HoldShot : cineIdx;
            if (i < 0 || i >= cine.Count) return false;
            var s = cine[i];
            name = s.name; dur = s.dur;
            into = hold ? Mathf.Clamp01(HoldU) * s.dur : Mathf.Clamp(cineT - s.start, 0f, s.dur);
            return true;
        }

        /// <summary>The camera of a shot of the flying list at `into` seconds of it (the pose the flight will put there, its floor rule
        /// included). False = no such shot.</summary>
        public bool CinePoseAt(string shot, float into, out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = Vector3.zero; rot = Quaternion.identity; fov = 32f;
            if (cine == null) return false;
            foreach (var s in cine)
            {
                if (s.name != shot) continue;
                var p = Eval(s, Mathf.Clamp01(into / Mathf.Max(0.05f, s.dur)));
                pos = p.pos;
                float fl = s.floor >= 0f ? GroundAt(pos) + s.floor : CineFloor(pos);
                if (pos.y < fl) pos.y = fl;
                var dir = p.look - pos;
                if (dir.sqrMagnitude > 1e-4f) rot = Quaternion.LookRotation(dir, Vector3.up);
                fov = p.fov;
                return true;
            }
            return false;
        }
    }
}
