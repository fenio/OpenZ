using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// ONE per-event trim table on top of every existing gain, measured and written by
    /// OzMixProbe (Play mode, default RTS view, source at the screen centre). Resources/OpenZ/Audio/mix_trims.txt, one line each:
    ///   ca &lt;CombatAudio key&gt; &lt;dB&gt;        (CombatAudio.PlayPitched; "engine_loop" = LegacyEngineAudio's loops)
    ///   ad &lt;AudioDirector event id&gt; &lt;dB&gt;  (one-shots + loops; an id ending in * trims every id with that prefix)
    /// Voices, UI and music are never trimmed here (they stay the reference the world mix sits under).
    /// </summary>
    public static class OzMixTrim
    {
        public const string Res = "OpenZ/Audio/mix_trims";
        static Dictionary<string, float> ca, ad;
        static List<KeyValuePair<string, float>> adPrefix;
        /// <summary>The probe's "before" pass: every trim reads 0.</summary>
        public static bool Off;

        static void Ensure()
        {
            if (ca != null) return;
            ca = new Dictionary<string, float>(); ad = new Dictionary<string, float>(); adPrefix = new List<KeyValuePair<string, float>>();
            var ta = Resources.Load<TextAsset>(Res);
            if (ta) Parse(ta.text);
        }

        public static void Parse(string text)
        {
            ca = new Dictionary<string, float>(); ad = new Dictionary<string, float>(); adPrefix = new List<KeyValuePair<string, float>>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim(); int hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash).Trim();
                if (line.Length == 0) continue;
                var p = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3 || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var db)) continue;
                Set(p[0], p[1], db);
            }
        }

        public static void Set(string kind, string key, float db)
        {
            Ensure();
            if (kind == "ca") { if (db == 0f) ca.Remove(key); else ca[key] = db; }
            else if (kind == "ad")
            {
                if (key.EndsWith("*"))
                {
                    var pre = key.Substring(0, key.Length - 1); adPrefix.RemoveAll(kv => kv.Key == pre);
                    if (db != 0f) adPrefix.Add(new KeyValuePair<string, float>(pre, db));
                }
                else if (db == 0f) ad.Remove(key); else ad[key] = db;
            }
        }

        public static float Get(string kind, string key)
        {
            Ensure();
            if (kind == "ca") return ca.TryGetValue(key, out var c) ? c : 0f;
            if (key.EndsWith("*")) { var pre = key.Substring(0, key.Length - 1); foreach (var kv in adPrefix) if (kv.Key == pre) return kv.Value; return 0f; }
            return ad.TryGetValue(key, out var a) ? a : 0f;
        }

        public static float CaDb(string key)
        {
            if (Off || key == null) return 0f;
            Ensure();
            return ca.TryGetValue(key, out var d) ? d : 0f;
        }
        public static float CaLin(string key) { float d = CaDb(key); return d == 0f ? 1f : Mathf.Pow(10f, d / 20f); }

        public static float AdDb(string id)
        {
            if (Off || id == null) return 0f;
            Ensure();
            if (ad.TryGetValue(id, out var d)) return d;
            for (int i = 0; i < adPrefix.Count; i++) if (id.StartsWith(adPrefix[i].Key)) return adPrefix[i].Value;
            return 0f;
        }

        public static string Serialize(string header)
        {
            Ensure();
            var sb = new StringBuilder();
            foreach (var h in (header ?? "").Split('\n')) if (h.Trim().Length > 0) sb.Append("# ").AppendLine(h.Trim());
            var keys = new List<string>(ca.Keys); keys.Sort();
            foreach (var k in keys) sb.AppendLine("ca " + k + " " + ca[k].ToString("+0.0;-0.0", CultureInfo.InvariantCulture));
            var ids = new List<string>(ad.Keys); ids.Sort();
            foreach (var k in ids) sb.AppendLine("ad " + k + " " + ad[k].ToString("+0.0;-0.0", CultureInfo.InvariantCulture));
            foreach (var kv in adPrefix) sb.AppendLine("ad " + kv.Key + "* " + kv.Value.ToString("+0.0;-0.0", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
