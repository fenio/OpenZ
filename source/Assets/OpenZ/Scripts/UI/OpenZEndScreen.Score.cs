using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// The end screen's score strip (between the forces chart and the buttons): SCORE counting up, the RANK, and what the result
    /// did in the level's table: NEW RECORD, its place in the top 10, or why it is not in it. Count, ranks and the table:
    /// OzHighScores.
    ///
    /// A result enters the table when the level was WON by the player himself in a normal match: not when a CPU played his side
    /// (bot match, demo, autopilot), not in a test session (also while the gate battery runs, which stages a victory screen),
    /// not when the testing overlay put units on the map. A defeat or a
    /// draw shows its score and rank and the line "WIN THE LEVEL TO ENTER THE TABLE".
    /// An entry is the result only: nothing is asked for, typed or shown besides it.
    /// The right half holds two lines that both end at the strip's right margin: what the result did in the table and, under
    /// it, the amber TOP 10 sign. The level's table is one hover away and the sign says so: hovering that half shows the table
    /// (place, rank, score, time), hovering the left half shows how the score was made and the rank ladder.
    ///
    /// The two hover areas are switched off until the strip is fully shown (ScoreLive): a pointer resting there while the end
    /// screen opens calls no tooltip over the still-empty screen.
    ///
    /// Timeline (s after the end screen opens, with the rest of OpenZEndScreen.Animate): 2.45 the strip rises (ui_plate_land),
    /// 2.55 - 3.35 the score counts up (ui_count_tick), 3.40 - 3.54 rank, table line and TOP 10 sign fade in; when they stand the
    /// hover areas go live and the cue plays: NEW RECORD ui_victory_hit, a place in the top 10 ui_unit_ready, anything else
    /// ui_stamp. A click that skips the reveal skips the strip's sounds too, except the record / table cue: that one always
    /// plays.
    /// </summary>
    public partial class OpenZEndScreen
    {
        const float ScoreAt = 2.45f, ScoreNoteW = 330f;
        // the right half's two lines (strip units from the strip's top): the note's capitals centred on NoteMidY, the TOP 10 sign
        // from TopY, 18 u tall. Together they are 39.4 u tall (capitals 8.7, 12.6 between, the sign 18) and stand in the middle of
        // the 64 u strip: 12.6 u above, 12 u below
        const float ScoreNoteMidY = 17f, ScoreTopY = 34f;
        RectTransform scoreRt, scoreTopRt; OzText scoreValue, scoreRank, scoreNote; Image scoreFlash, scoreTopBg; HudButton scoreTableHot, scoreBreakHot;
        OzScoreParts scoreParts; OzScoreEntry scoreEntry; int scorePlace, scoreShown = -1; string scoreWhy;
        bool scoreSubmitted, scoreLanded, scoreRevealed; float scoreTickAt, scoreRevealAt = -100f;

        /// <summary>Checks: the score of this ending, its rank, its place in the table (0 = not in it), why it does not count (null = it counts).</summary>
        public int ProofScore => scoreParts.total;
        public string ProofRank => OzHighScores.Rank(scoreParts.total);
        public int ProofPlace => scorePlace;
        public string ProofUnranked => scoreWhy;
        /// <summary>Checks: the strip's hover areas take the pointer (false until the strip is fully shown).</summary>
        public bool ProofScoreLive => scoreTableHot != null && scoreTableHot.Alive;

        /// <summary>null = this ending enters the table; else the line the strip shows instead.</summary>
        string UnrankedWhy()
        {
            if (kind == "defeat") return "WIN THE LEVEL TO ENTER THE TABLE";
            if (kind != "victory") return "A DRAW DOES NOT ENTER THE TABLE";
            var W = game.World;
            foreach (var b in W.Bots) if (b.Team == hud.Player) return "THE CPU PLAYED YOUR SIDE · NOT IN THE TABLE";
            if (GameBoot.TestMode || PlayerPrefs.GetInt("oz_botmatch", 0) == 1 || OzDevEnding.I || GateBattery.Running) return "TEST SESSION · NOT IN THE TABLE";
            return OzHighScores.Unranked;
        }

        /// <summary>Show(): counts the score, enters it in the table (once per match), builds the strip.</summary>
        void BuildScore(Color accent, float sx0)
        {
            var W = game.World; var me = hud.Player; var st = hud.Stats;
            int zonesTotal = W.Zones.Count, zonesMine = 0; foreach (var z in W.Zones) if (z.Owner == me) zonesMine++;
            int kills = OpenZMatchStats.Get(st.Kills, me), losses = OpenZMatchStats.Get(st.Losses, me), built = OpenZMatchStats.Get(st.Built, me);
            scoreParts = OzHighScores.Score(win, W.Time, kills, losses, zonesMine, zonesTotal);
            scoreWhy = UnrankedWhy();
            if (!scoreSubmitted)
            {
                scoreSubmitted = true; scorePlace = 0; scoreEntry = null;
                if (scoreWhy == null)
                {
                    scoreEntry = new OzScoreEntry { score = scoreParts.total, seconds = (int)W.Time, kills = kills, losses = losses, built = built, zones = zonesMine, zonesTotal = zonesTotal };
                    scorePlace = OzHighScores.Submit(game.level, game.gameDifficulty, scoreEntry);
                    if (scorePlace == 0) scoreEntry = null;
                }
                Debug.Log($"[OpenZ] end screen score: {scoreParts.total} = victory {scoreParts.victory} + speed {scoreParts.speed} + combat {scoreParts.combat} + territory {scoreParts.territory} "
                          + $"(time {(int)W.Time} s, kills {kills}, losses {losses}, zones {zonesMine}/{zonesTotal}), rank {OzHighScores.Rank(scoreParts.total)}, "
                          + $"{game.level} {OpenZDifficulty.Label(game.gameDifficulty)}: {(scoreWhy != null ? "not entered (" + scoreWhy + ")" : scorePlace > 0 ? "place " + scorePlace + " of the table" : "outside the top " + OzHighScores.TableSize)}");
            }
            scoreShown = -1; scoreLanded = scoreRevealed = false; scoreRevealAt = -100f; scoreTableHot = scoreBreakHot = null;

            float w = OzLayout.EndChartW, h = OzLayout.EndScoreH;
            scoreRt = Hud.Node("Score", root).Place(new OzRect(OzAnchor.TC, sx0, OzLayout.EndScoreY, w, h));
            Hud.Plate(scoreRt, "md", 0f, accent);
            scoreFlash = Hud.Img(scoreRt, "Flash", OzUi.A(win ? OzUi.AmberHi : OzUi.Alert, 0f)); scoreFlash.rectTransform.Fill();
            Hud.Icon(scoreRt, "Glyph", "star", 18, (h - 20f) * 0.5f, 20, accent);
            Hud.Label(scoreRt, "Label", "SCORE", 11, OzUi.Muted, 0.22f, OzStyle.Plain).At(48, h * 0.5f, OzH.L, true, 60);
            scoreValue = Hud.Label(scoreRt, "Value", "0", 34, OzUi.Ink, -0.01f).At(112, h * 0.5f, OzH.L, true, 190);
            Hud.Img(scoreRt, "Rule", OzUi.A(OzUi.Khaki, 0.18f)).rectTransform.Place(new OzRect(314, 12, 1, h - 24));
            Hud.Label(scoreRt, "RankLabel", "RANK", 11, OzUi.Muted, 0.22f, OzStyle.Plain).At(330, h * 0.5f, OzH.L, true, 50);
            scoreRank = Hud.Label(scoreRt, "Rank", OzHighScores.Rank(scoreParts.total), 20, accent, 0.08f).At(382, h * 0.5f, OzH.L, true, 240);
            // right half, two lines that end at the strip's right margin (16 u): what the result did in the table and, under it,
            // the TOP 10 sign (the table is one hover away)
            Color noteColor = scorePlace == 1 ? (Color)OzUi.AmberHi : scorePlace > 1 ? (Color)OzUi.Ink : (Color)OzUi.Muted;
            scoreNote = Hud.Label(scoreRt, "Note", NoteText(), 12, noteColor, 0.14f, OzStyle.Plain).At(w - 16, ScoreNoteMidY, OzH.R, true, ScoreNoteW);
            float topRight = w - 16;
            var topChip = Hud.Chip(scoreRt, "TopChip", new OzRect(topRight - 64f, ScoreTopY, 64f, 18), "TOP " + OzHighScores.TableSize, OzUi.Amber, OzUi.GM950, 10, 0.16f);
            scoreTopBg = topChip.bg; scoreTopRt = topChip.bg.transform.parent.GetComponent<RectTransform>();
            float topW = topChip.text.preferredWidth + 18f;
            scoreTopRt.Place(new OzRect(topRight - topW, ScoreTopY, topW, 18));
            scoreTopRt.gameObject.AddComponent<CanvasGroup>().alpha = 0f;

            scoreBreakHot = ScoreHotspot("ScoreHot", new OzRect(0, 0, w - ScoreNoteW - 24f, h), BreakdownTip());
            scoreTableHot = ScoreHotspot("TableHot", new OzRect(w - ScoreNoteW - 24f, 0, ScoreNoteW + 24f, h), TableTip());
            ScoreLive(false);                                                                               // off until the strip is fully shown (AnimateScore)
            scoreRt.gameObject.AddComponent<CanvasGroup>();
        }

        HudButton ScoreHotspot(string name, OzRect r, string tip)
        {
            var b = HudButton.Make(scoreRt, name, null, r.Size, HudButtonKind.Small, 12);
            b.Rt.Place(r); b.Bg.enabled = false; b.Priority = 100; b.Tooltip = tip;
            if (b.Hatch) { Destroy(b.Hatch.gameObject); b.Hatch = null; }
            return b;
        }

        /// <summary>The strip's two hover areas take the pointer only while this is on (HudButton.Alive asks activeInHierarchy):
        /// off from Show() until the rank and the table line stand. Before that a pointer resting in the band would call the
        /// score or the table tooltip over the still-empty screen.</summary>
        void ScoreLive(bool on)
        {
            if (scoreBreakHot != null && scoreBreakHot.Rt) scoreBreakHot.Rt.gameObject.SetActive(on);
            if (scoreTableHot != null && scoreTableHot.Rt) scoreTableHot.Rt.gameObject.SetActive(on);
        }

        string NoteText()
        {
            var best = OzHighScores.Best(game.level, game.gameDifficulty);
            string bestText = best != null ? " · BEST " + OzHighScores.Group(best.score) : "";
            if (scoreWhy != null) return scoreWhy + (kind == "victory" ? "" : bestText);
            if (scorePlace == 1) return "NEW RECORD · " + game.level + " · " + OpenZDifficulty.Label(game.gameDifficulty);
            if (scorePlace > 1) return "PLACE " + scorePlace + " OF THE TOP " + OzHighScores.TableSize + bestText;
            return "OUTSIDE THE TOP " + OzHighScores.TableSize + bestText;
        }

        string BreakdownTip()
        {
            var p = scoreParts;
            return "SCORE " + OzHighScores.Group(p.total) + "|"
                 + "Victory " + OzHighScores.Group(p.victory) + "\n"
                 + "Speed " + OzHighScores.Group(p.speed) + " (9 000 minus 5 per second, for a victory)\n"
                 + "Combat " + OzHighScores.Group(p.combat) + " (3 000 x kills / kills + losses)\n"
                 + "Territory " + OzHighScores.Group(p.territory) + " (3 000 x zones held / zones)\n"
                 + OzHighScores.LadderTip() + "|";
        }

        /// <summary>The level's table for the hover of the right half (OzHighScores.TableLines: place, rank, score, mission time).</summary>
        string TableTip()
        {
            string lines = OzHighScores.TableLines(game.level, game.gameDifficulty, scoreEntry);
            return OzHighScores.TableTitle(game.level, game.gameDifficulty) + "|" + (lines.Length > 0 ? lines : "No victory on this level at this difficulty yet.") + "|";
        }

        // ------------------------------------------------------------------ reveal (Animate)
        void AnimateScore()
        {
            if (!scoreRt) return;
            float now = OzMotion.Now;
            float kp = OzEase.OutCubic(T(ScoreAt, OzMotion.Dur(0.26f)));
            scoreRt.Place(new OzRect(OzAnchor.TC, OzLayout.RefW * 0.5f - OzLayout.EndChartW * 0.5f, OzLayout.EndScoreY + 24f * (1f - kp), OzLayout.EndChartW, OzLayout.EndScoreH));
            SetGroupAlpha(scoreRt, kp);
            if (!scoreLanded && kp >= 1f) { scoreLanded = true; if (!skipped) OzSfx.Ui("ui_plate_land", 0.8f); }
            // the number counts 0 -> score over 0.8 s
            float kv = OzEase.OutQuart(T(ScoreAt + 0.1f, OzMotion.Dur(0.8f)));
            int v = Mathf.RoundToInt(scoreParts.total * kv);
            if (v != scoreShown)
            {
                scoreShown = v; scoreValue.Set(OzText.Tab(OzHighScores.Group(v)));
                if (!skipped && v > 0 && now - scoreTickAt > 0.06f) { scoreTickAt = now; OzSfx.Ui("ui_count_tick"); }
            }
            // rank + table line + TOP 10 sign; the record / table cue plays even when the reveal was skipped
            float kr = T(ScoreAt + 0.95f, OzMotion.Dur(0.14f, true));
            scoreRank.Alpha = kr; scoreNote.Alpha = kr;
            SetGroupAlpha(scoreTopRt, kr);
            if (!scoreRevealed && kr >= 1f)
            {
                scoreRevealed = true; scoreRevealAt = now;
                ScoreLive(true);                                                                            // from here on the hover areas take the pointer
                if (scorePlace == 1) OzSfx.Ui("ui_victory_hit", 0.7f);
                else if (scorePlace > 1) OzSfx.Ui("ui_unit_ready", 0.9f);
                else if (!skipped) OzSfx.Ui("ui_stamp", 0.5f);
            }
            if (scoreTopBg)
            {   // the sign lights while the pointer is on the table half
                Color tc = scoreTableHot != null && scoreTableHot.Hovered ? (Color)OzUi.AmberHi : (Color)OzUi.Amber;
                if (scoreTopBg.color != tc) scoreTopBg.color = tc;
            }
            float kf = OzMotion.T(scoreRevealAt, OzMotion.Dur(0.5f, true));
            SetA(scoreFlash, scorePlace > 0 && scoreRevealed && kf < 1f ? 0.35f * (1f - kf) : 0f);
            if (scorePlace == 1 && scoreRevealed && !OzMotion.Reduced && now - scoreRevealAt < 2.4f)
                scoreNote.color = OzMotion.Blink(OzMotion.BlinkSlow) ? (Color)OzUi.AmberHi : (Color)OzUi.Ink;      // NEW RECORD blinks for 2.4 s
            else if (scorePlace == 1 && scoreRevealed) scoreNote.color = OzUi.AmberHi;
        }
    }
}
