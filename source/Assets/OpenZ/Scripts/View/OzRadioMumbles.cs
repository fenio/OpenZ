using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The commander's comms window opens and closes with a short radio call. ZodBriefing.PlayStatic asks here for "open" and "close"
    /// at every transmission: the menu greeting, the level-card remarks, the level briefing and the victory / defeat / nuke endings.
    /// - Opening: the first opening call comms_static_open, as it always was.
    /// - Closing: the first closing call comms_static_close stays the most common call (FirstCallShare of the closings, 1 in 2). The
    ///   other closings draw one of eight urgent radio transmissions with mumbled words, comms_mumble_urgent_01 .. _08, from a
    ///   shuffle bag: every call once before any comes back, never the same call twice in a row. The five spoken sign-offs
    ///   comms_call_a .. comms_call_e stay in the folder, out of the draw.
    /// - The seven radio mumbles without words (comms_radio_a .. comms_radio_g) stay in the folder, out of the draw while UseMumbles is
    ///   off (the default). With it on they join the closings' bag, and they take the other half of the openings (a spoken sign-off
    ///   never opens the window).
    /// Every call: the radio band, static and squelch, 0.80-1.50 s, at the first calls'
    /// loudness, so the window's own gain (ZodBriefing.staticEdgeDb) is unchanged. The mid-line static bursts keep their own clips.
    /// Sound log: source ZodCameo, key comms_radio_open / comms_radio_close, the call's name, its kind, the volume.
    /// </summary>
    public static class OzRadioMumbles
    {
        public const string Folder = "OpenZ/Zod/Sfx/";
        public const int CloseFirst = 0, OpenFirst = 1, CallsFrom = 2, CallsCount = 8, MumblesFrom = 10, MumblesCount = 7;
        public static readonly string[] Takes =
        {
            "comms_static_close", "comms_static_open",
            "comms_mumble_urgent_01", "comms_mumble_urgent_02", "comms_mumble_urgent_03", "comms_mumble_urgent_04",
            "comms_mumble_urgent_05", "comms_mumble_urgent_06", "comms_mumble_urgent_07", "comms_mumble_urgent_08",
            "comms_radio_a", "comms_radio_b", "comms_radio_c", "comms_radio_d", "comms_radio_e", "comms_radio_f", "comms_radio_g",
        };
        /// <summary>What each spoken call says (empty: no words, or the first calls).</summary>
        public static readonly string[] Words = { "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "" };

        /// <summary>Extra variety: the seven mumbles join the draw. Off by default.</summary>
        public static bool UseMumbles = false;
        /// <summary>Share of the closings that play the first closing call; while the mumbles are on, also the share of the openings
        /// that play the first opening call.</summary>
        public static float FirstCallShare = 0.5f;

        /// <summary>Checks: calls drawn since the game started, and draws of a spoken call or mumble that repeated the one before it
        /// (must stay 0; the first calls may follow themselves, as they always did).</summary>
        public static int Picks, Repeats;
        /// <summary>Checks: the last call drawn (index into Takes, -1 = none yet).</summary>
        public static int Last { get; private set; } = -1;

        static AudioClip[] clips;
        static int lastVariety = -1;
        static bool bagWithMumbles;
        static readonly List<int> bag = new List<int>();

        /// <summary>The window's opening and closing radio calls are drawn here; any other static (the mid-line bursts) is not.</summary>
        public static bool Owns(string edge) => edge == "open" || edge == "close";

        /// <summary>The loaded take i (null when its clip is missing).</summary>
        public static AudioClip Clip(int i)
        {
            Load();
            return i >= 0 && i < clips.Length ? clips[i] : null;
        }

        /// <summary>The kind of take i, as the sound log shows it.</summary>
        public static string Kind(int i) =>
            i == CloseFirst ? "first closing call" : i == OpenFirst ? "first opening call"
            : i >= CallsFrom && i < MumblesFrom ? "radio call " + (i - CallsFrom + 1) + "/" + CallsCount
            : i >= MumblesFrom && i < Takes.Length ? "mumble " + (i - MumblesFrom + 1) + "/" + MumblesCount : "?";

        static void Load()
        {
            if (clips != null) return;
            clips = new AudioClip[Takes.Length];
            for (int i = 0; i < Takes.Length; i++)
            {
                clips[i] = Resources.Load<AudioClip>(Folder + Takes[i]);
                if (!clips[i]) Debug.LogWarning("[OzRadioMumbles] take missing: Resources/" + Folder + Takes[i]);
            }
        }

        /// <summary>The window's "open" / "close" call; null for anything else or when no take loads (the caller then plays its old clip).</summary>
        public static AudioClip Pick(string edge)
        {
            if (!Owns(edge)) return null;
            Load();
            bool open = edge == "open";
            int k = -1;
            if (UnityEngine.Random.value >= FirstCallShare && (!open || UseMumbles)) k = open ? Mumble() : Draw();
            if (k < 0) k = open ? OpenFirst : CloseFirst;
            if (!clips[k]) return null;
            if (k >= CallsFrom) { if (k == lastVariety) Repeats++; lastVariety = k; }
            Last = k; Picks++;
            return clips[k];
        }

        /// <summary>A closing's spoken call (or mumble, with the switch on) from the shuffle bag; -1 when none loads.</summary>
        static int Draw()
        {
            if (bagWithMumbles != UseMumbles) { bag.Clear(); bagWithMumbles = UseMumbles; }          // the switch changed: a new round with the new set
            if (bag.Count == 0 || (bag.Count == 1 && bag[0] == lastVariety)) Refill();               // the last one left was just heard (an opening's mumble): the next round joins
            if (bag.Count == 0) return -1;
            int at = bag.Count > 1 && bag[0] == lastVariety ? 1 : 0;                                   // never the call just heard
            int k = bag[at]; bag.RemoveAt(at);
            return k;
        }

        static void Refill()
        {
            var round = new List<int>();
            for (int i = CallsFrom; i < Takes.Length; i++) if (clips[i] && (i < MumblesFrom || UseMumbles)) round.Add(i);
            for (int i = round.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); int t = round[i]; round[i] = round[j]; round[j] = t; }
            if (round.Count > 1 && round[0] == lastVariety) { round.RemoveAt(0); round.Add(lastVariety); }   // a new round never starts with the call just heard
            bag.AddRange(round);
        }

        /// <summary>An opening's mumble (switch on only): any loaded mumble but the one heard last; -1 when none loads.</summary>
        static int Mumble()
        {
            var ok = new List<int>();
            for (int i = MumblesFrom; i < Takes.Length; i++) if (clips[i] && i != lastVariety) ok.Add(i);
            return ok.Count > 0 ? ok[UnityEngine.Random.Range(0, ok.Count)] : -1;
        }

        /// <summary>One sound log line for a call that started.</summary>
        public static void Log(string edge, AudioClip clip, float vol)
        {
            bool ours = clip && Last >= 0 && clip.name == Takes[Last];
            OzSoundLog.Add("ZodCameo", "comms_radio_" + edge, clip ? clip.name : null, ours ? Kind(Last) : "old clip (no take loaded)", vol);
        }
    }
}
