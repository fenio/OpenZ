using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// every burning fixture (priority x brightness, in or near the view, falling off with distance from the camera focus)
    /// and grants a real URP Light to the best N; the rest keep their emissive heads, beams and a projected pool impostor
    /// (NightPool), so far lights still read. Real lights cross-fade in / out (no pops). Shadows only for the best few
    /// shadow candidates (searchlights). N shrinks when:
    /// <list type="bullet">
    /// <item>
    /// N 8, no shadows; back to full below 70 C (GpuGuard itself pauses play mode at 78 C);</item>
    /// <item>frames run long (smoothed frame time over 19 ms: -25 %, recovers under 15 ms);</item>
    /// <item>the combat FX light pools are busy (half of the burning OpenZFxPlayer + FxManager lights is taken off N).</item>
    /// </list>
    /// </summary>
    public sealed class NightLightBudget
    {
        public int MaxLights = 24, MaxShadowed = 3;
        /// <summary></summary>
        public static System.Func<int> GpuTemperature;

        public int EffectiveMax { get; private set; }
        public int EffectiveShadows { get; private set; }
        public int Temperature { get; private set; } = -1;
        public int HeatLevel { get; private set; }
        public float FrameMs { get; private set; } = 16.7f;
        public int Granted { get; private set; }
        public int ShadowsGranted { get; private set; }
        public int Burning { get; private set; }
        public int FxLights { get; private set; }

        float nextTick, perfScale = 1f, perfCheck;
        readonly Plane[] planes = new Plane[6];
        readonly List<NightFixture> ranked = new List<NightFixture>(256);
        // an IComparer instance (List.Sort(Comparison) wraps the delegate in a new comparer on every call under Mono)
        sealed class ScoreDesc : IComparer<NightFixture> { public int Compare(NightFixture a, NightFixture b) => b.Score.CompareTo(a.Score); }
        static readonly ScoreDesc ByScore = new ScoreDesc();
        FxManager fxOwner; Light[] fxPool;

        public void Tick(List<NightFixture> all, Camera cam, Vector3 focus, float now, float dt)
        {
            all = NightBudgetShare.Merge(all);                                   // vehicle_lights: vehicle headlights share this budget
            FrameMs = Mathf.Lerp(FrameMs, Time.unscaledDeltaTime * 1000f, 0.05f);
            // cross-fade every frame towards the last grant
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                f.RealK = Mathf.MoveTowards(f.RealK, f.Budgeted && f.LightOut > 0.001f ? 1f : 0f, dt / 0.35f);
                f.ShadowK = Mathf.MoveTowards(f.ShadowK, f.Shadowed && f.Budgeted && f.LightOut > 0.001f ? 1f : 0f, dt / NightShadows.FadeSeconds);
            }
            if (now < nextTick) return;
            nextTick = now + 0.2f;

            // heat and frame-time scaling
            // quality cuts. The GPU temperature is still read for the status line only; HeatLevel stays 0 and perfScale 1.
            Temperature = GpuTemperature != null ? GpuTemperature() : -1;
            HeatLevel = 0; perfScale = 1f;
            FxLights = (OpenZFxPlayer.I ? OpenZFxPlayer.I.ActiveLights : 0) + FxManagerLights();
            // so heat level 1-2 (cap 14 / 8) x perfScale 0.4 minus half the explosion lights left 4-6 real lights, all taken by
            // the searchlights: headlights, lamps and factory lights never reached the ground. Unshadowed Forward+ lights are cheap
            // (one cluster lookup per pixel in their range), so heat and frame time now trim the count only mildly and take the
            // shadows first; explosion lights have their own pools and no longer eat the night budget.
            int max = HeatLevel == 2 ? Mathf.RoundToInt(MaxLights * 0.7f) : HeatLevel == 1 ? Mathf.RoundToInt(MaxLights * 0.85f) : MaxLights;
            max = Mathf.RoundToInt(max * Mathf.Max(0.75f, perfScale));
            EffectiveMax = Mathf.Clamp(max, 16, MaxLights);
            EffectiveShadows = HeatLevel == 2 ? 0 : HeatLevel == 1 ? Mathf.Min(1, MaxShadowed) : perfScale < 0.7f ? Mathf.Min(1, MaxShadowed) : MaxShadowed;
            if (NightShadows.Mode == 0) EffectiveShadows = 0;

            // score
            ranked.Clear(); int burning = 0;
            if (cam) GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < all.Count; i++)
            {
                var f = all[i];
                f.Budgeted = false; f.Shadowed = false; f.Score = -1f;
                if (!f.Light || f.LightOut <= 0.02f) continue;
                burning++;
                var p = f.LightPos;
                float reach = Mathf.Max(3f, f.Range * 0.8f);
                f.Visible = !cam || GeometryUtility.TestPlanesAABB(planes, new Bounds(p, new Vector3(reach, reach, reach) * 2f));
                if (!f.Visible) continue;
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(focus.x, focus.z));
                float bright = Mathf.Sqrt(Mathf.Max(0.1f, f.Intensity)) * f.LightOut;
                f.Score = (1f + f.Priority) * bright / (1f + d * d / 1600f) * (f.Target != null ? 1.5f : 1f);
                ranked.Add(f);
            }
            Burning = burning;
            ranked.Sort(ByScore);
            int n = Mathf.Min(EffectiveMax, ranked.Count), s = 0;
            for (int i = 0; i < n; i++)
            {
                var f = ranked[i]; f.Budgeted = true;
                if (f.ShadowCandidate && s < EffectiveShadows) { f.Shadowed = true; s++; }
            }
            if (NightShadows.Mode == 2) s += GrantNearby(n, focus, now); else nearHeld.Clear();
            Granted = n; ShadowsGranted = s;
        }

        // vehicle headlights, whose spots VehicleLights drives unshadowed) nearest the screen centre with something to shadow in
        // their pool. A slot is held >= HoldSeconds and changes hands only on a clear lead (no flicker while the camera pans).
        readonly List<NightFixture> nearHeld = new List<NightFixture>(8), nearCand = new List<NightFixture>(32);
        sealed class ShadowDesc : IComparer<NightFixture> { public int Compare(NightFixture a, NightFixture b) => b.ShadowScore.CompareTo(a.ShadowScore); }
        static readonly ShadowDesc ByShadow = new ShadowDesc();
        public int NearbyShadows => nearHeld.Count;
        int GrantNearby(int n, Vector3 focus, float now)
        {
            nearCand.Clear();
            for (int i = 0; i < n; i++)
            {
                var f = ranked[i];
                if (f.Shadowed || f.ShadowCandidate || !f.Light || f.Light.type != LightType.Spot) continue;
                if (f.Rig == null && f.Id != null && f.Id.StartsWith("vehicle_")) continue;
                if (!NightShadows.HasCasters(f)) continue;
                var c = NightShadows.PoolCentre(f);
                float d = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(focus.x, focus.z));
                f.ShadowScore = 1f / (1f + d * d / 400f);
                nearCand.Add(f);
            }
            for (int i = nearHeld.Count - 1; i >= 0; i--) if (!nearCand.Contains(nearHeld[i])) nearHeld.RemoveAt(i);
            nearCand.Sort(ByShadow);
            for (int i = 0; i < nearCand.Count; i++)
            {
                var f = nearCand[i];
                if (nearHeld.Contains(f)) continue;
                if (nearHeld.Count < NightShadows.NearbySlots) { nearHeld.Add(f); f.ShadowUntil = now + NightShadows.HoldSeconds; continue; }
                int w = -1; float ws = float.MaxValue;
                for (int k = 0; k < nearHeld.Count; k++) if (now >= nearHeld[k].ShadowUntil && nearHeld[k].ShadowScore < ws) { ws = nearHeld[k].ShadowScore; w = k; }
                if (w >= 0 && f.ShadowScore > ws * NightShadows.SwapLead) { nearHeld[w] = f; f.ShadowUntil = now + NightShadows.HoldSeconds; }
            }
            nearHeld.Sort(ByShadow);
            for (int k = 0; k < nearHeld.Count; k++) { nearHeld[k].Shadowed = true; nearHeld[k].ShadowHigh = k < NightShadows.HighTierCount; }
            return nearHeld.Count;
        }

        /// <summary>Burning lights of FxManager's pool (28 flash / fire lights, no public counter): its Light children,
        /// collected once per FxManager instance, counted without allocating.</summary>
        int FxManagerLights()
        {
            var fx = FxManager.I;
            if (!fx) return 0;
            if (fx != fxOwner || fxPool == null || fxPool.Length == 0) { fxOwner = fx; fxPool = fx.GetComponentsInChildren<Light>(true); }
            int n = 0;
            for (int i = 0; i < fxPool.Length; i++) if (fxPool[i] && fxPool[i].enabled) n++;
            return n;
        }
    }
}
