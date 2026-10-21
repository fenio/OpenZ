using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K407 (one landing voice per shuttle landing, View/OzShuttleLandingVoice.cs): the voice director answers, without saying anything,
    /// whether a robot line or a Rachel line would be taken now, so the landing picks a voice that will really speak; and hands back
    /// whether Rachel's line was taken into her queue. The same tests as SayImpl / AnnounceImpl (settings, the line's cooldown, the end
    /// sequence, Commander Zod speaking); nothing else changes.
    /// </summary>
    public partial class AudioDirector
    {
        /// <summary>Would the robot line id be said now (voices on, its ROBOT CHATTER setting, its cooldown)? Nothing is played.</summary>
        public static bool SayWouldPlay(string id) => I && I.SayWouldPlayImpl(id);
        /// <summary>Would Rachel's line id be taken into her queue now (voices on, her ANNOUNCER setting, its cooldown, not waiting
        /// already)? Nothing is queued.</summary>
        public static bool AnnounceWouldQueue(string id) => I && I.AnnounceWouldQueueImpl(id);
        /// <summary>Rachel's line id (with its line filter) into her queue; true = taken.</summary>
        public static bool AnnounceQueued(string id, string lineFilter) => I && I.AnnounceImpl(id, lineFilter);
        /// <summary>Rachel's shuttle landing line waits in her queue or plays now (another landing then stays silent: one voice for both).</summary>
        public static bool ShuttleLandingLineWaitingOrPlaying => I && I.ShuttleLandingLineWaitingOrPlayingImpl();

        bool ShuttleLandingLineWaitingOrPlayingImpl()
        {
            if (ShuttleRachelLanding) return true;
            foreach (var q in rachelQueue) if (q.d != null && q.d.id == ShuttleLandingLine) return true;
            return false;
        }

        bool SayWouldPlayImpl(string id)
        {
            var d = Def(id);
            if (d == null || !voices || OpenZVictory.MutesVoice(id)) return false;
            if (ExternalVoiceBusy != null && ExternalVoiceBusy()) return false;
            bool robotLine = d.channel == "alan_main" || d.channel == "alan_alert";
            if (robotLine && !RobotChatter.Allows(id)) return false;
            float cooldown = robotLine ? RobotChatter.Cooldown(id, d.cooldown) : d.cooldown;
            return !(cooldown > 0 && lastLine.TryGetValue(id, out var lt) && Time.unscaledTime - lt < cooldown);
        }

        bool AnnounceWouldQueueImpl(string id)
        {
            var d = Def(id);
            if (d == null || !voices || OpenZVictory.MutesVoice(id) || !Chatter.Allows(id)) return false;
            float gapMul = Chatter.Level == 1 ? 2.5f : 1f;
            if (lastLine.TryGetValue(id, out var lt) && Time.unscaledTime - lt < Mathf.Max(4f * gapMul, d.cooldown * gapMul)) return false;
            foreach (var q in rachelQueue) if (q.d == d) return false;
            return true;
        }
    }
}
