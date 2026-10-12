using System.Collections.Generic;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The Electrobot speaks his OWN select / move / attack / running-gag lines even when unit_voices_v1 is not in the
    /// project. Three one-line hooks in AudioDirector (fx\patches\edits_fx.py):
    /// <list type="bullet">
    /// <item>SelectionVoice's classic generic branch asks <see cref="Select"/> first: the speaker Zod picked is an Electrobot -> his
    /// vo_select_x_electrobot pool ("Electrobot! Fully charged!" ...), the 3rd+ quick click on the same Electrobot -> the running gag
    /// vo_idle_x_electrobot ("Keep clicking! You'll get a shock!" ...). Robots with a Plural name keep Zod's 50 % type line; the
    /// Electrobot has none, so he always speaks his own.</item>
    /// <item>PollInput tells <see cref="Ordered"/> about an accepted order (before the classic order line), and the static
    /// AudioDirector.Say routes (<see cref="Route"/>) that frame's classic vo_order_ack / vo_order_attack to his
    /// vo_order_x_electrobot_move / _attack pools when the order's speaker (a random selected unit, like the selection voice) is an
    /// Electrobot. "No way!" stays classic.</item>
    /// </list>
    /// When unit_voices_v1 is live AND knows the Electrobot (UnitVoices.Types contains "electrobot": fx\apply.py's optional edit), its
    /// UvSelect / UvOrder own these pools (their 60 % share, the gag after 4 clicks, the channel rules) and this fallback stays silent,
    /// so nothing is said twice. Every line still goes through AudioDirector.Say (SayImpl: a speaking line is never cut). View only.
    /// </summary>
    public static class ElectroVoices
    {
        public static bool Enabled = true;
        public const string SelectPool = "vo_select_x_electrobot", GagPool = "vo_idle_x_electrobot",
                            MovePool = "vo_order_x_electrobot_move", AttackPool = "vo_order_x_electrobot_attack";
        /// <summary>Quick clicks on the same Electrobot within this many seconds count toward the running gag (3rd click on).</summary>
        public static float GagWindow = 1.0f;
        public static int GagFromClick = 3;

        static int checkedUv; static bool uvOwns;
        /// <summary>true when unit_voices_v1 is compiled in and lists "electrobot" in UnitVoices.Types (it then owns his pools).</summary>
        public static bool UnitVoicesOwnHim
        {
            get
            {
                if (checkedUv == 0)
                {
                    checkedUv = 1;
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    {
                        System.Type t = null;
                        try { t = asm.GetType("OpenZ.UnitVoices", false); } catch { }
                        if (t == null) continue;
                        var f = t.GetField("Types", BindingFlags.Public | BindingFlags.Static);
                        uvOwns = f != null && f.GetValue(null) is string[] types && System.Array.IndexOf(types, ZElectro.Type) >= 0;
                        break;
                    }
                }
                return uvOwns;
            }
        }

        static int lastId = -1, clicks; static float lastClick = -99f;
        public static int SelectLines { get; private set; }
        public static int OrderLines { get; private set; }

        /// <summary>AudioDirector.SelectionVoice, the classic generic branch (u = the speaker Zod picked). true = the Electrobot spoke.</summary>
        public static bool Select(ZObject u)
        {
            if (!Enabled || u == null || !u.IsElectrobot || UnitVoicesOwnHim) return false;
            float now = Time.unscaledTime;
            clicks = u.Id == lastId && now - lastClick < GagWindow ? clicks + 1 : 1;
            lastId = u.Id; lastClick = now;
            string id = clicks >= GagFromClick && AudioDirector.Has(GagPool) ? GagPool : SelectPool;
            if (!AudioDirector.Has(id)) return false;
            AudioDirector.Say(id); SelectLines++;
            return true;
        }

        static int orderFrame = -1; static bool orderElectro;

        /// <summary>AudioDirector.PollInput, an accepted order (before its classic order line): is this order's speaker an Electrobot?</summary>
        public static void Ordered(List<ZObject> selection)
        {
            orderFrame = Time.frameCount; orderElectro = false;
            if (!Enabled || selection == null || selection.Count == 0 || UnitVoicesOwnHim) return;
            var u = selection[Random.Range(0, selection.Count)];                     // view RNG, like SelectionVoice (never the sim's)
            orderElectro = u != null && u.IsElectrobot;
        }

        /// <summary>AudioDirector.Say: this frame's classic order line of an Electrobot's order becomes his own pool.</summary>
        public static string Route(string id)
        {
            if (!orderElectro || orderFrame != Time.frameCount || id == null) return id;
            string to = id == "vo_order_ack" ? MovePool : id == "vo_order_attack" ? AttackPool : null;
            if (to == null || !AudioDirector.Has(to)) return id;
            orderElectro = false; OrderLines++;
            return to;
        }
    }
}
