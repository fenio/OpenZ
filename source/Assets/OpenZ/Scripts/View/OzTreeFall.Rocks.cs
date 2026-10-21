using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: a felled level 16 tree never lies in a rock (OzRockSolid.TreeAway). A tree whose lying trunk or crown
    /// would lie in a rock's drawn shape falls turned by the least turn that clears every rock (at most 90 deg); a fall that touches no rock
    /// keeps its way (away from the hit). View only.
    /// </summary>
    public partial class OzTreeFall
    {
        /// <summary>OnBroken: the way it falls, off the rocks.</summary>
        Vector3 RockFreeAway(Stand t, Vector3 foot, Vector3 away) => OzRockSolid.TreeAway(foot, away, Mathf.Max(2f, t.height), t.radius, Mathf.Clamp(t.height * 0.08f, 0.35f, 0.6f));
    }
}
