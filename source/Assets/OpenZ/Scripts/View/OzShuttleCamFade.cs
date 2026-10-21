using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// K440 (item 441): the game camera never sees into a shuttle. At the closest zoom (25 m) the flying ship's hull top reaches the camera
    /// over raised ground, over a kapok, as another ship giving way upward, and in every kickflip (the hull turns over 12.2 m above its
    /// root); in the free camera (its floor 14 m inside the map) the ship flies through the lens at plain cruise height. The near plane
    /// (0.5 m) then cut the hull open, or the camera stood inside it, and the bay showed.
    /// Now, as a drawn shuttle (and each piece of a shot-down one) comes near the game camera, the whole of it is dithered away with the
    /// rocks' own 4 x 4 screen-door pattern (OpenZ/TeamLit, _OZCamFade): drawn as always while each of its parts' boxes is at least
    /// <see cref="ClearM"/> from the eye, every pixel of it cut once any box is within <see cref="GoneM"/>, eased between. A box always holds
    /// its part, so the ship is gone long before any of it can reach the near plane (the frame's corners 0.58 m from the eye); every
    /// surface of the ship is cut by the same threshold at a pixel, so where the hull is cut, the inside behind it is cut too. Its glows,
    /// lamp lenses, beams and flames near the eye are hidden once half of it is gone; its shadow and its sounds stay; its own motion vectors
    /// are off while it is faded (no streak behind an unseen hull). Measured against the camera as it draws (the pose of the picture, the
    /// kickflip's included), once in LateUpdate and again just before the game camera draws. Without the shader's _OZCamFade (an older
    /// TeamLit) the hull is hidden at half way instead. A break-up's pieces copy the hull's paint block: the cut in that copy is put back
    /// to 0 on each new piece before it is drawn (<see cref="ClearCopiedCut"/>). View only: no rule reads it.
    /// Play check: return OpenZ.OzShuttleCamFade.Status();
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class OzShuttleCamFade : MonoBehaviour
    {
        /// <summary>Off = the ship is always drawn, as before (for a before / after look).</summary>
        public static bool On = true;
        /// <summary>The pieces of a shot-down ship fade the same way.</summary>
        public static bool PiecesToo = true;
        /// <summary>Distance (m) from the eye to the nearest part box: every pixel cut at or under GoneM, drawn as always from ClearM on.</summary>
        public static float GoneM = 2.0f, ClearM = 6.0f;
        /// <summary>From this cut on the ship's other parts (glows, lenses, beams, flames) near the eye are hidden (and, with an older shader,
        /// the hull itself).</summary>
        public static float OtherHideAt = 0.5f;
        /// <summary>A ship's (a piece's) farthest part from its middle (m, an open ramp included): farther than this plus ClearM = nothing to do.</summary>
        public const float ShipRadiusM = 8f, PieceRadiusM = 4f, ShipMiddleUpM = 2.8f;
        public const string FadeProperty = "_OZCamFade";
        static readonly int FadeId = Shader.PropertyToID(FadeProperty);

        /// <summary>Proof counters: ticks, ticks with some ship faded, with some ship fully cut, block writes, restores, parts hidden, the fallback
        /// used (an older shader), the last nearest box distance (m) and cut, the largest cut seen; break-up pieces looked at once and the
        /// copied cuts put back to 0 on them (<see cref="ClearCopiedCut"/>).</summary>
        public static int Ticks, FadedTicks, GoneTicks, Writes, Restores, PartsHidden, Fallbacks, PiecesLookedAt, CopiedCutsCleared;
        public static float LastBoxM = -1f, LastCut, PeakCut;

        sealed class Rec
        {
            public Transform root; public float cut; public bool fallback;
            public readonly List<Renderer> fade = new List<Renderer>(), other = new List<Renderer>(), meshes = new List<Renderer>();   // meshes: the other parts that are meshes (the fallback's hull)
            public readonly Dictionary<Renderer, float> written = new Dictionary<Renderer, float>();
            public readonly HashSet<Renderer> hidden = new HashSet<Renderer>(), mvOff = new HashSet<Renderer>();
        }

        static OzShuttleCamFade inst;
        readonly Dictionary<int, Rec> recs = new Dictionary<int, Rec>();
        readonly HashSet<int> seen = new HashSet<int>();
        readonly HashSet<int> piecesSeen = new HashSet<int>();                                          // break-up pieces already looked at (ClearCopiedCut)
        readonly List<int> drop = new List<int>();
        static readonly List<Renderer> buf = new List<Renderer>();
        static readonly List<Material> mats = new List<Material>();
        static readonly Dictionary<Renderer, int> matCount = new Dictionary<Renderer, int>();       // each renderer's material count, as last sorted
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        Camera target; bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (!FindFirstObjectByType<OzShuttleCamFade>()) { var go = new GameObject("OZ_ShuttleCamFade"); DontDestroyOnLoad(go); go.AddComponent<OzShuttleCamFade>(); } }

        void OnEnable() { inst = this; if (!hooked) { RenderPipelineManager.beginCameraRendering += Begin; hooked = true; } }
        void OnDisable() { if (hooked) { RenderPipelineManager.beginCameraRendering -= Begin; hooked = false; } RestoreAll(); if (inst == this) inst = null; }
        void OnApplicationQuit() => OnDisable();

        /// <summary>The game camera: the main camera, else the RtsCamera's own.</summary>
        static Camera GameCamera()
        {
            var c = Camera.main;
            if (c) return c;
            var rts = FindFirstObjectByType<RtsCamera>();
            return rts ? rts.GetComponent<Camera>() : null;
        }

        void LateUpdate() { target = GameCamera(); Tick(target); }

        void Begin(ScriptableRenderContext ctx, Camera c) { if (c && c == target) Tick(c); }   // the pose the picture is drawn from

        // ------------------------------------------------------------------ the pure part (the counted check uses it too)
        /// <summary>The cut (0 = drawn as always .. 1 = every pixel cut) at nearest-box distance d (m).</summary>
        public static float CutAt(float d) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GoneM, ClearM, d));

        /// <summary>The nearest distance (m) from the eye to the world boxes of the drawn renderers in rs (0 inside one; +inf: none drawn).
        /// A part this hid itself (forceRenderingOff, the older-shader fallback) still counts: its box is where it would be drawn.</summary>
        public static float BoxDistance(IList<Renderer> rs, Vector3 eye)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < rs.Count; i++)
            {
                var r = rs[i];
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                float d2 = r.bounds.SqrDistance(eye);
                if (d2 < best) best = d2;
            }
            return best < float.PositiveInfinity ? Mathf.Sqrt(best) : best;
        }

        /// <summary>The renderer takes the fade: one of its materials is a TeamLit with _OZCamFade.</summary>
        public static bool TakesFade(Renderer r)
        {
            mats.Clear();
            if (!r) return false;
            r.GetSharedMaterials(mats);
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return false;
            for (int i = 0; i < mats.Count; i++) if (mats[i] && mats[i].HasProperty(FadeId)) return true;
            return false;
        }

        /// <summary>Writes the cut into every property block the renderer has (its own and each material's), so a block that overrides
        /// another still carries it; a renderer with none gets its own. The other values in each block are kept.</summary>
        public static void WriteFade(Renderer r, float v, int materials)
        {
            if (!r) return;
            bool wrote = false;
            if (r.HasPropertyBlock()) { r.GetPropertyBlock(mpb); mpb.SetFloat(FadeId, v); r.SetPropertyBlock(mpb); wrote = true; }
            for (int i = 0; i < materials; i++)
            {
                r.GetPropertyBlock(mpb, i);
                if (mpb.isEmpty) continue;
                mpb.SetFloat(FadeId, v); r.SetPropertyBlock(mpb, i); wrote = true;
            }
            if (!wrote) { mpb.Clear(); mpb.SetFloat(FadeId, v); r.SetPropertyBlock(mpb); }
            Writes++;
        }

        /// <summary>A break-up builds its pieces with a copy of the hull's paint block (OzShuttleBreakup.Break), and with it the cut this wrote
        /// there while the ship was near the eye: the pieces' painted faces would stay cut (their inner faces drawn) wherever they fall. Every
        /// block under root that carries a cut gets 0 (the other values kept; no block is made). Returns the blocks put back.</summary>
        public static int ClearCopiedCut(Transform root)
        {
            if (!root) return 0;
            int n = 0;
            root.GetComponentsInChildren(true, buf);
            for (int k = 0; k < buf.Count; k++)
            {
                var r = buf[k]; if (!r) continue;
                r.GetPropertyBlock(mpb);
                if (!mpb.isEmpty && mpb.HasFloat(FadeId) && mpb.GetFloat(FadeId) != 0f) { mpb.SetFloat(FadeId, 0f); r.SetPropertyBlock(mpb); n++; }
                mats.Clear(); r.GetSharedMaterials(mats);
                for (int i = 0; i < mats.Count; i++)
                {
                    r.GetPropertyBlock(mpb, i);
                    if (mpb.isEmpty || !mpb.HasFloat(FadeId) || mpb.GetFloat(FadeId) == 0f) continue;
                    mpb.SetFloat(FadeId, 0f); r.SetPropertyBlock(mpb, i); n++;
                }
            }
            CopiedCutsCleared += n;
            return n;
        }

        /// <summary>The cut a renderer's blocks carry now (-1: none carries it). For the checks.</summary>
        public static float ReadFade(Renderer r)
        {
            if (!r) return -1f;
            float v = -1f;
            r.GetPropertyBlock(mpb); if (!mpb.isEmpty && mpb.HasFloat(FadeId)) v = Mathf.Max(v, mpb.GetFloat(FadeId));
            mats.Clear(); r.GetSharedMaterials(mats);
            for (int i = 0; i < mats.Count; i++) { r.GetPropertyBlock(mpb, i); if (!mpb.isEmpty && mpb.HasFloat(FadeId)) v = Mathf.Max(v, mpb.GetFloat(FadeId)); }
            return v;
        }

        // ------------------------------------------------------------------ the tick
        void Tick(Camera cam)
        {
            Ticks++;
            if (piecesSeen.Count > 4096) piecesSeen.Clear();                                              // scenes come and go: never grows without end
            foreach (var p in OzShuttleBreakup.LastPieces)                                            // a new break-up: its pieces lose the copied cut before they are drawn
                if (p && piecesSeen.Add(p.GetInstanceID())) { ClearCopiedCut(p.transform); PiecesLookedAt++; }
            if (!On || !cam) { RestoreAll(); return; }
            var eye = cam.transform.position;
            seen.Clear();
            bool anyFaded = false, anyGone = false;
            for (int i = 0; i < OzPlayableShuttle.Count; i++)
            {
                var s = OzPlayableShuttle.At(i);
                if (!s || !s.isActiveAndEnabled || s.BrokenUp) continue;
                Consider(s.transform, s.transform.position + s.transform.up * ShipMiddleUpM, ShipRadiusM, eye, ref anyFaded, ref anyGone);
            }
            if (PiecesToo)
                foreach (var p in OzShuttleBreakup.LastPieces)
                    if (p && p.isActiveAndEnabled) Consider(p.transform, p.transform.position, PieceRadiusM, eye, ref anyFaded, ref anyGone);
            drop.Clear();
            foreach (var kv in recs)
            {
                if (!kv.Value.root) { Restore(kv.Value); drop.Add(kv.Key); continue; }
                if (!seen.Contains(kv.Key) && (kv.Value.cut > 0f || kv.Value.hidden.Count > 0)) Restore(kv.Value);
            }
            foreach (var k in drop) recs.Remove(k);
            if (anyFaded) FadedTicks++;
            if (anyGone) GoneTicks++;
        }

        void Consider(Transform root, Vector3 middle, float radius, Vector3 eye, ref bool anyFaded, ref bool anyGone)
        {
            int id = root.GetInstanceID();
            if ((middle - eye).magnitude - radius > ClearM) return;                                   // far: not seen, so put back if it was faded
            seen.Add(id);
            if (!recs.TryGetValue(id, out var rec)) recs[id] = rec = new Rec { root = root };
            Collect(rec);
            float d = BoxDistance(rec.fallback ? rec.meshes : rec.fade, eye);
            float cut = float.IsInfinity(d) ? 0f : CutAt(d);
            Apply(rec, cut, eye);
            LastBoxM = d; LastCut = cut; if (cut > PeakCut) PeakCut = cut;
            if (cut > 0f) anyFaded = true;
            if (cut >= 0.999f) anyGone = true;
        }

        /// <summary>The object's renderers, sorted again each time it is near (a part made or re-painted later is taken as it is now): those
        /// that take the fade, and the rest (particles, trails and lines among them).</summary>
        static void Collect(Rec rec)
        {
            rec.fade.Clear(); rec.other.Clear(); rec.meshes.Clear();
            rec.root.GetComponentsInChildren(true, buf);
            if (matCount.Count > 4096) matCount.Clear();                                              // scenes come and go: never grows without end
            for (int i = 0; i < buf.Count; i++)
            {
                var r = buf[i]; if (!r) continue;
                bool takes = TakesFade(r); matCount[r] = mats.Count;                                     // TakesFade filled mats with its materials
                (takes ? rec.fade : rec.other).Add(r);
                if (!takes && (r is MeshRenderer || r is SkinnedMeshRenderer)) rec.meshes.Add(r);
            }
            bool fb = rec.fade.Count == 0;
            if (fb && !rec.fallback) Fallbacks++;
            rec.fallback = fb;
        }

        void Apply(Rec rec, float cut, Vector3 eye)
        {
            bool on = cut > 0.0005f;
            if (!on && rec.cut <= 0f && rec.hidden.Count == 0 && rec.mvOff.Count == 0) return;
            for (int i = 0; i < rec.fade.Count; i++)
            {
                var r = rec.fade[i]; if (!r) continue;
                float want = on ? cut : 0f;
                if (on || (rec.written.TryGetValue(r, out float had) && had != 0f))
                { WriteFade(r, want, matCount.TryGetValue(r, out int n) ? n : 1); rec.written[r] = want; }    // every frame while faded: a writer that rebuilds its block drops it
                if (on) { if (r.motionVectorGenerationMode == MotionVectorGenerationMode.Object) { r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera; rec.mvOff.Add(r); } }
                else if (rec.mvOff.Remove(r) && r.motionVectorGenerationMode == MotionVectorGenerationMode.Camera) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            }
            float near2 = ClearM * ClearM;
            for (int i = 0; i < rec.other.Count; i++)
            {
                var r = rec.other[i]; if (!r) continue;
                bool hide = cut >= OtherHideAt && ((rec.fallback && (r is MeshRenderer || r is SkinnedMeshRenderer)) || r.bounds.SqrDistance(eye) < near2);   // the fallback: the whole hull goes at half way
                if (hide) { if (!r.forceRenderingOff) { r.forceRenderingOff = true; rec.hidden.Add(r); PartsHidden++; } }
                else if (rec.hidden.Remove(r)) r.forceRenderingOff = false;
            }
            rec.cut = on ? cut : 0f;
        }

        void Restore(Rec rec)
        {
            foreach (var kv in rec.written) if (kv.Key && kv.Value != 0f) WriteFade(kv.Key, 0f, matCount.TryGetValue(kv.Key, out int n) ? n : 1);
            rec.written.Clear();
            foreach (var r in rec.mvOff) if (r && r.motionVectorGenerationMode == MotionVectorGenerationMode.Camera) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            rec.mvOff.Clear();
            foreach (var r in rec.hidden) if (r) r.forceRenderingOff = false;
            rec.hidden.Clear();
            rec.cut = 0f; Restores++;
        }

        void RestoreAll() { foreach (var kv in recs) Restore(kv.Value); recs.Clear(); }

        /// <summary>For the checks: the cut of the object under root now (0: drawn as always, or not near the eye).</summary>
        public static float CutOf(Transform root) => inst && root && inst.recs.TryGetValue(root.GetInstanceID(), out var r) ? r.cut : 0f;
        /// <summary>For the checks: renderers this hides now (all objects).</summary>
        public static int HiddenNow() { int n = 0; if (inst) foreach (var kv in inst.recs) n += kv.Value.hidden.Count; return n; }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status() =>
            $"OzShuttleCamFade: {(On ? "on" : "off")}, fade {ClearM:0.#} -> {GoneM:0.#} m (part boxes), nearest box {(LastBoxM < 0f || float.IsInfinity(LastBoxM) ? "-" : LastBoxM.ToString("0.00"))} m, cut {LastCut:0.00} (peak {PeakCut:0.00}), " +
            $"ticks {Ticks} (faded {FadedTicks}, fully cut {GoneTicks}), block writes {Writes}, restores {Restores}, parts hidden {PartsHidden}, older-shader fallbacks {Fallbacks}, " +
            $"break-up pieces looked at {PiecesLookedAt} (copied cuts put back {CopiedCutsCleared})";
    }
}
