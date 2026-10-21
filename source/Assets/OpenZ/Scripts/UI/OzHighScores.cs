using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>One line of a level's table: the result itself. It carries no name.</summary>
    [Serializable]
    public class OzScoreEntry
    {
        public int score, seconds, kills, losses, built, zones, zonesTotal;
    }

    /// <summary>The top results of one level on one difficulty, best first.</summary>
    [Serializable]
    public class OzScoreTable
    {
        public string level, difficulty;
        public List<OzScoreEntry> entries = new List<OzScoreEntry>();
    }

    [Serializable]
    public class OzScoreFile
    {
        public int version = 1;
        public List<OzScoreTable> tables = new List<OzScoreTable>();
    }

    /// <summary>The four parts of a score and their sum.</summary>
    public struct OzScoreParts { public int victory, speed, combat, territory, total; }

    /// <summary>
    /// Level high scores and ranks.
    ///
    /// The open engine port has no score, rank or table (its end of game only counts which team still has units,
    /// zserver.cpp:325-367, and adds a win or a loss to an online account, zserver.cpp:2885-2925), so the count is OpenZ's own,
    /// made from the numbers the end screen already shows:
    ///
    ///     SCORE = VICTORY 10 000                                   (a won level only)
    ///           + SPEED   9 000 - 5 for every second of mission time, never below 0   (a won level only; 0 from 30:00 on)
    ///           + COMBAT  3 000 x kills / (kills + losses)         (0 when nothing was killed or lost)
    ///           + TERRITORY 3 000 x zones held at the end / zones of the map
    ///
    /// Whole numbers, each part rounded to the nearest point. Every part has a ceiling (25 000 in all), so no part can be farmed
    /// by drawing a match out, and any won level (10 000 or more) stands above any lost one (6 000 at most).
    /// Ranks by score, fourteen army ranks in three groups. Enlisted (a defeat never leaves them): PRIVATE, CORPORAL 2 000,
    /// SERGEANT 4 000, STAFF SERGEANT 6 000, SERGEANT MAJOR 8 000. Officers (every victory): LIEUTENANT 10 000, CAPTAIN 11 500,
    /// MAJOR 13 000, LIEUTENANT COLONEL 14 500, COLONEL 16 000. Generals: BRIGADIER GENERAL 17 500, MAJOR GENERAL 19 000,
    /// LIEUTENANT GENERAL 20 500, GENERAL 22 000. The rank is always worked out from the score, never stored.
    ///
    /// The table: the 10 best victories of each level on each difficulty, best first; equal scores: the shorter mission first,
    /// then the older entry. Kept in one small file next to the game's own saved data (Application.persistentDataPath /
    /// highscores.json), written when an entry is added. It never leaves the computer. An entry is the result only (score,
    /// mission time, units killed, lost and built, zones): no name is asked for, stored or shown. A file written by an earlier
    /// build, which holds a name with every entry, is read as it is; the name is not kept and is gone at the next write.
    /// View side only: the sim never reads any of it.
    /// </summary>
    public static class OzHighScores
    {
        public const int VictoryPoints = 10000, SpeedMax = 9000, SpeedPerSecond = 5, CombatMax = 3000, TerritoryMax = 3000;
        public const int TableSize = 10;
        public const string FileName = "highscores.json";

        // ------------------------------------------------------------------ the count
        public static OzScoreParts Score(bool victory, double missionSeconds, int kills, int losses, int zonesHeld, int zonesTotal)
        {
            OzScoreParts p = default;
            int s = (int)Math.Max(0.0, Math.Min(1000000.0, Math.Floor(missionSeconds)));
            kills = Math.Max(0, kills); losses = Math.Max(0, losses);
            p.victory = victory ? VictoryPoints : 0;
            p.speed = victory ? Math.Max(0, SpeedMax - SpeedPerSecond * s) : 0;
            int fights = kills + losses;
            p.combat = fights > 0 ? (int)(((long)CombatMax * kills + fights / 2) / fights) : 0;
            zonesHeld = Math.Max(0, Math.Min(zonesHeld, zonesTotal));
            p.territory = zonesTotal > 0 ? (TerritoryMax * zonesHeld + zonesTotal / 2) / zonesTotal : 0;
            p.total = p.victory + p.speed + p.combat + p.territory;
            return p;
        }

        static readonly (int min, string name)[] Ladder =
        {
            (22000, "GENERAL"), (20500, "LIEUTENANT GENERAL"), (19000, "MAJOR GENERAL"), (17500, "BRIGADIER GENERAL"),
            (16000, "COLONEL"), (14500, "LIEUTENANT COLONEL"), (13000, "MAJOR"), (11500, "CAPTAIN"), (10000, "LIEUTENANT"),
            (8000, "SERGEANT MAJOR"), (6000, "STAFF SERGEANT"), (4000, "SERGEANT"), (2000, "CORPORAL"), (0, "PRIVATE"),
        };

        public static string Rank(int score)
        {
            foreach (var r in Ladder) if (score >= r.min) return r.name;
            return Ladder[Ladder.Length - 1].name;
        }

        /// <summary>The ladder for a tooltip, one line a group: the enlisted ranks (under the victory points), the officers, the generals.</summary>
        public static string LadderTip()
        {
            var sb = new StringBuilder(); int group = -1;
            for (int i = Ladder.Length - 1; i >= 0; i--)
            {
                int g = Ladder[i].min >= GeneralsFrom ? 2 : Ladder[i].min >= VictoryPoints ? 1 : 0;
                if (g != group) { if (sb.Length > 0) sb.Append((char)10); sb.Append(g == 0 ? "Enlisted: " : g == 1 ? "Officers (every victory): " : "Generals: "); group = g; }
                else sb.Append(" · ");
                sb.Append(Ladder[i].name); if (Ladder[i].min > 0) sb.Append(' ').Append(Group(Ladder[i].min));
            }
            return sb.ToString();
        }
        /// <summary>The first score of the generals' group (BRIGADIER GENERAL).</summary>
        public const int GeneralsFrom = 17500;

        /// <summary>The ladder as one line (the deviation row): "CORPORAL 2 000 · SERGEANT 4 000 · ...".</summary>
        public static string LadderLine()
        {
            var sb = new StringBuilder();
            for (int i = Ladder.Length - 2; i >= 0; i--) { if (sb.Length > 0) sb.Append(" · "); sb.Append(Ladder[i].name).Append(' ').Append(Group(Ladder[i].min)); }
            return sb.ToString();
        }

        /// <summary>19901 -> "19 901".</summary>
        public static string Group(int n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

        // ------------------------------------------------------------------ a match that does not count
        /// <summary>Set while something in this match keeps it out of the table (the testing overlay put units on the map); the
        /// text is shown on the end screen. Cleared when a scene is loaded.</summary>
        public static string Unranked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Unranked = null; data = null; pathOverride = null;
            SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene;
        }
        static void OnScene(Scene s, LoadSceneMode m) { Unranked = null; }

        // ------------------------------------------------------------------ the file
        static OzScoreFile data; static string pathOverride;
        /// <summary>Bumped whenever a table changes (the level cards refresh on it).</summary>
        public static int Version { get; private set; }
        /// <summary>The editor's own table file, in the same folder as the game's. The editor and a game installed on the same
        /// computer share that folder: with a file of its own, nothing that is tried, staged or forced while the game is worked on
        /// reaches the table of the installed game.</summary>
        public const string EditorFileName = "highscores_editor.json";
        public static string FilePath => pathOverride ?? Path.Combine(Application.persistentDataPath, Application.isEditor ? EditorFileName : FileName);

        /// <summary>Checks: work on another file (null = back to the player's own). The tables are read again.</summary>
        public static void UseFile(string path) { pathOverride = path; data = null; Version++; }

        static OzScoreFile Data
        {
            get
            {
                if (data != null) return data;
                var d = new OzScoreFile();
                string p = FilePath;
                try
                {
                    if (File.Exists(p))
                    {
                        var read = JsonUtility.FromJson<OzScoreFile>(File.ReadAllText(p, Encoding.UTF8));
                        if (read != null && read.tables != null) d = read;
                    }
                }
                catch (Exception e)
                {   // an unreadable file is kept under another name and a new table starts: the end screen never fails on it
                    Debug.LogWarning("[OpenZ Scores] the table file could not be read (" + e.Message + "): it is kept as " + FileName + ".bad and a new table starts");
                    try { if (File.Exists(p)) { string badPath = p + ".bad"; if (File.Exists(badPath)) File.Delete(badPath); File.Move(p, badPath); } } catch (Exception) { }
                    d = new OzScoreFile();
                }
                d.tables.RemoveAll(t => t == null || string.IsNullOrEmpty(t.level) || string.IsNullOrEmpty(t.difficulty));
                foreach (var t in d.tables)
                {
                    if (t.entries == null) t.entries = new List<OzScoreEntry>();
                    t.entries.RemoveAll(x => x == null);
                    var sorted = new List<OzScoreEntry>(t.entries.Count);                                   // best first; equal entries keep the file's order (a stable pass)
                    foreach (var x in t.entries) { int at = sorted.Count; while (at > 0 && Better(x, sorted[at - 1])) at--; sorted.Insert(at, x); }
                    t.entries = sorted;
                    if (t.entries.Count > TableSize) t.entries.RemoveRange(TableSize, t.entries.Count - TableSize);
                }
                data = d;
                return data;
            }
        }

        static int Compare(OzScoreEntry a, OzScoreEntry b) => a.score != b.score ? b.score.CompareTo(a.score) : a.seconds.CompareTo(b.seconds);
        static bool Better(OzScoreEntry a, OzScoreEntry b) => Compare(a, b) < 0;

        static OzScoreTable Table(string level, GameDifficulty d, bool create)
        {
            string diff = OpenZDifficulty.Label(d);
            foreach (var t in Data.tables) if (t.level == level && t.difficulty == diff) return t;
            if (!create) return null;
            var made = new OzScoreTable { level = level, difficulty = diff };
            Data.tables.Add(made);
            return made;
        }

        static readonly List<OzScoreEntry> none = new List<OzScoreEntry>();
        /// <summary>The table of a level on a difficulty, best first (0 .. 10 entries).</summary>
        public static IReadOnlyList<OzScoreEntry> Top(string level, GameDifficulty d) { var t = Table(level, d, false); return t != null ? t.entries : none; }
        /// <summary>The best entry, or null when the level was never won on that difficulty.</summary>
        public static OzScoreEntry Best(string level, GameDifficulty d) { var t = Table(level, d, false); return t != null && t.entries.Count > 0 ? t.entries[0] : null; }

        /// <summary>The title of a level's table: "TOP 10 · L02 · MEDIUM".</summary>
        public static string TableTitle(string level, GameDifficulty d) => "TOP " + TableSize + " · " + level + " · " + OpenZDifficulty.Label(d);

        /// <summary>
        /// time); the entry `mark` (the result of the match just played) gets an arrow. Empty text = no entry yet. Shown by the
        /// TOP 10 tooltip of the end screen's strip.</summary>
        public static string TableLines(string level, GameDifficulty d, OzScoreEntry mark = null)
        {
            var list = Top(level, d);
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (i > 0) sb.Append('\n');
                sb.Append(i + 1).Append(".  ").Append(Rank(e.score)).Append("  ").Append(Group(e.score)).Append("  ").Append(UnitInfo.Duration(e.seconds));
                if (mark != null && e == mark) sb.Append("  ◀");
            }
            return sb.ToString();
        }

        /// <summary>Adds a result. Returns its place 1 .. 10, or 0 when it did not make the table. Written at once.</summary>
        public static int Submit(string level, GameDifficulty d, OzScoreEntry e)
        {
            if (e == null || string.IsNullOrEmpty(level)) return 0;
            var t = Table(level, d, true);
            int at = 0;
            while (at < t.entries.Count && !Better(e, t.entries[at])) at++;                              // an equal older entry stays above
            if (at >= TableSize) return 0;
            t.entries.Insert(at, e);
            if (t.entries.Count > TableSize) t.entries.RemoveRange(TableSize, t.entries.Count - TableSize);
            Version++;
            Save();
            return at + 1;
        }

        /// <summary>Write the tables (a new entry). False = the file could not be written (logged; the tables stay in memory).</summary>
        public static bool Save()
        {
            try
            {
                string p = FilePath, tmp = p + ".tmp";
                string dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true), new UTF8Encoding(false));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
                Version++;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OpenZ Scores] the table file could not be written (" + e.Message + "); the table stays in memory for this session");
                return false;
            }
        }
    }
}
