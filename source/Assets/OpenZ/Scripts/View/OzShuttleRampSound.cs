using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Called by ShuttleRearDoor on the rules' counters, in the same frames and the same order as before (open + hydraulic together, the close
    /// timed to the shut, the latch at the shut):
    ///  * the opening's door clip (its hiss, then the clunk 1.52 s in) starts <see cref="DoorOpenSkip"/> into the clip at pitch
    ///    <see cref="DoorOpenPitch"/> (x the event's own), so its clunk lands as the ramp does (a 30 ms fade-in: no click);
    ///  * the ramp's hydraulic (a 2.2 s clip) fades out over <see cref="HydraulicFade"/> once the ramp stops: fully down, fully shut, or turned
    ///    round (a reversal starts its own); before, it ran on 0.7 - 0.8 s past the ramp's stop, now it would be 1.1 s.
    /// The door-close clip keeps its timing (ShuttleRearDoor: its clunk on the shut, from ShuttleRampCloseSec). View only.
    /// </summary>
    public static class OzShuttleRampSound
    {
        /// <summary>The door-open clip's clunk (s into the clip, measured), the pitch of the quicker opening, the hydraulic's fade (s).</summary>
        public const float DoorOpenClunk = 1.52f, DoorOpenPitch = 1.08f, HydraulicFade = 0.25f, FadeIn = 0.03f;
        /// <summary>Where the door-open clip starts (clip seconds) so its clunk is the ramp landing: 0.33 s at 1.1 s and pitch 1.08.</summary>
        public static float DoorOpenSkip => Mathf.Max(0f, DoorOpenClunk - (float)ZWorld.ShuttleRampOpenSec * DoorOpenPitch);
        /// <summary>Proof: door opens played part-way in, hydraulics started, hydraulics faded with the ramp's stop (opening / closing), and the last
        /// opening's planned clunk (s after its start, unscaled pitch variation left out).</summary>
        public static int DoorOpens, Hydraulics, HydraulicStopsOpen, HydraulicStopsShut;
        public static float LastClunkAfter;

        sealed class DoorState { public AudioDirector.RampVoice hyd; public bool opening; public bool live; }
        static readonly Dictionary<int, DoorState> doors = new Dictionary<int, DoorState>();

        static DoorState Of(ShuttleRearDoor d)
        {
            int id = d.GetInstanceID();
            if (!doors.TryGetValue(id, out var s)) { s = new DoorState(); doors[id] = s; if (doors.Count > 32) doors.Clear(); }
            return s;
        }

        /// <summary>ShuttleRearDoor, an opening began: the door's hiss with its clunk at the landing.</summary>
        public static void DoorOpen(ShuttleRearDoor d, Vector3 at, int emitter)
        {
            const string id = "shuttle_door_open";
            if (!AudioDirector.Has(id)) { OzSoundLog.Add("DOOR", id, null, "DROP no event", 1f, at); return; }
            float skip = DoorOpenSkip;
            AudioDirector.FireFrom(id, at, emitter, 1f, DoorOpenPitch, skip, skip > 0f ? FadeIn : 0f);
            LastClunkAfter = (DoorOpenClunk - skip) / DoorOpenPitch; DoorOpens++;
        }

        /// <summary>ShuttleRearDoor, an opening or a closing began: the ramp's hydraulic (the one still sounding from a swing turned round
        /// fades out first).</summary>
        public static void Hydraulic(ShuttleRearDoor d, Vector3 at, int emitter, float pitch, bool opening)
        {
            const string id = "shuttle_ramp_hydraulic";
            var s = Of(d);
            if (s.live) AudioDirector.FadeOut(s.hyd, HydraulicFade);
            if (!AudioDirector.Has(id)) { OzSoundLog.Add("DOOR", id, null, "DROP no event", 1f, at); s.live = false; return; }
            s.hyd = AudioDirector.FireFrom(id, at, emitter, 1f, pitch, 0f, 0f); s.live = s.hyd.Valid; s.opening = opening; Hydraulics++;
        }

        /// <summary>ShuttleRearDoor, every frame: the hydraulic fades once the ramp has stopped (fully down or fully shut); the voice fades move on.</summary>
        public static void Tick(ShuttleRearDoor d, ZShuttleRamp phase, Vector3 at)
        {
            var s = Of(d);
            if (s.live && ((s.opening && phase == ZShuttleRamp.Open) || (!s.opening && phase == ZShuttleRamp.Shut)))
            {
                AudioDirector.FadeOut(s.hyd, HydraulicFade); s.live = false;
                if (s.opening) HydraulicStopsOpen++; else HydraulicStopsShut++;
                OzSoundLog.Add("DOOR", "shuttle_ramp_hydraulic_fade", null, s.opening ? "the hydraulic fades: the ramp is down" : "the hydraulic fades: the ramp is shut", 0f, at);
            }
            AudioDirector.StepRampFades();
        }
    }
}
