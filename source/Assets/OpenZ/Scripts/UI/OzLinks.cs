using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Every outside address the game shows or opens, in ONE file: Resources/OpenZ/Text/openz_links.json (a list of key + url).
    /// The menus hold keys only and ask here for the address, so a changed line in that file changes it in every place.
    /// Open hands the address to the system (the default browser or the app that owns the address), never an in-game view.
    /// </summary>
    public static class OzLinks
    {
        public const string File = "OpenZ/Text/openz_links";
        public const string Discord = "discord", FeedbackForm = "feedback_form";
        public const string DiscordButton = "OPENZ DISCORD";
        public const string DiscordLine = "Talk to other players, report bugs, share ideas.";

        [System.Serializable] sealed class Row { public string key = null, url = null; }
        [System.Serializable] sealed class Rows { public Row[] links = null; }
        static Dictionary<string, string> map;

        /// <summary>Proof switch (session only): log the exact address instead of handing it to the system.</summary>
        public static bool ProofNoOpen;
        public static string LastOpened, LastKey, LastFrom;
        public static int Opened;
        public static int Count { get { Load(); return map.Count; } }

        static void Load()
        {
            if (map != null) return;
            map = new Dictionary<string, string>();
            var t = Resources.Load<TextAsset>(File);
            if (!t) { Debug.LogWarning("[OpenZ Links] the links file is missing: Resources/" + File); return; }
            Rows rows = null;
            try { rows = JsonUtility.FromJson<Rows>(t.text); } catch (System.Exception e) { Debug.LogWarning("[OpenZ Links] the links file does not read: " + e.Message); }
            if (rows != null && rows.links != null)
                foreach (var r in rows.links) if (r != null && !string.IsNullOrEmpty(r.key) && !string.IsNullOrEmpty(r.url)) map[r.key] = r.url.Trim();
        }

        /// <summary>Read the file again (after it changed).</summary>
        public static void Reload() { map = null; }

        /// <summary>The address under a key; an empty text (and one warning) when the file has no such key.</summary>
        public static string Get(string key)
        {
            Load();
            string url;
            if (key != null && map.TryGetValue(key, out url)) return url;
            Debug.LogWarning("[OpenZ Links] no address under the key '" + key + "'");
            return "";
        }

        /// <summary>The site of an address as a link row shows it ("discord.gg").</summary>
        public static string Site(string key)
        {
            string u = Get(key); int a = u.IndexOf("://"); if (a >= 0) u = u.Substring(a + 3);
            int b = u.IndexOf('/'); if (b >= 0) u = u.Substring(0, b);
            return u.StartsWith("www.") ? u.Substring(4) : u;
        }

        public static void Open(string key, string from)
        {
            string url = Get(key);
            LastOpened = url; LastKey = key; LastFrom = from; Opened++;
            if (string.IsNullOrEmpty(url)) return;
            if (ProofNoOpen) { Debug.Log("[OpenZ Links] open (proof: not handed to the system) from " + from + ": " + key + " -> " + url); return; }
            Debug.Log("[OpenZ Links] open from " + from + ": " + key + " -> " + url);
            Application.OpenURL(url);
        }
    }
}
