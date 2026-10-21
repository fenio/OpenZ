using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 checks of the comms window's radio calls (menu OpenZ/V1.1/Radio Calls ...): the window opens with its first opening
    /// call, as it always did; it closes with its first closing call in about half of the closings (the most common call) and with one of
    /// eight urgent radio transmissions in the rest, never the same one twice in a row; the seven mumbles without words stay out while
    /// OzRadioMumbles.UseMumbles is off (the default).
    /// <list type="bullet">
    /// <item>Calls and 400 draws (Edit or Play): every take loads (name, length, rate, channels, words); 200 draws with the switch off,
    /// then 200 with it on (the switch is put back). PASS = openings all the first call while off; closings: the first call 35-65 %
    /// and more often than any other call, every sign-off drawn, 0 mumbles while off, mumbles drawn while on, never a spoken sign-off
    /// at an opening, 0 repeats in a row.</item>
    /// <item>Sound log of the window's calls (Play): every comms_radio_open / comms_radio_close line since Play began; PASS = at least
    /// 6 calls, both edges seen, 0 calls on an old clip, 0 repeats in a row of a sign-off, the rules above for what each edge played.</item>
    /// <item>Mumbles on / off (Edit or Play): flips the switch for this session only (the default in the code stays off).</item>
    /// <item>Win now (Play): the enemy fort falls (the real chain to the ending and Zod's victory transmission).</item>
    /// </list>
    /// Test tools only: nothing is saved.
    /// </summary>
    public static class OzRadioMumblesCheck
    {
        const string M = "OpenZ/V1.1/Radio Calls ";

        [MenuItem(M + "Calls And 400 Draws (Edit or Play)")]
        static void Draws()
        {
            var sb = new StringBuilder("[OpenZ radio] the comms window's calls:\n");
            int missing = 0;
            for (int i = 0; i < OzRadioMumbles.Takes.Length; i++)
            {
                var c = OzRadioMumbles.Clip(i);
                string w = OzRadioMumbles.Words[i].Length > 0 ? "\"" + OzRadioMumbles.Words[i] + "\"" : "";
                if (!c) { missing++; sb.AppendLine($"  {i + 1,2}. {OzRadioMumbles.Takes[i],-20} MISSING"); continue; }
                sb.AppendLine($"  {i + 1,2}. {OzRadioMumbles.Takes[i],-20} {c.length:F3} s  {c.frequency} Hz  {c.channels} ch  {OzRadioMumbles.Kind(i),-20} {w}");
            }
            var state = Random.state; bool was = OzRadioMumbles.UseMumbles;
            Random.InitState(2716);
            bool pass = missing == 0;
            try
            {
                foreach (bool mumbles in new[] { false, true })
                {
                    OzRadioMumbles.UseMumbles = mumbles;
                    var openN = new int[OzRadioMumbles.Takes.Length]; var closeN = new int[OzRadioMumbles.Takes.Length];
                    int repeats = 0, nulls = 0, prevVariety = -1;
                    for (int n = 0; n < 200; n++)
                    {
                        bool open = n % 2 == 0;
                        var c = OzRadioMumbles.Pick(open ? "open" : "close");
                        if (!c) { nulls++; continue; }
                        int k = OzRadioMumbles.Last;
                        if (k >= OzRadioMumbles.CallsFrom) { if (k == prevVariety) repeats++; prevVariety = k; }
                        (open ? openN : closeN)[k]++;
                    }
                    int first = closeN[OzRadioMumbles.CloseFirst];
                    int otherMax = closeN.Where((x, k) => k != OzRadioMumbles.CloseFirst).Max();
                    int calls = Enumerable.Range(OzRadioMumbles.CallsFrom, OzRadioMumbles.CallsCount).Sum(k => closeN[k]);
                    int callsSeen = Enumerable.Range(OzRadioMumbles.CallsFrom, OzRadioMumbles.CallsCount).Count(k => closeN[k] > 0);
                    int mumN = Enumerable.Range(OzRadioMumbles.MumblesFrom, OzRadioMumbles.MumblesCount).Sum(k => closeN[k] + openN[k]);
                    int openFirst = openN[OzRadioMumbles.OpenFirst];
                    int spokenAtOpen = Enumerable.Range(OzRadioMumbles.CallsFrom, OzRadioMumbles.CallsCount).Sum(k => openN[k]) + openN[OzRadioMumbles.CloseFirst];
                    sb.AppendLine($"  switch {(mumbles ? "ON " : "OFF")}: 100 openings: first opening call {openFirst}, mumbles {Enumerable.Range(OzRadioMumbles.MumblesFrom, OzRadioMumbles.MumblesCount).Sum(k => openN[k])}, a closing call {spokenAtOpen}");
                    sb.AppendLine($"              100 closings: first closing call {first} (most of any other: {otherMax}), sign-offs {calls} ({string.Join(" ", Enumerable.Range(OzRadioMumbles.CallsFrom, OzRadioMumbles.CallsCount).Select(k => closeN[k]))}), mumbles {Enumerable.Range(OzRadioMumbles.MumblesFrom, OzRadioMumbles.MumblesCount).Sum(k => closeN[k])}");
                    sb.AppendLine($"              repeats in a row {repeats}, empty draws {nulls}");
                    bool ok = nulls == 0 && repeats == 0 && first >= 35 && first <= 65 && first > otherMax && callsSeen == OzRadioMumbles.CallsCount && spokenAtOpen == 0
                              && (mumbles ? mumN > 0 && openFirst >= 35 && openFirst <= 65 : mumN == 0 && openFirst == 100);
                    pass &= ok;
                }
            }
            finally { OzRadioMumbles.UseMumbles = was; Random.state = state; }
            bool burst = OzRadioMumbles.Pick("burst") == null;
            sb.AppendLine("  mid-line burst left alone: " + (burst ? "yes" : "NO"));
            pass &= burst;
            sb.Append(pass ? "PASS radio draws: 17 takes load; the first opening call opens; the first closing call is the most common closing (about 1 in 2), 8 radio calls share the rest, 0 repeats; mumbles only with the switch on"
                           : "FAIL radio draws (see above)");
            if (pass) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        [MenuItem(M + "Sound Log Of The Window's Calls (Play)")]
        static void Log()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ radio] enter Play first (main menu or a level), let Zod's window open and close a few times"); return; }
            var es = OzSoundLog.Since(0f).Where(e => e.key != null && e.key.StartsWith("comms_radio_")).ToList();
            var sb = new StringBuilder($"[OpenZ radio] the comms window's calls since Play began: {es.Count} (mumbles switch {(OzRadioMumbles.UseMumbles ? "ON" : "OFF")})\n");
            int repeats = 0, old = 0, opens = 0, closes = 0, wrong = 0, firstClose = 0, signOffs = 0; string prevVariety = null;
            foreach (var e in es)
            {
                bool open = e.key == "comms_radio_open";
                if (open) opens++; else closes++;
                bool variety = e.clip != null && (e.clip.StartsWith("comms_mumble_urgent_") || e.clip.StartsWith("comms_radio_"));
                bool rep = variety && e.clip == prevVariety;
                if (variety) prevVariety = e.clip;
                if (rep) repeats++;
                if (e.outcome == null || e.outcome.StartsWith("old clip")) old++;
                bool mumble = e.clip != null && e.clip.StartsWith("comms_radio_");
                bool bad = open ? !(e.clip == "comms_static_open" || (mumble && OzRadioMumbles.UseMumbles))
                                : !(e.clip == "comms_static_close" || (e.clip != null && e.clip.StartsWith("comms_mumble_urgent_")) || (mumble && OzRadioMumbles.UseMumbles));
                if (bad) wrong++;
                if (!open && e.clip == "comms_static_close") firstClose++;
                if (!open && e.clip != null && e.clip.StartsWith("comms_mumble_urgent_")) signOffs++;
                sb.AppendLine($"  {e.t,8:F2}  {e.key,-18} {e.clip ?? "-",-20} {e.outcome,-22} vol {e.vol:F3}{(rep ? "   REPEAT" : "")}{(bad ? "   NOT EXPECTED HERE" : "")}");
            }
            sb.AppendLine($"  openings {opens}, closings {closes} (first closing call {firstClose}, sign-offs {signOffs}), calls on an old clip {old}, repeats in a row {repeats}, unexpected {wrong}");
            bool pass = es.Count >= 6 && opens > 0 && closes > 0 && old == 0 && repeats == 0 && wrong == 0;
            sb.Append(pass ? $"PASS radio sound log: {es.Count} calls, each edge as wanted" : "FAIL radio sound log (at least 6 calls, both edges, 0 old clips, 0 repeats, 0 unexpected wanted)");
            if (pass) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        [MenuItem(M + "Mumbles On Or Off For This Session (Edit or Play)")]
        static void Switch()
        {
            OzRadioMumbles.UseMumbles = !OzRadioMumbles.UseMumbles;
            Debug.Log("[OpenZ radio] the seven mumbles are now " + (OzRadioMumbles.UseMumbles ? "IN the draw (extra variety)" : "OUT of the draw (the default)") + " for this session; the default in the code is unchanged");
        }

        [MenuItem(M + "Win Now: The Enemy Fort Falls (Play)")]
        static void Win()
        {
            if (!Application.isPlaying || !ZGame.I) { Debug.LogWarning("[OpenZ radio] enter Play first (a level with an enemy fort)"); return; }
            int n = OpenZVictory.DebugFortFalls(true);
            Debug.Log(n > 0 ? "[OpenZ radio] the enemy fort falls: the ending and Zod's victory window follow (its open and close calls go into the sound log)" : "[OpenZ radio] no enemy fort standing");
        }
    }
}
