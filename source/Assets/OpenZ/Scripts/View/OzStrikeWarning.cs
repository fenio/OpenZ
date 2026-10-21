using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// DV107 enemy strike groups: the announcer warns the player when an enemy strike group starts to march, if the player owns a
    /// radar (the rules: OzStrikeWarningRule.cs). Reads the computers' strike group records (ZBot.StrikeEvents) and the player's
    /// buildings; says the line through the announcer's queue (AudioDirector.Announce, event ann_strike_group, the exact line by
    /// its words), so the ANNOUNCER volume and the ANNOUNCER CHATTER setting apply as to every other announcer line (MINIMAL keeps
    /// only the critical lines: there the warning is silent). View only: writes nothing back into the match. Attached to the game
    /// object at every level start, like the other view layers.
    /// </summary>
    public sealed class OzStrikeWarning : MonoBehaviour
    {
        public const string EventId = "ann_strike_group";
        /// <summary>The words of the twenty lines, exactly as the sound event's clips carry them.</summary>
        public static readonly string[] Lines =
        {
            "Warning. A big hostile strike group detected.",
            "Warning. A massive hostile force is approaching.",
            "Warning. Enemy strike group on the move.",
            "Alert. A large enemy force is heading our way.",
            "Warning. Hostile strike group advancing.",
            "Radar contact. A big enemy group is moving out.",
            "Warning. A big enemy attack group is on its way.",
            "Heads up. A hostile strike group is coming.",
            "Radar alert. Massive enemy force incoming.",
            "Warning. Many hostiles approaching together.",
            "Alert. Enemy strike force spotted on radar.",
            "Warning. A strong enemy group is closing in.",
            "Attention. A hostile army is marching toward us.",
            "Warning. Big enemy push detected.",
            "Radar shows a large hostile group on the move.",
            "Warning. Enemy strike group inbound.",
            "Alert. Hostile forces advancing in strength.",
            "Get ready. A big enemy force is approaching.",
            "Warning. A large hostile column is moving in.",
            "Brace yourselves. Enemy strike group incoming.",
        };

        public static bool AutoAttach = true;
        public static OzStrikeWarning I { get; private set; }
        /// <summary>The rule of this level (counters for a Play check).</summary>
        public OzStrikeWarningRule Rule { get; private set; }

        ZGame game; ZWorld world;
        readonly Dictionary<ZBot, int> seen = new Dictionary<ZBot, int>();

        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => Attach(); }
            Attach();
        }
        public static void Attach()
        {
            if (!AutoAttach || !ZGame.I || ZGame.I.GetComponent<OzStrikeWarning>()) return;
            ZGame.I.gameObject.AddComponent<OzStrikeWarning>();
        }

        void Awake() { I = this; Rule = new OzStrikeWarningRule(Lines.Length, System.Environment.TickCount); }
        void OnDestroy() { if (I == this) I = null; }

        /// <summary>The human player owns a radar now (a standing radar building of his team).</summary>
        public static bool OwnsRadar(ZWorld w, Team t)
        {
            foreach (var o in w.Objects) if (o.Kind == ZKind.Building && o.Type == "radar" && o.Owner == t && !o.Destroyed) return true;
            return false;
        }

        static bool VoiceBusy() => AudioDirector.VoiceBusy || (AudioDirector.ExternalVoiceBusy != null && AudioDirector.ExternalVoiceBusy());

        void Update()
        {
            if (!game) game = GetComponent<ZGame>();
            var w = game ? game.World : null;
            if (w == null) return;
            if (w != world) { world = w; seen.Clear(); }
            var human = game.player;
            if (!w.IsHumanTeam(human)) return;                                                   // a match of two computers: nobody to warn
            bool radar = OwnsRadar(w, human);
            foreach (var bot in w.Bots)
            {
                if (bot.Team == human) continue;
                seen.TryGetValue(bot, out int n);
                for (; n < bot.StrikeEvents.Count; n++) Rule.Note(bot.StrikeEvents[n].kind, w.Time, Time.unscaledTime, radar);
                seen[bot] = n;
            }
            int line = Rule.Tick(w.Time, Time.unscaledTime, radar, VoiceBusy());
            if (line < 0) return;
            AudioDirector.Announce(EventId, "=" + Lines[line]);
            Debug.Log($"[OpenZ] strike group warning {Rule.Said}: \"{Lines[line]}\" at {w.Time:F0} s (marches seen {Rule.Requests}; no radar {Rule.NoRadar}, inside 5 min {Rule.InGap}, too late {Rule.TooLate})");
        }
    }
}
