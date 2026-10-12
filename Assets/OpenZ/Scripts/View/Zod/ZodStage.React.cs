// OpenZ (GPL-3.0)
// raises both forearms over his face and most of the hat brim, so from the front feed camera the viewer could not see Zod react to
// the HQ blast (the blast flash + kick + boom land on the same frame). While such a clip plays (clip seconds from..to) the feed CUTS
// to a second camera (lower, to his side, closer), then cuts back to the line's framing: the blast flash masks the cut in.
// Data-driven per model: Resources/OpenZ/Zod/zod_react_cams.json, keys "<model>/<clip>" (model = the Resources prefab name, so
// Zod v1 and v2 each get their own entry) before the plain "<clip>"; a clip without an entry never cuts.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        [Serializable]
        public class ReactCam
        {
            /// <summary>"&lt;clip&gt;" or "&lt;model&gt;/&lt;clip&gt;".</summary>
            public string key = "";
            /// <summary>Clip seconds the cut covers (it also ends when the clip hands back to the bed).</summary>
            public float from = 0f, to = 1.2f;
            /// <summary>Added to yawDeg (deg, + = his left side) and to the elevation (dir.y, negative = lower, looking up).</summary>
            public float yaw = 40f, elev = -0.3f;
            /// <summary>Aim height relative to the line framing (m), camera distance (m), field-of-view multiplier.</summary>
            public float look = -0.1f, dist = 1.9f, fov = 1f;
            /// <summary>The cut stays this long after the clip window / the hand-back to the bed (the arms come down during the 0.35 s
            /// crossfade: cutting back on it showed the arms over his face in the wide opening again, run 1).</summary>
            public float tail = 0.5f;
            public override string ToString() => $"{key}: t {from:0.00}-{to:0.00} s + tail {tail:0.00} s, yaw +{yaw:0} deg, elev {elev:+0.00;-0.00}, look {look:+0.00;-0.00} m, dist {dist:0.00} m, fov x{fov:0.00}";
        }
        [Serializable] class ReactCamFile { public ReactCam[] cams = new ReactCam[0]; }

        public const string ReactCamsPath = "OpenZ/Zod/zod_react_cams";
        /// <summary>The loaded table (live-tunable in Play: change a field, the next frame uses it).</summary>
        public static readonly Dictionary<string, ReactCam> ReactCams = new Dictionary<string, ReactCam>();
        static bool reactLoaded;
        /// <summary>Feed cuts to a react camera since the domain loaded (proof counter).</summary>
        public static int ReactCuts;
        public static string LastReact = "";
        string modelKey = "";
        bool reactWas; ReactCam reactKeep; float reactKeepUntil = -1f;

        public static void LoadReactCams(bool force = false)
        {
            if (reactLoaded && !force) return;
            reactLoaded = true; ReactCams.Clear();
            var ta = Resources.Load<TextAsset>(ReactCamsPath);
            if (!ta) { Debug.Log("[ZodCameo] no " + ReactCamsPath + ".json: no react camera cuts"); return; }
            try
            {
                var f = JsonUtility.FromJson<ReactCamFile>(ta.text);
                if (f != null && f.cams != null) foreach (var c in f.cams) if (c != null && !string.IsNullOrEmpty(c.key)) ReactCams[c.key] = c;
            }
            catch (Exception e) { Debug.LogWarning("[ZodCameo] " + ReactCamsPath + ".json: " + e.Message); }
        }

        /// <summary>The react camera of `clip` on this model (model entry first), or null.</summary>
        public ReactCam ReactFor(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return null;
            LoadReactCams();
            if (!string.IsNullOrEmpty(modelKey) && ReactCams.TryGetValue(modelKey + "/" + clip, out var m)) return m;
            return ReactCams.TryGetValue(clip, out var c) ? c : null;
        }

        /// <summary>True while the playing clip asks for its react cut this frame.</summary>
        bool ReactActive(out ReactCam rc)
        {
            rc = ReactFor(CurrentClip);
            bool inClip = rc != null && CurrentClip != Bed && clock - clipStart >= rc.from && clock - clipStart < rc.to;
            if (inClip) { reactKeep = rc; reactKeepUntil = clock + Mathf.Max(0f, rc.tail); }
            bool on = inClip || (reactKeep != null && clock < reactKeepUntil && (CurrentClip == Bed || rc == reactKeep));   // the tail covers the react clip's own last frames and the hand-back to the bed: another gesture's clip ends the cut at once (its framing and zoom are made for the line camera)
            if (on && !inClip) rc = reactKeep;
            if (on != reactWas)
            {
                reactWas = on;
                if (on) { ReactCuts++; LastReact = $"cut IN on '{CurrentClip}' at clip t {clock - clipStart:0.00} s ({rc})"; }
                else
                {
                    reactKeep = null;
                    bool boys = boysOn && clock <= reactKeepUntil + 0.25f;                                  // (not a fresh opening after the feed was dark)
                    if (boys) ResetBoys();                                                                  // back to the line framing, never to the wide opening
                    LastReact = $"cut OUT at clip t {clock - clipStart:0.00} s (clip {CurrentClip}){(boys ? ", the boys' opening skipped" : "")}";
                }
                Debug.Log("[ZodCameo] react camera " + LastReact);
            }
            if (!on) rc = null;
            return on;
        }
    }
}
