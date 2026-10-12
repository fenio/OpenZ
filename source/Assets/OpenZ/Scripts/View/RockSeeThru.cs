using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// walking behind a wall is hidden from the 55 deg RTS camera. Every frame this finds the units whose line of sight to
    /// the camera passes through a RockZ pillar (the pillar's current stage: intact or damaged stump) and hands up to 16 of
    /// them to OpenZ/TeamLit as globals. Rock materials (<c>_SeeThru</c> = 1, set by OpenZRocks) then cut a soft dithered
    /// hole through every rock pixel that lies between the camera and such a unit, in the colour AND depth passes (so SSAO,
    /// the atmosphere and the selection rings agree). Shadows are not cut. Selected units go first, then the player's
    /// units, then the rest by distance to the screen centre; each hole fades in / out (no popping).
    /// Self-installing: created on scene load wherever a <see cref="RockPillar"/> exists. No renderer feature, no layers.
    /// </summary>
    [DefaultExecutionOrder(200)]                                   // after UnitView (60) placed the units this frame
    public class RockSeeThru : MonoBehaviour
    {
        public const int MaxUnits = 16;                            // = the _OZSeeThru[16] array in TeamLit.shader

        [Tooltip("Rock coverage kept inside the hole (0 = clean hole, 0.3 = sparse screen-door ghost of the rock)")]
        [Range(0f, 0.8f)] public float keepCoverage = 0.12f;
        [Tooltip("Soft dithered rim, as a fraction of the hole radius")]
        [Range(0.1f, 0.9f)] public float softEdge = 0.45f;
        [Tooltip("Rock closer than this (m) in front of the unit centre is not cut (the rock the unit leans on stays)")]
        public float frontMargin = 0.3f;
        [Tooltip("Surviving rock pixels in the rim get this much darker (frames the unit)")]
        [Range(0f, 0.8f)] public float edgeDarken = 0.3f;
        public float robotRadius = 1.35f, vehicleRadius = 2.3f, cannonRadius = 2.1f;
        public float fadeIn = 0.15f, fadeOut = 0.35f;
        [Tooltip("Also reveal enemy / neutral units (Z shows every unit on the map)")]
        public bool allTeams = true;

        struct Pillar { public RockPillar rp; public Vector2 xz; public float baseY, hIntact, hDamaged, radius; }
        readonly List<Pillar> pillars = new List<Pillar>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        const float Cell = 4f;
        float maxTopY = float.MinValue;
        int pillarCount = -1; float rescanAt;

        readonly Dictionary<int, float> weight = new Dictionary<int, float>();
        readonly Dictionary<int, Vector4> lastPos = new Dictionary<int, Vector4>();
        readonly List<(int id, float prio, Vector4 s)> cand = new List<(int, float, Vector4)>();
        readonly HashSet<int> wanted = new HashSet<int>();
        readonly List<int> scratch = new List<int>();
        readonly Vector4[] slots = new Vector4[MaxUnits];
        PlayerInput input; Camera cam;

        static readonly int IdUnits = Shader.PropertyToID("_OZSeeThru");
        static readonly int IdCount = Shader.PropertyToID("_OZSeeThruCount");
        static readonly int IdParams = Shader.PropertyToID("_OZSeeThruParams");

        /// <summary>Units currently handed to the shader (for checks / debug overlays).</summary>
        public int ActiveCount { get; private set; }
        public int PillarCount => pillars.Count;
        public static RockSeeThru I { get; private set; }

        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => Ensure(); }
            Ensure();
        }

        /// <summary>Adds the component when the open scene has rock pillars (idempotent).</summary>
        public static RockSeeThru Ensure()
        {
            if (I) return I;
            var e = FindFirstObjectByType<RockSeeThru>(); if (e) return e;
            if (!FindFirstObjectByType<RockPillar>()) return null;
            return new GameObject("RockSeeThru").AddComponent<RockSeeThru>();
        }

        void OnEnable() { I = this; Clear(); }
        void OnDisable() { if (I == this) I = null; Clear(); }

        void Clear()
        {
            for (int i = 0; i < MaxUnits; i++) slots[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(IdUnits, slots);                // fixes the array size (16) on first use
            Shader.SetGlobalFloat(IdCount, 0f);
            ActiveCount = 0;
        }

        void Scan()
        {
            pillars.Clear(); grid.Clear(); maxTopY = float.MinValue;
            var all = FindObjectsByType<RockPillar>(FindObjectsSortMode.None);
            foreach (var rp in all)
            {
                if (!rp) continue;
                var t = rp.transform;
                var p = new Pillar { rp = rp, xz = new Vector2(t.position.x, t.position.z), baseY = t.position.y };
                p.hIntact = Top(rp.pillar, p.baseY, out float r1);
                p.hDamaged = Top(rp.damaged, p.baseY, out float r2);
                p.radius = Mathf.Max(1f, Mathf.Max(r1, r2));
                int idx = pillars.Count; pillars.Add(p);
                long key = Key(Mathf.FloorToInt(p.xz.x / Cell), Mathf.FloorToInt(p.xz.y / Cell));
                if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                l.Add(idx);
                maxTopY = Mathf.Max(maxTopY, p.baseY + p.hIntact);
            }
            pillarCount = all.Length;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Height of a stage mesh above the pillar pivot and its horizontal half size (works on inactive stages).</summary>
        static float Top(GameObject g, float baseY, out float radius)
        {
            radius = 0f;
            if (!g) return 0f;
            var mf = g.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) return 0f;
            var b = mf.sharedMesh.bounds; var t = g.transform; var s = t.lossyScale;
            radius = Mathf.Max(b.extents.x * Mathf.Abs(s.x), b.extents.z * Mathf.Abs(s.z));
            return t.position.y + b.max.y * Mathf.Abs(s.y) - baseY;
        }

        static float StageHeight(in Pillar p)
        {
            if (!p.rp) return 0f;
            if (p.rp.pillar && p.rp.pillar.activeInHierarchy) return p.hIntact;
            if (p.rp.damaged && p.rp.damaged.activeInHierarchy) return p.hDamaged;
            return 0f;                                                              // rubble never hides a unit
        }

        /// <summary>True when the sight line from the unit centre c to the camera passes through a pillar.</summary>
        bool Occluded(Vector3 c, Vector3 camPos, float unitR)
        {
            var toCam = camPos - c;
            var h = new Vector2(toCam.x, toCam.z); float hl = h.magnitude;
            if (hl < 0.01f || toCam.y <= 0.01f) return false;
            var dirH = h / hl; float slope = toCam.y / hl;                           // rise per horizontal metre
            float sMax = Mathf.Clamp((maxTopY - c.y) / slope, 0f, 40f) + 2f;       // past this the line clears every pillar top
            var c2 = new Vector2(c.x, c.z); var e2 = c2 + dirH * sMax;
            int x0 = Mathf.FloorToInt((Mathf.Min(c2.x, e2.x) - 2f) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(c2.x, e2.x) + 2f) / Cell);
            int z0 = Mathf.FloorToInt((Mathf.Min(c2.y, e2.y) - 2f) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(c2.y, e2.y) + 2f) / Cell);
            float lateralPad = unitR * 0.55f;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (!grid.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (int i in list)
                    {
                        var p = pillars[i];
                        float hgt = StageHeight(p); if (hgt <= 0.3f) continue;
                        var d = p.xz - c2;
                        float s = Vector2.Dot(d, dirH);                                   // along the sight line (toward the camera)
                        if (s + p.radius < 0.2f) continue;                                // the pillar is behind the unit
                        float lat = Mathf.Abs(d.x * dirH.y - d.y * dirH.x);
                        if (lat > p.radius + lateralPad) continue;
                        float sNear = Mathf.Max(0f, s - p.radius);
                        if (c.y + slope * sNear < p.baseY + hgt) return true;             // the line is still below the top there
                    }
                }
            return false;
        }

        void LateUpdate()
        {
            var g = ZGame.I;
            if (!cam || !cam.isActiveAndEnabled) cam = Camera.main;
            if (!cam) { var rc = FindFirstObjectByType<RtsCamera>(); if (rc) cam = rc.GetComponent<Camera>(); }
            if (Time.unscaledTime >= rescanAt)
            {
                rescanAt = Time.unscaledTime + 3f;
                int n = FindObjectsByType<RockPillar>(FindObjectsSortMode.None).Length;
                if (n != pillarCount) Scan();
            }
            if (g == null || g.World == null || !cam || pillars.Count == 0) { if (ActiveCount != 0 || weight.Count != 0) { weight.Clear(); lastPos.Clear(); Clear(); } return; }
            if (!input) input = FindFirstObjectByType<PlayerInput>();

            var camPos = cam.transform.position;
            cand.Clear(); wanted.Clear();
            foreach (var kv in g.Views)
            {
                var v = kv.Value;
                if (!v || !v.isActiveAndEnabled || v.Dying) continue;
                var o = v.Obj;
                if (o == null || o.Destroyed) continue;
                if (!allTeams && o.Owner != g.player) continue;
                float hc, r;
                switch (o.Kind)
                {
                    case ZKind.Robot: hc = 0.85f; r = robotRadius; break;
                    case ZKind.Vehicle: hc = 1.1f; r = vehicleRadius; break;
                    case ZKind.Cannon: hc = 1.2f; r = cannonRadius; break;
                    default: continue;
                }
                var c = v.transform.position + Vector3.up * hc;
                var vp = cam.WorldToViewportPoint(c);
                if (vp.z <= 0f || vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f) continue;
                if (!Occluded(c, camPos, r)) continue;
                bool sel = input && (input.Selection.Contains(o) || (o.Leader != null && input.Selection.Contains(o.Leader)));
                float prio = (sel ? 100f : 0f) + (o.Owner == g.player ? 10f : 0f) - (Mathf.Abs(vp.x - 0.5f) + Mathf.Abs(vp.y - 0.5f));
                cand.Add((kv.Key, prio, new Vector4(c.x, c.y, c.z, r)));
            }
            cand.Sort((a, b) => b.prio.CompareTo(a.prio));
            float dt = Time.unscaledDeltaTime;
            int count = 0;
            for (int i = 0; i < cand.Count && i < MaxUnits; i++)
            {
                var (id, _, s) = cand[i];
                wanted.Add(id);
                weight.TryGetValue(id, out float w);
                w = Mathf.Min(1f, w + dt / Mathf.Max(0.01f, fadeIn));
                weight[id] = w; lastPos[id] = s;
                slots[count++] = new Vector4(s.x, s.y, s.z, s.w * Ease(w));
            }
            // units that just left cover (or lost their slot) fade out where they are
            scratch.Clear(); scratch.AddRange(weight.Keys);
            foreach (int id in scratch)
            {
                if (wanted.Contains(id)) continue;
                float w = weight[id] - dt / Mathf.Max(0.01f, fadeOut);
                if (w <= 0f || !g.Views.TryGetValue(id, out var v) || !v || v.Dying) { weight.Remove(id); lastPos.Remove(id); continue; }
                weight[id] = w;
                var s = lastPos[id];
                var c = v.transform.position; s.x = c.x; s.z = c.z; lastPos[id] = s;
                if (count < MaxUnits) slots[count++] = new Vector4(s.x, s.y, s.z, s.w * Ease(w));
            }
            for (int i = count; i < MaxUnits; i++) slots[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(IdUnits, slots);
            Shader.SetGlobalFloat(IdCount, count);
            Shader.SetGlobalVector(IdParams, new Vector4(keepCoverage, Mathf.Max(0.05f, softEdge), frontMargin, edgeDarken));
            ActiveCount = count;
        }

        static float Ease(float w) => w * w * (3f - 2f * w);

        [ContextMenu("Log state")]
        void LogState() => Debug.Log($"[OpenZ] RockSeeThru: {pillars.Count} pillars, {ActiveCount} units revealed, {weight.Count} fading");
    }
}
