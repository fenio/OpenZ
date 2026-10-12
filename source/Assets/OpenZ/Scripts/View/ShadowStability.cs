using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Stable sun shadows: small sunlit holes and slits in the shadows jittered from frame to frame. The cause was URP's
    /// caster-side normal bias, which pushes every caster vertex inward along its own normal; the flat-shaded low-poly meshes (cliffs, rocks, radar: 71-100 % of the welded
    /// vertices on a hard edge) therefore tore open along every facet edge in the shadow map, and those sub-texel slits
    /// re-rasterised on every sun or dish step. Two-sided casting could not close them on the open cliff shells (the same torn
    /// triangles, drawn from both sides), nor could the 0.25 deg sun steps (they only bunched the flicker into visible jumps).
    /// The fix lives in OpenZ/TeamLit (patch 01): the sun's caster keeps the depth bias only, the receiver takes the normal offset.
    /// This class (a) still makes the open terrain shells cast TWO-SIDED (an open wall whose faces look away from the sun would
    /// otherwise cast no shadow at all) and (b) hands TeamLit the receiver offset URP would have used: _OZShadowRecvBias =
    /// normal bias x URP's soft-shadow kernel (ShadowUtils.GetShadowBias: 2.5 for the sun's "use pipeline settings" quality).
    /// Runs on every level load and again a few seconds in (rock pillars and cliff kits are built at start).
    /// </summary>
    public class ShadowStability : MonoBehaviour
    {
        static readonly string[] EnvKeys = { "cliff", "canyon", "rock", "pocket_cover", "dune", "mesa", "ridge", "pillar", "boulder" };
        static readonly int RecvBiasId = Shader.PropertyToID("_OZShadowRecvBias");
        public static int LastCount { get; private set; }
        /// <summary>Last receiver normal offset handed to TeamLit (shadow texels at N.L = 0).</summary>
        public static float LastRecvBias { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded;
            Install();
        }
        static void OnLoaded(Scene s, LoadSceneMode m) => Install();
        static void Install() { if (!FindFirstObjectByType<ZGame>()) return; new GameObject("OZ_ShadowStability").AddComponent<ShadowStability>(); }

        System.Collections.IEnumerator Start()
        {
            yield return null; Apply();
            yield return new WaitForSeconds(3f); Apply();
            Destroy(gameObject, 1f);
        }

        public static int Apply()
        {
            SyncReceiverBias();
            int n = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.shadowCastingMode != ShadowCastingMode.On) continue;
                var root = r.transform.root.name;
                if (root != "Environment" && root != "ZodObjects" && root != "RockChunk" && !root.StartsWith("Cliffs")) continue;
                if (!IsTerrainCaster(r.transform)) continue;
                r.shadowCastingMode = ShadowCastingMode.TwoSided; n++;
            }
            LastCount = n;
            return n;
        }

        /// <summary>The normal offset URP would have applied to the sun's casters, in shadow texels (x (1 - N.L) in the shader):
        /// the same bias source (asset or light) and the same kernel scale as ShadowUtils.GetShadowBias.</summary>
        public static float SyncReceiverBias()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var dn = FindFirstObjectByType<DayNight>();
            var sun = dn && dn.sun ? dn.sun : RenderSettings.sun;
            float nb = urp ? urp.shadowNormalBias : 0.3f;
            float kernel = 2.5f;
            if (sun && sun.TryGetComponent(out UniversalAdditionalLightData ald))
            {
                if (!ald.usePipelineSettings) nb = sun.shadowNormalBias;
                kernel = ald.softShadowQuality == SoftShadowQuality.High ? 3.5f : ald.softShadowQuality == SoftShadowQuality.Low ? 1.5f : 2.5f;
            }
            if ((urp && !urp.supportsSoftShadows) || (sun && sun.shadows != LightShadows.Soft)) kernel = 1f;
            LastRecvBias = Mathf.Max(1e-4f, nb * kernel);                                   // 0 would mean "unset" (shader default 0.75)
            Shader.SetGlobalFloat(RecvBiasId, LastRecvBias);
            return LastRecvBias;
        }

        static bool IsTerrainCaster(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                var name = p.name.ToLowerInvariant();
                foreach (var k in EnvKeys) if (name.Contains(k)) return true;
                if (name == "ter_cliffs" || name.StartsWith("cliffsv")) return true;
            }
            return false;
        }
    }
}
