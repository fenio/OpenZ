namespace OpenZ
{
    /// <summary>
    /// K327 (items 327 + 328): the rule for the repair key (R by default) while a fort's or a factory's build menu is open. Plain C#
    /// (no engine types), so the whole conflict matrix can be counted outside the editor; OzRallyKey (View/OzRallyKey.cs) reads the
    /// frame for it and does what the route says, PlayerInput asks it before the key's own reading.
    ///  - No build menu open and no rally tool armed: the key does exactly what it did before (the damaged vehicles of the selection,
    ///    the shuttle too, go to repair and back; else NEXT ROBOT SQUAD when it shares the key): route Today.
    ///  - A build menu open (it was open at the end of the last frame too): the key is the menu's SET RALLY button. Route SetRally
    ///    runs the button's own click (it arms the rally tool and closes the menu; on an armed tool it cancels). No repair order and
    ///    no next robot squad come from that press, whatever is selected (the menu keeps the selection, so units can be selected).
    ///  - The rally tool armed (the menu closed when it was armed): the key again cancels the tool, as Esc does (route Cancel); never
    ///    a repair order, also with damaged vehicles selected.
    ///  - Esc or a left press on the map in the same frame (both close the menu / cancel the tool), or the menu or the tool opening /
    ///    closing earlier in this frame (a HUD click, the factory list key, the hide-HUD key): that action wins and the key adds
    ///    nothing (route Swallow). One press never does two things.
    ///  - A key sent by the touch layer or the gamepad (the gamepad's Y taps NEXT ROBOT when it has no repair to send): unchanged,
    ///    route Today. After the match is over: unchanged, route Today.
    /// Held down: one press, one action (the input package reports a press once; the system's key repeat adds no new press).
    /// Ctrl / Alt + the key are other actions (ALL ROBOTS ...): PlayerInput never asks for them.
    /// </summary>
    public static class OzRallyKeyCore
    {
        public enum Route { None, Today, SetRally, Cancel, Swallow }

        /// <summary>What the frame holds when PlayerInput reads its keys.</summary>
        public struct Frame
        {
            public bool keyDown;                  // the repair key went down this frame
            public bool tapped;                   // ... sent by the touch layer / the gamepad
            public bool gameOver;                 // the match is over
            public bool menuOpen, menuWasOpen;    // a build menu is open now / was open at the end of the last frame
            public bool armed, armedWas;          // the rally tool is armed now / was armed at the end of the last frame
            public bool esc, leftPress;           // Esc / a left mouse press this frame
        }

        public static Route Decide(Frame f)
        {
            if (!f.keyDown) return Route.None;
            if (f.tapped || f.gameOver) return Route.Today;
            bool owned = f.menuOpen || f.menuWasOpen || f.armed || f.armedWas;
            if (!owned) return Route.Today;
            if (f.esc || f.leftPress) return Route.Swallow;
            if (f.menuOpen != f.menuWasOpen || f.armed != f.armedWas) return Route.Swallow;
            return f.menuOpen ? Route.SetRally : Route.Cancel;
        }

        /// <summary>The press belongs to the build menu / its rally tool: no repair order and no next robot squad for it.</summary>
        public static bool Taken(Route r) => r == Route.SetRally || r == Route.Cancel || r == Route.Swallow;

        /// <summary>Checks: presses of the key by route since the game started (or the last ResetCounts).</summary>
        public static int SetRallies, Cancels, Swallowed, Todays;

        public static void Count(Route r)
        {
            switch (r)
            {
                case Route.SetRally: SetRallies++; break;
                case Route.Cancel: Cancels++; break;
                case Route.Swallow: Swallowed++; break;
                case Route.Today: Todays++; break;
            }
        }

        public static void ResetCounts() { SetRallies = Cancels = Swallowed = Todays = 0; }

        public static string Name(Route r)
        {
            switch (r)
            {
                case Route.Today: return "as before (repair / next robot squad)";
                case Route.SetRally: return "SET RALLY (the build menu's button)";
                case Route.Cancel: return "rally tool cancelled";
                case Route.Swallow: return "nothing (another action of this frame closed the menu)";
                default: return "no press";
            }
        }
    }
}
