namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's death at its drawn altitude (rule DV118, view only): the plain part, shared by the game
    /// (View/OzShuttleBreakup.Altitude.cs, View/OzShuttleCrewLanding.cs, View/OzPlayableShuttle.DeathHold.cs) and the kit's twin. No engine
    /// type, no clock and no random draw of its own (the caller hands in every number).
    /// <list type="bullet">
    /// <item>THE BREAK (<see cref="How"/>): destroyed with its pads on the ground = "ground"; shot down in the air, the ship breaks on the
    /// drawn kill moment where it is drawn, by what killed it: rifle-class fire (a known killing hit up to the small-arms power: rifles,
    /// machine guns, lasers, the sniper, flame, tesla) = "crack" (the hull cracks open: a low throw, the pieces tumbling and trailing
    /// smoke as they fall), a heavy shell = "split" (a second blast throws the panels wide), everything else = "burst". The whole-ship
    /// "drop" (the hull falling whole and breaking on the ground) only when its switch is on.</item>
    /// <item>THE CRACK'S THROW (<see cref="Throw"/>): the burst's out-throw x <see cref="CrackOutK"/>, up-throw x <see cref="CrackUpK"/>,
    /// spin x <see cref="CrackSpinK"/>: the pieces leave the hull slowly and fall from where it was drawn at once.</item>
    /// <item>A LANDING (<see cref="Lands"/>): a falling piece or body has landed when it touches the ground or the water, when it touches
    /// anything within a short height of the ground or water under it, or when it comes down ON something that stands or lies still
    /// (a roof, a rock, a piece that has landed, a body that lies): the contact's normal points up. Touching another piece or body that is
    /// still flying, high up, is no landing (no landing crash, scorch, dust, camera kick or landing sound up in the air).</item>
    /// <item>DRAWN UNTIL IT BREAKS (<see cref="KeepDrawn"/>): the ship that is dying and not yet broken (the drawn kill moment still to
    /// come) or falling whole after a failed break-up is never hidden by its unit view before the break-up takes it.</item>
    /// </list>
    /// </summary>
    public static class OzShuttleDeathPlan
    {
        public const string Ground = "ground", Burst = "burst", Split = "split", Crack = "crack", Drop = "drop";

        /// <summary>A kill with the pads more than this over the ground (m) breaks in the air.</summary>
        public const double AirFeetM = 0.5;
        public static bool InAir(double feet) => feet > AirFeetM;

        /// <summary>The break of a kill. feet = the pads over the ground at the drawn kill moment (m); hitKnown / power = the killing hit
        /// drawn in the last 1.5 s and how hard it was (none known: power 1); smallArms / heavy = the power limits; dropWhole / fallMinFeet
        /// = the whole-ship drop's switch and its least pad height.</summary>
        public static string How(double feet, bool hitKnown, double power, double smallArms, double heavy, bool dropWhole, double fallMinFeet)
        {
            if (!InAir(feet)) return Ground;
            if (hitKnown && power <= smallArms) return dropWhole && feet >= fallMinFeet ? Drop : Crack;
            return power >= heavy ? Split : Burst;
        }

        /// <summary>The crack's throw against the burst's: out, up, spin.</summary>
        public static double CrackOutK = 0.6, CrackUpK = 0.45, CrackSpinK = 1.6;
        /// <summary>The crack's dark air cloud against the burst's (its seconds).</summary>
        public static double CrackCloudK = 0.6;

        /// <summary>One piece's launch numbers (out m/s, up m/s, spin rad/s) for this break.</summary>
        public static void Throw(string how, ref double vo, ref double vu, ref double sp)
        {
            if (how != Crack) return;
            vo *= CrackOutK; vu *= CrackUpK; sp *= CrackSpinK;
        }

        /// <summary>What a falling piece or body touched: the ground (the terrain, a crater), the water, still scenery (a roof, a rock, a
        /// trunk: no body of its own), something lying still (a piece that has landed or rests, a body that has landed, a body that
        /// sleeps), something still flying.</summary>
        public enum Touch { Ground, Water, Scenery, Resting, Flying }

        /// <summary>A contact this close over the ground or the water under it (m) is a landing whatever it touched: a piece, a body.</summary>
        public const double PieceNearM = 1.2, BodyNearM = 0.6;
        /// <summary>Coming down ON something: the contact's normal (pointing at the faller) at least this much up.</summary>
        public const double TopNormal = 0.45;

        /// <summary>Is this contact the faller's landing? normalUp = the contact normal's up part (toward the faller), over = the contact's
        /// height over the ground or water under it (m), near = PieceNearM or BodyNearM.</summary>
        public static bool Lands(Touch other, double normalUp, double over, double near)
        {
            if (other == Touch.Ground || other == Touch.Water) return true;
            if (over <= near) return true;
            if (other == Touch.Flying) return false;
            return normalUp >= TopNormal;
        }

        /// <summary>The dying ship's drawn hull stays this long at most (s) waiting for its break-up (its own whole-fall fallback starts
        /// at 2.5 s and keeps it drawn itself).</summary>
        public const double KeepDrawnMaxSec = 3.0;
        /// <summary>The unit view keeps the dead ship drawn: not in pieces yet, and dying (the drawn kill moment to come) or falling whole.</summary>
        public static bool KeepDrawn(bool brokenUp, bool dying, bool crashing, double dyingSec) => !brokenUp && ((dying && dyingSec <= KeepDrawnMaxSec) || crashing);
    }
}
