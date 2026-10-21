using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Up there its renderers cast nothing into the sun's shadow map
    /// (OzShuttleRealShadow: else the ground under it would get its real shadow next to the landing mark), and URP keeps one map per
    /// light that every receiver reads (its custom shadow layers choose casters, not receivers; TeamLit has no light-layer variant), so
    /// the map cannot shade the hull and spare the ground. This draws the ship's own small map instead, every frame while its real shadow
    /// is off or fading (k under 1):
    /// <list type="bullet">
    /// <item>its casting meshes (OzShuttleRealShadow.CasterParts under its root) along the main light (the sun, or the moon at night)
    /// into a <see cref="MapSize"/>-square float map (OpenZ/ShuttleSelfShadowDepth: the nearest depth per texel), an orthographic box
    /// round the ship's own bounds (+ <see cref="PadM"/>), with a command buffer run at once (Graphics.ExecuteCommandBuffer) after
    /// every other LateUpdate (the ship's final pose, a kickflip's too);</item>
    /// <item>only the ship's own TeamLit renderers read it (a property block, per material slot: TeamPaint's values are kept):
    /// OzShuttleSelfShadow.hlsl takes the main light's share out by (1 - its 3 x 3 filtered self-shadow), with the light's shadow strength
    /// and distance fade, so the pods shade the hull as they do standing (the off-Unity twin: 21 .. 27 % of its sunlit area, IoU 0.84 ..
    /// 0.90 with the ray-traced truth at 1024); the ground still gets nothing;</item>
    /// <item>standing (k = 1) it is off: the sun's map has the ship again. Off the screen it draws nothing.</item>
    /// </list>
    /// Switch: <see cref="Enabled"/> = false is K256g (no self-shadow in the air). Without the shader or a blendable half-float map
    /// it stays off (logged once).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class OzShuttleSelfShadow : MonoBehaviour
    {
        public static bool Enabled = true;
        public static int MapSize = 1024;
        /// <summary>The depth bias (m), the receiver's normal offset (m), the box's margin round the ship (m), the filter's tap step (texels).</summary>
        public static float BiasM = 0.015f, NormalM = 0.01f, PadM = 0.1f, TapTexels = 1.5f;
        public const string DepthMaterialPath = "OpenZ/FX/M_OzShuttleSelfShadowDepth";
        /// <summary>Proof: frames the map was drawn, meshes drawn into it, times it could not run (no shader / no format / no light),
        /// receivers on the last ship.</summary>
        public static int Frames, Draws, Unsupported, NoLight, ReceiversLast;
        /// <summary></summary>
        public static int SlotsSwitchedOff;
        public bool On { get; private set; }
        public RenderTexture Map => rt;
        public Matrix4x4 WorldToMap { get; private set; }
        public float RadiusM => radius;

        OzShuttleRealShadow src; Transform srcRoot; int fedFrame = -10; RenderTexture rt; Material depthMat; bool ownMat, broken, warned; CommandBuffer cmd;
        MaterialPropertyBlock mpb; readonly List<Renderer> receivers = new List<Renderer>(); readonly List<int> slots = new List<int>(); float radius = -1f, recvAt = -9f; Vector3 localCentre;
        static Light mainLight; static float lightLook = -9f;
        static readonly int OnId = Shader.PropertyToID("_OzSelfShadowOn"), TexId = Shader.PropertyToID("_OzSelfShadowTex"), MatId = Shader.PropertyToID("_OzSelfShadowMatrix"),
                            BiasId = Shader.PropertyToID("_OzSelfShadowBias"), ClipId = Shader.PropertyToID("_OzSSClip"), DepthId = Shader.PropertyToID("_OzSSDepth");

        void Awake() { if (mpb == null) mpb = new MaterialPropertyBlock(); }   // a MaterialPropertyBlock may not be made in a MonoBehaviour field initializer (it threw and left the lists unmade)

        public static OzShuttleSelfShadow For(OzPlayableShuttle s)
        {
            if (!s) return null;
            var c = s.GetComponent<OzShuttleSelfShadow>();
            return c ? c : s.gameObject.AddComponent<OzShuttleSelfShadow>();
        }

        /// <summary>Every frame by whoever drives the ship's real shadow (its LateUpdate, or the hand-over after a kill): the cross-fade
        /// (its K) and the ship's root. Not fed this frame = off.</summary>
        public void Feed(OzShuttleRealShadow real, Transform shipRoot) { src = real; srcRoot = shipRoot; fedFrame = Time.frameCount; }

        public void Stop() { src = null; SetOn(false); }

        /// <summary>
        /// it this map's switch, map and matrix while the ship flew: the pieces would keep reading the gone ship's frozen map (a ghost of
        /// its own shadow on the wreck, for its whole life under a low kill). The switch is set off on every renderer under holder (renderer
        /// and per-slot blocks; the paint in them kept). Returns the slots set off.</summary>
        public static int Scrub(Transform holder)
        {
            if (!holder) return 0;
            var b = new MaterialPropertyBlock(); int n = 0;
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (!r) continue;
                r.GetPropertyBlock(b);
                if (!b.isEmpty && b.GetFloat(OnId) > 0f) { b.SetFloat(OnId, 0f); r.SetPropertyBlock(b); n++; }
                int nm = r.sharedMaterials.Length;
                for (int i = 0; i < nm; i++)
                {
                    r.GetPropertyBlock(b, i);
                    if (b.isEmpty || b.GetFloat(OnId) <= 0f) continue;
                    b.SetFloat(OnId, 0f); r.SetPropertyBlock(b, i); n++;
                }
            }
            SlotsSwitchedOff += n;
            return n;
        }

        bool Ready()
        {
            if (broken) return false;
            if (!depthMat)
            {
                var m = Resources.Load<Material>(DepthMaterialPath);
                var sh = m && m.shader ? m.shader : Shader.Find("OpenZ/ShuttleSelfShadowDepth");
                bool fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RHalf);
                if (!sh || !sh.isSupported || !fmt)
                {
                    broken = true; Unsupported++;
                    Debug.LogWarning("[OpenZ] shuttle self-shadow: " + (!fmt ? "no blendable half-float render texture" : "the shader OpenZ/ShuttleSelfShadowDepth is missing") + ": no self-shadow in the air (as K256g)");
                    return false;
                }
                depthMat = m ? m : new Material(sh) { name = "OZ_ShuttleSelfShadowDepth (runtime)" }; ownMat = !m;
            }
            if (!rt)
            {
                rt = new RenderTexture(MapSize, MapSize, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear) { name = "OZ_ShuttleSelfShadowMap", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false };
                rt.Create();
            }
            cmd ??= new CommandBuffer { name = "OZ shuttle self-shadow" };
            return true;
        }

        static Light MainLight()
        {
            if (Time.unscaledTime - lightLook < 1f && (mainLight == null || mainLight)) return mainLight;
            lightLook = Time.unscaledTime;
            var s = RenderSettings.sun;
            if (s && s.isActiveAndEnabled && s.type == LightType.Directional) return mainLight = s;
            Light best = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l && l.isActiveAndEnabled && l.type == LightType.Directional && (!best || l.intensity > best.intensity)) best = l;
            return mainLight = best;
        }

        void Receivers()
        {
            if (Time.unscaledTime - recvAt < 5f && receivers.Count > 0) return;
            recvAt = Time.unscaledTime; receivers.Clear(); slots.Clear();
            foreach (var r in srcRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r) continue;
                var ms = r.sharedMaterials;                                                                     // read once here, not every frame (no garbage a frame)
                foreach (var m in ms) if (m && m.shader && m.shader.name == "OpenZ/TeamLit") { receivers.Add(r); slots.Add(Mathf.Max(1, ms.Length)); break; }
            }
            ReceiversLast = receivers.Count;
        }

        /// <summary>The ship's own box (root space), once: its casting parts' bounds.</summary>
        bool Measure()
        {
            if (radius > 0f) return true;
            bool any = false; Bounds b = default; var parts = src.CasterParts;
            for (int pi = 0; pi < parts.Count; pi++)
            {
                var (r, f) = parts[pi];
                if (!r || !f || !f.sharedMesh || !r.transform.IsChildOf(srcRoot)) continue;
                var mb = f.sharedMesh.bounds; var M = srcRoot.worldToLocalMatrix * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = M.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                }
            }
            if (!any) return false;
            localCentre = b.center; radius = b.extents.magnitude + PadM;
            return true;
        }

        void LateUpdate()
        {
            bool want = Enabled && src != null && srcRoot && fedFrame == Time.frameCount && src.K < 0.999f;
            if (!want || !Ready()) { SetOn(false); return; }
            var light = MainLight();
            if (!light || light.shadows == LightShadows.None) { if (!warned && !light) { warned = true; NoLight++; } SetOn(false); return; }
            Receivers();
            bool vis = false; foreach (var r in receivers) if (r && r.enabled && r.isVisible) { vis = true; break; }
            if (!vis || !Measure()) { SetOn(false); return; }
            Vector3 L = light.transform.forward, c = srcRoot.TransformPoint(localCentre); float R = radius * Mathf.Max(1f, srcRoot.lossyScale.x);
            var rot = Quaternion.LookRotation(L, Mathf.Abs(L.y) > 0.99f ? Vector3.forward : Vector3.up);
            var eye = c - L * (R + 0.5f); float far = 2f * R + 1f;
            var view = Matrix4x4.TRS(eye, rot, new Vector3(1f, 1f, -1f)).inverse;
            var proj = Matrix4x4.Ortho(-R, R, -R, R, 0.01f, far);
            var dv = new Vector4(L.x / far, L.y / far, L.z / far, -Vector3.Dot(L, eye) / far);         // depth01 = dot(L, w - eye) / far
            cmd.Clear();
            cmd.SetRenderTarget(rt);
            cmd.ClearRenderTarget(false, true, Color.white);
            cmd.SetGlobalMatrix(ClipId, GL.GetGPUProjectionMatrix(proj, true) * view);
            cmd.SetGlobalVector(DepthId, dv);
            int n = 0; var cp = src.CasterParts;
            for (int pi = 0; pi < cp.Count; pi++)                                                               // by index: no boxed enumerator a frame
            {
                var (r, f) = cp[pi];
                if (!r || !f || !r.enabled || !r.gameObject.activeInHierarchy || !r.transform.IsChildOf(srcRoot)) continue;
                var mesh = f.sharedMesh; if (!mesh) continue;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) cmd.DrawMesh(mesh, r.localToWorldMatrix, depthMat, sub, 0);
                n++;
            }
            if (n == 0) { SetOn(false); return; }
            Graphics.ExecuteCommandBuffer(cmd);
            Frames++; Draws += n;
            // world -> (u, v, depth01): uv in the layout a render texture drawn with GetGPUProjectionMatrix(.., true) has on every platform
            var uvm = Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.identity, new Vector3(0.5f, 0.5f, 1f)) * proj * view;
            uvm.SetRow(2, dv); uvm.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            WorldToMap = uvm;
            var bias = new Vector4(BiasM / far, TapTexels / MapSize, NormalM, 0f);
            for (int ri = 0; ri < receivers.Count; ri++)
            {
                var r = receivers[ri]; if (!r) continue;
                int nm = slots[ri];                                                                             // the slot count read with the receivers (no array a frame)
                for (int i = 0; i < nm; i++)
                {
                    r.GetPropertyBlock(mpb, i);
                    mpb.SetFloat(OnId, 1f); mpb.SetTexture(TexId, rt); mpb.SetMatrix(MatId, uvm); mpb.SetVector(BiasId, bias);
                    r.SetPropertyBlock(mpb, i);
                }
            }
            On = true;
        }

        void SetOn(bool on)
        {
            if (on || !On) return;
            for (int ri = 0; ri < receivers.Count; ri++)
            {
                var r = receivers[ri]; if (!r) continue;
                int nm = ri < slots.Count ? slots[ri] : Mathf.Max(1, r.sharedMaterials.Length);
                if (mpb == null) mpb = new MaterialPropertyBlock();
                for (int i = 0; i < nm; i++) { r.GetPropertyBlock(mpb, i); mpb.SetFloat(OnId, 0f); r.SetPropertyBlock(mpb, i); }
            }
            On = false;
        }

        void OnDestroy()
        {
            SetOn(false);
            if (rt) { rt.Release(); Destroy(rt); }
            if (ownMat && depthMat) Destroy(depthMat);
            cmd?.Release(); cmd = null;
        }
    }
}
