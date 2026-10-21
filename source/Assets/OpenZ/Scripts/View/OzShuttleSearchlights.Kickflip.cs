using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K344 (the shuttle's kickflip, view only): what the flip's lamp fade needs of the two belly lamps (a new part of their class, nothing of
    /// it changed): each lamp's drawn pieces, so the flip can dim them in its picture while the hull is in their beam's way
    /// (View/OzShuttleKickflip.Lamps.cs). Their colours are set by renderer (SetPropertyBlock without a material index).
    /// </summary>
    public sealed partial class OzShuttleSearchlights
    {
        /// <summary>Lamp i's lens, halo, beam cone, its pool of light on the ground and its real light (null when there is no such lamp).</summary>
        internal bool KickParts(int i, out Renderer lens, out Renderer halo, out Renderer beam, out Renderer pool, out Light light)
        {
            lens = halo = beam = pool = null; light = null;
            if (i < 0 || i >= lamps.Length || lamps[i] == null) return false;
            var L = lamps[i]; lens = L.lens; halo = L.halo; beam = L.beam; pool = L.pool; light = L.light;
            return true;
        }
    }
}
