using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// GW1's door prints (DoorWear) were invisible at the RTS camera, so every factory / fort door
    /// now also gets a soft trodden fan (L02: lighter scuffed sand, clumps, one tyre / track scuff pair; L17: dusty concrete
    /// scuff + dirt tracking), and every bridge gets sand / dust drifts on its deck ends and ramps spilling onto the approach,
    /// a light dust film, darker worn wheel lanes and a few small bright metal scratches (per bridge seed, deterministic).
    /// Terrain / deck conforming grid meshes lifted 3 cm (deck heights raycast on the drawn intact plate through a temporary
    /// MeshCollider, ground heights from TrackSurface's rendered TER_* heights), drawn with OpenZ/Grime (2x multiply: keeps the
    /// lit ground's shading day and night, Offset, no depth write: never z-fights). The deck part draws only while the bridge's
    /// intact plate is shown (destroyed / waiting for repair = none). View only; Graphics.DrawMesh, no renderer or collider stays
    /// in the scene.
    /// </summary>
    [DefaultExecutionOrder(97)]
    public class GroundGrime : MonoBehaviour
    {
        public static bool On = true;
        /// <summary>Draw switch for before / after proof frames.</summary>
        public static bool Show = true;
        public static GroundGrime I { get; private set; }
        public int doorPatches, bridgesBuilt, deckTris, approachTris;
        const float Lift = 0.03f, Cell = 0.3f, Approach = 3.2f;

        sealed class Br { public string name; public Transform root; public Renderer plate; public Mesh deck, ground; public int layer; public bool tried; }
        readonly List<Br> bridges = new List<Br>();
        Mesh doorMesh; Material mat; TrackSurface surf; ZWorld W; bool city, ready; float nextScan;

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
            if (!(s.name.StartsWith("L02") || s.name.StartsWith("L17"))) return;
            if (I && I.gameObject.scene == s) return;
            var go = new GameObject("GroundGrime");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<GroundGrime>();
        }

        void Awake() { I = this; }
        void OnDestroy()
        {
            if (I == this) I = null;
            if (doorMesh) Destroy(doorMesh); if (mat) Destroy(mat);
            foreach (var b in bridges) { if (b.deck) Destroy(b.deck); if (b.ground) Destroy(b.ground); }
        }

        IEnumerator Start()
        {
            float t0 = Time.realtimeSinceStartup;
            while (!(ZGame.I && ZGame.I.World != null && TrackMarks.Active && TrackMarks.I.Surface != null) && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            while (!(DoorWear.I && DoorWear.I.Doors.Count > 0) && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            yield return null;
            try { Init(); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ] ground grime: " + e); }
        }

        void Init()
        {
            W = ZGame.I ? ZGame.I.World : null;
            if (W == null) { Debug.Log("[OpenZ] ground grime: no world"); return; }
            surf = TrackMarks.Active ? TrackMarks.I.Surface : null;
            city = W.Map.planet == "city";
            var src = Resources.Load<Material>("OpenZ/FX/M_Grime");
            mat = src ? new Material(src) : null;
            if (!mat) { var sh = Shader.Find("OpenZ/Grime"); if (sh) mat = new Material(sh); }
            if (!mat) { Debug.LogWarning("[OpenZ] ground grime: OpenZ/Grime shader missing"); return; }
            mat.name = "GroundGrime";
            BuildDoors();
            var root = GameObject.Find("ZodObjects");
            if (root) foreach (Transform t in root.transform)
                    if (t.name.StartsWith("bridge_") && !t.name.EndsWith("_intact") && !t.name.EndsWith("_damaged") && !t.name.EndsWith("_destroyed"))
                        bridges.Add(new Br { name = t.name, root = t });
            ready = true;
            Scan();
            Debug.Log($"[OpenZ] ground grime ({(city ? "L17 dusty scuff" : "L02 trodden sand")}): {doorPatches} door patches ({doorMesh?.vertexCount ?? 0} v), {bridges.Count} bridges ({bridgesBuilt} built now, deck {deckTris} / approach {approachTris} tris)");
        }

        // ------------------------------------------------------------------------------------------ ground helpers
        bool Water(float x, float z)
        {
            int tx = Mathf.FloorToInt(x * 0.5f), ty = Mathf.FloorToInt(-z * 0.5f);
            var k = W.Map.ClassAt(tx, ty);
            return k == ZClass.Water || k == ZClass.DeepWater;
        }

        float GroundY(float x, float z)
        {
            if (surf != null && surf.RenderY(x, z, out float y)) return y;
            if (surf != null && surf.Sample(x, z, out float y2, out bool _)) return y2;
            return ZGame.I ? ZGame.I.GroundY(x, z) : 0f;
        }

        // ------------------------------------------------------------------------------------------ door fans
        void BuildDoors()
        {
            var dw = DoorWear.I; if (!dw) return;
            var v = new List<Vector3>(); var c = new List<Color32>(); var u0 = new List<Vector2>(); var u1 = new List<Vector2>(); var u2 = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i < dw.Doors.Count; i++)
            {
                var door = new Vector2(dw.Doors[i].x, dw.Doors[i].z); var dir = new Vector2(dw.DoorDirs[i].x, dw.DoorDirs[i].z).normalized;
                if (dir.sqrMagnitude < 0.5f) continue;
                var side = new Vector2(-dir.y, dir.x);
                float seed = (i * 7.31f + door.x * 0.17f + door.y * 0.29f) % 97f;
                const float a0 = -0.3f, a1 = 5.1f, l1 = 3.3f;
                int na = Mathf.CeilToInt((a1 - a0) / Cell), nl = Mathf.CeilToInt(2f * l1 / Cell);
                int b0 = v.Count;
                for (int ia = 0; ia <= na; ia++)
                    for (int il = 0; il <= nl; il++)
                    {
                        float a = a0 + (a1 - a0) * ia / na, l = -l1 + 2f * l1 * il / nl;
                        var p = door + dir * a + side * l;
                        // alpha = where wear may show (ground factor x the grid's own edge); the fan itself is shaped in the shader
                        float m = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a1 - 0.5f, a1, a))) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(l1 - 0.5f, l1, Mathf.Abs(l))));
                        float y = GroundY(p.x, p.y);
                        if (Water(p.x, p.y)) m = 0f;
                        else if (!city && surf != null) m *= Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(surf.MarkStrength(p.x, p.y) * 1.25f));   // faint on the roads
                        if (surf != null && surf.Sample(p.x, p.y, out float _, out bool deck) && deck) m = 0f;
                        v.Add(new Vector3(p.x, y + Lift, p.y)); c.Add(new Color32(0, 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f)));
                        u0.Add(new Vector2(a, l)); u1.Add(new Vector2(city ? 1f : 0f, seed)); u2.Add(Vector2.zero);
                    }
                for (int ia = 0; ia < na; ia++)
                    for (int il = 0; il < nl; il++)
                    {
                        int q = b0 + ia * (nl + 1) + il;
                        if (c[q].a == 0 && c[q + 1].a == 0 && c[q + nl + 1].a == 0 && c[q + nl + 2].a == 0) continue;
                        tri.Add(q); tri.Add(q + nl + 1); tri.Add(q + 1); tri.Add(q + 1); tri.Add(q + nl + 1); tri.Add(q + nl + 2);
                    }
                doorPatches++;
            }
            if (tri.Count == 0) return;
            doorMesh = MakeMesh("GroundGrime_Doors", v, c, u0, u1, u2, tri);
        }

        static Mesh MakeMesh(string name, List<Vector3> v, List<Color32> c, List<Vector2> u0, List<Vector2> u1, List<Vector2> u2, List<int> tri)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, u0); mesh.SetUVs(1, u1); mesh.SetUVs(2, u2); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------------------------------ bridges
        /// <summary>The bridge's INTACT plate: in the placed view itself, or RulesView's sibling "&lt;bridge&gt;_intact" variant made at
        /// run time (L17's bridges start destroyed: the wear appears with the rebuilt deck).</summary>
        void Scan()
        {
            Transform zo = null;
            foreach (var b in bridges)
            {
                if (b.tried || !b.root) continue;
                if (!zo) zo = b.root.parent;
                if (!zo) continue;
                foreach (Transform t in zo)
                {
                    if (!t.name.StartsWith(b.name, System.StringComparison.Ordinal)) continue;
                    foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (!mf.name.Contains("intact_plate") || !mf.sharedMesh) continue;
                        var r = mf.GetComponent<Renderer>(); if (!r) continue;
                        b.plate = r; b.layer = mf.gameObject.layer; b.tried = true;
                        try { BuildBridge(b, mf); } catch (System.Exception e) { Debug.LogWarning("[OpenZ] ground grime bridge " + b.name + ": " + e.Message); }
                        break;
                    }
                    if (b.tried) break;
                }
            }
        }

        void BuildBridge(Br b, MeshFilter mf)
        {
            // the drawn plate's top: a temporary MeshCollider on its own object (Collider.Raycast needs no layer), removed at once
            var probe = new GameObject("GroundGrimeProbe") { hideFlags = HideFlags.HideAndDontSave };
            probe.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
            probe.transform.localScale = mf.transform.lossyScale;
            var mc = probe.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            Physics.SyncTransforms();
            try
            {
                var bb = b.plate.bounds;
                bool alongX = bb.size.x > bb.size.z;
                float halfLen = (alongX ? bb.size.x : bb.size.z) * 0.5f, halfW = (alongX ? bb.size.z : bb.size.x) * 0.5f;
                var ctr = bb.center; var ax = alongX ? Vector3.right : Vector3.forward; var cr = alongX ? Vector3.forward : Vector3.right;
                float seed = (Mathf.Abs(b.name.GetHashCode()) % 997) * 0.1f;
                float wIn = halfW - 0.35f;                                     // stays inside the posts / kerbs
                float aMax = halfLen + Approach;
                int na = Mathf.CeilToInt(2f * aMax / Cell), nl = Mathf.CeilToInt(2f * wIn / Cell);
                var v = new List<Vector3>(); var c = new List<Color32>(); var u0 = new List<Vector2>(); var u1 = new List<Vector2>(); var u2 = new List<Vector2>();
                var isDeck = new List<bool>();
                float top = bb.max.y + 2f;
                for (int ia = 0; ia <= na; ia++)
                    for (int il = 0; il <= nl; il++)
                    {
                        float a = -aMax + 2f * aMax * ia / na, l = -wIn + 2f * wIn * il / nl;
                        var p = ctr + ax * a + cr * l;
                        float y; bool deck = false; float m = 1f;
                        if (mc.Raycast(new Ray(new Vector3(p.x, top, p.z), Vector3.down), out var hit, 10f) && hit.normal.y > 0.6f && hit.point.y > GroundY(p.x, p.z) - 0.05f)
                        { y = hit.point.y; deck = true; }
                        else
                        {
                            y = GroundY(p.x, p.z);
                            if (Water(p.x, p.z)) m = 0f;
                            else if (surf != null && !city) m *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, surf.DustStrength(p.x, p.z)));   // L02: on the road only (sand on sand is invisible)
                        }
                        m *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(aMax - 1.2f, aMax, Mathf.Abs(a)));          // fades into the road / ground
                        m *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(wIn - (deck ? 0.25f : 0.9f), wIn, Mathf.Abs(l)));
                        v.Add(new Vector3(p.x, y + Lift, p.z)); isDeck.Add(deck);
                        c.Add(new Color32((byte)(deck ? 255 : 0), 0, 0, (byte)Mathf.RoundToInt(Mathf.Clamp01(m) * 255f)));
                        u0.Add(new Vector2(a, l)); u1.Add(new Vector2(2f, seed)); u2.Add(new Vector2(halfLen - 0.9f, wIn));
                    }
                var td = new List<int>(); var tg = new List<int>();
                for (int ia = 0; ia < na; ia++)
                    for (int il = 0; il < nl; il++)
                    {
                        int q = ia * (nl + 1) + il; int[] k = { q, q + 1, q + nl + 1, q + nl + 2 };
                        float lo = float.MaxValue, hi = float.MinValue; int dn = 0, zero = 0;
                        foreach (int j in k) { lo = Mathf.Min(lo, v[j].y); hi = Mathf.Max(hi, v[j].y); if (isDeck[j]) dn++; if (c[j].a == 0) zero++; }
                        if (zero == 4 || hi - lo > 0.22f) continue;                                              // never a wall down a step
                        var list = dn == 4 ? td : (dn == 0 ? tg : (hi - lo < 0.08f ? tg : null));
                        if (list == null) continue;
                        list.Add(q); list.Add(q + nl + 1); list.Add(q + 1); list.Add(q + 1); list.Add(q + nl + 1); list.Add(q + nl + 2);
                    }
                if (td.Count > 0) b.deck = MakeMesh("GroundGrime_Deck_" + b.name, v, c, u0, u1, u2, td);
                if (tg.Count > 0) b.ground = MakeMesh("GroundGrime_Approach_" + b.name, v, c, u0, u1, u2, tg);
                deckTris += td.Count / 3; approachTris += tg.Count / 3; bridgesBuilt++;
            }
            finally { Destroy(probe); }
        }

        void LateUpdate()
        {
            if (!ready || !mat) return;
            if (Time.time >= nextScan) { nextScan = Time.time + 0.5f; Scan(); }
            if (!Show) return;
            if (doorMesh) Graphics.DrawMesh(doorMesh, Matrix4x4.identity, mat, 0, null, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            foreach (var b in bridges)
            {
                if (b.ground) Graphics.DrawMesh(b.ground, Matrix4x4.identity, mat, b.layer, null, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                if (b.deck && b.plate && b.plate.gameObject.activeInHierarchy && b.plate.enabled)
                    Graphics.DrawMesh(b.deck, Matrix4x4.identity, mat, b.layer, null, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
        }

        public string Status()
        {
            var sb = new System.Text.StringBuilder($"[OpenZ] ground grime: {doorPatches} door patches, bridges {bridgesBuilt}/{bridges.Count}, deck {deckTris} tris, approach {approachTris} tris;");
            foreach (var b in bridges) sb.Append($" {b.name}={(b.deck ? (b.plate && b.plate.gameObject.activeInHierarchy ? "deck drawn" : "deck hidden") : "no deck")}{(b.ground ? "+approach" : "")}");
            return sb.ToString();
        }
    }
}
