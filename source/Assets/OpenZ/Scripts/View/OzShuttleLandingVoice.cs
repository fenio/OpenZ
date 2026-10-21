// OpenZ (GPL-3.0) - K407: one landing voice per shuttle landing. Plain numbers only (nothing of the engine), so the same rule is also
// counted outside the editor; OzPlayableShuttle.LandingVoice.cs asks it once per landing.
using System;

namespace OpenZ
{
    /// <summary>Who speaks for one landing: the robot pilot ("Touchdown!" / "Nice landing!" / "Coming in hot!"), Rachel ("Landing one hundred
    /// percent successful." and her other landing lines) or nobody (neither may speak now).</summary>
    public enum OzLandingVoice { None = 0, Robot = 1, Rachel = 2 }

    /// <summary>
    /// The rule (item 407): every landing gets exactly ONE landing voice, never both one after the other. Some landings the robot
    /// pilot speaks, some Rachel:
    /// <list type="bullet">
    /// <item>a seeded coin per landing (the ship's seed and the landing's number: the same landings give the same voices again);</item>
    /// <item>never more than <see cref="MaxRun"/> landings in a row with the same voice while the other one may speak (the coin is
    /// overruled);</item>
    /// <item>Rachel's landing line at most once per <see cref="RachelGapSec"/> game seconds (her landing comment never spams; the robot
    /// line keeps its own sound-table cooldown);</item>
    /// <item>the chosen voice cannot speak (Rachel: the landing is off screen, her ANNOUNCER setting, her line's cooldown; the robot: his
    /// ROBOT CHATTER setting, his line's cooldown): the other one speaks; neither: nobody (counted);</item>
    /// <item>two ships landing close together are ONE landing: within <see cref="SharedGapSec"/> of another landing's voice (any ship)
    /// a landing stays silent (<see cref="Quiet"/>), so the other voice never follows for the second ship.</item>
    /// </list>
    /// The repair-station landing never asks (it says nothing, as before); "Can't land here!" is not a landing voice and never asks.
    /// </summary>
    public sealed class OzShuttleLandingVoice
    {
        /// <summary>The most landings in a row with the same voice (while the other one may speak).</summary>
        public static int MaxRun = 2;
        /// <summary>Rachel's landing line at most once per this many game seconds.</summary>
        public static double RachelGapSec = 15.0;

        /// <summary>Two ships landing close together (own and enemy ships alike) are heard as ONE landing: a landing that comes within this
        /// many real seconds of another landing's voice (any ship) stays silent, so "Touchdown!" and Rachel's line never follow each other
        /// for two landings either.</summary>
        public static double SharedGapSec = 4.0;
        static double sharedVoiceAt = double.NaN;
        /// <summary>Another landing's voice (any ship) was booked less than <see cref="SharedGapSec"/> real seconds ago (a clock that
        /// started again, a new Play, counts as nothing booked).</summary>
        public static bool SharedBusy(double realNow) => !double.IsNaN(sharedVoiceAt) && realNow >= sharedVoiceAt && realNow - sharedVoiceAt < SharedGapSec;
        /// <summary>A landing voice (any ship) was booked now.</summary>
        public static void SharedSpoke(double realNow) { sharedVoiceAt = realNow; }
        public static void SharedReset() { sharedVoiceAt = double.NaN; }

        public readonly int Seed;
        public OzShuttleLandingVoice(int seed) { Seed = seed; }

        /// <summary>Landings asked so far (the next landing's number).</summary>
        public int Ordinal { get; private set; }
        /// <summary>The voice of the last landing that had one, and how many landings in a row it has spoken.</summary>
        public OzLandingVoice Last { get; private set; }
        public int Run { get; private set; }
        /// <summary>Game time of Rachel's last landing line (negative infinity: none yet).</summary>
        public double LastRachelAt { get; private set; } = double.NegativeInfinity;
        /// <summary>Counts: landings the robot spoke, Rachel spoke, nobody spoke; coins overruled by the run rule; Rachel's line refused
        /// after the pick (the robot line said instead).</summary>
        public int Robot, Rachel, Silent, RunCapped, FellBack;
        /// <summary>Why the last pick came out as it did (for the sound log).</summary>
        public string Why { get; private set; } = "";

