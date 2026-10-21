#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV108 hero squads, KH2: the Play menus that count items 196 to 198 on the drawn bones (View/OzHeroKh2Probe.cs prints the lines to
    /// the console when it is done). A/B in Play: RobotLegIK.HeroLie, RobotLegIK.HeroAnticFeet, UnitAim.HeroHeadLook (UnitAim.HeroTorso is
    /// read when a view is made: set it before the level starts).
    /// </summary>
    public static class OzHeroKh2PlayMenu
    {
        [MenuItem("OpenZ/Hero Squad/Play: KH2 lounger legs numbers (5 s, the heroes lying)")]
        static void Lie()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ HeroSquad KH2] Play first (Level 16 from its scene, the heroes on their loungers)"); return; }
            OzHeroKh2Probe.Begin("lie", 5f);
            Debug.Log("[OpenZ HeroSquad KH2] measuring the lying legs for 5 s ...");
        }

        [MenuItem("OpenZ/Hero Squad/Play: KH2 drink legs numbers (forces a drink on both)")]
        static void Drink()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ HeroSquad KH2] Play first (the pair standing idle, no enemy near)"); return; }
            var g = ZGame.I; if (!g || g.World == null) return;
            var sb = new StringBuilder("[OpenZ HeroSquad KH2] drink forced:");
            foreach (var o in g.World.Objects) if (o.IsHero && !o.Destroyed && o.KillAt < 0) sb.Append($" {o.HeroMemberName} #{o.Id}: {AnticDirector.Force(o.Id, "Drink")};");
            Debug.Log(sb.ToString());
            OzHeroKh2Probe.Begin("drink", 30f, true);
            Debug.Log("[OpenZ HeroSquad KH2] measuring the legs through the drinks and their return to the standing idle (up to 30 s) ...");
        }

        [MenuItem("OpenZ/Hero Squad/Play: KH2 face on target numbers (10 s, the heroes firing)")]
        static void Aim()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ HeroSquad KH2] Play first (the pair firing at enemies)"); return; }
            OzHeroKh2Probe.Begin("aim", 10f);
            Debug.Log("[OpenZ HeroSquad KH2] measuring the heroes' faces against their targets for 10 s ...");
        }
    }
}
#endif
