using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K407
    /// every landing has exactly ONE landing voice.
    ///  * The robot pilot: "Touchdown!" / "Nice landing!" ("Coming in hot!" when it was hit in the last 5 s; a crash landing: "Coming in
    ///    hot!" about 1 s before it hits, never the landing line), as before.
    ///  * OR Rachel: "Landing one hundred percent successful." / "Autopilot landing one hundred percent successful." (a crash landing:
    ///    "Landing eighty-five percent successful." / "Landing sixty percent successful."), through her queue as before; then the robot
    ///    says nothing for that landing (and a crash she comments has no "Coming in hot!").
    ///  * Which one: OzShuttleLandingVoice (a seeded coin per landing, never 3 landings in a row with the same voice while the other may
    ///    speak, Rachel at most once per 15 game seconds; Rachel only when the landing is on screen). A crash landing picks when its
    ///    "Coming in hot!" is due (before the contact), the touchdown uses that pick.
    ///  * Two ships landing close together (own or enemy ships) are heard as ONE landing: a landing within 4 real seconds of another
    ///    landing's voice (any ship), or while Rachel's landing line still waits or plays, stays silent (never "Touchdown!" for one ship
    ///    and Rachel's line for the other right after it).
    ///  * Rachel's line refused after all when its turn comes (another ship's landing line took her slot): the robot's landing line instead
    ///    (a crash: nobody; a late "Coming in hot!" after the hit would be wrong), so never two voices.
    ///  * Unchanged: the repair-station landing says nothing (item 276.13: Landed returns before this); "Can't land here!" (K395) is not a
    ///    landing voice; the two voices never overlap (AudioDirector.ShuttleVoice.cs / .ShuttleRound3.cs); the proof switch
    ///    <see cref="ProofRachelEvery"/> makes every landing Rachel's (the robot then says nothing for it).
    /// View only: nothing here changes the rules.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        public const string RachelLandingId = "ann_shuttle_landing";
        /// <summary>How long a crash's pick (made when "Coming in hot!" is due) is kept for its touchdown (game-clock seconds).</summary>
        public static float LandingPickKeepSec = 4f;
        /// <summary>Checks (every ship): landings with the robot's voice, Rachel's, nobody's; Rachel's lines refused after the pick; the
        /// robot lines asked on a landing Rachel had (0), Rachel lines asked on a landing the robot had (0).</summary>
        public static int LandingRobot, LandingRachel, LandingSilent, LandingRachelRefused, LandingDoubles;
        /// <summary>The last picks of every ship, for the Play check.</summary>
        public static readonly List<string> LandingVoiceLog = new List<string>();
        /// <summary>The last landing's voice of this ship (None before the first) and its number.</summary>
        public OzLandingVoice LastLandingVoice { get; private set; }
        public int LandingVoiceNumber => landingVoice != null ? landingVoice.Ordinal : 0;
        public OzShuttleLandingVoice LandingVoicePicker => landingVoice;
        /// <summary>Game time of this ship's last Rachel landing line (-1000: none yet).</summary>
        public double LastRachelLandingAt => lastRachel;

        OzShuttleLandingVoice landingVoice;
        bool landingPicked, crashHotSpoken; OzLandingVoice landingPick; float landingPickAt;

        double LandingClock => game != null && game.World != null ? game.World.Time : Time.timeSinceLevelLoad;

        bool LandingOnScreen()
        {
            var cam = Camera.main; if (!cam) return false;
            var vp = cam.WorldToViewportPoint(transform.position);
            return vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f;
        }

        /// <summary>The one voice of the landing about to happen (picked once per landing).</summary>
        OzLandingVoice PickLandingVoice(string robotId)
        {
            if (landingVoice == null) landingVoice = new OzShuttleLandingVoice((Obj != null ? Obj.Id : 0) * 31 + 407);
            double now = LandingClock;
            OzLandingVoice v;
            if (ProofRachelEvery) v = landingVoice.Force(OzLandingVoice.Rachel, now);
            else if (OzShuttleLandingVoice.SharedBusy(Time.unscaledTime) || AudioDirector.ShuttleLandingLineWaitingOrPlaying)
                v = landingVoice.Quiet("another landing's voice was just heard: one voice for both");   // two ships landing together: ONE voice, never "Touchdown!" then Rachel
            else
            {
                bool rachelCan = LandingOnScreen() && AudioDirector.AnnounceWouldQueue(RachelLandingId);
                bool robotCan = AudioDirector.SayWouldPlay(robotId);
                v = landingVoice.Choose(now, rachelCan, robotCan);
            }
            if (v != OzLandingVoice.None) OzShuttleLandingVoice.SharedSpoke(Time.unscaledTime);
            if (v == OzLandingVoice.Robot) LandingRobot++; else if (v == OzLandingVoice.Rachel) LandingRachel++; else LandingSilent++;
            LastLandingVoice = v;
            string who = v == OzLandingVoice.Robot ? "the robot" : v == OzLandingVoice.Rachel ? "Rachel" : "nobody";
            LandingVoiceLog.Add($"ship {(Obj != null ? Obj.Id : -1)}, landing {landingVoice.Ordinal}, {who}, {landingVoice.Why}, {now:F1} s");
            if (LandingVoiceLog.Count > 200) LandingVoiceLog.RemoveAt(0);
            OzSoundLog.Add("dir", v == OzLandingVoice.Rachel ? RachelLandingId : robotId, null, $"landing voice: {who} ({landingVoice.Why}; landing {landingVoice.Ordinal}, run {landingVoice.Run})");
            return v;
        }

        /// <summary>A crash's "Coming in hot!" is due (CrashDescent): the landing's voice is picked now; true = the robot speaks it.</summary>
        bool CrashHotIsTheVoice()
        {
            landingPick = PickLandingVoice("vo_shuttle_hot"); landingPicked = true; landingPickAt = (float)LandingClock;
            crashHotSpoken = landingPick == OzLandingVoice.Robot;
            return crashHotSpoken;
        }

        /// <summary>Landed (not on a repair station): exactly one landing voice.</summary>
        void LandingOneVoice(ZWorld w, ZShuttleNote n, bool rough)
        {
            string robotId = rough || n.underFire ? "vo_shuttle_hot" : "vo_shuttle_landed";
            bool had = landingPicked && (float)LandingClock - landingPickAt <= LandingPickKeepSec;
            bool hotBefore = rough && hotSaid && crashHotSpoken;                                     // the crash's "Coming in hot!" was said before the contact
            landingPicked = false; hotSaid = false; crashHotSpoken = false;
            OzLandingVoice v = had ? landingPick : hotBefore ? OzLandingVoice.Robot : PickLandingVoice(robotId);
            if (v == OzLandingVoice.Robot)
            {
                if (hotBefore) return;                                                               // the crash's call was its voice: nothing more
                if (rough) CrashHotCalls++;
                Voice(robotId); LastAlanId = robotId; LastAlanAt = Time.unscaledTime;
                return;
            }
            if (v != OzLandingVoice.Rachel) return;
            if (hotBefore) LandingDoubles++;                                                         // never: a Rachel pick says no "Coming in hot!"
            bool second = (Hash(Obj.Id + 57, Obj.ShuttleLandings) & 1) == 1;
            string line = rough ? (second ? "=Landing sixty percent successful." : "=Landing eighty-five percent successful.")
                                : (second ? "=Autopilot landing one hundred percent successful." : "=Landing one hundred percent successful.");
            lastRachel = w.Time;
            StartCoroutine(AnnounceOnly(line, rough ? null : robotId));
        }

        /// <summary>Rachel's landing line as before (one frame, then while any voice speaks (at most 3.5 s), then 0.3 s, then her queue);
        /// refused there after all: the robot's line instead (robotId null: nobody).</summary>
        IEnumerator AnnounceOnly(string line, string robotId)
        {
            yield return null;
            float t0 = Time.unscaledTime;
            while (AudioDirector.VoiceBusy && Time.unscaledTime - t0 < 3.5f) yield return null;
            float t1 = Time.unscaledTime; while (Time.unscaledTime - t1 < 0.3f) yield return null;
            if (BrokenUp || Obj == null || Obj.Destroyed) yield break;   // K418: destroyed meanwhile: no landing line after its death
            if (AudioDirector.AnnounceQueued(RachelLandingId, line))
            {
                RachelLines++; LastVoice = RachelLandingId + " " + line; LastRachelAt = Time.unscaledTime;
                yield break;
            }
            LandingRachelRefused++; LandingRachel--;
            if (landingVoice != null) landingVoice.RachelRefused(LandingClock, robotId != null);
            LastLandingVoice = robotId != null ? OzLandingVoice.Robot : OzLandingVoice.None;
            if (robotId != null) LandingRobot++; else LandingSilent++;
            OzSoundLog.Add("dir", RachelLandingId, null, robotId != null ? "landing voice: Rachel's line refused, the robot instead" : "landing voice: Rachel's line refused (a crash: nobody)");
            if (robotId == null) yield break;
            OzShuttleLandingVoice.SharedSpoke(Time.unscaledTime);
            Voice(robotId); LastAlanId = robotId; LastAlanAt = Time.unscaledTime;
        }
    }
}
