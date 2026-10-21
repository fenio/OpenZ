using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV123 "the Tough stands and fires" (Zod has no such rule; it is DV120's steady rule, Sim/ZObject.SniperSteady.cs,
    /// given to the Tough on foot). Toughs already never step aside from a shell or a grenade (b49, ZObject.Calm.cs CalmStandsFirm).
    /// <para>What still made them walk about in a fight with nothing to dodge: a Tough fires rockets and the grunts it fires at step
    /// aside from rockets (Zod's DodgeMissile); the chased grunt left the agro leash and Zod's agro walk turned the Tough back to where
    /// the chase began (zobject.cpp:2233-2328), then forward again; after each kill the next enemy just outside the range started a
    /// small step (Zod's agro: range + 40 px), and every step stopped at the very edge of the range, where the next dodge started the
    /// next one; a walking robot never fires (zobject.cpp:1615). Each walk let the drawn brace go and brace again.</para>
    /// <para>DV123, for a Tough on foot: (1)-(4) of DV120 (ZObject.SniperSteady.cs), with two differences: it stops for ANY enemy unit
    /// it can hit (it is the anti-vehicle robot), one tile (ZWorld.SniperStandBand) inside its range; its squad "fires" while one of
    /// them holds a target or fired in the last <see cref="ZWorld.ToughHoldSecs"/> s (its rocket cycle is 1.442 s, so DV120's 1 s would
    /// lapse between two rockets). So: no walk past an enemy in range, no walk back on a dodging target, the spot kept after a kill
    /// while an enemy stands just outside the range, not pushed about by robots of its team while it fires. The player's own attack order
    /// is done at once and until it is done (DV16); a move, a force move, entering, a fort entry, a grenade crate, a repair and every
    /// step aside of the spacing layer are never held. DV11 (the 0.8 s first-rocket aim, ZObject.ToughPrepare.cs) is not changed: it
    /// comes exactly when the drawn tube has to come back onto a target (after a walk, or 1.6 s with nothing to shoot), and DV123 takes
    /// away most of the small steps that made it come again.</para>
    /// Deterministic: sim state and object order only, no random draw. Switch ZWorld.ToughSteadyDeviation: View/ZGame.cs sets it at
    /// every start (off in 'Zod exact'); off in headless worlds and in the counted checks unless they set it (OzV11Kit.GameSwitches).
    /// Off = the game as before, tick for tick.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV123 applies to this unit: a Tough on foot, the switch on.</summary>
        public bool ToughSteady => ZWorld.ToughSteadyDeviation && Kind == ZKind.Robot && Type == "tough" && !IsFortDefender;

        /// <summary>DV123: the squad-fired hold of a Tough (ZWorld.ToughHoldSecs), DV120's (ZWorld.SniperHoldSecs) for every other unit.</summary>
        static double SteadyHoldSecs(ZObject o) => o.ToughSteady ? ZWorld.ToughHoldSecs : ZWorld.SniperHoldSecs;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV123 switch (see ZObject.ToughSteady.cs). View/ZGame.cs sets it at every start (off in 'Zod exact'); headless default off.</summary>
        public static bool ToughSteadyDeviation = false;
        /// <summary>DV123: "its squad fires": a target held, or a rocket of the squad in the last this many s (the Tough's cycle is 1.442 s).
        /// Chosen with the counted check (K306 kit, tools/band_hold.py: 1.6 / 1.8 / 2.0 / 2.2 / 2.5 s tried): 2.5 s gave the fewest brace cycles
        /// and creep steps with the fights within 10 % of before; it is also about a rocket's flight plus the drawn tube's 1.6 s linger, so a
        /// Tough keeps its spot while its tube is still up.</summary>
        public static double ToughHoldSecs = 2.5;

        /// <summary>Turns the rule off until it is disposed, then puts back what was there. The parity report holds one while it
        /// measures (a Play session leaves the switch on); its own row turns the rule on by itself.</summary>
        public sealed class ToughSteadyHold : IDisposable
        {
            readonly bool was;
            internal ToughSteadyHold() { was = ToughSteadyDeviation; ToughSteadyDeviation = false; }
            public void Dispose() { ToughSteadyDeviation = was; }
        }
        public static ToughSteadyHold ToughSteadyOff() => new ToughSteadyHold();
    }
}
