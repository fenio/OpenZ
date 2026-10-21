#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row of the level high scores and ranks (view only: the sim is read at the end of a match, never
    /// written). The open engine port has no score, rank or table, so the count is OpenZ's own; the row is built from the live
    /// constants of OzHighScores and measured by the counted check OpenZ/QC/High Scores Proof (OzHighScoresProof.Report), run
    /// when the block is written.</summary>
    public static partial class OpenZParity
    {
        /// <summary>The row's number. The kit's apply script writes the next free number here when it copies the file in (the number
        /// below was the next free one when the kit was made: the bug kits of the same stage take DV82 to DV84).</summary>
        const string HighScoresDvId = "DV85";

        static void AddHighScoresDeviation(List<Deviation> list)
        {
            string g(int n) => OzHighScores.Group(n);
            string proof; bool pass;
            try { proof = OzHighScoresProof.Report().Replace("[OpenZ high scores proof] ", ""); pass = proof.Contains(" checks. PASS"); }
            catch (Exception ex) { proof = "the proof threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            var a = OzHighScores.Score(true, 754.9, 42, 18, 6, 7); var b = OzHighScores.Score(true, 2000, 10, 30, 14, 14); var c = OzHighScores.Score(false, 400, 12, 36, 2, 7);
            list.Add(new Deviation
            {
                id = HighScoresDvId, change = "Level high scores and ranks (view only)",
                zod = "no score, rank or table: the end of a game only counts the teams that still own units (zserver.cpp:325-367) and adds a win or a loss to an online account (zserver.cpp:2885-2925)",
                openz = $"OzHighScores: score = victory {g(OzHighScores.VictoryPoints)} + speed ({g(OzHighScores.SpeedMax)} - {OzHighScores.SpeedPerSecond} per second, a victory only) " +
                        $"+ combat {g(OzHighScores.CombatMax)} x kills / (kills + losses) + territory {g(OzHighScores.TerritoryMax)} x zones held / zones; ranks by score, fourteen army ranks: PRIVATE, then {OzHighScores.LadderLine()}; " +
                        $"the {OzHighScores.TableSize} best victories per level and difficulty in a local file ({OzHighScores.FileName} in the game's own data folder); shown on the end screen (score strip with its TOP {OzHighScores.TableSize} table), on the level cards (BEST chip) and in the HIGH SCORES popup of the battlefield screen (every entry: rank, score, time, units killed, lost and built, zones); no sim rule reads it",
                measured = $"three staged endings {g(a.total)} / {g(b.total)} / {g(c.total)} ({OzHighScores.Rank(a.total)} / {OzHighScores.Rank(b.total)} / {OzHighScores.Rank(c.total)}); OpenZ/QC/High Scores Proof: {proof}",
                toggle = "none (always on; a defeat or a draw, a CPU on the player's side, a test session or the testing overlay keep a result out of the table)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
