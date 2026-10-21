using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shot-down shuttle stays drawn until it breaks (rule DV118, view only; OzShuttleDeathPlan.KeepDrawn): from the rules' death
    /// to its drawn kill moment (a round still in flight to it, a blast front still on its way) the ship drifts on as it was drawn
    /// (DyingPose) and its unit view never hides it; the break-up takes it at the drawn kill moment (its keeper hides the whole ship
    /// then). A ship whose break-up never comes falls whole (its old fall) and stays drawn for that fall too.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>UnitView keeps this dead ship drawn (OzShuttleBreakup.KeepsDrawn).</summary>
        internal bool KeepDrawnForBreak => OzShuttleDeathPlan.KeepDrawn(BrokenUp, dying, crashing, dyingT);

        Renderer deathHoldHull; bool deathHoldHullSought;
        /// <summary>Proof: the whole ship is drawn now (its object active and its hull's renderer on).</summary>
        public bool HullDrawn
        {
            get
            {
                if (!this || !gameObject.activeInHierarchy) return false;
                if (!deathHoldHullSought)
                {
                    deathHoldHullSought = true;
                    foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r && r.gameObject.name == "SH_Hull") { deathHoldHull = r; break; }
                }
                return !deathHoldHull || deathHoldHull.enabled;
            }
        }
        /// <summary>Proof: dying (the rules have it dead, the drawn kill moment still to come) and how long (s).</summary>
        public bool DyingNow => dying;
        public float DyingSec => dyingT;
    }
}
