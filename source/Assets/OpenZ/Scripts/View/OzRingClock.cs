using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K403 (view only): the selection rings' own clock. Every selected unit's green ring pulses softly (+-3.5 %, 3.2 rad/s:
    /// SelectionOverlay); the pulse used to read the real-time clock (Time.unscaledTime) straight. In a fixed-step recording (the clips:
    /// Time.captureFramerate 60, every frame one 1/60 s step of the game while the real frame takes 0.06 .. 0.4 s) the real clock jumps a
    /// different amount every frame, so the pulse was sampled at random phases: the ring grew and shrank by its whole swing from one frame
    /// to the next (about ten times a second while a squad got out of the shuttle, when the frames were slowest) and read as a blinking,
    /// jittering ring. This clock advances once a frame: by the capture step while a fixed-step recording runs, else by the real frame
    /// time, never more than <see cref="StepMaxSec"/> a frame (one slow frame in Play no longer jumps the pulse either). The pulse itself
    /// (its size, speed and look) is unchanged.
    /// </summary>
    public static class OzRingClock
    {
        /// <summary>The pulse: its swing (share of the radius) and its rate (rad/s), as the rings always had.</summary>
        public const float PulseAmp = 0.035f, PulseRate = 3.2f;
        /// <summary>The most the clock moves in one frame (s).</summary>
        public const float StepMaxSec = 0.1f;
        static int frame = -1; static float t, dt;
        /// <summary>Checks: frames counted, frames that ran on the capture step, the largest step taken (s).</summary>
        public static int Frames, CaptureFrames; public static float StepMax;

        static void Tick()
        {
            int f = Time.frameCount;
            if (f == frame) return;
            frame = f;
            bool cap = Time.captureDeltaTime > 0f;
            float step = cap ? Time.captureDeltaTime : Time.unscaledDeltaTime;
            if (float.IsNaN(step) || step < 0f) step = 0f;
            dt = Mathf.Min(step, StepMaxSec);
            t += dt;
            const float period = 2f * Mathf.PI / PulseRate;
            if (t > 1000f * period) t -= 1000f * period;                                                 // whole pulse periods: no jump, the float stays fine
            Frames++; if (cap) CaptureFrames++; if (dt > StepMax) StepMax = dt;
        }

        /// <summary>The clock now (s).</summary>
        public static float Now { get { Tick(); return t; } }
        /// <summary>This frame's step (s): the capture step in a fixed-step recording, else the real frame time (at most StepMaxSec).</summary>
        public static float Dt { get { Tick(); return dt; } }
        /// <summary>The rings' pulse now: 1 +- PulseAmp.</summary>
        public static float Pulse => 1f + PulseAmp * Mathf.Sin(Now * PulseRate);
    }
}
