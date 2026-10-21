using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle under the pointer (rule DV118): the unit overlay's hover box for the shuttle is its real drawn outline (the
    /// selection frame's own silhouette, PlayerInput.MeshRect, projected from where the ship is drawn, in the air too), not the small
    /// box every vehicle gets at its ground point. So the pointer on the ship shows its name chip "SHUTTLE" over the health bar as on
    /// every other unit, landed and flying. Hook: OpenZUnitOverlay.HoverUnit.
    /// </summary>
    public static class OzShuttleOverlay
    {
        /// <summary>Proof: the shuttle's id the last time it was under the pointer, and the frames it was.</summary>
        public static int LastHoverId = -1, HoverFrames;
        static PlayerInput input;

        /// <summary>The pointer `mp` (screen px) is on the drawn ship; sd = the squared distance to its frame's middle.</summary>
        public static bool Hovered(ZObject o, Vector2 mp, out float sd)
        {
            sd = float.MaxValue;
            if (o == null || !o.IsShuttle) return false;
            if (!input) input = Object.FindFirstObjectByType<PlayerInput>();
            if (!input || !input.MeshRect(o, out var r) || !r.Contains(mp)) return false;
            sd = (r.center - mp).sqrMagnitude;
            LastHoverId = o.Id; HoverFrames++;
            return true;
        }
    }
}
