using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    ///
    ///   applyPopEffect / updatePopEffects, POP_EFFECT_CONFIG { duration .4, peakScale 1.15, startScale 0 }:
    ///     first half start -> peak, ease-out cubic 1 - (1 - t)^3; second half peak -> 1, ease-in quad t^2 (scale x the original).
    ///   createRedTurret / createBoltTurret / createGatling run applyPopEffect(group) and, for a turret
    ///     built from the GUI, playSound('turretbuilt', .6) (+ 50 % "turret ready" by the woman announcer 3 s later; OpenZ keeps
    ///     Rachel's own "gun" lines). turretbuilt.mp3 = the mechanical clank-and-lock placement sound (1.0 s) -> atom-turret-placed.
    ///   killJeepDriver: the jeep's driver is killed, the jeep goes neutral, playSound('popWebm', .4, jeep pos).
    ///     The vehicle capture pop uses the same sound + applyPopEffect(mesh, 0, .25). pop.webm -> atom-pop.wav
    ///     (Opus decoded to 48 kHz PCM, the 170 ms of leading silence cut so the pop lands on the snipe frame).
    /// (OpenZFxPlayer.DrawHalo: the colour is linearised before the gain, like every OpenZ HDR effect).
    ///
    /// View only: it reacts to the sim's CannonPlaced / Sniped events (RulesView / CombatView) and never writes sim state, so the
    /// sim stays deterministic. CannonPlaced is only emitted by ZWorld.PlaceCannon (a stored cannon placed from a building), so
    /// map-load cannons and robots leaving a cannon never pop. Both teams get it (the event carries no team rule); note that the
    /// Zod CPU (and OpenZ's ZBot, ported from it) never places stored cannons.
    ///
    /// The scale pop is applied only while the cameras render (beginCameraRendering -> endCameraRendering, like OpenZFxShake):
    /// game logic always sees the model's real scale, so UnitView's tower sizing (localScale x .72 on a fort pad), BoardingView's
    /// crew scale (lossyScale on the first frame) and picking are untouched. The scale is taken about the gun's base-plate centre
    /// (TowerMounts.FootprintCentreLocal), so a gun grows out of its own footprint.
    /// </summary>
    [DefaultExecutionOrder(97)]   // after UnitView (60), BoardingView (70), PopOut (95); before FxManager (100) / OpenZFxPlayer (110)
    public class PlacementPop : MonoBehaviour
    {
        /// <summary>POP_EFFECT_CONFIG: .4 s, 0 -> 1.15 -> 1.</summary>
        public const float Duration = 0.4f, PeakScale = 1.15f, StartScale = 0f;
        /// <summary>The small bounce: after the peak the scale dips to .95 and settles on 1 (still .4 s in all). Atom
        /// Assault's own second half (1.15 -> 1, ease-in quad) with AtomExact = true.</summary>
        public const float Undershoot = 0.95f;
        public static bool AtomExact = false;
        /// <summary>Snipe jolt of the emptied vehicle: the same .4 s curve from .92 up to 1.08, dip .98, settle (Atom's capture pop
        /// starts at .25, far too much for a tank that stays on the map).</summary>
        public const float JoltStart = 0.92f, JoltPeak = 1.08f, JoltUnder = 0.98f, JoltDuration = 0.4f;
        public const string PlacedClip = "atom-turret-placed", PopClip = "atom-pop";
        public const string PlacedKey = "placed", PopKey = "snipe_pop";
        /// <summary>
        /// (:69698); OpenZ: .5 so the pop reads over the snipe ricochet + death it plays with.</summary>
        public const float PlacedGain = 0.6f, SnipePopGain = 0.5f;

        [Header("Placement dust ring (m12_dust)")]
        public int ringPuffs = 16;
        public float ringRadius = 1.5f, ringSpeed = 3.2f, ringUp = 0.45f, ringLife = 0.85f, ringSize = 1.25f, ringAlpha = 0.42f;
        [Header("Placement flash")]
        public float flashLife = 0.12f, flashLightPower = 5f, flashLightRange = 7.5f, flashHaloSize = 3.0f, flashHaloGain = 1.8f;
        public float sparksStrength = 0.9f;
        [Header("Snipe pop")]
        public float snipeHaloSize = 1.6f, snipeHaloGain = 1.5f, snipeLightPower = 3f, snipeLightRange = 5f, snipeDust = 0.7f;

        static readonly Color FlashColor = new Color(1f, 0.87f, 0.64f);           // warm white (the pop is not a blast: no orange)
        static readonly Color SnipeFlashColor = new Color(0.78f, 0.87f, 1f);
        static readonly Color DustDesert = new Color(0.784f, 0.604f, 0.4f);       // #c89a66, the M12 dust (OpenZFxCatalog.M12Dust)
        static readonly Color DustCity = new Color(0.62f, 0.57f, 0.5f);           // L17: grey-brown concrete / rust dirt dust

        public static int Pops { get; private set; }
        public static int Snipes { get; private set; }

        // ------------------------------------------------------------------ jobs
        sealed class Job
        {
            public Transform t; public float t0, dur, start, peak, under; public bool hold;
            public bool hasPivot; public Vector3 localPivot;
            public bool applied; public Vector3 baseScale, basePos;
        }
        sealed class Pending { public int id; public UnitView view; public bool tower; public int frames; public Vector3 fallback; public Job job; }
        sealed class Flash { public Vector3 at; public float age, life, size, gain; public Color color; }

        static readonly List<Job> jobs = new List<Job>();
        readonly List<Pending> pending = new List<Pending>();
        readonly List<Flash> flashes = new List<Flash>();
        static bool hooked;
        static PlacementPop inst;

        static PlacementPop Instance
        {
            get
            {
                if (inst) return inst;
                inst = FindFirstObjectByType<PlacementPop>();
                if (!inst) inst = new GameObject("PlacementPop").AddComponent<PlacementPop>();
                return inst;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {   // Enter Play Mode without a domain reload keeps statics: start clean
            if (hooked) { RenderPipelineManager.beginCameraRendering -= BeginCam; RenderPipelineManager.endCameraRendering -= EndCam; }
            hooked = false; jobs.Clear(); inst = null; Pops = 0; Snipes = 0;
        }

        static void Hook()
        {
            if (hooked) return;
            RenderPipelineManager.beginCameraRendering += BeginCam;
            RenderPipelineManager.endCameraRendering += EndCam;
            hooked = true;
        }

        void Awake() { if (inst && inst != this) { Destroy(this); return; } inst = this; }
        void OnDestroy() { if (inst == this) inst = null; }

        // ------------------------------------------------------------------ the curve
        /// <summary>
        /// ease-in quad down to 1 (AtomExact) or peak -> under (smoothstep, .3) -> 1 (ease-out quad, .2).</summary>
        public static float Curve(float t, float start, float peak, float under)
        {
            if (t <= 0f) return start;
            if (t >= 1f) return 1f;
            if (t < 0.5f) { float u = t * 2f; return start + (peak - start) * (1f - Mathf.Pow(1f - u, 3f)); }
            if (AtomExact) { float v = (t - 0.5f) * 2f; return peak - (peak - 1f) * v * v; }
            if (t < 0.8f) { float v = (t - 0.5f) / 0.3f; v = v * v * (3f - 2f * v); return peak + (under - peak) * v; }
            float w = (t - 0.8f) / 0.2f; return under + (1f - under) * (1f - (1f - w) * (1f - w));
        }

        static float K(Job j)
        {
            if (j.hold) return Mathf.Max(0.001f, j.start);
            return Mathf.Max(0.001f, Curve((Time.time - j.t0) / Mathf.Max(0.01f, j.dur), j.start, j.peak, j.under));
        }

        static Job StartPop(Transform t, float start, float peak, float under, float dur, bool hold)
        {
            if (!t) return null;
            foreach (var x in jobs) if (x.t == t) { x.t0 = Time.time; x.start = Mathf.Min(start, K(x)); x.peak = peak; x.under = under; x.dur = dur; x.hold = hold; return x; }
            var j = new Job { t = t, t0 = Time.time, dur = dur, start = start, peak = peak, under = under, hold = hold };
            jobs.Add(j); Hook();
            return j;
        }

        /// <summary>Render-time only: the popped scale for this camera, the real one back right after it.</summary>
        static void BeginCam(ScriptableRenderContext ctx, Camera cam)
        {
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                var j = jobs[i];
                if (j.applied) continue;                                                                     // nested camera: keep the first apply
                if (!j.t || PopOut.IsPopping(j.t)) { jobs.RemoveAt(i); continue; }                         // gone / dying: PopOut owns the scale
                if (!j.hold && Time.time - j.t0 >= j.dur) { jobs.RemoveAt(i); continue; }                   // done: the model keeps its own scale
                float k = K(j);
                j.baseScale = j.t.localScale; j.basePos = j.t.position;
                if (!j.hasPivot) { j.localPivot = TowerMounts.FootprintCentreLocal(j.t); j.hasPivot = true; }
                var pivot = j.t.TransformPoint(j.localPivot); pivot.y = j.basePos.y;
                j.t.localScale = j.baseScale * k;
                j.t.position = pivot + (j.basePos - pivot) * k;
                j.applied = true;
            }
        }

        static void EndCam(ScriptableRenderContext ctx, Camera cam)
        {
            foreach (var j in jobs)
            {
                if (!j.applied) continue;
                j.applied = false;
                if (j.t) { j.t.localScale = j.baseScale; j.t.position = j.basePos; }
            }
        }

        // ------------------------------------------------------------------ entry points (RulesView / CombatView)
        /// <summary>CannonPlaced: the gun pops out of its footprint (held at 0 until its view stands where it belongs: a fort pad is
        /// </summary>
        public static void Placed(ZGame game, ZObject c)
        {
            if (game == null || c == null) return;
            var v = game.Views.TryGetValue(c.Id, out var uv) ? uv : null;
            var p = Instance;
            var job = v ? StartPop(v.transform, StartScale, PeakScale, Undershoot, Duration, true) : null;
            p.pending.Add(new Pending { id = c.Id, view = v, tower = !c.Ejectable, fallback = game.CenterWorld(c), job = job });
            Pops++;
        }

        /// <summary>Sniped: the emptied vehicle / cannon jolts (a small .92 -> 1.08 pop), a cool flash and a dust puff at its base,
        /// line and BoardingView's thrown driver.</summary>
        public static void Sniped(ZGame game, ZObject v, Vector3 seat)
        {
            if (game == null || v == null) return;
            var view = game.Views.TryGetValue(v.Id, out var uv) ? uv : null;
            if (view && !view.Dying) StartPop(view.transform, JoltStart, JoltPeak, JoltUnder, JoltDuration, false);
            var at = view ? view.transform.position : game.CenterWorld(v);
            var au = CombatAudio.I;
            if (au) au.Play(PopKey, seat, SnipePopGain, 0f, v.Id, OzSoundV3.BankSnipePop);   // sound_v3
            var p = Instance;
            p.flashes.Add(new Flash { at = seat, life = p.flashLife, size = p.snipeHaloSize, gain = p.snipeHaloGain, color = SnipeFlashColor });
            FxManager.I?.AddLight(seat + Vector3.up * 0.3f, SnipeFlashColor, p.snipeLightPower, p.snipeLightRange, p.flashLife, 0f, true);
            var z = OpenZFxPlayer.I;
            if (z && z.isActiveAndEnabled) { if (!z.WetAt(at)) z.Dust(at + Vector3.up * 0.2f, p.snipeDust); }
            else FxManager.I?.Dust(at + Vector3.up * 0.2f, p.snipeDust);
            Snipes++;
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            var game = ZGame.I;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var q = pending[i];
                // a gun placed on a fort turret spot (non-ejectable) waits until UnitView has put it on its pad (<= 12 frames)
                if (q.view && q.tower && !q.view.OnTower && ++q.frames <= 12) continue;
                pending.RemoveAt(i);
                var at = q.view ? q.view.transform.position : q.fallback;
                if (q.job != null) { q.job.hold = false; q.job.t0 = Time.time; }
                PlacedFx(game, q, at);
            }
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var f = flashes[i];
                f.age += Time.deltaTime;
                if (f.age >= f.life) { flashes.RemoveAt(i); continue; }
                float k = 1f - f.age / f.life;
                var z = OpenZFxPlayer.I;
                if (z && z.isActiveAndEnabled) z.DrawHalo(f.at, f.size * (0.7f + 0.3f * k), f.color, f.gain * k * k);
            }
        }

        void PlacedFx(ZGame game, Pending q, Vector3 at)
        {
            var au = CombatAudio.I;
            if (au) au.Play(PlacedKey, at, PlacedGain, 0f, q.id, au.HasClip(PlacedClip) ? PlacedClip : "enter");
            var mid = at + Vector3.up * 1.0f;
            flashes.Add(new Flash { at = mid, life = flashLife, size = flashHaloSize * (q.tower ? 0.75f : 1f), gain = flashHaloGain, color = FlashColor });
            FxManager.I?.AddLight(mid, FlashColor, flashLightPower, flashLightRange, flashLife, 0f, true);
            var z = OpenZFxPlayer.I;
            bool live = z && z.isActiveAndEnabled, wet = live && !q.tower && z.WetAt(at);                  // open water only (bridge decks are ground)
            if (live)
            {
                if (wet) { z.Splash(at, 0.7f); z.Ripple(at, 1.1f); }
                else DustRing(z, game, at, q.tower ? 0.7f : 1f);
                z.Pop(mid, sparksStrength);
            }
            else if (FxManager.I)
            {
                FxManager.I.Dust(at, 1f);
                FxManager.I.Pop(mid, sparksStrength);
            }
        }

        /// <summary>A ring of m12_dust puffs on the gun's footprint edge rolling outward over the ground (the gun "lands").</summary>
        void DustRing(OpenZFxPlayer z, ZGame game, Vector3 at, float s)
        {
            var col = game != null && game.level == "L17" ? DustCity : DustDesert;
            int n = Mathf.Max(6, Mathf.RoundToInt(ringPuffs * Mathf.Lerp(0.7f, 1f, s)));
            float phase = Random.value * Mathf.PI * 2f;
            for (int i = 0; i < n; i++)
            {
                float a = phase + i * Mathf.PI * 2f / n + Random.Range(-0.12f, 0.12f), ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                var pos = at + new Vector3(ca * ringRadius * s, 0.25f, sa * ringRadius * s);
                if (game != null) pos.y = Mathf.Max(pos.y, game.GroundY(pos.x, pos.z) + 0.25f);   // on a fort pad the ground below stays below
                if (pos.y - at.y > 1.5f) pos.y = at.y + 0.25f;                                    // a wall / cliff next to the gun: stay at its level
                float sp = ringSpeed * s * Random.Range(0.85f, 1.15f);
                var vel = new Vector3(ca * sp, ringUp * Random.Range(0.7f, 1.3f), sa * sp);
                z.EmitChannel("m12_dust", pos, vel, ringLife * Random.Range(0.85f, 1.15f), ringSize * s * Random.Range(0.8f, 1.2f), col, ringAlpha);
            }
            z.Dust(at + Vector3.up * 0.3f, 0.8f * s);                                                          // the M12 golden-angle puff in the middle
        }
    }
}
