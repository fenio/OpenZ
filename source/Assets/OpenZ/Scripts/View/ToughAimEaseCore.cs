using System;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// Tough's bazooka as plain C# without engine types, so the off-engine replay runs this exact code. View only: nothing in the
    /// simulation changes (targets, timing, damage, range, rate). The component <see cref="ToughAimEase"/> feeds it every frame.
    /// <para>Before: WeaponHold laid the tube, and squared the chest, onto the raw aim of this very frame: UnitAim's pick (the sim's
    /// target, else its own 0.25 s check of the first enemy in range, a dying target counting as none) turned by RobotWeaponPose onto its
    /// own pick (the sim's target, else ITS own 0.25 s check). So every kill dropped the tube and the chest onto the body front in one
    /// 0.03-0.10 s after the drop: there and back, and so did a short step, where the body turned at its walking rate.</para>
    /// <para>Now, per Tough, from that raw aim:</para>
    /// <list type="number">
    /// <item>hold: while the raw aim has no target (a kill, the gap until the next target check, a short step in the fight), the last aim
    /// on a target is kept for <see cref="HoldSecs"/> (the sim's carry window, after which the tube is lowered to the carry anyway), at
    /// most <see cref="TwistMax"/> off the body front (UnitAim's own torso limit); not while he walks off on an order to go elsewhere;</item>
    /// <item>confirm: a jump of the aim (more than <see cref="SwitchDeg"/> in one frame) is followed once the new aim has held
    /// <see cref="SwitchHold"/> s when it is on the sim's own target, <see cref="PredictHold"/> s while it is only the views' guess of
    /// the next one (the sim may pick another); a jump back in that time is never followed; at once while a rocket waits for the tube;</item>
    /// <item>ease: the drawn heading and elevation follow critically damped (<see cref="SmoothTime"/>, at most <see cref="MaxSpeed"/>
    /// deg/s; <see cref="HurrySmooth"/> / <see cref="HurrySpeed"/> while a rocket waits), never a step out of rest;</item>
    /// <item>fire gate: <see cref="HoldsFire"/> while the drawn tube is more than <see cref="LagTol"/> deg (+ the follow lag of a steadily
    /// moving aim) off a target aim (UnitAim keeps the rocket within its own wait cap, the sim's detonation time unchanged), so a rocket
    /// leaves along the tube.</item>
    /// </list>
    /// Angles in degrees; heading = Unity's yaw (atan2(x, z)), elevation + = up. Deterministic: inputs only, no random draw.
    /// </summary>
    public sealed class ToughAimEaseCore
    {
        // ------------------------------------------------------------------------------------------------ numbers (live-tunable)
        /// <summary>Critically damped follow time (s) of the drawn heading and elevation.</summary>
        public static double SmoothTime = 0.10;
        /// <summary>Fastest turn of the drawn aim (deg/s); UnitView turns a walking robot at 400.</summary>
        public static double MaxSpeed = 360.0;
        /// <summary>While a rocket waits for the tube: SmoothTime x this, MaxSpeed x HurrySpeed (UnitAim's own hurry numbers).</summary>
        public static double HurrySmooth = 0.45, HurrySpeed = 2.5;
        /// <summary>Seconds the last aim on a target is held when the raw aim has none (the sim's carry window, ZWorld.ToughCarrySeconds).</summary>
        public static double HoldSecs = ZWorld.ToughCarrySeconds;
        /// <summary>The held aim stays within this many degrees of the body front (UnitAim keeps a gun within 75 deg of the chest).</summary>
        public static double TwistMax = 75.0;
        /// <summary>A one-frame jump of the aim onto a target (another target, or a target after the held aim or the carry) larger than this
        /// (deg) and than JumpRatio x the step before it is a switch: followed after SwitchHold / PredictHold s. Going to the held aim or to
        /// the carry is never waited for (no target to be unsure of), and both move only with the body (also its walking turn).</summary>
        public static double SwitchDeg = 6.0, JumpRatio = 3.0;
        /// <summary>Seconds a new aim must hold before the drawn aim turns to it: on the sim's own target / only a guess of the next one
        /// (UnitAim's and the bazooka's 0.25 s checks; the sim's own check runs every 0.5 s and may pick another enemy, so a guess waits a
        /// little longer than that beat: once the sim takes the guessed unit, SwitchHold applies at once).</summary>
        public static double SwitchHold = 0.15, PredictHold = 0.55;
        /// <summary>A rocket waits while the drawn tube is more than this (deg) off its aim, plus the follow lag a steadily moving aim keeps
        /// while a rocket waits (its turn rate x SmoothTime x HurrySmooth: a jeep or the shuttle crossing fast never holds a rocket to UnitAim's cap).</summary>
        public static double LagTol = 2.0;
        /// <summary>Seconds over which the raw aim's steady turn rate is averaged (the fire gate's follow-lag allowance).</summary>
        public static double TrackRateSecs = 0.1;
        /// <summary>No target and nothing held (the carry along the body front) while the body turns faster than BodyTurn deg/s (a walking
        /// turn: UnitView turns it at 400 deg/s): SmoothTime x this and MaxSpeed = CarrySpeed, so the tube stays close to the turning body.</summary>
        public static double CarrySmooth = 0.3, CarrySpeed = 900.0, BodyTurn = 30.0;

        // ------------------------------------------------------------------------------------------------ proof counters
        /// <summary>Frames eased; holds started (aim kept through a target gap); switches followed after their wait; jumps never followed
        /// (the aim came back within the wait); frames the fire gate held a rocket.</summary>
        public static int Frames, Holds, Switches, JumpsIgnored, GateFrames;
        /// <summary>Largest one-frame step of the raw aim and of the drawn aim since <see cref="ResetCounters"/> (deg).</summary>
        public static double MaxStepRaw, MaxStepDrawn;
        public static void ResetCounters() { Frames = Holds = Switches = JumpsIgnored = GateFrames = 0; MaxStepRaw = MaxStepDrawn = 0; }

        // ------------------------------------------------------------------------------------------------ state
        bool init, holding, hasHeld;
        double yaw, elev, yawVel, elevVel;              // drawn
        double goalYaw, goalElev;                       // adopted aim
        double prevYaw, prevElev, prevStep;             // last frame's wanted aim and its step (jump detector)
        double rawPrevYaw, rawPrevElev;                 // last frame's raw aim (proof counter)
        double heldYaw, heldElev, lastOnAt = double.NegativeInfinity, waitFrom = -1, prevBody, trackRate;

        /// <summary>The drawn heading / elevation after the last <see cref="Step"/> (deg).</summary>
        public double Yaw => yaw;
        public double Elev => elev;
        /// <summary>True while the drawn tube is more than LagTol off a target aim (a rocket should wait).</summary>
        public bool HoldsFire { get; private set; }
        /// <summary>Degrees between the drawn aim and the raw target aim (0 without a target).</summary>
        public double Lag { get; private set; }
        /// <summary>The last aim was kept through a target gap this frame.</summary>
        public bool Holding => holding;

        /// <summary>The next Step starts from the raw aim (first frame, after a frame gap, knocked down, not drawn).</summary>
        public void Reset() { init = false; holding = false; hasHeld = false; waitFrom = -1; HoldsFire = false; Lag = 0; lastOnAt = double.NegativeInfinity; }

        /// <summary>One frame. raw = the aim WeaponHold would lay the tube on now; onTarget = that aim is on a target (UnitAim's or the
        /// bazooka's ballistic line); engaged = the sim holds a live target (the aim is on it, not a guess); sent = he walks on an order to
        /// go elsewhere (a move, entering, a crate, a repair: no aim is held); bodyYaw = the robot's front; shotHeld = UnitAim holds a rocket
        /// for the tube.</summary>
        public void Step(double now, double dt, double rawYaw, double rawElev, bool onTarget, bool engaged, bool sent, double bodyYaw, bool shotHeld)
        {
            if (!(dt > 1e-5)) dt = 1e-5;
            rawYaw = Wrap(rawYaw); bodyYaw = Wrap(bodyYaw);
            // (1) the aim wanted now: the raw target aim, else the last one held, else the raw (carry) aim
            double wy, we;
            if (onTarget) { wy = rawYaw; we = rawElev; lastOnAt = now; holding = false; }               // the held aim is set below, only from an aim the drawn tube follows
            else if (hasHeld && !sent && now - lastOnAt <= HoldSecs)
            {
                wy = Wrap(bodyYaw + Clamp(Delta(bodyYaw, heldYaw), -TwistMax, TwistMax)); we = heldElev;
                if (!holding) { holding = true; Holds++; }
            }
            else { wy = rawYaw; we = rawElev; hasHeld = false; holding = false; }
            if (!init)
            {
                init = true; yaw = goalYaw = prevYaw = rawPrevYaw = wy; elev = goalElev = prevElev = rawPrevElev = we; yawVel = elevVel = prevStep = 0; waitFrom = -1;
                prevBody = bodyYaw; trackRate = 0;
                if (onTarget) { heldYaw = wy; heldElev = we; hasHeld = true; }
                Lag = 0; HoldsFire = false; return;
            }
            Frames++;
            double rawStep = Between(rawYaw, rawElev, rawPrevYaw, rawPrevElev); rawPrevYaw = rawYaw; rawPrevElev = rawElev;
            if (rawStep > MaxStepRaw) MaxStepRaw = rawStep;
            trackRate = onTarget ? trackRate + ((rawStep <= SwitchDeg ? rawStep / dt : 0.0) - trackRate) * Math.Min(1.0, dt / TrackRateSecs) : 0.0;   // the raw target aim's steady turn rate (a jump, or the carry along a turning body, is none)
            // (2) a jump is followed once it has held SwitchHold s (at once while a rocket waits); back where the drawn aim goes: nothing to follow
            double step = Between(wy, we, prevYaw, prevElev); prevYaw = wy; prevElev = we;
            bool jump = onTarget && step > SwitchDeg && step > JumpRatio * prevStep; prevStep = step;
            if (!onTarget && waitFrom >= 0) waitFrom = -1;                                          // the target went before it was followed
            if (jump) waitFrom = now;
            if (waitFrom >= 0 && Between(wy, we, goalYaw, goalElev) <= SwitchDeg) { waitFrom = -1; JumpsIgnored++; }
            bool wait = waitFrom >= 0 && !shotHeld && now - waitFrom < (engaged ? SwitchHold : PredictHold);
            if (!wait)
            {
                if (waitFrom >= 0) { Switches++; waitFrom = -1; }
                goalYaw = wy; goalElev = we;
                if (onTarget) { heldYaw = wy; heldElev = we; hasHeld = true; }                   // a jump still waiting, or gone before it was followed, is never held
            }
            // (3) critically damped follow
            bool carry = !onTarget && !holding && Math.Abs(Delta(prevBody, bodyYaw)) > BodyTurn * dt; prevBody = bodyYaw;
            double st = SmoothTime * (shotHeld ? HurrySmooth : carry ? CarrySmooth : 1.0), sp = shotHeld ? MaxSpeed * HurrySpeed : carry ? CarrySpeed : MaxSpeed, py = yaw, pe = elev;
            yaw = Wrap(SmoothDamp(yaw, yaw + Delta(yaw, goalYaw), ref yawVel, st, sp, dt));
            elev = Clamp(SmoothDamp(elev, goalElev, ref elevVel, st, sp, dt), -60.0, 60.0);
            double drawnStep = Between(yaw, elev, py, pe); if (drawnStep > MaxStepDrawn) MaxStepDrawn = drawnStep;
            // (4) the fire gate
            Lag = onTarget ? Between(yaw, elev, rawYaw, rawElev) : 0.0;
            HoldsFire = onTarget && Lag > LagTol + trackRate * SmoothTime * HurrySmooth;          // a fast crossing target (a jeep, the shuttle): its steady follow lag never holds a rocket
            if (HoldsFire && shotHeld) GateFrames++;
        }

        // ------------------------------------------------------------------------------------------------ maths (as Unity's Mathf)
        /// <summary>Mathf.SmoothDamp (Unity's critically damped spring), in double.</summary>
        public static double SmoothDamp(double current, double target, ref double velocity, double smoothTime, double maxSpeed, double dt)
        {
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime, x = omega * dt, exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target, original = target, maxChange = maxSpeed * smoothTime;
            change = Clamp(change, -maxChange, maxChange);
            target = current - change;
            double temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            double output = target + (change + temp) * exp;
            if (original - current > 0.0 == output > original) { output = original; velocity = (output - original) / dt; }
            return output;
        }

        /// <summary>Mathf.DeltaAngle: the shortest signed turn from a to b (deg).</summary>
        public static double Delta(double a, double b) { double d = (b - a) % 360.0; if (d < 0) d += 360.0; if (d > 180.0) d -= 360.0; return d; }
        public static double Wrap(double a) { double d = (a + 180.0) % 360.0; if (d < 0) d += 360.0; return d - 180.0; }
        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>Angle (deg) between two directions given as heading / elevation.</summary>
        public static double Between(double y1, double e1, double y2, double e2)
        {
            const double R = Math.PI / 180.0;
            double c1 = Math.Cos(e1 * R), c2 = Math.Cos(e2 * R);
            double dot = c1 * c2 * Math.Cos((y1 - y2) * R) + Math.Sin(e1 * R) * Math.Sin(e2 * R);
            return Math.Acos(Clamp(dot, -1.0, 1.0)) / R;
        }
    }
}