        OzLandingVoice prevLast; int prevRun; double prevRachelAt = double.NegativeInfinity;

        public bool RachelRested(double now) => now - LastRachelAt >= RachelGapSec;

        /// <summary>The seeded coin of landing n (0 = the robot, 1 = Rachel).</summary>
        public static int Coin(int seed, int n)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)((n + 1) * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (int)((h >> 7) & 1u);
            }
        }

        static OzLandingVoice Other(OzLandingVoice v) => v == OzLandingVoice.Rachel ? OzLandingVoice.Robot : OzLandingVoice.Rachel;

        /// <summary>The one voice of the next landing. now = game seconds; rachelCan / robotCan = may each speak now (settings, screen,
        /// their lines' cooldowns; Rachel's own landing gap is checked here).</summary>
        public OzLandingVoice Choose(double now, bool rachelCan, bool robotCan)
        {
            int n = Ordinal++;
            bool rachelOk = rachelCan && RachelRested(now);
            var want = Coin(Seed, n) == 1 ? OzLandingVoice.Rachel : OzLandingVoice.Robot;
            string why = "coin";
            if (Run >= MaxRun && Last == want) { want = Other(want); RunCapped++; why = "run of " + Run + " " + (Last == OzLandingVoice.Rachel ? "Rachel" : "robot"); }
            if (want == OzLandingVoice.Rachel && !rachelOk)
            {
                why += rachelCan ? ", Rachel rests (" + RachelGapSec.ToString("0") + " s gap)" : ", Rachel may not speak";
                want = robotCan ? OzLandingVoice.Robot : OzLandingVoice.None;
            }
            else if (want == OzLandingVoice.Robot && !robotCan)
            {
                why += ", the robot may not speak";
                want = rachelOk ? OzLandingVoice.Rachel : OzLandingVoice.None;
            }
            Why = why;
            prevLast = Last; prevRun = Run; prevRachelAt = LastRachelAt;
            Commit(want, now);
            return want;
        }

        /// <summary>A proof switch: this landing's voice is v whatever the coin says (counted as a landing like any other).</summary>
        public OzLandingVoice Force(OzLandingVoice v, double now)
        {
            Ordinal++; Why = "proof switch";
            prevLast = Last; prevRun = Run; prevRachelAt = LastRachelAt;
            Commit(v, now);
            return v;
        }

        /// <summary>This landing stays silent (another ship's landing voice was just heard): counted, the run and Rachel's gap unchanged.</summary>
        public OzLandingVoice Quiet(string why)
        {
            Ordinal++; Why = why;
            prevLast = Last; prevRun = Run; prevRachelAt = LastRachelAt;
            Silent++;
            return OzLandingVoice.None;
        }

        /// <summary>Rachel's line of the last pick was refused after all (another ship's landing line took her slot): the robot speaks
        /// instead (robotSpeaks false: nobody), so the landing still has one voice at most.</summary>
        public void RachelRefused(double now, bool robotSpeaks)
        {
            if (Last != OzLandingVoice.Rachel || Rachel <= 0) return;
            Rachel--; FellBack++;
            Last = prevLast; Run = prevRun; LastRachelAt = prevRachelAt;
            Commit(robotSpeaks ? OzLandingVoice.Robot : OzLandingVoice.None, now);
        }

        void Commit(OzLandingVoice v, double now)
        {
            if (v == OzLandingVoice.None) { Silent++; return; }
            if (v == Last) Run++; else { Last = v; Run = 1; }
            if (v == OzLandingVoice.Rachel) { Rachel++; LastRachelAt = now; } else Robot++;
        }
    }
}
