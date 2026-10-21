using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: a unit's big death pieces against the rocks (OzRockSolid.All.cs, OzRockBody):
    /// <list type="bullet">
    /// <item>the flipped light-vehicle hull (DV17) meets the rocks while it flies and tumbles, comes to rest on or beside them, and when
    /// it lies on a rock at the end of its smoulder it pops away with its dust instead of sinking through the rock top;</item>
    /// <item>the turret toss and the scripted blast fall land on free ground (UnitView.cs: OzRockSolid.TurretLanding / BodyLanding);</item>
    /// <item>a robot's ragdoll meets the rocks (RobotRagdoll.Launch); its corpse pops about the rock top it lies on.</item>
    /// </list>
    /// View only.
    /// </summary>
    public partial class UnitView
    {
        /// <summary>StartFlip: the flipped hull is a big body against the rocks (until its fade begins).</summary>
        void RockFlip(BoxCollider bc)
        {
            if (!wreckBody || !bc) return;
            var ls = transform.lossyScale;
            OzRockBody.Add(gameObject, wreckBody, OzRockBody.Kind.FlipHull, new Vector3(bc.size.x * Mathf.Abs(ls.x), bc.size.y * Mathf.Abs(ls.y), bc.size.z * Mathf.Abs(ls.z)), WreckFx.SmoulderSeconds - 0.5f, () => fading);
        }

        /// <summary>BeginFade: the sink depth of the wreck; a flipped hull lying on a rock pops away now instead (0 = no sink).</summary>
        float RockFadeDepth(float depth)
        {
            if (!flipWreck || !OzRockBody.OnRockNow(gameObject)) return depth;
            OzRockBody.CountPop(gameObject);
            PopAway();
            return 0f;
        }
    }
}
