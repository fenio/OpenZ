using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: a building's heavy break-up chunks against the rocks (OzRockSolid.All.cs, OzRockBody). When a
    /// building that is not a fort collapses, each of its big parts / library chunks whose longest side is at least 1 m (the L and the
    /// bigger M chunks; the S / XS chips stay as they are) meets the rocks within its reach while it flies, comes to rest on or beside them,
    /// and when it lies on a rock at the end of its life it pops away instead of sinking through the rock top (BigPart). A fort's chunks
    /// are OzRockSolid's own (the fort's fall). View only.
    /// </summary>
    public partial class BuildingBreaker
    {
        /// <summary>LaunchBigParts, before the parts fly: the big ones meet the rocks.</summary>
        void RocksBigParts(Vector3 center, bool fort)
        {
            if (fort || !OzRockSolid.AllOn) return;
            foreach (var rb in bigParts)
            {
                if (!rb) continue;
                OzRockBody.Add(rb.gameObject, rb, OzRockBody.Kind.BuildingChunk, OzRockBody.ShapeSize(rb.gameObject), BigPartLifeMin - 0.5f);
            }
        }
    }
}
