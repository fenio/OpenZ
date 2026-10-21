using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV112 (Sim/ZHeroSquad.Kh4.cs): the unit panel names a hero's self-repair. For a hero robot of a kind that repairs himself (ALAN &amp; BRAD,
    /// the member whose health the panel shows) the small HEALTH label over the health bar reads "HEALTH · SELF-REPAIR 20%/MIN" (or
    /// "SELF-REPAIR 20%/MIN" where the long one would reach the health value), in the label's own size and spacing: green while he repairs
    /// (below full health), the label's muted grey at full health. Every other unit (and a vehicle with heroes inside: no repair there):
    /// "HEALTH" as before. View only.
    /// </summary>
    public partial class OpenZHud
    {
        string heroRegenShown;
        bool heroRegenShort;

        /// <summary>The panel's unit refresh hook (right after the health value and bar): the health label for the robot <paramref name="hu"/>.</summary>
        void HeroRegenLine(ZObject hu)
        {
            if (uHpLabel == null) return;
            int pct = hu != null && hu.Kind == ZKind.Robot && hu.MaxHealth > 0 && !hu.Destroyed ? ZHeroBoost.RegenPct(hu.Hero) : 0;
            if (pct <= 0)
            {
                if (heroRegenShown != null) { uHpLabel.Set("HEALTH"); uHpLabel.color = OzUi.Muted; heroRegenShown = null; }
                return;
            }
            string full = $"HEALTH · SELF-REPAIR {pct}%/MIN", brief = $"SELF-REPAIR {pct}%/MIN";
            if (heroRegenShown == null || (heroRegenShown != full && heroRegenShown != brief))
            {
                uHpLabel.Set(full);
                heroRegenShort = uHpLabel.preferredWidth > HeroRegenLabelMaxW;                                 // measured once per text: the long one or the short one
                heroRegenShown = heroRegenShort ? brief : full;
            }
            uHpLabel.Set(heroRegenShown);
            uHpLabel.color = hu.HeroRepairing ? (Color)OzUi.Go : (Color)OzUi.Muted;
        }

        /// <summary>The room for the label: from its left edge to a little before the right-aligned health value ("100%").</summary>
        const float HeroRegenLabelMaxW = OzLayout.UnitHpValueRX - 48f - OzLayout.UnitNameX;
    }
}
