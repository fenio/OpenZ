using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Robot antics on the unit view (AnticDirector picks who and what; AnticRun plays it). Every frame, before the other animation
    /// owners, AnticAnim either keeps the idle timer (any order, move, attack, selection, hit, throw, grab, stance or knockdown resets
    /// it) or, while an antic runs, checks the same list plus threats and cancels on that very frame: the props pop off, the weapon
    /// pops back and the normal state machine below cross-fades out of the clip (a move or a shot starts the same frame).
    /// While the antic runs it owns the base layer at 1x (its events are on the authored frames), the upper-body aim layer eases out
    /// and WeaponHold is suspended (the weapon clips hold the rifle themselves; the hand antics stow it). View only.
    /// </summary>
    public partial class UnitView
    {
        public static bool AnticsEnabled = true;
        AnticRun antic; AnticKind? anticLast;
        float anticIdleSince = -1f, anticHp = -1f; int anticStates = -1;

        public AnticRun Antic => antic;
        public bool AnticActive => antic != null;
        public AnticKind? AnticLast => anticLast;
        /// <summary>RobotLegIK releases the legs while an antic poses them (the sunbed).</summary>
        public bool AnticPosesLegs => antic != null && antic.PosesLegs;
        public float AnticIdleFor => anticIdleSince < 0f ? 0f : Time.time - anticIdleSince;
        public float AnticCooldownUntil { get; private set; }
        public void AnticResetIdle() => anticIdleSince = Time.time;
        /// <summary>The robot's front (the view yaw), world space.</summary>
        public Vector3 AnticFront => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        public float AnticYaw => yaw;

        /// <summary>A humanoid robot whose controller has the antic states (grunt + the V3 types; never the Generic laser).</summary>
        public bool CanAntic
        {
            get
            {
                if (!AnticsEnabled || !anim || !anim.isHuman || Obj == null || Obj.Kind != ZKind.Robot || Dying) return false;
                if (anticStates < 0)
                    anticStates = anim.HasState(0, Animator.StringToHash("antic_turn")) && anim.HasState(0, Animator.StringToHash("drink"))
                                  && anim.HasState(0, Animator.StringToHash("fish_cast")) && anim.HasState(0, Animator.StringToHash("sunbed_lie")) ? 1 : 0;
                return anticStates == 1;
            }
        }

        /// <summary>Why this robot is not idle right now (null = idle). Cheap: read every frame.</summary>
        public string AnticBusyReason()
        {
            if (Obj == null || Obj.Destroyed || Dying) return "dead";
            if (game == null || game.World == null || game.World.GameOver) return "game over";
            if (Obj.Waypoints.Count > 0) return "order";
            if (Obj.Leader != null && (Obj.Leader.Waypoints.Count > 0 || Obj.Leader.IsMoving)) return "squad order";
            if (Obj.IsMoving || walkShown) return "moving";
            if (Obj.AttackObject != null) return "enemy (engaged)";
            if (Obj.Stunned) return "stunned";
            if (anticHp >= 0f && Obj.Health < anticHp) return "hit";
            if (reactKind != 0) return "knocked";
            if (Time.time < throwUntil) return "throw";
            if (Grabbing || grabActive) return "grab";
            if (stance && stance.Busy) return "stance";
            if (fdMan) return "fort defender";
            if (OpenZEnding.Holds(Obj.Id)) return "ending";
            if (AnticDirector.IsSelected(Obj)) return "selected";
            return null;
        }

        /// <summary>UnitView's animation step (first owner): true while the antic owns the base layer.</summary>
        bool AnticAnim(float dt)
        {
            if (Obj == null || Obj.Kind != ZKind.Robot) return false;
            string busy = AnticBusyReason();
            if (Obj != null) anticHp = Obj.Health;
            if (antic == null)
            {
                if (busy != null || anticIdleSince < 0f) anticIdleSince = Time.time;
                if (AnticsEnabled && AnticDirector.Enabled && !AnticDirector.I && CanAntic) AnticDirector.Ensure();
                return false;
            }
            if (busy == null) busy = AnticDirector.ThreatReason(this);
            if (busy != null) { AnticCancel(busy, false); return false; }
            if (!antic.Tick(anim, dt)) { AnticStop(false, "done"); return false; }
            anim.speed = 1f;
            if (hold) hold.Suspended = true;
            if (anim.layerCount > 1) { upWeight = Mathf.MoveTowards(upWeight, 0f, dt * 10f); anim.SetLayerWeight(1, upWeight); }
            return true;
        }

        /// <summary>AnticDirector: starts `run` (false if the robot is not free or cannot play antics).</summary>
        public bool AnticBegin(AnticRun run)
        {
            if (run == null || antic != null || !CanAntic || AnticBusyReason() != null) return false;
            antic = run; anticLast = run.Kind; state = null;
            return true;
        }

        /// <summary>Cancels a running antic now (an order, a threat, a hit, death). `instant`: no pops (death / removal).</summary>
        public void AnticCancel(string reason, bool instant)
        {
            if (antic == null) return;
            var r = antic;
            r.Finish(true, instant, reason);
            antic = null; state = null; anticIdleSince = Time.time;
            AnticCooldownUntil = Time.time + Random.Range(AnticDirector.CooldownMin, AnticDirector.CooldownMax) * 0.5f;
            if (hold && !Dying) hold.Suspended = false;
            if (AnticDirector.I) AnticDirector.I.Stopped(r, true, reason);
        }

        void AnticStop(bool cancelled, string reason)
        {
            var r = antic; if (r == null) return;
            r.Finish(cancelled, false, reason);
            antic = null; state = null; anticIdleSince = Time.time;
            AnticCooldownUntil = Time.time + Random.Range(AnticDirector.CooldownMin, AnticDirector.CooldownMax);
            if (hold) hold.Suspended = false;
            if (AnticDirector.I) AnticDirector.I.Stopped(r, cancelled, reason);
        }

        /// <summary>AnticRun: turns the standing robot toward `want` (fishing faces the water, the lounger's spot) at the robot turn rate.</summary>
        public void AnticFace(float want, float dt)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, want)) < 0.5f) return;
            yaw = Mathf.MoveTowardsAngle(yaw, want, TurnRate * 0.5f * dt); heading = yaw;
            transform.rotation = Quaternion.Euler(0f, yaw + modelYaw, 0f);
        }
    }
}
