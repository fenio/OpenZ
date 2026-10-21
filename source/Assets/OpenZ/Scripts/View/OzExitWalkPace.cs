using System;

namespace OpenZ
{
    /// <summary>
    /// (View/BoardingView.ExitWalk.cs) and the headless trace of the kit use the very same rule.
    ///  * <see cref="OldSpeedMul"/>: the rule before K441 (BoardingView.StepGhost): once its robot fired, was shot at or moved, whatever was
    ///    left of the walk played in catchUp (0.2 s): the jump.
    ///  * <see cref="LastStep"/>: the last stretch, the walk on to the robot: the step that arrives on time, never faster than the chase pace
    ///    (a robot that moved meanwhile is followed; the walk then ends a little later, on the robot).
    ///  * <see cref="FaceBlend"/>: how much of its robot's facing the figure has taken near the end (smooth, 1 at the hand-over).
    ///  * <see cref="LegSec"/>: one leg of the shuttle's walk from the ramp foot to the robot, timed at rule DV132's own pace
    ///    (ZWorld.ExitWalkMps, the last leg at least ZWorld.ExitWalkMinSec), when the view plans that walk again on the ship as drawn;
    ///  * <see cref="DueSpeedMul"/>: the speed factor that keeps a walk inside rule DV132's out moment (evenly faster, never slower, capped).
    /// </summary>
    public static class OzExitWalkPace
    {
        /// <summary>The figure is on its robot (m, flat).</summary>
        public const double ReachM = 0.03;

        /// <summary>Before K441: the walk's speed factor after this frame (acted: its robot fired, was shot at or moved).</summary>
        public static double OldSpeedMul(double speedMul, double remaining, double catchUp, bool acted) =>
            acted && remaining > catchUp ? Math.Max(speedMul, remaining / catchUp) : speedMul;

        /// <summary>The step (m) along the walk on to the robot this frame: what arrives on time (timeLeft s of the walk's own time left),
        /// at most chaseMps x dt; never past the robot.</summary>
        public static double LastStep(double distLeft, double timeLeft, double dt, double chaseMps)
        {
            if (distLeft <= 0 || dt <= 0) return 0;
            double onTime = timeLeft > dt ? distLeft * dt / timeLeft : distLeft;
            return Math.Min(distLeft, Math.Min(onTime, Math.Max(0, chaseMps) * dt));
        }

        /// <summary>The share of its robot's facing (0..1, smoothstep) with `left` seconds of the walk to go, over the last `blendSec`.</summary>
        public static double FaceBlend(double left, double blendSec)
        {
            if (blendSec <= 0) return 1;
            double w = 1 - Math.Min(1, Math.Max(0, left / blendSec));
            return w * w * (3 - 2 * w);
        }

        /// <summary>The walk is over: on the robot, or past its own end by maxOver seconds (a safety: then it is handed over anyway).</summary>
        public static bool Reached(double distLeft, double overTime, double maxOver) => distLeft <= ReachM || overTime >= maxOver;

        /// <summary>K441b: the time of one leg of the walk from the ramp foot to the robot (lenM metres, flat) at mps, at least minSec.</summary>
        public static double LegSec(double lenM, double mps, double minSec) => Math.Max(minSec, Math.Max(0, lenM) / Math.Max(0.1, mps));

        /// <summary>K441b: the walk's speed factor so that `rest` seconds of its own time end `left` seconds from now (rule DV132's out moment
        /// less the lead): unchanged when it makes it (0.02 s of slack) or when no time is left; else rest / left, at most `max`, never below
        /// the factor it has.</summary>
        public static double DueSpeedMul(double rest, double left, double speedMul, double max)
        {
            if (rest <= 0 || left <= 0.02 || rest <= left * speedMul + 0.02) return speedMul;
            double need = Math.Min(max, rest / left);
            return need > speedMul ? need : speedMul;
        }
    }
}
