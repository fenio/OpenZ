using System;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Water surface height per map tile, for a level whose pools and streams lie at different heights (level 16).
    /// Table: Resources/OpenZ/Maps/&lt;level&gt;_water (JSON: width, height, dry, levels[width x height], row by row from the
    /// north; a tile whose value equals `dry` has no water). A level without a table (levels 2 and 17) has one flat surface:
    /// every caller passes the height it measured itself and gets exactly that back, so those levels do not change.
    /// </summary>
    public static class OzWaterLevel
    {
        [Serializable] sealed class Doc { public int width, height; public float dry; public float[] levels; }

        static string loadedFor;
        static Doc doc;

        static void Load(string level)
        {
            if (level == loadedFor) return;
            loadedFor = level; doc = null;
            if (string.IsNullOrEmpty(level)) return;
            var ta = Resources.Load<TextAsset>("OpenZ/Maps/" + level + "_water");
            if (ta == null) return;
            var d = JsonUtility.FromJson<Doc>(ta.text);
            if (d == null || d.levels == null || d.width <= 0 || d.height <= 0 || d.levels.Length != d.width * d.height)
            { Debug.LogWarning("[OpenZ] water table of " + level + " is not readable: one flat water height is used"); return; }
            doc = d;
            Debug.Log("[OpenZ] water table " + level + ": " + d.width + " x " + d.height + " tiles");
        }

        /// <summary>Does this level have its own table?</summary>
        public static bool Has(string level) { Load(level); return doc != null; }

        /// <summary>Surface height (m) at a world point; `flat` when the level has no table or the tile is dry.</summary>
        public static float At(string level, float x, float z, float flat)
        {
            Load(level);
            if (doc == null) return flat;
            int tx = Mathf.FloorToInt(x * 0.5f), ty = Mathf.FloorToInt(-z * 0.5f);            // one tile = 2 m; z runs negative to the south
            if (tx < 0 || ty < 0 || tx >= doc.width || ty >= doc.height) return flat;
            float v = doc.levels[ty * doc.width + tx];
            return v == doc.dry ? flat : v;
        }
    }
}
