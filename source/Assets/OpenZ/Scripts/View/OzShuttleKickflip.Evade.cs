using System;

namespace OpenZ
{
    /// <summary>
    /// K436, the kickflip as an evasive manoeuvre (the plain part, shared by the game, the counted check and the headless runner; the rules'
    /// side is the rule DV129, Sim/ZWorld.ShuttleKick.cs: the dash, the harder target, the self-flip). With <see cref="EvadeRules"/> on (the
    /// game):
    /// <list type="bullet">
    /// <item>FIRE NEVER STOPS IT: the double click's flip starts in the steady cruise also while the ship is fired at, hit or gliding
    /// sideways from a strafe (the strafe's lean is not a bank), and once it has popped a hit, fire, a strafe or a turn never ease it back:
    /// only the rules' own word does (a new order in the pop closes the rules' window: <see cref="OzKickflipIn.SimAbort"/>), or, for a
    /// flip the rules do not know, a new order in the pop as before. The cruise ending under it still eases it to the nearer level.</item>
    /// <item>THE RULES' WORD ON THE START: the steady test also asks the rules (<see cref="OzKickflipIn.SimCan"/>: the way for the whole
    /// flip with its dash, no flip already on).</item>
    /// <item>NEVER TWO AT ONCE: a double click during a flip (or its way out) is ignored (it used to wait and arm the next flip).</item>
    /// <item>THE SHIP'S OWN FLIP: <see cref="StartExternal"/> starts the same flip (the same curve, arc, trails, sounds) when the rules
    /// started one by themselves; it pops at once (no steady test of the view, no on-screen test: the rules decided).</item>
    /// </list>
    /// Off (<see cref="EvadeRules"/> = false): every rule as in round 2 (the K344 / K390 / K411 / K423 checks run that way).
    /// No engine type, no random draw, no clock of its own, nothing allocated per frame.
    /// </summary>
    public sealed partial class OzKickflipState
    {
        /// <summary>The K436 rules on (the game) or round 2's (the older checks).</summary>
        public static bool EvadeRules = true;
        /// <summary>The longest head start an outside start takes on the curve (s): a rules' flip seen a frame late starts a little in.</summary>
        public static double ExternalLeadMaxSec = 0.15;
        /// <summary>K436 counts: double clicks ignored during a flip, flips started from outside (the rules), of them the ship's own.</summary>
        public int IgnoredInFlip, ExternalStarts, SelfFlips;
        /// <summary>K436: the flip now (or the last one) was the ship's own (evasive).</summary>
        public bool SelfFlip { get; private set; }
        bool extStart;

        /// <summary>K436: the rules started a flip (the evasive self-flip, or a rules' window the view did not start): it pops now, t0 s into
        /// the curve (at most <see cref="ExternalLeadMaxSec"/>), with the arc the room allows; (gx, gy) = the rules' goal now. false = a flip
        /// is already on (never two at once).</summary>
        public bool StartExternal(double t0, double arcBlocked, bool self, int gx, int gy)
        {
            if (Phase == OzKickflipPhase.Flip || Phase == OzKickflipPhase.Settle) return false;
            Phase = OzKickflipPhase.Flip; T = Clamp(double.IsNaN(t0) ? 0 : t0, 0, ExternalLeadMaxSec); Starts++; ExternalStarts++;
            rollBegan = false; catchSaid = false; lastHealth = double.NaN; pending = false; clickNew = false;
            ArcK = double.IsNaN(arcBlocked) ? 1.0 : 1.0 - Clamp(arcBlocked, 0, 1);
            Dir = ((Id + Starts) & 1) == 0 ? 1 : -1;
            ArmX = gx; ArmY = gy; SteadyFor = 0;
            SelfFlip = self; if (self) SelfFlips++;
            extStart = true;
            return true;
        }

        /// <summary>Step's first lines: the outside start's pop events (once).</summary>
        int ExternalEvents()
        {
            if (!extStart) return 0;
            extStart = false;
            return EvStart | EvPop;
        }

        /// <summary>The steady cruise under the K436 rules (see the class summary): null = it may start.</summary>
        public static string WhyNotEvade(in OzKickflipIn i)
        {
            if (!i.Alive) return "not alive";
            if (i.State != 2) return i.State == 1 ? "climbing" : i.State == 3 ? "coming down" : i.State == 4 ? "hovering" : "on the ground";
            if (!i.AtHeight) return "under cruise height";
            if (i.Braking) return "braking";
            if (i.HoverTurn) return "hover turn";
            if (i.SpeedMps < SteadySpeed * i.VmaxMps) return "slow";
            if (Math.Abs(i.AccelMps2) > SteadyAcc * Math.Max(1e-6, i.AccMaxMps2)) return "speeding up or slowing";
            if (Math.Abs(i.TurnRateDeg) > SteadyTurnDeg) return "turning";
            if (Math.Abs(i.BankDeg - i.LeanDeg) > SteadyBankDeg) return "banked";
            if (Math.Abs(i.PitchDeg) > SteadyPitchDeg) return "nose not level";
            if (i.FeetM < SteadyFeetM) return "low";
            if (i.RemainM - i.BrakeM < i.SpeedMps * (OzKickflipCurve.TotalSec + MarginSec)) return "too little way left";
            if (i.SimKnown && !i.SimCan) return "the rules say not now (the way for the dash, or a flip is on)";
            return null;
        }

        public static bool SteadyEvade(in OzKickflipIn i) => WhyNotEvade(i) == null;

        /// <summary>The soft way out before the roll under the K436 rules: the rules' word for a flip they know, else a new order.</summary>
        static bool SoftEvade(in OzKickflipIn i, bool newOrder) => i.SimKnown ? i.SimAbort : newOrder;
    }
}
