// OpenZ (GPL-3.0)
//  - Every transmission (the level briefings, the victory / defeat punchlines, the idle barb, the nuke transmission, the menu
//    greeting) opens on the backs of the two grunts in his HQ and pushes in to him (ZodStage.BeginBoys, ZodStage.Boys.cs): the
//    push starts `boysLead` s after his first word and lasts `boysPushSeconds` (0.8). The tiny level-select quip window (136 x 102) keeps
//    its close-up. Per-window switches below.
//  - Play checks (an editor script while playing):
//      return OpenZ.ZodBriefing.DebugBoys();                        state of the boys + the current opening
//      return OpenZ.ZodBriefing.DebugBoysHold(true);                hold the opening frame (no push) for a screenshot; false = go on
using System;
using System.IO;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        [Header("zod_briefings_v3: the boys (opening shot)")]
        [Tooltip("Level briefings open on the boys' backs")] public bool boysBriefing = true;
        [Tooltip("Victory / defeat punchlines open on the boys")] public bool boysEndScreen = true;
        [Tooltip("In-game transmissions (idle barb, nuke) open on the boys")] public bool boysInGame = true;
        [Tooltip("Main menu greeting opens on the boys")] public bool boysMenu = true;
        [Tooltip("Seconds after his first word before the push-in starts (the backs read while he calls them)")] public float boysLead = 0.30f;
        [Tooltip("Push-in duration (s), eased (smootherstep)")] public float boysPushSeconds = 0.80f;
        [Tooltip("A briefing: Alan has left the opening picture this long before the first word (s)")] public float alanOutBeforeWord = 0.15f;

        bool BoysFor(Kind kind, ZodLine line)
        {
            switch (kind)
            {
                case Kind.Briefing: return boysBriefing;
                case Kind.Victory: case Kind.Defeat: return boysEndScreen;
                case Kind.Idle: case Kind.Nuke: return boysInGame;
                case Kind.Greeting: return boysMenu;
                case Kind.Quip: return false;                                                       // the 136 x 102 quip window: close-up only
                case Kind.Debug: return line == null || line.group != "map_select";
            }
            return false;
        }

        /// <summary>StartPerf: the opening on the boys (after SetFraming + SnapFraming).</summary>
        void StartBoys(Kind kind, ZodLine line, float preroll)
        {
            if (!stage) return;
            bool want = BoysFor(kind, line);
            stage.BoysSfxGain = VoiceGain();                                                          // the boys' step / servo on Zod's voice bus level
            bool on = want && stage.BeginBoys(preroll + boysLead, boysPushSeconds);
            if (on && kind == Kind.Briefing) stage.AlanOutBefore(preroll - alanOutBeforeWord);     // a briefing: Alan is out of the picture before the first word
            if (want && !on) Debug.Log("[ZodCameo] " + (line != null ? line.id : "?") + ": no boys (" + stage.BoysSource + "), opening on Zod");
        }

        /// <summary>Play check: the boys and the opening of the current line.</summary>
        public static string DebugBoys()
        {
            if (!I) return "no ZodBriefing in this scene";
            if (!I.stage) return "stage not built yet";
            return I.stage.BoysStatus() + (I.perf != null ? $" | line {I.perf.id} t={I.perf.t:F2}" : " | no line playing");
        }

        /// <summary>Play check: hold the opening frame (true) for a screenshot, release it (false).</summary>
        public static string DebugBoysHold(bool hold)
        {
            if (!I || !I.stage) return "no stage";
            I.stage.boysHoldForProof = hold;
            return "boys hold " + hold + " | " + I.stage.BoysStatus();
        }

        /// <summary>Play check: saves the comms feed (the stage camera's RenderTexture) as a PNG.</summary>
        public static string DebugSaveFeed(string path)
        {
            if (!I || !I.stage || !I.stage.Feed) return "no feed";
            var rt = I.stage.Feed; var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                return $"saved {rt.width}x{rt.height} -> {path} (boys k {I.stage.BoysK:F2})";
            }
            catch (Exception e) { return "save failed: " + e.Message; }
            finally { RenderTexture.active = prev; }
        }
    }
}
