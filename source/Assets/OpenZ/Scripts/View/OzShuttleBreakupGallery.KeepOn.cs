using System.Linq;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The break-up proof's fort-gun stages G19 and K3 (test only): their staging.
    /// <list type="bullet">
    /// <item>The match goes on: ClearFort takes the fort's own units out, and on a fresh level 16 those are all the enemy has, so the
    /// rules ended the game (one team left; DV10 freezes the match) and the fort's gun fell silent (K3: 0 shells; the victory cheer
    /// 26 s after the level start). <see cref="KeepMatchOn"/> keeps one enemy squad far away from the fort, the ship and every own unit.</item>
    /// <item>G19 counts the fort's shells at the hovering ship: nothing may bring it down meanwhile (the fort's roof defender comes up
    /// with rifle rounds and rifle grenades; topped up only under 60 %, the ship fell after the second shell and the count stopped at 2).</item>
    /// <item>K3 wants the fort's gun to be the killing hit: from its first shell the ship is held at <see cref="FortKillHp"/> (the roof
    /// defender's rounds, 11 a hit, are topped up again; a fort shell does far more and is the kill). It was held at 1 HP: any rifle
    /// round of the roof defender during the shell's 3.9 s flight killed it first.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleBreakupGallery
    {
        /// <summary>K3: the ship's hit points from the fort's first shell on (more than the roof defender's rounds can take in a frame).</summary>
        public const int FortKillHp = 100;
        /// <summary>The far squad: at least this far (m) from the fort and the ship, this far from every own unit.</summary>
        public const float KeepFarM = 90f, KeepOwnM = 40f;
        /// <summary>Proof: squads kept so that the match goes on.</summary>
        public static int KeptSquads;

        /// <summary>ClearFort, after the fort's units left: when the enemy has no unit left, one squad far away keeps the match on.</summary>
        void KeepMatchOn(ZObject fort)
        {
            if (W == null || fort == null) return;
            if (W.Objects.Any(o => o.IsUnit && o.Owner == them && !o.Destroyed && o.KillAt < 0 && !o.IsFortDefender)) return;
            var fc = W3(fort.CenterX, fort.CenterY); var sc = s != null ? W3(s.CenterX, s.CenterY) : fc;
            var own = W.Objects.Where(o => o.IsUnit && o.Owner == me && !o.Destroyed && o.KillAt < 0).Select(o => W3(o.CenterX, o.CenterY)).ToList();
            for (float far = KeepFarM; far >= 40f; far -= 25f)
            {
                int bx = -1, by = -1; float best = float.MaxValue;
                for (int ty = 4; ty < W.Map.height - 4; ty += 2)
                    for (int tx = 4; tx < W.Map.width - 4; tx += 2)
                    {
                        if (!W.Path.TilePassable(tx, ty, true)) continue;
                        var p = W3(tx * 16 + 8, ty * 16 + 8);
                        float df = Flat(p, fc);
                        if (df < far || Flat(p, sc) < far || OpenZFxPlayer.IsWaterTile(p)) continue;
                        bool clear = true; foreach (var q in own) if (Flat(p, q) < KeepOwnM) { clear = false; break; }
                        if (!clear || df >= best) continue;
                        best = df; bx = tx; by = ty;
                    }
                if (bx < 0) continue;
                var sq = Squad("grunt", bx, by, them);
                if (sq == null) continue;
                KeptSquads++;
                Log($"the match goes on: the enemy had no unit left (the rules would end the game and silence the fort's gun); one {them} grunt squad kept at tile ({bx},{by}), {best:F0} m from the fort");
                return;
            }
            Log("the match goes on: NO place for a far enemy squad (the rules may end the game: the fort's gun then stays silent)");
        }

        /// <summary>FortGun's loop, every frame: G19 nothing brings the ship down while the shells are counted; K3 healed until the fort
        /// fires at it, then held at FortKillHp so that a shell of the fort is the kill.</summary>
        void FortHold(bool kill, int shots)
        {
            if (s == null || s.Destroyed) return;
            if (kill)
            {
                if (shots == 0) Heal();
                else if (s.Health > 0 && s.Health != FortKillHp) s.SetHealth(FortKillHp);
                return;
            }
            if (s.MaxHealth < 1000000) s.MaxHealth = 1000000;
            Heal();
        }
    }
}
