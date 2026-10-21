using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The second explosion type (OpenZFxPlayer.Exp2.cs) in its AIR form, for a blast high over the ground (the playable shuttle's
    /// break-up up in the air: OzShuttleBreakup.BigBlast.cs). The fireball, its flash, spikes, sparks, embers, light, metal debris and
    /// smoke are exactly the ground form's; what belongs to the ground under a blast is left out while <see cref="e2Air"/> is set (three
    /// small hooks in OpenZFxPlayer.Exp2.cs): the ground light, the Big look's ground fire ring, its rolling ground billows and its 2-3
    /// burning secondaries on the ground, the Small look's flat ground ring. The scorch keeps its own height gate.
    /// </summary>
    public partial class OpenZFxPlayer
    {
        /// <summary>Set only inside <see cref="Exp2Air"/>: the blast is up in the air (nothing of it laid on the ground under it).</summary>
        bool e2Air;
        /// <summary>Proof: air-form exp2 blasts played (all kinds).</summary>
        public static int Exp2AirTotal;

        /// <summary>One exp2 explosion in its air form (see the class). Returns false when it could not play (the player off, not built).</summary>
        public bool Exp2Air(Vector3 at, float U, Exp2Kind k, int id, float s, FxBlastKind kind, bool vehicle)
        {
            e2Air = true;
            try
            {
                bool ok = Exp2(at, U, k, id, s, kind, false, vehicle);
                if (ok) Exp2AirTotal++;
                return ok;
            }
            finally { e2Air = false; }
        }
    }
}
