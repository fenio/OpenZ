using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// slows robots on water tiles to 0.7 (ZMapData.TileWalkSpeed, ZConst.WATER_SPEED) and vehicles never path into water, so
    /// there is no vehicle case. After UnitView has put a robot on the river bed, this lowers it so the water line sits at
    /// bank by the real water depth (no pop at the tile edge). While a robot is in the water: ripple rings around the legs
    /// (moving every .55 s, idle every 1.2 s), a small wake behind it, droplets + a ring on every footfall (foot bones), and a
    /// the body. Wade points are published to shaders (_OZWadePoints / _OZWadeCount) for an optional water-shader foam ring.
    /// </summary>
    [DefaultExecutionOrder(70)]   // after UnitView (60): UnitView places the robot, this lowers it into the water
    public class OpenZWading : MonoBehaviour
    {
        public static OpenZWading I { get; private set; }
        public static bool AutoCreate = true;

        [Range(0.2f, 0.7f)] public float sinkFraction = OpenZFxCatalog.WadeSinkFraction;
        public bool ripples = true, footSplashes = true, wake = true, enterSplash = true, sound = true, shaderHook = true;
        [Tooltip("Robot height used when the renderer bounds cannot be read (m)")] public float fallbackHeight = 2.4f;

        public int Wading { get; private set; }
        public float WaterY => waterY;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || I) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;
            new GameObject("OpenZWading").AddComponent<OpenZWading>();
        }

        sealed class Wader
        {
            public UnitView v; public float height = -1f, sink, wet; public bool wasWet, wasDying;
            public Transform footL, footR; public float relL, relR, lastStep = -10f, stepDist; public bool downL, downR; public int stepSide;
            public float ringT, wakeT; public int wakeSide; public Vector3 lastPos; public bool hasLast;
            public SkinnedMeshRenderer skin; public MaterialPropertyBlock block;
        }

        readonly Dictionary<UnitView, Wader> waders = new Dictionary<UnitView, Wader>();
        readonly List<UnitView> dead = new List<UnitView>();
        static readonly int AccentLoId = Shader.PropertyToID("_AccentLo"), AccentHiId = Shader.PropertyToID("_AccentHi");   // UnitView.cs:28
        static readonly int WadePointsId = Shader.PropertyToID("_OZWadePoints"), WadeCountId = Shader.PropertyToID("_OZWadeCount");
        readonly Vector4[] wadePoints = new Vector4[OpenZFxCatalog.WadeShaderPoints];
        float waterY = float.NegativeInfinity, cleanT;
        ZGame game;

        void Awake() { if (I && I != this) { Destroy(gameObject); return; } I = this; }
        void OnDestroy() { if (I == this) I = null; if (shaderHook) Shader.SetGlobalFloat(WadeCountId, 0f); }

        void Start() { game = ZGame.I; MeasureWater(); }

        void MeasureWater()
        {   // ZoneMarkers.cs:24-27: the flat water surface = lowest WAT_* renderer bounds + 2 cm
            float y = float.NegativeInfinity;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("WAT_") && r.gameObject.activeInHierarchy) y = Mathf.Max(y, r.bounds.min.y + 0.02f);
            waterY = y;
        }

        void Bind(Wader w)
        {
            foreach (var t in w.v.GetComponentsInChildren<Transform>(true))
            {   // mixamorigLeftFoot / mixamorig:LeftFoot / LeftFoot (Z_robot.prefab)
                if (!w.footL && t.name.EndsWith("LeftFoot")) w.footL = t;
                if (!w.footR && t.name.EndsWith("RightFoot")) w.footR = t;
            }
            w.skin = w.v.GetComponentInChildren<SkinnedMeshRenderer>();
            w.block = new MaterialPropertyBlock();
            float seed = (w.v.Obj.Id * 0.6180339f) % 1f;                                   // own phase per robot, never lockstep
            w.ringT = seed * OpenZFxCatalog.WadeRingMoveEvery; w.wakeT = seed * OpenZFxCatalog.WadeWakeEvery;
        }

        float MeasureHeight(Wader w)
        {   // top of the body above the feet, from the renderers (the armature carries a 1/60 scale, so local bounds lie)
            var t = w.v.transform; float top = float.NegativeInfinity;
            foreach (var r in w.v.GetComponentsInChildren<Renderer>())
                if (r.enabled && !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer)) top = Mathf.Max(top, r.bounds.max.y);
            float h = top - t.position.y;
            return float.IsInfinity(h) || float.IsNaN(h) ? fallbackHeight : Mathf.Clamp(h, 1.2f, 4.5f);
        }

        void LateUpdate()
        {
            if (game == null) game = ZGame.I;
            if (game == null || game.World == null) return;
            if (float.IsNegativeInfinity(waterY)) { if (shaderHook) Shader.SetGlobalFloat(WadeCountId, 0f); return; }
            float dt = Time.deltaTime; var fx = OpenZFxPlayer.I; var cam = Camera.main;
            int points = 0, wading = 0;
            foreach (var v in game.Views.Values)
            {
                if (!v || !v.isActiveAndEnabled || v.Obj == null || v.Obj.Kind != ZKind.Robot) continue;
                if (!waders.TryGetValue(v, out var w)) { w = new Wader { v = v }; Bind(w); waders[v] = w; }
                Step(w, dt, fx, cam);
                if (w.wet > 0.01f)
                {
                    wading++;
                    if (points < wadePoints.Length)
                    {
                        var p = v.transform.position;
                        wadePoints[points++] = new Vector4(p.x, waterY, p.z, w.wet * (v.Obj.IsMoving ? 1f : 0.4f));
                    }
                }
            }
            Wading = wading;
            for (int i = points; i < wadePoints.Length; i++) wadePoints[i] = Vector4.zero;
            if (shaderHook) { Shader.SetGlobalVectorArray(WadePointsId, wadePoints); Shader.SetGlobalFloat(WadeCountId, points); }
            if ((cleanT += dt) > 2f)
            {
                cleanT = 0f; dead.Clear();
                foreach (var kv in waders) if (!kv.Key) dead.Add(kv.Key);
                foreach (var k in dead) waders.Remove(k);
            }
        }

        void Step(Wader w, float dt, OpenZFxPlayer fx, Camera cam)
        {
            var v = w.v; var t = v.transform;
            if (v.Dying)
            {   // UnitView stops placing a dying robot: leave it where it is (already lowered); one splash as it falls in
                if (!w.wasDying && w.wet > 0.3f && fx) fx.Splash(new Vector3(t.position.x, waterY, t.position.z), 0.5f);
                w.wasDying = true; return;
            }
            w.wasDying = false;
            var p = t.position;                                                             // UnitView: ground + yOffset (+ recoil)
            bool waterTile = OpenZFxPlayer.IsWaterTile(p);
            float depth = waterTile ? waterY - p.y : 0f;                                    // water above the robot's feet on the bed
            float targetWet = depth > OpenZFxCatalog.WadeMinDepth ? Mathf.Clamp01((depth - OpenZFxCatalog.WadeMinDepth) / OpenZFxCatalog.WadeRampDepth) : 0f;
            // sits at the water surface (no carved bed under the robot) the depth reads ~0 and nothing sank. On a water tile a
            // robot is always wading: full wet, and the sink below brings the water line to half body (eased over .3 s).
            if (waterTile && depth < OpenZFxCatalog.WadeMinDepth + OpenZFxCatalog.WadeRampDepth) targetWet = 1f;
            float k = 1f - Mathf.Exp(-dt * 3f / OpenZFxCatalog.WadeBlendTime);           // ~95 % in .3 s
            w.wet = Mathf.Lerp(w.wet, targetWet, k);
            if (w.wet > 0.01f && w.height < 0f) w.height = MeasureHeight(w);
            float H = w.height > 0f ? w.height : fallbackHeight;
            float extra = Mathf.Max(0f, sinkFraction * H - Mathf.Max(0f, depth) + Mathf.Max(0f, -depth));   // water line at sinkFraction x height (feet above the surface: sink that too)
            w.sink = Mathf.Lerp(w.sink, targetWet * extra, k);
            if (w.sink > 0.001f)
            {
                t.position = p - Vector3.up * w.sink;
                if (w.skin)
                {   // the team-colour band was set from the unsunk height this frame (UnitView.cs:366-371): lower it with the body
                    w.skin.GetPropertyBlock(w.block, 0);
                    float lo = w.block.GetFloat(AccentLoId), hi = w.block.GetFloat(AccentHiId);
                    if (hi > lo) { w.block.SetFloat(AccentLoId, lo - w.sink); w.block.SetFloat(AccentHiId, hi - w.sink); w.skin.SetPropertyBlock(w.block, 0); }
                }
            }

            // ---- FX (only on screen)
            bool moving = v.Obj.IsMoving;
            var pos = t.position; var surf = new Vector3(pos.x, waterY, pos.z);
            var move = w.hasLast ? pos - w.lastPos : Vector3.zero; move.y = 0f;
            w.lastPos = pos; w.hasLast = true;
            bool wet = w.wet > 0.5f;
            if (enterSplash && wet && !w.wasWet && fx && OnScreen(cam, surf))
            {
                fx.Splash(surf, OpenZFxCatalog.WadeEnterSplash);
                if (sound && CombatAudio.I) CombatAudio.I.Play("swim", surf, OpenZFxCatalog.WadeSwimVolume, 0.5f, v.Obj.Id, OzSoundV3.BankSwim);   // sound_v3
            }
            w.wasWet = wet;
            if (!wet || !fx || !OnScreen(cam, surf)) { w.downL = w.downR = false; return; }

            if (ripples)
            {
                w.ringT -= dt;
                if (w.ringT <= 0f)
                {
                    w.ringT = moving ? OpenZFxCatalog.WadeRingMoveEvery : OpenZFxCatalog.WadeRingIdleEvery;
                    fx.Ripple(surf, moving ? OpenZFxCatalog.WadeRingMoveStrength : OpenZFxCatalog.WadeRingIdleStrength, OpenZFxCatalog.RippleLife, OpenZFxCatalog.WadeRingLift);
                }
            }
            if (wake && moving && move.sqrMagnitude > 1e-6f)
            {   // small wake: alternating rings behind the robot (coast wake: moving body every 250 ms)
                w.wakeT -= dt;
                if (w.wakeT <= 0f)
                {
                    w.wakeT = OpenZFxCatalog.WadeWakeEvery;
                    var fwd = move.normalized; var side = Vector3.Cross(Vector3.up, fwd);
                    w.wakeSide = w.wakeSide == 1 ? -1 : 1;
                    var wp = surf - fwd * OpenZFxCatalog.WadeWakeBack + side * OpenZFxCatalog.WadeWakeSide * w.wakeSide;
                    fx.Ripple(wp, OpenZFxCatalog.WadeWakeStrength, OpenZFxCatalog.WadeWakeLife, OpenZFxCatalog.WadeRingLift);
                }
            }
            if (footSplashes && moving) FootSteps(w, fx, surf, move);
        }

        void FootSteps(Wader w, OpenZFxPlayer fx, Vector3 surf, Vector3 move)
        {
            var t = w.v.transform;
            float now = Time.time;
            if (w.footL && w.footR)
            {   // a footfall = the foot's lowest point relative to the root (it stops going down and starts lifting)
                float rl = w.footL.position.y - t.position.y, rr = w.footR.position.y - t.position.y;
                bool dl = rl < w.relL - 1e-4f, dr = rr < w.relR - 1e-4f;
                if (w.downL && !dl) FootFall(w, fx, w.footL.position, now);
                if (w.downR && !dr) FootFall(w, fx, w.footR.position, now);
                w.downL = dl; w.downR = dr; w.relL = rl; w.relR = rr;
                return;
            }
            // no foot bones: one step every .8 m, alternating sides
            w.stepDist += move.magnitude;
            if (w.stepDist < OpenZFxCatalog.WadeStepCadenceM) return;
            w.stepDist = 0f; w.stepSide = w.stepSide == 1 ? -1 : 1;
            var side = Vector3.Cross(Vector3.up, move.sqrMagnitude > 1e-6f ? move.normalized : t.forward) * 0.25f * w.stepSide;
            FootFall(w, fx, surf + side, now);
        }

        void FootFall(Wader w, OpenZFxPlayer fx, Vector3 foot, float now)
        {
            if (now - w.lastStep < OpenZFxCatalog.WadeStepMinInterval) return;
            w.lastStep = now;
            var at = new Vector3(foot.x, waterY, foot.z);
            fx.Droplets(at, OpenZFxCatalog.WadeStepSplash, OpenZFxCatalog.WadeStepDroplets);   // 4-6 droplets per footfall
            fx.Ripple(at, OpenZFxCatalog.WadeStepRipple, OpenZFxCatalog.RippleLife * 0.7f, OpenZFxCatalog.WadeRingLift);
        }

        static bool OnScreen(Camera cam, Vector3 p)
        {
            if (!cam) return true;
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
        }
    }
}
