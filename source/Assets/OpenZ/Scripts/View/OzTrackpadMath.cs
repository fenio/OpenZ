using System;

namespace OpenZ
{
    /// <summary>
    /// TRACKPAD SCROLL, the rules without engine types (OzTrackpadScroll.cs applies them to the camera; the counted check outside
    /// the engine runs this very file). Units: the scroll in wheel notches (one notch of a mouse wheel = 1, a trackpad gives
    /// parts of a notch), the pan in screen pixels.
    /// </summary>
    public static class OzTrackpadMath
    {
        /// <summary>The setting: ZOOM (default, 0) = the scroll zooms as it always did; MOVE MAP (1) = the scroll pans the map.</summary>
        public const int Zoom = 0, MoveMap = 1;

        /// <summary>What one frame of scroll does.
        /// ZoomAsBefore: the camera's own wheel code zooms (ZOOM, the default: untouched).
        /// Pan: the map moves (MOVE MAP). ZoomBy: Ctrl (or Cmd on a Mac) held, the scroll zooms by as much as was scrolled.
        /// MenuHasIt: a menu or question is in front, the camera does nothing. None: no scroll this frame.</summary>
        public enum Act { None, ZoomAsBefore, Pan, ZoomBy, MenuHasIt }

        public struct Result
        {
            public Act act;
            /// <summary>Pan: how far the ground is dragged, in notches: x to the right of the screen, y up the screen.</summary>
            public float dragX, dragY;
            /// <summary>ZoomBy: wheel notches, over 0 = closer (as the wheel: rolled away from the player = closer).</summary>
            public float notches;
        }

        /// <summary>A scroll smaller than this (notches) is no scroll.</summary>
        public const float Still = 0.0001f;
        /// <summary>The most notches one frame of Ctrl / Cmd + scroll zooms (the wheel's own limit in the camera).</summary>
        public const float MaxNotchesPerFrame = 3f;

        /// <summary>One frame. sx, sy: the scroll as the input reads it (x to the right, y away from the player / up);
        /// perNotch: how much of it is one wheel notch (1, or 120 where the raw platform range is kept).
        /// The pan moves the ground the way a scroll view moves its content: x along, y the other way (wheel up = the content comes
        /// down = the view goes north). With the system's natural scrolling (a Mac's default) the content, and so the map, follows
        /// the fingers.</summary>
        public static Result Decide(int setting, float sx, float sy, bool ctrl, bool cmd, bool mac, bool menuInFront, float perNotch)
        {
            var r = new Result();
            if (setting != MoveMap) { r.act = Act.ZoomAsBefore; return r; }
            float k = perNotch > 0f ? 1f / perNotch : 1f;
            float x = sx * k, y = sy * k;
            if (Math.Abs(x) <= Still && Math.Abs(y) <= Still) { r.act = Act.None; return r; }
            if (menuInFront) { r.act = Act.MenuHasIt; return r; }
            if (ctrl || (mac && cmd))
            {
                if (Math.Abs(y) <= Still) { r.act = Act.None; return r; }                 // a sideways swipe with the zoom key held: nothing
                r.act = Act.ZoomBy; r.notches = Clamp(y, -MaxNotchesPerFrame, MaxNotchesPerFrame);
                return r;
            }
            r.act = Act.Pan; r.dragX = x; r.dragY = -y;
            return r;
        }

        /// <summary>The height factor of a zoom by notches (under 1 = closer), the same rule as the wheel: (1 - step) ^ notches.</summary>
        public static float ZoomFactor(float notches, float wheelStep) => (float)Math.Pow(1.0 - Clamp(wheelStep, 0.01f, 0.9f), notches);

        /// <summary>The zoom keys (Ctrl or Cmd with + or -). dir: +1 closer (+), -1 back (-), 0 no key held. pressed: a key went down
        /// this frame. One notch on the press; held longer than delay s, perSecond notches a second after it. Returns this frame's
        /// notches.</summary>
        public static float KeyNotches(int dir, bool pressed, ref float heldFor, float dt, float delay, float perSecond)
        {
            if (dir == 0) { heldFor = 0f; return 0f; }
            if (pressed) { heldFor = 0f; return dir; }
            heldFor += Math.Max(0f, dt);
            return heldFor > delay ? dir * perSecond * Math.Max(0f, dt) : 0f;
        }

        /// <summary>The glide: each frame the pan still to go (pixels) moves a part of the way, 1 - e^(-dt / seconds), never faster
        /// than capPerSecond pixels a second; under half a pixel left, the rest goes at once.</summary>
        public static void Glide(ref float pendX, ref float pendY, float dt, float seconds, float capPerSecond, out float takeX, out float takeY)
        {
            dt = Math.Max(0f, dt);
            float f = seconds > 0f ? 1f - (float)Math.Exp(-dt / seconds) : 1f;
            takeX = pendX * f; takeY = pendY * f;
            float m = (float)Math.Sqrt(takeX * takeX + takeY * takeY), cap = capPerSecond * dt;
            if (capPerSecond > 0f && m > cap && m > 0f) { takeX *= cap / m; takeY *= cap / m; }
            pendX -= takeX; pendY -= takeY;
            if (pendX * pendX + pendY * pendY < 0.25f) { takeX += pendX; takeY += pendY; pendX = 0f; pendY = 0f; }
        }

        /// <summary>The pan still to go is never longer than most pixels (a very fast swipe does not run on for long after it).</summary>
        public static void LimitPending(ref float pendX, ref float pendY, float most)
        {
            float m = (float)Math.Sqrt(pendX * pendX + pendY * pendY);
            if (most > 0f && m > most) { pendX *= most / m; pendY *= most / m; }
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
