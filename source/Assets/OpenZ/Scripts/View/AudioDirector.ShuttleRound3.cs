using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Rule DV118, the shuttle's third round (K256e), in the voice director:
    ///  * M13: Alan's shuttle lines (vo_shuttle_*) and EVERY Rachel line never overlap (OzShuttleVoiceGate holds the rule in plain numbers):
    ///    her lines wait while a shuttle line plays + 0.3 s (at most 3.5 s); a shuttle line asked while she speaks is held and said after
    ///    her (+ 0.3 s, at most 3 s, else dropped), and a shuttle line is never ducked under the robots' voice duck.
    ///  * M8
    ///    Those two lines stay silent for a shuttle; every other vehicle keeps them.
    ///  * M15: a sound for every hit on the shuttle's hull: a laser or electro bolt (shuttle_hull_hit_energy), a flame jet
    ///    (shuttle_hull_hit_flame), at most one every <see cref="HullHitGap"/> s per ship.
    /// Hooks: AudioDirector.cs (SayImpl, UpdateChannels, Apply, the frame tick, the repair events, OnHitscan). Nothing changes for any line
    /// while no shuttle line plays or is held.
    /// </summary>
    public partial class AudioDirector
    {
        public static readonly OzShuttleVoiceGate ShuttleGate = new OzShuttleVoiceGate();
        /// <summary>Checks: shuttle lines said after Rachel, dropped while held; repair lines kept silent for the shuttle; hull hits heard.</summary>
        public static int ShuttleR3Said, ShuttleR3Dropped, ShuttleRepairMuted, HullHitsEnergy, HullHitsFlame;
        public static float HullHitGap = 0.25f;
        readonly System.Collections.Generic.Dictionary<int, float> hullHitLast = new System.Collections.Generic.Dictionary<int, float>();

        bool AlanShuttleLinePlaying =>
            (alanMain != null && alanMain.busy && alanMain.src && alanMain.src.isPlaying && alanMain.def != null && OzShuttleVoiceGate.IsShuttleLine(alanMain.def.id))
            || (alanAlert != null && alanAlert.busy && alanAlert.src && alanAlert.src.isPlaying && alanAlert.def != null && OzShuttleVoiceGate.IsShuttleLine(alanAlert.def.id));
        bool RachelPlayingNow => rachel != null && rachel.busy && rachel.src && rachel.src.isPlaying;

        /// <summary>SayImpl hook: a shuttle line asked while Rachel speaks (or just finished) is held (true = do not play it now).</summary>
        bool ShuttleLineWaitsForRachel(string id, string filter)
        {
            if (shuttleR3Releasing || !OzShuttleVoiceGate.IsShuttleLine(id)) return false;
            float now = Time.unscaledTime;
            ShuttleGate.Tick(now, AlanShuttleLinePlaying, RachelPlayingNow);
            if (!ShuttleGate.ShuttleMustWait(now)) return false;
            ShuttleGate.Hold(id, filter, now);
            OzSoundLog.Add("dir", id, null, "held (Rachel speaks: said after her)");
            return true;
        }
        bool shuttleR3Releasing;

        /// <summary>UpdateChannels hook: Rachel's next line waits while a shuttle line plays (true = wait).</summary>
        bool RachelWaitsForShuttleLine(float now)
        {
            ShuttleGate.Tick(now, AlanShuttleLinePlaying, RachelPlayingNow);
            return !ShuttleGate.RachelMayStart(now);
        }

        /// <summary>Once a frame: the held shuttle line, once Rachel has finished.</summary>
        void ShuttleRound3Tick()
        {
            if (rachel == null || alanMain == null || alanAlert == null) return;
            float now = Time.unscaledTime;
            ShuttleGate.Tick(now, AlanShuttleLinePlaying, RachelPlayingNow);
            int r = ShuttleGate.Poll(now, out string id, out string filter);
            if (r == 2) { ShuttleR3Dropped++; OzSoundLog.Add("dir", id, null, "dropped (held too long behind Rachel)"); return; }
            if (r != 1) return;
            bool keep = shuttleReleasing; shuttleR3Releasing = true; shuttleReleasing = true;   // its own cooldown and the robot line gap were met when it was asked
            try { SayImpl(id, filter); } finally { shuttleR3Releasing = false; shuttleReleasing = keep; }
            ShuttleR3Said++;
        }

        /// <summary>Apply hook: a shuttle line is never ducked under the robots' voice duck (the duck Rachel's lines put on the robots).</summary>
        float ShuttleLineUnduck(Voice v)
        {
            if (v == null || v.def == null || v.combatMix || !OzShuttleVoiceGate.IsShuttleLine(v.def.id) || v.def.bus != "Voice/Robots") return 1f;
            return duckRobots < 0f ? Db(-duckRobots) : 1f;
        }

        /// <summary>The repair events: "starting repair" / "vehicle repaired" are not said for a shuttle.</summary>
        static bool ShuttleRepairLine(ZObject a, string id)
        {
            if (a == null || !a.IsShuttle) return false;
            ShuttleRepairMuted++;
            OzSoundLog.Add("dir", id, null, "silent for the shuttle (on the repair station)");
            return true;
        }

        /// <summary>OnHitscan hook: a laser / electro bolt or a flame jet that hits the shuttle is heard on its hull.</summary>
        void ShuttleHullHit(string type, ZObject t, bool hit)
        {
            if (!hit || t == null || !t.IsShuttle || t.Destroyed) return;
            bool flame = type == "pyro", energy = type == "laser" || type == "electrobot";
            if (!flame && !energy) return;
            float now = Time.unscaledTime;
            if (hullHitLast.TryGetValue(t.Id, out var last) && now - last < HullHitGap) return;
            hullHitLast[t.Id] = now;
            var sv = OzPlayableShuttle.Of(t);
            var at = sv ? sv.transform.position + sv.transform.up * 2.6f : PosOf(t) + Vector3.up * 1.8f;
            string id = flame ? "shuttle_hull_hit_flame" : "shuttle_hull_hit_energy";
            if (!Has(id)) return;
            Fire(id, at, t.Id, 1f);
            if (flame) HullHitsFlame++; else HullHitsEnergy++;
        }
    }
}
