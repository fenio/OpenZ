using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256e (FIX_LIST S5): a building's world badge (the production badge with its timer and TECH row, the level tag) is drawn on the UI,
    /// so it lay OVER a flying shuttle's hull (the fort's "1:01 TECH VII" over the ship in the take-off strip). Now a badge fades to
    /// <see cref="Under"/> of its alpha while a flying shuttle's drawn hull overlaps it on the screen (eased in <see cref="FadeSec"/>,
    /// back as the hull leaves), so the ship reads above the building's badge. The ships' screen rects are measured once a frame (the
    /// SH_ parts' bounds, projected). View only; nothing else changes for any badge.
    /// </summary>
    public static class OzShuttleBadgeFade
    {
        public static float Under = 0.22f, FadeSec = 0.2f, Pad = 6f;
        /// <summary>Proof: badge frames faded under a flying ship (at least half way), the badges faded now.</summary>
        public static int FadedFrames, FadedNow;

        static int frame = -1; static readonly List<Rect> hulls = new List<Rect>(4);
        static readonly Dictionary<int, float> k = new Dictionary<int, float>();
        static readonly Dictionary<int, int> seen = new Dictionary<int, int>();
        static readonly Vector3[] corners = new Vector3[8];
        static readonly Dictionary<int, (List<MeshRenderer> rs, int at)> parts = new Dictionary<int, (List<MeshRenderer> rs, int at)>();

        static void Measure(Camera cam)
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount; hulls.Clear(); FadedNow = 0;
            if (!cam) return;
            for (int i = 0; i < OzPlayableShuttle.Count; i++)
            {
                var sv = OzPlayableShuttle.At(i); var o = sv ? sv.Obj : null;
                if (!sv || o == null || o.Destroyed || !o.ShuttleAirborne || sv.BrokenUp) continue;
                bool any = false; var b = new Bounds();
                if (!parts.TryGetValue(o.Id, out var list) || Time.frameCount - list.at > 120)
                {   // the hull's parts, looked up again every 2 s (no search every frame)
                    var found = new List<MeshRenderer>();
                    foreach (var r in sv.GetComponentsInChildren<MeshRenderer>()) if (r.gameObject.name.StartsWith("SH_")) found.Add(r);
                    list = (found, Time.frameCount); parts[o.Id] = list;
                }
                foreach (var r in list.rs)
                {
                    if (!r || !r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) continue;
                Vector2 mn = new Vector2(1e9f, 1e9f), mx = new Vector2(-1e9f, -1e9f); bool front = false;
                var c = b.center; var e = b.extents;
                for (int q = 0; q < 8; q++)
                {
                    corners[q] = c + new Vector3((q & 1) != 0 ? e.x : -e.x, (q & 2) != 0 ? e.y : -e.y, (q & 4) != 0 ? e.z : -e.z);
                    var sp = cam.WorldToScreenPoint(corners[q]); if (sp.z <= 0f) continue;
                    front = true; mn = Vector2.Min(mn, sp); mx = Vector2.Max(mx, sp);
                }
                if (front) hulls.Add(Rect.MinMaxRect(mn.x - Pad, mn.y - Pad, mx.x + Pad, mx.y + Pad));
            }
        }

        /// <summary>The alpha factor for badge <paramref name="id"/> whose screen rect (pixels, y up) is <paramref name="r"/>: 1 when no flying
        /// ship's hull overlaps it, eased to <see cref="Under"/> while one does.</summary>
        public static float Alpha(int id, Rect r, float dt)
        {
            Measure(Camera.main);
            bool over = false;
            for (int i = 0; i < hulls.Count; i++) if (hulls[i].Overlaps(r)) { over = true; break; }
            float now = k.TryGetValue(id, out var v) ? v : 1f;
            if (seen.TryGetValue(id, out var f) && f == Time.frameCount) return now;                       // asked twice in one frame: the same answer
            seen[id] = Time.frameCount;
            float target = over ? Under : 1f;
            now = Mathf.MoveTowards(now, target, (1f - Under) * Mathf.Max(0f, dt) / Mathf.Max(0.01f, FadeSec));
            k[id] = now;
            if (now < 0.5f * (1f + Under)) { FadedFrames++; FadedNow++; }
            if (k.Count > 512) { k.Clear(); seen.Clear(); }
            return now;
        }
    }
}
