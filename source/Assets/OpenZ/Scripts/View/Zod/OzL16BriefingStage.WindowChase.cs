// OpenZ (GPL-3.0) - Level 16 briefing, the two robots' own script: the crocodile chase goes on into the transmission WINDOW.
//  - The whole screen shows the chase only from FAR: its chase shot (SwimShot, a high camera over the pool's east bank, from the smaller
//    robot's "What about THAT?!") starts the chase clock; the chase then goes on through the shot after it until the window closes, so
//    that the window's last shot (the lounger set's chase shot, OzL16LoungerSet.Window.cs) shows it close. No close-up of the chase on
//    the whole screen.
//  - The jaws clack on the briefing's own clock (seconds on the voice clip, SnapsOnVoice), whatever the cuts are: once, seen from far,
//    on "THAT" (the far chase is on the whole screen from that line only), and twice in the window (after "CROCS" and after "Watch out").
//  - Each clack plays the crocodile's OWN jaw event (JawEvent) with the crocodile's own clips, in turn, 2D like every sound of this stage
//    (the chase is far from the listener while the window shows it). CrocJawFiles asks the crocodile code for its list (a static string[]
//    JawSnapFiles on CrocodileView, when the crocodile code has one), else it plays the clips the crocodile plays today.
//  - Under the two robots' words nothing of the chase sounds but the clacks and the water: the crocodile's lunge roar is never played on
//    the far clack (it lunges about 33 m away), and a roar, a growl or the fleeing robot's alarm plays only where it starts and ends
//    between two words of the briefing's line (else it is left out and the sound log says so). The roar's level is the crocodile's own
//    ratio (GainRoar).
// Play checks read: Jaws (clacks played in this session), ChasePoolDur, SnapsOnVoice, RobotHeadAt, Hushed, ClipSeconds.
using System;
using System.Reflection;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16BriefingStage
    {
        /// <summary>The seconds on the voice clip at which the jaws clack shut (the window shows the last two).</summary>
        public static float[] SnapsOnVoice = { 14.62f, 16.95f, 18.25f };
        /// <summary>The cut into the chase shot on the voice clock, used only when the briefing's clock cannot be read.</summary>
        public static float ChaseCutOnVoice = 13.52f;
        /// <summary>The crocodile's own jaw event and the clips it plays today (the crocodile code's list wins when it has one).</summary>
        public static string JawEvent = "critter_croc_jaw_snap";
        public static string[] JawFilesNow => SnapFiles;
        /// <summary>The length of the chase shot of the whole screen in this flight (s; below 0 = no chase yet).</summary>
        public static float ChasePoolDur = -1f;
        /// <summary>Play check: jaw clacks played in this session.</summary>
        public static int Jaws;
        /// <summary>The lunge roar on the far clack (the first one, seen from far on the whole screen): off, it would sound on a word.</summary>
        public static bool RoarOnFarClack = false;
        /// <summary>The line whose words the chase's roar, growl and alarm keep clear of, and the seconds kept clear before and after a word.</summary>
        public static string OwnerLineId = "zod_brief_L16"; public static float WordGap = 0.05f;
        /// <summary>Play check: roars, growls and alarms left out (on the far clack, or because they would sound under a word).</summary>
        public static int Hushed;
        /// <summary>The fleeing robot's head now (its head bone; without one 0.8 m over the water above its feet; its feet are RobotSink
        /// under the water): the window's chase camera and its check aim here.</summary>
        public static Vector3 RobotHeadAt => I && I.robotHead ? I.robotHead.position : RobotNow ? RobotNow.position + Vector3.up * (RobotSink + 0.8f) : Vector3.zero;
        float chaseVoice0 = float.NaN; bool chaseBegun; int jawTurn = -1;
        static string[] jawFilesAsked; static bool jawFilesTried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetWindowChaseStatics() { ChasePoolDur = -1f; Jaws = Hushed = 0; jawFilesAsked = null; jawFilesTried = false; }

        /// <summary>The voice clock of the briefing the two robots hold (NaN = none).</summary>
        static float VoiceClock()
        {
            var s = OzL16LoungerSet.I;
            return s && s.BriefingOn ? s.BriefClock : float.NaN;
        }

        /// <summary>TickCrocs: the chase began in this flight and the whole screen has cut on: it goes on (the window shows it).</summary>
        bool ChaseGoesOn(string shot) => chaseBegun && crocsOn && !string.IsNullOrEmpty(shot) && shot != SwimShot;

        /// <summary>TickCrocs, every frame: the chase clock's start on the voice clock, and the clacks' seconds on the chase clock.</summary>
        void ChaseClock(string shot, float into, float dur, bool swim)
        {
            if (swim && !chaseBegun)
            {
                chaseBegun = true;
                float v = VoiceClock();
                chaseVoice0 = float.IsNaN(v) || float.IsInfinity(v) ? ChaseCutOnVoice : v - into;
            }
            if (swim) ChasePoolDur = Mathf.Max(1f, dur);
            if (!chaseBegun || float.IsNaN(chaseVoice0) || SnapsOnVoice == null || SnapsOnVoice.Length == 0) return;
            float pool = Mathf.Max(1f, ChasePoolDur);
            SnapInPool = SnapsOnVoice[0] - chaseVoice0;
            int n = Mathf.Max(1, SnapsOnVoice.Length - 1);
            if (SnapsInCut == null || SnapsInCut.Length != n) SnapsInCut = new float[n];
            for (int k = 0; k < n; k++) SnapsInCut[k] = (k + 1 < SnapsOnVoice.Length ? SnapsOnVoice[k + 1] : SnapsOnVoice[0] + 99f) - chaseVoice0 - pool;
        }

        /// <summary>Release: the next flight starts its chase clock again.</summary>
        void ChaseRelease() { chaseBegun = false; chaseVoice0 = float.NaN; ChasePoolDur = -1f; }

        /// <summary>The crocodile's own jaw clips: the crocodile code's list when it has one (CrocodileView.JawSnapFiles), else today's three.</summary>
        public static string[] CrocJawFiles()
        {
            if (!jawFilesTried)
            {
                jawFilesTried = true;
                try
                {
                    Type t = null;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("OpenZ.CrocodileView"); if (t != null) break; }
                    var f = t?.GetField("JawSnapFiles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null && f.GetValue(null) is string[] list && list.Length > 0) jawFilesAsked = list;
                }
                catch (Exception) { jawFilesAsked = null; }
            }
            return jawFilesAsked ?? JawFilesNow;
        }

        /// <summary>The crocodile's lunge roar (snap = the clack it belongs to) or its growl (snap below 0): never on the far clack, never under a word.</summary>
        AudioSource PlayRoar(int snap, string id, string file, float duck, string shot)
        {
            if (snap == 0 && !RoarOnFarClack)
            {
                Hushed++; OzSoundLog.Add("L16B", id, file, "SKIP no roar on the far clack (it lunges about 33 m away)");
                return null;
            }
            return PlayWordFree(id, file, GainRoar, duck, shot);
        }

        /// <summary>A sound of the chase that is neither a clack nor water: played only where it starts and ends between two words of the
        /// briefing's line (no line on the clock: played as before).</summary>
        AudioSource PlayWordFree(string id, string file, float gain, float duck, string shot)
        {
            float at = VoiceClock(), len = ClipSeconds(file);
            if (!float.IsNaN(at) && len > 0f && UnderWord(at, len, out string word))
            {
                Hushed++; OzSoundLog.Add("L16B", id, file, $"SKIP under a word ('{word}', {at:F2} s on the voice, {len:F2} s long)");
                return null;
            }
            return Play(id, file, gain, duck, shot);
        }

        /// <summary>Would a sound from `at` for `len` seconds on the voice clock lie on a word of the briefing's line?</summary>
        public static bool UnderWord(float at, float len, out string word)
        {
            word = null;
            var line = ZodLibrary.Shared.Line(OwnerLineId);
            if (line == null || line.words == null) return false;
            foreach (var w in line.words)
                if (w != null && at < w.end + WordGap && at + len > w.start - WordGap) { word = w.word; return true; }
            return false;
        }

        /// <summary>The length (s) of a clip of this stage (0 = not found).</summary>
        public static float ClipSeconds(string file)
        {
            if (string.IsNullOrEmpty(file)) return 0f;
            var c = I ? I.Clip(file) : null;
            if (!c) c = Resources.Load<AudioClip>(file.IndexOf('/') >= 0 ? file : SfxRes + file);
            return c ? c.length : 0f;
        }

        /// <summary>One clack: the crocodile's own jaw event, its clips in turn (never the same twice in a row when it has two or more).</summary>
        AudioSource PlayJaw(float gain, float duck, string shot)
        {
            var files = CrocJawFiles();
            jawTurn = (jawTurn + 1) % Mathf.Max(1, files.Length);
            string file = files[jawTurn];
            if (!sfx.ContainsKey(file) || !sfx[file])
            {
                var c = L16AnimalKit.Clip(file);
                sfx[file] = c ? c : Resources.Load<AudioClip>(file.IndexOf('/') >= 0 ? file : SfxRes + file);
            }
            Jaws++;
            return Play(JawEvent, file, gain, duck, shot);
        }
    }
}
