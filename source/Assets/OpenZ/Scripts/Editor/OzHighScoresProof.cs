using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the level high scores (no scene, no Play mode): menu OpenZ/QC/High Scores Proof (numbers).
    /// It works on a file of its own in the temporary folder: the player's own table is not touched.
    ///  1. Three staged endings give the expected scores and ranks:
    ///       B  victory, 33:20 (2000 s), 10 kills, 30 losses, 14 of 14 zones -> 10 000 +     0 +   750 + 3 000 = 13 750  MAJOR
    ///       C  defeat,   6:40 (400 s), 12 kills, 36 losses, 2 of 7 zones   ->      0 +     0 +   750 +   857 =  1 607  PRIVATE
    ///  2. The table: 12 results entered in mixed order leave 10, best first; equal scores: the shorter mission first, then
    ///     the older entry; a 13th, weaker result is refused (place 0) and the table stays as it is.
    ///  3. The table survives: the file is read again from disk and holds the same 10 entries in the same order. Another level
    ///     and another difficulty have their own, empty tables.
    ///  4. A table file written by an earlier build, which holds a name with every entry, is read as it is; the name is not
    ///     kept: the next write of the file holds none.
    /// An entry carries no name: the check tells its rows apart by a number in their kills field (shown as E1 .. E13).
    /// </summary>
    public static class OzHighScoresProof
    {
        [MenuItem("OpenZ/QC/High Scores Proof (numbers)")]
        public static void Run() { Debug.Log(Report()); }

        static string Line(System.Collections.Generic.IReadOnlyList<OzScoreEntry> list)
        {
            var sb = new StringBuilder();
            foreach (var e in list) { if (sb.Length > 0) sb.Append(' '); sb.Append('E').Append(e.kills).Append(':').Append(e.score).Append('/').Append(e.seconds); }
            return sb.ToString();
        }

        public static string Report()
        {
            int checks = 0, ok = 0; var bad = new StringBuilder();
            void Check(string what, bool pass, string got) { checks++; if (pass) ok++; else bad.Append(" | FAIL ").Append(what).Append(": ").Append(got); }

            // 1. the three staged endings
            var a = OzHighScores.Score(true, 754.9, 42, 18, 6, 7);
            var b = OzHighScores.Score(true, 2000, 10, 30, 14, 14);
            var c = OzHighScores.Score(false, 400, 12, 36, 2, 7);
            Check("ending A = 19901 MAJOR GENERAL", a.total == 19901 && a.victory == 10000 && a.speed == 5230 && a.combat == 2100 && a.territory == 2571 && OzHighScores.Rank(a.total) == "MAJOR GENERAL", $"{a.victory}+{a.speed}+{a.combat}+{a.territory}={a.total} {OzHighScores.Rank(a.total)}");
            Check("ending B = 13750 MAJOR", b.total == 13750 && b.speed == 0 && b.combat == 750 && b.territory == 3000 && OzHighScores.Rank(b.total) == "MAJOR", $"{b.victory}+{b.speed}+{b.combat}+{b.territory}={b.total} {OzHighScores.Rank(b.total)}");
            Check("ending C = 1607 PRIVATE", c.total == 1607 && c.victory == 0 && c.speed == 0 && c.combat == 750 && c.territory == 857 && OzHighScores.Rank(c.total) == "PRIVATE", $"{c.victory}+{c.speed}+{c.combat}+{c.territory}={c.total} {OzHighScores.Rank(c.total)}");
            Check("rank steps", OzHighScores.Rank(1999) == "PRIVATE" && OzHighScores.Rank(2000) == "CORPORAL" && OzHighScores.Rank(9999) == "SERGEANT MAJOR" && OzHighScores.Rank(10000) == "LIEUTENANT" &&
                                OzHighScores.Rank(21999) == "LIEUTENANT GENERAL" && OzHighScores.Rank(22000) == "GENERAL" && OzHighScores.Rank(0) == "PRIVATE",
                  $"{OzHighScores.Rank(1999)} {OzHighScores.Rank(2000)} {OzHighScores.Rank(9999)} {OzHighScores.Rank(10000)} {OzHighScores.Rank(21999)} {OzHighScores.Rank(22000)} {OzHighScores.Rank(0)}");
            Check("no fight, no zones = no points for them", OzHighScores.Score(true, 0, 0, 0, 0, 0).total == 19000, OzHighScores.Score(true, 0, 0, 0, 0, 0).total.ToString());

            // 2 + 3. the table, on a file of its own
            string tmp = Path.Combine(Application.temporaryCachePath, "oz_highscores_proof.json");
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                OzHighScores.UseFile(tmp);
                (int n, int score, int sec)[] rows =
                {
                    (1, 13750, 2000), (2, 19901, 754), (3, 1607, 400), (4, 15000, 900), (5, 15000, 800), (6, 15000, 900),
                    (7, 21000, 500), (8, 11000, 1500), (9, 12500, 1300), (10, 18000, 700), (11, 17000, 760), (12, 16000, 820),
                };
                var places = new StringBuilder();
                foreach (var r in rows) places.Append(OzHighScores.Submit("L02", GameDifficulty.Medium, new OzScoreEntry { kills = r.n, score = r.score, seconds = r.sec })).Append(' ');
                const string want = "E7:21000/500 E2:19901/754 E10:18000/700 E11:17000/760 E12:16000/820 E5:15000/800 E4:15000/900 E6:15000/900 E1:13750/2000 E9:12500/1300";
                string got = Line(OzHighScores.Top("L02", GameDifficulty.Medium));
                Check("12 entered, 10 kept, best first, ties by time then age", got == want, got);
                Check("places returned at entry", places.ToString() == "1 1 3 2 2 4 1 7 7 3 4 5 ", places.ToString());
                int late = OzHighScores.Submit("L02", GameDifficulty.Medium, new OzScoreEntry { kills = 13, score = 12000, seconds = 100 });
                Check("a 13th, weaker result is refused", late == 0 && Line(OzHighScores.Top("L02", GameDifficulty.Medium)) == want, "place " + late);
                Check("other level / difficulty: own empty tables", OzHighScores.Top("L17", GameDifficulty.Medium).Count == 0 && OzHighScores.Top("L02", GameDifficulty.Hard).Count == 0 && OzHighScores.Best("L17", GameDifficulty.Easy) == null,
                      $"{OzHighScores.Top("L17", GameDifficulty.Medium).Count} {OzHighScores.Top("L02", GameDifficulty.Hard).Count}");
                OzHighScores.UseFile(tmp);                                                                 // forget the tables in memory: read the file again
                string again = Line(OzHighScores.Top("L02", GameDifficulty.Medium));
                Check("the file holds the same table after a new read", File.Exists(tmp) && again == want, again);
                Check("best entry", OzHighScores.Best("L02", GameDifficulty.Medium) != null && OzHighScores.Best("L02", GameDifficulty.Medium).kills == 7, "E" + OzHighScores.Best("L02", GameDifficulty.Medium)?.kills);
            }
            finally
            {
                OzHighScores.UseFile(null);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (System.Exception) { }
            }

            // 4. a table file written by an earlier build: a name with every entry
            string older = Path.Combine(Application.temporaryCachePath, "oz_highscores_proof_older.json");
            try
            {
                File.WriteAllText(older, "{\"version\":1,\"tables\":[{\"level\":\"L02\",\"difficulty\":\"" + OpenZDifficulty.Label(GameDifficulty.Medium) + "\",\"entries\":["
                                         + "{\"name\":\"AAA\",\"score\":19901,\"seconds\":754,\"kills\":42,\"losses\":18,\"built\":30,\"zones\":6,\"zonesTotal\":7},"
                                         + "{\"name\":\"\",\"score\":13750,\"seconds\":2000,\"kills\":10,\"losses\":30,\"built\":12,\"zones\":14,\"zonesTotal\":14}]}]}");
                OzHighScores.UseFile(older);
                var had = OzHighScores.Top("L02", GameDifficulty.Medium); int hadCount = had.Count;
                bool read = hadCount == 2 && had[0].score == 19901 && had[0].seconds == 754 && had[0].kills == 42 && had[0].losses == 18 && had[0].built == 30 && had[0].zones == 6 && had[0].zonesTotal == 7 && had[1].score == 13750;
                int place = OzHighScores.Submit("L02", GameDifficulty.Medium, new OzScoreEntry { score = 15000, seconds = 900 });   // the next write of the file
                string written = File.ReadAllText(older);
                Check("an older file with names is read; the next write holds no name", read && place == 2 && !written.Contains("name") && written.Contains("19901") && written.Contains("15000") && written.Contains("13750"),
                      $"{hadCount} entries read, the new result at place {place}, the written file {(written.Contains("name") ? "still holds a name" : "holds no name")}");
            }
            finally
            {
                OzHighScores.UseFile(null);
                try { if (File.Exists(older)) File.Delete(older); } catch (System.Exception) { }
            }

            return $"[OpenZ high scores proof] {ok}/{checks} checks. {(ok == checks ? "PASS" : "FAIL")}{bad}";
        }
    }
}
