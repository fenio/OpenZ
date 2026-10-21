using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's own sun / moon shadow (rule DV118, K256g, view only): none while it flies (in the air its landing mark,
    /// OzShuttleShadow, shows where it can come down; two shadows read silly), eased in when it lands and out when it takes off, crossed
    /// with the mark: the ship's ground share k (OzPlayableShuttle.Touches.cs: 0 in the air .. 1 standing, eased over
    /// <see cref="OzPlayableShuttle.GroundFadeSec"/>, smoothstepped) gives this shadow k and the mark 1 - k, so the two never add up to more
    /// than one full shadow.
    /// <list type="bullet">
    /// <item>Standing (k = 1): the ship's renderers cast as they always did (their own modes, put back exactly).</item>
    /// <item>In the air (k = 0): every renderer of the ship that casts a shadow casts none (ShadowCastingMode.Off); the ship still receives
    /// shadows and lights as before.</item>
    /// <item>Between (the 0.5 s of a touchdown or a take-off): the renderers cast none, and the same meshes are drawn once more into the
    /// shadow maps only (Graphics.DrawMesh, ShadowCastingMode.ShadowsOnly) with OpenZ/ShadowFadeCaster (Resources/OpenZ/FX/
    /// M_OzShadowFadeCaster): a shadow-caster-only shader that keeps k of the shadow map's texels in a fixed 4 x 4 ordered pattern (the soft
    /// shadow filter reads it as a shadow k strong) and the ship's own caster bias (TeamLit's ShadowCaster pass), so at k = 1 it is the
    /// ship's own shadow texel for texel and the hand-back is seamless. URP has no per-object shadow strength: this dithered caster is
    /// the smooth way. Without the shader (not in the build) the shadow switches at the middle of the cross-fade (k 0.5), logged once.</item>
    /// </list>
    /// Renderers are read from the ship once (and again at each take-off: parts added since are taken in with their own mode).
    /// K256g2: at a kill in the air the pieces' renderers are taken in as well (<see cref="Adopt"/>: they start without a shadow and come
    /// in with the same dithered caster, OzShuttleShadowHandOver), and the ship's own casting parts are shared with its self-shadow map
    /// (<see cref="CasterParts"/>, OzShuttleSelfShadow).
    /// </summary>
    public sealed class OzShuttleRealShadow
    {
        public const string MaterialPath = "OpenZ/FX/M_OzShadowFadeCaster";
        /// <summary>Proof counters (every ship): frames drawn with the dithered caster, meshes drawn so, switches off / back on, the
        /// renderers taken over (last ship), times the shader was missing.</summary>
        public static int FadeFrames, FadeDraws, SwitchesOff, SwitchesOn, Missing, PartsLast;
        /// <summary>The dithered caster in between (false: one switch at the middle of the cross-fade instead, as without the shader).</summary>
        public static bool UseDither = true;

        readonly Transform root;
        readonly Dictionary<MeshRenderer, ShadowCastingMode> own = new Dictionary<MeshRenderer, ShadowCastingMode>();
        readonly List<(MeshRenderer r, MeshFilter f)> casters = new List<(MeshRenderer, MeshFilter)>();
        Material mat; bool ownOn = true, scanned, warned;
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        /// <summary>The ship's own shadow now (1 = as it always was, 0 = none).</summary>
        public float K { get; private set; } = 1f;
        /// <summary>The ship's renderers cast their own shadow now (k = 1).</summary>
        public bool OwnOn => ownOn;
        /// <summary>The soft (dithered) fade is there (false = it switches at the middle).</summary>
        public bool Soft => mat != null;
        public int Casters => casters.Count;
        /// <summary>K256g2: the casting parts (renderer, mesh filter): the ship's own and the adopted pieces'.</summary>
        public IReadOnlyList<(MeshRenderer r, MeshFilter f)> CasterParts => casters;
        /// <summary>K256g2, proof: pieces taken in (every ship).</summary>
        public static int Adopted;

        /// <summary>K256g2: the renderers under `holder` (a break-up's pieces) cast through this cross-fade from now on: off while it is
        /// under 1 (drawn by the dithered caster at k), their own mode back at 1. Returns how many were taken in.</summary>
        public int Adopt(Transform holder)
        {
            if (!holder) return 0;
            if (!scanned) Scan();
            int n = 0;
            foreach (var r in holder.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r || own.ContainsKey(r)) continue;
                own[r] = r.shadowCastingMode;
                var f = r.GetComponent<MeshFilter>();
                if (r.shadowCastingMode != ShadowCastingMode.Off && f && f.sharedMesh) { casters.Add((r, f)); n++; }
                if (!ownOn) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            Adopted += n;
            return n;
        }

        public OzShuttleRealShadow(Transform shipRoot)
        {
            root = shipRoot;
            var m = Resources.Load<Material>(MaterialPath);
            var sh = m && m.shader ? m.shader : Shader.Find("OpenZ/ShadowFadeCaster");
            if (sh && sh.isSupported) mat = new Material(sh) { name = "OZ_ShuttleShadowFade (runtime)", enableInstancing = false };
        }

        void Scan()
        {
            if (!root) return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r || own.ContainsKey(r)) continue;
                own[r] = r.shadowCastingMode;                                                                      // its own mode, put back exactly
                var f = r.GetComponent<MeshFilter>();
                if (r.shadowCastingMode != ShadowCastingMode.Off && f && f.sharedMesh) casters.Add((r, f));
            }
            PartsLast = casters.Count; scanned = true;
        }

        void SetOwn(bool on)
        {
            if (on == ownOn) return;
            if (!on) Scan();                                                                                      // a take-off: parts added since are taken in
            ownOn = on;
            foreach (var kv in own) if (kv.Key) kv.Key.shadowCastingMode = on ? kv.Value : ShadowCastingMode.Off;
            if (on) SwitchesOn++; else SwitchesOff++;
        }

        /// <summary>One frame (LateUpdate, after the ship's pose): k = the ship's ground share, smoothstepped (0 in the air .. 1 standing).</summary>
        public void Step(float k)
        {
            if (!root) return;
            if (!scanned) Scan();
            k = Mathf.Clamp01(k);
            if (k >= 0.999f) { SetOwn(true); K = 1f; return; }
            if (mat == null || !UseDither)
            {   // no fade shader in this build (or the dither switched off): one switch at the middle of the cross-fade
                if (!warned && mat == null) { warned = true; Missing++; Debug.LogWarning("[OpenZ] shuttle: the shadow fade shader OpenZ/ShadowFadeCaster is missing: the ship's own shadow switches at the middle of its cross-fade with the landing mark"); }
                SetOwn(k >= 0.5f); K = k >= 0.5f ? 1f : 0f; return;
            }
            SetOwn(false);
            K = k;
            if (k <= 0.001f) { K = 0f; return; }
            mat.SetFloat(FadeId, k);
            for (int i = 0; i < casters.Count; i++)
            {
                var (r, f) = casters[i];
                if (!r || !f || !r.enabled || !r.gameObject.activeInHierarchy) continue;                          // a hidden part casts nothing (as before)
                var mesh = f.sharedMesh; if (!mesh) continue;
                var m = r.localToWorldMatrix; int layer = r.gameObject.layer;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.DrawMesh(mesh, m, mat, layer, null, sub, null, ShadowCastingMode.ShadowsOnly, false);
                FadeDraws++;
            }
            FadeFrames++;
        }

        /// <summary>The ship's renderers cast as they always did (a break-up, the view going away).</summary>
        public void Restore() { SetOwn(true); K = 1f; }

        public void Dispose() { Restore(); if (mat) Object.Destroy(mat); mat = null; }
    }
}
