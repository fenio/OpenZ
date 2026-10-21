// OpenZ (GPL-3.0) - Level 16, the LOSE ending: the sounds of the bigger robot's tossed can on the grass (version 1.1 item 229).
// The toss (OzL16LoungerSet.Toss.cs) played ONE quiet event at each contact of the can's path (the antics' can clatter, 2-20 m, -12 dB
// on the Foley bus): its first hit went out at about a fifth of the level of the drink just before it and was not heard over the scene.
// Now each contact of the data file's "tosses" row may name its own event ("hitEv", one per "hitT"; an empty or missing name, or an
// event the sound table does not have, falls back to the row's "ev"):
//   the FIRST ground hit: end_can_toss_land (the can clatter's two single-hit takes, a touch lower, at the level of the drink);
//   the bounce landing and the roll on its side: end_can_toss_roll (the two double-hit takes, softer);
// both 3D at the can, heard in the ending's shot (not dropped off screen), with their own cooldown slot (emitter -1621).
// Each contact is written into the sound log as before ("end_can_toss_hit", the event, "sent, started" when the sound table really
// started it); TossHeard counts the started ones of this ending's toss.
// Play check: return OpenZ.OzL16LoungerSet.TossSoundStatus();
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16LoungerSet
    {
        /// <summary>Play check: the contact sounds of this ending's toss that the sound table really started, and their events.</summary>
        public static int TossHeard;
        public static string TossHeardEvents = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void TossSoundStatics() { TossHeard = 0; TossHeardEvents = ""; }

        /// <summary>The event of contact i of the toss: its own "hitEv" entry when the sound table has it, else the row's "ev".</summary>
        static string TossHitEvent(OzLoungerToss d, int i)
        {
            if (i == 0) { TossHeard = 0; TossHeardEvents = ""; }                                          // a new toss
            string ev = d != null && d.hitEv != null && i < d.hitEv.Length ? d.hitEv[i] : null;
            if (string.IsNullOrEmpty(ev) || !AudioDirector.Has(ev)) ev = d != null ? d.ev : null;
            return ev;
        }

        /// <summary>The toss's contact i was sent: count it when the sound table started it.</summary>
        static void TossHitHeard(string ev, bool started)
        {
            if (started) TossHeard++;
            TossHeardEvents += (TossHeardEvents.Length > 0 ? ", " : "") + ev + (started ? "" : " (not started)");
        }

        /// <summary>Play check: the toss's contacts, the sounds started, their events.</summary>
        public static string TossSoundStatus() => $"can toss sounds: contacts {TossHits}, started {TossHeard} ({TossHeardEvents}); {TossStatus}";
    }
}
