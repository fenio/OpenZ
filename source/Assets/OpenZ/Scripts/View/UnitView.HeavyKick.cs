namespace OpenZ
{
    /// <summary>
    /// slides the barrel that fired (each its own envelope), UnitView.ApplyRecoil jolts the hull back per shot as before, and this
    /// rocks the suspension toward the side of the barrel that fired (VehicleMotion spring, like the MLRS per-side kick).
    /// </summary>
    public partial class UnitView
    {
        /// <summary>Degrees of the heavy tank's per-barrel hull roll / pitch kick (VehicleMotion spring; MLRS uses 1.6 / -1.2).</summary>
        public static float HeavyRollDeg = 1.6f, HeavyPitchDeg = -0.8f;

        void HeavyBarrelKick()
        {
            if (Obj == null || Obj.Type != "heavy" || !motion || !gunAim) return;
            motion.Kick(gunAim.FiredSide * HeavyRollDeg, HeavyPitchDeg);
        }
    }
}
