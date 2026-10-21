using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads, KH2 (item 197, Brad's drink): a hero's cross-fade into the first clip of a standing antic
    /// whose feet are held (RobotLegIK.HeroHoldsFeet: the drink, the phone, the neck stretch, the fishing) is <see cref="HeroFadeIn"/> s
    /// instead of 0.12 s. His feet are held where they stand through the antic (View/RobotLegIK.Hero2.cs), so only the body moves from the
    /// idle's posture to the antic's (the drink: about 6 cm x his size); over 0.12 s that drew the knees up to 1.2 cm a frame (measured off
    /// the engine), over 0.3 s under 1 cm. Every other robot and every other antic (the turn, the scan, the lounger): the antic's own fade.
    /// </summary>
    public sealed partial class AnticRun
    {
        public static float HeroFadeIn = 0.3f;

        float FirstFade(float fade) =>
            View && View.Obj != null && View.Obj.IsHero && RobotLegIK.HeroHoldsFeet(Kind) && RobotLegIK.HeroAnticFeet ? Mathf.Max(fade, HeroFadeIn) : fade;
    }
}
