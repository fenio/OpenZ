namespace OpenZ
{
    /// <summary>
    /// K344 / K418 P7, the kickflip of a ship destroyed mid-flip (view only). The gate (OzShuttleKickflipGate, order -47) ends the flip of a
    /// ship the rules' tick destroyed (order -50): it leaves the flipped pose as drawn and stops the flip. A kill that comes AFTER the gate in
    /// the same frame (anything outside the rules' tick: a test's kill, a cheat, a later script) found the level pose already back and the
    /// ship's view returning early from then on (dying, crashing, broken up), so the flip state stayed in its Flip phase on the dead ship and
    /// the death and the break-up started from the level pose: a one-frame snap out of the roll.
    /// <see cref="KickflipDeathBake"/>, called where the view first sees the death (Down, and first thing in LateUpdate), does what the gate
    /// does: the last drawn flipped pose back on (lamp heads kept on aim), the flip stopped, nothing restored. The dying drift and the
    /// break-up then start from the flipped pose that was on screen.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>Proof: flips stopped on a dead ship by this (not by the gate).</summary>
        public int KickDeathBakes { get; private set; }

        void KickflipDeathBake()
        {
            if (flip.Phase == OzKickflipPhase.Idle && !kickOn) return;
            bool gone = Obj == null || game == null || game.World == null || Obj.Destroyed || Obj.KillAt >= 0 || game.World.Get(Obj.Id) != Obj || BrokenUp || dying || crashing;
            if (!gone) return;
            if (!kickOn && KickNow.Active) KickPose(true, true);                                                 // the picture's last flipped pose (the gate had put the level one back)
            kickOn = false; kickGimballed = false; flip.Kill(); KickNow = default; KickBaked++; KickDeathBakes++;
            for (int i = 0; i < kickLampDimmed.Length; i++) kickLampDimmed[i] = false;                          // the lamps stay as drawn and go out with the ship
            for (int h = 0; h < kickLightDimmed.Length; h++) kickLightDimmed[h] = false;
        }
    }
}
