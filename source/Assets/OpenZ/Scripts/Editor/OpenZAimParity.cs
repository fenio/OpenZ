using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// combatview parity row: Zod has no turn-before-fire. zobject.cpp:2102-2213 (ProcessAttackDamage) never reads a
    /// direction; the turret / robot direction is a client-side snap (zvehicle.cpp:140-153, vlight.cpp:119-126,
    /// cgatling.cpp:193-197, zcannon.cpp:30-43, zrobot.cpp:139-143). So the OpenZ sim must fire on the very tick it engages
    /// (cooldown permitting) for every weapon, and the view (UnitAim) pre-aims and holds only the visual shot. This check
    /// runs the headless sim for every armed unit type against a target placed BEHIND it and writes
    /// </summary>
    public static partial class OpenZParity
    {
        [MenuItem("OpenZ/Run Aim Parity Check")]
        public static string RunAim()
        {
            var rows = new List<Row>();
            bool toughPrep = ZWorld.ToughPrepareDeviation; ZWorld.ToughPrepareDeviation = false;   // weapons_v2 DV11: the Zod rows run Zod exact
            bool heavyPair = ZWorld.HeavyPairDeviation; ZWorld.HeavyPairDeviation = false;         // vehicles_nb_v1 DV30: the Zod rows run Zod exact
            bool cfKnock = ZWorld.KnockHoldDeviation, cfRocket = ZWorld.RocketSpeedDeviation; ZWorld.KnockHoldDeviation = ZWorld.RocketSpeedDeviation = false;
            bool fom = ZWorld.FireOnMoveDeviation; ZWorld.FireOnMoveDeviation = false;
            try { AimTests(rows); } finally { ZWorld.ToughPrepareDeviation = toughPrep; ZWorld.HeavyPairDeviation = heavyPair; ZWorld.KnockHoldDeviation = cfKnock; ZWorld.RocketSpeedDeviation = cfRocket; ZWorld.FireOnMoveDeviation = fom; }
            ToughPrepareAimRow(rows);                                                            // weapons_v2: DV11 measured on its own row
            var sb = new StringBuilder();
            int pass = rows.Count(r => r.pass);
            sb.AppendLine("# OpenZ aim parity (combatview)");
            sb.AppendLine();
            sb.AppendLine($"Generated {System.DateTime.Now:yyyy-MM-dd HH:mm} by `OpenZ/Run Aim Parity Check`. **{pass}/{rows.Count} PASS**. " +
                          "Zod fires on its cooldown only; turning is a client visual, so first shot = engage tick.");
            sb.AppendLine();
            sb.AppendLine("| Test | Expected (Zod) | Measured (OpenZ) | Result | Note |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var r in rows) sb.AppendLine($"| {r.test} | {r.expected} | {r.measured} | {(r.pass ? "PASS" : "**FAIL**")} | {r.note} |");
            var path = Path.Combine(OpenZPaths.Records, "aim_parity.md");
            File.WriteAllText(path, sb.ToString());
            string summary = $"aim parity: {pass}/{rows.Count} PASS";
            Debug.Log(summary + " -> " + path);
            return summary;
        }

        /// <summary>combatview visual check in play mode: every weapon type turns onto targets behind it
        /// </summary>
        [MenuItem("OpenZ/Stage Weapon Line-Up (combatview)")]
        public static string StageLineUp()
        {
            if (!Application.isPlaying) return "enter play mode on L02 (or L17) first";
            var c = CombatViewCheck.Begin();
            return c ? $"staged; report in {CombatViewCheck.Duration:F0} s -> work/records/combatview_check.md" : "no running ZGame";
        }

        static void AimTests(List<Row> rows)
        {
            var units = new[]
            {
                ("robot", "grunt"), ("robot", "psycho"), ("robot", "sniper"), ("robot", "tough"), ("robot", "pyro"), ("robot", "laser"),
                ("vehicle", "jeep"), ("vehicle", "light"), ("vehicle", "medium"), ("vehicle", "heavy"), ("vehicle", "missile_launcher"),
                ("cannon", "gatling"), ("cannon", "gun"), ("cannon", "howitzer"), ("cannon", "missile_cannon")
            };
            foreach (var (cat, type) in units)
            {
                var w = Legacy(21);
                var a = cat == "robot" ? w.CreateRobotGroup(type, 56 * 16, 14 * 16, Team.Red, 1) : w.CreateObject(cat, type, 56 * 16, 14 * 16, Team.Red);
                var t = Dummy(w, "robot", "grunt", 51, 14, Team.Blue);                             // 5 tiles to the west: behind any rest heading
                double engaged = -1, fired = -1;
                for (int i = 0; i < 1000 && fired < 0; i++)                                      // 10 s at 100 Hz
                {
                    w.Tick(ZConst.TICK);
                    if (engaged < 0 && a.AttackObject != null) engaged = w.Time;
                    foreach (var e in w.Events)
                        if (e.objId == a.Id && (e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired)) { fired = w.Time; break; }
                    w.Events.Clear();
                }
                bool ok = engaged >= 0 && fired >= 0 && fired - engaged <= ZConst.TICK + 1e-9;
                rows.Add(new Row
                {
                    test = $"{type}: first shot after engaging a target behind it",
                    expected = "same tick (no turn delay)",
                    measured = engaged < 0 ? "never engaged" : fired < 0 ? "never fired" : $"{(fired - engaged) * 1000:F0} ms",
                    pass = ok,
                    note = "view: UnitAim pre-aims + holds only the visual shot"
                });
            }
        }
    }
}
