using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// A local downwash for the level's trees and plants (rule DV118's shuttle, view only): the plants within a radius of a point lean
    /// OUTWARD from it and shake, strongest near the middle. The playable shuttle sets it every frame (<see cref="Set"/>); not refreshed,
    /// it fades to nothing within 0.2 s. OzFloraWind hands it to the plant shader as two globals beside the level wind
    /// (_OZDownwash = x, ground y, z, radius; _OZDownwash2 = strength, its own clock) and to its plain-number twin
    /// (OzFloraWindMath.Wind.Dw*: zero = the plants' movement exactly as without it). Each plant mixes it from its own foot
    /// (Shaders/OpenZFloraWind.hlsl OZFloraWind and its twin OzFloraWindMath.DownwashMix): kw = k (1 - smoothstep(0.35 R, R, distance)); stronger sway (+1.2 kw,
    /// at most 2.1), the rain lean and leaf push turned outward (0.9 kw), the wind's way bent outward (2.5 kw) and a rotor buffet on the
    /// gust with its own clock. A crown that the downwash first hits hard (kw over 0.5) rustles (shuttle_tree_rustle, at most one every
    /// 0.6 s; its own sound name, so the night / rain hush of the level's leaf rustle does not apply).
    /// </summary>
    public static class OzFloraDownwash
    {
        public static float FadeSec = 0.2f, RustleKw = 0.5f, RustleGap = 0.6f;
        /// <summary>Proof: rustles played, crowns hit hard now, the strength drawn now.</summary>
        public static int Rustles, HitNow;
        public static float K { get; private set; }
        public static float R { get; private set; }
        public static Vector3 Centre { get; private set; }
        public static float Clock { get; private set; }
        static float setAt = -100f, k0, lastRustle = -100f, lastTick = -1f, scanT;
        static readonly HashSet<int> hard = new HashSet<int>();

        /// <summary>The downwash this frame: the ground point under its middle, its radius (m) and strength k (0 .. about 1.6).</summary>
        public static void Set(Vector3 groundCentre, float radius, float k) => Set(groundCentre, radius, k, -1);

        /// <summary>The same for one of several shuttles (the shader takes one downwash): in a frame the strongest wins, the one that won
        /// the frame before keeps it unless another is clearly stronger (no flicker between two ships).</summary>
        public static void Set(Vector3 groundCentre, float radius, float k, int shipId)
        {
            float now = Time.time; k = Mathf.Max(0f, k);
            if (setAt == now && setBy != shipId)
            {
                bool mineBefore = shipId == lastWinner, theirsBefore = setBy == lastWinner;
                if (theirsBefore ? k <= k0 * 1.25f : (!mineBefore && k <= k0)) return;
            }
            Centre = groundCentre; R = Mathf.Max(0f, radius); k0 = k; setAt = now;
            if (setBy != shipId || frameWon != now) { setBy = shipId; frameWon = now; }
        }
        static int setBy = -1, lastWinner = -1; static float frameWon = -1f;

        /// <summary>The value drawn now (fades when not refreshed). OzFloraWind's per-frame globals and its twin read this.</summary>
        public static void Current(out Vector4 a, out Vector4 b)
        {
            float now = Time.time;
            if (lastTick != now) { float dt = lastTick < 0f ? 0f : now - lastTick; lastTick = now; lastWinner = setBy; Step(dt); }
            a = new Vector4(Centre.x, Centre.y, Centre.z, K > 0f ? R : 0f); b = new Vector4(K, Clock, 0f, 0f);
        }

        static void Step(float dt)
        {
            float fade = Mathf.Clamp01(1f - (Time.time - setAt) / Mathf.Max(0.01f, FadeSec));
            K = k0 * fade; Clock += dt;
            if (K <= 0.001f) { hard.Clear(); HitNow = 0; return; }
            if ((scanT -= dt) > 0f) return;
            scanT = 0.1f;
            var fw = OzFloraWind.I; if (!fw) return;
            var crowns = fw.Crowns; int n = 0;
            for (int i = 0; i < crowns.Count; i++)
            {
                var c = crowns[i]; float dx = c.x - Centre.x, dz = c.z - Centre.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > R) { hard.Remove(i); continue; }
                float kw = K * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f * R, R, d)));
                if (kw < RustleKw) { hard.Remove(i); continue; }
                n++;
                if (hard.Add(i) && Time.time - lastRustle >= RustleGap && AudioDirector.Has("shuttle_tree_rustle"))
                {
                    lastRustle = Time.time; Rustles++;
                    AudioDirector.Fire("shuttle_tree_rustle", new Vector3(c.x, c.y + 4f, c.z), -1, Mathf.Clamp01(0.5f + 0.3f * kw));
                }
            }
            HitNow = n;
        }

        /// <summary>The shuttle's numbers: radius 13 m while its pads are under 6 m (take-off, landing), 9 m at cruise; strength 1.6 up to
        /// 6 m, 0.9 at 14 m, none from 24 m.</summary>
        public static void ForShip(float feetM, out float radius, out float k)
        {
            radius = Mathf.Lerp(13f, 9f, Mathf.InverseLerp(6f, 12f, feetM));
            k = feetM <= 6f ? 1.6f : feetM <= 14f ? Mathf.Lerp(1.6f, 0.9f, (feetM - 6f) / 8f) : Mathf.Lerp(0.9f, 0f, Mathf.Clamp01((feetM - 14f) / 10f));
        }

        /// <summary>OzFloraWind.CurrentWind: the plain-number wind with this frame's downwash (the seats on branches follow it too).</summary>
        public static OzFloraWindMath.Wind With(OzFloraWindMath.Wind w)
        {
            Current(out var a, out var b);
            w.DwX = a.x; w.DwY = a.y; w.DwZ = a.z; w.DwR = a.w; w.DwK = b.x; w.DwT = b.y;
            return w;
        }
    }
}
