using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// fort and factory doors. Each door gets a small patch (about 2-4 m) of soft robot boot prints and a few scuffs in front of its
    /// DRAWN door (FactoryView.DoorOnGround; Zod's footprint edge if a building has no rig), walking out along Zod's creation point ->
    /// exit, densest at the threshold and fading into the ground. Never long lines or paths between buildings (WL1: BasePropDecor
    /// stays off), never on water or roads (the Zod tile classes), hidden under the building itself by the depth test, faint on hard
    /// ground (TrackSurface.MarkStrength). Drawn with TrackMarks' own OpenZ/TrackMark
    /// look (multiplies the lit ground, ZTest Always + the camera depth texture with a 10 cm tolerance: never z-fights, never paints a
    /// unit standing on it), each print's corners on the RENDERED terrain + 2 cm, so it conforms. View only, never the sim; one static
    /// mesh, one draw call per level.
    /// </summary>
    public class DoorWear : MonoBehaviour
    {
        public static bool On = true;
        public static DoorWear I { get; private set; }
        public int doors, prints, skipped;
        /// <summary>Door thresholds (world, y = 0) and their exit directions, for the proof shots.</summary>
        public readonly List<Vector3> Doors = new List<Vector3>();
        public readonly List<Vector3> DoorDirs = new List<Vector3>();
        /// <summary>Start0: the first steps land just past the door's ramp / sill (a print under the ramp is hidden by the depth test
        /// </summary>
        const float Lift = 0.02f, BermPad = 0.18f, Reach = 3.0f, Start0 = 0.7f;
        const int Steps = 30, Scuffs = 6;

        Mesh mesh; Material mat; TrackSurface surf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) OnLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }

        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            if (!On || !Application.isPlaying || !s.IsValid()) return;
            if (!(s.name.StartsWith("L02") || s.name.StartsWith("L16") || s.name.StartsWith("L17"))) return;
            if (I && I.gameObject.scene == s) return;
            var go = new GameObject("DoorWear");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<DoorWear>();
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; if (mesh) Destroy(mesh); if (mat) Destroy(mat); }

        IEnumerator Start()
        {
            // the world's buildings and TrackMarks' rendered terrain heights (rasterised at its Start): wait up to 8 s
            float t0 = Time.realtimeSinceStartup;
            while (!(ZGame.I && ZGame.I.World != null && TrackMarks.Active && TrackMarks.I.Surface != null) && Time.realtimeSinceStartup - t0 < 8f) yield return null;
            // the factory / fort rigs (FactoryView: the drawn door); a level without rigs falls back to Zod's footprint edge
            while (FindObjectsByType<FactoryView>(FindObjectsSortMode.None).Length == 0 && Time.realtimeSinceStartup - t0 < 8f) yield return null;
            yield return null;
            try { Build(); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ] door wear (GW1): " + e.Message); }
        }

        void Build()
        {
            var g = ZGame.I;
            if (!g || g.World == null) { Debug.Log("[OpenZ] door wear (GW1): no world, nothing built"); return; }
            var W = g.World;
            surf = TrackMarks.Active ? TrackMarks.I.Surface : null;
            var v = new List<Vector3>(); var c = new List<Color32>(); var u0 = new List<Vector2>(); var u1 = new List<Vector4>(); var tri = new List<int>();
            var rigs = new Dictionary<int, FactoryView>();
            foreach (var fv in FindObjectsByType<FactoryView>(FindObjectsSortMode.None)) if (fv.B != null) rigs[fv.B.Id] = fv;
            int drawn = 0;
            foreach (var b in W.Objects)
            {
                if (b.Kind != ZKind.Building || b.Destroyed) continue;
                if (!b.GetCreationPoint(out int cx, out int cy) || !b.GetCreationMovePoint(out int ex, out int ey)) continue;   // forts + factories only
                var a3 = ZGame.PxToWorld(cx, cy); var e3 = ZGame.PxToWorld(ex, ey);
                var a = new Vector2(a3.x, a3.z); var e = new Vector2(e3.x, e3.z);
                if ((e - a).sqrMagnitude < 0.01f) continue;
                var dir = (e - a).normalized;
                var door = Threshold(W, a, e, dir); float width = 2.4f;
                // the DRAWN door (the model is smaller than Zod's footprint: its door sits up to ~2 m inside the footprint edge)
                if (rigs.TryGetValue(b.Id, out var rig) && rig.DoorOnGround(out var dc, out var dd, out float dwid))
                { door = new Vector2(dc.x, dc.z); dir = new Vector2(dd.x, dd.z).normalized; width = Mathf.Clamp(dwid, 1.2f, 5f); drawn++; }
                Doors.Add(new Vector3(door.x, 0f, door.y)); DoorDirs.Add(new Vector3(dir.x, 0f, dir.y));
                Patch(W, door, dir, width, new System.Random(b.Id * 7919 + 17), v, c, u0, u1, tri);
                doors++;
            }
            prints = tri.Count / 6;
            if (v.Count > 0) BuildMesh(v, c, u0, u1, tri);
            Debug.Log($"[OpenZ] door wear (GW1): {doors} doors ({drawn} at the drawn door, {doors - drawn} at Zod's footprint edge), {prints} prints, {skipped} skipped (road / water / building / deck / too faint), heights from {(surf != null ? (surf.RenderHeights ? "rendered TER_*" : "COL_Terrain") : "ZGame.GroundY")}");
        }

        /// <summary>From the exit point back toward the creation point while the tile is walkable: the door's outer threshold.</summary>
        static Vector2 Threshold(ZWorld W, Vector2 a, Vector2 e, Vector2 dir)
        {
            var p = e; float len = (e - a).magnitude;
            for (float s = 0f; s <= len; s += 0.25f)
            {
                var q = e - dir * s;
                int tx = Mathf.FloorToInt(q.x * 0.5f), ty = Mathf.FloorToInt(-q.y * 0.5f);
                if (!W.Path.TilePassable(tx, ty, true)) break;
                p = q;
            }
            return p;
        }

        /// <summary>Road or water (Zod's tile classes): no wear there. Under the building itself the prints are hidden by the
        /// camera depth test (a print only draws where the ground is the nearest surface).</summary>
        static bool Blocked(ZWorld W, Vector2 p)
        {
            int tx = Mathf.FloorToInt(p.x * 0.5f), ty = Mathf.FloorToInt(-p.y * 0.5f);
            var k = W.Map.ClassAt(tx, ty);
            return k == ZClass.Road || k == ZClass.Water || k == ZClass.DeepWater;
        }

        static Vector2 Rot(Vector2 d, float a) => new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));

        void Patch(ZWorld W, Vector2 door, Vector2 dir, float width, System.Random rnd, List<Vector3> v, List<Color32> c, List<Vector2> u0, List<Vector4> u1, List<int> tri)
        {
            var side = new Vector2(-dir.y, dir.x);
            float half = Mathf.Clamp(width * 0.4f, 0.6f, 1.8f);
            // steps: Start0 .. Start0 + Reach m out, denser at the threshold; the fan widens a little outward; more walked out than in
            for (int i = 0; i < Steps; i++)
            {
                float u = (float)rnd.NextDouble(), w = (float)rnd.NextDouble() * 2f - 1f;
                float along = Start0 + Reach * Mathf.Pow(u, 1.25f);
                float lat = w * (half + 0.25f * along);
                var p = door + dir * along + side * lat;
                bool outward = rnd.NextDouble() < 0.62;
                float yaw = ((float)rnd.NextDouble() - 0.5f) * 44f * Mathf.Deg2Rad + (outward ? 1f : -1f) * lat * 0.12f;
                var f = Rot(outward ? dir : -dir, yaw);
                float fade = 1f - Mathf.Clamp01((along - Start0 * 0.5f) / (Reach + 1f));
                Print(W, p, f, 0.30f, 0.16f, 0.95f * fade * (0.72f + 0.28f * (float)rnd.NextDouble()), 2f + 0.13f + 0.5f * (float)rnd.NextDouble(), v, c, u0, u1, tri);
            }
            // scuffs: shuffled, dragged steps right at the threshold (longer, fainter)
            for (int i = 0; i < Scuffs; i++)
            {
                float u = (float)rnd.NextDouble(), w = (float)rnd.NextDouble() * 2f - 1f;
                float along = Start0 + 1.2f * u, lat = w * half * 0.8f;
                var p = door + dir * along + side * lat;
                var f = Rot(rnd.NextDouble() < 0.5 ? dir : -dir, ((float)rnd.NextDouble() - 0.5f) * 70f * Mathf.Deg2Rad);
                float fade = 1f - Mathf.Clamp01((along - Start0 * 0.5f) / (Reach + 1f));
                Print(W, p, f, 0.46f, 0.19f, 0.6f * fade, 2f + 0.61f + 0.3f * (float)rnd.NextDouble(), v, c, u0, u1, tri);
            }
        }

        void Print(ZWorld W, Vector2 p, Vector2 f, float L, float Wd, float str, float kind, List<Vector3> v, List<Color32> c, List<Vector2> u0, List<Vector4> u1, List<int> tri)
        {
            float pl = L * (1f + 2f * BermPad), pw = Wd * (1f + 2f * BermPad);
            var n = new Vector2(-f.y, f.x);
            var heel = p - f * (pl * 0.5f); var toe = p + f * (pl * 0.5f);
            Vector2[] q = { heel - n * (pw * 0.5f), heel + n * (pw * 0.5f), toe - n * (pw * 0.5f), toe + n * (pw * 0.5f) };
            Vector2[] uv = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            var ys = new float[4]; float kmin = 1f;
            for (int j = 0; j < 4; j++)
            {
                if (Blocked(W, q[j]) || !Ground(q[j].x, q[j].y, out ys[j], out float sj)) { skipped++; return; }
                kmin = Mathf.Min(kmin, sj);
            }
            float k = str * kmin;
            if (k < 0.03f) { skipped++; return; }
            byte al = (byte)Mathf.RoundToInt(Mathf.Clamp01(k) * 255f);
            var d = new Vector4(-1f, 1e8f, 1e9f, kind);                                                     // born, endless, robot boot
            int b0 = v.Count;
            for (int j = 0; j < 4; j++) { v.Add(new Vector3(q[j].x, ys[j] + Lift, q[j].y)); c.Add(new Color32(255, 255, 255, al)); u0.Add(uv[j]); u1.Add(d); }
            tri.Add(b0); tri.Add(b0 + 2); tri.Add(b0 + 1); tri.Add(b0 + 1); tri.Add(b0 + 2); tri.Add(b0 + 3);
        }

        bool Ground(float x, float z, out float y, out float strength)
        {
            strength = 1f; y = 0f;
            if (surf != null)
            {
                bool ok = surf.Sample(x, z, out y, out bool deck);
                if (deck) { strength = 0f; return false; }
                strength = surf.MarkStrength(x, z);
                return ok;
            }
            if (ZGame.I) { y = ZGame.I.GroundY(x, z); return true; }
            return false;
        }

        void BuildMesh(List<Vector3> v, List<Color32> c, List<Vector2> u0, List<Vector4> u1, List<int> tri)
        {
            var src = Resources.Load<Material>("OpenZ/FX/M_TrackMark");
            if (src && src.shader && src.shader.name == "OpenZ/TrackMark") mat = new Material(src);
            else { var sh = Shader.Find("OpenZ/TrackMark"); if (sh) mat = new Material(sh); }
            if (!mat) { Debug.LogWarning("[OpenZ] door wear (GW1): OpenZ/TrackMark shader missing, no door wear"); return; }
            mat.name = "DoorWear";
            // the old paths' look (0.95); the patch stays soft through its fade, density and size, not through a paler print
            mat.SetFloat("_Intensity", 0.95f); mat.SetFloat("_Compress", 0.24f); mat.SetFloat("_Groove", 0.1f);
            mat.SetFloat("_Berm", 0.06f); mat.SetFloat("_BermPad", BermPad); mat.SetFloat("_FadeStart", 1f);
            mat.SetFloat("_DissolveScale", 2.6f);
            mat.SetFloat("_DepthTolerance", surf != null && surf.RenderHeights ? 0.1f : 0.18f);
            bool sceneDepth = true;
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) sceneDepth = urp.supportsCameraDepthTexture;
            mat.SetFloat("_SceneDepthTest", sceneDepth ? 1f : 0f);
            mat.SetFloat("_ZTest", (float)(sceneDepth ? CompareFunction.Always : CompareFunction.LessEqual));
            mesh = new Mesh { name = "DoorWear", indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, u0); mesh.SetUVs(1, u1); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("DoorWearPrints"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
